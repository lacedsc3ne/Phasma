namespace PhasmaStrap.Integrations.AntiAliasing
{
    public static class AntiAliasingSettings
    {
        // "Off" is kept as index 0 so a stray out-of-range value never accidentally
        // turns a technique on; the master Enabled toggle is what actually gates the overlay.
        public static readonly string[] MethodNames = new[] { "Off", "FXAA", "FXAA Ultra", "SMAA", "SMAA Ultra", "DLAA", "NFAA", "TSAA" };

        public static int MethodIndex => Math.Clamp(App.Settings.Prop.AntiAliasingMethodIndex, 0, MethodNames.Length - 1);
    }
}
