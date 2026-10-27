using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Numerics;
using System.Text;
using System.Text.RegularExpressions;
using Silk.NET.OpenGL;

namespace SilkGrass.Graphics
{
    /// <summary>
    /// A linked GL program built from GLSL files under <see cref="Root"/>.
    ///
    /// Sources are preprocessed before compilation:
    ///   - #include "file" is resolved relative to the including file, then
    ///     relative to <see cref="Root"/>. A file is included once only
    ///     ("#pragma once" semantics: the first occurrence wins, later ones
    ///     expand to nothing).
    ///   - <see cref="GlobalDefines"/> and the per-program defines are injected
    ///     right after the #version line. Programs are compile-time variants of
    ///     one source this way.
    ///   - Non-ASCII characters are rejected with the file, line and column;
    ///     GLSL compilers fail on them with an unhelpful "unexpected $end".
    ///
    /// Every live program registers itself, so a change to a global define
    /// (TAA_VELOCITY, switched with the anti-aliasing mode) can recompile the
    /// programs that use it in place.
    /// </summary>
    public sealed class ShaderProgram : IDisposable
    {
        /// <summary>Directory the shader paths are relative to.</summary>
        public static string Root { get; set; } = Path.Combine(AppContext.BaseDirectory, "shaders");

        /// <summary>Defines injected into every program compiled after they are set.</summary>
        public static readonly Dictionary<string, string> GlobalDefines = new();

        private static readonly List<ShaderProgram> All = new();
        private static int _programCount;
        private static long _totalMs;

        private readonly GL _gl;
        private readonly string[] _stagePaths; // compute, or vertex + fragment
        private readonly string[] _defines;
        private readonly Dictionary<string, int> _uniformLocations = new();
        private uint _handle;
        private string _usesGlobalDefine; // the global define the source references, if any

        public uint Handle => _handle;

        /// <summary>Compute program.</summary>
        public ShaderProgram(GL gl, string computePath)
            : this(gl, new[] { computePath }, null) { }

        /// <summary>Vertex + fragment program, optionally compiled with extra defines.</summary>
        public ShaderProgram(GL gl, string vertPath, string fragPath, string[] defines = null)
            : this(gl, new[] { vertPath, fragPath }, defines) { }

        private ShaderProgram(GL gl, string[] stagePaths, string[] defines)
        {
            _gl = gl;
            _stagePaths = stagePaths;
            _defines = defines;
            _handle = Build(isRebuild: false);
            All.Add(this);
        }

        public static void LogSummary() =>
            Log.Info($"[Shader] {_programCount} programs compiled in {_totalMs} ms");

        /// <summary>
        /// Recompiles every live program whose source references the given
        /// global define. Call after changing that define at runtime.
        /// </summary>
        public static void RebuildUsersOf(string define)
        {
            int rebuilt = 0;
            foreach (var program in All.ToArray())
            {
                if (program._usesGlobalDefine != define) continue;
                program.Rebuild();
                rebuilt++;
            }
            if (rebuilt > 0) Log.Info($"[Shader] Rebuilt {rebuilt} program(s) using {define}.");
        }

        /// <summary>
        /// Recompiles from the original sources with the current defines. The
        /// replacement is linked before the old program is deleted, so a compile
        /// error keeps the working program (logged, not fatal).
        /// </summary>
        public void Rebuild()
        {
            try
            {
                uint fresh = Build(isRebuild: true);
                _gl.DeleteProgram(_handle);
                _handle = fresh;
                _uniformLocations.Clear();
            }
            catch (Exception e)
            {
                Log.Error($"[Shader] Rebuild of '{_stagePaths[0]}' failed, keeping the old program: {e.Message}");
            }
        }

        private uint Build(bool isRebuild)
        {
            var sw = Stopwatch.StartNew();
            var sources = new string[_stagePaths.Length];
            _usesGlobalDefine = null;
            for (int i = 0; i < _stagePaths.Length; i++)
            {
                string path = Path.Combine(Root, _stagePaths[i]);
                string resolved = ResolveIncludes(File.ReadAllText(path), path, new HashSet<string>(StringComparer.OrdinalIgnoreCase));
                // Checked before injection: once injected, an active global
                // define would appear in every program.
                foreach (var key in TrackedDefines)
                    if (resolved.Contains(key, StringComparison.Ordinal)) _usesGlobalDefine = key;
                sources[i] = InjectDefines(resolved);
            }

            uint handle;
            if (_stagePaths.Length == 1)
            {
                uint compute = Compile(ShaderType.ComputeShader, sources[0], _stagePaths[0]);
                handle = Link(compute);
            }
            else
            {
                uint vert = Compile(ShaderType.VertexShader, sources[0], _stagePaths[0]);
                uint frag = Compile(ShaderType.FragmentShader, sources[1], _stagePaths[1]);
                handle = Link(vert, frag);
            }

            sw.Stop();
            string name = Path.GetFileNameWithoutExtension(_stagePaths[0]);
            if (_defines is { Length: > 0 }) name += $" [{string.Join(", ", _defines)}]";
            if (!isRebuild)
            {
                _programCount++;
                _totalMs += sw.ElapsedMilliseconds;
            }
            Log.Info($"[Shader] {(isRebuild ? "rebuilt" : "compiled")} {name} ({sw.ElapsedMilliseconds} ms)");
            return handle;
        }

        // Global defines that can change at runtime. A program whose source
        // mentions one is rebuilt by RebuildUsersOf when it changes.
        private static readonly string[] TrackedDefines = { "TAA_VELOCITY" };

        private uint Compile(ShaderType type, string source, string path)
        {
            uint shader = _gl.CreateShader(type);
            _gl.ShaderSource(shader, source);
            _gl.CompileShader(shader);
            _gl.GetShader(shader, ShaderParameterName.CompileStatus, out int success);
            if (success == 0)
            {
                string info = _gl.GetShaderInfoLog(shader);
                _gl.DeleteShader(shader);
                throw new Exception($"{type} compilation failed in '{path}':\n{info}");
            }
            return shader;
        }

        private uint Link(params uint[] shaders)
        {
            uint program = _gl.CreateProgram();
            foreach (var s in shaders) _gl.AttachShader(program, s);
            _gl.LinkProgram(program);
            foreach (var s in shaders) _gl.DeleteShader(s);
            _gl.GetProgram(program, ProgramPropertyARB.LinkStatus, out int success);
            if (success == 0)
            {
                string info = _gl.GetProgramInfoLog(program);
                _gl.DeleteProgram(program);
                throw new Exception($"Program link failed for '{_stagePaths[0]}':\n{info}");
            }
            return program;
        }

        private static string ResolveIncludes(string source, string filePath, HashSet<string> visited)
        {
            string fullPath = Path.GetFullPath(filePath);
            if (!visited.Add(fullPath)) return string.Empty;

            ValidateAscii(source, fullPath);
            string directory = Path.GetDirectoryName(fullPath) ?? Root;

            return Regex.Replace(source, @"^\s*#include\s+""([^""]+)""\s*$", match =>
            {
                string name = match.Groups[1].Value;
                string path = Path.Combine(directory, name);
                if (!File.Exists(path)) path = Path.Combine(Root, name);
                if (!File.Exists(path))
                    throw new Exception($"#include file not found: '{name}' in shader '{fullPath}'");
                return ResolveIncludes(File.ReadAllText(path), path, visited);
            }, RegexOptions.Multiline);
        }

        private string InjectDefines(string source)
        {
            var block = new StringBuilder();
            foreach (var kv in GlobalDefines) block.AppendLine($"#define {kv.Key} {kv.Value}");
            if (_defines != null)
                foreach (var d in _defines) block.AppendLine($"#define {d}");
            if (block.Length == 0) return source;

            var version = Regex.Match(source, @"^\s*#version\s+.+$", RegexOptions.Multiline);
            return version.Success
                ? source.Insert(version.Index + version.Length, "\n" + block)
                : block + source;
        }

        private static void ValidateAscii(string source, string path)
        {
            int line = 1, col = 1;
            foreach (char c in source)
            {
                if (c == '\n') { line++; col = 1; continue; }
                if (c > 0x7F)
                    throw new Exception($"Non-ASCII character U+{(int)c:X4} in shader '{path}' at line {line}, col {col}. GLSL source must be pure ASCII.");
                col++;
            }
        }

        public void Use() => _gl.UseProgram(_handle);

        private int Location(string name)
        {
            if (_uniformLocations.TryGetValue(name, out int loc)) return loc;
            loc = _gl.GetUniformLocation(_handle, name);
            _uniformLocations[name] = loc;
            return loc;
        }

        // Setting a uniform the program does not declare (or the compiler
        // stripped) is a silent no-op, so one upload routine can serve every
        // variant of a program.
        public void SetUniform(string name, int value) { int l = Location(name); if (l != -1) _gl.Uniform1(l, value); }
        public void SetUniform(string name, float value) { int l = Location(name); if (l != -1) _gl.Uniform1(l, value); }
        public void SetUniform(string name, Vector2 v) { int l = Location(name); if (l != -1) _gl.Uniform2(l, v.X, v.Y); }
        public void SetUniform(string name, Vector3 v) { int l = Location(name); if (l != -1) _gl.Uniform3(l, v.X, v.Y, v.Z); }
        public void SetUniform(string name, Vector4 v) { int l = Location(name); if (l != -1) _gl.Uniform4(l, v.X, v.Y, v.Z, v.W); }
        public void SetUniform(string name, int x, int y, int z, int w) { int l = Location(name); if (l != -1) _gl.Uniform4(l, x, y, z, w); }

        public unsafe void SetUniform(string name, Matrix4x4 m)
        {
            int l = Location(name);
            if (l != -1) _gl.UniformMatrix4(l, 1, false, (float*)&m);
        }

        public unsafe void SetUniform(string name, Vector4[] values, int count)
        {
            int l = Location(name);
            if (l == -1) return;
            fixed (Vector4* p = values) _gl.Uniform4(l, (uint)count, (float*)p);
        }

        public void Dispose()
        {
            All.Remove(this);
            _gl.DeleteProgram(_handle);
        }
    }
}
