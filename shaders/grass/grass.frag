#version 430 core

// ============================================================================
// GRASS FRAGMENT SHADER - see grass.vert for the variants.
//
//   GRASS_DYNAMIC (near/mid): the full per-fragment colour pipeline.
//   GRASS_VS_COLOR (far/distant): colour arrives shaded from the vertex stage.
//
// The fog at the end is shared by every variant (one copy, so the bands cannot
// drift apart and show a ring where they meet).
//
// Fidelity cuts used here: GRASS_CUT_PATCHES, GRASS_CUT_MACRO, GRASS_CUT_SHEEN,
// GRASS_CUT_SPLASH, GRASS_CLOUD_PER_VERTEX.
// ============================================================================

#if defined(GRASS_FULL_PHYSICS) || defined(GRASS_TRAIL_PHYSICS)
#define GRASS_DYNAMIC
#endif

layout(location = 0) out vec4 FragColor;
#ifdef TAA_VELOCITY
layout(location = 1) out vec2 OutVelocity;  // UV-space motion
#endif

in vec3 vWorldPos;
#ifdef GRASS_DYNAMIC
in vec3 vNormal;
in float vBladeHeight;
in float vRandom;
in float vWindFactor;
in float vCrushFactor;
#ifdef GRASS_CLOUD_PER_VERTEX
in float vCloudShadow;
#endif
#ifdef GRASS_TRAIL_PHYSICS
in float vReliefK;
#endif
#else
in vec3 vColor;
#endif

#ifdef TAA_VELOCITY
in vec4 vTaaCurrClip;
in vec4 vTaaPrevClip;
#endif

#include "../common_uniforms.glsl"
#include "../common_noise.glsl"
#include "../common_shading.glsl"
#include "../common_fog.glsl"
#ifdef GRASS_TRAIL_PHYSICS
// Only the shade function: the slope came per blade from the vertex stage
#include "../common_terrain_relief.glsl"
#endif

uniform sampler2D MacroNoiseTex;

void main()
{
    // Planar camera distance, shared by the sheen's range gate and the fog
    float camDistXZ = distance(vWorldPos.xz, CameraPosition.xz);

#ifndef GRASS_DYNAMIC
    vec3 finalColor = vColor;
#else
    // 1. Cloud shadow
#ifdef GRASS_CLOUD_PER_VERTEX
    float sunLit = vCloudShadow;
#else
    float sunLit = cloudShadowAt(vWorldPos.xz);
#endif

    // 2. Patches and macro variation
    vec3 localTop = GrassTop;
#ifndef GRASS_CUT_PATCHES
    localTop = applyPatches(GrassTop, vWorldPos.xz);
#endif
#ifndef GRASS_CUT_MACRO
    localTop *= macroTint(MacroNoiseTex, vWorldPos.xz);
#endif

    // 3. Per-blade variation and the wind colour wave. The mix factor is
    // clamped: unbounded, it extrapolates into a bright sliver on swaying blades.
    vec3 effectiveTop = localTop + (vRandom * 0.1 - 0.05);
    float windMix = clamp(vWindFactor * vBladeHeight * 0.8, 0.0, 1.0);
    effectiveTop = mix(effectiveTop, WindTint, windMix);

    // 4. Root-to-tip gradient. max() guards the root: some drivers lower pow()
    // to exp(n * log(x)), which is NaN at exactly 0.
    float gradientFactor = pow(max(vBladeHeight, 1e-4), 1.4);
    vec3 albedo = mix(GrassBottom, effectiveTop, gradientFactor);
    albedo = applyShadowColor(albedo, sunLit);

    // 5. Height gradient and trample darkening
    albedo = applyHeightGradient(albedo, vWorldPos.y);
    albedo = mix(albedo, albedo * 0.4, vCrushFactor);

    // 6. Lighting
    float NdotL = max(dot(vNormal, SunDirection), 0.0);
    float lightIntensity = (NdotL * 0.5 + 0.5) * (0.7 + 0.3 * sunLit);
    vec3 finalColor = albedo * lightIntensity;

    if (sunLit > 0.2) {
        float h = vBladeHeight;
        finalColor += vec3(0.2, 0.2, 0.1) * (h * h * h) * 0.5 * sunLit;
    }

#ifndef GRASS_CUT_SHEEN
    // 7. Sheen: a broad highlight band scrolling along the wind, so light rolls
    // across the field like sun on water. It is a smooth world-space field with
    // a soft view weighting (no per-blade normal, no sharp specular peak), so it
    // cannot sparkle or crawl. Skipped beyond SheenMaxDist, fading over the last 40%.
    if (SheenStrength > 0.0 && camDistXZ < SheenMaxDist) {
        float distFade = 1.0 - smoothstep(SheenMaxDist * 0.6, SheenMaxDist, camDistXZ);
        vec2 flow = vWorldPos.xz + SheenDir * (Time * SheenSpeed * 10.0);
        float band = smoothstep(0.55, 0.95, noise(flow * SheenScale));
        vec3 V = normalize(CameraPosition - vWorldPos);
        float graze = 1.0 - max(V.y, 0.0);   // strongest toward the horizon
        graze *= graze;
        finalColor += SheenColor * (band * graze * vBladeHeight * sunLit * distFade * SheenStrength);
    }
#endif

    // 8. Rain: wet darkening and splashes on the blade tips
    if (RainEnabled > 0.5) {
        finalColor *= 1.0 - RainIntensity * WetDarkening;
#ifndef GRASS_CUT_SPLASH
        if (vBladeHeight > 0.85) {
            float splashNoise = noise(vWorldPos.xz * SplashFrequency + vec2(Time * 8.0, Time * 6.0));
            float splashDot = smoothstep(0.92, 0.95, splashNoise);
            float tipFactor = smoothstep(0.85, 1.0, vBladeHeight);
            finalColor += vec3(SplashBrightness) * splashDot * tipFactor * RainIntensity;
        }
#endif
    }

#ifdef GRASS_TRAIL_PHYSICS
    // 9. Relief light (mid band; mirrors the per-vertex copy of the far bands)
    finalColor = terrainReliefShade(finalColor, vReliefK, sunLit);
#endif
#endif // GRASS_DYNAMIC

    finalColor = applyDistanceFog(finalColor, vWorldPos, camDistXZ);
    FragColor = vec4(scrubColor(finalColor), 1.0);

#ifdef TAA_VELOCITY
    // Per-fragment divide of the interpolated unjittered clip positions.
    // NDC delta * 0.5 = UV-space motion.
    vec2 ndcCurr = vTaaCurrClip.xy / max(vTaaCurrClip.w, 1e-6);
    vec2 ndcPrev = vTaaPrevClip.xy / max(vTaaPrevClip.w, 1e-6);
    OutVelocity = (ndcCurr - ndcPrev) * 0.5;
#endif
}
