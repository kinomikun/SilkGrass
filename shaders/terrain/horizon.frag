#version 430 core
layout(location = 0) out vec4 FragColor;
#ifdef TAA_VELOCITY
layout(location = 1) out vec2 OutVelocity;
in vec4 vTaaCurrClip;
in vec4 vTaaPrevClip;
#endif

// The terrain carpet from the end of the grass out to the horizon, shaded to
// continue the grass: same patches, macro variation, height gradient, cloud
// shadows, relief light and fog.

in vec3 vWorldPos;

#include "../common_uniforms.glsl"
#include "../common_noise.glsl"
#include "../common_shading.glsl"
#include "../common_fog.glsl"
#include "../common_terrain.glsl"
#define RELIEF_WITH_TERRAIN
#include "../common_terrain_relief.glsl"

uniform sampler2D MacroNoiseTex;
uniform float OuterRadius;   // edge of the carpet

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

void main()
{
#ifdef TAA_VELOCITY
    vec2 ndcCurr = vTaaCurrClip.xy / max(vTaaCurrClip.w, 1e-6);
    vec2 ndcPrev = vTaaPrevClip.xy / max(vTaaPrevClip.w, 1e-6);
    OutVelocity = (ndcCurr - ndcPrev) * 0.5;
#endif

    vec3 terrainColor = applyPatches(HorizonTopColor, vWorldPos.xz);
    terrainColor *= macroTint(MacroNoiseTex, vWorldPos.xz);

    // Blade grain
    float grain = noise(vWorldPos.xz * 6.0);
    vec3 mixedColor = mix(HorizonBottomColor, terrainColor, 0.85);
    vec3 albedo = mix(mixedColor * 0.9, mixedColor, grain);
    albedo = applyHeightGradient(albedo, vWorldPos.y);

    float sunLit = cloudShadowAt(vWorldPos.xz);
    albedo = applyShadowColor(albedo, sunLit);

    float NdotL = max(dot(noisyNormal(vWorldPos.xz), SunDirection), 0.0);
    float lightIntensity = (NdotL * 0.5 + 0.5) * (0.7 + 0.3 * sunLit);
    vec3 color = desaturate(albedo * lightIntensity, 0.85) * 0.95;

    float camDist = distance(vWorldPos.xz, CameraPosition.xz);
    float reliefFade = terrainReliefFade(camDist);
    if (reliefFade > 0.0) {
        float h = GetTerrainHeight(vWorldPos.xz);
        color = terrainReliefShade(color, terrainReliefAt(vWorldPos.xz, h) * reliefFade, sunLit);
    }

    if (RainEnabled > 0.5)
        color *= 1.0 - RainIntensity * WetDarkening * 0.5;

    color = applyDistanceFog(color, vWorldPos, camDist);

    // Melt the outer edge into the fog colour, which is also the sky's colour
    // at the horizon, so the end of the world never shows as a line.
    float edge = smoothstep(OuterRadius * 0.75, OuterRadius * 0.98, distance(vWorldPos.xz, CenterPosition.xz));
    color = mix(color, AtmosphereColor, edge);

    FragColor = vec4(scrubColor(color), 1.0);
}
