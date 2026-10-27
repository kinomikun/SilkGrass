using System;
using Silk.NET.OpenGL;
using SilkGrass.Graphics;
using SilkGrass.Settings;

namespace SilkGrass.Rendering
{
    /// <summary>
    /// Procedural sky on a cube around the camera (shaders/sky). Drawn first
    /// without writing depth, so the post-process sees sky pixels at the far plane.
    /// </summary>
    public sealed class SkyRenderer : IDisposable
    {
        private readonly GL _gl;
        private readonly ShaderProgram _shader;
        private readonly uint _vao;
        private readonly uint _vbo;

        public unsafe SkyRenderer(GL gl)
        {
            _gl = gl;
            _shader = new ShaderProgram(gl, "sky/sky.vert", "sky/sky.frag");

            float[] cube =
            {
                -1, 1,-1,  -1,-1,-1,   1,-1,-1,   1,-1,-1,   1, 1,-1,  -1, 1,-1,
                -1,-1, 1,  -1,-1,-1,  -1, 1,-1,  -1, 1,-1,  -1, 1, 1,  -1,-1, 1,
                 1,-1,-1,   1,-1, 1,   1, 1, 1,   1, 1, 1,   1, 1,-1,   1,-1,-1,
                -1,-1, 1,  -1, 1, 1,   1, 1, 1,   1, 1, 1,   1,-1, 1,  -1,-1, 1,
                -1, 1,-1,   1, 1,-1,   1, 1, 1,   1, 1, 1,  -1, 1, 1,  -1, 1,-1,
                -1,-1,-1,  -1,-1, 1,   1,-1,-1,   1,-1,-1,  -1,-1, 1,   1,-1, 1,
            };
            _vao = _gl.GenVertexArray();
            _gl.BindVertexArray(_vao);
            _vbo = _gl.GenBuffer();
            _gl.BindBuffer(BufferTargetARB.ArrayBuffer, _vbo);
            fixed (float* v = cube)
                _gl.BufferData(BufferTargetARB.ArrayBuffer, (nuint)(cube.Length * sizeof(float)), v, BufferUsageARB.StaticDraw);
            _gl.EnableVertexAttribArray(0);
            _gl.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, 3 * sizeof(float), (void*)0);
            _gl.BindVertexArray(0);
        }

        public void Render(SceneSettings.SkySettings sky)
        {
            _gl.DepthMask(false);
            _shader.Use();
            _shader.SetUniform("ZenithColor", sky.ZenithColor);
            _shader.SetUniform("GradientPower", sky.GradientPower);
            _shader.SetUniform("SkyBrightness", sky.Brightness);
            _shader.SetUniform("HorizonFog", sky.HorizonFog);
            _shader.SetUniform("SunDiscVisible", sky.SunDiscVisible ? 1f : 0f);
            _shader.SetUniform("CloudCoverage", sky.CloudCoverage);
            _shader.SetUniform("CloudScale", sky.CloudScale);
            _shader.SetUniform("CloudSpeed", sky.CloudSpeed);
            _shader.SetUniform("CloudLitColor", sky.CloudLitColor);
            _shader.SetUniform("CloudShadeColor", sky.CloudShadeColor);

            _shader.SetUniform("StormAmount", sky.StormAmount);
            _shader.SetUniform("StormCoverage", sky.StormCoverage);
            _shader.SetUniform("StormScale", sky.StormScale);
            _shader.SetUniform("StormSpeed", sky.StormSpeed);
            _shader.SetUniform("StormHeight", sky.StormHeight);
            _shader.SetUniform("StormDarkColor", sky.StormDarkColor);
            _shader.SetUniform("StormLightColor", sky.StormLightColor);
            _shader.SetUniform("StormVirgaAmount", sky.StormVirgaAmount);
            _shader.SetUniform("StormVirgaDensity", sky.StormVirgaDensity);
            _shader.SetUniform("StormVirgaSpeed", sky.StormVirgaSpeed);
            _shader.SetUniform("StormHazeLift", sky.StormHazeLift);
            _shader.SetUniform("StormHazeTint", sky.StormHazeTint);

            _gl.BindVertexArray(_vao);
            _gl.DrawArrays(PrimitiveType.Triangles, 0, 36);
            _gl.BindVertexArray(0);
            _gl.DepthMask(true);
        }

        public void Dispose()
        {
            _gl.DeleteVertexArray(_vao);
            _gl.DeleteBuffer(_vbo);
            _shader.Dispose();
        }
    }
}
