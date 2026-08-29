namespace PhasmaStrap.Integrations.AntiAliasing
{
    // Anti-Aliasing no longer owns a background thread/overlay of its own - its shader pass now
    // runs as a stage inside the shared Overlays.OverlayCompositor (see AntiAliasingStage.cs and
    // OverlayCompositor.RenderFrame). This just updates settings and asks OverlayHub to
    // start/stop the compositor immediately if the change means it should now be running (or no
    // longer needs to be).
    public static class AntiAliasingManager
    {
        private const string LOG_IDENT = "AntiAliasing";

        public static void SetEnabled(bool enabled)
        {
            App.Settings.Prop.AntiAliasingEnabled = enabled;
            App.Settings.Save();
            App.Logger.WriteLine(LOG_IDENT, $"Enabled set to {enabled}");
            Overlays.OverlayHub.Refresh();
        }

        public static void SetMethod(int methodIndex)
        {
            App.Settings.Prop.AntiAliasingMethodIndex = System.Math.Clamp(methodIndex, 0, AntiAliasingSettings.MethodNames.Length - 1);
            App.Settings.Save();
            App.Logger.WriteLine(LOG_IDENT, "Method set to " + AntiAliasingSettings.MethodNames[AntiAliasingSettings.MethodIndex]);
            Overlays.OverlayHub.Refresh();
        }
    }
}
