#version 430 core

// ============================================================================
// GRASS VERTEX SHADER
//
// One source compiled into a program per distance band. A variant only contains
// the code its band needs - distant grass carries no physics or wind movement -
// while the colour and fog maths stay identical across bands, so there is no
// seam where one band hands over to the next.
//
//   GRASS_FULL_PHYSICS   near bucket: trample sim (with waves), push away from
//                        the trampler, micro wind flutter
//   GRASS_TRAIL_PHYSICS  mid bucket: trail-sim trample, push
//   GRASS_VS_COLOR       far + distant buckets: static blades, the whole colour
//                        pipeline per vertex; fog stays per fragment
//
// GRASS_DYNAMIC (derived) = either physics variant: wind movement, trample
// squash and TAA wind motion.
//
// Grass fidelity cuts (GrassRenderer.FidelityDefines): GRASS_CUT_PATCHES,
// GRASS_CUT_MACRO, GRASS_CUT_FLUTTER, GRASS_CLOUD_PER_VERTEX (and two that only
// the fragment stage uses).
// ============================================================================

#if defined(GRASS_FULL_PHYSICS) || defined(GRASS_TRAIL_PHYSICS)
#define GRASS_DYNAMIC
#endif

layout (location = 0) in vec3 aPos;     // unit-height blade, x = half width offset
layout (location = 1) in vec3 aNormal;

// Per-blade records from grass_cull.comp:
// x = wrapped world X, y = terrain height at the root, z = wrapped world Z,
// w = packHalf2x16(widthScale, heightScale)
layout(std430, binding = 0) readonly buffer GrassData { vec4 instances[]; };

#include "../common_uniforms.glsl"
#include "../common_noise.glsl"
#include "../common_swell.glsl"
#include "../common_shading.glsl"

// The relief light shades the mid and far bands (the near band ends before its
// fade-in starts) and needs the terrain height for the slope.
#ifndef GRASS_FULL_PHYSICS
#include "../common_terrain.glsl"
#define RELIEF_WITH_TERRAIN
#include "../common_terrain_relief.glsl"
#endif

// Pre-baked tileable noise (Grass/NoiseLuts.cs): one bilinear fetch instead of
// the analytic noise(). The period of 256 wind-scroll units never visibly tiles.
uniform sampler2D WindNoiseTex;
const float WIND_NOISE_INV_PERIOD = 1.0 / 256.0;

uniform sampler2D MacroNoiseTex;

#ifdef GRASS_DYNAMIC
// Wrap period of this band's trample texture (and of the push offset)
uniform float FieldSize;
uniform sampler2D TrailTexture;  // .g = trample

// A disc that presses the grass aside in real time (the camera, when low)
uniform vec2 TramplerPos;
uniform float TramplerRadius;
uniform float TramplerStrength;  // 0 = off
#endif

#ifdef GRASS_FULL_PHYSICS
uniform sampler2D FluidTexture;  // .r = wave depression, .g = trample

// The fluid sim wipes everything beyond 48% of its field from the sim centre
// (so trails cannot wrap around), but this band reaches a little further. Near
// that edge the trail sim, which reaches much further, fills in the trample.
uniform float TrailSimSize;
uniform float FluidClearDist;
uniform vec2 SimCenter;
#endif

out vec3 vWorldPos;
#ifdef GRASS_VS_COLOR
out vec3 vColor;          // shaded colour before fog
#else
out vec3 vNormal;
out float vBladeHeight;
out float vRandom;
out float vWindFactor;
out float vCrushFactor;
#ifdef GRASS_CLOUD_PER_VERTEX
out float vCloudShadow;
#endif
#ifdef GRASS_TRAIL_PHYSICS
out float vReliefK;       // terrain relief x distance fade at the blade root
#endif
#endif

#ifdef TAA_VELOCITY
out vec4 vTaaCurrClip;    // unjittered clip position this frame
out vec4 vTaaPrevClip;    // clip position last frame
#endif

void main()
{
    vec4 data = instances[gl_InstanceID];
    vec3 instancePos = data.xyz;
    float terrainH = data.y;
    // The root rides the swell, so everything built on instancePos moves with
    // it. terrainH stays the still ground, which the relief light measures.
    float swell = GetSwell(instancePos.xz);
    instancePos.y += swell;
    vec2 unpacked = unpackHalf2x16(floatBitsToUint(data.w));
    float widthScale = unpacked.x;
    float heightScale = unpacked.y;
    // Per-blade random from the wrapped position: stable while the blade is
    // visible (it only changes when the blade wraps to the far side of the field).
    float randomVal = hash(instancePos.xz);

    float stiffness = aPos.y * aPos.y;

    // --- TRAMPLE ---
#ifdef GRASS_DYNAMIC
    float waveHeight = 0.0;
    float crush = 0.0;

    // Push away from the trampler. instancePos is already the blade's real
    // world position (the copy nearest the field centre), so no wrap here.
    vec2 fromTrampler = instancePos.xz - TramplerPos;
    float tramplerDist = length(fromTrampler);
    vec2 pushDir = tramplerDist > 0.001 ? fromTrampler / tramplerDist : vec2(0.0);
    float activeCrush = TramplerStrength * (1.0 - smoothstep(TramplerRadius * 0.5, TramplerRadius * 1.25, tramplerDist));
#endif

#ifdef GRASS_FULL_PHYSICS
    {
        vec4 fluid = texture(FluidTexture, instancePos.xz / FieldSize + 0.5);
        waveHeight = fluid.r;
        float trample = fluid.g;

        vec2 fromCenter = instancePos.xz - SimCenter;
        fromCenter -= round(fromCenter / FieldSize) * FieldSize;
        if (length(fromCenter) > FluidClearDist - 12.0)
            trample = max(trample, texture(TrailTexture, instancePos.xz / TrailSimSize + 0.5).g);

        crush = max(trample, activeCrush);
    }
#endif

#ifdef GRASS_TRAIL_PHYSICS
    {
        float trample = texture(TrailTexture, instancePos.xz / FieldSize + 0.5).g;
        crush = max(trample, activeCrush);
    }
#endif

    // --- WIND ---
    float leanNoise = 0.8 + randomVal * 0.4;
    vec3 staticLean = vec3(BaseLeanDir.x, -0.2, BaseLeanDir.y) * BaseLeanStrength * leanNoise * stiffness;

    // WindTime is integrated on the CPU (speed x time), so speed changes never
    // make the pattern jump.
    vec2 windScroll = instancePos.xz * WindFrequency - WindDir * WindTime;
    float windA = texture(WindNoiseTex, windScroll * WIND_NOISE_INV_PERIOD).r;
    float combinedWind = windA;

#if defined(GRASS_FULL_PHYSICS) && !defined(GRASS_CUT_FLUTTER)
    // Micro flutter, near band only
    float windB = texture(WindNoiseTex, (windScroll * 3.5 + Time * 0.5) * WIND_NOISE_INV_PERIOD).r;
    combinedWind = mix(windA, windB, 0.4);
#endif

    combinedWind = combinedWind * 2.0 - 1.0;
    float windIntensity = clamp(combinedWind * 0.5 + 0.5, 0.0, 1.0);
    float windFactor = windIntensity * stiffness;

#ifdef GRASS_DYNAMIC
    // Physical sway and push. The far bands keep blades static, which prevents
    // distance aliasing; they still use windFactor for the colour wave.
    vec3 windOffset = vec3(WindDir.x, 0.0, WindDir.y) * combinedWind * WindStrength * stiffness;
    float windVertical = -abs(combinedWind) * VerticalBend * stiffness;
    vec3 windForce = vec3(windOffset.x, windVertical, windOffset.z);

    vec3 pushOffset = vec3(pushDir.x, 0.0, pushDir.y) * activeCrush * 2.0 * stiffness;
    float uprightStrength = 1.0 - smoothstep(0.0, 0.5, crush);

    vec3 environment = (staticLean + windForce) * uprightStrength + pushOffset;
    float squashFactor = 1.0 - crush * 0.5;
#else
    vec3 environment = staticLean;
    const float squashFactor = 1.0;
#endif

    // --- ASSEMBLY ---
    float distToCam = distance(instancePos, CameraPosition);

    // The far-band look (deeper root sink, floor and height jitter) fades in
    // with distance instead of switching per band: a blade crossing a dithered
    // bucket boundary keeps the same shape and does not pop.
    float farness = smoothstep(150.0, 350.0, distToCam);

#ifndef GRASS_FULL_PHYSICS
    // Relief light at the blade root, by planar camera distance - the same
    // measure the ground and horizon use, so a blade and the carpet under it
    // agree. The slope costs two more terrain evaluations, so only where the
    // fade has started.
    float reliefK = 0.0;
    float reliefFade = terrainReliefFade(distance(instancePos.xz, CameraPosition.xz));
    if (reliefFade > 0.0) reliefK = terrainReliefAt(instancePos.xz, terrainH) * reliefFade;
#endif

    // Distant blades thicken, so a pixel keeps covering grass
    float distThicken = 1.0 + clamp((distToCam - 10.0) * 0.05, 0.0, 4.0);

    float scaleX = widthScale * distThicken;
    float scaleY = heightScale * BladeHeight;
#ifdef GRASS_FULL_PHYSICS
    // Blades right at the camera shrink away instead of being sliced by the
    // near clip plane when the camera flies through the grass.
    scaleY *= smoothstep(0.35, 0.9, distance(instancePos.xz, CameraPosition.xz));
#endif
    float randomHeightOffset = 1.0 + randomVal * 0.2 * farness;

    // Billboarded across, upright along Y
    vec3 cameraRight = vec3(View[0][0], View[1][0], View[2][0]);
    vec3 vertexOffset = cameraRight * aPos.x * scaleX
                      + vec3(0.0, 1.0, 0.0) * aPos.y * scaleY * squashFactor * randomHeightOffset;

    vec3 worldPos = instancePos + vertexOffset + environment;
#ifdef GRASS_DYNAMIC
    worldPos.y += waveHeight * stiffness;
#endif

    // Never below the ground: the root sinks a little, more with distance
    float floorLimit = instancePos.y - mix(0.4, 0.48, farness) + randomVal * 0.05 * farness;
    if (worldPos.y < floorLimit) worldPos.y = floorLimit;

    gl_Position = Projection * View * vec4(worldPos, 1.0);
    vWorldPos = worldPos;

#ifdef TAA_VELOCITY
    // Every band writes velocity, the static far ones included: past the mid
    // field a pixel covers many sub-pixel blades, so the resolve's depth
    // fallback reconstructs a different distance every frame. Projecting the
    // blade's own position through both matrices is exact.
    if (TaaActive > 0.5) {
        vec3 prevWorldPos = worldPos;
#ifdef GRASS_DYNAMIC
        // The wind is a pure function of time and position: re-evaluate it at
        // last frame's phase. Trample and waves reuse this frame's values; the
        // small error is absorbed by the resolve's variance clip.
        vec2 windScrollPrev = instancePos.xz * WindFrequency - WindDir * TaaPrevWindTime;
        float windAPrev = texture(WindNoiseTex, windScrollPrev * WIND_NOISE_INV_PERIOD).r;
        float combinedWindPrev = windAPrev;
        // Must match the current-frame formula exactly, flutter cut included
#if defined(GRASS_FULL_PHYSICS) && !defined(GRASS_CUT_FLUTTER)
        float windBPrev = texture(WindNoiseTex, (windScrollPrev * 3.5 + TaaPrevTime * 0.5) * WIND_NOISE_INV_PERIOD).r;
        combinedWindPrev = mix(windAPrev, windBPrev, 0.4);
#endif
        combinedWindPrev = combinedWindPrev * 2.0 - 1.0;

        vec3 windOffsetPrev = vec3(WindDir.x, 0.0, WindDir.y) * combinedWindPrev * WindStrength * stiffness;
        float windVerticalPrev = -abs(combinedWindPrev) * VerticalBend * stiffness;
        vec3 windForcePrev = vec3(windOffsetPrev.x, windVerticalPrev, windOffsetPrev.z);

        prevWorldPos = worldPos + (windForcePrev - windForce) * uprightStrength;
#endif
        prevWorldPos.y += GetSwellPrev(instancePos.xz) - swell;
        vTaaCurrClip = TaaCurrViewProj * vec4(worldPos, 1.0);
        vTaaPrevClip = TaaPrevViewProj * vec4(prevWorldPos, 1.0);
    } else {
        vTaaCurrClip = vec4(0.0, 0.0, 0.0, 1.0);
        vTaaPrevClip = vec4(0.0, 0.0, 0.0, 1.0);
    }
#endif

#ifdef GRASS_DYNAMIC
    vBladeHeight = aPos.y;
    vRandom = randomVal;
    vWindFactor = windFactor;
    vCrushFactor = crush;
#ifdef GRASS_TRAIL_PHYSICS
    vReliefK = reliefK;
#endif
#ifdef GRASS_CLOUD_PER_VERTEX
    // Fidelity Low: cloud shadow once per blade root. The cloud field is tens
    // of metres across, so per blade matches per fragment.
    vCloudShadow = cloudShadowAt(instancePos.xz);
#endif
    vec3 leanNormal = normalize(vec3(staticLean.x, 1.0, staticLean.z));
    vNormal = mix(leanNormal, vec3(0.0, 1.0, 0.0), crush);
#endif

#ifdef GRASS_VS_COLOR
    // ========================================================================
    // PER-BLADE COLOUR (far and distant bands). The same maths as the fragment
    // path in grass.frag with the fragment inputs replaced by their vertex
    // sources; at this distance a blade covers far more fragments than
    // vertices, and every input is effectively constant across one blade.
    // ========================================================================
    float cloudShadow = cloudShadowAt(worldPos.xz);

    vec3 localTop = GrassTop;
#ifndef GRASS_CUT_PATCHES
    localTop = applyPatches(GrassTop, worldPos.xz);
#endif
#ifndef GRASS_CUT_MACRO
    localTop *= macroTint(MacroNoiseTex, worldPos.xz);
#endif

    vec3 effectiveTop = localTop + (randomVal * 0.1 - 0.05);
    // Clamped: an unbounded factor would extrapolate past the tint into a
    // bright stripe on swaying blades.
    float windMix = clamp(windFactor * aPos.y * 0.8, 0.0, 1.0);
    effectiveTop = mix(effectiveTop, WindTint, windMix);
    float gradientFactor = pow(max(aPos.y, 1e-4), 1.4);
    vec3 albedo = mix(GrassBottom, effectiveTop, gradientFactor);

    float sunLit = cloudShadow;
    albedo = applyShadowColor(albedo, sunLit);
    albedo = applyHeightGradient(albedo, worldPos.y);

    vec3 leanNormal = normalize(vec3(staticLean.x, 1.0, staticLean.z));
    float NdotL = max(dot(leanNormal, SunDirection), 0.0);
    float lightIntensity = (NdotL * 0.5 + 0.5) * (0.7 + 0.3 * sunLit);
    vec3 finalColor = albedo * lightIntensity;

    if (sunLit > 0.2) {
        float h = aPos.y;
        finalColor += vec3(0.2, 0.2, 0.1) * (h * h * h) * 0.5 * sunLit;
    }

    if (RainEnabled > 0.5)
        finalColor *= 1.0 - RainIntensity * WetDarkening;

    finalColor = terrainReliefShade(finalColor, reliefK, sunLit);
    vColor = finalColor;
#endif
}
