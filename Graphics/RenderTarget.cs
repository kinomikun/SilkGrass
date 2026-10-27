using System;
using Silk.NET.OpenGL;

namespace SilkGrass.Graphics
{
    /// <summary>
    /// Color + depth/stencil render target. Multisampled targets use a
    /// multisample color texture and a renderbuffer for depth; single-sample
    /// targets use plain textures for both, so the depth can be sampled by the
    /// post-process passes.
    /// </summary>
    public sealed class RenderTarget : IDisposable
    {
        private readonly GL _gl;
        private readonly int _samples;
        private readonly InternalFormat _colorFormat;
        private uint _fbo;
        private uint _colorTexture;
        private uint _depthTexture;
        private uint _depthRenderbuffer;

        public int Width { get; private set; }
        public int Height { get; private set; }
        public int Samples => _samples;
        public uint Handle => _fbo;
        public uint ColorTexture => _colorTexture;
        public uint DepthTexture => _depthTexture;

        public RenderTarget(GL gl, int width, int height, int samples, InternalFormat colorFormat)
        {
            _gl = gl;
            Width = width;
            Height = height;
            _samples = samples;
            _colorFormat = colorFormat;
            Create();
        }

        private unsafe void Create()
        {
            _fbo = _gl.GenFramebuffer();
            _gl.BindFramebuffer(FramebufferTarget.Framebuffer, _fbo);

            _colorTexture = _gl.GenTexture();
            if (_samples > 1)
            {
                _gl.BindTexture(TextureTarget.Texture2DMultisample, _colorTexture);
                _gl.TexImage2DMultisample(TextureTarget.Texture2DMultisample, (uint)_samples, _colorFormat,
                    (uint)Width, (uint)Height, true);
                _gl.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0,
                    TextureTarget.Texture2DMultisample, _colorTexture, 0);

                _depthRenderbuffer = _gl.GenRenderbuffer();
                _gl.BindRenderbuffer(RenderbufferTarget.Renderbuffer, _depthRenderbuffer);
                _gl.RenderbufferStorageMultisample(RenderbufferTarget.Renderbuffer, (uint)_samples,
                    InternalFormat.Depth24Stencil8, (uint)Width, (uint)Height);
                _gl.FramebufferRenderbuffer(FramebufferTarget.Framebuffer, FramebufferAttachment.DepthStencilAttachment,
                    RenderbufferTarget.Renderbuffer, _depthRenderbuffer);
            }
            else
            {
                _gl.BindTexture(TextureTarget.Texture2D, _colorTexture);
                _gl.TexImage2D(TextureTarget.Texture2D, 0, _colorFormat, (uint)Width, (uint)Height, 0,
                    PixelFormat.Rgba, PixelType.HalfFloat, null);
                SetSampling(TextureMinFilter.Linear, TextureMagFilter.Linear);
                _gl.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0,
                    TextureTarget.Texture2D, _colorTexture, 0);

                _depthTexture = _gl.GenTexture();
                _gl.BindTexture(TextureTarget.Texture2D, _depthTexture);
                _gl.TexImage2D(TextureTarget.Texture2D, 0, InternalFormat.Depth24Stencil8, (uint)Width, (uint)Height, 0,
                    PixelFormat.DepthStencil, PixelType.UnsignedInt248, null);
                SetSampling(TextureMinFilter.Nearest, TextureMagFilter.Nearest);
                _gl.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.DepthStencilAttachment,
                    TextureTarget.Texture2D, _depthTexture, 0);
            }

            if (_gl.CheckFramebufferStatus(FramebufferTarget.Framebuffer) != GLEnum.FramebufferComplete)
                throw new Exception("Framebuffer is not complete.");
            _gl.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
        }

        private void SetSampling(TextureMinFilter min, TextureMagFilter mag)
        {
            _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)min);
            _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)mag);
            _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
            _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToEdge);
        }

        /// <summary>Binds for drawing and sets the viewport to the full target.</summary>
        public void Bind()
        {
            _gl.BindFramebuffer(FramebufferTarget.Framebuffer, _fbo);
            _gl.Viewport(0, 0, (uint)Width, (uint)Height);
        }

        /// <summary>Copies (and resolves, when multisampled) color and depth into another target of the same size.</summary>
        public void BlitTo(RenderTarget destination)
        {
            _gl.BindFramebuffer(FramebufferTarget.ReadFramebuffer, _fbo);
            _gl.BindFramebuffer(FramebufferTarget.DrawFramebuffer, destination.Handle);
            _gl.BlitFramebuffer(0, 0, Width, Height, 0, 0, Width, Height,
                ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit, BlitFramebufferFilter.Nearest);
            _gl.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
        }

        public void Dispose()
        {
            _gl.DeleteFramebuffer(_fbo);
            _gl.DeleteTexture(_colorTexture);
            if (_depthTexture != 0) _gl.DeleteTexture(_depthTexture);
            if (_depthRenderbuffer != 0) _gl.DeleteRenderbuffer(_depthRenderbuffer);
        }
    }
}
