using System;
using System.Numerics;
using Silk.NET.OpenGL;
using SilkGrass.Graphics;
using SilkGrass.Settings;

namespace SilkGrass.Rendering
{
    /// <summary>
    /// Falling rain: instanced streaks in a box that follows the camera. Each
    /// drop's position is a closed-form function of time (shaders/rain), so
    /// nothing is simulated on the CPU. The intensity decides how many of the
    /// drops draw. Alpha-blended, so it writes no depth and no velocity.
    /// </summary>
    public sealed class RainRenderer : IDisposable
    {
        private const int DropCount = 80_000;
        private static readonly Vector3 BoxSize = new(200f, 80f, 200f);

        private readonly GL _gl;
        private readonly ShaderProgram _shader;
        private readonly uint _vao;
        private readonly uint _quadVbo;
        private readonly uint _instanceVbo;

        public unsafe RainRenderer(GL gl)
        {
            _gl = gl;
            _shader = new ShaderProgram(gl, "rain/rain.vert", "rain/rain.frag");

            float[] quad = { -0.5f, -0.5f, 0f, 0.5f, -0.5f, 0f, -0.5f, 0.5f, 0f, 0.5f, 0.5f, 0f };
            var instances = new float[DropCount * 4];
            var rng = new Random(42);
            for (int i = 0; i < instances.Length; i++) instances[i] = (float)rng.NextDouble();

            _vao = _gl.GenVertexArray();
            _gl.BindVertexArray(_vao);
            _quadVbo = _gl.GenBuffer();
            _gl.BindBuffer(BufferTargetARB.ArrayBuffer, _quadVbo);
            fixed (float* q = quad)
                _gl.BufferData(BufferTargetARB.ArrayBuffer, (nuint)(quad.Length * sizeof(float)), q, BufferUsageARB.StaticDraw);
            _gl.EnableVertexAttribArray(0);
            _gl.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, 3 * sizeof(float), (void*)0);

            _instanceVbo = _gl.GenBuffer();
            _gl.BindBuffer(BufferTargetARB.ArrayBuffer, _instanceVbo);
            fixed (float* d = instances)
                _gl.BufferData(BufferTargetARB.ArrayBuffer, (nuint)(instances.Length * sizeof(float)), d, BufferUsageARB.StaticDraw);
            _gl.EnableVertexAttribArray(1);
            _gl.VertexAttribPointer(1, 4, VertexAttribPointerType.Float, false, 4 * sizeof(float), (void*)0);
            _gl.VertexAttribDivisor(1, 1);
            _gl.BindVertexArray(0);
        }

        public void Render(SceneSettings.WeatherSettings weather)
        {
            if (!weather.RainEnabled || weather.RainIntensity <= 0.001f) return;

            _gl.DepthMask(false);
            _gl.Enable(EnableCap.Blend);
            _gl.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);

            _shader.Use();
            _shader.SetUniform("BoxSize", BoxSize);
            _shader.SetUniform("RainSpeed", weather.RainSpeed);
            _shader.SetUniform("RainColor", weather.RainColor);
            _shader.SetUniform("RainOpacity", weather.RainOpacity);

            _gl.BindVertexArray(_vao);
            _gl.DrawArraysInstanced(PrimitiveType.TriangleStrip, 0, 4, DropCount);
            _gl.BindVertexArray(0);

            _gl.Disable(EnableCap.Blend);
            _gl.DepthMask(true);
        }

        public void Dispose()
        {
            _gl.DeleteVertexArray(_vao);
            _gl.DeleteBuffer(_quadVbo);
            _gl.DeleteBuffer(_instanceVbo);
            _shader.Dispose();
        }
    }
}
