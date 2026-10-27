using System;
using Silk.NET.OpenGL;

namespace SilkGrass.Terrain
{
    /// <summary>
    /// Subdivided XZ grid spanning [-1, 1], scaled and displaced by the ground
    /// shader. Positions only.
    /// </summary>
    public sealed class GroundMesh : IDisposable
    {
        private readonly GL _gl;
        private uint _vao;
        private uint _vbo;
        private uint _ibo;
        private int _indexCount;

        public GroundMesh(GL gl, int subdivisions)
        {
            _gl = gl;
            Generate(subdivisions);
        }

        private unsafe void Generate(int subdivisions)
        {
            int perSide = subdivisions + 1;
            var vertices = new float[perSide * perSide * 3];
            for (int z = 0; z < perSide; z++)
            for (int x = 0; x < perSide; x++)
            {
                int i = (z * perSide + x) * 3;
                vertices[i + 0] = (float)x / subdivisions * 2f - 1f;
                vertices[i + 1] = 0f;
                vertices[i + 2] = (float)z / subdivisions * 2f - 1f;
            }

            _indexCount = subdivisions * subdivisions * 6;
            var indices = new uint[_indexCount];
            int idx = 0;
            for (int z = 0; z < subdivisions; z++)
            for (int x = 0; x < subdivisions; x++)
            {
                uint tl = (uint)(z * perSide + x);
                uint tr = tl + 1;
                uint bl = (uint)((z + 1) * perSide + x);
                uint br = bl + 1;
                indices[idx++] = tl; indices[idx++] = bl; indices[idx++] = tr;
                indices[idx++] = tr; indices[idx++] = bl; indices[idx++] = br;
            }

            _vao = _gl.GenVertexArray();
            _gl.BindVertexArray(_vao);
            _vbo = _gl.GenBuffer();
            _gl.BindBuffer(BufferTargetARB.ArrayBuffer, _vbo);
            fixed (float* v = vertices)
                _gl.BufferData(BufferTargetARB.ArrayBuffer, (nuint)(vertices.Length * sizeof(float)), v, BufferUsageARB.StaticDraw);
            _ibo = _gl.GenBuffer();
            _gl.BindBuffer(BufferTargetARB.ElementArrayBuffer, _ibo);
            fixed (uint* i = indices)
                _gl.BufferData(BufferTargetARB.ElementArrayBuffer, (nuint)(indices.Length * sizeof(uint)), i, BufferUsageARB.StaticDraw);
            _gl.EnableVertexAttribArray(0);
            _gl.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, 3 * sizeof(float), (void*)0);
            _gl.BindVertexArray(0);
        }

        public unsafe void Render()
        {
            _gl.BindVertexArray(_vao);
            _gl.DrawElements(PrimitiveType.Triangles, (uint)_indexCount, DrawElementsType.UnsignedInt, (void*)0);
            _gl.BindVertexArray(0);
        }

        public void Dispose()
        {
            _gl.DeleteVertexArray(_vao);
            _gl.DeleteBuffer(_vbo);
            _gl.DeleteBuffer(_ibo);
        }
    }
}
