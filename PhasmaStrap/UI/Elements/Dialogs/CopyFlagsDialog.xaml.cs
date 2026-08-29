using System.Windows;

using PhasmaStrap.Resources;

namespace PhasmaStrap.UI.Elements.Dialogs
{
    public enum CopyFlagsFormat
    {
        Json,
        GroupedJson,
        Base64,
    }

    // Lets the user pick how the current FastFlag set gets copied to the clipboard from
    // FastFlagEditorPage - plain JSON (the previous, only option), JSON grouped by flag
    // prefix, or Base64-encoded JSON for pasting somewhere that mangles raw JSON.
    //
    // Ported from Voidstrap's UI/Elements/Dialogs/CopyFlagsDialog.xaml.cs.
    public partial class CopyFlagsDialog
    {
        private sealed class FormatOption
        {
            public CopyFlagsFormat Format { get; init; }
            public string Label { get; init; } = string.Empty;
            public override string ToString() => Label;
        }

        private readonly List<FormatOption> _options;

        public MessageBoxResult Result = MessageBoxResult.Cancel;

        public CopyFlagsFormat SelectedFormat =>
            FormatBox.SelectedItem is FormatOption option ? option.Format : CopyFlagsFormat.Json;

        public CopyFlagsDialog(int flagCount)
        {
            InitializeComponent();

            _options = new List<FormatOption>
            {
                new FormatOption { Format = CopyFlagsFormat.Json, Label = Strings.Dialog_CopyFlags_Format_Json },
                new FormatOption { Format = CopyFlagsFormat.GroupedJson, Label = Strings.Dialog_CopyFlags_Format_GroupedJson },
                new FormatOption { Format = CopyFlagsFormat.Base64, Label = Strings.Dialog_CopyFlags_Format_Base64 },
            };

            FormatBox.ItemsSource = _options;
            FormatBox.SelectedIndex = 0;

            CountText.Text = flagCount == 1
                ? Strings.Dialog_CopyFlags_CountSingular
                : string.Format(Strings.Dialog_CopyFlags_CountPlural, flagCount);

            CopyButton.IsEnabled = flagCount > 0;
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
