using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Silk.NET.OpenGL;

namespace SilkGrass.Graphics
{
    /// <summary>
    /// Routes OpenGL debug-context messages (errors, undefined behaviour,
    /// performance warnings) to the log, so GL misuse names itself in one line
    /// instead of a silent black screen. Needs a debug context (Program.cs
    /// requests one in Debug builds).
    /// </summary>
    public static class GlDebug
    {
        // GL stores only the native pointer; keep the delegate alive here.
        private static DebugProc _callback;

        // Per-message-id throttle so a per-frame warning cannot flood the log.
        private const int MaxLogsPerId = 3;
        private static readonly Dictionary<int, int> IdCounts = new();

        public static unsafe void Install(GL gl)
        {
            _callback = OnMessage;
            gl.Enable(EnableCap.DebugOutput);
            // Synchronous: the callback fires on the offending call's stack, so
            // a breakpoint in OnMessage lands on the culprit.
            gl.Enable(EnableCap.DebugOutputSynchronous);
            gl.DebugMessageCallback(_callback, null);
            // Silence notification-severity chatter (buffer usage hints etc.).
            gl.DebugMessageControl(GLEnum.DontCare, GLEnum.DontCare,
                GLEnum.DebugSeverityNotification, 0, null, false);
            Log.Info("[GlDebug] GL debug message callback installed.");
        }

        private static void OnMessage(GLEnum source, GLEnum type, int id, GLEnum severity,
            int length, nint message, nint userParam)
        {
            lock (IdCounts)
            {
                IdCounts.TryGetValue(id, out int count);
                if (count >= MaxLogsPerId) return;
                IdCounts[id] = count + 1;

                string text = Marshal.PtrToStringAnsi(message, length);
                string suffix = count + 1 == MaxLogsPerId ? " (further messages with this id suppressed)" : "";
                string line = $"[GL {severity} {type} src={source} id={id}] {text}{suffix}";

                if (type == GLEnum.DebugTypeError)
                    Log.Error(line + "\n" + Environment.StackTrace);
                else
                    Log.Warn(line);
            }
        }
    }
}
