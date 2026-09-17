using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;

using Microsoft.Win32;

using PhasmaStrap.Integrations.Nvidia;
using PhasmaStrap.UI.Elements.Dialogs;
using PhasmaStrap.UI.ViewModels.Settings;

namespace PhasmaStrap.UI.Elements.Settings.Pages
{
    /// <summary>
    /// Interaction logic for NvidiaPage.xaml
    /// </summary>
    public partial class NvidiaPage
    {
        // Grid view's filtered copy of _viewModel.CustomSettings, same code-behind-only DataGrid
        // idiom as FastFlagEditorPage (see the comment at the top of that file) - kept in sync
        // with the viewmodel's collection via CollectionChanged rather than an XAML binding, so
        // the search box can filter it without touching the underlying data.
        private readonly ObservableCollection<NvidiaSetting> _gridSettings = new();

        private readonly NvidiaViewModel _viewModel;

        private string _gridSearchFilter = string.Empty;
        private bool _applying;
        private bool _resetting;

        public NvidiaPage()
        {
            _viewModel = new NvidiaViewModel();
            DataContext = _viewModel;
            InitializeComponent();

            _viewModel.CustomSettings.CollectionChanged += (_, _) => ReloadGridList();
        }

        private void Page_Loaded(object sender, RoutedEventArgs e)
        {
            _viewModel.Attach();
        }

        private void Page_Unloaded(object sender, RoutedEventArgs e)
        {
            _viewModel.Detach();
        }

        private async void Apply_Click(object sender, RoutedEventArgs e)
        {
            if (_applying)
                return;

            _applying = true;
            ApplyButton.IsEnabled = false;
            ApplyButtonGrid.IsEnabled = false;

            try
            {
                NvidiaApplyResult result = await Task.Run(() => _viewModel.ApplyToDriver());

                Frontend.ShowMessageBox(
                    Describe(result),
                    result.Ok ? MessageBoxImage.Asterisk : MessageBoxImage.Exclamation);
            }
            catch (Exception ex)
            {
                App.Logger.WriteException("NvidiaPage::Apply_Click", ex);
                Frontend.ShowMessageBox("Failed to apply NVIDIA driver settings:\n" + ex.Message, MessageBoxImage.Error);
            }
            finally
            {
                _applying = false;
                ApplyButton.IsEnabled = true;
                ApplyButtonGrid.IsEnabled = true;
            }
        }

        // "Advanced Editor" row on the card view - flips to the raw grid view in place (same
        // page/DataContext, see the DataTriggers in NvidiaPage.xaml), rather than opening a new
        // window/page.
        private void AdvancedEditor_Click(object sender, RoutedEventArgs e)
        {
            ReloadGridList();
            _viewModel.NvidiaEditorViewMode = true;
        }

        // "Back" button in the grid view - flips back to the card view.
        private void AdvancedEditorBack_Click(object sender, RoutedEventArgs e)
        {
            _viewModel.NvidiaEditorViewMode = false;
        }

        // "NVIDIA Setup" row. PhasmaStrap's NVIDIA integration talks to the driver directly via
        // NVAPI (see NvidiaProfileInspector.cs) - there's no separate "NVIDIA Profile Inspector"
        // tool to install or point PhasmaStrap at like Voidstrap needs, so there's nothing to
        // walk the user through installing. This just explains that in place, rather than
        // linking out to a wiki page that doesn't exist for this feature.
        private void NvidiaSetup_Click(object sender, RoutedEventArgs e)
        {
            Frontend.ShowMessageBox(Strings.Menu_Nvidia_Setup_HelpText, MessageBoxImage.Information);
        }

        private void EditorSearchTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            _gridSearchFilter = EditorSearchTextBox.Text;
            ReloadGridList();
        }

        // Rebuilds the grid's filtered list from _viewModel.CustomSettings, filtering by Name or
        // Setting ID (decimal or hex) the same way FastFlagEditorPage's ReloadList filters by
        // flag name. Preserves the current selection across a refresh so deleting a filtered
        // subset or adding a new setting doesn't surprise-clear what's selected.
        private void ReloadGridList()
        {
            HashSet<uint> selected = new HashSet<uint>();
            foreach (object item in NvidiaEditorGrid.SelectedItems)
            {
                if (item is NvidiaSetting setting)
                    selected.Add(setting.Id);
            }

            _gridSettings.Clear();

            foreach (NvidiaSetting setting in _viewModel.CustomSettings)
            {
                if (_gridSearchFilter.Length > 0
                    && setting.Name.IndexOf(_gridSearchFilter, StringComparison.OrdinalIgnoreCase) < 0
                    && setting.HexId.IndexOf(_gridSearchFilter, StringComparison.OrdinalIgnoreCase) < 0
                    && !setting.Id.ToString(CultureInfo.InvariantCulture).Contains(_gridSearchFilter, StringComparison.OrdinalIgnoreCase))
                    continue;

                _gridSettings.Add(setting);
            }

            if (NvidiaEditorGrid.ItemsSource is null)
                NvidiaEditorGrid.ItemsSource = _gridSettings;

            foreach (NvidiaSetting setting in _gridSettings)
            {
                if (selected.Contains(setting.Id))
                    NvidiaEditorGrid.SelectedItems.Add(setting);
            }
        }

        private void DeleteSelectedSettings_Click(object sender, RoutedEventArgs e)
        {
            List<NvidiaSetting> selected = new List<NvidiaSetting>();
            foreach (object item in NvidiaEditorGrid.SelectedItems)
            {
                if (item is NvidiaSetting setting)
                    selected.Add(setting);
            }

            if (selected.Count == 0)
                return;

            _viewModel.RemoveCustomSettings(selected);
        }

        private void DeleteAllSettings_Click(object sender, RoutedEventArgs e)
        {
            int count = _viewModel.CustomSettings.Count;
            if (count == 0)
                return;

            MessageBoxResult result = Frontend.ShowMessageBox(
                string.Format(Strings.Menu_Nvidia_Editor_DeleteAllConfirm, count),
                MessageBoxImage.Warning,
                MessageBoxButton.YesNo);

            if (result != MessageBoxResult.Yes)
                return;

            _viewModel.ClearCustomSettings();
        }

        private async void ResetNip_Click(object sender, RoutedEventArgs e)
        {
            if (_resetting)
                return;

            MessageBoxResult confirm = Frontend.ShowMessageBox(
                Strings.Menu_Nvidia_Editor_ResetNipConfirm,
                MessageBoxImage.Warning,
                MessageBoxButton.YesNo);

            if (confirm != MessageBoxResult.Yes)
                return;

            _resetting = true;
            ResetNipButton.IsEnabled = false;

            try
            {
                NvidiaApplyResult result = await Task.Run(() => _viewModel.ResetProfile());
                ReloadGridList();

                Frontend.ShowMessageBox(
                    Describe(result),
                    result.Ok ? MessageBoxImage.Asterisk : MessageBoxImage.Exclamation);
            }
            catch (Exception ex)
            {
                App.Logger.WriteException("NvidiaPage::ResetNip_Click", ex);
                Frontend.ShowMessageBox("Failed to reset the NVIDIA driver profile:\n" + ex.Message, MessageBoxImage.Error);
            }
            finally
            {
                _resetting = false;
                ResetNipButton.IsEnabled = true;
            }
        }

        private void ClearFlagHistory_Click(object sender, RoutedEventArgs e)
        {
            NvidiaFlagHistory.Clear();
        }

        private void Reload_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                _viewModel.ReloadFromDriver();
            }
            catch (Exception ex)
            {
                App.Logger.WriteException("NvidiaPage::Reload_Click", ex);
                Frontend.ShowMessageBox("Failed to read NVIDIA driver settings:\n" + ex.Message, MessageBoxImage.Error);
            }
        }

        private void ExportNip_Click(object sender, RoutedEventArgs e)
        {
            List<NvidiaSetting> snapshot = _viewModel.BuildSettingsSnapshot();

            SaveFileDialog dialog = new SaveFileDialog
            {
                Filter = $"{Strings.FileTypes_NIPFiles}|*.nip",
                FileName = "PhasmaStrap.nip",
            };

            if (dialog.ShowDialog() != true)
                return;

            try
            {
                NvidiaProfileManager.SaveToNip(dialog.FileName, snapshot);
                NvidiaFlagHistory.Log("Exported .nip to " + dialog.FileName);
                Frontend.ShowMessageBox(Strings.Menu_Nvidia_NipExported, MessageBoxImage.Asterisk);
            }
            catch (Exception ex)
            {
                App.Logger.WriteException("NvidiaPage::ExportNip_Click", ex);
                Frontend.ShowMessageBox(string.Format(Strings.Menu_Nvidia_NipExportFailed, ex.Message), MessageBoxImage.Error);
            }
        }

        private void AddCustomSetting_Click(object sender, RoutedEventArgs e)
        {
            AddNvidiaCustomSettingDialog dialog = new AddNvidiaCustomSettingDialog(_viewModel.IsCustomSettingIdTaken)
            {
                Owner = Window.GetWindow(this),
            };

            dialog.ShowDialog();

            if (dialog.Result != MessageBoxResult.OK)
                return;

            _viewModel.AddCustomSetting(dialog.SettingName, dialog.SettingId, dialog.SettingValue);
        }

        private void RemoveCustomSetting_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement element && element.Tag is NvidiaSetting setting)
                _viewModel.RemoveCustomSetting(setting);
        }

        private void CopySettings_Click(object sender, RoutedEventArgs e)
        {
            List<NvidiaSetting> snapshot = _viewModel.BuildSettingsSnapshot();

            CopyNvidiaSettingsDialog dialog = new CopyNvidiaSettingsDialog(snapshot.Count)
            {
                Owner = Window.GetWindow(this),
            };

            dialog.ShowDialog();

            if (dialog.Result != MessageBoxResult.OK)
                return;

            string payload = dialog.SelectedFormat switch
            {
                CopyNvidiaSettingsFormat.SettingRows => string.Join(Environment.NewLine, snapshot.Select(setting => $"{setting.Name} (0x{setting.Id:X8}) = {setting.Value}")),
                CopyNvidiaSettingsFormat.Base64Nip => Convert.ToBase64String(Encoding.Unicode.GetBytes(NvidiaProfileManager.BuildNipText(snapshot))),
                _ => NvidiaProfileManager.BuildNipText(snapshot),
            };

            try
            {
                Clipboard.SetDataObject(payload);
                Frontend.ShowMessageBox(Strings.Menu_Nvidia_SettingsCopiedToClipboard, MessageBoxImage.Asterisk);
            }
            catch (Exception ex)
            {
                App.Logger.WriteException("NvidiaPage::CopySettings_Click", ex);
                Frontend.ShowMessageBox("Could not access the clipboard: " + ex.Message, MessageBoxImage.Error);
            }
        }

        private static string Describe(NvidiaApplyResult result)
        {
            if (result.Failures.Count == 0)
                return result.Message;

            StringBuilder text = new StringBuilder(result.Message);
            text.Append('\n');
            int shown = 0;
            foreach (string failure in result.Failures)
            {
                if (shown++ == 6)
                {
                    text.Append("\n... and ").Append(result.Failures.Count - 6).Append(" more");
                    break;
                }
                text.Append("\n- ").Append(failure);
            }
            return text.ToString();
        }
    }
}
