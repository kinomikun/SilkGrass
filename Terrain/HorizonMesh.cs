using System;
using System.Collections.Generic;
using Silk.NET.OpenGL;

namespace SilkGrass.Terrain
{
    /// <summary>
    /// Flat ring (annulus) around the origin that carries the terrain carpet
    /// from the end of the grass out to the horizon. The horizon shader centres
    /// it on the camera and displaces it by the terrain height. Rings are
    /// spaced quadratically, so detail concentrates near the inner edge.
    /// Positions only.
    /// </summary>
    public sealed class HorizonMesh : IDisposable
    {
        private const int Segments = 200;
        private const int Rings = 40;

        private readonly GL _gl;
        private uint _vao;
        private uint _vbo;
        private uint _ebo;
        private int _indexCount;

        public HorizonMesh(GL gl, float innerRadius, float outerRadius)
        {
            _gl = gl;
            Generate(innerRadius, outerRadius);
        }

        private unsafe void Generate(float inner, float outer)
        {
            var vertices = new List<float>();
            var indices = new List<uint>();
            uint row = Segments + 1;

            for (int r = 0; r <= Rings; r++)
            {
                float t = (float)r / Rings;
                float radius = inner + (outer - inner) * t * t;
                for (int s = 0; s <= Segments; s++)
                {
                    float angle = (float)s / Segments * MathF.PI * 2f;
                    vertices.Add(MathF.Sin(angle) * radius);
                    vertices.Add(0f);
                    vertices.Add(MathF.Cos(angle) * radius);

                    if (r < Rings && s < Segments)
                    {
                        uint i = (uint)(r * row + s);
                        indices.Add(i); indices.Add(i + row); indices.Add(i + 1);
                        indices.Add(i + 1); indices.Add(i + row); indices.Add(i + row + 1);
                    }
                }
            }

            _indexCount = indices.Count;
            _vao = _gl.GenVertexArray();
            _gl.BindVertexArray(_vao);
            _vbo = _gl.GenBuffer();
            _gl.BindBuffer(BufferTargetARB.ArrayBuffer, _vbo);
            fixed (float* v = vertices.ToArray())
                _gl.BufferData(BufferTargetARB.ArrayBuffer, (nuint)(vertices.Count * sizeof(float)), v, BufferUsageARB.StaticDraw);
            _ebo = _gl.GenBuffer();
            _gl.BindBuffer(BufferTargetARB.ElementArrayBuffer, _ebo);
            fixed (uint* i = indices.ToArray())
                _gl.BufferData(BufferTargetARB.ElementArrayBuffer, (nuint)(indices.Count * sizeof(uint)), i, BufferUsageARB.StaticDraw);
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
            _gl.DeleteBuffer(_ebo);
        }
    }
}
