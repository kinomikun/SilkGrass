using System;
using System.Numerics;
using Silk.NET.OpenGL;

namespace SilkGrass.Grass
{
    /// <summary>
    /// Output of the grass cull pass: four (mesh, feature) buckets, each with
    /// a compacted visible-instance buffer, plus ONE indirect command buffer
    /// holding the four DrawArraysIndirect commands back to back.
    ///
    ///   bucket 0: high mesh (9 verts) + full physics   (near)
    ///   bucket 1: medium mesh (5 verts) + trail physics (mid)
    ///   bucket 2: medium mesh + per-vertex colour       (far)
    ///   bucket 3: rectangle (4 verts) + per-vertex colour (distant)
    ///
    /// grass_cull.comp routes every surviving blade from every ring into a
    /// bucket by dithered distance and bumps that bucket's instanceCount. The
    /// unused baseInstance field of each command doubles as an overflow
    /// counter: blades dropped because the bucket buffer was full
    /// (gl_InstanceID ignores baseInstance, so the draw is unaffected).
    /// </summary>
    public sealed class GrassBucketSet : IDisposable
    {
        public const int BucketCount = 4;

        // SSBO bindings in grass_cull.comp: visible buffers 1..4, commands 5
        // (binding 0 is the ring's source buffer).
        private const uint FirstVisibleBinding = 1;
        private const uint CommandBinding = 5;

        private readonly GL _gl;
        private readonly uint[] _visible = new uint[BucketCount];
        private readonly int[] _capacities = new int[BucketCount];
        private readonly uint _commands;

        // Counter readback goes through a staging copy read one refresh
        // later: reading the command buffer itself makes the driver move it to
        // host memory, which slows the next frames' indirect draws for a while.
        private readonly uint _staging;
        private bool _stagingFilled;

        public uint CommandBuffer => _commands;
        public int Capacity(int bucket) => _capacities[bucket];

        /// <summary>Visible and overflow counts from the last <see cref="RefreshCounts"/>.</summary>
        public readonly (uint Visible, uint Overflow)[] Counts = new (uint, uint)[BucketCount];

        public unsafe GrassBucketSet(GL gl, int[] capacities)
        {
            _gl = gl;
            Array.Copy(capacities, _capacities, BucketCount);

            for (int i = 0; i < BucketCount; i++)
            {
                _visible[i] = _gl.GenBuffer();
                _gl.BindBuffer(BufferTargetARB.ShaderStorageBuffer, _visible[i]);
                _gl.BufferData(BufferTargetARB.ShaderStorageBuffer,
                    (nuint)(_capacities[i] * sizeof(Vector4)), null, BufferUsageARB.DynamicCopy);
            }
            _gl.BindBuffer(BufferTargetARB.ShaderStorageBuffer, 0);

            _commands = _gl.GenBuffer();
            _gl.BindBuffer(BufferTargetARB.DrawIndirectBuffer, _commands);
            uint* zero = stackalloc uint[BucketCount * 4];
            for (int i = 0; i < BucketCount * 4; i++) zero[i] = 0;
            _gl.BufferData(BufferTargetARB.DrawIndirectBuffer, (nuint)(BucketCount * 16), zero, BufferUsageARB.DynamicDraw);
            _gl.BindBuffer(BufferTargetARB.DrawIndirectBuffer, 0);

            _staging = _gl.GenBuffer();
            _gl.BindBuffer(BufferTargetARB.CopyWriteBuffer, _staging);
            _gl.BufferData(BufferTargetARB.CopyWriteBuffer, (nuint)(BucketCount * 16), null, BufferUsageARB.StreamRead);
            _gl.BindBuffer(BufferTargetARB.CopyWriteBuffer, 0);
        }

        /// <summary>
        /// Resets the four commands for this frame: per-bucket vertex counts,
        /// zero instances, zero overflow.
        /// </summary>
        public unsafe void ResetCommands(uint verts0, uint verts1, uint verts2, uint verts3)
        {
            uint* cmds = stackalloc uint[BucketCount * 4];
            Span<uint> verts = stackalloc uint[] { verts0, verts1, verts2, verts3 };
            for (int i = 0; i < BucketCount; i++)
            {
                cmds[i * 4 + 0] = verts[i]; // vertices per instance
                cmds[i * 4 + 1] = 0;        // instanceCount, written by grass_cull.comp
                cmds[i * 4 + 2] = 0;        // firstVertex
                cmds[i * 4 + 3] = 0;        // baseInstance, used as the overflow counter
            }
            _gl.BindBuffer(BufferTargetARB.DrawIndirectBuffer, _commands);
            _gl.BufferSubData(BufferTargetARB.DrawIndirectBuffer, 0, (nuint)(BucketCount * 16), cmds);
            _gl.BindBuffer(BufferTargetARB.DrawIndirectBuffer, 0);
        }

        /// <summary>Binds the visible buffers and the command buffer for grass_cull.comp.</summary>
        public void BindForCull()
        {
            for (int i = 0; i < BucketCount; i++)
                _gl.BindBufferBase(BufferTargetARB.ShaderStorageBuffer, FirstVisibleBinding + (uint)i, _visible[i]);
            _gl.BindBufferBase(BufferTargetARB.ShaderStorageBuffer, CommandBinding, _commands);
        }

        /// <summary>Binds one bucket's visible-instance buffer for drawing (SSBO 0).</summary>
        public void BindBucketForDraw(int bucket) =>
            _gl.BindBufferBase(BufferTargetARB.ShaderStorageBuffer, 0, _visible[bucket]);

        /// <summary>
        /// Publishes the counts copied at the previous call into
        /// <see cref="Counts"/>, then copies this frame's commands for the
        /// next call. Call after the grass draws, every few dozen frames.
        /// </summary>
        public unsafe void RefreshCounts()
        {
            if (_stagingFilled)
            {
                uint* vals = stackalloc uint[BucketCount * 4];
                _gl.BindBuffer(BufferTargetARB.CopyReadBuffer, _staging);
                _gl.GetBufferSubData(BufferTargetARB.CopyReadBuffer, 0, (nuint)(BucketCount * 16), vals);
                _gl.BindBuffer(BufferTargetARB.CopyReadBuffer, 0);
                for (int i = 0; i < BucketCount; i++)
                    Counts[i] = (vals[i * 4 + 1], vals[i * 4 + 3]);
            }

            _gl.BindBuffer(BufferTargetARB.CopyReadBuffer, _commands);
            _gl.BindBuffer(BufferTargetARB.CopyWriteBuffer, _staging);
            _gl.CopyBufferSubData(CopyBufferSubDataTarget.CopyReadBuffer, CopyBufferSubDataTarget.CopyWriteBuffer,
                0, 0, (nuint)(BucketCount * 16));
            _gl.BindBuffer(BufferTargetARB.CopyReadBuffer, 0);
            _gl.BindBuffer(BufferTargetARB.CopyWriteBuffer, 0);
            _stagingFilled = true;
        }

        public void Dispose()
        {
            for (int i = 0; i < BucketCount; i++) _gl.DeleteBuffer(_visible[i]);
            _gl.DeleteBuffer(_commands);
            _gl.DeleteBuffer(_staging);
        }
    }
}
