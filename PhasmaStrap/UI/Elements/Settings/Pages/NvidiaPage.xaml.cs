using System.Windows;

using PhasmaStrap.Integrations.Nvidia;
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
