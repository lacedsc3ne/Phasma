namespace PhasmaStrap.Models.Persistable
{
    /// <summary>
    /// Configuration for the RiShade post-processing shader stack (ported from Voidstrap).
    /// <para/>
    /// This is a trimmed-down port: only screen-space effects that don't require an AI-estimated
    /// depth buffer are supported. Voidstrap's depth-buffer-driven effects (depth of field, screen
    /// space reflections, ambient occlusion, global illumination, fog, eye adaptation and the debug
    /// depth/normal views) all depend on <c>RiShadeDepth.cs</c>, which downloads and runs an ONNX
    /// depth estimation model through DirectML at runtime. That dependency was intentionally not
    /// ported, so those fields don't exist here and their shader inputs are always fed disabled.
    /// </summary>
    public class RiShadeSettings
    {
        // color grade
        public bool GradeEnabled { get; set; } = false;
        public float Brightness { get; set; } = 0.0f;
        public float Gamma { get; set; } = 1.0f;
        public float HueShift { get; set; } = 0.0f;
        public float[] Lift { get; set; } = new[] { 0f, 0f, 0f };
        public float[] Gain { get; set; } = new[] { 1f, 1f, 1f };
        public float[] ColorBalance { get; set; } = new[] { 1f, 1f, 1f };
        public int ColorTemp { get; set; } = 1;
        public float[] ColorTempCustom { get; set; } = new[] { 1f, 1f, 1f };

        // tonemap
        public bool TonemapEnabled { get; set; } = false;
        public int TonemapMode { get; set; } = 0;
        public float TonemapExposure { get; set; } = 1.0f;
        public float TonemapWhitepoint { get; set; } = 4.0f;

        // vignette
        public bool VignetteEnabled { get; set; } = false;
        public float VignetteStrength { get; set; } = 0.5f;
        public float VignetteFeather { get; set; } = 1.2f;
        public float VignetteCenterX { get; set; } = 0.0f;
        public float VignetteCenterY { get; set; } = 0.0f;

        // sharpen
        public bool SharpenEnabled { get; set; } = false;
        public float SharpenStrength { get; set; } = 0.8f;
        public float SharpenRadius { get; set; } = 1.0f;
        public float SharpenClamp { get; set; } = 0.08f;

        // bloom
        public bool BloomEnabled { get; set; } = false;
        public float BloomStrength { get; set; } = 0.2f;
        public int BloomPasses { get; set; } = 3;
        public float BloomThreshold { get; set; } = 0.7f;
        public float BloomRadius { get; set; } = 1.5f;
        public float[] BloomTint { get; set; } = new[] { 1f, 1f, 1f };

        // chromatic aberration
        public bool ChromaEnabled { get; set; } = false;
        public float ChromaStrength { get; set; } = 0.003f;
        public bool ChromaRadial { get; set; } = true;

        // film grain
        public bool GrainEnabled { get; set; } = false;
        public float GrainStrength { get; set; } = 0.04f;
        public float GrainSize { get; set; } = 1.0f;
        public bool GrainColored { get; set; } = false;

        // local contrast / debanding / soft "ambient" bloom-glow (all screen-space, no depth needed)
        public float ClarityStrength { get; set; } = 0.0f;
        public bool DebandEnabled { get; set; } = false;
        public float DebandStrength { get; set; } = 0.5f;
        public float AmbientStrength { get; set; } = 0.0f;

        public int RenderScaleIndex { get; set; } = 0;

        public static readonly string[] RenderScaleNames = new[] { "Native", "Balanced", "Performance", "Max FPS" };
        public static readonly float[] RenderScaleValues = new[] { 1.0f, 0.7f, 0.5f, 0.35f };

        public float ResolveRenderScale()
        {
            int i = Math.Clamp(RenderScaleIndex, 0, RenderScaleValues.Length - 1);
            return RenderScaleValues[i];
        }

        public static readonly string[] TonemapNames = new[] { "Reinhard", "ACES", "Uncharted 2", "Filmic", "AgX" };
        public static readonly string[] ColorTempNames = new[] { "Warm", "Neutral", "Cool", "Custom" };
        public static readonly float[][] ColorTempValues = new[]
        {
            new[] { 1.00f, 0.96f, 0.88f },
            new[] { 1.00f, 1.00f, 1.00f },
            new[] { 0.88f, 0.96f, 1.00f },
        };

        public float[] ResolveColorTemp()
        {
            if (ColorTemp >= 0 && ColorTemp < ColorTempValues.Length)
                return ColorTempValues[ColorTemp];
            return ColorTempCustom;
        }

        private static float[] FixTriple(float[]? v, float fill)
        {
            var r = new[] { fill, fill, fill };
            if (v != null)
            {
                for (int i = 0; i < v.Length && i < 3; i++)
                    r[i] = v[i];
            }
            return r;
        }

        public void Normalize()
        {
            Lift = FixTriple(Lift, 0f);
            Gain = FixTriple(Gain, 1f);
            ColorBalance = FixTriple(ColorBalance, 1f);
            ColorTempCustom = FixTriple(ColorTempCustom, 1f);
            BloomTint = FixTriple(BloomTint, 1f);
        }

        public bool HasVisibleEffects =>
            GradeEnabled
            || TonemapEnabled
            || VignetteEnabled
            || SharpenEnabled
            || BloomEnabled
            || ChromaEnabled
            || GrainEnabled
            || ClarityStrength > 0f
            || DebandEnabled
            || AmbientStrength > 0f;

        public static readonly string[] PresetNames = new[]
        {
            "Vanilla (off)",
            "Cinematic",
            "Vibrant",
            "Dark and Moody",
            "Retro Film",
        };

        public static void ApplyPreset(RiShadeSettings s, string name)
        {
            switch (name)
            {
                case "Cinematic":
                    s.GradeEnabled = true;
                    s.TonemapEnabled = true;
                    s.TonemapMode = 1;
                    s.TonemapExposure = 1.1f;
                    s.VignetteEnabled = true;
                    s.VignetteStrength = 0.4f;
                    s.BloomEnabled = true;
                    s.BloomStrength = 0.15f;
                    s.BloomThreshold = 0.75f;
                    s.GrainEnabled = true;
                    s.GrainStrength = 0.025f;
                    break;
                case "Vibrant":
                    s.GradeEnabled = true;
                    s.Brightness = 0.05f;
                    s.BloomEnabled = true;
                    s.BloomStrength = 0.13f;
                    s.BloomThreshold = 0.72f;
                    s.TonemapEnabled = true;
                    s.TonemapMode = 0;
                    s.TonemapExposure = 1.05f;
                    break;
                case "Dark and Moody":
                    s.GradeEnabled = true;
                    s.Brightness = -0.08f;
                    s.Gamma = 0.88f;
                    s.VignetteEnabled = true;
                    s.VignetteStrength = 0.7f;
                    s.TonemapEnabled = true;
                    s.TonemapMode = 3;
                    s.TonemapExposure = 0.9f;
                    break;
                case "Retro Film":
                    s.GradeEnabled = true;
                    s.GrainEnabled = true;
                    s.GrainStrength = 0.07f;
                    s.GrainColored = true;
                    s.ChromaEnabled = true;
                    s.ChromaStrength = 0.004f;
                    s.VignetteEnabled = true;
                    s.VignetteStrength = 0.55f;
                    s.TonemapEnabled = true;
                    s.TonemapMode = 3;
                    break;
                default:
                    // "Vanilla (off)" - reset happens by the caller replacing with new RiShadeSettings()
                    break;
            }
        }
    }
}
