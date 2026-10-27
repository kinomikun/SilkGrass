// Shading helpers shared by the grass, ground and horizon shaders.
// Requires common_uniforms.glsl and common_noise.glsl.

// Drifting cloud shadows: 1 = lit, 0 = fully shadowed. The phase is integrated on
// the CPU, so changing the drift speed never makes the pattern jump.
float cloudShadowAt(vec2 worldXZ) {
    float n = noise(worldXZ * 0.02 + vec2(CloudShadowTime, 0.0));
    return smoothstep(CloudThreshold, CloudThreshold + CloudShadowSharpness, n);
}

// Stylized shade: shadowed albedo is tinted cool and slightly saturated instead of
// darkened toward grey. litFactor: 1 = full sun, 0 = full shade.
vec3 applyShadowColor(vec3 albedo, float litFactor) {
    float luma = dot(albedo, vec3(0.299, 0.587, 0.114));
    vec3 shadowed = max(mix(vec3(luma), albedo, 1.0 + ShadowSaturation), vec3(0.0)) * ShadowAmbientTint;
    return mix(shadowed, albedo, litFactor);
}

// Height gradient: valleys lean toward ValleyColor, hilltops toward HillColor.
// The full mix factor is clamped: GradientIntensity has no upper bound, and an
// unclamped mix() would extrapolate into bright stripes.
vec3 applyHeightGradient(vec3 albedo, float worldY) {
    float midY = (ValleyY + HillY) * 0.5;
    if (worldY < midY) {
        float f = clamp((midY - worldY) / (midY - ValleyY), 0.0, 1.0);
        return albedo * mix(vec3(1.0), ValleyColor, clamp(f * GradientIntensity, 0.0, 1.0));
    }
    float f = clamp((worldY - midY) / (HillY - midY), 0.0, 1.0);
    return albedo * mix(vec3(1.0), HillColor, clamp(f * GradientIntensity, 0.0, 1.0));
}

// Metre-scale patches of darker and lighter grass on top of the base colour.
vec3 applyPatches(vec3 topColor, vec2 worldXZ) {
    float patchNoise = noise(worldXZ * PatchNoiseFreq);
    vec3 color = topColor;
    float darkFactor = 1.0 - smoothstep(0.0, PatchDarkThreshold, patchNoise);
    color = mix(color, topColor * PatchDarkTint, darkFactor);
    float lightFactor = smoothstep(PatchLightThreshold, 1.0, patchNoise);
    color = mix(color, topColor * PatchLightTint, lightFactor);
    return color;
}

// Large low-frequency tint regions from the baked macro noise texture.
vec3 macroTint(sampler2D macroNoise, vec2 worldXZ) {
    if (MacroVarStrength <= 0.0) return vec3(1.0);
    float m = texture(macroNoise, worldXZ * MacroVarFreq).r;
    float lo = smoothstep(0.5, 0.15, m);
    float hi = smoothstep(0.5, 0.85, m);
    vec3 tint = mix(vec3(1.0), MacroVarDarkTint, lo * MacroVarStrength);
    return mix(tint, MacroVarLightTint, hi * MacroVarStrength);
}
