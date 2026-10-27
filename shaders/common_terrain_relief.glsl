// Terrain relief light: map-style hillshading for the far field.
//
// Slopes that face the sun's compass direction get brighter and warmer, slopes that
// face away get darker and cooler. A high sun barely shades gentle hills
// physically, so the term uses only the sun's AZIMUTH against a chosen
// slope-for-full-effect instead of N.L.
//
// Every far surface that shows the terrain colour takes the same term, or the hills
// would shade differently where one surface hands over to the next:
//   grass.vert    mid + far grass tiers, once per blade at the root
//   ground.frag   the far ground carpet
//   horizon.frag  the terrain past the grass horizon
// The near grass bucket has none: the fade starts beyond it (UniformBuffers.cs
// keeps it that way).
//
// Requires common_uniforms.glsl. Define RELIEF_WITH_TERRAIN before including in a
// stage that has GetTerrainHeight (common_terrain.glsl) and evaluates the slope.

// 0 near the camera, 1 in the far field. Zero strength returns 0, so callers that
// gate the slope evaluation on this pay nothing while the term is off.
float terrainReliefFade(float camDist) {
    if (ReliefParams.x <= 0.0) return 0.0;
    return smoothstep(ReliefParams.z, ReliefParams.w, camDist);
}

#ifdef RELIEF_WITH_TERRAIN
// -1 (full lee) .. +1 (fully sun-facing). h0 = terrain height at pos.
// Forward differences over 6 m: the hills are hundreds of metres across.
float terrainReliefAt(vec2 pos, float h0) {
    const float e = 6.0;
    vec2 grad = vec2(GetTerrainHeight(pos + vec2(e, 0.0)) - h0,
                     GetTerrainHeight(pos + vec2(0.0, e)) - h0) / e;
    // Terrain rising toward the sun tilts the surface AWAY from it
    return clamp(-dot(grad, ReliefSunAz) / max(ReliefParams.y, 1e-4), -1.0, 1.0);
}
#endif

// r = relief * fade. sunLit = the combined shadow term (1 = lit): relief is direct
// light, so it keeps only 30% under a shadow - which also mutes it by itself in
// overcast weather.
vec3 terrainReliefShade(vec3 color, float r, float sunLit) {
    float k = r * ReliefParams.x * mix(0.3, 1.0, sunLit);
    vec3 gain = k > 0.0 ? vec3(1.0) + k * ReliefSplit.x * vec3(0.34, 0.28, 0.08)   // sun-facing: brighter, warmer
                        : vec3(1.0) + k * ReliefSplit.y * vec3(0.34, 0.26, 0.12);  // lee: darker, cooler
    return color * max(gain, vec3(0.0));
}
