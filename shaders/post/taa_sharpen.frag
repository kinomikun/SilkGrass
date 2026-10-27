#version 430 core
out vec4 FragColor;

// Contrast-adaptive unsharp mask after the TAA resolve, counteracting its
// accumulation softness. Writes to its own target, so the history stays unsharpened.

in vec2 TexCoords;

uniform sampler2D ColorTexture;
uniform vec2 Resolution;
uniform float SharpenStrength;    // 0 = off, 1 = strong

void main()
{
    vec2 texel = 1.0 / Resolution;
    vec3 c = texture(ColorTexture, TexCoords).rgb;
    vec3 n = texture(ColorTexture, TexCoords + vec2(0.0,  texel.y)).rgb;
    vec3 s = texture(ColorTexture, TexCoords + vec2(0.0, -texel.y)).rgb;
    vec3 e = texture(ColorTexture, TexCoords + vec2( texel.x, 0.0)).rgb;
    vec3 w = texture(ColorTexture, TexCoords + vec2(-texel.x, 0.0)).rgb;

    vec3 detail = c - (n + s + e + w) * 0.25;

    // Less sharpening where local contrast is already high, to avoid ringing
    const vec3 lumaW = vec3(0.299, 0.587, 0.114);
    float lc = dot(c, lumaW), ln = dot(n, lumaW), ls = dot(s, lumaW), le = dot(e, lumaW), lw = dot(w, lumaW);
    float localContrast = max(lc, max(max(ln, ls), max(le, lw))) - min(lc, min(min(ln, ls), min(le, lw)));
    float strength = SharpenStrength * (1.0 - smoothstep(0.05, 0.3, localContrast));

    FragColor = vec4(max(c + detail * strength, vec3(0.0)), 1.0);
}
