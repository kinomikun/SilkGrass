// Procedural storm sky: a dark two-layer cloud deck. Included by sky.frag, which
// cross-fades it over the clear sky by StormAmount. Declares its own uniforms.
//
// The look: a lighter grey-blue high overcast behind a ragged dark main deck,
// plus a lower, darker, faster scud layer in front. Depth comes from three cheap
// tricks:
//   1. Planar projection (uv = dir.xz / dir.y * k): overhead clouds are big and
//      the pattern compresses toward the horizon into natural banding.
//   2. Two layers on different virtual heights and scroll speeds = parallax.
//   3. Offset-sample self-shading: resample the deck shifted along the wind;
//      where the shifted side is denser this fragment reads as an underside and
//      darkens. Fake volume for one coarse extra FBM.
// Requires common_uniforms.glsl (WindDir, WindTime, CameraPosition).

uniform float StormCoverage;    // 0..1 how much of the sky the deck covers
uniform float StormScale;       // pattern scale (smaller = larger masses)
uniform float StormSpeed;       // scroll speed on top of the wind phase
// Elevation below which the deck melts into the horizon haze. 1 = default;
// raise it to lift the dark masses up the dome and leave a band of pale storm
// light along the horizon.
uniform float StormHeight;
uniform vec3  StormDarkColor;   // dense masses, shaded undersides
uniform vec3  StormLightColor;  // lit breaks between masses

// Distant rain curtains (virga) hanging out of the deck into the haze band
uniform float StormVirgaAmount;  // 0 = off
uniform float StormVirgaDensity; // how many shafts stand around the horizon
uniform float StormVirgaSpeed;   // drift rate, independent of StormSpeed

// Haze band: a pale lift just above the horizon and a warm-to-cool hue swing
uniform float StormHazeLift;
uniform float StormHazeTint;

float sky_hash(vec2 p)
{
    p = fract(p * vec2(123.34, 456.21));
    p += dot(p, p + 45.32);
    return fract(p.x * p.y);
}

float sky_noise(vec2 p)
{
    vec2 i = floor(p);
    vec2 f = fract(p);
    vec2 u = f * f * (3.0 - 2.0 * f);
    float a = sky_hash(i);
    float b = sky_hash(i + vec2(1.0, 0.0));
    float c = sky_hash(i + vec2(0.0, 1.0));
    float d = sky_hash(i + vec2(1.0, 1.0));
    return mix(mix(a, b, u.x), mix(c, d, u.x), u.y);
}

float sky_fbm(vec2 p, int octaves)
{
    float v = 0.0;
    float amp = 0.5;
    for (int i = 0; i < octaves; i++)
    {
        v += sky_noise(p) * amp;
        p = p * 2.13 + vec2(31.7, 17.3);
        amp *= 0.5;
    }
    return v;
}

// The haze between the horizon and the deck base: a vertical value ramp with a
// slight hue swing instead of one flat fog colour. The exact fog colour is pinned
// at dir.y = 0, where the fogged ground meets the sky.
vec3 StormHazeColor(vec3 dir, vec3 fogColor, float baseY)
{
    float t = clamp(dir.y / max(baseY, 1e-4), 0.0, 1.0);

    float tintAmt = clamp(StormHazeTint, 0.0, 1.0);
    vec3 warm = mix(vec3(1.0), vec3(1.06, 1.01, 0.94), tintAmt);
    vec3 cool = mix(vec3(1.0), vec3(0.90, 0.95, 1.05), tintAmt);

    vec3 haze = fogColor * mix(warm, cool, t) * (1.0 + StormHazeLift * (1.0 - t));
    return mix(fogColor, haze, smoothstep(0.0, baseY * 0.18, dir.y));
}

vec3 StormSkyColor(vec3 dir, vec3 fogColor)
{
    // Keep the planar projection finite at the horizon; below ~0.03 the result
    // is all haze anyway.
    float upy = max(dir.y, 0.05);

    float scale = max(StormScale, 0.05);
    vec2 windUV = WindDir * (WindTime * StormSpeed);
    vec2 camDrift = CameraPosition.xz * 0.0009 * scale; // the deck stays put over the world

    // --- Layer 1: high main deck ---
    vec2 uv1 = (dir.xz / upy) * 0.32 * scale + camDrift - windUV * 0.014;
    // Domain warp tears the smooth blobs into ragged edges
    vec2 warp = vec2(sky_noise(uv1 * 2.1 + 13.7), sky_noise(uv1 * 2.1 - 7.9)) - 0.5;
    uv1 += warp * 0.42;
    float d1 = sky_fbm(uv1, 5);

    float cov = clamp(StormCoverage, 0.0, 1.0);
    float edge1 = 0.55 - cov * 0.45;
    float m1 = smoothstep(edge1, edge1 + 0.30, d1);

    // Self-shading: coarse resample offset along the wind
    float dShade = sky_fbm(uv1 + WindDir * 0.13, 3);
    float shade = clamp((dShade - d1) * 2.6 + 0.5, 0.0, 1.0);

    // --- Layer 2: low scud, closer, darker, ~2.4x faster ---
    vec2 uv2 = (dir.xz / upy) * 0.85 * scale + camDrift * 2.4 - windUV * 0.033 + vec2(41.0);
    float d2 = sky_fbm(uv2, 4);
    float edge2 = 0.70 - cov * 0.42;
    float m2 = smoothstep(edge2, edge2 + 0.34, d2);

    // --- Composite ---
    float zen = smoothstep(0.15, 0.85, dir.y);
    vec3 gap = StormLightColor * mix(1.06, 0.60, zen);
    vec3 deckLit = mix(StormLightColor, StormDarkColor, 0.45);
    vec3 deck = mix(deckLit, StormDarkColor, shade);
    vec3 sky = mix(gap, deck, m1);
    sky = mix(sky, StormDarkColor * 0.78, m2 * 0.9);

    // Melt into the fog at the horizon band
    float baseY = max(StormHeight, 0.05) * 0.19;
    float horizonFog = 1.0 - smoothstep(baseY * 0.105, baseY, dir.y);
    sky = mix(sky, StormHazeColor(dir, fogColor, baseY), horizonFog);

    // --- Rain curtains ---
    // Sampled on the horizon circle (normalized dir.xz) rather than the planar
    // projection, so the pattern stays bounded as dir.y -> 0. Rotating the circle
    // drifts the shafts sideways; a small dir.y shear makes them read as falling
    // streaks. Drawn after the haze so they stay legible against it.
    float virgaTop = 1.0 - smoothstep(baseY * 0.9, baseY * 2.6, dir.y);
    float virgaAmt = clamp(StormVirgaAmount, 0.0, 1.0);
    if (virgaTop * virgaAmt > 0.002)
    {
        vec2 hdir = normalize(dir.xz + vec2(1e-5));
        float wa = WindTime * StormVirgaSpeed * 0.02;
        float cw = cos(wa);
        float sw = sin(wa);
        vec2 hrot = vec2(hdir.x * cw - hdir.y * sw, hdir.x * sw + hdir.y * cw);

        vec2 vuv = hrot * max(StormVirgaDensity, 0.5) + vec2(dir.y * 1.2, 0.0);
        float v = smoothstep(0.45, 0.72, sky_fbm(vuv, 3));
        v *= mix(0.55, 1.0, sky_noise(vuv * 3.1 + 19.0));

        // Ragged bottoms: dense shafts hang low, wispy ones evaporate high
        float virgaBot = smoothstep(0.0, baseY * mix(0.45, 0.10, v), dir.y);

        vec3 virgaColor = mix(fogColor, StormDarkColor, 0.55);
        sky = mix(sky, virgaColor, v * virgaTop * virgaBot * virgaAmt);
    }

    return sky;
}
