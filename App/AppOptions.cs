using System;
using System.Globalization;
using System.Numerics;
using SilkGrass.Settings;

namespace SilkGrass.App
{
    /// <summary>
    /// Command-line options.
    ///
    ///   --size WxH            window size (default 1600x900)
    ///   --camera x,y,z,yaw,pitch
    ///                         start pose; yaw/pitch in degrees, y above the ground
    ///   --weather clear|rain|storm
    ///   --aa taa|msaa|off     anti-aliasing mode (default taa)
    ///   --fidelity high|medium|low
    ///                         grass shader fidelity (default high)
    ///   --no-panel            start with the tuning panel hidden
    ///   --capture file.png    render, save a screenshot after --frames frames, exit
    ///   --frames N            frames before the capture (default 180)
    /// </summary>
    public sealed class AppOptions
    {
        public int Width = 1600;
        public int Height = 900;
        public Vector3? CameraPosition;
        public float? CameraYawDeg;
        public float? CameraPitchDeg;
        public string Weather;
        public AntiAliasingMode? AntiAliasing;
        public GrassFidelity? Fidelity;
        public bool ShowPanel = true;
        public string CapturePath;
        public int CaptureFrames = 180;

        public static AppOptions Parse(string[] args)
        {
            var o = new AppOptions();
            for (int i = 0; i < args.Length; i++)
            {
                string Next() => i + 1 < args.Length ? args[++i] : throw new ArgumentException($"{args[i]} needs a value");
                switch (args[i].ToLowerInvariant())
                {
                    case "--size":
                        var wh = Next().Split('x', 'X');
                        o.Width = int.Parse(wh[0], CultureInfo.InvariantCulture);
                        o.Height = int.Parse(wh[1], CultureInfo.InvariantCulture);
                        break;
                    case "--camera":
                        var v = Array.ConvertAll(Next().Split(','), s => float.Parse(s, CultureInfo.InvariantCulture));
                        o.CameraPosition = new Vector3(v[0], v[1], v[2]);
                        if (v.Length > 3) o.CameraYawDeg = v[3];
                        if (v.Length > 4) o.CameraPitchDeg = v[4];
                        break;
                    case "--weather": o.Weather = Next().ToLowerInvariant(); break;
                    case "--aa": o.AntiAliasing = Enum.Parse<AntiAliasingMode>(Next(), ignoreCase: true); break;
                    case "--fidelity": o.Fidelity = Enum.Parse<GrassFidelity>(Next(), ignoreCase: true); break;
                    case "--no-panel": o.ShowPanel = false; break;
                    case "--capture": o.CapturePath = Next(); break;
                    case "--frames": o.CaptureFrames = int.Parse(Next(), CultureInfo.InvariantCulture); break;
                    default: throw new ArgumentException($"Unknown option '{args[i]}'");
                }
            }
            return o;
        }
    }
}
