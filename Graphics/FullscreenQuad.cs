using System;
using Silk.NET.OpenGL;

namespace SilkGrass.Graphics
{
    /// <summary>Two triangles covering the screen: attribute 0 = NDC xy, attribute 1 = uv.</summary>
    public sealed class FullscreenQuad : IDisposable
    {
        private readonly GL _gl;
        private readonly uint _vao;
        private readonly uint _vbo;

        private static readonly float[] Vertices =
        {
            -1f,  1f, 0f, 1f,
            -1f, -1f, 0f, 0f,
             1f, -1f, 1f, 0f,
            -1f,  1f, 0f, 1f,
             1f, -1f, 1f, 0f,
             1f,  1f, 1f, 1f,
        };

        public unsafe FullscreenQuad(GL gl)
        {
            _gl = gl;
            _vao = _gl.GenVertexArray();
            _gl.BindVertexArray(_vao);
            _vbo = _gl.GenBuffer();
            _gl.BindBuffer(BufferTargetARB.ArrayBuffer, _vbo);
            fixed (float* v = Vertices)
                _gl.BufferData(BufferTargetARB.ArrayBuffer, (nuint)(Vertices.Length * sizeof(float)), v, BufferUsageARB.StaticDraw);
            _gl.EnableVertexAttribArray(0);
            _gl.VertexAttribPointer(0, 2, VertexAttribPointerType.Float, false, 4 * sizeof(float), (void*)0);
            _gl.EnableVertexAttribArray(1);
            _gl.VertexAttribPointer(1, 2, VertexAttribPointerType.Float, false, 4 * sizeof(float), (void*)(2 * sizeof(float)));
            _gl.BindVertexArray(0);
        }

        public void Render()
        {
            _gl.BindVertexArray(_vao);
            _gl.DrawArrays(PrimitiveType.Triangles, 0, 6);
            _gl.BindVertexArray(0);
        }

        public void Dispose()
        {
            _gl.DeleteVertexArray(_vao);
            _gl.DeleteBuffer(_vbo);
        }
    }
}
