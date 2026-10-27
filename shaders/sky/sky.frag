#version 430 core
out vec4 FragColor;

// Procedural sky: a gradient from the fog colour at the horizon to the zenith
// colour, a sun disc with a glow, drifting fair-weather clouds, and the storm deck
// cross-faded on top. The horizon is exactly the fog colour, so fogged terrain
// meets the sky without a line.

in vec3 vDirection;

#include "../common_uniforms.glsl"
#include "common_storm_sky.glsl"

uniform vec3  ZenithColor;
uniform float GradientPower;
uniform float SkyBrightness;
uniform float HorizonFog;       // how far the fog colour climbs up from the horizon
uniform float SunDiscVisible;

uniform float CloudCoverage;    // 0 = clear, 1 = overcast
uniform float CloudScale;
uniform float CloudSpeed;
uniform vec3  CloudLitColor;
uniform vec3  CloudShadeColor;

uniform float StormAmount;      // 0 = clear sky only, 1 = storm deck only

// Fair-weather clouds on a virtual plane overhead. Returns the cloud colour in
// rgb and its opacity in a.
vec4 FairClouds(vec3 dir)
{
    if (CloudCoverage <= 0.001 || dir.y <= 0.0) return vec4(0.0);
    float upy = max(dir.y, 0.03);
    float scale = max(CloudScale, 0.05);

    vec2 uv = (dir.xz / upy) * 0.6 * scale
            + CameraPosition.xz * 0.0008 * scale
            - WindDir * (WindTime * CloudSpeed * 0.02);
    vec2 warp = vec2(sky_noise(uv * 1.3 + 5.3), sky_noise(uv * 1.3 - 9.1)) - 0.5;
    uv += warp * 0.5;
    float density = sky_fbm(uv, 6);

    // Coverage thins the clouds toward the horizon less than overhead, the way
    // a cloud field seen at a grazing angle closes up.
    float cov = clamp(CloudCoverage, 0.0, 1.0);
    float edge = 0.68 - cov * 0.4 - (1.0 - smoothstep(0.0, 0.5, dir.y)) * 0.06;
    float mask = smoothstep(edge, edge + 0.12, density);

    // Lit toward the sun, shaded at the dense cores
    vec2 sunXZ = normalize(SunDirection.xz + vec2(1e-5));
    float towardSun = sky_fbm(uv + sunXZ * 0.08, 4);
    float lit = clamp(0.6 + (density - towardSun) * 4.0, 0.0, 1.0);
    lit *= mix(1.0, 0.75, smoothstep(edge + 0.1, edge + 0.35, density));
    vec3 color = mix(CloudShadeColor, CloudLitColor, lit);

    // Thin edges glow when they pass in front of the sun
    float sunAlign = max(dot(dir, SunDirection), 0.0);
    color += vec3(1.0, 0.92, 0.75) * pow(sunAlign, 24.0) * (1.0 - mask) * 0.8;

    // The cloud plane recedes into haze toward the horizon
    float fade = smoothstep(0.02, 0.25, dir.y);
    return vec4(color * SkyBrightness, mask * fade);
}

void main()
{
    vec3 dir = normalize(vDirection);
    vec3 color = AtmosphereColor;

    if (StormAmount < 0.999)
    {
        float up = clamp(dir.y, 0.0, 1.0);
        vec3 sky = mix(AtmosphereColor, ZenithColor * SkyBrightness, pow(up, max(GradientPower, 0.01)));

        vec4 clouds = FairClouds(dir);

        if (SunDiscVisible > 0.5)
        {
            float angle = acos(clamp(dot(dir, SunDirection), -1.0, 1.0));
            float disc = 1.0 - smoothstep(0.018, 0.024, angle);
            float glow = exp(-angle * angle / 0.012) * 0.4;
            vec3 sun = disc * vec3(1.0, 0.88, 0.5) * 2.5 * (1.0 - clouds.a) + glow * vec3(1.0, 0.75, 0.3);
            sky += sun * (dir.y > -0.02 ? 1.0 : 0.0);
        }

        sky = mix(sky, clouds.rgb, clouds.a);

        // Fog colour climbing up from the horizon; below it, all fog
        float horizon = 1.0 - smoothstep(0.0, 0.4, max(dir.y, 0.0));
        sky = mix(sky, AtmosphereColor, horizon * HorizonFog);
        color = dir.y < 0.0 ? AtmosphereColor : sky;
    }

    if (StormAmount > 0.001)
        color = mix(color, StormSkyColor(dir, AtmosphereColor), StormAmount);

    FragColor = vec4(color, 1.0);
}
