#version 430 core
layout(location = 0) out vec4 FragColor;

// Temporal anti-aliasing resolve: blends the jittered current frame into the
// reprojected history.
//   - history: 9-tap Catmull-Rom (5 bilinear fetches) - plain bilinear history
//     resampling converges to mush under motion
//   - rejection: variance clipping in tonemapped YCgCo, clipping toward the box
//     centre (keeps the history's hue, unlike per-channel clamping)
//   - motion: per-pixel velocity from the scene, dilated to the closest-depth
//     texel of the 3x3 neighbourhood; camera reprojection from depth where no
//     velocity was written
//   - the current frame is reconstructed at the pixel centre (jitter-aware), so
//     the accumulation does not converge to a jitter-blurred image

in vec2 TexCoords;

uniform sampler2D ColorTexture;    // current frame (jittered)
uniform sampler2D HistoryTexture;  // previous resolve output
uniform sampler2D DepthTexture;
uniform sampler2D VelocityTexture; // RG16F UV-space motion; the clear sentinel where nothing wrote

// Current clip space -> previous clip space in ONE matrix, built camera-relative
// on the CPU. Going through world space instead (unproject, divide, re-project)
// pushes positions through kilometre-scale coordinates and cancels them again,
// which destroys float precision far from the origin and leaves a still camera
// with small non-zero motion. Composed, a still camera gives exact identity.
uniform mat4 CurrToPrevClip;
uniform vec2 Resolution;
uniform float BlendFactor;         // base weight of the current frame
uniform float VarianceGamma;       // clip box width in standard deviations
uniform int DebugView;             // 0 = off, 1 = velocity, 2 = history rejection

// This frame's sub-pixel jitter in UV: the sample stored in the texel at uv was
// taken at uv + JitterUV on the unjittered pixel grid the history lives on.
uniform vec2 JitterUV;

// The velocity buffer is cleared to (10000, 10000). Real motion is far below 1.0
// in UV, so anything past this means "nothing wrote velocity here".
const float VELOCITY_SENTINEL = 5000.0;

// Temporal maths runs in tonemapped YCgCo: the reversible tonemap keeps HDR
// fireflies from dominating the accumulation, and separating chroma gives a
// tighter clip box than RGB.
vec3 RGBtoYCgCo(vec3 rgb) {
    float y  = dot(rgb, vec3(0.25, 0.50, 0.25));
    float cg = dot(rgb, vec3(-0.25, 0.50, -0.25));
    float co = dot(rgb, vec3(0.50, 0.00, -0.50));
    return vec3(y, cg, co);
}

vec3 YCgCoToRGB(vec3 ycc) {
    float t = ycc.x - ycc.y;
    float g = ycc.x + ycc.y;
    float b = t - ycc.z;
    float r = t + ycc.z;
    return vec3(r, g, b);
}

vec3 Tonemap(vec3 x) { return x / (1.0 + x); }
vec3 InvertTonemap(vec3 x) { return x / (1.0 - clamp(x, 0.0, 0.999)); }

// Tonemap(64): the firefly cap bloom_threshold.frag applies anyway.
const float TONEMAP_MAX = 0.9846;

// Back to RGB, forced inside the tonemapped gamut first. The variance clip box is
// wider than the valid colour gamut on every axis and the clip lands the history
// on its surface, so the raw reconstruction can hold a channel above 1 or below
// 0. InvertTonemap would blow the first up to ~1000 (a saturated speck that
// bloom then spreads), and the error always runs along Cg, the green-magenta
// axis. Clamping here also stops such a pixel feeding back into the history.
vec3 ResolveToRGB(vec3 ycc)
{
    return InvertTonemap(clamp(YCgCoToRGB(ycc), 0.0, TONEMAP_MAX));
}

vec3 SampleHistoryCatmullRom(vec2 uv) {
    vec2 samplePos = uv * Resolution;
    vec2 texPos1 = floor(samplePos - 0.5) + 0.5;
    vec2 f = samplePos - texPos1;

    vec2 w0 = f * (-0.5 + f * (1.0 - 0.5 * f));
    vec2 w1 = 1.0 + f * f * (-2.5 + 1.5 * f);
    vec2 w2 = f * (0.5 + f * (2.0 - 1.5 * f));
    vec2 w3 = f * f * (-0.5 + 0.5 * f);

    vec2 w12 = w1 + w2;
    vec2 offset12 = w2 / w12;

    vec2 texPos0 = (texPos1 - 1.0) / Resolution;
    vec2 texPos3 = (texPos1 + 2.0) / Resolution;
    vec2 texPos12 = (texPos1 + offset12) / Resolution;

    vec3 result =
        texture(HistoryTexture, vec2(texPos0.x,  texPos0.y)).rgb  * w0.x  * w0.y +
        texture(HistoryTexture, vec2(texPos12.x, texPos0.y)).rgb  * w12.x * w0.y +
        texture(HistoryTexture, vec2(texPos3.x,  texPos0.y)).rgb  * w3.x  * w0.y +
        texture(HistoryTexture, vec2(texPos0.x,  texPos12.y)).rgb * w0.x  * w12.y +
        texture(HistoryTexture, vec2(texPos12.x, texPos12.y)).rgb * w12.x * w12.y +
        texture(HistoryTexture, vec2(texPos3.x,  texPos12.y)).rgb * w3.x  * w12.y +
        texture(HistoryTexture, vec2(texPos0.x,  texPos3.y)).rgb  * w0.x  * w3.y +
        texture(HistoryTexture, vec2(texPos12.x, texPos3.y)).rgb  * w12.x * w3.y +
        texture(HistoryTexture, vec2(texPos3.x,  texPos3.y)).rgb  * w3.x  * w3.y;

    // Catmull-Rom can ring slightly negative on hard edges
    return max(result, vec3(0.0));
}

vec3 ClipToAABB(vec3 history, vec3 boxMin, vec3 boxMax) {
    vec3 boxCenter = 0.5 * (boxMax + boxMin);
    vec3 boxExtent = 0.5 * (boxMax - boxMin) + 1e-5;
    vec3 rayDir = history - boxCenter;
    vec3 dists = abs(rayDir) / boxExtent;
    float maxDist = max(dists.x, max(dists.y, dists.z));
    if (maxDist > 1.0)
        return boxCenter + rayDir / maxDist;
    return history;
}

void main()
{
    ivec2 px = ivec2(gl_FragCoord.xy);
    ivec2 maxCoord = ivec2(Resolution) - 1;
    vec2 uv = TexCoords;

    // ------------------------------------------------------------------
    // 1. 3x3 neighbourhood: colour moments for the clip box, the closest-depth
    //    texel for velocity dilation, and the current frame reconstructed at the
    //    pixel centre. Each neighbour sampled the scene at its own centre plus
    //    the jitter; weighting them by a small kernel evaluated at that true
    //    offset puts the current frame back on the history's grid. Every tap is
    //    still an exact texel fetch, so this sharpens rather than blurs.
    //    Gaussian fit to Blackman-Harris (exp(-2.29 d^2)): no negative lobes,
    //    so it cannot ring on HDR highlights.
    // ------------------------------------------------------------------
    vec3 m1 = vec3(0.0);
    vec3 m2 = vec3(0.0);
    vec3 center = vec3(0.0);
    float centerWeight = 0.0;
    float closestDepth = 1.0;
    ivec2 closestOffset = ivec2(0);

    vec2 jitterPx = JitterUV * Resolution;

    for (int y = -1; y <= 1; y++) {
        for (int x = -1; x <= 1; x++) {
            ivec2 p = clamp(px + ivec2(x, y), ivec2(0), maxCoord);
            vec3 s = RGBtoYCgCo(Tonemap(texelFetch(ColorTexture, p, 0).rgb));
            m1 += s;
            m2 += s * s;

            vec2 d = vec2(x, y) + jitterPx;
            float w = exp(-2.29 * dot(d, d));
            center += s * w;
            centerWeight += w;

            float dep = texelFetch(DepthTexture, p, 0).r;
            if (dep < closestDepth) {
                closestDepth = dep;
                closestOffset = ivec2(x, y);
            }
        }
    }

    center /= max(centerWeight, 1e-5);

    vec3 mu = m1 / 9.0;
    vec3 sigma = sqrt(max(m2 / 9.0 - mu * mu, vec3(0.0)));
    vec3 boxMin = mu - VarianceGamma * sigma;
    vec3 boxMax = mu + VarianceGamma * sigma;

    // ------------------------------------------------------------------
    // 2. Velocity, fetched at the closest-depth texel so silhouette edges take
    //    the foreground's motion. Where nothing wrote velocity, reproject from
    //    depth with the unjittered matrices (exact for anything only the
    //    camera moves).
    // ------------------------------------------------------------------
    ivec2 vpx = clamp(px + closestOffset, ivec2(0), maxCoord);
    vec2 velocity = texelFetch(VelocityTexture, vpx, 0).rg;
    bool dynamicPixel = velocity.x < VELOCITY_SENTINEL;

    if (!dynamicPixel) {
        vec2 cuv = (vec2(vpx) + 0.5) / Resolution;
        vec4 clipPos = vec4(cuv * 2.0 - 1.0, closestDepth * 2.0 - 1.0, 1.0);
        vec4 prevClip = CurrToPrevClip * clipPos;
        vec2 prevUVc = (prevClip.xy / prevClip.w) * 0.5 + 0.5;
        velocity = cuv - prevUVc;
    }

    vec2 prevUV = uv - velocity;

    // ------------------------------------------------------------------
    // 3. History fetch and clip
    // ------------------------------------------------------------------
    if (prevUV.x < 0.0 || prevUV.x > 1.0 || prevUV.y < 0.0 || prevUV.y > 1.0) {
        FragColor = vec4(ResolveToRGB(center), 1.0); // came from offscreen: no history
        return;
    }

    vec3 history = RGBtoYCgCo(Tonemap(SampleHistoryCatmullRom(prevUV)));
    vec3 clippedHistory = ClipToAABB(history, boxMin, boxMax);

    // ------------------------------------------------------------------
    // 4. Blend: the base weight plus a gentle, capped velocity boost (fast
    //    content gets fresher history without collapsing the accumulation),
    //    luma-weighted against residual flicker from bright sub-pixel features.
    // ------------------------------------------------------------------
    float velPixels = length(velocity * Resolution);
    float blend = clamp(BlendFactor + clamp(velPixels * 0.01, 0.0, 0.1), BlendFactor, 0.25);

    float wCurr = blend / (1.0 + center.x);
    float wHist = (1.0 - blend) / (1.0 + clippedHistory.x);
    vec3 result = (center * wCurr + clippedHistory * wHist) / (wCurr + wHist);

    if (DebugView == 1) {
        // R/G = |motion| in pixels / 4, B = velocity was written
        FragColor = vec4(abs(velocity * Resolution) * 0.25, dynamicPixel ? 1.0 : 0.0, 1.0);
        return;
    }
    if (DebugView == 2) {
        // How far the clip box pulled the history
        float reject = length(clippedHistory - history) * 10.0;
        FragColor = vec4(reject, 1.0 - reject, 0.0, 1.0);
        return;
    }

    FragColor = vec4(ResolveToRGB(result), 1.0);
}
