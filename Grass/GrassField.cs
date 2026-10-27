using System;
using Silk.NET.OpenGL;

namespace SilkGrass.Grass
{
    /// <summary>
    /// One capacity ring: a static buffer of candidate blade positions,
    /// scattered over a square that the cull pass wraps around the field
    /// centre (toroidally), so a finite buffer covers an endless meadow.
    /// grass_cull.comp reads it and compacts the survivors into the shared
    /// bucket buffers (<see cref="GrassBucketSet"/>). The buffer is sorted
    /// into grid cells, so the cull pass can leave whole cells out
    /// (<see cref="GrassCellCuller"/>).
    /// </summary>
    public sealed class GrassField : IDisposable
    {
        /// <summary>Largest per-blade scale the scatter rolls; bounds how big a blade can get.</summary>
        public const float MaxBladeScale = 2.0f;

        private readonly GL _gl;
        private uint _ssbo;

        public int InstanceCount { get; }

        /// <summary>Side of the square the instances were scattered over.</summary>
        public float FieldSize { get; }

        /// <summary>CellStart[c] .. CellStart[c + 1] = grid cell c's instances in the buffer.</summary>
        public int[] CellStart { get; private set; }

        /// <summary>Compute work groups the cull pass runs per listed cell.</summary>
        public int GroupsPerCell { get; private set; }

        public GrassField(GL gl, float fieldSize, int count, int seed)
        {
            _gl = gl;
            InstanceCount = count;
            FieldSize = fieldSize;
            CreateInstanceBuffer(seed);
        }

        private unsafe void CreateInstanceBuffer(int seed)
        {
            var rng = new Random(seed);
            // Three floats per instance (12-byte std430 stride): world X,
            // world Z, per-blade scale. Mirror: SrcInstances in grass_cull.comp.
            var data = new float[InstanceCount * 3];
            for (int i = 0; i < InstanceCount; i++)
            {
                data[i * 3 + 0] = (float)(rng.NextDouble() * FieldSize - FieldSize / 2);
                data[i * 3 + 1] = (float)(rng.NextDouble() * FieldSize - FieldSize / 2);
                data[i * 3 + 2] = 1.6f + (float)(rng.NextDouble() * 0.4f); // up to MaxBladeScale
            }

            // Cell by cell, so a cell is one contiguous run the cull pass can
            // dispatch or skip. Order within the buffer means nothing else:
            // every blade is drawn from its own X/Z.
            data = GrassCellCuller.SortIntoCells(data, FieldSize, out int[] cellStart);
            CellStart = cellStart;
            GroupsPerCell = GrassCellCuller.GroupsPerCell(cellStart);

            _ssbo = _gl.GenBuffer();
            _gl.BindBuffer(BufferTargetARB.ShaderStorageBuffer, _ssbo);
            fixed (float* p = data)
                _gl.BufferData(BufferTargetARB.ShaderStorageBuffer, (nuint)(data.Length * sizeof(float)), p, BufferUsageARB.StaticDraw);
            _gl.BindBuffer(BufferTargetARB.ShaderStorageBuffer, 0);
        }

        /// <summary>Binds the instance buffer for the cull pass (SSBO binding 0).</summary>
        public void BindSource() => _gl.BindBufferBase(BufferTargetARB.ShaderStorageBuffer, 0, _ssbo);

        public void Dispose() => _gl.DeleteBuffer(_ssbo);
    }
}
