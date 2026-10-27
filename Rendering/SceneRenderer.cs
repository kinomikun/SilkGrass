using System;
using System.Numerics;
using Silk.NET.OpenGL;
using SilkGrass.Cameras;
using SilkGrass.Grass;
using SilkGrass.Settings;
using SilkGrass.Terrain;

namespace SilkGrass.Rendering
{
    /// <summary>
    /// Runs one frame: advances the clocks and the simulations, prepares the
    /// camera for TAA, uploads the uniform blocks, draws the scene into the
    /// HDR target and hands it to the post-process.
    ///
    /// Draw order: sky (no depth write), grass, horizon carpet, ground (after
    /// the grass, so the depth test rejects the ground the blades cover), rain.
    /// </summary>
    public sealed class SceneRenderer : IDisposable
    {
        private readonly GL _gl;
        private readonly SceneSettings _scene;
        private readonly RenderSettings _render;

        private readonly UniformBuffers _ubos;
        private readonly PostProcess _post;
        private readonly SkyRenderer _sky;
        private readonly TerrainRenderer _terrain;
        private readonly RainRenderer _rain;

        public GrassLodSettings Lod { get; } = new GrassLodSettings();
        public GrassRenderer Grass { get; }
        public GrassFluidSim FluidSim { get; }
        public GrassSwell Swell { get; }
        public WeatherTransition Weather { get; } = new WeatherTransition();

        // Clocks integrated from their speeds, so changing a speed never makes
        // the pattern it drives jump (which time * speed would).
        private float _windTime;
        private float _cloudShadowTime;
        private float _prevTime;
        private float _prevWindTime;
        private bool _prevValid;

        // Previous frame's camera, for TAA reprojection
        private Matrix4x4 _prevViewProj;
        private Matrix4x4 _prevViewRot;
        private Matrix4x4 _prevProj;
        private Vector3 _prevCamPos;

        public SceneRenderer(GL gl, SceneSettings scene, RenderSettings render, int width, int height)
        {
            _gl = gl;
            _scene = scene;
            _render = render;

            // The velocity path is a compile-time define: set it before the
            // first scene program compiles.
            TaaVelocity.SetEnabled(render.AntiAliasing == AntiAliasingMode.Taa);

            _ubos = new UniformBuffers(gl);
            Swell = new GrassSwell(gl);
            _post = new PostProcess(gl, width, height);
            _sky = new SkyRenderer(gl);
            _terrain = new TerrainRenderer(gl);
            _rain = new RainRenderer(gl);
            FluidSim = new GrassFluidSim(gl);
            Grass = new GrassRenderer(gl, Lod, render.GrassFidelity);
        }

        /// <summary>Terrain plus swell height under a point, as rendered.</summary>
        public float SurfaceHeightAt(float x, float z) =>
            TerrainNoise.HeightAt(x, z, _scene.Terrain.Frequency, _scene.Terrain.Amplitude) + Swell.HeightAt(x, z);

        public void InvalidateTaaHistory() => _post.InvalidateHistory();

        /// <summary>
        /// Marches a ray against the grass surface (terrain plus swell).
        /// Coarse steps that grow with distance, then bisection.
        /// </summary>
        public bool RaycastSurface(Vector3 origin, Vector3 direction, float maxDistance, out Vector3 hit)
        {
            hit = default;
            direction = Vector3.Normalize(direction);
            float prev = 0f;
            float t = 0f;
            while (t < maxDistance)
            {
                float step = 0.5f + t * 0.02f;
                t = MathF.Min(t + step, maxDistance);
                Vector3 p = origin + direction * t;
                if (p.Y > SurfaceHeightAt(p.X, p.Z)) { prev = t; continue; }

                float lo = prev, hi = t;
                for (int i = 0; i < 16; i++)
                {
                    float mid = (lo + hi) * 0.5f;
                    Vector3 m = origin + direction * mid;
                    if (m.Y > SurfaceHeightAt(m.X, m.Z)) lo = mid; else hi = mid;
                }
                hit = origin + direction * hi;
                return true;
            }
            return false;
        }

        /// <param name="brush">World XZ of the trample brush this frame, or null.</param>
        public void Render(FreeFlyCamera camera, float deltaTime, float time, int windowW, int windowH, Vector2? brush)
        {
            var s = _scene;
            Weather.Update(deltaTime, s);

            _windTime += deltaTime * s.Wind.Speed * s.Weather.WindSpeedMultiplier;
            _cloudShadowTime += deltaTime * s.Atmosphere.CloudShadowSpeed;
            Swell.Advance(deltaTime, s.Atmosphere.SwellHeight);
            Swell.Upload();

            // AA mode and fidelity changes recompile the affected programs
            TaaVelocity.SetEnabled(_render.AntiAliasing == AntiAliasingMode.Taa);
            Grass.SetFidelity(_render.GrassFidelity);

            var (rtW, rtH) = PostProcess.TargetSize(windowW, windowH, _render.RenderScale);
            camera.UpdateMatrices((float)windowW / Math.Max(1, windowH), _render.FieldOfViewDeg, _render.NearClip, _render.FarClip);
            PrepareTaaMatrices(camera, rtW, rtH);

            // The field, the sims and the rings all follow the camera
            Vector3 center = camera.Position;
            var center2 = new Vector2(center.X, center.Z);

            // One trampler: the brush while it is held, otherwise the camera
            // itself (when enabled) while it skims the surface
            Vector2 tramplerPos = brush ?? center2;
            float trample = 0f;
            if (brush.HasValue)
            {
                trample = 1f;
            }
            else if (s.Trample.CameraTramples && s.Trample.MaxHeight > 0f)
            {
                float heightAboveGrass = camera.Position.Y - SurfaceHeightAt(center.X, center.Z);
                trample = Math.Clamp((s.Trample.MaxHeight - heightAboveGrass) / (s.Trample.MaxHeight * 0.3f), 0f, 1f);
            }

            FluidSim.Step(deltaTime, Lod.InnerFieldSize, Lod.MiddleFieldSize, center2, tramplerPos,
                s.Trample.Radius, trample, s.Trample.RecoveryTime);

            _post.BeginFrame(windowW, windowH, _render, s.PostProcess);
            _gl.Enable(EnableCap.DepthTest);
            _gl.DepthFunc(DepthFunction.Less);
            // Blades are camera-facing strips and the terrain is seen from
            // above; nothing gains from back-face culling.
            _gl.Disable(EnableCap.CullFace);

            if (!_prevValid)
            {
                _prevTime = time;
                _prevWindTime = _windTime;
                _prevValid = true;
            }

            ref var frame = ref _ubos.Frame;
            frame.View = camera.View;
            frame.Projection = camera.Projection;
            frame.CameraPosition = camera.Position;
            frame.CenterPosition = center;
            frame.SunDirection = s.Atmosphere.SunDirection;
            frame.Time = time;
            frame.CloudShadowTime = _cloudShadowTime;
            frame.TaaActive = TaaVelocity.Active ? 1f : 0f;
            frame.TaaPrevTime = _prevTime;
            frame.TaaPrevWindTime = _prevWindTime;
            _ubos.UploadFrame();
            _ubos.UploadScene(s, _windTime, camera.Position, center,
                Lod.MeshNearDistance + Lod.MeshDither * 0.5f);

            _sky.Render(s.Sky);

            Grass.Render(new GrassFrame
            {
                View = camera.View,
                Projection = camera.Projection,
                CameraPosition = camera.Position,
                CenterPosition = center,
                TerrainAmplitude = s.Terrain.Amplitude,
                FrustumCulling = _render.FrustumCulling,
                FluidTexture = FluidSim.FluidSim.CurrentTexture,
                TrailTexture = FluidSim.TrailSim.CurrentTexture,
                SimCenter = center2,
                TramplerPos = tramplerPos,
                TramplerRadius = s.Trample.Radius,
                TramplerStrength = trample,
            });

            // The carpet starts a little inside the distant ring's edge, so
            // they overlap instead of leaving a gap.
            _terrain.RenderHorizon(Lod.DistantFieldSize * 0.5f - 50f, Grass.BindMacroNoise);
            _terrain.RenderGround(camera.Position, FluidSim.FluidSim.CurrentTexture, FluidSim.TrailSim.CurrentTexture,
                center2, Lod.InnerFieldSize, Lod.MiddleFieldSize);

            _rain.Render(s.Weather);

            _post.EndFrame(windowW, windowH, _render, s.PostProcess);

            _prevTime = time;
            _prevWindTime = _windTime;
        }

        /// <summary>
        /// Publishes the unjittered current/previous view-projections and the
        /// reprojection matrix, then jitters the camera's projection for TAA.
        /// Everything reprojection-related is captured before the jitter.
        /// </summary>
        private void PrepareTaaMatrices(FreeFlyCamera camera, int targetW, int targetH)
        {
            Matrix4x4 viewProj = camera.View * camera.Projection;
            Matrix4x4 viewRot = camera.View;
            viewRot.M41 = 0f; viewRot.M42 = 0f; viewRot.M43 = 0f;
            Vector3 camPos = camera.Position;

            if (_prevViewProj.M44 == 0f)
            {
                _prevViewProj = viewProj;
                _prevViewRot = viewRot;
                _prevProj = camera.Projection;
                _prevCamPos = camPos;
            }

            _ubos.Frame.TaaCurrViewProj = viewProj;
            _ubos.Frame.TaaPrevViewProj = _prevViewProj;

            // Current clip -> previous clip, composed about the CURRENT camera
            // as origin: the only translation left is the camera's per-frame
            // move, so every entry stays small and a still camera composes to
            // the exact identity. Built in world space instead, the camera's
            // world position would sit in the matrices and float32 would lose
            // the reprojection's precision far from the origin.
            Matrix4x4 vpRelCurr = viewRot * camera.Projection;
            Matrix4x4 vpRelPrev = Matrix4x4.CreateTranslation(camPos - _prevCamPos) * _prevViewRot * _prevProj;
            _post.CurrToPrevClip = Matrix4x4.Invert(vpRelCurr, out var inv) ? inv * vpRelPrev : Matrix4x4.Identity;

            _prevViewProj = viewProj;
            _prevViewRot = viewRot;
            _prevProj = camera.Projection;
            _prevCamPos = camPos;

            if (_render.AntiAliasing == AntiAliasingMode.Taa)
            {
                camera.ApplyJitter(_post.NextJitterPixels(_scene.PostProcess.TaaJitterScale), targetW, targetH);
                // ApplyJitter adds the offset to M31/M32, which under the
                // row-vector convention SUBTRACTS it from NDC: the texel at uv
                // holds the sample taken at uv + offset / 2 on the unjittered grid.
                _post.TaaJitterUV = camera.JitterOffset * 0.5f;
            }
            else
            {
                _post.TaaJitterUV = Vector2.Zero;
            }
        }

        public void Dispose()
        {
            Grass.Dispose();
            FluidSim.Dispose();
            _rain.Dispose();
            _terrain.Dispose();
            _sky.Dispose();
            _post.Dispose();
            Swell.Dispose();
            _ubos.Dispose();
        }
    }
}
