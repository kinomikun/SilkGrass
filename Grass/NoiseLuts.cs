using System;
using Silk.NET.OpenGL;

namespace SilkGrass.Grass
{
    /// <summary>
    /// Pre-baked tileable value noise for the grass wind sway: one bilinear
    /// texture fetch per vertex instead of the analytic noise() (~30 ALU).
    ///
    /// The texture stores the same noise the shaders' noise() computes,
    /// evaluated at 4 texels per noise cell, so bilinear filtering closely
    /// reproduces the smooth analytic field. 256 cells per side give a period
    /// of 256 in wind-scroll units, far longer than any visible stretch, so the
    /// tiling never shows. Generated once at startup.
    /// </summary>
    public sealed class WindNoiseLut : IDisposable
    {
        private const int TexSize = 1024;
        private const int CellsPerSide = 256;

        /// <summary>Period of the texture in wind-scroll units (WIND_NOISE_INV_PERIOD in grass.vert).</summary>
        public const float Period = CellsPerSide;

        private readonly GL _gl;
        private uint _texture;

        public WindNoiseLut(GL gl)
        {
            _gl = gl;
            Generate();
        }

        private unsafe void Generate()
        {
            var data = new byte[TexSize * TexSize];
            const float UvToCells = (float)CellsPerSide / TexSize;
            for (int y = 0; y < TexSize; y++)
            for (int x = 0; x < TexSize; x++)
            {
                float n = NoiseBake.TileableNoise(x * UvToCells, y * UvToCells, CellsPerSide);
                data[y * TexSize + x] = NoiseBake.ToByte(n);
            }
            _texture = NoiseBake.CreateR8Texture(_gl, data, TexSize);
        }

        public void Bind(int unit)
        {
            _gl.ActiveTexture(TextureUnit.Texture0 + unit);
            _gl.BindTexture(TextureTarget.Texture2D, _texture);
        }

        public void Dispose() => _gl.DeleteTexture(_texture);
    }

    /// <summary>
    /// Pre-baked tileable multi-octave noise for the grass "macro variation":
    /// large, low-frequency regions of slightly different tint across the
    /// field. One fetch replaces the two analytic noise() octaves the effect
    /// would cost per fragment on the high-overdraw far tiers. Only the noise
    /// field is baked; tint, strength and frequency stay live uniforms.
    /// </summary>
    public sealed class MacroNoiseLut : IDisposable
    {
        private const int TexSize = 1024;
        private const int BaseCells = 4; // cells of the base octave across one tile
        private const int Octaves = 4;   // 4, 8, 16, 32 cells

        private readonly GL _gl;
        private uint _texture;

        public MacroNoiseLut(GL gl)
        {
            _gl = gl;
            Generate();
        }

        private void Generate()
        {
            var data = new byte[TexSize * TexSize];
            for (int y = 0; y < TexSize; y++)
            {
                float v = (float)y / TexSize;
                for (int x = 0; x < TexSize; x++)
                    data[y * TexSize + x] = NoiseBake.ToByte(Fbm((float)x / TexSize, v));
            }
            _texture = NoiseBake.CreateR8Texture(_gl, data, TexSize);
        }

        private static float Fbm(float u, float v)
        {
            float sum = 0f, amp = 0.5f, norm = 0f;
            int cells = BaseCells;
            for (int o = 0; o < Octaves; o++)
            {
                sum += amp * NoiseBake.TileableNoise(u * cells, v * cells, cells);
                norm += amp;
                amp *= 0.5f;
                cells *= 2;
            }
            return sum / norm;
        }

        public void Bind(int unit)
        {
            _gl.ActiveTexture(TextureUnit.Texture0 + unit);
            _gl.BindTexture(TextureTarget.Texture2D, _texture);
        }

        public void Dispose() => _gl.DeleteTexture(_texture);
    }

    internal static class NoiseBake
    {
        /// <summary>
        /// The shaders' value noise with integer cell indices wrapped modulo
        /// period, so the result tiles seamlessly under GL_REPEAT.
        /// </summary>
        public static float TileableNoise(float sx, float sy, int period)
        {
            float ix = MathF.Floor(sx), iy = MathF.Floor(sy);
            float fx = sx - ix, fy = sy - iy;
            int ix0 = Mod((int)ix, period), iy0 = Mod((int)iy, period);
            int ix1 = (ix0 + 1) % period, iy1 = (iy0 + 1) % period;
            float a = Hash(ix0, iy0), b = Hash(ix1, iy0), c = Hash(ix0, iy1), d = Hash(ix1, iy1);
            float ux = fx * fx * (3f - 2f * fx), uy = fy * fy * (3f - 2f * fy);
            return Mix(Mix(a, b, ux), Mix(c, d, ux), uy);
        }

        // Same as hash() in common_noise.glsl
        private static float Hash(float px, float py)
        {
            px = Frac(px * 0.3183099f + 0.1f);
            py = Frac(py * 0.3183099f + 0.1f);
            px *= 17f;
            py *= 17f;
            return Frac(px * py * (px + py));
        }

        public static byte ToByte(float v)
        {
            int i = (int)(v * 255f + 0.5f);
            return (byte)(i < 0 ? 0 : i > 255 ? 255 : i);
        }

        public static unsafe uint CreateR8Texture(GL gl, byte[] data, int size)
        {
            uint tex = gl.GenTexture();
            gl.BindTexture(TextureTarget.Texture2D, tex);
            gl.PixelStore(PixelStoreParameter.UnpackAlignment, 1);
            fixed (byte* p = data)
                gl.TexImage2D(TextureTarget.Texture2D, 0, InternalFormat.R8, (uint)size, (uint)size, 0,
                    PixelFormat.Red, PixelType.UnsignedByte, p);
            gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Linear);
            gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
            gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.Repeat);
            gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.Repeat);
            gl.BindTexture(TextureTarget.Texture2D, 0);
            return tex;
        }

        private static float Frac(float v) => v - MathF.Floor(v);
        private static float Mix(float a, float b, float t) => a + (b - a) * t;
        private static int Mod(int a, int m) { int r = a % m; return r < 0 ? r + m : r; }
    }
}
