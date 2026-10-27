using System;
using Silk.NET.OpenGL;

namespace SilkGrass.Grass
{
    public enum BladeShape { High, Medium, Rectangle }

    /// <summary>
    /// One grass blade as a triangle strip of unit height (attribute 0 =
    /// position, attribute 1 = normal). The vertex shader scales it per
    /// instance, billboards its width toward the camera and bends it.
    /// </summary>
    public sealed class GrassMesh : IDisposable
    {
        private const float HalfWidth = 0.02f;

        private readonly GL _gl;
        private uint _vao;
        private uint _vbo;

        public uint VertexCount { get; private set; }

        public GrassMesh(GL gl, BladeShape shape)
        {
            _gl = gl;
            float w = HalfWidth;
            float[] positions = shape switch
            {
                // Four segments narrowing to a point
                BladeShape.High => new[]
                {
                    -w, 0.00f,          w, 0.00f,
                    -w, 0.25f,          w, 0.25f,
                    -w * 0.9f, 0.50f,   w * 0.9f, 0.50f,
                    -w * 0.8f, 0.75f,   w * 0.8f, 0.75f,
                    0f, 1.00f,
                },
                // Two segments: root, bend point, tip
                BladeShape.Medium => new[]
                {
                    -w, 0.00f,          w, 0.00f,
                    -w * 0.9f, 0.50f,   w * 0.9f, 0.50f,
                    0f, 1.00f,
                },
                // No tip: a flat body that overlaps its neighbours well at
                // extreme distance, where blades are many times wider
                _ => new[]
                {
                    -w, 0.00f,          w, 0.00f,
                    -w * 0.9f, 1.00f,   w * 0.9f, 1.00f,
                },
            };
            Upload(positions);
        }

        private unsafe void Upload(float[] positions)
        {
            int count = positions.Length / 2;
            var vertices = new float[count * 6];
            for (int i = 0; i < count; i++)
            {
                vertices[i * 6 + 0] = positions[i * 2];
                vertices[i * 6 + 1] = positions[i * 2 + 1];
                vertices[i * 6 + 5] = 1f; // normal (0, 0, 1)
            }
            VertexCount = (uint)count;

            _vao = _gl.GenVertexArray();
            _gl.BindVertexArray(_vao);
            _vbo = _gl.GenBuffer();
            _gl.BindBuffer(BufferTargetARB.ArrayBuffer, _vbo);
            fixed (float* v = vertices)
                _gl.BufferData(BufferTargetARB.ArrayBuffer, (nuint)(vertices.Length * sizeof(float)), v, BufferUsageARB.StaticDraw);
            _gl.EnableVertexAttribArray(0);
            _gl.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, 6 * sizeof(float), (void*)0);
            _gl.EnableVertexAttribArray(1);
            _gl.VertexAttribPointer(1, 3, VertexAttribPointerType.Float, false, 6 * sizeof(float), (void*)(3 * sizeof(float)));
            _gl.BindVertexArray(0);
        }

        /// <summary>
        /// Draws with the instance count the cull pass wrote into an indirect
        /// command buffer; commandIndex picks the 16-byte command.
        /// </summary>
        public unsafe void RenderIndirect(uint indirectBuffer, int commandIndex)
        {
            _gl.BindVertexArray(_vao);
            _gl.BindBuffer(BufferTargetARB.DrawIndirectBuffer, indirectBuffer);
            _gl.DrawArraysIndirect(PrimitiveType.TriangleStrip, (void*)(commandIndex * 16));
            _gl.BindBuffer(BufferTargetARB.DrawIndirectBuffer, 0);
            _gl.BindVertexArray(0);
        }

        public void Dispose()
        {
            _gl.DeleteVertexArray(_vao);
            _gl.DeleteBuffer(_vbo);
        }
    }
}
