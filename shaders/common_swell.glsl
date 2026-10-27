// Grass swell: long rolling waves that lift and drop the grass surface (blades and
// the ground carpet under them) while the terrain itself stays still. Vertical only,
// so wrapping and culling are untouched.
//
// Three wave trains (a main swell with sharpened crests plus two shorter cross
// trains), each cos(dot(direction * wavenumber, pos) - phase). The phases are
// integrated on the CPU, so changing the speed never makes the sea jump.
//
// CPU mirror: Terrain/GrassSwell.cs - keep the two in sync.
// Usage: add GetSwell(xz) to a height.

layout(std140, binding = 5) uniform SwellData {
    vec4 SwellWave[3];   // xy = direction * wavenumber (rad/m), z = phase (rad), w = amplitude (m)
    vec4 SwellPrevPhase; // xyz = last frame's phases, for motion vectors
    vec4 SwellParams;    // x = crest sharpness, w = 1 when on
};

float SwellAtPhase(vec2 pos, vec3 phase) {
    // Main swell: the second harmonic sharpens crests and flattens troughs
    float c = cos(dot(SwellWave[0].xy, pos) - phase.x);
    float h = SwellWave[0].w * (c + SwellParams.x * (2.0 * c * c - 1.0));
    h += SwellWave[1].w * cos(dot(SwellWave[1].xy, pos) - phase.y);
    h += SwellWave[2].w * cos(dot(SwellWave[2].xy, pos) - phase.z);
    return h;
}

// Height of the swell at a world XZ position, in metres around zero.
float GetSwell(vec2 pos) {
    if (SwellParams.w < 0.5) return 0.0;
    return SwellAtPhase(pos, vec3(SwellWave[0].z, SwellWave[1].z, SwellWave[2].z));
}

// The same one frame ago (TAA motion vectors).
float GetSwellPrev(vec2 pos) {
    if (SwellParams.w < 0.5) return 0.0;
    return SwellAtPhase(pos, SwellPrevPhase.xyz);
}
