using System.Windows;

using PhasmaStrap.Resources;

namespace PhasmaStrap.UI.Elements.Dialogs
{
    public enum CopyNvidiaSettingsFormat
    {
        NipProfile,
        SettingRows,
        Base64Nip,
    }

    // Lets the user pick how the currently-configured NVIDIA driver settings (from
    // NvidiaPage/NvidiaViewModel) get copied to the clipboard - a full NVIDIA Profile
    // Inspector-compatible .nip document, a plain "name = value" listing, or the .nip
    // document Base64-encoded for pasting somewhere that mangles raw XML.
    //
    // Ported from Voidstrap's UI/Elements/Dialogs/CopyNvidiaSettingsDialog.xaml.cs.
    public partial class CopyNvidiaSettingsDialog
    {
        private sealed class FormatOption
        {
            public CopyNvidiaSettingsFormat Format { get; init; }
            public string Label { get; init; } = string.Empty;
            public override string ToString() => Label;
        }

        private readonly List<FormatOption> _options;

        public MessageBoxResult Result = MessageBoxResult.Cancel;

        public CopyNvidiaSettingsFormat SelectedFormat =>
            FormatBox.SelectedItem is FormatOption option ? option.Format : CopyNvidiaSettingsFormat.NipProfile;

        public CopyNvidiaSettingsDialog(int settingCount)
        {
            InitializeComponent();

            _options = new List<FormatOption>
            {
                new FormatOption { Format = CopyNvidiaSettingsFormat.NipProfile, Label = Strings.Dialog_CopyNvidiaSettings_Format_NipProfile },
                new FormatOption { Format = CopyNvidiaSettingsFormat.SettingRows, Label = Strings.Dialog_CopyNvidiaSettings_Format_SettingRows },
                new FormatOption { Format = CopyNvidiaSettingsFormat.Base64Nip, Label = Strings.Dialog_CopyNvidiaSettings_Format_Base64Nip },
            };

            FormatBox.ItemsSource = _options;
            FormatBox.SelectedIndex = 0;

            CountText.Text = settingCount == 1
                ? Strings.Dialog_CopyNvidiaSettings_CountSingular
                : string.Format(Strings.Dialog_CopyNvidiaSettings_CountPlural, settingCount);

            CopyButton.IsEnabled = settingCount > 0;
        }

        private void Copy_Click(object sender, RoutedEventArgs e)
        {
            Result = MessageBoxResult.OK;
            Close();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            Result = MessageBoxResult.Cancel;
            Close();
        }
    }
}
