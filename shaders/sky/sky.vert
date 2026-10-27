#version 430 core
layout (location = 0) in vec3 aPos;

#include "../common_uniforms.glsl"

out vec3 vDirection;

void main()
{
    vDirection = aPos;
    // Rotation only, so the sky stays at infinity. Scaled well beyond the near
    // plane so no face gets clipped; the fragment shader normalizes the
    // direction, so the scale does not show.
    gl_Position = Projection * mat4(mat3(View)) * vec4(aPos * 10.0, 1.0);
}
