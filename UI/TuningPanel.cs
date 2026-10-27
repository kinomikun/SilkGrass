using System;
using System.Numerics;
using ImGuiNET;
using SilkGrass.Cameras;
using SilkGrass.Grass;
using SilkGrass.Rendering;
using SilkGrass.Settings;

namespace SilkGrass.UI
{
    /// <summary>
    /// The ImGui window that exposes every tunable live. Toggle with F1.
    /// </summary>
    public sealed class TuningPanel
    {
        private static readonly Vector4 Warning = new(1f, 0.6f, 0.3f, 1f);
        private static readonly string[] AaModes = { "TAA", "MSAA", "Off" };
        private static readonly string[] Fidelities = { "High", "Medium", "Low" };
        private static readonly string[] Tonemaps = { "None", "ACES", "Reinhard" };
        private static readonly string[] TaaDebugViews = { "Off", "Velocity", "History rejection" };
        private static readonly int[] MsaaOptions = { 2, 4, 8 };

        private readonly SceneSettings _scene;
        private readonly RenderSettings _render;
        private readonly SceneRenderer _renderer;
        private readonly FreeFlyCamera _camera;

        private readonly float[] _densityPlot = new float[128];
        private readonly float[] _suppliedPlot = new float[128];
        private readonly float[] _coveragePlot = new float[128];

        private float _fps;
        private float _frameMs;

        public bool Visible = true;
        public bool StayAboveGround = true;
        public Action ResetCamera;

        public TuningPanel(SceneSettings scene, RenderSettings render, SceneRenderer renderer, FreeFlyCamera camera)
        {
            _scene = scene;
            _render = render;
            _renderer = renderer;
            _camera = camera;
        }

        public void Draw(float deltaTime)
        {
            // Smoothed frame timing
            float ms = deltaTime * 1000f;
            _frameMs = _frameMs <= 0f ? ms : _frameMs + (ms - _frameMs) * 0.05f;
            _fps = _frameMs > 0f ? 1000f / _frameMs : 0f;

            _renderer.Grass.StatsEnabled = false;
            if (!Visible) return;

            ImGui.SetNextWindowPos(new Vector2(12, 12), ImGuiCond.FirstUseEver);
            ImGui.SetNextWindowSize(new Vector2(440, 760), ImGuiCond.FirstUseEver);
            if (!ImGui.Begin("SilkGrass"))
            {
                ImGui.End();
                return;
            }
            // Widgets leave room for their labels on the right
            ImGui.PushItemWidth(-170f);

            ImGui.Text($"{_fps:F0} fps  ({_frameMs:F2} ms)");
            ImGui.TextDisabled("RMB look | LMB trample | WASD move");
            ImGui.TextDisabled("Space/E up | Ctrl/Q down | Shift sprint");
            ImGui.TextDisabled("Wheel speed | F1 panel | F12 screenshot");

            DrawStats();
            DrawCamera();
            DrawWeather();
            DrawWind();
            DrawGrassLod();
            DrawColors();
            DrawTerrain();
            DrawAtmosphere();
            DrawSky();
            DrawSwell();
            DrawTrample();
            DrawRendering();

            ImGui.PopItemWidth();
            ImGui.End();
        }

        private void DrawStats()
        {
            if (!ImGui.CollapsingHeader("Stats")) return;
            var grass = _renderer.Grass;
            grass.StatsEnabled = true;
            ImGui.Text($"Candidate blades: {grass.TotalInstances:N0}");
            uint total = 0;
            for (int i = 0; i < GrassBucketSet.BucketCount; i++)
            {
                var (visible, overflow) = grass.BucketCounts(i);
                total += visible;
                ImGui.Text($"{grass.BucketName(i),-8} {visible,10:N0} / {grass.BucketCapacity(i):N0}");
                if (overflow > 0)
                {
                    ImGui.SameLine();
                    ImGui.TextColored(Warning, $"OVERFLOW {overflow:N0}");
                }
            }
            ImGui.Text($"Visible blades: {total:N0}");
        }

        private void DrawCamera()
        {
            if (!ImGui.CollapsingHeader("Camera")) return;
            var p = _camera.Position;
            ImGui.Text($"Position {p.X:F1}, {p.Y:F1}, {p.Z:F1}");
            ImGui.SliderFloat("Field of view", ref _render.FieldOfViewDeg, 15f, 100f, "%.0f deg");
            ImGui.SliderFloat("Move speed", ref _camera.MoveSpeed, 1f, 200f, "%.1f m/s");
            ImGui.SliderFloat("Sprint multiplier", ref _camera.SprintMultiplier, 1f, 20f);
            ImGui.SliderFloat("Look sensitivity", ref _camera.LookSensitivity, 0.0005f, 0.01f, "%.4f");
            ImGui.Checkbox("Stay above ground", ref StayAboveGround);
            if (ImGui.Button("Reset camera")) ResetCamera?.Invoke();
        }

        private void DrawWeather()
        {
            if (!ImGui.CollapsingHeader("Weather")) return;
            var weather = _renderer.Weather;
            if (ImGui.Button("Clear")) weather.Start(WeatherPreset.Clear(), _scene);
            ImGui.SameLine();
            if (ImGui.Button("Rain")) weather.Start(WeatherPreset.Rain(), _scene);
            ImGui.SameLine();
            if (ImGui.Button("Storm")) weather.Start(WeatherPreset.Storm(), _scene);
            if (weather.IsRunning)
            {
                ImGui.SameLine();
                ImGui.TextDisabled($"-> {weather.TargetName}");
            }
            ImGui.SliderFloat("Transition (s)", ref weather.DurationSeconds, 0f, 20f, "%.1f");

            var w = _scene.Weather;
            ImGui.Checkbox("Rain", ref w.RainEnabled);
            ImGui.SliderFloat("Rain intensity", ref w.RainIntensity, 0f, 1f);
            ImGui.SliderFloat("Rain speed", ref w.RainSpeed, 1f, 80f);
            ImGui.SliderFloat("Rain opacity", ref w.RainOpacity, 0f, 1f);
            ImGui.ColorEdit3("Rain colour", ref w.RainColor);
            ImGui.SliderFloat("Wet darkening", ref w.WetDarkening, 0f, 1f);
            ImGui.SliderFloat("Splash frequency", ref w.SplashFrequency, 0.5f, 10f);
            ImGui.SliderFloat("Splash brightness", ref w.SplashBrightness, 0f, 2f);
            ImGui.SliderFloat("Wind speed multiplier", ref w.WindSpeedMultiplier, 0f, 4f);
        }

        private void DrawWind()
        {
            if (!ImGui.CollapsingHeader("Wind")) return;
            var w = _scene.Wind;
            ImGui.SliderFloat("Direction##wind", ref w.DirectionDeg, 0f, 360f, "%.0f deg");
            ImGui.SliderFloat("Speed##wind", ref w.Speed, 0f, 5f);
            ImGui.SliderFloat("Strength##wind", ref w.Strength, 0f, 3f);
            ImGui.SliderFloat("Frequency##wind", ref w.Frequency, 0.01f, 1f);
            ImGui.SliderFloat("Vertical bend", ref w.VerticalBend, 0f, 1f);
            ImGui.SliderFloat("Resting lean direction", ref w.BaseLeanDeg, 0f, 360f, "%.0f deg");
            ImGui.SliderFloat("Resting lean strength", ref w.BaseLeanStrength, 0f, 3f);
            ImGui.SliderFloat("Blade height", ref w.BladeHeight, 0.1f, 3f);
        }

        private void DrawGrassLod()
        {
            if (!ImGui.CollapsingHeader("Grass density and LOD")) return;
            var lod = _renderer.Lod;

            ImGui.TextDisabled("Density curve (blades per square metre)");
            ImGui.SliderFloat("Near density", ref lod.NearDensity, 1f, 60f);
            ImGui.SliderFloat("Plateau radius", ref lod.PlateauRadius, 10f, 400f, "%.0f m");
            ImGui.SliderFloat("Falloff exponent", ref lod.FalloffExponent, 0.2f, 3f);
            ImGui.SliderFloat("Far floor", ref lod.FarFloor, 0f, 2f);
            ImGui.SliderFloat("Grass horizon", ref lod.MaxDistance, 200f, 2000f, "%.0f m");
            ImGui.SliderFloat("Trail boost", ref lod.TrailBoostDensity, 0f, 20f);

            ImGui.TextDisabled("Coverage (blade width follows from it)");
            ImGui.SliderFloat("Width near", ref lod.WidthNear, 0.25f, 4f);
            ImGui.SliderFloat("Coverage at horizon", ref lod.CoverageFar, 0.2f, 2f);
            ImGui.SliderFloat("Coverage falloff start", ref lod.CoverageFalloffStart, 0f, 1500f, "%.0f m");
            ImGui.SliderFloat("Width max", ref lod.WidthMax, 1f, 40f);

            ImGui.TextDisabled("Mesh buckets");
            ImGui.SliderFloat("Near | mid", ref lod.MeshNearDistance, 20f, 300f, "%.0f m");
            ImGui.SliderFloat("Mid | far", ref lod.MeshMidDistance, 50f, 600f, "%.0f m");
            ImGui.SliderFloat("Far | distant", ref lod.MeshFarDistance, 100f, 1500f, "%.0f m");
            ImGui.SliderFloat("Dither band", ref lod.MeshDither, 0f, 100f, "%.0f m");
            if (lod.MeshNearDistance + lod.MeshDither * 0.5f > lod.InnerFieldSize * 0.5f)
                ImGui.TextColored(Warning, "Near bucket reaches past the trample sim");
            if (lod.MeshMidDistance + lod.MeshDither * 0.5f > lod.MiddleFieldSize * 0.5f)
                ImGui.TextColored(Warning, "Mid bucket reaches past the trail sim");
            if (lod.TrailBoostDensity > lod.InnerDensity - lod.NearDensity)
                ImGui.TextColored(Warning, "Trail boost exceeds the inner ring's spare capacity");

            float maxD = MathF.Max(lod.MaxDistance, 100f);
            float peak = 0f;
            for (int i = 0; i < _densityPlot.Length; i++)
            {
                float d = maxD * i / (_densityPlot.Length - 1);
                _densityPlot[i] = lod.DensityAt(d);
                _suppliedPlot[i] = lod.SuppliedDensityAt(d);
                _coveragePlot[i] = lod.CoverageAt(d);
                peak = MathF.Max(peak, _densityPlot[i]);
            }
            var size = new Vector2(0, 60);
            ImGui.PlotLines("Target", ref _densityPlot[0], _densityPlot.Length, 0, $"0..{maxD:F0} m", 0f, peak, size);
            ImGui.PlotLines("Supplied", ref _suppliedPlot[0], _suppliedPlot.Length, 0, null, 0f, peak, size);
            ImGui.PlotLines("Coverage", ref _coveragePlot[0], _coveragePlot.Length, 0, "1.0 = centre", 0f, 2f, size);

            if (ImGui.TreeNode("Capacity rings"))
            {
                ImGui.TextDisabled("Candidate blades per square metre, and ring size");
                ImGui.SliderFloat("Inner##cap", ref lod.InnerDensity, 1f, 60f);
                ImGui.SliderFloat("Inner size", ref lod.InnerFieldSize, 100f, 600f, "%.0f m");
                ImGui.SliderFloat("Middle##cap", ref lod.MiddleDensity, 0.5f, 20f);
                ImGui.SliderFloat("Middle size", ref lod.MiddleFieldSize, 200f, 1200f, "%.0f m");
                ImGui.SliderFloat("Far##cap", ref lod.FarDensity, 0.1f, 10f);
                ImGui.SliderFloat("Far size", ref lod.FarFieldSize, 400f, 2400f, "%.0f m");
                ImGui.SliderFloat("Distant##cap", ref lod.DistantDensity, 0.05f, 4f);
                ImGui.SliderFloat("Distant size", ref lod.DistantFieldSize, 800f, 4000f, "%.0f m");
                ImGui.SliderFloat("Ring fade width", ref lod.TransitionWidth, 0f, 200f, "%.0f m");

                long count = 0;
                for (int i = 0; i < 4; i++) count += lod.RingCount(i);
                ImGui.Text($"{count:N0} candidates, {count * 12 / (1024 * 1024)} MB");
                if (_renderer.Grass.FieldsOutOfDate)
                    ImGui.TextColored(Warning, "Rings changed - rebuild to apply");
                if (ImGui.Button("Rebuild grass rings")) _renderer.Grass.RebuildFields();
                ImGui.TreePop();
            }

            int fidelity = (int)_render.GrassFidelity;
            if (ImGui.Combo("Fidelity", ref fidelity, Fidelities, Fidelities.Length))
                _render.GrassFidelity = (GrassFidelity)fidelity;
            ImGui.Checkbox("Frustum culling", ref _render.FrustumCulling);
        }

        private void DrawColors()
        {
            if (!ImGui.CollapsingHeader("Grass colours")) return;
            var c = _scene.Colors;
            ImGui.ColorEdit3("Tip", ref c.GrassTop);
            ImGui.ColorEdit3("Root", ref c.GrassBottom);
            ImGui.ColorEdit3("Wind tint", ref c.WindTint);
            ImGui.ColorEdit3("Ground", ref c.GroundColor);
            ImGui.ColorEdit3("Horizon tip", ref c.HorizonTop);
            ImGui.ColorEdit3("Horizon root", ref c.HorizonBottom);
            ImGui.ColorEdit3("Valley", ref c.ValleyColor);
            ImGui.ColorEdit3("Hill", ref c.HillColor);
            ImGui.SliderFloat("Height gradient", ref c.HeightGradientIntensity, 0f, 1f);

            ImGui.TextDisabled("Patches");
            ImGui.SliderFloat("Frequency##patch", ref c.PatchNoiseFreq, 0.01f, 2f);
            ImGui.ColorEdit3("Dark tint##patch", ref c.PatchDarkTint);
            ImGui.ColorEdit3("Light tint##patch", ref c.PatchLightTint);
            ImGui.SliderFloat("Dark threshold", ref c.PatchDarkThreshold, 0f, 1f);
            ImGui.SliderFloat("Light threshold", ref c.PatchLightThreshold, 0f, 1f);

            ImGui.TextDisabled("Macro variation");
            ImGui.SliderFloat("Strength##macro", ref c.MacroVarStrength, 0f, 1f);
            ImGui.SliderFloat("Frequency##macro", ref c.MacroVarFreq, 0.0002f, 0.01f, "%.4f");
            ImGui.ColorEdit3("Dark tint##macro", ref c.MacroVarDarkTint);
            ImGui.ColorEdit3("Light tint##macro", ref c.MacroVarLightTint);

            ImGui.TextDisabled("Sheen");
            ImGui.SliderFloat("Strength##sheen", ref c.SheenStrength, 0f, 1f);
            ImGui.ColorEdit3("Colour##sheen", ref c.SheenColor);
            ImGui.SliderFloat("Scale##sheen", ref c.SheenScale, 0.01f, 1f);
            ImGui.SliderFloat("Speed##sheen", ref c.SheenSpeed, 0f, 3f);
            ImGui.SliderFloat("Max distance##sheen", ref c.SheenMaxDist, 50f, 600f, "%.0f m");

            ImGui.TextDisabled("Shade");
            ImGui.ColorEdit3("Shade tint", ref c.ShadowAmbientTint);
            ImGui.SliderFloat("Shade saturation", ref c.ShadowSaturation, 0f, 1f);
        }

        private void DrawTerrain()
        {
            if (!ImGui.CollapsingHeader("Terrain")) return;
            var t = _scene.Terrain;
            ImGui.SliderFloat("Frequency##terrain", ref t.Frequency, 0.0005f, 0.02f, "%.4f");
            ImGui.SliderFloat("Amplitude", ref t.Amplitude, 0f, 100f, "%.1f m");
            ImGui.SliderFloat("Valley height", ref t.ValleyHeight, 1f, 200f, "%.0f m");

            var a = _scene.Atmosphere;
            ImGui.TextDisabled("Relief light (far-field hillshading)");
            ImGui.SliderFloat("Strength##relief", ref a.ReliefStrength, 0f, 2f);
            ImGui.SliderFloat("Sun-facing", ref a.ReliefSunFacing, 0f, 2f);
            ImGui.SliderFloat("Lee", ref a.ReliefLee, 0f, 2f);
            ImGui.SliderFloat("Slope for full effect", ref a.ReliefFullSlope, 0.01f, 0.3f);
            ImGui.SliderFloat("Fade start##relief", ref a.ReliefFadeStart, 0f, 1000f, "%.0f m");
            ImGui.SliderFloat("Fade end##relief", ref a.ReliefFadeEnd, 0f, 2000f, "%.0f m");
        }

        private void DrawAtmosphere()
        {
            if (!ImGui.CollapsingHeader("Sun, fog and cloud shadows")) return;
            var a = _scene.Atmosphere;
            ImGui.SliderFloat("Sun azimuth", ref a.SunAzimuthDeg, -180f, 180f, "%.0f deg");
            ImGui.SliderFloat("Sun elevation", ref a.SunElevationDeg, 1f, 90f, "%.0f deg");
            ImGui.ColorEdit3("Fog colour", ref a.FogColor);
            ImGui.SliderFloat("Fog start", ref a.FogStart, 0f, 1000f, "%.0f m");
            ImGui.SliderFloat("Fog density", ref a.FogDensity, 0f, 10f);
            ImGui.SliderFloat("Fog height falloff", ref a.FogHeightFalloff, 0f, 0.1f, "%.4f");
            ImGui.SliderFloat("Fog max opacity", ref a.FogMaxOpacity, 0f, 1f);

            ImGui.Checkbox("Cloud shadows", ref a.CloudShadowsEnabled);
            ImGui.SliderFloat("Threshold##cloud", ref a.CloudShadowThreshold, -0.5f, 1f);
            ImGui.SliderFloat("Sharpness##cloud", ref a.CloudShadowSharpness, 0.01f, 1f);
            ImGui.SliderFloat("Speed##cloud", ref a.CloudShadowSpeed, 0f, 1f);
        }

        private void DrawSky()
        {
            if (!ImGui.CollapsingHeader("Sky")) return;
            var s = _scene.Sky;
            ImGui.ColorEdit3("Zenith", ref s.ZenithColor);
            ImGui.SliderFloat("Gradient power", ref s.GradientPower, 0.1f, 3f);
            ImGui.SliderFloat("Brightness##sky", ref s.Brightness, 0f, 2f);
            ImGui.SliderFloat("Horizon fog", ref s.HorizonFog, 0f, 1f);
            ImGui.Checkbox("Sun disc", ref s.SunDiscVisible);

            ImGui.TextDisabled("Clouds");
            ImGui.SliderFloat("Coverage##clouds", ref s.CloudCoverage, 0f, 1f);
            ImGui.SliderFloat("Scale##clouds", ref s.CloudScale, 0.1f, 4f);
            ImGui.SliderFloat("Speed##clouds", ref s.CloudSpeed, 0f, 5f);
            ImGui.ColorEdit3("Lit##clouds", ref s.CloudLitColor);
            ImGui.ColorEdit3("Shade##clouds", ref s.CloudShadeColor);

            ImGui.TextDisabled("Storm deck");
            ImGui.SliderFloat("Amount##storm", ref s.StormAmount, 0f, 1f);
            ImGui.SliderFloat("Coverage##storm", ref s.StormCoverage, 0f, 1f);
            ImGui.SliderFloat("Scale##storm", ref s.StormScale, 0.1f, 4f);
            ImGui.SliderFloat("Speed##storm", ref s.StormSpeed, 0f, 4f);
            ImGui.SliderFloat("Height##storm", ref s.StormHeight, 0.2f, 4f);
            ImGui.ColorEdit3("Dark##storm", ref s.StormDarkColor);
            ImGui.ColorEdit3("Light##storm", ref s.StormLightColor);
            ImGui.SliderFloat("Rain curtains", ref s.StormVirgaAmount, 0f, 1f);
            ImGui.SliderFloat("Curtain density", ref s.StormVirgaDensity, 0.5f, 20f);
            ImGui.SliderFloat("Curtain speed", ref s.StormVirgaSpeed, 0f, 2f);
            ImGui.SliderFloat("Haze lift", ref s.StormHazeLift, 0f, 0.5f);
            ImGui.SliderFloat("Haze tint", ref s.StormHazeTint, 0f, 1f);
        }

        private void DrawSwell()
        {
            if (!ImGui.CollapsingHeader("Swell")) return;
            var swell = _renderer.Swell;
            ImGui.SliderFloat("Height##swell", ref _scene.Atmosphere.SwellHeight, 0f, 2f, "%.2f m");
            ImGui.SliderFloat("Wavelength", ref swell.Wavelength, 10f, 400f, "%.0f m");
            ImGui.SliderFloat("Speed##swell", ref swell.Speed, 0f, 3f);
            ImGui.SliderFloat("Direction##swell", ref swell.DirectionDeg, 0f, 360f, "%.0f deg");
            ImGui.SliderFloat("Choppiness", ref swell.Choppiness, 0f, 3f);
            ImGui.SliderFloat("Crest sharpness", ref swell.CrestSharpness, 0f, 0.5f);
        }

        private void DrawTrample()
        {
            if (!ImGui.CollapsingHeader("Trample")) return;
            var t = _scene.Trample;
            ImGui.TextDisabled("Hold the left mouse button on the grass");
            ImGui.SliderFloat("Brush radius", ref t.Radius, 0.5f, 10f, "%.1f m");
            ImGui.SliderFloat("Recovery time", ref t.RecoveryTime, 0.5f, 60f, "%.1f s");
            ImGui.Checkbox("Camera tramples while low", ref t.CameraTramples);
            ImGui.SliderFloat("Max height", ref t.MaxHeight, 0.5f, 20f, "%.1f m");
        }

        private void DrawRendering()
        {
            if (!ImGui.CollapsingHeader("Rendering")) return;
            int aa = (int)_render.AntiAliasing;
            if (ImGui.Combo("Anti-aliasing", ref aa, AaModes, AaModes.Length))
                _render.AntiAliasing = (AntiAliasingMode)aa;
            if (_render.AntiAliasing == AntiAliasingMode.Msaa)
            {
                int idx = Math.Max(0, Array.IndexOf(MsaaOptions, _render.MsaaSamples));
                string[] labels = { "2x", "4x", "8x" };
                if (ImGui.Combo("Samples", ref idx, labels, labels.Length))
                    _render.MsaaSamples = MsaaOptions[idx];
            }
            ImGui.SliderFloat("Render scale", ref _render.RenderScale, 0.25f, 2f, "%.2f");
            ImGui.Checkbox("VSync", ref _render.VSync);

            var pp = _scene.PostProcess;
            if (_render.AntiAliasing == AntiAliasingMode.Taa && ImGui.TreeNode("TAA"))
            {
                ImGui.SliderFloat("Blend factor", ref pp.TaaBlendFactor, 0.01f, 0.5f);
                ImGui.SliderFloat("Sharpen", ref pp.TaaSharpen, 0f, 1f);
                ImGui.SliderFloat("Jitter scale", ref pp.TaaJitterScale, 0f, 2f);
                ImGui.SliderFloat("Variance gamma", ref pp.TaaVarianceGamma, 0.5f, 3f);
                ImGui.Combo("Debug view", ref pp.TaaDebugView, TaaDebugViews, TaaDebugViews.Length);
                ImGui.TreePop();
            }

            if (ImGui.TreeNode("Post-processing"))
            {
                ImGui.Combo("Tonemap", ref pp.TonemapMode, Tonemaps, Tonemaps.Length);
                ImGui.SliderFloat("Exposure", ref pp.TonemapExposure, 0.1f, 3f);
                ImGui.SliderFloat("Saturation", ref pp.Saturation, 0f, 2f);
                ImGui.SliderFloat("Brightness##pp", ref pp.Brightness, 0f, 2f);
                ImGui.SliderFloat("Contrast", ref pp.Contrast, 0f, 2f);
                ImGui.SliderFloat("Bloom threshold", ref pp.BloomThreshold, 0f, 2f);
                ImGui.SliderFloat("Bloom strength", ref pp.BloomStrength, 0f, 2f);
                ImGui.SliderInt("Bloom passes", ref pp.BloomPasses, 1, 8);
                ImGui.SliderFloat("Bloom radius", ref pp.BloomRadius, 0.1f, 3f);
                ImGui.SliderInt("Bloom downscale", ref pp.BloomDownscale, 1, 8);
                ImGui.SliderFloat("Distance blur start", ref pp.BlurStart, 0f, 3000f, "%.0f m");
                ImGui.SliderFloat("Distance blur fade", ref pp.BlurFade, 1f, 3000f, "%.0f m");
                ImGui.SliderFloat("Distance blur size", ref pp.MaxBlurSize, 0f, 4f);
                ImGui.SliderFloat("Film grain", ref pp.FilmGrainStrength, 0f, 0.2f);
                ImGui.SliderFloat("Vignette strength", ref pp.VignetteStrength, 0f, 1f);
                ImGui.SliderFloat("Vignette radius", ref pp.VignetteRadius, 0f, 1f);
                ImGui.ColorEdit3("Shadow tint", ref pp.ShadowTint);
                ImGui.SliderFloat("Shadow tint strength", ref pp.ShadowTintStrength, 0f, 1f);
                ImGui.ColorEdit3("Midtone tint", ref pp.MidtoneTint);
                ImGui.SliderFloat("Midtone tint strength", ref pp.MidtoneTintStrength, 0f, 1f);
                ImGui.ColorEdit3("Highlight tint", ref pp.HighlightTint);
                ImGui.SliderFloat("Highlight tint strength", ref pp.HighlightTintStrength, 0f, 1f);
                ImGui.TreePop();
            }
        }
    }
}
