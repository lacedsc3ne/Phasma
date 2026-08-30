using System.Windows.Input;
using System.Windows.Threading;
using System.Xml;

using ICSharpCode.AvalonEdit.CodeCompletion;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Editing;
using ICSharpCode.AvalonEdit.Highlighting.Xshd;
using ICSharpCode.AvalonEdit.Highlighting;

using PhasmaStrap.UI.Elements.Base;
using PhasmaStrap.UI.Elements.ContextMenu;
using PhasmaStrap.UI.Elements.Controls;
using PhasmaStrap.UI.ViewModels.Editor;
using System.Windows;

namespace PhasmaStrap.UI.Elements.Editor
{
    /// <summary>
    /// Interaction logic for BootstrapperEditorWindow.xaml
    /// </summary>
    public partial class BootstrapperEditorWindow : WpfUiWindow
    {
        private static class CustomBootstrapperSchema
        {
            private class Schema
            {
                public Dictionary<string, Element> Elements { get; set; } = new Dictionary<string, Element>();
                public Dictionary<string, Type> Types { get; set; } = new Dictionary<string, Type>();
            }

            private class Element
            {
                public string? SuperClass { get; set; } = null;
                public bool IsCreatable { get; set; } = false;

                // [AttributeName] = [TypeName]
                public Dictionary<string, string> Attributes { get; set; } = new Dictionary<string, string>();
            }

            public class Type
            {
                public bool CanHaveElement { get; set; } = false;
                public List<string>? Values { get; set; } = null;
            }

            private static Schema? _schema;

            /// <summary>
            /// Elements and their attributes
            /// </summary>
            public static SortedDictionary<string, SortedDictionary<string, string>> ElementInfo { get; set; } = new();

            /// <summary>
            /// Attributes of elements that can have property elements
            /// </summary>
            public static Dictionary<string, List<string>> PropertyElements { get; set; } = new();

            /// <summary>
            /// All type info
            /// </summary>
            public static SortedDictionary<string, Type> Types { get; set; } = new();

            public static void ParseSchema()
            {
                if (_schema != null)
                    return;

                _schema = JsonSerializer.Deserialize<Schema>(Resource.GetString("CustomBootstrapperSchema.json").Result);
                if (_schema == null)
                    throw new Exception("Deserialised CustomBootstrapperSchema is null");

                foreach (var type in _schema.Types)
                    Types.Add(type.Key, type.Value);

                PopulateElementInfo();
            }

            private static (SortedDictionary<string, string>, List<string>) GetElementAttributes(string name, Element element)
            {
                if (ElementInfo.ContainsKey(name))
                    return (ElementInfo[name], PropertyElements[name]);

                List<string> properties = new List<string>();
                SortedDictionary<string, string> attributes = new();

                foreach (var attribute in element.Attributes)
                {
                    attributes.Add(attribute.Key, attribute.Value);

                    if (!Types.ContainsKey(attribute.Value))
                        throw new Exception($"Schema for type {attribute.Value} is missing. Blame Matt!");

                    Type type = Types[attribute.Value];
                    if (type.CanHaveElement)
                        properties.Add(attribute.Key);
                }

                if (element.SuperClass != null)
                {
                    (SortedDictionary<string, string> superAttributes, List<string> superProperties) = GetElementAttributes(element.SuperClass, _schema!.Elements[element.SuperClass]);
                    foreach (var attribute in superAttributes)
                        attributes.Add(attribute.Key, attribute.Value);

                    foreach (var property in superProperties)
                        properties.Add(property);
                }

                properties.Sort();

                ElementInfo[name] = attributes;
                PropertyElements[name] = properties;

                return (attributes, properties);
            }

            private static void PopulateElementInfo()
            {
                List<string> toRemove = new List<string>();

                foreach (var element in _schema!.Elements)
                {
                    GetElementAttributes(element.Key, element.Value);

                    if (!element.Value.IsCreatable)
                        toRemove.Add(element.Key);
                }

                // remove non-creatable from list now that everything is done
                foreach (var name in toRemove)
                {
                    ElementInfo.Remove(name);
                }
            }
        }

        private BootstrapperEditorWindowViewModel _viewModel;
        private CompletionWindow? _completionWindow = null;

        // line-diff highlighting (ported from Voidstrap's LineDiff + TextMarkerService): shades
        // lines that differ from the last-saved baseline so unsaved edits stand out in the gutter.
        private TextMarkerService? _diffMarkerService;
        private DispatcherTimer? _diffTimer;
        private string _baselineCode = "";

        // external editor integration (ported from Voidstrap's ExternalEditor/ExternalEditorPickerDialog):
        // launches a locally installed editor on Theme.xml and reloads it here when that editor saves.
        private ExternalEditorInfo? _externalEditor;
        private FileSystemWatcher? _externalWatcher;
        private DispatcherTimer? _externalReloadTimer;

        public BootstrapperEditorWindow(string name)
        {
            CustomBootstrapperSchema.ParseSchema();

            string directory = Path.Combine(Paths.CustomThemes, name);

            string themeContents = File.ReadAllText(Path.Combine(directory, "Theme.xml"));
            themeContents = ToCRLF(themeContents); // make sure the theme is in CRLF. a function expects CRLF.

            _viewModel = new BootstrapperEditorWindowViewModel();
            _viewModel.ThemeSavedCallback = ThemeSavedCallback;
            _viewModel.Directory = directory;
            _viewModel.Name = name;
            _viewModel.Title = string.Format(Strings.CustomTheme_Editor_Title, name);
            _viewModel.Code = themeContents;

            DataContext = _viewModel;
            InitializeComponent();

            UIXML.Text = _viewModel.Code;
            UIXML.TextChanged += OnCodeChanged;
            UIXML.TextArea.TextEntered += OnTextAreaTextEntered;

            LoadHighlightingTheme();

            _baselineCode = _viewModel.Code;
            InitializeDiffHighlighting();
        }

        private void LoadHighlightingTheme()
        {
            string name = $"Editor-Theme-{App.Settings.Prop.Theme.GetFinal()}.xshd";
            using Stream xmlStream = Resource.GetStream(name);
            using XmlReader reader = XmlReader.Create(xmlStream);
            UIXML.SyntaxHighlighting = HighlightingLoader.Load(reader, HighlightingManager.Instance);

            UIXML.TextArea.TextView.SetResourceReference(ICSharpCode.AvalonEdit.Rendering.TextView.LinkTextForegroundBrushProperty, "NewTextEditorLink");
        }

        private void ThemeSavedCallback(bool success, string message)
        {
            if (success)
            {
                _baselineCode = _viewModel.Code;
                UpdateDiffHighlighting();
                Snackbar.Show(Strings.CustomTheme_Editor_Save_Success, message, Wpf.Ui.Common.SymbolRegular.CheckmarkCircle32, Wpf.Ui.Common.ControlAppearance.Success);
            }
            else
            {
                Snackbar.Show(Strings.CustomTheme_Editor_Save_Error, message, Wpf.Ui.Common.SymbolRegular.ErrorCircle24, Wpf.Ui.Common.ControlAppearance.Danger);
            }
        }

        private static string ToCRLF(string text)
        {
            return text.Replace("\r\n", "\n").Replace("\r", "\n").Replace("\n", "\r\n");
        }

        private void OnCodeChanged(object? sender, EventArgs e)
        {
            _viewModel.Code = UIXML.Text;
            _viewModel.CodeChanged = true;
            QueueDiffHighlighting();
        }

        private void OnClosing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            if (!_viewModel.CodeChanged)
                return;

            var result = Frontend.ShowMessageBox(string.Format(Strings.CustomTheme_Editor_ConfirmSave, _viewModel.Name), MessageBoxImage.Information, MessageBoxButton.YesNoCancel);
            if (result == MessageBoxResult.Cancel)
            {
                e.Cancel = true;
            }
            else if (result == MessageBoxResult.Yes)
            {
                _viewModel.SaveCommand.Execute(null);
            }
        }

        private void OnClosed(object? sender, EventArgs e)
        {
            _diffTimer?.Stop();
            if (_diffTimer != null)
                _diffTimer.Tick -= DiffTimer_Tick;
            _diffTimer = null;

            StopExternalWatch();
        }

        #region Line-diff highlighting

        private void InitializeDiffHighlighting()
        {
            _diffMarkerService = new TextMarkerService(UIXML.Document);
            UIXML.TextArea.TextView.BackgroundRenderers.Add(_diffMarkerService);
            UIXML.TextArea.TextView.LineTransformers.Add(_diffMarkerService);

            _diffTimer = new DispatcherTimer(DispatcherPriority.Background)
            {
                Interval = TimeSpan.FromMilliseconds(200)
            };
            _diffTimer.Tick += DiffTimer_Tick;
        }

        private void QueueDiffHighlighting()
        {
            if (_diffTimer == null)
                return;

            _diffTimer.Stop();
            _diffTimer.Start();
        }

        private void DiffTimer_Tick(object? sender, EventArgs e)
        {
            _diffTimer?.Stop();
            UpdateDiffHighlighting();
        }

        private void UpdateDiffHighlighting()
        {
            if (_diffMarkerService == null)
                return;

            _diffMarkerService.RemoveAll(_ => true);

            List<DiffLine> diff = LineDiff.Compare(_baselineCode, UIXML.Text);

            foreach (DiffLine line in diff)
            {
                if (line.Kind != DiffKind.Added)
                    continue;

                if (line.NewNumber <= 0 || line.NewNumber > UIXML.Document.LineCount)
                    continue;

                var docLine = UIXML.Document.GetLineByNumber(line.NewNumber);
                var marker = _diffMarkerService.Create(docLine.Offset, docLine.Length);
                marker.BackgroundColor = System.Windows.Media.Color.FromArgb(0x2A, 0x4C, 0xAF, 0x50);
            }

            UIXML.TextArea.TextView.Redraw();
        }

        #endregion

        #region External editor

        private void OpenExternal_Click(object sender, RoutedEventArgs e)
        {
            IReadOnlyList<ExternalEditorInfo> editors = ExternalEditor.Detect();

            if (editors.Count == 0)
            {
                Snackbar.Show(Strings.CustomTheme_Editor_OpenExternal_Title, Strings.CustomTheme_Editor_OpenExternal_NoEditors);
                return;
            }

            ExternalEditorInfo? chosen = null;

            if (!string.IsNullOrEmpty(App.Settings.Prop.PreferredExternalEditorPath))
            {
                foreach (var editor in editors)
                {
                    if (string.Equals(editor.Path, App.Settings.Prop.PreferredExternalEditorPath, StringComparison.OrdinalIgnoreCase))
                    {
                        chosen = editor;
                        break;
                    }
                }
            }

            if (chosen == null)
            {
                var dialog = new ExternalEditorPickerDialog(editors) { Owner = this };
                dialog.ShowDialog();

                if (dialog.Result != MessageBoxResult.OK || dialog.SelectedEditor == null)
                    return;

                chosen = dialog.SelectedEditor;
                App.Settings.Prop.PreferredExternalEditorPath = chosen.Path;
                App.Settings.Save();
            }

            LaunchExternal(chosen);
        }

        private void LaunchExternal(ExternalEditorInfo editor)
        {
            // make sure the file on disk matches what's in the buffer before handing it off
            _viewModel.SaveCommand.Execute(null);

            string themePath = Path.Combine(_viewModel.Directory, "Theme.xml");

            if (!ExternalEditor.Open(editor, themePath))
            {
                Snackbar.Show(Strings.CustomTheme_Editor_OpenExternal_Title, string.Format(Strings.CustomTheme_Editor_OpenExternal_LaunchFailed, editor.Name), Wpf.Ui.Common.SymbolRegular.ErrorCircle24, Wpf.Ui.Common.ControlAppearance.Danger);
                return;
            }

            _externalEditor = editor;
            StartExternalWatch(themePath);
            Snackbar.Show(Strings.CustomTheme_Editor_OpenExternal_Title, string.Format(Strings.CustomTheme_Editor_OpenExternal_Launched, editor.Name));
        }

        private void StartExternalWatch(string themePath)
        {
            StopExternalWatch();

            string? folder = Path.GetDirectoryName(themePath);
            if (string.IsNullOrEmpty(folder))
                return;

            try
            {
                _externalWatcher = new FileSystemWatcher(folder, Path.GetFileName(themePath))
                {
                    NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size,
                    EnableRaisingEvents = true
                };
                _externalWatcher.Changed += OnExternalFileChanged;
                _externalWatcher.Created += OnExternalFileChanged;
            }
            catch (Exception ex)
            {
                App.Logger?.WriteLine("BootstrapperEditorWindow", "Could not watch the theme file: " + ex.Message);
                _externalWatcher = null;
            }
        }

        private void StopExternalWatch()
        {
            if (_externalWatcher != null)
            {
                try
                {
                    _externalWatcher.EnableRaisingEvents = false;
                    _externalWatcher.Changed -= OnExternalFileChanged;
                    _externalWatcher.Created -= OnExternalFileChanged;
                    _externalWatcher.Dispose();
                }
                catch
                {
                }

                _externalWatcher = null;
            }

            if (_externalReloadTimer != null)
            {
                _externalReloadTimer.Stop();
                _externalReloadTimer.Tick -= OnExternalReloadTick;
                _externalReloadTimer = null;
            }
        }

        private void OnExternalFileChanged(object sender, FileSystemEventArgs e)
        {
            if (Dispatcher.HasShutdownStarted)
                return;

            try
            {
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    if (_externalReloadTimer == null)
                    {
                        _externalReloadTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
                        _externalReloadTimer.Tick += OnExternalReloadTick;
                    }

                    _externalReloadTimer.Stop();
                    _externalReloadTimer.Start();
                }));
            }
            catch (Exception ex)
            {
                App.Logger?.WriteLine("BootstrapperEditorWindow", "External reload dispatch failed: " + ex.Message);
            }
        }

        private void OnExternalReloadTick(object? sender, EventArgs e)
        {
            _externalReloadTimer?.Stop();

            string themePath = Path.Combine(_viewModel.Directory, "Theme.xml");

            try
            {
                string text = ToCRLF(File.ReadAllText(themePath));

                if (text == UIXML.Text)
                    return;

                int caret = UIXML.CaretOffset;
                UIXML.Text = text;
                UIXML.CaretOffset = Math.Min(caret, text.Length);

                Snackbar.Show(Strings.CustomTheme_Editor_OpenExternal_Title, string.Format(Strings.CustomTheme_Editor_OpenExternal_Reloaded, _externalEditor?.Name ?? ""));
            }
            catch (Exception ex)
            {
                App.Logger?.WriteLine("BootstrapperEditorWindow", "Could not reload the theme: " + ex.Message);
            }
        }

        #endregion

        private void OnTextAreaTextEntered(object sender, TextCompositionEventArgs e)
        {
            switch (e.Text)
            {
                case "<":
                    OpenElementAutoComplete();
                    break;
                case " ":
                    OpenAttributeAutoComplete();
                    break;
                case ".":
                    OpenPropertyElementAutoComplete();
                    break;
                case "/":
                    AddEndTag();
                    break;
                case ">":
                    CloseCompletionWindow();
                    break;
                case "!":
                    CloseCompletionWindow();
                    break;
            }
        }

        private (string, int) GetLineAndPosAtCaretPosition()
        {
            // this assumes the file was saved as CSLF (\r\n newlines)
            int offset = UIXML.CaretOffset - 1;
            int lineStartIdx = UIXML.Text.LastIndexOf('\n', offset);
            int lineEndIdx = UIXML.Text.IndexOf('\n', offset);

            string line;
            int pos;
            if (lineStartIdx == -1 && lineEndIdx == -1)
            {
                line = UIXML.Text;
                pos = offset;
            }
            else if (lineStartIdx == -1)
            {
                line = UIXML.Text[..(lineEndIdx - 1)];
                pos = offset;
            }
            else if (lineEndIdx == -1)
            {
                line = UIXML.Text[(lineStartIdx + 1)..];
                pos = offset - lineStartIdx - 2;
            }
            else
            {
                line = UIXML.Text[(lineStartIdx + 1)..(lineEndIdx - 1)];
                pos = offset - lineStartIdx - 2;
            }

            return (line, pos);
        }

        /// <summary>
        /// Source: https://xsemmel.codeplex.com
        /// </summary>
        /// <param name="xml"></param>
        /// <param name="offset"></param>
        /// <returns></returns>
        public static string? GetElementAtCursor(string xml, int offset, bool onlyAllowInside = false)
        {
            if (offset == xml.Length)
            {
                offset--;
            }
            int startIdx = xml.LastIndexOf('<', offset);
            if (startIdx < 0) return null;

            if (startIdx < xml.Length && xml[startIdx + 1] == '/')
            {
                startIdx = startIdx + 1;
            }

            int endIdx1 = xml.IndexOf(' ', startIdx);
            if (endIdx1 == -1 /*|| endIdx1 > offset*/) endIdx1 = int.MaxValue;

            int endIdx2 = xml.IndexOf('>', startIdx);
            if (endIdx2 == -1 /*|| endIdx2 > offset*/)
            {
                endIdx2 = int.MaxValue;
            }
            else
            {
                if (onlyAllowInside && endIdx2 < offset)
                    return null; // we dont want attribute auto complete to show outside of elements

                if (endIdx2 < xml.Length && xml[endIdx2 - 1] == '/')
                {
                    endIdx2 = endIdx2 - 1;
                }
            }

            int endIdx = Math.Min(endIdx1, endIdx2);
            if (endIdx2 > 0 && endIdx2 < int.MaxValue && endIdx > startIdx)
            {
                string element = xml.Substring(startIdx + 1, endIdx - startIdx - 1);
                return element == "!--" ? null : element; // dont treat comments as elements
            }
            else
            {
                return null;
            }
        }

        /// <summary>
        /// A space between the cursor and the element will completely cancel this function
        /// </summary>
        private string? GetElementAtCursorNoSpaces(string xml, int offset)
        {
            (string line, int pos) = GetLineAndPosAtCaretPosition();

            string curr = "";
            while (pos != -1)
            {
                char c = line[pos];
                if (c == ' ' || c == '\t')
                    return null;
                if (c == '<')
                    return curr;
                curr = c + curr;
                pos--;
            }

            return null;
        }

        /// <summary>
        /// Returns null if not eligible to auto complete there.
        /// Returns the name of the element to show the attributes for
        /// </summary>
        /// <returns></returns>
        private string? ShowAttributesForElementName()
        {
            (string line, int pos) = GetLineAndPosAtCaretPosition();

            // check if theres an even number of speech marks on the line
            int numSpeech = line.Count(x => x == '"');
            if (numSpeech % 2 == 0)
            {
                // we have an equal number, let's check if pos is in between the speech marks
                int count = -1;
                int idx = pos;
                int size = line.Length - 1;
                while (idx != -1)
                {
                    count++;

                    if (size > idx + 1)
                        idx = line.IndexOf('"', idx + 1);
                    else
                        idx = -1;
                }

                if (count % 2 != 0)
                {
                    // odd number of speech marks means we're inside a string right now
                    // we dont want to display attribute auto complete while we're inside a string
                    return null;
                }
            }

            return GetElementAtCursor(UIXML.Text, UIXML.CaretOffset, true);
        }

        private void AddEndTag()
        {
            CloseCompletionWindow();

            if (UIXML.Text.Length > 2 && UIXML.Text[UIXML.CaretOffset - 2] == '<')
            {
                var elementName = GetElementAtCursor(UIXML.Text, UIXML.CaretOffset - 3);
                if (elementName == null)
                    return;

                UIXML.TextArea.Document.Insert(UIXML.CaretOffset, $"{elementName}>");
            }
            else
            {
                if (UIXML.Text.Length > UIXML.CaretOffset && UIXML.Text[UIXML.CaretOffset] == '>')
                    return;

                var elementName = ShowAttributesForElementName(); // re-using functions :)
                if (elementName != null)
                    UIXML.TextArea.Document.Insert(UIXML.CaretOffset, ">");
            }
        }

        private void OpenElementAutoComplete()
        {
            var data = new List<ICompletionData>();

            foreach (var element in CustomBootstrapperSchema.ElementInfo.Keys)
                data.Add(new ElementCompletionData(element));

            ShowCompletionWindow(data);
        }

        private void OpenAttributeAutoComplete()
        {
            string? element = ShowAttributesForElementName();
            if (element == null)
            {
                CloseCompletionWindow();
                return;
            }

            if (!CustomBootstrapperSchema.ElementInfo.ContainsKey(element))
            {
                CloseCompletionWindow();
                return;
            }

            var attributes = CustomBootstrapperSchema.ElementInfo[element];

            var data = new List<ICompletionData>();

            foreach (var attribute in attributes)
                data.Add(new AttributeCompletionData(attribute.Key, () => OpenTypeValueAutoComplete(attribute.Value)));

            ShowCompletionWindow(data);
        }

        private void OpenTypeValueAutoComplete(string typeName)
        {
            var typeValues = CustomBootstrapperSchema.Types[typeName].Values;
            if (typeValues == null)
                return;

            var data = new List<ICompletionData>();

            foreach (var value in typeValues)
                data.Add(new TypeValueCompletionData(value));

            ShowCompletionWindow(data);
        }

        private void OpenPropertyElementAutoComplete()
        {
            string? element = GetElementAtCursorNoSpaces(UIXML.Text, UIXML.CaretOffset);
            if (element == null)
            {
                CloseCompletionWindow();
                return;
            }

            if (!CustomBootstrapperSchema.PropertyElements.ContainsKey(element))
            {
                CloseCompletionWindow();
                return;
            }

            var properties = CustomBootstrapperSchema.PropertyElements[element];

            var data = new List<ICompletionData>();

            foreach (var property in properties)
                data.Add(new TypeValueCompletionData(property));

            ShowCompletionWindow(data);
        }

        private void CloseCompletionWindow()
        {
            if (_completionWindow != null)
            {
                _completionWindow.Close();
                _completionWindow = null;
            }
        }

        private void ShowCompletionWindow(List<ICompletionData> completionData)
        {
            CloseCompletionWindow();

            if (!completionData.Any())
                return;

            _completionWindow = new CompletionWindow(UIXML.TextArea);

            IList<ICompletionData> data = _completionWindow.CompletionList.CompletionData;
            foreach (var c in completionData)
                data.Add(c);

            _completionWindow.Show();
            _completionWindow.Closed += (_, _) => _completionWindow = null;
        }
    }

    public class ElementCompletionData : ICompletionData
    {
        public ElementCompletionData(string text)
        {
            this.Text = text;
        }

        public System.Windows.Media.ImageSource? Image => null;

        public string Text { get; private set; }

        // Use this property if you want to show a fancy UIElement in the list.
        public object Content => Text;

        public object? Description => null;

        public double Priority { get; }

        public void Complete(TextArea textArea, ISegment completionSegment,
            EventArgs insertionRequestEventArgs)
        {
            textArea.Document.Replace(completionSegment, this.Text);
        }
    }

    public class AttributeCompletionData : ICompletionData
    {
        private Action _openValueAutoCompleteAction;

        public AttributeCompletionData(string text, Action openValueAutoCompleteAction)
        {
            _openValueAutoCompleteAction = openValueAutoCompleteAction;
            this.Text = text;
        }

        public System.Windows.Media.ImageSource? Image => null;

        public string Text { get; private set; }

        // Use this property if you want to show a fancy UIElement in the list.
        public object Content => Text;

        public object? Description => null;

        public double Priority { get; }

        public void Complete(TextArea textArea, ISegment completionSegment,
            EventArgs insertionRequestEventArgs)
        {
            textArea.Document.Replace(completionSegment, this.Text + "=\"\"");
            textArea.Caret.Offset = textArea.Caret.Offset - 1;
            _openValueAutoCompleteAction();
        }
    }

    public class TypeValueCompletionData : ICompletionData
    {
        public TypeValueCompletionData(string text)
        {
            this.Text = text;
        }

        public System.Windows.Media.ImageSource? Image => null;

        public string Text { get; private set; }

        // Use this property if you want to show a fancy UIElement in the list.
        public object Content => Text;

        public object? Description => null;

        public double Priority { get; }

        public void Complete(TextArea textArea, ISegment completionSegment,
            EventArgs insertionRequestEventArgs)
        {
            textArea.Document.Replace(completionSegment, this.Text);
        }
    }
}
