using System;
using Silk.NET.OpenGL;
using SilkGrass.Graphics;

namespace SilkGrass.Grass
{
    /// <summary>
    /// Trample simulation the grass reads: two toroidal RGBA16F textures,
    /// world-anchored (uv = worldXZ / fieldSize + 0.5, wrapping), updated by
    /// shaders/sim/trample.comp every frame.
    ///
    ///   FluidSim - fine, covers the inner ring: trample with neighbour
    ///              diffusion plus a short-lived wave depression (r channel).
    ///   TrailSim - coarse, covers the middle ring: trample only.
    ///
    /// Channel g is the trample amount (0..1). Trampled grass recovers over
    /// the recovery time. Everything farther than 48% of a field from the sim
    /// centre is wiped each frame, so old trails cannot wrap around the torus
    /// and reappear ahead of the camera.
    /// </summary>
    public sealed class GrassFluidSim : IDisposable
    {
        public const int Resolution = 1024;

        // Neighbour-blur rate at 1024^2. The blur spreads per texel, so it is
        // scaled by (resolution / 1024)^2 to keep the world-space spread.
        private const float TrampleDiffusionBase = 5.0f;

        private readonly GL _gl;
        private readonly ShaderProgram _shader;

        public FluidField FluidSim { get; }
        public FluidField TrailSim { get; }

        public GrassFluidSim(GL gl)
        {
            _gl = gl;
            FluidSim = new FluidField(gl, Resolution, Resolution);
            TrailSim = new FluidField(gl, Resolution, Resolution);
            _shader = new ShaderProgram(gl, "sim/trample.comp");
        }

        /// <summary>One simulation step for both fields.</summary>
        /// <param name="simCenter">World XZ the sims follow (the camera).</param>
        /// <param name="tramplerPos">World XZ of the trampling disc.</param>
        /// <param name="tramplerStrength">0 = nothing tramples this frame.</param>
        public void Step(float deltaTime, float innerFieldSize, float trailFieldSize,
            System.Numerics.Vector2 simCenter, System.Numerics.Vector2 tramplerPos, float tramplerRadius,
            float tramplerStrength, float recoveryTime)
        {
            float scale = FluidSim.Width / 1024f;
            _shader.Use();
            _shader.SetUniform("DeltaTime", deltaTime);
            _shader.SetUniform("RecoveryTime", MathF.Max(recoveryTime, 0.01f));
            _shader.SetUniform("TrampleDiffusion", TrampleDiffusionBase * scale * scale);
            _shader.SetUniform("SimCenter", simCenter);
            _shader.SetUniform("TramplerPos", tramplerPos);
            _shader.SetUniform("TramplerRadius", tramplerRadius);
            _shader.SetUniform("TramplerStrength", tramplerStrength);

            FluidSim.BindForCompute(0, 1);
            _shader.SetUniform("FieldSize", innerFieldSize);
            _shader.SetUniform("SimMode", 0);
            _gl.DispatchCompute((uint)(FluidSim.Width / 16), (uint)(FluidSim.Height / 16), 1);

            TrailSim.BindForCompute(0, 1);
            _shader.SetUniform("FieldSize", trailFieldSize);
            _shader.SetUniform("SimMode", 1);
            _gl.DispatchCompute((uint)(TrailSim.Width / 16), (uint)(TrailSim.Height / 16), 1);

            // TextureFetch: the cull pass and the grass vertex shaders sample
            // these as sampler2D later this frame. ShaderImageAccess: the next
            // step imageLoad()s what this one wrote (ping-pong).
            _gl.MemoryBarrier(MemoryBarrierMask.TextureFetchBarrierBit | MemoryBarrierMask.ShaderImageAccessBarrierBit);
            FluidSim.Swap();
            TrailSim.Swap();
        }

        public void Dispose()
        {
            FluidSim.Dispose();
            TrailSim.Dispose();
            _shader.Dispose();
        }
    }

    /// <summary>A ping-pong pair of RGBA16F textures the trample compute shader reads and writes.</summary>
    public sealed class FluidField : IDisposable
    {
        private readonly GL _gl;
        private readonly uint[] _textures = new uint[2];
        private int _read;

        public int Width { get; }
        public int Height { get; }

        /// <summary>The texture holding the latest step.</summary>
        public uint CurrentTexture => _textures[_read];

        public unsafe FluidField(GL gl, int width, int height)
        {
            _gl = gl;
            Width = width;
            Height = height;
            var empty = new float[width * height * 4];
            for (int i = 0; i < 2; i++)
            {
                _textures[i] = _gl.GenTexture();
                _gl.BindTexture(TextureTarget.Texture2D, _textures[i]);
                fixed (float* p = empty)
                {
                    // Must match layout(rgba16f) in trample.comp and the
                    // format given to BindImageTexture below.
                    _gl.TexImage2D(TextureTarget.Texture2D, 0, InternalFormat.Rgba16f,
                        (uint)width, (uint)height, 0, PixelFormat.Rgba, PixelType.Float, p);
                }
                // Repeat: the sim is toroidal, so samplers wrap like the image loads do.
                _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.Repeat);
                _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.Repeat);
                _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Linear);
                _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
            }
            _gl.BindTexture(TextureTarget.Texture2D, 0);
        }

        public void BindForCompute(uint writeBinding, uint readBinding)
        {
            _gl.BindImageTexture(writeBinding, _textures[1 - _read], 0, false, 0, BufferAccessARB.WriteOnly, InternalFormat.Rgba16f);
            _gl.BindImageTexture(readBinding, _textures[_read], 0, false, 0, BufferAccessARB.ReadOnly, InternalFormat.Rgba16f);
        }

        public void Swap() => _read = 1 - _read;

        public void Dispose()
        {
            _gl.DeleteTexture(_textures[0]);
            _gl.DeleteTexture(_textures[1]);
        }
    }
}
