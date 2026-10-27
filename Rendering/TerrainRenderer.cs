using System;
using System.Numerics;
using Silk.NET.OpenGL;
using SilkGrass.Graphics;
using SilkGrass.Terrain;

namespace SilkGrass.Rendering
{
    /// <summary>
    /// The ground under the grass (a grid following the camera) and the
    /// terrain carpet from the end of the grass out to the horizon.
    /// </summary>
    public sealed class TerrainRenderer : IDisposable
    {
        /// <summary>Half the side of the ground grid.</summary>
        public const float GroundHalfSize = 1500f;
        /// <summary>Where the horizon carpet ends; the far clip plane must lie beyond it.</summary>
        public const float HorizonOuterRadius = 3000f;

        private const int FluidUnit = 0;
        private const int TrailUnit = 1;
        private const int MacroNoiseUnit = 5;

        private readonly GL _gl;
        private readonly ShaderProgram _groundShader;
        private readonly ShaderProgram _horizonShader;
        private readonly GroundMesh _groundMesh;
        private HorizonMesh _horizonMesh;
        private float _horizonInnerRadius;

        public TerrainRenderer(GL gl)
        {
            _gl = gl;
            _groundShader = new ShaderProgram(gl, "terrain/ground.vert", "terrain/ground.frag");
            _horizonShader = new ShaderProgram(gl, "terrain/horizon.vert", "terrain/horizon.frag");
            // 256 cells over 3 km = ~11.7 m, just under the shader's 12 m grid snap
            _groundMesh = new GroundMesh(gl, 256);
        }

        /// <summary>Draws the horizon carpet. innerRadius: where the grass ends (the distant ring's edge).</summary>
        public void RenderHorizon(float innerRadius, Action<int> bindMacroNoise)
        {
            if (_horizonMesh == null || innerRadius != _horizonInnerRadius)
            {
                _horizonMesh?.Dispose();
                _horizonMesh = new HorizonMesh(_gl, innerRadius, HorizonOuterRadius);
                _horizonInnerRadius = innerRadius;
            }

            _horizonShader.Use();
            bindMacroNoise(MacroNoiseUnit);
            _horizonShader.SetUniform("MacroNoiseTex", MacroNoiseUnit);
            _horizonShader.SetUniform("OuterRadius", HorizonOuterRadius);
            TaaVelocity.Begin(_gl);
            _horizonMesh.Render();
            TaaVelocity.End(_gl);
            _gl.ActiveTexture(TextureUnit.Texture0);
        }

        public void RenderGround(Vector3 cameraPosition, uint fluidTexture, uint trailTexture,
            Vector2 simCenter, float innerFieldSize, float middleFieldSize)
        {
            _groundShader.Use();
            _gl.ActiveTexture(TextureUnit.Texture0 + FluidUnit);
            _gl.BindTexture(TextureTarget.Texture2D, fluidTexture);
            _gl.ActiveTexture(TextureUnit.Texture0 + TrailUnit);
            _gl.BindTexture(TextureTarget.Texture2D, trailTexture);
            _gl.ActiveTexture(TextureUnit.Texture0);
            _groundShader.SetUniform("FluidTexture", FluidUnit);
            _groundShader.SetUniform("TrailTexture", TrailUnit);
            _groundShader.SetUniform("SimCenter", simCenter);
            _groundShader.SetUniform("InnerFieldSize", innerFieldSize);
            _groundShader.SetUniform("MiddleFieldSize", middleFieldSize);

            // Half a metre below the terrain: the soil sits under the blade roots
            var model = Matrix4x4.CreateScale(GroundHalfSize)
                      * Matrix4x4.CreateTranslation(cameraPosition.X, -0.5f, cameraPosition.Z);
            _groundShader.SetUniform("Model", model);
            _groundMesh.Render();
        }

        public void Dispose()
        {
            _groundShader.Dispose();
            _horizonShader.Dispose();
            _groundMesh.Dispose();
            _horizonMesh?.Dispose();
        }
    }
}
