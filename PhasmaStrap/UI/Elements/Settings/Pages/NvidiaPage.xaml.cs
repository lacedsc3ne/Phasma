using System.Windows;

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
        private readonly NvidiaViewModel _viewModel;

        private bool _applying;

        public NvidiaPage()
        {
            _viewModel = new NvidiaViewModel();
            DataContext = _viewModel;
            InitializeComponent();
        }

        private async void Apply_Click(object sender, RoutedEventArgs e)
        {
            if (_applying)
                return;

            _applying = true;
            ApplyButton.IsEnabled = false;

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
            }
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
                Frontend.ShowMessageBox(Strings.Menu_Nvidia_NipExported, MessageBoxImage.Asterisk);
            }
            catch (Exception ex)
            {
                App.Logger.WriteException("NvidiaPage::ExportNip_Click", ex);
                Frontend.ShowMessageBox(string.Format(Strings.Menu_Nvidia_NipExportFailed, ex.Message), MessageBoxImage.Error);
            }
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
