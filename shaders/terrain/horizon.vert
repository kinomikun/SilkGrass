#version 430 core
layout (location = 0) in vec3 aPos;

#include "../common_uniforms.glsl"
#include "../common_terrain.glsl"

// Lift above the terrain, so the carpet sits at the height of the grass tops it
// continues.
const float CarpetLift = 2.5;

out vec3 vWorldPos;

#ifdef TAA_VELOCITY
out vec4 vTaaCurrClip;
out vec4 vTaaPrevClip;
#endif

void main()
{
    // The ring follows the field centre, but each vertex lands on a real world
    // position of static terrain.
    vec3 pos = aPos + vec3(CenterPosition.x, 0.0, CenterPosition.z);
    pos.y = GetTerrainHeight(pos.xz) + CarpetLift;
    vWorldPos = pos;

#ifdef TAA_VELOCITY
    // Static terrain: the motion vector is the camera's alone, and projecting
    // the point through both frames' matrices gives it exactly. (The vertices
    // slide with the ring, but the surface point they sample does not move.)
    if (TaaActive > 0.5) {
        vTaaCurrClip = TaaCurrViewProj * vec4(pos, 1.0);
        vTaaPrevClip = TaaPrevViewProj * vec4(pos, 1.0);
    } else {
        vTaaCurrClip = vec4(0.0, 0.0, 0.0, 1.0);
        vTaaPrevClip = vec4(0.0, 0.0, 0.0, 1.0);
    }
#endif

    gl_Position = Projection * View * vec4(pos, 1.0);
}
