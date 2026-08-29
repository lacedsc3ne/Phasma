using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Threading;
using System.Xml;

using ICSharpCode.AvalonEdit.Highlighting;
using ICSharpCode.AvalonEdit.Highlighting.Xshd;

using PhasmaStrap.UI.Elements.Base;
using PhasmaStrap.UI.Elements.Dialogs;

// Ported (trimmed down) from Voidstrap's UI/Elements/ContextMenu/CustomThemeEditor.xaml.cs.
// Dropped relative to the original: the "open in an external editor" flow and its
// FileSystemWatcher hot-reload (ExternalEditor/ExternalEditorPickerDialog don't exist in this
// codebase and aren't essential), the custom RinColorPickerDialog (replaced with the standard
// System.Windows.Forms.ColorDialog, which this project already links via UseWindowsForms), and
// the "Publish" button (explicitly out of scope - PhasmaStrap has no hosted theme site).
namespace PhasmaStrap.UI.Elements.ContextMenu
{
    public partial class AppColorThemeEditor : WpfUiWindow
    {
        private const string LOG_IDENT = "AppColorThemeEditor";

        private readonly string _path = Paths.CustomColorThemeXaml;

        private readonly ObservableCollection<ThemeColorItem> _items = new();

        private readonly DispatcherTimer _previewTimer;

        private ResourceDictionary? _previewDict;

        private string _currentXaml = "";

        private string _savedXaml = "";

        private bool _saved;

        private bool _suppressPreview;

        public AppColorThemeEditor()
        {
            InitializeComponent();

            _previewTimer = new DispatcherTimer(DispatcherPriority.Background)
            {
                Interval = TimeSpan.FromMilliseconds(120)
            };
            _previewTimer.Tick += PreviewTimer_Tick;

            BuildItems();

            CollectionViewSource view = new() { Source = _items };
            view.GroupDescriptions.Add(new PropertyGroupDescription("Group"));
            ColorList.ItemsSource = view.View;

            _currentXaml = LoadInitialXaml();
            _savedXaml = _currentXaml;
            CodeEditor.Text = _currentXaml;
            CodeEditor.TextChanged += CodeEditor_TextChanged;

            LoadHighlightingTheme();

            ApplyPreview(_currentXaml, quiet: true);
        }

        private void LoadHighlightingTheme()
        {
            try
            {
                string name = $"Editor-Theme-{App.Settings.Prop.Theme.GetFinal()}.xshd";
                using Stream xmlStream = Resource.GetStream(name);
                using XmlReader reader = XmlReader.Create(xmlStream);
                CodeEditor.SyntaxHighlighting = HighlightingLoader.Load(reader, HighlightingManager.Instance);
            }
            catch (Exception ex)
            {
                App.Logger?.WriteLine(LOG_IDENT, "Could not load the editor colour scheme: " + ex.Message);
            }
        }

        private string LoadInitialXaml()
        {
            try
            {
                if (File.Exists(_path))
                {
                    string text = AppColorTheme.ReadFile(_path);
                    if (AppColorTheme.Validate(text).Ok)
                        return text;
                }
            }
            catch (Exception ex)
            {
                App.Logger?.WriteLine(LOG_IDENT, "Could not read the saved colour theme: " + ex.Message);
            }
            return AppColorTheme.BuildXaml(_items.Select(i => new KeyValuePair<string, Color>(i.Key, i.Color)));
        }

        private void BuildItems()
        {
            _suppressPreview = true;
            Dictionary<string, Color> existing = LoadExistingColors();

            foreach (ThemeKeyInfo info in AppColorTheme.Schema)
            {
                Color color = existing.TryGetValue(info.Key, out Color found)
                    ? found
                    : (AppColorTheme.TryParseColor(info.Fallback, out Color fb) ? fb : Colors.Black);

                ThemeColorItem item = new(info.Key, info.Label, color, info.Group);
                item.Changed += SchedulePreview;
                _items.Add(item);
            }
            _suppressPreview = false;
        }

        private Dictionary<string, Color> LoadExistingColors()
        {
            Dictionary<string, Color> map = new(StringComparer.Ordinal);
            try
            {
                if (!File.Exists(_path))
                    return map;
                ThemeValidationResult result = AppColorTheme.Validate(AppColorTheme.ReadFile(_path));
                if (result.Dictionary == null)
                    return map;
                AppColorTheme.ReadColors(result.Dictionary, map);
            }
            catch (Exception ex)
            {
                App.Logger?.WriteLine(LOG_IDENT, "Could not read saved colours: " + ex.Message);
            }
            return map;
        }

        private void SchedulePreview()
        {
            if (_suppressPreview)
                return;
            _previewTimer.Stop();
            _previewTimer.Interval = TimeSpan.FromMilliseconds(120);
            _previewTimer.Start();
        }

        private void PreviewTimer_Tick(object? sender, EventArgs e)
        {
            _previewTimer.Stop();

            if (EditorTabs.SelectedItem == CodeTab)
            {
                ApplyPreview(CodeEditor.Text, quiet: false);
                return;
            }

            _currentXaml = AppColorTheme.BuildXaml(_items.Select(i => new KeyValuePair<string, Color>(i.Key, i.Color)));
            ApplyPreview(_currentXaml, quiet: true);
        }

        private void CodeEditor_TextChanged(object? sender, EventArgs e)
        {
            if (_suppressPreview || EditorTabs.SelectedItem != CodeTab)
                return;
            _previewTimer.Stop();
            _previewTimer.Interval = TimeSpan.FromMilliseconds(400);
            _previewTimer.Start();
        }

        private bool ApplyPreview(string xaml, bool quiet)
        {
            ThemeValidationResult result = AppColorTheme.Validate(xaml);

            if (!result.Ok)
            {
                string first = result.Errors.FirstOrDefault() ?? Strings.Menu_Appearance_ColorTheme_Editor_StatusInvalidGeneric;
                ShowStatus(result.ErrorLine > 0 ? string.Format(Strings.Menu_Appearance_ColorTheme_Editor_StatusInvalidLine, result.ErrorLine, first) : first, isError: true);
                return false;
            }

            _currentXaml = xaml;
            ApplyPreviewDict(AppColorTheme.Merge(result.Dictionary));

            if (result.Warnings.Count > 0)
                ShowStatus(string.Join(" ", result.Warnings.Take(2)), isError: false);
            else if (!quiet)
                ShowStatus(Strings.Menu_Appearance_ColorTheme_Editor_StatusLooksGood, isError: false);
            else
                ClearStatus();

            SyncSwatches(result.Dictionary);
            return true;
        }

        private void ApplyPreviewDict(ResourceDictionary dict)
        {
            try
            {
                var merged = Application.Current.Resources.MergedDictionaries;
                if (_previewDict != null)
                    merged.Remove(_previewDict);
                _previewDict = dict;
                merged.Add(dict);
            }
            catch (Exception ex)
            {
                App.Logger?.WriteLine(LOG_IDENT, "Preview failed: " + ex.Message);
            }
        }

        private void RemovePreviewDict()
        {
            try
            {
                if (_previewDict != null)
                    Application.Current.Resources.MergedDictionaries.Remove(_previewDict);
            }
            catch
            {
            }
            _previewDict = null;
        }

        private void SyncSwatches(ResourceDictionary? dict)
        {
            if (dict == null)
                return;
            Dictionary<string, Color> map = new(StringComparer.Ordinal);
            AppColorTheme.ReadColors(dict, map);
            _suppressPreview = true;
            foreach (ThemeColorItem item in _items)
            {
                if (map.TryGetValue(item.Key, out Color c))
                    item.SetColor(c);
            }
            _suppressPreview = false;
        }

        private void ShowStatus(string message, bool isError)
        {
            StatusText.Text = message;
            StatusIcon.Symbol = isError ? Wpf.Ui.Common.SymbolRegular.ErrorCircle24 : Wpf.Ui.Common.SymbolRegular.CheckmarkCircle24;
            StatusBorder.Background = new SolidColorBrush(isError ? Color.FromArgb(0x33, 0xFF, 0x3B, 0x3B) : Color.FromArgb(0x22, 0xFF, 0xFF, 0xFF));
            StatusBorder.BorderBrush = new SolidColorBrush(isError ? Color.FromArgb(0x80, 0xFF, 0x3B, 0x3B) : Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF));
            StatusText.Foreground = new SolidColorBrush(isError ? Color.FromRgb(0xFF, 0xC9, 0xC9) : Color.FromRgb(0xDD, 0xDD, 0xDD));
            StatusBorder.Visibility = Visibility.Visible;
            SaveButton.IsEnabled = !isError;
        }

        private void ClearStatus()
        {
            StatusBorder.Visibility = Visibility.Collapsed;
            SaveButton.IsEnabled = true;
        }

        private void EditorTabs_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if (!IsLoaded || e.OriginalSource != EditorTabs)
                return;

            if (EditorTabs.SelectedItem == CodeTab)
            {
                _suppressPreview = true;
                CodeEditor.Text = _currentXaml;
                _suppressPreview = false;
            }
            else
            {
                ApplyPreview(CodeEditor.Text, quiet: true);
            }

            UpdatePreviewPane();
        }

        private void PreviewToggle_Changed(object sender, RoutedEventArgs e) => UpdatePreviewPane();

        private void UpdatePreviewPane()
        {
            if (PreviewPane == null || PreviewColumn == null)
                return;

            bool show = EditorTabs.SelectedItem != CodeTab || PreviewToggle.IsChecked == true;
            PreviewPane.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
            PreviewColumn.Width = show ? new GridLength(300) : new GridLength(0);
            PreviewGap.Width = show ? new GridLength(14) : new GridLength(0);
        }

        private void Format_Click(object sender, RoutedEventArgs e)
        {
            ThemeValidationResult result = AppColorTheme.Validate(CodeEditor.Text);
            if (!result.Ok)
            {
                ShowStatus(result.Errors.FirstOrDefault() ?? Strings.Menu_Appearance_ColorTheme_Editor_StatusFormatFailed, isError: true);
                return;
            }
            Dictionary<string, Color> map = new(StringComparer.Ordinal);
            AppColorTheme.ReadColors(result.Dictionary!, map);
            _suppressPreview = true;
            CodeEditor.Text = AppColorTheme.BuildXaml(map);
            _suppressPreview = false;
            ApplyPreview(CodeEditor.Text, quiet: false);
        }

        private void PickColor_Click(object sender, RoutedEventArgs e)
        {
            if (TryPickColor(Colors.White, out Color picked))
                CodeEditor.Document.Insert(CodeEditor.CaretOffset, AppColorTheme.ToHex(picked));
        }

        private void Swatch_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not FrameworkElement fe || fe.DataContext is not ThemeColorItem item)
                return;
            if (TryPickColor(item.Color, out Color picked))
                item.SetColor(picked);
        }

        private static bool TryPickColor(Color initial, out Color picked)
        {
            picked = initial;
            using var dialog = new System.Windows.Forms.ColorDialog
            {
                Color = System.Drawing.Color.FromArgb(initial.A, initial.R, initial.G, initial.B),
                FullOpen = true,
            };

            if (dialog.ShowDialog() != System.Windows.Forms.DialogResult.OK)
                return false;

            picked = Color.FromArgb(dialog.Color.A, dialog.Color.R, dialog.Color.G, dialog.Color.B);
            return true;
        }

        private void CopyCode_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Clipboard.SetText(CodeEditor.Text);
                ShowStatus(Strings.Menu_Appearance_ColorTheme_Editor_CopySuccess, isError: false);
            }
            catch (Exception ex)
            {
                App.Logger?.WriteLine(LOG_IDENT, "Copy failed: " + ex.Message);
            }
        }

        private void Reset_Click(object sender, RoutedEventArgs e)
        {
            _suppressPreview = true;
            foreach (ThemeColorItem item in _items)
            {
                ThemeKeyInfo info = AppColorTheme.Schema.First(s => s.Key == item.Key);
                if (AppColorTheme.TryParseColor(info.Fallback, out Color c))
                    item.SetColor(c);
            }
            _suppressPreview = false;
            _currentXaml = AppColorTheme.BuildXaml(_items.Select(i => new KeyValuePair<string, Color>(i.Key, i.Color)));
            CodeEditor.Text = _currentXaml;
            ApplyPreview(_currentXaml, quiet: false);
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            string xaml = EditorTabs.SelectedItem == CodeTab ? CodeEditor.Text : _currentXaml;

            ThemeValidationResult result = AppColorTheme.Validate(xaml);
            if (!result.Ok)
            {
                string first = result.Errors.FirstOrDefault() ?? Strings.Menu_Appearance_ColorTheme_Editor_StatusInvalidGeneric;
                ShowStatus(result.ErrorLine > 0 ? string.Format(Strings.Menu_Appearance_ColorTheme_Editor_StatusInvalidLine, result.ErrorLine, first) : first, isError: true);
                return;
            }

            Dictionary<string, Color> oldMap = new(StringComparer.Ordinal);
            Dictionary<string, Color> newMap = new(StringComparer.Ordinal);
            ThemeValidationResult oldResult = AppColorTheme.Validate(_savedXaml);
            if (oldResult.Dictionary != null)
                AppColorTheme.ReadColors(oldResult.Dictionary, oldMap);
            AppColorTheme.ReadColors(result.Dictionary!, newMap);

            var changes = ThemeChangesDialog.BuildChanges(AppColorTheme.Schema, oldMap, newMap);

            if (changes.Count > 0)
            {
                var dialog = new ThemeChangesDialog(changes) { Owner = this };
                dialog.ShowDialog();

                if (dialog.Result != MessageBoxResult.OK)
                    return;
            }

            try
            {
                string? dir = Path.GetDirectoryName(_path);
                if (!string.IsNullOrEmpty(dir))
                    Directory.CreateDirectory(dir);
                AppColorTheme.WriteFile(_path, xaml);
            }
            catch (Exception ex)
            {
                ShowStatus(string.Format(Strings.Menu_Appearance_ColorTheme_Editor_StatusSaveFailed, ex.Message), isError: true);
                return;
            }

            App.Settings.Prop.CustomColorThemeEnabled = true;
            App.Settings.Save();
            _saved = true;
            _savedXaml = xaml;

            RemovePreviewDict();
            WpfUiWindow.ApplyThemeToAllOpenWindows();
            Close();
        }

        private void Close_Click(object sender, RoutedEventArgs e) => Close();

        private void AppColorThemeEditor_Closed(object? sender, EventArgs e)
        {
            Closed -= AppColorThemeEditor_Closed;
            CodeEditor.TextChanged -= CodeEditor_TextChanged;
            _previewTimer.Stop();
            _previewTimer.Tick -= PreviewTimer_Tick;

            foreach (ThemeColorItem item in _items)
            {
                item.Changed -= SchedulePreview;
                item.Detach();
            }

            if (!_saved)
                RemovePreviewDict();

            WpfUiWindow.ApplyThemeToAllOpenWindows();
        }
    }
}
