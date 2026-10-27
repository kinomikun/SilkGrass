using System.Numerics;

namespace SilkGrass.Settings
{
    /// <summary>
    /// Every look and behaviour tunable of the scene, grouped as the tuning
    /// panel shows them. Fields are public so ImGui can bind them by ref; the
    /// renderer reads them every frame, so any edit is live.
    /// </summary>
    public class SceneSettings
    {
        public TerrainSettings Terrain = new();
        public ColorSettings Colors = new();
        public WindSettings Wind = new();
        public AtmosphereSettings Atmosphere = new();
        public SkySettings Sky = new();
        public WeatherSettings Weather = new();
        public TrampleSettings Trample = new();
        public PostProcessSettings PostProcess = new();

        public class TerrainSettings
        {
            public float Frequency = 0.003f;
            public float Amplitude = 20.0f;
            // The height gradient tints blades below ValleyHeight * 0.1 toward
            // the valley colour and above ValleyHeight * 0.3 toward the hill colour.
            public float ValleyHeight = 50.0f;
        }

        public class ColorSettings
        {
            public Vector3 GrassTop = new(0.361f, 0.848f, 0.166f);
            public Vector3 GrassBottom = new(0.221f, 0.618f, 0.373f);
            public Vector3 WindTint = new(0.8f, 0.95f, 1f);
            public Vector3 GroundColor = new(49 / 255f, 165 / 255f, 25 / 255f);

            // Terrain carpet past the grass horizon
            public Vector3 HorizonTop = new(146 / 255f, 229 / 255f, 124 / 255f);
            public Vector3 HorizonBottom = new(0.05f, 0.25f, 0.4f);

            public Vector3 ValleyColor = new(0.042f, 0.456f, 0.103f);
            public Vector3 HillColor = new(0.989f, 1f, 0.809f);
            public float HeightGradientIntensity = 0.6f;

            // Metre-scale patches of darker and lighter grass
            public float PatchNoiseFreq = 0.2f;
            public Vector3 PatchDarkTint = new(0.6f, 0.9f, 0.7f);
            public Vector3 PatchLightTint = new(1.4f, 1.3f, 0.5f);
            public float PatchDarkThreshold = 0.45f;
            public float PatchLightThreshold = 0.55f;

            // Large, low-frequency tint regions across the field
            public float MacroVarStrength = 0.5f;
            public float MacroVarFreq = 0.002f;
            public Vector3 MacroVarDarkTint = new(41 / 255f, 123 / 255f, 0f);
            public Vector3 MacroVarLightTint = new(1.10f, 1.12f, 1.00f);

            // A broad highlight band rolling across the field with the wind
            public float SheenStrength = 0.15f;
            public Vector3 SheenColor = new(0.95f, 1.0f, 0.85f);
            public float SheenScale = 0.2f;
            public float SheenSpeed = 0.6f;
            public float SheenMaxDist = 300.0f;

            // Shade is tinted cool and slightly saturated instead of darkened grey
            public Vector3 ShadowAmbientTint = new(0.62f, 0.78f, 0.88f);
            public float ShadowSaturation = 0.25f;
        }

        public class WindSettings
        {
            /// <summary>Direction the wind blows toward, in degrees (0 = +X, 90 = +Z).</summary>
            public float DirectionDeg = 180f;
            public float Speed = 0.75f;
            public float Strength = 0.5f;
            public float Frequency = 0.15f;
            public float VerticalBend = 0.1f;

            /// <summary>Resting lean of every blade, in degrees like DirectionDeg.</summary>
            public float BaseLeanDeg = 180f;
            public float BaseLeanStrength = 1f;
            public float BladeHeight = 1.0f;

            public Vector2 Direction => DirectionFromDegrees(DirectionDeg);
            public Vector2 BaseLeanDirection => DirectionFromDegrees(BaseLeanDeg);

            public static Vector2 DirectionFromDegrees(float deg)
            {
                float rad = deg * System.MathF.PI / 180f;
                return new Vector2(System.MathF.Cos(rad), System.MathF.Sin(rad));
            }
        }

        public class AtmosphereSettings
        {
            /// <summary>Fog colour; the sky meets it at the horizon.</summary>
            public Vector3 FogColor = new(122 / 255f, 172 / 255f, 184 / 255f);
            public float SunAzimuthDeg = -53.1f;
            public float SunElevationDeg = 58.0f;

            public float FogStart = 200f;
            /// <summary>Uploaded divided by 1000; the panel works in friendlier numbers.</summary>
            public float FogDensity = 0.60f;
            public float FogHeightFalloff = 0.01f;
            public float FogMaxOpacity = 0.8f;

            // Drifting cloud shadows on the grass and ground
            public bool CloudShadowsEnabled = true;
            public float CloudShadowThreshold = 0.15f;
            public float CloudShadowSharpness = 0.4f;
            public float CloudShadowSpeed = 0.065f;

            // Map-style hillshading of the far field: slopes facing the sun's
            // compass direction brighten and warm, lee slopes darken and cool.
            public float ReliefStrength = 0.4f;
            public float ReliefSunFacing = 1.0f;
            public float ReliefLee = 1.0f;
            public float ReliefFullSlope = 0.06f;
            public float ReliefFadeStart = 200f;
            public float ReliefFadeEnd = 500f;

            /// <summary>Height of the grass swell in metres; 0 = a still meadow.</summary>
            public float SwellHeight = 0f;

            public Vector3 SunDirection
            {
                get
                {
                    float az = SunAzimuthDeg * System.MathF.PI / 180f;
                    float el = SunElevationDeg * System.MathF.PI / 180f;
                    return Vector3.Normalize(new Vector3(
                        System.MathF.Cos(el) * System.MathF.Cos(az),
                        System.MathF.Sin(el),
                        System.MathF.Cos(el) * System.MathF.Sin(az)));
                }
            }
        }

        public class SkySettings
        {
            public Vector3 ZenithColor = new(0.24f, 0.47f, 0.84f);
            public float GradientPower = 0.55f;
            public float Brightness = 1.0f;
            /// <summary>How far the fog colour climbs up the sky from the horizon.</summary>
            public float HorizonFog = 0.1f;

            public bool SunDiscVisible = true;

            // Fair-weather clouds
            public float CloudCoverage = 0.4f;
            public float CloudScale = 1.0f;
            public float CloudSpeed = 1.0f;
            public Vector3 CloudLitColor = new(0.93f, 0.93f, 0.91f);
            public Vector3 CloudShadeColor = new(0.62f, 0.70f, 0.80f);

            // Dark two-layer storm deck, cross-faded over the clear sky
            public float StormAmount = 0f;
            public float StormCoverage = 0.62f;
            public float StormScale = 1.0f;
            public float StormSpeed = 1.0f;
            public float StormHeight = 1.0f;
            public Vector3 StormDarkColor = new(0.10f, 0.11f, 0.14f);
            public Vector3 StormLightColor = new(0.47f, 0.50f, 0.58f);
            public float StormVirgaAmount = 0.5f;
            public float StormVirgaDensity = 7.0f;
            public float StormVirgaSpeed = 0.25f;
            public float StormHazeLift = 0.10f;
            public float StormHazeTint = 0.6f;
        }

        public class WeatherSettings
        {
            public bool RainEnabled = false;
            public float RainIntensity = 0.5f;
            public float RainSpeed = 20.0f;
            public float RainOpacity = 0.3f;
            public Vector3 RainColor = new(0.7f, 0.75f, 0.85f);
            public float WetDarkening = 0.25f;
            public float SplashFrequency = 3.0f;
            public float SplashBrightness = 0.8f;
            /// <summary>Multiplies the wind scroll speed (storms blow harder).</summary>
            public float WindSpeedMultiplier = 1f;
        }

        /// <summary>
        /// The trample brush: holding the left mouse button presses it into the
        /// grass under the cursor. Trampled grass flattens, darkens and parts
        /// around the brush, and the trail recovers over RecoveryTime.
        /// </summary>
        public class TrampleSettings
        {
            public float Radius = 2.5f;
            /// <summary>The camera itself tramples while it flies low (when the brush is not in use).</summary>
            public bool CameraTramples = false;
            /// <summary>Above this height over the grass the camera stops trampling.</summary>
            public float MaxHeight = 4.0f;
            /// <summary>Seconds for trampled grass to stand back up.</summary>
            public float RecoveryTime = 10.0f;
        }

        public class PostProcessSettings
        {
            public float BloomThreshold = 0.1f;
            public float BloomStrength = 0.325f;
            public int BloomPasses = 3;
            public float BloomRadius = 0.8f;
            public int BloomDownscale = 2;

            // Depth blur of the far distance
            public float BlurStart = 1000.0f;
            public float BlurFade = 1200.0f;
            public float MaxBlurSize = 1f;

            public float FilmGrainStrength = 0.04f;
            public float Saturation = 1.125f;
            public float Brightness = 1.025f;
            public float Contrast = 1.275f;
            public float VignetteStrength = 0.35f;
            public float VignetteRadius = 0.6f;

            public int TonemapMode = 1; // 0 = none, 1 = ACES, 2 = Reinhard
            public float TonemapExposure = 0.7f;

            // Split toning (after the tonemap)
            public Vector3 ShadowTint = new(0.369f, 0.369f, 0.637f);
            public float ShadowTintStrength = 0.3f;
            public Vector3 MidtoneTint = new(0.853f, 0.853f, 0.447f);
            public float MidtoneTintStrength = 0.05f;
            public Vector3 HighlightTint = new(0.99f, 0.961f, 0.961f);
            public float HighlightTintStrength = 0.45f;

            // Temporal anti-aliasing
            public float TaaBlendFactor = 0.05f;
            public float TaaSharpen = 0.95f;
            public float TaaJitterScale = 1.0f;
            public float TaaVarianceGamma = 1.25f;
            public int TaaDebugView = 0; // 0 = off, 1 = velocity, 2 = history rejection
        }
    }
}
