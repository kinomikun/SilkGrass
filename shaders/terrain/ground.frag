#version 430 core
layout(location = 0) out vec4 FragColor;

// The ground under the grass. Near the camera it is soil seen through the gaps
// between blades; from 250 m on it blends into a grass-coloured carpet (the same
// shading as the horizon terrain), so the far field reads as continuous grass.
// No velocity output: a smooth, well-covered surface is safe on the TAA depth
// fallback.

in vec3 vWorldPos;

#include "../common_uniforms.glsl"
#include "../common_noise.glsl"
#include "../common_shading.glsl"
#include "../common_fog.glsl"
#include "../common_terrain.glsl"
#define RELIEF_WITH_TERRAIN
#include "../common_terrain_relief.glsl"

uniform sampler2D FluidTexture;
uniform sampler2D TrailTexture;
uniform vec2 SimCenter;
uniform float InnerFieldSize;
uniform float MiddleFieldSize;

vec3 desaturate(vec3 color, float amount) {
    vec3 gray = vec3(dot(color, vec3(0.299, 0.587, 0.114)));
    return mix(gray, color, amount);
}

vec3 noisyNormal(vec2 pos) {
    const float eps = 0.2;
    float center = noise(pos * 3.0);
    float right  = noise((pos + vec2(eps, 0.0)) * 3.0);
    float up     = noise((pos + vec2(0.0, eps)) * 3.0);
    return normalize(vec3((center - right) * 0.8, 1.0, (center - up) * 0.8));
}

// Trample from the fine sim inside its live radius, the trail sim beyond it.
// Both are world-anchored and wrap (GL_REPEAT).
float trampleAt(vec2 posXZ) {
    vec2 d = posXZ - SimCenter;
    d -= round(d / InnerFieldSize) * InnerFieldSize;
    if (length(d) < InnerFieldSize * 0.48)
        return texture(FluidTexture, posXZ / InnerFieldSize + 0.5).g;
    return texture(TrailTexture, posXZ / MiddleFieldSize + 0.5).g;
}

// Far carpet colour: the grass palette shaded like horizon.frag
vec3 carpetColor(float sunLit) {
    vec3 terrainColor = applyPatches(GrassTop, vWorldPos.xz);

    // Blade grain
    float grain = noise(vWorldPos.xz * 6.0);
    vec3 mixedColor = mix(GrassBottom, terrainColor, 0.85);
    vec3 albedo = mix(mixedColor * 0.9, mixedColor, grain);
    albedo = applyHeightGradient(albedo, vWorldPos.y);
    albedo = applyShadowColor(albedo, sunLit);

    float NdotL = max(dot(noisyNormal(vWorldPos.xz), SunDirection), 0.0);
    float lightIntensity = (NdotL * 0.5 + 0.5) * (0.7 + 0.3 * sunLit);
    vec3 color = desaturate(albedo * lightIntensity, 0.85) * 0.95;

    float reliefFade = terrainReliefFade(distance(vWorldPos.xz, CameraPosition.xz));
    if (reliefFade > 0.0) {
        float h = GetTerrainHeight(vWorldPos.xz);
        color = terrainReliefShade(color, terrainReliefAt(vWorldPos.xz, h) * reliefFade, sunLit);
    }

    if (RainEnabled > 0.5)
        color *= 1.0 - RainIntensity * WetDarkening * 0.5;
    return color;
}

void main()
{
    float camDist = distance(vWorldPos.xz, CameraPosition.xz);
    float carpetBlend = smoothstep(250.0, 350.0, camDist);
    float sunLit = cloudShadowAt(vWorldPos.xz);

    // Near: soil, with the same shade tint and light falloff as the blades, so
    // the gaps between them never read brighter than the grass in shadow.
    vec3 nearColor = applyShadowColor(GroundColor, sunLit);
    float nearNdotL = max(dot(noisyNormal(vWorldPos.xz), SunDirection), 0.0);
    nearColor *= (nearNdotL * 0.5 + 0.5) * (0.7 + 0.3 * sunLit);

    vec3 color;
    if (carpetBlend < 0.01) color = nearColor;
    else if (carpetBlend > 0.99) color = carpetColor(sunLit);
    else color = mix(nearColor, carpetColor(sunLit), carpetBlend);

    // Trample darkening after the blend, so trails show at every distance
    color = mix(color, color * 0.4, trampleAt(vWorldPos.xz));

    color = applyDistanceFog(color, vWorldPos, camDist);
    FragColor = vec4(scrubColor(color), 1.0);
}
