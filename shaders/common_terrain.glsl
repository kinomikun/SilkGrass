// Procedural terrain height. Requires common_uniforms.glsl (LookData).
// CPU mirror: Terrain/TerrainNoise.cs.

#include "common_noise.glsl"

float GetTerrainHeight(vec2 pos) {
    float n = noise(pos * TerrainFrequency);
    n = n * n * (3.0 - 2.0 * n);
    return n * TerrainAmplitude;
}
