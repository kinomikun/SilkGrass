using Silk.NET.OpenGL;
using SilkGrass.Graphics;

namespace SilkGrass.Rendering
{
    /// <summary>
    /// Motion vectors for TAA.
    ///
    /// While TAA is the active mode the scene framebuffer carries an RG16F
    /// velocity texture on COLOR_ATTACHMENT1. It is mapped into the fragment
    /// outputs only around draws whose shaders write it (Begin/End);
    /// everything else renders to COLOR_ATTACHMENT0 alone and leaves the
    /// sentinel the frame was cleared to, so the resolve falls back to camera
    /// reprojection from depth there. An enabled draw buffer that the bound
    /// shader never writes would be undefined behaviour.
    ///
    /// The velocity code is compiled in only while TAA is on (the TAA_VELOCITY
    /// define), so MSAA and no-AA pay nothing for it.
    ///
    /// Anything sub-pixel, aliased or animated (grass above all) must write
    /// explicit velocity: the depth fallback is only reliable for smooth,
    /// well-covered surfaces. Past the mid field a pixel covers many blades,
    /// the jitter changes which one wins the depth test every frame, and the
    /// reconstructed motion turns into noise the history never converges on.
    /// </summary>
    public static class TaaVelocity
    {
        public const string ShaderDefine = "TAA_VELOCITY";

        /// <summary>True while the scene pass renders with a velocity attachment.</summary>
        public static bool Active;

        /// <summary>Whether the loaded programs were compiled with their velocity output.</summary>
        public static bool ShadersCompiled => ShaderProgram.GlobalDefines.ContainsKey(ShaderDefine);

        /// <summary>
        /// Adds or removes the velocity define, and recompiles the programs
        /// that use it when it changed. Call before the first program is
        /// created too (nothing to rebuild then).
        /// </summary>
        public static void SetEnabled(bool taaActive)
        {
            bool before = ShadersCompiled;
            if (taaActive) ShaderProgram.GlobalDefines[ShaderDefine] = "1";
            else ShaderProgram.GlobalDefines.Remove(ShaderDefine);
            if (before != ShadersCompiled)
                ShaderProgram.RebuildUsersOf(ShaderDefine);
        }

        private static readonly GLEnum[] ColorAndVelocity = { GLEnum.ColorAttachment0, GLEnum.ColorAttachment1 };
        private static readonly GLEnum[] ColorOnly = { GLEnum.ColorAttachment0 };

        /// <summary>Maps the velocity attachment before velocity-writing draws.</summary>
        public static void Begin(GL gl)
        {
            if (Active) gl.DrawBuffers(2, ColorAndVelocity);
        }

        /// <summary>Back to colour-only output after velocity-writing draws.</summary>
        public static void End(GL gl)
        {
            if (Active) gl.DrawBuffers(1, ColorOnly);
        }
    }
}
