#version 430 core
out vec4 FragColor;

in vec2 TexCoords;

uniform sampler2D ScreenTexture;
uniform float BloomThreshold;

void main()
{
    vec3 color = texture(ScreenTexture, TexCoords).rgb;

    // The scene target is RGBA16F, so NaN, Inf and negatives from upstream maths
    // survive sampling. Drop them here, or the downsample spreads a NaN across a
    // whole tile (a black square after the tonemap). Select, never mix(): mix()
    // passes a NaN straight through. Very bright fireflies are capped too.
    bool bad = any(isnan(color)) || any(isinf(color));
    color = bad ? vec3(0.0) : color;
    color = clamp(color, vec3(0.0), vec3(64.0));

    float brightness = dot(color, vec3(0.2126, 0.7152, 0.0722));

    // Soft knee above the threshold
    const float knee = 0.1;
    float soft = clamp(brightness - BloomThreshold + knee, 0.0, 2.0 * knee);
    soft = soft * soft / (4.0 * knee + 0.00001);
    float contribution = max(soft, brightness - BloomThreshold);
    contribution = max(contribution, 0.0) / max(brightness, 0.00001);

    FragColor = vec4(color * contribution, 1.0);
}
