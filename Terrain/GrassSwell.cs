using System;
using System.Numerics;
using Silk.NET.OpenGL;

namespace SilkGrass.Terrain
{
    /// <summary>
    /// Sea-of-grass swell: long rolling waves that lift and drop the grass and
    /// the ground carpet under it, while the terrain itself stays still.
    ///
    /// A layer on top of the terrain, never part of it: GetTerrainHeight stays
    /// the still ground everywhere. The shaders that ride the swell add
    /// GetSwell() from shaders/common_swell.glsl; this class is its CPU mirror
    /// (keep the two in sync), owns its uniform block and integrates the wave
    /// phases, so changing the speed never makes the sea jump.
    ///
    /// Three vertical-only cosine trains: a main swell with second-harmonic
    /// crest sharpening plus two shorter cross trains. Each travels at the
    /// deep-water speed for its wavelength (omega^2 = g k).
    /// </summary>
    public sealed class GrassSwell : IDisposable
    {
        public const uint UniformBinding = 5;

        public float Wavelength = 125f;     // metres from crest to crest of the main swell
        public float Speed = 1f;            // 1 = as fast as real deep-water waves of that length
        public float DirectionDeg = 205f;   // where the main swell travels: 0 = +X, 90 = +Z
        public float Choppiness = 1.5f;     // strength of the two shorter cross trains
        public float CrestSharpness = 0.5f; // 0 = smooth sine, up to 0.5 = peaked crests, flat troughs

        // The cross trains relative to the main swell: heading offset
        // (degrees), wavelength factor, height factor at Choppiness 1.
        private static readonly (float Heading, float Length, float Height)[] CrossTrains =
        {
            (-38f, 0.61f, 0.45f),
            (51f, 0.37f, 0.25f),
        };

        private const int WaveCount = 3;
        private const float Gravity = 9.81f;

        private readonly GL _gl;
        private readonly uint _ubo;
        private readonly Vector2[] _waveK = new Vector2[WaveCount];
        private readonly float[] _amplitude = new float[WaveCount];
        private readonly double[] _phase = { 0.0, 1.7, 4.1 };
        private readonly double[] _prevPhase = { 0.0, 1.7, 4.1 };
        private readonly float[] _uboData = new float[(WaveCount + 2) * 4];
        private bool _active;
        private float _sharpness;

        public unsafe GrassSwell(GL gl)
        {
            _gl = gl;
            _ubo = _gl.GenBuffer();
            _gl.BindBuffer(BufferTargetARB.UniformBuffer, _ubo);
            _gl.BufferData(BufferTargetARB.UniformBuffer, (nuint)(_uboData.Length * sizeof(float)), null, BufferUsageARB.DynamicDraw);
            _gl.BindBufferBase(BufferTargetARB.UniformBuffer, UniformBinding, _ubo);
            _gl.BindBuffer(BufferTargetARB.UniformBuffer, 0);
        }

        /// <summary>The farthest the swell can lift or drop the surface, in metres.</summary>
        public float MaxAmplitude => _active ? _amplitude[0] * (1f + _sharpness) + _amplitude[1] + _amplitude[2] : 0f;

        /// <summary>
        /// Advances the wave phases and rebuilds the trains from the tunables.
        /// Once per frame.
        /// </summary>
        /// <param name="height">Metres the main swell lifts and drops the surface; 0 = still.</param>
        public void Advance(float deltaTime, float height)
        {
            _active = height > 0f && Wavelength > 1f;
            _sharpness = Math.Clamp(CrestSharpness, 0f, 0.5f);
            float chop = MathF.Max(0f, Choppiness);

            for (int i = 0; i < WaveCount; i++)
            {
                float heading = DirectionDeg, length = MathF.Max(Wavelength, 1f), h = height;
                if (i > 0)
                {
                    var train = CrossTrains[i - 1];
                    heading += train.Heading;
                    length *= train.Length;
                    h *= train.Height * chop;
                }

                float k = 2f * MathF.PI / length;
                float rad = heading * MathF.PI / 180f;
                _waveK[i] = new Vector2(MathF.Cos(rad), MathF.Sin(rad)) * k;
                _amplitude[i] = _active ? h : 0f;

                double omega = Math.Sqrt(Gravity * k) * Speed;
                _prevPhase[i] = _phase[i];
                _phase[i] = (_phase[i] + omega * Math.Max(0f, deltaTime)) % (2.0 * Math.PI);
            }
        }

        /// <summary>Swell height at a world position in metres; CPU mirror of GetSwell().</summary>
        public float HeightAt(float x, float z)
        {
            if (!_active) return 0f;
            double c = Math.Cos(_waveK[0].X * (double)x + _waveK[0].Y * (double)z - _phase[0]);
            double h = _amplitude[0] * (c + _sharpness * (2.0 * c * c - 1.0));
            for (int i = 1; i < WaveCount; i++)
                h += _amplitude[i] * Math.Cos(_waveK[i].X * (double)x + _waveK[i].Y * (double)z - _phase[i]);
            return (float)h;
        }

        /// <summary>Uploads this frame's trains (after Advance).</summary>
        public unsafe void Upload()
        {
            // std140 SwellData: vec4 SwellWave[3], vec4 SwellPrevPhase, vec4 SwellParams
            for (int i = 0; i < WaveCount; i++)
            {
                _uboData[i * 4 + 0] = _waveK[i].X;
                _uboData[i * 4 + 1] = _waveK[i].Y;
                _uboData[i * 4 + 2] = (float)_phase[i];
                _uboData[i * 4 + 3] = _amplitude[i];
                _uboData[WaveCount * 4 + i] = (float)_prevPhase[i];
            }
            _uboData[WaveCount * 4 + 3] = 0f;
            int p = (WaveCount + 1) * 4;
            _uboData[p + 0] = _sharpness;
            _uboData[p + 1] = 0f;
            _uboData[p + 2] = 0f;
            _uboData[p + 3] = _active ? 1f : 0f;

            _gl.BindBuffer(BufferTargetARB.UniformBuffer, _ubo);
            fixed (float* data = _uboData)
                _gl.BufferSubData(BufferTargetARB.UniformBuffer, 0, (nuint)(_uboData.Length * sizeof(float)), data);
            _gl.BindBuffer(BufferTargetARB.UniformBuffer, 0);
            _gl.BindBufferBase(BufferTargetARB.UniformBuffer, UniformBinding, _ubo);
        }

        public void Dispose() => _gl.DeleteBuffer(_ubo);
    }
}
