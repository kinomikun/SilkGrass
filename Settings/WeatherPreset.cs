using System;
using System.Numerics;

namespace SilkGrass.Settings
{
    /// <summary>
    /// A snapshot of the weather-driven settings. Presets blend into the live
    /// settings over a few seconds (<see cref="WeatherTransition"/>), so rain,
    /// fog, the storm deck and the swell all come and go together.
    /// </summary>
    public class WeatherPreset
    {
        public string Name;
        public bool RainEnabled;
        public float RainIntensity;
        public float RainSpeed;
        public float RainOpacity;
        public Vector3 RainColor;
        public float WetDarkening;
        public float SplashFrequency;
        public float SplashBrightness;
        public float WindSpeedMultiplier;

        public Vector3 FogColor;
        public float FogDensity;
        public float FogHeightFalloff;
        public float FogMaxOpacity;
        public float CloudShadowThreshold;
        public float SwellHeight;

        public float SkyBrightness;
        public float SkyHorizonFog;
        public float CloudCoverage;
        public float StormAmount;

        /// <summary>The current values of the settings a preset controls.</summary>
        public static WeatherPreset FromSettings(SceneSettings s) => new WeatherPreset
        {
            Name = "Current",
            RainEnabled = s.Weather.RainEnabled,
            RainIntensity = s.Weather.RainIntensity,
            RainSpeed = s.Weather.RainSpeed,
            RainOpacity = s.Weather.RainOpacity,
            RainColor = s.Weather.RainColor,
            WetDarkening = s.Weather.WetDarkening,
            SplashFrequency = s.Weather.SplashFrequency,
            SplashBrightness = s.Weather.SplashBrightness,
            WindSpeedMultiplier = s.Weather.WindSpeedMultiplier,
            FogColor = s.Atmosphere.FogColor,
            FogDensity = s.Atmosphere.FogDensity,
            FogHeightFalloff = s.Atmosphere.FogHeightFalloff,
            FogMaxOpacity = s.Atmosphere.FogMaxOpacity,
            CloudShadowThreshold = s.Atmosphere.CloudShadowThreshold,
            SwellHeight = s.Atmosphere.SwellHeight,
            SkyBrightness = s.Sky.Brightness,
            SkyHorizonFog = s.Sky.HorizonFog,
            CloudCoverage = s.Sky.CloudCoverage,
            StormAmount = s.Sky.StormAmount,
        };

        /// <summary>Writes the blend from a to b at t (0..1) into the settings.</summary>
        public static void ApplyBlend(WeatherPreset a, WeatherPreset b, float t, SceneSettings s)
        {
            // Rain switches on at the start of a blend toward rain and off at
            // the end of a blend away from it; its intensity carries the fade.
            s.Weather.RainEnabled = b.RainEnabled || (a.RainEnabled && t < 1f);
            float fromIntensity = a.RainEnabled ? a.RainIntensity : 0f;
            float toIntensity = b.RainEnabled ? b.RainIntensity : 0f;
            s.Weather.RainIntensity = Lerp(fromIntensity, toIntensity, t);
            s.Weather.RainSpeed = Lerp(a.RainSpeed, b.RainSpeed, t);
            s.Weather.RainOpacity = Lerp(a.RainOpacity, b.RainOpacity, t);
            s.Weather.RainColor = Vector3.Lerp(a.RainColor, b.RainColor, t);
            s.Weather.WetDarkening = Lerp(a.WetDarkening, b.WetDarkening, t);
            s.Weather.SplashFrequency = Lerp(a.SplashFrequency, b.SplashFrequency, t);
            s.Weather.SplashBrightness = Lerp(a.SplashBrightness, b.SplashBrightness, t);
            s.Weather.WindSpeedMultiplier = Lerp(a.WindSpeedMultiplier, b.WindSpeedMultiplier, t);

            s.Atmosphere.FogColor = Vector3.Lerp(a.FogColor, b.FogColor, t);
            s.Atmosphere.FogDensity = Lerp(a.FogDensity, b.FogDensity, t);
            s.Atmosphere.FogHeightFalloff = Lerp(a.FogHeightFalloff, b.FogHeightFalloff, t);
            s.Atmosphere.FogMaxOpacity = Lerp(a.FogMaxOpacity, b.FogMaxOpacity, t);
            s.Atmosphere.CloudShadowThreshold = Lerp(a.CloudShadowThreshold, b.CloudShadowThreshold, t);
            s.Atmosphere.SwellHeight = Lerp(a.SwellHeight, b.SwellHeight, t);

            s.Sky.Brightness = Lerp(a.SkyBrightness, b.SkyBrightness, t);
            s.Sky.HorizonFog = Lerp(a.SkyHorizonFog, b.SkyHorizonFog, t);
            s.Sky.CloudCoverage = Lerp(a.CloudCoverage, b.CloudCoverage, t);
            s.Sky.StormAmount = Lerp(a.StormAmount, b.StormAmount, t);
        }

        private static float Lerp(float a, float b, float t) => a + (b - a) * t;

        public static WeatherPreset Clear() => FromSettingsWithName(new SceneSettings(), "Clear");

        public static WeatherPreset Rain() => new WeatherPreset
        {
            Name = "Rain",
            RainEnabled = true,
            RainIntensity = 0.4f,
            RainSpeed = 18.0f,
            RainOpacity = 0.25f,
            RainColor = new Vector3(0.6f, 0.65f, 0.7f),
            WetDarkening = 0.15f,
            SplashFrequency = 2.0f,
            SplashBrightness = 0.5f,
            WindSpeedMultiplier = 1f,
            FogColor = new Vector3(0.4f, 0.5f, 0.55f),
            FogDensity = 0.80f,
            FogHeightFalloff = 0.008f,
            FogMaxOpacity = 0.85f,
            CloudShadowThreshold = 1f,
            SwellHeight = 0.4f,
            SkyBrightness = 0.8f,
            SkyHorizonFog = 0.6f,
            CloudCoverage = 0.85f,
            StormAmount = 0.45f,
        };

        public static WeatherPreset Storm() => new WeatherPreset
        {
            Name = "Storm",
            RainEnabled = true,
            RainIntensity = 0.75f,
            RainSpeed = 50.0f,
            RainOpacity = 0.8f,
            RainColor = new Vector3(0.5f, 0.55f, 0.65f),
            WetDarkening = 0.5f,
            SplashFrequency = 5.0f,
            SplashBrightness = 0.8f,
            WindSpeedMultiplier = 2f,
            FogColor = new Vector3(79 / 255f, 83 / 255f, 93 / 255f),
            FogDensity = 6.0f,
            FogHeightFalloff = 0.0f,
            FogMaxOpacity = 0.85f,
            CloudShadowThreshold = 1f,
            SwellHeight = 0.6f,
            SkyBrightness = 1f,
            SkyHorizonFog = 0.65f,
            CloudCoverage = 1f,
            StormAmount = 1f,
        };

        private static WeatherPreset FromSettingsWithName(SceneSettings s, string name)
        {
            var p = FromSettings(s);
            p.Name = name;
            return p;
        }
    }

    /// <summary>Blends the live settings from where they are toward a preset.</summary>
    public class WeatherTransition
    {
        public float DurationSeconds = 4f;

        private WeatherPreset _from;
        private WeatherPreset _to;
        private float _elapsed;

        public bool IsRunning => _to != null;
        public string TargetName => _to?.Name;

        public void Start(WeatherPreset target, SceneSettings settings)
        {
            _from = WeatherPreset.FromSettings(settings);
            _to = target;
            _elapsed = 0f;
        }

        public void Update(float deltaTime, SceneSettings settings)
        {
            if (_to == null) return;
            _elapsed += deltaTime;
            float t = DurationSeconds > 0f ? Math.Clamp(_elapsed / DurationSeconds, 0f, 1f) : 1f;
            float eased = t * t * (3f - 2f * t);
            WeatherPreset.ApplyBlend(_from, _to, eased, settings);
            if (t >= 1f) _to = null;
        }
    }
}
