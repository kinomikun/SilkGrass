// Value noise: hash(vec2) and noise(vec2).
//
// The evaluation order in hash() is load-bearing. Terrain/TerrainNoise.cs mirrors
// it on the CPU, and the two only agree while both round every step the same way.
// Far from the world origin the first fract() keeps only a few mantissa bits and
// the *17.0 and the final fract() amplify any difference roughly 900x. GLSL lets a
// driver fuse "p * k + c" into one multiply-add, C# never does; 'precise' forbids
// that fusing and any reassociation. Do not reorder the arithmetic on either side
// without making the identical change on the other.
#if __VERSION__ >= 400
#define NOISE_PRECISE precise
#else
#define NOISE_PRECISE
#endif

float hash(vec2 p) {
    NOISE_PRECISE vec2 q = fract(p * 0.3183099 + 0.1) * 17.0;
    NOISE_PRECISE float h = fract(q.x * q.y * (q.x + q.y));
    return h;
}

float noise(vec2 st) {
    vec2 i = floor(st);
    vec2 f = fract(st);
    float a = hash(i);
    float b = hash(i + vec2(1.0, 0.0));
    float c = hash(i + vec2(0.0, 1.0));
    float d = hash(i + vec2(1.0, 1.0));
    vec2 u = f * f * (3.0 - 2.0 * f);
    return mix(a, b, u.x) + (c - a) * u.y * (1.0 - u.x) + (d - b) * u.x * u.y;
}
