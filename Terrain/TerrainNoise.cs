using System;

namespace SilkGrass.Terrain
{
    /// <summary>
    /// CPU mirror of the terrain height in shaders/common_noise.glsl and
    /// shaders/common_terrain.glsl, for code that has to agree with the
    /// rendered ground (the camera's ground clearance, the trampler).
    ///
    /// The hash's evaluation order is load-bearing and must match the GLSL
    /// step for step: far from the world origin the first Fract() keeps only a
    /// few mantissa bits, and the *17 and the closing Fract() amplify any
    /// difference roughly 900x. The shader pins its order with 'precise'; here,
    /// never rewrite a multiply-add as MathF.FusedMultiplyAdd and never
    /// reassociate the final product.
    /// </summary>
    public static class TerrainNoise
    {
        public static float Fract(float x) => x - MathF.Floor(x);

        public static float Hash(float x, float y)
        {
            float px = Fract(x * 0.3183099f + 0.1f);
            float py = Fract(y * 0.3183099f + 0.1f);
            px *= 17f;
            py *= 17f;
            return Fract(px * py * (px + py));
        }

        public static float Noise(float x, float y)
        {
            float ix = MathF.Floor(x), iy = MathF.Floor(y);
            float fx = Fract(x), fy = Fract(y);
            float a = Hash(ix, iy);
            float b = Hash(ix + 1f, iy);
            float c = Hash(ix, iy + 1f);
            float d = Hash(ix + 1f, iy + 1f);
            float ux = fx * fx * (3f - 2f * fx);
            float uy = fy * fy * (3f - 2f * fy);
            return (a * (1f - ux) + b * ux) * (1f - uy) + (c * (1f - ux) + d * ux) * uy;
        }

        /// <summary>Terrain height at a world position; matches GetTerrainHeight in the shaders.</summary>
        public static float HeightAt(float x, float z, float frequency, float amplitude)
        {
            float n = Noise(x * frequency, z * frequency);
            n = n * n * (3f - 2f * n);
            return n * amplitude;
        }
    }
}
