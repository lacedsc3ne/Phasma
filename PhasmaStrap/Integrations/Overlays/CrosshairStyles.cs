namespace PhasmaStrap.Integrations.Overlays
{
    // The crosshair in use, wherever it is stored: the editor's design, or - for settings written
    // before the editor existed - the old shape / size / gap values turned into a design.
    internal static class CrosshairStyles
    {
        public static CrosshairStyle Current => App.Settings.Prop.CrosshairActive?.Clamped() ?? FromLegacy(App.Settings.Prop);

        public static CrosshairStyle FromLegacy(Models.Persistable.Settings prop)
        {
            var style = new CrosshairStyle
            {
                Name = "My crosshair",
                Color = prop.CrosshairColorHex,
                OutlineColor = prop.CrosshairOutlineColorHex,
                Opacity = prop.CrosshairOpacity,
                Outline = true,
                OutlineThickness = Math.Max(1, prop.CrosshairLineThickness / 2),
            };

            switch (prop.CrosshairShapeIndex)
            {
                case 1: // dot
                    style.Arms = false;
                    style.Dot = true;
                    style.DotRound = true;
                    style.DotSize = Math.Clamp(prop.CrosshairSize, 1, 16);
                    break;

                case 2: // ring
                    style.Arms = false;
                    style.Ring = true;
                    style.RingRadius = Math.Clamp(prop.CrosshairSize / 2, 2, 60);
                    style.RingThickness = Math.Clamp(prop.CrosshairLineThickness, 1, 12);
                    break;

                case 3: // "none"
                    style.Arms = false;
                    break;

                default: // cross
                    style.ArmLength = prop.CrosshairSize;
                    style.ArmThickness = prop.CrosshairLineThickness;
                    style.Gap = prop.CrosshairGap;
                    break;
            }

            return style.Clamped();
        }
    }
}
