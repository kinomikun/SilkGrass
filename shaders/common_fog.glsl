// Distance + height fog with a warm scatter toward the sun.
// Requires common_uniforms.glsl and common_noise.glsl.
//
// Every surface fogs by its PLANAR distance from the camera with this one function
// (the grass, the ground and the horizon carpet), so they always agree where one
// hands over to the next.

vec3 applyDistanceFog(vec3 color, vec3 worldPos, float camDistXZ) {
    float rawFogDist = camDistXZ - FogStart;
    if (rawFogDist <= 0.0) return color;

    float heightFactor = exp(-worldPos.y * FogHeightFalloff);

    // Animated density variation, blended in over 30..70 m past the fog start
    // rather than switched on at once (a hard switch shows as a ring in heavy fog).
    float densityVar = 1.0;
    float noiseBlend = smoothstep(30.0, 70.0, rawFogDist);
    if (noiseBlend > 0.0) {
        const vec2 fogWind = vec2(0.89442719, 0.44721360);
        const float fogSpeed = 18.0;
        float n = noise(worldPos.xz * 0.005 + (Time * fogSpeed * 0.01 * fogWind));
        densityVar = mix(1.0, 0.7 + n * 0.6, noiseBlend);
    }

    // x*x rather than pow(x, 2.0): some drivers lower pow() to exp(2*log(x)),
    // which is NaN at 0.
    float fogAmount = rawFogDist * FogDensity * densityVar * heightFactor;
    float fogFactor = clamp(1.0 - exp(-fogAmount * fogAmount), 0.0, FogMaxOpacity);

    vec3 viewDir = normalize(worldPos - CameraPosition);
    float sunDot = max(dot(viewDir, SunDirection), 0.0);
    vec3 sunScatter = vec3(1.0, 0.9, 0.7) * pow(max(sunDot, 1e-4), 8.0) * 0.5;

    return mix(color, AtmosphereColor + sunScatter, fogFactor);
}

// NaN/Inf to 0 and clamp to [0, 1] before writing to the HDR target. A NaN would
// survive into the bloom and show as a black tile after the tonemap. The select
// matters: mix(x, y, a) is x*(1-a) + y*a, which passes a NaN straight through.
vec3 scrubColor(vec3 c) {
    bool bad = any(isnan(c)) || any(isinf(c));
    return clamp(bad ? vec3(0.0) : c, vec3(0.0), vec3(1.0));
}
