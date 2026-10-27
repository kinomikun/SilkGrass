using System;
using System.Numerics;
using Silk.NET.OpenGL;
using SilkGrass.Graphics;
using SilkGrass.Settings;

namespace SilkGrass.Rendering
{
    /// <summary>
    /// Owns the HDR scene target and everything after the scene pass:
    /// MSAA resolve, TAA resolve and sharpening, bloom, and the final
    /// composite (tonemap, grade) into the window.
    ///
    /// The scene renders into an RGBA16F target at window size x render scale.
    /// Under TAA the target is single-sample and carries the RG16F velocity
    /// texture on COLOR_ATTACHMENT1; under MSAA it is multisampled.
    /// </summary>
    public sealed class PostProcess : IDisposable
    {
        private const int HaltonCount = 16;
        private static readonly float[] VelocitySentinel = { 10000f, 10000f, 0f, 0f };
        private static readonly GLEnum[] ColorAndVelocity = { GLEnum.ColorAttachment0, GLEnum.ColorAttachment1 };
        private static readonly GLEnum[] ColorOnly = { GLEnum.ColorAttachment0 };

        private readonly GL _gl;
        private readonly FullscreenQuad _quad;
        private readonly ShaderProgram _compositeShader;
        private readonly ShaderProgram _bloomThresholdShader;
        private readonly ShaderProgram _bloomBlurShader;
        private readonly ShaderProgram _taaResolveShader;
        private readonly ShaderProgram _taaSharpenShader;

        private RenderTarget _sceneTarget;    // rendered into (multisampled under MSAA)
        private RenderTarget _resolveTarget;  // single-sample copy the post passes sample
        private uint _velocityTexture;
        private int _sceneSamples;

        private uint _bloomFboA, _bloomFboB, _bloomTexA, _bloomTexB;
        private int _bloomW, _bloomH, _bloomDownscale;

        private uint _taaFboA, _taaFboB, _taaTexA, _taaTexB;
        private uint _sharpenFbo, _sharpenTex;
        private bool _taaWriteToA = true;
        private bool _taaHistoryValid;
        private bool _taaWasActive;
        private bool _sharpenActive;

        private int _haltonIndex;
        // Film grain seed: its own small counter, wrapped so it stays exact in float32.
        private int _grainFrame;

        /// <summary>
        /// This frame's TAA jitter in UV: the sample stored in the texel at uv
        /// was taken at uv + TaaJitterUV on the unjittered grid. Zero when TAA is off.
        /// </summary>
        public Vector2 TaaJitterUV { get; set; }

        /// <summary>Current clip space to previous clip space, composed camera-relative (TAA fallback reprojection).</summary>
        public Matrix4x4 CurrToPrevClip { get; set; } = Matrix4x4.Identity;

        public int RenderWidth => _sceneTarget.Width;
        public int RenderHeight => _sceneTarget.Height;

        public PostProcess(GL gl, int width, int height)
        {
            _gl = gl;
            _quad = new FullscreenQuad(gl);
            const string vert = "post/fullscreen.vert";
            _compositeShader = new ShaderProgram(gl, vert, "post/post_process.frag");
            _bloomThresholdShader = new ShaderProgram(gl, vert, "post/bloom_threshold.frag");
            _bloomBlurShader = new ShaderProgram(gl, vert, "post/bloom_blur.frag");
            _taaResolveShader = new ShaderProgram(gl, vert, "post/taa_resolve.frag");
            _taaSharpenShader = new ShaderProgram(gl, vert, "post/taa_sharpen.frag");
            CreateTargets(Math.Max(1, width), Math.Max(1, height), 1, 2, taa: false);
        }

        /// <summary>Next Halton (2, 3) jitter offset in render-target pixels, scaled.</summary>
        public Vector2 NextJitterPixels(float scale)
        {
            _haltonIndex = (_haltonIndex + 1) % HaltonCount;
            return new Vector2(Halton(_haltonIndex + 1, 2) - 0.5f, Halton(_haltonIndex + 1, 3) - 0.5f) * scale;
        }

        private static float Halton(int index, int b)
        {
            float f = 1f, r = 0f;
            for (int i = index; i > 0; i /= b)
            {
                f /= b;
                r += f * (i % b);
            }
            return r;
        }

        /// <summary>Drops the TAA history, for a camera cut.</summary>
        public void InvalidateHistory() => _taaHistoryValid = false;

        /// <summary>Scene target size for a window size and render scale.</summary>
        public static (int W, int H) TargetSize(int windowW, int windowH, float renderScale) =>
            (Math.Max(1, (int)(windowW * renderScale)), Math.Max(1, (int)(windowH * renderScale)));

        /// <summary>
        /// Brings the targets in line with the settings, binds the scene target
        /// and clears it (velocity to the sentinel). Call before the scene pass.
        /// </summary>
        public void BeginFrame(int windowW, int windowH, RenderSettings render, SceneSettings.PostProcessSettings pp)
        {
            bool taa = render.AntiAliasing == AntiAliasingMode.Taa;
            int samples = render.AntiAliasing == AntiAliasingMode.Msaa ? Math.Max(1, render.MsaaSamples) : 1;
            var (w, h) = TargetSize(windowW, windowH, render.RenderScale);
            int downscale = Math.Max(1, pp.BloomDownscale);

            if (w != _sceneTarget.Width || h != _sceneTarget.Height || samples != _sceneSamples
                || taa != (_velocityTexture != 0) || downscale != _bloomDownscale)
            {
                DestroyTargets();
                CreateTargets(w, h, samples, downscale, taa);
            }

            if (taa != _taaWasActive)
            {
                _taaWasActive = taa;
                _taaHistoryValid = false;
            }

            if (samples > 1) _gl.Enable(EnableCap.Multisample);
            else _gl.Disable(EnableCap.Multisample);

            _sceneTarget.Bind();
            _gl.ClearColor(0.5f, 0.7f, 1.0f, 1.0f);
            _gl.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit | ClearBufferMask.StencilBufferBit);

            // Velocity is only mapped while the loaded programs write it
            TaaVelocity.Active = taa && _velocityTexture != 0 && TaaVelocity.ShadersCompiled;
            if (TaaVelocity.Active)
            {
                _gl.DrawBuffers(2, ColorAndVelocity);
                _gl.ClearBuffer(GLEnum.Color, 1, VelocitySentinel);
                _gl.DrawBuffers(1, ColorOnly);
            }
        }

        /// <summary>Resolves, post-processes and composites the scene into the window.</summary>
        public void EndFrame(int windowW, int windowH, RenderSettings render, SceneSettings.PostProcessSettings pp)
        {
            bool taa = render.AntiAliasing == AntiAliasingMode.Taa;
            TaaVelocity.Active = false;

            _gl.Disable(EnableCap.DepthTest);
            _gl.Disable(EnableCap.Blend);
            _sceneTarget.BlitTo(_resolveTarget);

            if (taa) ResolveTaa(pp);
            Sharpen(taa, pp);
            Bloom(pp);
            Composite(windowW, windowH, render, pp);

            if (taa)
            {
                _taaWriteToA = !_taaWriteToA;
                _taaHistoryValid = true;
            }
            _gl.Enable(EnableCap.DepthTest);
        }

        private uint TaaOutputTexture => _taaWriteToA ? _taaTexA : _taaTexB;

        private uint SceneSource(bool taa) =>
            _sharpenActive ? _sharpenTex : taa ? TaaOutputTexture : _resolveTarget.ColorTexture;

        private void ResolveTaa(SceneSettings.PostProcessSettings pp)
        {
            uint writeFbo = _taaWriteToA ? _taaFboA : _taaFboB;
            uint historyTex = _taaWriteToA ? _taaTexB : _taaTexA;
            int w = _resolveTarget.Width, h = _resolveTarget.Height;

            _gl.BindFramebuffer(FramebufferTarget.Framebuffer, writeFbo);
            _gl.Viewport(0, 0, (uint)w, (uint)h);

            _taaResolveShader.Use();
            BindTexture(0, _resolveTarget.ColorTexture);
            BindTexture(1, historyTex);
            BindTexture(2, _resolveTarget.DepthTexture);
            BindTexture(3, _velocityTexture);
            _taaResolveShader.SetUniform("ColorTexture", 0);
            _taaResolveShader.SetUniform("HistoryTexture", 1);
            _taaResolveShader.SetUniform("DepthTexture", 2);
            _taaResolveShader.SetUniform("VelocityTexture", 3);
            _taaResolveShader.SetUniform("CurrToPrevClip", CurrToPrevClip);
            _taaResolveShader.SetUniform("Resolution", new Vector2(w, h));
            _taaResolveShader.SetUniform("JitterUV", TaaJitterUV);
            // Full weight on the current frame until there is a history
            _taaResolveShader.SetUniform("BlendFactor", _taaHistoryValid ? pp.TaaBlendFactor : 1f);
            _taaResolveShader.SetUniform("VarianceGamma", pp.TaaVarianceGamma);
            _taaResolveShader.SetUniform("DebugView", pp.TaaDebugView);
            _quad.Render();
        }

        private void Sharpen(bool taa, SceneSettings.PostProcessSettings pp)
        {
            // Counteracts TAA's softness; MSAA and no-AA stay unsharpened
            _sharpenActive = taa && pp.TaaSharpen > 0.001f;
            if (!_sharpenActive) return;

            int w = _resolveTarget.Width, h = _resolveTarget.Height;
            _gl.BindFramebuffer(FramebufferTarget.Framebuffer, _sharpenFbo);
            _gl.Viewport(0, 0, (uint)w, (uint)h);
            _taaSharpenShader.Use();
            BindTexture(0, TaaOutputTexture);
            _taaSharpenShader.SetUniform("ColorTexture", 0);
            _taaSharpenShader.SetUniform("Resolution", new Vector2(w, h));
            _taaSharpenShader.SetUniform("SharpenStrength", pp.TaaSharpen);
            _quad.Render();
        }

        private void Bloom(SceneSettings.PostProcessSettings pp)
        {
            _gl.BindFramebuffer(FramebufferTarget.Framebuffer, _bloomFboA);
            _gl.Viewport(0, 0, (uint)_bloomW, (uint)_bloomH);
            _bloomThresholdShader.Use();
            BindTexture(0, SceneSource(_velocityTexture != 0));
            _bloomThresholdShader.SetUniform("ScreenTexture", 0);
            _bloomThresholdShader.SetUniform("BloomThreshold", pp.BloomThreshold);
            _quad.Render();

            // Separable Gaussian, ping-ponging A -> B -> A; ends in A
            _bloomBlurShader.Use();
            _bloomBlurShader.SetUniform("SourceTexture", 0);
            _bloomBlurShader.SetUniform("Radius", pp.BloomRadius);
            int passes = Math.Max(1, pp.BloomPasses);
            for (int i = 0; i < passes; i++)
            {
                _gl.BindFramebuffer(FramebufferTarget.Framebuffer, _bloomFboB);
                BindTexture(0, _bloomTexA);
                _bloomBlurShader.SetUniform("Direction", new Vector2(1f / _bloomW, 0f));
                _quad.Render();

                _gl.BindFramebuffer(FramebufferTarget.Framebuffer, _bloomFboA);
                BindTexture(0, _bloomTexB);
                _bloomBlurShader.SetUniform("Direction", new Vector2(0f, 1f / _bloomH));
                _quad.Render();
            }
        }

        private void Composite(int windowW, int windowH, RenderSettings render, SceneSettings.PostProcessSettings pp)
        {
            _gl.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
            _gl.Viewport(0, 0, (uint)windowW, (uint)windowH);

            _compositeShader.Use();
            BindTexture(0, SceneSource(_velocityTexture != 0));
            BindTexture(1, _resolveTarget.DepthTexture);
            BindTexture(2, _bloomTexA);
            _compositeShader.SetUniform("ScreenTexture", 0);
            _compositeShader.SetUniform("DepthTexture", 1);
            _compositeShader.SetUniform("BloomTexture", 2);
            _compositeShader.SetUniform("ZNear", render.NearClip);
            _compositeShader.SetUniform("ZFar", render.FarClip);
            _compositeShader.SetUniform("BlurStart", pp.BlurStart);
            _compositeShader.SetUniform("BlurFade", pp.BlurFade);
            _compositeShader.SetUniform("MaxBlurSize", pp.MaxBlurSize);
            _compositeShader.SetUniform("BloomStrength", pp.BloomStrength);
            _compositeShader.SetUniform("FilmGrainStrength", pp.FilmGrainStrength);
            _grainFrame = (_grainFrame + 1) % 4096;
            _compositeShader.SetUniform("GrainSeed", (float)_grainFrame);
            _compositeShader.SetUniform("Saturation", pp.Saturation);
            _compositeShader.SetUniform("Brightness", pp.Brightness);
            _compositeShader.SetUniform("Contrast", pp.Contrast);
            _compositeShader.SetUniform("VignetteStrength", pp.VignetteStrength);
            _compositeShader.SetUniform("VignetteRadius", pp.VignetteRadius);
            _compositeShader.SetUniform("TonemapMode", pp.TonemapMode);
            _compositeShader.SetUniform("TonemapExposure", pp.TonemapExposure);
            _compositeShader.SetUniform("ShadowTint", pp.ShadowTint);
            _compositeShader.SetUniform("ShadowTintStrength", pp.ShadowTintStrength);
            _compositeShader.SetUniform("MidtoneTint", pp.MidtoneTint);
            _compositeShader.SetUniform("MidtoneTintStrength", pp.MidtoneTintStrength);
            _compositeShader.SetUniform("HighlightTint", pp.HighlightTint);
            _compositeShader.SetUniform("HighlightTintStrength", pp.HighlightTintStrength);
            _quad.Render();
            _gl.ActiveTexture(TextureUnit.Texture0);
        }

        private void BindTexture(int unit, uint texture)
        {
            _gl.ActiveTexture(TextureUnit.Texture0 + unit);
            _gl.BindTexture(TextureTarget.Texture2D, texture);
        }

        private unsafe void CreateTargets(int w, int h, int samples, int bloomDownscale, bool taa)
        {
            _sceneSamples = samples;
            _sceneTarget = new RenderTarget(_gl, w, h, samples, InternalFormat.Rgba16f);
            _resolveTarget = new RenderTarget(_gl, w, h, 1, InternalFormat.Rgba16f);

            _bloomDownscale = bloomDownscale;
            _bloomW = Math.Max(1, w / bloomDownscale);
            _bloomH = Math.Max(1, h / bloomDownscale);
            (_bloomTexA, _bloomFboA) = CreateColorTarget(_bloomW, _bloomH);
            (_bloomTexB, _bloomFboB) = CreateColorTarget(_bloomW, _bloomH);

            (_taaTexA, _taaFboA) = CreateColorTarget(w, h);
            (_taaTexB, _taaFboB) = CreateColorTarget(w, h);
            (_sharpenTex, _sharpenFbo) = CreateColorTarget(w, h);
            _taaHistoryValid = false;
            _taaWriteToA = true;

            // The velocity attachment exists only under TAA (single-sample target)
            if (taa)
            {
                _velocityTexture = _gl.GenTexture();
                _gl.BindTexture(TextureTarget.Texture2D, _velocityTexture);
                _gl.TexImage2D(TextureTarget.Texture2D, 0, InternalFormat.RG16f, (uint)w, (uint)h, 0,
                    PixelFormat.RG, PixelType.HalfFloat, null);
                // Nearest: the resolve fetches exact texels
                _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Nearest);
                _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Nearest);
                _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
                _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToEdge);
                _gl.BindFramebuffer(FramebufferTarget.Framebuffer, _sceneTarget.Handle);
                _gl.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment1,
                    TextureTarget.Texture2D, _velocityTexture, 0);
                _gl.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
            }
        }

        private unsafe (uint Texture, uint Fbo) CreateColorTarget(int w, int h)
        {
            uint tex = _gl.GenTexture();
            _gl.BindTexture(TextureTarget.Texture2D, tex);
            _gl.TexImage2D(TextureTarget.Texture2D, 0, InternalFormat.Rgba16f, (uint)w, (uint)h, 0,
                PixelFormat.Rgba, PixelType.HalfFloat, null);
            _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Linear);
            _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
            _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
            _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToEdge);

            uint fbo = _gl.GenFramebuffer();
            _gl.BindFramebuffer(FramebufferTarget.Framebuffer, fbo);
            _gl.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0,
                TextureTarget.Texture2D, tex, 0);
            _gl.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
            return (tex, fbo);
        }

        private void DestroyTargets()
        {
            _sceneTarget?.Dispose();
            _resolveTarget?.Dispose();
            if (_velocityTexture != 0) { _gl.DeleteTexture(_velocityTexture); _velocityTexture = 0; }
            foreach (var fbo in new[] { _bloomFboA, _bloomFboB, _taaFboA, _taaFboB, _sharpenFbo })
                if (fbo != 0) _gl.DeleteFramebuffer(fbo);
            foreach (var tex in new[] { _bloomTexA, _bloomTexB, _taaTexA, _taaTexB, _sharpenTex })
                if (tex != 0) _gl.DeleteTexture(tex);
        }

        public void Dispose()
        {
            DestroyTargets();
            _quad.Dispose();
            _compositeShader.Dispose();
            _bloomThresholdShader.Dispose();
            _bloomBlurShader.Dispose();
            _taaResolveShader.Dispose();
            _taaSharpenShader.Dispose();
        }
    }
}
