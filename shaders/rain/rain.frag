#version 430 core
out vec4 FragColor;

in vec2 vUV;
in float vDistToGround;
in vec3 vWorldPos;

#include "../common_uniforms.glsl"

uniform vec3 RainColor;
uniform float RainOpacity;

void main()
{
    // Streak profile: soft ends, thin across
    float alpha = smoothstep(0.0, 0.15, vUV.y) * smoothstep(1.0, 0.85, vUV.y);
    alpha *= smoothstep(1.0, 0.3, abs(vUV.x - 0.5) * 2.0);
    alpha *= smoothstep(0.0, 3.0, vDistToGround);

    // Drops fade toward the fog colour with distance
    float dist = distance(vWorldPos.xz, CameraPosition.xz);
    float fogAmount = max(dist - FogStart, 0.0) * FogDensity * 3.0;
    float fogFactor = clamp(1.0 - exp(-fogAmount * fogAmount), 0.0, FogMaxOpacity);
    vec3 color = mix(RainColor, AtmosphereColor, fogFactor * 0.5);

    alpha *= RainOpacity;
    if (alpha < 0.01) discard;
    FragColor = vec4(color, alpha);
}
