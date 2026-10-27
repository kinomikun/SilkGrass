using System;
using System.Numerics;
using System.Runtime.InteropServices;
using Silk.NET.OpenGL;
using SilkGrass.Settings;

namespace SilkGrass.Rendering
{
    /// <summary>
    /// The scene-wide uniform blocks, uploaded once per frame and read by
    /// every scene shader. The GLSL side is shaders/common_uniforms.glsl;
    /// the structs below mirror its std140 layout field for field (a vec3
    /// followed by a float packs into one 16-byte slot).
    ///
    /// Uniform block bindings:
    ///   1 FrameData       camera, matrices, clocks, TAA state
    ///   2 WindData        wind and blade shape
    ///   3 LookData        terrain and grass colours
    ///   4 AtmosphereData  fog, cloud shadows, rain, relief light
    ///   5 SwellData       (GrassSwell)
    ///   6 CullCells       (GrassRenderer)
    /// </summary>
    public sealed class UniformBuffers : IDisposable
    {
        public const uint FrameBinding = 1;
        public const uint WindBinding = 2;
        public const uint LookBinding = 3;
        public const uint AtmosphereBinding = 4;

        /// <summary>The panel's fog density is divided by this before it reaches the shaders.</summary>
        public const float FogDensityScale = 1000f;

        [StructLayout(LayoutKind.Sequential)]
        public struct FrameData
        {
            public Matrix4x4 View;
            public Matrix4x4 Projection;      // jittered while TAA is on
            public Matrix4x4 TaaCurrViewProj; // unjittered, for motion vectors
            public Matrix4x4 TaaPrevViewProj;
            public Vector3 CameraPosition; public float Time;
            public Vector3 CenterPosition; public float CloudShadowTime;
            public Vector3 SunDirection;   public float TaaActive;
            public float TaaPrevTime;
            public float TaaPrevWindTime;
            private float _pad0, _pad1;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct WindData
        {
            public Vector2 WindDir; public float WindSpeed; public float WindStrength;
            public float WindFrequency; public float VerticalBend; public float BaseLeanStrength; public float WindTime;
            public Vector2 BaseLeanDir; public float BladeHeight; private float _pad0;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct LookData
        {
            public Vector3 GrassTop;           public float TerrainFrequency;
            public Vector3 GrassBottom;        public float TerrainAmplitude;
            public Vector3 WindTint;           public float ValleyY;
            public Vector3 ValleyColor;        public float HillY;
            public Vector3 HillColor;          public float GradientIntensity;
            public Vector3 PatchDarkTint;      public float PatchNoiseFreq;
            public Vector3 PatchLightTint;     public float PatchDarkThreshold;
            public Vector3 GroundColor;        public float PatchLightThreshold;
            public Vector3 HorizonTopColor;    public float MacroVarStrength;
            public Vector3 HorizonBottomColor; public float MacroVarFreq;
            public Vector3 MacroVarDarkTint;   public float ShadowSaturation;
            public Vector3 MacroVarLightTint;  public float SheenStrength;
            public Vector3 ShadowAmbientTint;  public float SheenScale;
            public Vector3 SheenColor;         public float SheenSpeed;
            public Vector2 SheenDir;           public float SheenMaxDist; private float _pad0;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct AtmosphereData
        {
            public Vector3 AtmosphereColor; public float FogStart;
            public float FogDensity; public float FogHeightFalloff; public float FogMaxOpacity; public float CloudThreshold;
            public float CloudShadowSharpness; public float RainEnabled; public float RainIntensity; public float WetDarkening;
            public float SplashFrequency; public float SplashBrightness; private float _pad0, _pad1;
            public Vector4 ReliefParams; // strength, slope for full effect, fade start, fade end
            public Vector2 ReliefSunAz;  public Vector2 ReliefSplit;
        }

        private readonly GL _gl;
        private readonly uint _frameUbo, _windUbo, _lookUbo, _atmosphereUbo;

        public FrameData Frame;

        public unsafe UniformBuffers(GL gl)
        {
            _gl = gl;
            _frameUbo = Create(sizeof(FrameData), FrameBinding);
            _windUbo = Create(sizeof(WindData), WindBinding);
            _lookUbo = Create(sizeof(LookData), LookBinding);
            _atmosphereUbo = Create(sizeof(AtmosphereData), AtmosphereBinding);
        }

        private unsafe uint Create(int size, uint binding)
        {
            uint ubo = _gl.GenBuffer();
            _gl.BindBuffer(BufferTargetARB.UniformBuffer, ubo);
            _gl.BufferData(BufferTargetARB.UniformBuffer, (nuint)size, null, BufferUsageARB.DynamicDraw);
            _gl.BindBuffer(BufferTargetARB.UniformBuffer, 0);
            _gl.BindBufferBase(BufferTargetARB.UniformBuffer, binding, ubo);
            return ubo;
        }

        private unsafe void Upload<T>(uint ubo, uint binding, ref T data) where T : unmanaged
        {
            _gl.BindBuffer(BufferTargetARB.UniformBuffer, ubo);
            fixed (T* p = &data)
                _gl.BufferSubData(BufferTargetARB.UniformBuffer, 0, (nuint)sizeof(T), p);
            _gl.BindBuffer(BufferTargetARB.UniformBuffer, 0);
            _gl.BindBufferBase(BufferTargetARB.UniformBuffer, binding, ubo);
        }

        /// <summary>Uploads <see cref="Frame"/> as filled in by the caller.</summary>
        public void UploadFrame() => Upload(_frameUbo, FrameBinding, ref Frame);

        public void UploadScene(SceneSettings s, float windTime, Vector3 cameraPosition, Vector3 centerPosition,
            float nearBucketReach)
        {
            var wind = new WindData
            {
                WindDir = s.Wind.Direction,
                WindSpeed = s.Wind.Speed,
                WindStrength = s.Wind.Strength,
                WindFrequency = s.Wind.Frequency,
                VerticalBend = s.Wind.VerticalBend,
                BaseLeanStrength = s.Wind.BaseLeanStrength,
                WindTime = windTime,
                BaseLeanDir = s.Wind.BaseLeanDirection,
                BladeHeight = s.Wind.BladeHeight,
            };
            Upload(_windUbo, WindBinding, ref wind);

            var c = s.Colors;
            var look = new LookData
            {
                GrassTop = c.GrassTop,
                GrassBottom = c.GrassBottom,
                WindTint = c.WindTint,
                ValleyColor = c.ValleyColor,
                HillColor = c.HillColor,
                PatchDarkTint = c.PatchDarkTint,
                PatchLightTint = c.PatchLightTint,
                GroundColor = c.GroundColor,
                HorizonTopColor = c.HorizonTop,
                HorizonBottomColor = c.HorizonBottom,
                MacroVarDarkTint = c.MacroVarDarkTint,
                MacroVarLightTint = c.MacroVarLightTint,
                ShadowAmbientTint = c.ShadowAmbientTint,
                SheenColor = c.SheenColor,
                TerrainFrequency = s.Terrain.Frequency,
                TerrainAmplitude = s.Terrain.Amplitude,
                ValleyY = s.Terrain.ValleyHeight * 0.1f,
                HillY = s.Terrain.ValleyHeight * 0.3f,
                GradientIntensity = c.HeightGradientIntensity,
                PatchNoiseFreq = c.PatchNoiseFreq,
                PatchDarkThreshold = c.PatchDarkThreshold,
                PatchLightThreshold = c.PatchLightThreshold,
                MacroVarStrength = c.MacroVarStrength,
                MacroVarFreq = c.MacroVarFreq,
                ShadowSaturation = c.ShadowSaturation,
                SheenStrength = c.SheenStrength,
                SheenScale = c.SheenScale,
                SheenSpeed = c.SheenSpeed,
                // The scroll moves the sampled field, which pushes the visible
                // band the other way: negate to make it follow the wind.
                SheenDir = -s.Wind.Direction,
                SheenMaxDist = c.SheenMaxDist,
            };
            Upload(_lookUbo, LookBinding, ref look);

            var a = s.Atmosphere;
            var w = s.Weather;

            // The near grass bucket carries no relief code (its blades end
            // before the fade normally begins), so the fade may never start
            // inside it or the bucket edge would show as a seam. Buckets are
            // measured from the field centre and the fade from the camera,
            // hence the camera offset on top of the bucket's reach.
            float fadeStart = MathF.Max(a.ReliefFadeStart,
                nearBucketReach + Vector2.Distance(new Vector2(cameraPosition.X, cameraPosition.Z),
                                                   new Vector2(centerPosition.X, centerPosition.Z)));
            float fadeEnd = MathF.Max(a.ReliefFadeEnd, fadeStart + 1f);
            Vector3 sun = a.SunDirection;
            var sunAz = new Vector2(sun.X, sun.Z);

            var atm = new AtmosphereData
            {
                AtmosphereColor = a.FogColor,
                FogStart = a.FogStart,
                FogDensity = a.FogDensity / FogDensityScale,
                FogHeightFalloff = a.FogHeightFalloff,
                FogMaxOpacity = a.FogMaxOpacity,
                // Disabled: push the smoothstep band below the noise's range,
                // so every receiver reads fully lit.
                CloudThreshold = a.CloudShadowsEnabled ? a.CloudShadowThreshold : -2f,
                CloudShadowSharpness = a.CloudShadowsEnabled ? a.CloudShadowSharpness : 0.0001f,
                RainEnabled = w.RainEnabled ? 1f : 0f,
                RainIntensity = w.RainIntensity,
                WetDarkening = w.WetDarkening,
                SplashFrequency = w.SplashFrequency,
                SplashBrightness = w.SplashBrightness,
                ReliefParams = new Vector4(a.ReliefStrength, a.ReliefFullSlope, fadeStart, fadeEnd),
                ReliefSunAz = sunAz.LengthSquared() > 1e-6f ? Vector2.Normalize(sunAz) : Vector2.UnitX,
                ReliefSplit = new Vector2(a.ReliefSunFacing, a.ReliefLee),
            };
            Upload(_atmosphereUbo, AtmosphereBinding, ref atm);
        }

        public void Dispose()
        {
            _gl.DeleteBuffer(_frameUbo);
            _gl.DeleteBuffer(_windUbo);
            _gl.DeleteBuffer(_lookUbo);
            _gl.DeleteBuffer(_atmosphereUbo);
        }
    }
}
