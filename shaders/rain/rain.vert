#version 430 core
layout (location = 0) in vec3 aPos;          // unit quad corner
layout (location = 1) in vec4 aInstanceData; // xyz = start in [0,1]^3, w = per-drop random

// Rain streaks falling through a box that follows the camera. Every drop's
// position is a closed-form function of time, so there is no CPU simulation.

#include "../common_uniforms.glsl"
#include "../common_terrain.glsl"

uniform vec3 BoxSize;
uniform float RainSpeed;

out vec2 vUV;
out float vDistToGround;
out vec3 vWorldPos;

void main()
{
    vec3 start01 = aInstanceData.xyz;
    float random01 = aInstanceData.w;

    // Lighter rain draws fewer drops
    if (random01 > RainIntensity) {
        gl_Position = vec4(0.0);
        return;
    }

    float fallSpeed = RainSpeed + random01 * 10.0;
    float windDrift = WindSpeed * 2.0;
    vec3 velocity = vec3(WindDir.x * windDrift, -fallSpeed, WindDir.y * windDrift);

    // Wrap into the box around the camera
    vec3 relative = start01 * BoxSize + velocity * Time - CameraPosition;
    vec3 worldPos = CameraPosition + mod(relative, BoxSize) - BoxSize * 0.5;

    vDistToGround = worldPos.y - GetTerrainHeight(worldPos.xz);
    if (vDistToGround < 0.0) {
        gl_Position = vec4(0.0);
        return;
    }

    // Thin streak along the fall direction, facing the camera across
    vec3 camRight = vec3(View[0][0], View[1][0], View[2][0]);
    vec3 streakUp = -normalize(velocity);
    float streakWidth = 0.015;
    float streakLength = 0.4 + random01 * 0.3;
    vec3 finalPos = worldPos + camRight * aPos.x * streakWidth + streakUp * aPos.y * streakLength;

    gl_Position = Projection * View * vec4(finalPos, 1.0);
    vUV = aPos.xy * 0.5 + 0.5;
    vWorldPos = worldPos;
}
