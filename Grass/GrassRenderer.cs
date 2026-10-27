using System;
using System.Linq;
using System.Numerics;
using Silk.NET.OpenGL;
using SilkGrass.Graphics;
using SilkGrass.Rendering;
using SilkGrass.Settings;

namespace SilkGrass.Grass
{
    /// <summary>
    /// Culls and draws the grass.
    ///
    /// Per frame:
    ///   1. CPU pre-cull (GrassCellCuller): which grid cells of each ring can
    ///      hold a visible blade, nearest the camera first.
    ///   2. GPU cull (grass_cull.comp), one dispatch per ring over the listed
    ///      cells: toroidal wrap around the field centre, density allocation,
    ///      frustum test, terrain snap - once per blade instead of once per
    ///      vertex. Survivors are compacted into four distance buckets and
    ///      counted straight into indirect draw commands.
    ///   3. One indirect draw per bucket, each with the program variant
    ///      compiled for that distance band (grass.vert / grass.frag):
    ///        near:    full physics (trample sim, waves, push), micro flutter
    ///        mid:     trail-sim trample, push
    ///        far:     static blades, whole colour pipeline per vertex
    ///      All four write TAA motion vectors.
    /// </summary>
    public sealed class GrassRenderer : IDisposable
    {
        // Texture units used by the grass programs
        private const int FluidUnit = 0;
        private const int TrailUnit = 1;
        private const int WindNoiseUnit = 4;
        private const int MacroNoiseUnit = 5;

        private const uint CullCellsBinding = 6;
        // Pad on every frustum plane in the cull tests (metres). Covers blade
        // height, wind lean and the swell lift, which the cull does not model.
        private const float FrustumMargin = 8.0f;

        // Generous bounds on how many blades can land in each bucket. The cull
        // pass drops and counts any extras (shown as overflow in the panel).
        private static readonly int[] BucketCapacities = { 2_500_000, 3_000_000, 3_000_000, 2_500_000 };
        private static readonly string[] BucketNames = { "Near", "Mid", "Far", "Distant" };

        private readonly GL _gl;
        private readonly GrassLodSettings _lod;
        private readonly GrassCellCuller _cellCuller = new GrassCellCuller();
        private readonly FrustumCuller _frustum = new FrustumCuller();
        private readonly float[] _planeData = new float[24];
        private readonly GrassField[] _rings = new GrassField[GrassCellCuller.RingCount];

        private readonly ShaderProgram _cullShader;
        private readonly GrassBucketSet _buckets;
        private readonly uint _cullCellsUbo;
        private readonly GrassMesh _bladeHigh;
        private readonly GrassMesh _bladeMedium;
        private readonly GrassMesh _bladeRect;
        private readonly WindNoiseLut _windNoise;
        private readonly MacroNoiseLut _macroNoise;

        private ShaderProgram _nearShader;
        private ShaderProgram _midShader;
        private ShaderProgram _farShader;
        private GrassFidelity _fidelity;

        private int _statsCooldown;

        /// <summary>Read back visible blade counts every few dozen frames (for the panel).</summary>
        public bool StatsEnabled;

        public int TotalInstances => _rings.Sum(r => r?.InstanceCount ?? 0);
        public string BucketName(int bucket) => BucketNames[bucket];
        public int BucketCapacity(int bucket) => _buckets.Capacity(bucket);
        public (uint Visible, uint Overflow) BucketCounts(int bucket) => _buckets.Counts[bucket];

        /// <summary>Whether the ring sizes changed since the fields were built.</summary>
        public bool FieldsOutOfDate
        {
            get
            {
                for (int i = 0; i < _rings.Length; i++)
                    if (_rings[i].FieldSize != _lod.RingSize(i) || _rings[i].InstanceCount != _lod.RingCount(i))
                        return true;
                return false;
            }
        }

        public GrassRenderer(GL gl, GrassLodSettings lod, GrassFidelity fidelity)
        {
            _gl = gl;
            _lod = lod;
            _cullShader = new ShaderProgram(gl, "grass/grass_cull.comp");
            _buckets = new GrassBucketSet(gl, BucketCapacities);
            _cullCellsUbo = _gl.GenBuffer();
            _bladeHigh = new GrassMesh(gl, BladeShape.High);
            _bladeMedium = new GrassMesh(gl, BladeShape.Medium);
            _bladeRect = new GrassMesh(gl, BladeShape.Rectangle);
            _windNoise = new WindNoiseLut(gl);
            _macroNoise = new MacroNoiseLut(gl);
            CreatePrograms(fidelity);
            RebuildFields();
        }

        /// <summary>
        /// Extra defines per fidelity level. High adds none, so it compiles the
        /// full shaders. The flutter cut also removes the matching term from
        /// the TAA previous-position evaluation, so motion vectors stay exact.
        /// </summary>
        public static string[] FidelityDefines(GrassFidelity level) => level switch
        {
            GrassFidelity.Medium => new[] { "GRASS_CUT_SHEEN" },
            GrassFidelity.Low => new[]
            {
                "GRASS_CUT_SHEEN", "GRASS_CUT_MACRO", "GRASS_CUT_PATCHES",
                "GRASS_CUT_FLUTTER", "GRASS_CUT_SPLASH", "GRASS_CLOUD_PER_VERTEX",
            },
            _ => Array.Empty<string>(),
        };

        private void CreatePrograms(GrassFidelity level)
        {
            const string vert = "grass/grass.vert";
            const string frag = "grass/grass.frag";
            string[] cuts = FidelityDefines(level);
            string[] Defines(string tier) => new[] { tier }.Concat(cuts).ToArray();

            _nearShader = new ShaderProgram(_gl, vert, frag, Defines("GRASS_FULL_PHYSICS"));
            _midShader = new ShaderProgram(_gl, vert, frag, Defines("GRASS_TRAIL_PHYSICS"));
            _farShader = new ShaderProgram(_gl, vert, frag, Defines("GRASS_VS_COLOR"));
            _fidelity = level;
        }

        /// <summary>Binds the macro variation texture, for surfaces that continue the grass colour.</summary>
        public void BindMacroNoise(int unit) => _macroNoise.Bind(unit);

        /// <summary>Recompiles the grass programs for a new fidelity level (no-op when unchanged).</summary>
        public void SetFidelity(GrassFidelity level)
        {
            if (level == _fidelity) return;
            _nearShader.Dispose();
            _midShader.Dispose();
            _farShader.Dispose();
            CreatePrograms(level);
        }

        /// <summary>Recreates the four rings from the current ring sizes and capacities.</summary>
        public void RebuildFields()
        {
            for (int i = 0; i < _rings.Length; i++)
            {
                _rings[i]?.Dispose();
                _rings[i] = new GrassField(_gl, _lod.RingSize(i), _lod.RingCount(i), seed: 1 + i);
            }
            Log.Info($"[Grass] Built {TotalInstances:N0} candidate blades in {_rings.Length} rings.");
        }

        public void Render(in GrassFrame f)
        {
            _frustum.Update(f.View, f.Projection);
            Vector4[] planes = f.FrustumCulling ? _frustum.Planes : null;

            // Terrain noise lies in [0, amplitude]: the cull pass can test the
            // frustum at y = 0 with this much extra margin before it pays for
            // the terrain height, and still never reject a visible blade.
            float heightBound = MathF.Abs(f.TerrainAmplitude);

            PrepareCullCells(f.CameraPosition, f.CenterPosition, planes, heightBound);

            _windNoise.Bind(WindNoiseUnit);
            _macroNoise.Bind(MacroNoiseUnit);
            _gl.ActiveTexture(TextureUnit.Texture0 + FluidUnit);
            _gl.BindTexture(TextureTarget.Texture2D, f.FluidTexture);
            _gl.ActiveTexture(TextureUnit.Texture0 + TrailUnit);
            _gl.BindTexture(TextureTarget.Texture2D, f.TrailTexture);

            // --- Cull ---
            _cullShader.Use();
            SetFrustumUniforms(planes);
            _cullShader.SetUniform("TerrainHeightBound", heightBound);
            _cullShader.SetUniform("FrustumMargin", FrustumMargin);
            SetLodUniforms();
            _cullShader.SetUniform("TrailTexture", TrailUnit);
            _cullShader.SetUniform("TrailSimSize", _lod.MiddleFieldSize);
            _cullShader.SetUniform("TrailBoostDensity", _lod.TrailBoostDensity);

            _buckets.ResetCommands(_bladeHigh.VertexCount, _bladeMedium.VertexCount,
                _bladeMedium.VertexCount, _bladeRect.VertexCount);
            _buckets.BindForCull();
            for (int ring = 0; ring < _rings.Length; ring++)
                DispatchCull(ring);
            // Bucket buffers feed the vertex shaders, commands feed the indirect draws.
            _gl.MemoryBarrier(MemoryBarrierMask.ShaderStorageBarrierBit | MemoryBarrierMask.CommandBarrierBit);

            // --- Draw ---
            TaaVelocity.Begin(_gl);

            _nearShader.Use();
            SetDrawUniforms(_nearShader, f);
            _nearShader.SetUniform("FieldSize", _lod.InnerFieldSize);
            // Near the fluid sim's wipe radius the near tier blends in the
            // trail sim, so the trampled path stays continuous to the bucket edge.
            _nearShader.SetUniform("FluidTexture", FluidUnit);
            _nearShader.SetUniform("TrailSimSize", _lod.MiddleFieldSize);
            _nearShader.SetUniform("FluidClearDist", _lod.InnerFieldSize * 0.48f);
            _nearShader.SetUniform("SimCenter", f.SimCenter);
            _buckets.BindBucketForDraw(0);
            _bladeHigh.RenderIndirect(_buckets.CommandBuffer, 0);

            _midShader.Use();
            SetDrawUniforms(_midShader, f);
            _midShader.SetUniform("FieldSize", _lod.MiddleFieldSize);
            _buckets.BindBucketForDraw(1);
            _bladeMedium.RenderIndirect(_buckets.CommandBuffer, 1);

            // The far tiers are static, but they still write velocity: the
            // depth fallback is exactly what fails on sub-pixel blades.
            _farShader.Use();
            SetDrawUniforms(_farShader, f);
            _buckets.BindBucketForDraw(2);
            _bladeMedium.RenderIndirect(_buckets.CommandBuffer, 2);
            _buckets.BindBucketForDraw(3);
            _bladeRect.RenderIndirect(_buckets.CommandBuffer, 3);

            TaaVelocity.End(_gl);
            _gl.ActiveTexture(TextureUnit.Texture0);

            if (StatsEnabled && --_statsCooldown <= 0)
            {
                _statsCooldown = 30;
                _buckets.RefreshCounts();
            }
        }

        private void SetDrawUniforms(ShaderProgram shader, in GrassFrame f)
        {
            shader.SetUniform("WindNoiseTex", WindNoiseUnit);
            shader.SetUniform("MacroNoiseTex", MacroNoiseUnit);
            shader.SetUniform("TrailTexture", TrailUnit);
            shader.SetUniform("TramplerPos", f.TramplerPos);
            shader.SetUniform("TramplerRadius", f.TramplerRadius);
            shader.SetUniform("TramplerStrength", f.TramplerStrength);
        }

        private unsafe void SetFrustumUniforms(Vector4[] planes)
        {
            _cullShader.SetUniform("UseFrustumCulling", planes != null ? 1 : 0);
            if (planes == null) return;
            for (int i = 0; i < 6; i++)
            {
                _planeData[i * 4 + 0] = planes[i].X;
                _planeData[i * 4 + 1] = planes[i].Y;
                _planeData[i * 4 + 2] = planes[i].Z;
                _planeData[i * 4 + 3] = planes[i].W;
            }
            int loc = _gl.GetUniformLocation(_cullShader.Handle, "FrustumPlanes");
            if (loc >= 0)
                fixed (float* p = _planeData) _gl.Uniform4(loc, 6, p);
        }

        /// <summary>The LOD curve and ring layout for the cull pass; CPU mirrors in GrassLodSettings.</summary>
        private void SetLodUniforms()
        {
            float tw = _lod.TransitionWidth;
            _cullShader.SetUniform("RingCapacities", new Vector4(
                _lod.InnerDensity, _lod.MiddleDensity, _lod.FarDensity, _lod.DistantDensity));
            _cullShader.SetUniform("RingFadeStart", new Vector4(
                _lod.InnerFieldSize * 0.5f - tw, _lod.MiddleFieldSize * 0.5f - tw,
                _lod.FarFieldSize * 0.5f - tw, _lod.DistantFieldSize * 0.5f - tw));
            _cullShader.SetUniform("RingFadeWidth", tw);

            _cullShader.SetUniform("CurveNearDensity", _lod.NearDensity);
            _cullShader.SetUniform("CurvePlateau", _lod.PlateauRadius);
            _cullShader.SetUniform("CurveExponent", _lod.FalloffExponent);
            _cullShader.SetUniform("CurveFarFloor", _lod.FarFloor);
            _cullShader.SetUniform("CurveMaxDistance", _lod.MaxDistance);

            _cullShader.SetUniform("WidthNear", _lod.WidthNear);
            _cullShader.SetUniform("CoverageFar", _lod.CoverageFar);
            _cullShader.SetUniform("CoverageFalloffStart", _lod.CoverageFalloffStart);
            _cullShader.SetUniform("WidthMax", _lod.WidthMax);

            _cullShader.SetUniform("BucketDistances", new Vector3(
                _lod.MeshNearDistance, _lod.MeshMidDistance, _lod.MeshFarDistance));
            _cullShader.SetUniform("BucketDither", _lod.MeshDither);
            _cullShader.SetUniform("BucketCapacities",
                _buckets.Capacity(0), _buckets.Capacity(1), _buckets.Capacity(2), _buckets.Capacity(3));
        }

        /// <summary>
        /// Runs the CPU pre-cull for all rings and uploads the cell lists. The
        /// reach and frustum arguments mirror what the cull shader is given.
        /// </summary>
        private unsafe void PrepareCullCells(Vector3 camPos, Vector3 centerPos, Vector4[] planes, float heightBound)
        {
            // Past the grass horizon the curve asks for nothing; the trail
            // boost can still add demand anywhere inside the trail sim.
            float demandReach = _lod.MaxDistance;
            if (_lod.TrailBoostDensity > 0f)
                demandReach = MathF.Max(demandReach, _lod.MiddleFieldSize * 0.5f);

            var center = new Vector2(centerPos.X, centerPos.Z);
            var camera = new Vector2(camPos.X, camPos.Z);
            for (int ring = 0; ring < _rings.Length; ring++)
            {
                float size = _lod.RingSize(ring);
                // A ring's capacity has faded to zero half a field out
                float reach = _lod.TransitionWidth > 0f ? MathF.Min(demandReach, size * 0.5f) : demandReach;
                _cellCuller.CullRing(ring, _rings[ring].CellStart, _rings[ring].FieldSize, size,
                    center, camera, reach, planes, FrustumMargin, heightBound);
            }

            _gl.BindBuffer(BufferTargetARB.UniformBuffer, _cullCellsUbo);
            fixed (uint* cells = _cellCuller.Cells)
                _gl.BufferData(BufferTargetARB.UniformBuffer, (nuint)(_cellCuller.Cells.Length * sizeof(uint)), cells, BufferUsageARB.StreamDraw);
            _gl.BindBuffer(BufferTargetARB.UniformBuffer, 0);
        }

        private void DispatchCull(int ring)
        {
            int cells = _cellCuller.ListedCells(ring);
            if (cells == 0) return;

            const int ringBytes = GrassCellCuller.RingStride * sizeof(uint);
            var field = _rings[ring];
            field.BindSource();
            _gl.BindBufferRange(BufferTargetARB.UniformBuffer, CullCellsBinding, _cullCellsUbo,
                ring * ringBytes, ringBytes);
            _cullShader.SetUniform("GroupsPerCell", field.GroupsPerCell);
            _cullShader.SetUniform("FieldSize", _lod.RingSize(ring));
            _cullShader.SetUniform("RingIndex", ring);
            _gl.DispatchCompute((uint)(cells * field.GroupsPerCell), 1, 1);
        }

        public void Dispose()
        {
            foreach (var ring in _rings) ring?.Dispose();
            _cullShader.Dispose();
            _buckets.Dispose();
            _gl.DeleteBuffer(_cullCellsUbo);
            _bladeHigh.Dispose();
            _bladeMedium.Dispose();
            _bladeRect.Dispose();
            _windNoise.Dispose();
            _macroNoise.Dispose();
            _nearShader.Dispose();
            _midShader.Dispose();
            _farShader.Dispose();
        }
    }

    /// <summary>Per-frame inputs to <see cref="GrassRenderer.Render"/>.</summary>
    public readonly struct GrassFrame
    {
        public Matrix4x4 View { get; init; }
        public Matrix4x4 Projection { get; init; }
        public Vector3 CameraPosition { get; init; }
        public Vector3 CenterPosition { get; init; }
        public float TerrainAmplitude { get; init; }
        public bool FrustumCulling { get; init; }
        public uint FluidTexture { get; init; }
        public uint TrailTexture { get; init; }
        public Vector2 SimCenter { get; init; }
        public Vector2 TramplerPos { get; init; }
        public float TramplerRadius { get; init; }
        public float TramplerStrength { get; init; }
    }
}
