using System;
using System.Globalization;
using Silk.NET.Maths;
using Silk.NET.Windowing;
using Silk.NET.Windowing.Glfw;
using SilkGrass.Graphics;

namespace SilkGrass.App
{
    public static class Program
    {
        public static int Main(string[] args)
        {
            // Numbers in the log, the panel and the command line read the same everywhere
            CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;

            AppOptions options;
            try
            {
                options = AppOptions.Parse(args);
            }
            catch (Exception e)
            {
                Log.Error(e.Message);
                return 2;
            }

            GlfwWindowing.Use();
            var windowOptions = WindowOptions.Default;
            windowOptions.Size = new Vector2D<int>(options.Width, options.Height);
            windowOptions.Title = "SilkGrass";
            windowOptions.VSync = true;
#if DEBUG
            // A debug context lets GlDebug report GL misuse
            const ContextFlags flags = ContextFlags.Debug;
#else
            const ContextFlags flags = ContextFlags.Default;
#endif
            windowOptions.API = new GraphicsAPI(ContextAPI.OpenGL, ContextProfile.Core, flags, new APIVersion(4, 6));

            try
            {
                using var window = Window.Create(windowOptions);
                _ = new GrassApp(window, options);
                window.Run();
                return 0;
            }
            catch (Exception e)
            {
                Log.Error(e.ToString());
                return 1;
            }
        }
    }
}
