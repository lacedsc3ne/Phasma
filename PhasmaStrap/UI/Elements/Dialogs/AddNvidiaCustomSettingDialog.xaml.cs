using System.Windows;

using PhasmaStrap.Resources;

namespace PhasmaStrap.UI.Elements.Dialogs
{
    public partial class AddNvidiaCustomSettingDialog
    {
        private readonly Func<uint, bool> _isIdTaken;

        public MessageBoxResult Result = MessageBoxResult.Cancel;

        public string SettingName { get; private set; } = string.Empty;

        public uint SettingId { get; private set; }

        public uint SettingValue { get; private set; }

        public AddNvidiaCustomSettingDialog(Func<uint, bool> isIdTaken)
        {
            _isIdTaken = isIdTaken;
            InitializeComponent();
            NameTextBox.Focus();
        }

        private void OKButton_Click(object sender, RoutedEventArgs e)
        {
            string name = NameTextBox.Text.Trim();
            if (name.Length == 0)
            {
                ShowError(Strings.Dialog_AddNvidiaCustomSetting_Error_Name);
                return;
            }

            if (!TryParseUInt(SettingIdTextBox.Text, out uint id))
            {
                ShowError(Strings.Dialog_AddNvidiaCustomSetting_Error_SettingId);
                return;
            }

            if (_isIdTaken(id))
            {
                ShowError(Strings.Dialog_AddNvidiaCustomSetting_Error_Duplicate);
                return;
            }

            if (!TryParseUInt(ValueTextBox.Text, out uint value))
            {
                ShowError(Strings.Dialog_AddNvidiaCustomSetting_Error_Value);
                return;
            }

            SettingName = name;
            SettingId = id;
            SettingValue = value;
            Result = MessageBoxResult.OK;
            Close();
        }

        private void ShowError(string message)
        {
            StatusText.Text = message;
        }

        private static bool TryParseUInt(string? raw, out uint value)
        {
            string text = (raw ?? string.Empty).Trim();
            if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                return uint.TryParse(text.AsSpan(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out value);

            return uint.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
        }
    }
}
