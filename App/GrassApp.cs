using System;
using System.IO;
using System.Numerics;
using ImGuiNET;
using Silk.NET.Input;
using Silk.NET.Maths;
using Silk.NET.OpenGL;
using Silk.NET.OpenGL.Extensions.ImGui;
using Silk.NET.Windowing;
using SilkGrass.Cameras;
using SilkGrass.Graphics;
using SilkGrass.Rendering;
using SilkGrass.Settings;
using SilkGrass.UI;

namespace SilkGrass.App
{
    /// <summary>
    /// The window: input, the free-fly camera, the scene and the tuning panel.
    /// </summary>
    public sealed class GrassApp
    {
        // Lowest the camera may go above the grass surface
        private const float GroundClearance = 0.6f;
        private const float StartHeight = 6f;

        private readonly IWindow _window;
        private readonly AppOptions _options;
        private readonly SceneSettings _scene = new SceneSettings();
        private readonly RenderSettings _render = new RenderSettings();
        private readonly FreeFlyCamera _camera = new FreeFlyCamera();

        private GL _gl;
        private IInputContext _input;
        private ImGuiController _imGui;
        private SceneRenderer _renderer;
        private TuningPanel _panel;

        private double _time;
        private int _frame;
        private bool _looking;
        private Vector2 _lastMouse;
        private bool _screenshotRequested;
        private Vector2? _brush;

        public GrassApp(IWindow window, AppOptions options)
        {
            _window = window;
            _options = options;
            _window.Load += OnLoad;
            _window.Update += OnUpdate;
            _window.Render += OnRender;
            _window.Closing += OnClosing;
        }

        private void OnLoad()
        {
            _gl = GL.GetApi(_window);
#if DEBUG
            GlDebug.Install(_gl);
#endif
            Log.Info($"[App] {_gl.GetStringS(StringName.Renderer)} | OpenGL {_gl.GetStringS(StringName.Version)}");

            _input = _window.CreateInput();
            foreach (var keyboard in _input.Keyboards) keyboard.KeyDown += OnKeyDown;
            foreach (var mouse in _input.Mice)
            {
                mouse.MouseDown += OnMouseDown;
                mouse.MouseUp += OnMouseUp;
                mouse.Scroll += OnScroll;
            }
            _imGui = new ImGuiController(_gl, _window, _input);

            if (_options.AntiAliasing.HasValue) _render.AntiAliasing = _options.AntiAliasing.Value;
            if (_options.Fidelity.HasValue) _render.GrassFidelity = _options.Fidelity.Value;

            var fb = _window.FramebufferSize;
            _renderer = new SceneRenderer(_gl, _scene, _render, fb.X, fb.Y);
            ShaderProgram.LogSummary();

            _panel = new TuningPanel(_scene, _render, _renderer, _camera)
            {
                Visible = _options.ShowPanel,
                ResetCamera = ResetCamera,
            };
            ResetCamera();

            switch (_options.Weather)
            {
                case "rain": StartWeather(WeatherPreset.Rain()); break;
                case "storm": StartWeather(WeatherPreset.Storm()); break;
            }
        }

        private void StartWeather(WeatherPreset preset)
        {
            // Applied at once for a command-line start, so a capture shows it
            _renderer.Weather.Start(preset, _scene);
            _renderer.Weather.Update(float.MaxValue, _scene);
        }

        private void ResetCamera()
        {
            Vector3 start = _options.CameraPosition ?? new Vector3(0f, StartHeight, 0f);
            // The start height is measured from the ground
            start.Y += _renderer.SurfaceHeightAt(start.X, start.Z);
            _camera.Position = start;
            // Default: looking west with the sun behind, so lit slopes face the view
            _camera.Yaw = (_options.CameraYawDeg ?? -90f) * MathF.PI / 180f;
            _camera.Pitch = (_options.CameraPitchDeg ?? 6f) * MathF.PI / 180f;
            _renderer?.InvalidateTaaHistory();
        }

        private void OnKeyDown(IKeyboard keyboard, Key key, int scancode)
        {
            switch (key)
            {
                case Key.F1: _panel.Visible = !_panel.Visible; break;
                case Key.F12: _screenshotRequested = true; break;
                case Key.Escape: _window.Close(); break;
            }
        }

        private void OnMouseDown(IMouse mouse, MouseButton button)
        {
            if (button != MouseButton.Right || ImGui.GetIO().WantCaptureMouse) return;
            _looking = true;
            _lastMouse = mouse.Position;
            mouse.Cursor.CursorMode = CursorMode.Raw;
        }

        private void OnMouseUp(IMouse mouse, MouseButton button)
        {
            if (button != MouseButton.Right || !_looking) return;
            _looking = false;
            mouse.Cursor.CursorMode = CursorMode.Normal;
        }

        private void OnScroll(IMouse mouse, ScrollWheel wheel)
        {
            if (ImGui.GetIO().WantCaptureMouse) return;
            _camera.MoveSpeed = Math.Clamp(_camera.MoveSpeed * MathF.Pow(1.2f, wheel.Y), 0.5f, 500f);
        }

        private void OnUpdate(double deltaTime)
        {
            float dt = (float)Math.Min(deltaTime, 0.1);
            var mouse = _input.Mice.Count > 0 ? _input.Mice[0] : null;
            if (_looking && mouse != null)
            {
                Vector2 delta = mouse.Position - _lastMouse;
                _lastMouse = mouse.Position;
                _camera.Look(delta.X, delta.Y);
            }

            var kb = _input.Keyboards.Count > 0 ? _input.Keyboards[0] : null;
            if (kb != null && !ImGui.GetIO().WantCaptureKeyboard)
            {
                var move = Vector3.Zero;
                if (kb.IsKeyPressed(Key.W)) move.Z += 1f;
                if (kb.IsKeyPressed(Key.S)) move.Z -= 1f;
                if (kb.IsKeyPressed(Key.D)) move.X += 1f;
                if (kb.IsKeyPressed(Key.A)) move.X -= 1f;
                if (kb.IsKeyPressed(Key.Space) || kb.IsKeyPressed(Key.E)) move.Y += 1f;
                if (kb.IsKeyPressed(Key.ControlLeft) || kb.IsKeyPressed(Key.Q)) move.Y -= 1f;
                bool sprint = kb.IsKeyPressed(Key.ShiftLeft) || kb.IsKeyPressed(Key.ShiftRight);
                _camera.Move(move, sprint, dt);
            }

            if (_panel.StayAboveGround)
            {
                float floor = _renderer.SurfaceHeightAt(_camera.Position.X, _camera.Position.Z) + GroundClearance;
                if (_camera.Position.Y < floor) _camera.Position.Y = floor;
            }

            _brush = null;
            if (mouse != null && mouse.IsButtonPressed(MouseButton.Left) && !_looking && !ImGui.GetIO().WantCaptureMouse)
                _brush = BrushUnderCursor(mouse.Position);        }

        /// <summary>
        /// Where the cursor ray meets the grass, within the trail sim's reach
        /// (the brush has no effect farther out).
        /// </summary>
        private Vector2? BrushUnderCursor(Vector2 cursor)
        {
            var size = _window.Size;
            if (size.X <= 0 || size.Y <= 0) return null;
            if (!Matrix4x4.Invert(_camera.View * _camera.Projection, out var inverse)) return null;

            float x = 2f * cursor.X / size.X - 1f;
            float y = 1f - 2f * cursor.Y / size.Y;
            Vector4 a = Vector4.Transform(new Vector4(x, y, 0f, 1f), inverse);
            Vector4 b = Vector4.Transform(new Vector4(x, y, 0.5f, 1f), inverse);
            Vector3 direction = new Vector3(b.X, b.Y, b.Z) / b.W - new Vector3(a.X, a.Y, a.Z) / a.W;

            float reach = _renderer.Lod.MiddleFieldSize * 0.46f;
            return _renderer.RaycastSurface(_camera.Position, direction, reach, out var hit)
                ? new Vector2(hit.X, hit.Z)
                : null;
        }

        private void OnRender(double deltaTime)
        {
            float dt = (float)Math.Min(deltaTime, 0.1);
            _time += dt;
            _window.VSync = _render.VSync;

            var fb = _window.FramebufferSize;
            if (fb.X <= 0 || fb.Y <= 0) return;

            _renderer.Render(_camera, dt, (float)_time, fb.X, fb.Y, _brush);

            _imGui.Update(dt);
            _panel.Draw(dt);
            _imGui.Render();

            _frame++;
            if (_options.CapturePath != null && _frame == _options.CaptureFrames)
            {
                Screenshot.Capture(_gl, fb.X, fb.Y, _options.CapturePath);
                _window.Close();
            }
            else if (_screenshotRequested)
            {
                _screenshotRequested = false;
                string dir = Path.Combine(AppContext.BaseDirectory, "screenshots");
                Screenshot.Capture(_gl, fb.X, fb.Y, Path.Combine(dir, $"silkgrass_{DateTime.Now:yyyyMMdd_HHmmss}.png"));
            }
        }

        private void OnClosing()
        {
            _renderer?.Dispose();
            _imGui?.Dispose();
            _input?.Dispose();
            _gl?.Dispose();
        }
    }
}
