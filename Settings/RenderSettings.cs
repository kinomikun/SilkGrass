namespace SilkGrass.Settings
{
    public enum AntiAliasingMode { Taa, Msaa, Off }

    /// <summary>
    /// How many per-fragment grass features are compiled in. High compiles
    /// the full shaders; the lower levels append cut defines to the grass
    /// programs (see GrassRenderer.FidelityDefines).
    /// </summary>
    public enum GrassFidelity { High, Medium, Low }

    /// <summary>Renderer-wide options (not part of the scene's look).</summary>
    public class RenderSettings
    {
        public AntiAliasingMode AntiAliasing = AntiAliasingMode.Taa;
        public int MsaaSamples = 4;
        /// <summary>Scene resolution relative to the window (post-process and UI stay at window size).</summary>
        public float RenderScale = 1.0f;
        public bool VSync = true;

        public float FieldOfViewDeg = 40f;
        public float NearClip = 0.25f;
        public float FarClip = 3500f;

        public GrassFidelity GrassFidelity = GrassFidelity.High;
        public bool FrustumCulling = true;
    }
}
