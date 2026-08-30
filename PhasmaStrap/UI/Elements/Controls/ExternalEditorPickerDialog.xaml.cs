using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

// Ported (adapted to PhasmaStrap's dialog conventions - a WpfUiWindow subclass with a plain XAML
// counterpart, a public Result field, shown via ShowDialog()) from Voidstrap's
// UI/Elements/Controls/ExternalEditorPickerDialog.cs, which built its UI purely in code.
namespace PhasmaStrap.UI.Elements.Controls
{
    public sealed class EditorChoice
    {
        public ExternalEditorInfo Editor { get; init; } = new();

        public string Name => Editor.Name;

        public ImageSource? Icon { get; init; }
    }

    public partial class ExternalEditorPickerDialog
    {
        private const string LOG_IDENT = "ExternalEditorPickerDialog";

        public MessageBoxResult Result = MessageBoxResult.Cancel;

        public ExternalEditorInfo? SelectedEditor => (EditorList.SelectedItem as EditorChoice)?.Editor;

        public ExternalEditorPickerDialog(IReadOnlyList<ExternalEditorInfo> editors)
        {
            InitializeComponent();

            List<EditorChoice> choices = new();
            foreach (ExternalEditorInfo editor in editors)
                choices.Add(new EditorChoice { Editor = editor, Icon = LoadIcon(editor.Path) });

            EditorList.ItemsSource = choices;
        }

        private static ImageSource? LoadIcon(string path)
        {
            try
            {
                using System.Drawing.Icon? icon = System.Drawing.Icon.ExtractAssociatedIcon(path);
                if (icon == null)
                    return null;

                ImageSource source = Imaging.CreateBitmapSourceFromHIcon(icon.Handle, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                source.Freeze();
                return source;
            }
            catch (Exception ex)
            {
                App.Logger?.WriteLine(LOG_IDENT, "No icon for " + path + ": " + ex.Message);
                return null;
            }
        }

        private void Accept()
        {
            if (EditorList.SelectedItem == null)
                return;

            Result = MessageBoxResult.OK;
            Close();
        }

        private void OKButton_Click(object sender, RoutedEventArgs e) => Accept();

        private void EditorList_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e) => Accept();
    }
}
