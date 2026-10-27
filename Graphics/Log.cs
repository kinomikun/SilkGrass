using System;

namespace SilkGrass.Graphics
{
    /// <summary>
    /// Console logging with a level prefix and the time since startup.
    /// </summary>
    public static class Log
    {
        private static readonly DateTime Start = DateTime.UtcNow;
        private static readonly object Gate = new object();

        public static void Info(string message) => Write("INFO", message, ConsoleColor.Gray);
        public static void Warn(string message) => Write("WARN", message, ConsoleColor.Yellow);
        public static void Error(string message) => Write("ERROR", message, ConsoleColor.Red);

        private static void Write(string level, string message, ConsoleColor color)
        {
            lock (Gate)
            {
                double seconds = (DateTime.UtcNow - Start).TotalSeconds;
                var previous = Console.ForegroundColor;
                Console.ForegroundColor = color;
                Console.WriteLine($"[{seconds,8:F2}] [{level}] {message}");
                Console.ForegroundColor = previous;
            }
        }
    }
}
