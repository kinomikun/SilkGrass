#version 430 core
out vec4 FragColor;

// Final composite to the screen: distance blur, bloom, film grain, tonemap,
// split toning, colour grade and vignette.

in vec2 TexCoords;

uniform sampler2D ScreenTexture;
uniform sampler2D DepthTexture;
uniform sampler2D BloomTexture;

uniform float ZNear;
uniform float ZFar;

// Depth blur of the far distance
uniform float BlurStart;
uniform float BlurFade;
uniform float MaxBlurSize;

uniform float BloomStrength;
uniform float FilmGrainStrength;
uniform float GrainSeed;        // small integer frame counter

uniform float Saturation;
uniform float Brightness;
uniform float Contrast;

uniform float VignetteStrength;
uniform float VignetteRadius;   // where the darkening starts

uniform int TonemapMode;        // 0 = none, 1 = ACES, 2 = Reinhard
uniform float TonemapExposure;

uniform vec3 ShadowTint;
uniform float ShadowTintStrength;
uniform vec3 MidtoneTint;
uniform float MidtoneTintStrength;
uniform vec3 HighlightTint;
uniform float HighlightTintStrength;

// Hash-without-sine (Hoskins). Keyed on the integer pixel and the frame as a
// separate axis, so the grain is redrawn every frame instead of sliding.
float grainHash13(vec3 p)
{
    p = fract(p * 0.1031);
    p += dot(p, p.zyx + 31.32);
    return fract((p.x + p.y) * p.z);
}

// ACES filmic fit (Stephen Hill)
vec3 ACESFilm(vec3 x) {
    const float a = 2.51, b = 0.03, c = 2.43, d = 0.59, e = 0.14;
    return clamp((x * (a * x + b)) / (x * (c * x + d) + e), 0.0, 1.0);
}

float LinearizeDepth(float depth)
{
    float z = depth * 2.0 - 1.0;
    return (2.0 * ZNear * ZFar) / (ZFar + ZNear - z * (ZFar - ZNear));
}

void main()
{
    vec2 texel = 1.0 / vec2(textureSize(ScreenTexture, 0));
    float depth = texture(DepthTexture, TexCoords).r;
    bool isSky = depth >= 0.9999;

    // --- Distance blur (never on the sky, which sits at the far plane) ---
    vec3 color = texture(ScreenTexture, TexCoords).rgb;
    float blurStrength = isSky ? 0.0 : smoothstep(BlurStart, BlurStart + BlurFade, LinearizeDepth(depth));
    if (blurStrength > 0.01) {
        vec3 sum = vec3(0.0);
        float total = 0.0;
        float size = MaxBlurSize * blurStrength;
        for (float x = -1.0; x <= 1.0; x++)
        for (float y = -1.0; y <= 1.0; y++) {
            float w = 1.0 / (1.0 + abs(x) + abs(y));
            sum += texture(ScreenTexture, TexCoords + vec2(x, y) * size * texel).rgb * w;
            total += w;
        }
        color = mix(color, sum / total, blurStrength);
    }

    // --- Bloom ---
    color += texture(BloomTexture, TexCoords).rgb * BloomStrength;

    // NaN/Inf/negatives from upstream maths would become black tiles after the
    // tonemap. Select, never mix(): mix() passes a NaN through.
    bool bad = any(isnan(color)) || any(isinf(color));
    color = max(bad ? vec3(0.0) : color, vec3(0.0));

    // --- Film grain ---
    float grain = grainHash13(vec3(floor(gl_FragCoord.xy), GrainSeed));
    color += (grain - 0.5) * FilmGrainStrength;

    // --- Tonemap and split toning ---
    if (TonemapMode > 0) {
        color *= TonemapExposure;
        color = TonemapMode == 1 ? ACESFilm(color) : color / (1.0 + color);

        float luma = dot(color, vec3(0.2126, 0.7152, 0.0722));
        float shadowWeight = 1.0 - smoothstep(0.0, 0.5, luma);
        color = mix(color, color * ShadowTint * 2.0, shadowWeight * ShadowTintStrength);
        float midWeight = max(1.0 - abs(luma - 0.5) * 2.0, 0.0);
        color = mix(color, color * MidtoneTint * 2.0, midWeight * MidtoneTintStrength);
        float highWeight = smoothstep(0.5, 1.0, luma);
        color = mix(color, color * HighlightTint * 2.0, highWeight * HighlightTintStrength);
    }

    // --- Colour grade ---
    float luma = dot(color, vec3(0.2126, 0.7152, 0.0722));
    color = mix(vec3(luma), color, Saturation);
    color *= Brightness;
    color = (color - 0.5) * Contrast + 0.5;

    // --- Vignette ---
    if (VignetteStrength > 0.001) {
        float dist = length(TexCoords - 0.5) * 1.414; // corners = 1
        color *= 1.0 - smoothstep(VignetteRadius, VignetteRadius + 0.45, dist) * VignetteStrength;
    }

    FragColor = vec4(color, 1.0);
}
