using PhasmaStrap.Integrations.Overlays;

namespace PhasmaStrap.UI.ViewModels.Settings
{
    public class OverlaysViewModel : NotifyPropertyChangedViewModel
    {
        public bool HudEnabled
        {
            get => App.Settings.Prop.OverlayHudEnabled;
            set { App.Settings.Prop.OverlayHudEnabled = value; OverlayHub.Refresh(); }
        }

        public bool DiagnosticsEnabled
        {
            get => App.Settings.Prop.OverlayDiagnosticsEnabled;
            set => App.Settings.Prop.OverlayDiagnosticsEnabled = value;
        }

        public bool CrosshairEnabled
        {
            get => App.Settings.Prop.Crosshair;
            set { App.Settings.Prop.Crosshair = value; OverlayHub.Refresh(); }
        }

        public int CrosshairShapeIndex
        {
            get => App.Settings.Prop.CrosshairShapeIndex;
            set => App.Settings.Prop.CrosshairShapeIndex = value;
        }

        public int CrosshairSize
        {
            get => App.Settings.Prop.CrosshairSize;
            set => App.Settings.Prop.CrosshairSize = value;
        }

        public int CrosshairLineThickness
        {
            get => App.Settings.Prop.CrosshairLineThickness;
            set => App.Settings.Prop.CrosshairLineThickness = value;
        }

        public int CrosshairGap
        {
            get => App.Settings.Prop.CrosshairGap;
            set => App.Settings.Prop.CrosshairGap = value;
        }

        public double CrosshairOpacity
        {
            get => App.Settings.Prop.CrosshairOpacity;
            set => App.Settings.Prop.CrosshairOpacity = value;
        }

        public string CrosshairColorHex
        {
            get => App.Settings.Prop.CrosshairColorHex;
            set => App.Settings.Prop.CrosshairColorHex = value;
        }

        public string CrosshairOutlineColorHex
        {
            get => App.Settings.Prop.CrosshairOutlineColorHex;
            set => App.Settings.Prop.CrosshairOutlineColorHex = value;
        }
    }
}
