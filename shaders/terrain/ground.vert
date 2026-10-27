#version 430 core
layout (location = 0) in vec3 aPos;

#include "../common_uniforms.glsl"
#include "../common_terrain.glsl"
#include "../common_swell.glsl"

uniform mat4 Model;   // scales the [-1, 1] grid and centres it under the camera

out vec3 vWorldPos;

void main()
{
    vec4 worldPos = Model * vec4(aPos, 1.0);

    // Snap XZ to a world grid, so the sampled terrain does not swim as the
    // grid follows the camera.
    const float gridSnap = 12.0;
    worldPos.xz = round(worldPos.xz / gridSnap) * gridSnap;

    // The carpet rides the swell with the blades rooted in it
    worldPos.y += GetTerrainHeight(worldPos.xz) + GetSwell(worldPos.xz);
    vWorldPos = worldPos.xyz;
    gl_Position = Projection * View * worldPos;
}
