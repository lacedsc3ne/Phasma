namespace PhasmaStrap.Enums
{
    // window backdrop material for wpfui-based windows (settings window, dialogs, etc)
    // "Default" preserves the existing hardcoded Acrylic behaviour from WpfUiWindow
    public enum BackdropStyle
    {
        [EnumName(StaticName = "Acrylic (Default)")]
        Default = 0,

        [EnumName(StaticName = "Mica")]
        Mica = 1,

        [EnumName(StaticName = "Acrylic")]
        Acrylic = 2,

        [EnumName(StaticName = "No backdrop")]
        None = 3
    }
}
