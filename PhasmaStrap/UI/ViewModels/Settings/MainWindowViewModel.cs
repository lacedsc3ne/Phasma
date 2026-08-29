using System.Windows;
using System.Windows.Input;
using PhasmaStrap.UI.Elements.About;
using CommunityToolkit.Mvvm.Input;

namespace PhasmaStrap.UI.ViewModels.Settings
{
    public class MainWindowViewModel : NotifyPropertyChangedViewModel
    {
        public ICommand OpenAboutCommand => new RelayCommand(OpenAbout);

        public ICommand SaveSettingsCommand => new RelayCommand(SaveSettings);

        public ICommand SaveAndLaunchCommand => new RelayCommand(SaveAndLaunch);

        public ICommand RestartCommand => new RelayCommand(Restart);

        public ICommand CloseWindowCommand => new RelayCommand(CloseWindow);

        public EventHandler? RequestSaveNoticeEvent;

        public EventHandler? RequestCloseWindowEvent;

        // read by MainWindow.xaml.cs's Closed handler to decide what to do once the window has
        // actually finished closing - set just before RequestCloseWindowEvent fires
        public bool LaunchAfterClose { get; private set; }

        public bool RestartAfterClose { get; private set; }

        public bool TestModeEnabled
        {
            get => App.LaunchSettings.TestModeFlag.Active;
            set
            {
                if (value)
                {
                    var result = Frontend.ShowMessageBox(Strings.Menu_TestMode_Prompt, MessageBoxImage.Information, MessageBoxButton.YesNo);

                    if (result != MessageBoxResult.Yes)
                        return;
                }

                App.LaunchSettings.TestModeFlag.Active = value;
            }
        }

        private void OpenAbout() => new MainWindow().ShowDialog();

        private void CloseWindow() => RequestCloseWindowEvent?.Invoke(this, EventArgs.Empty);

        private void SaveSettings()
        {
            const string LOG_IDENT = "MainWindowViewModel::SaveSettings";

            App.Settings.Save();
            App.State.Save();
            App.FastFlags.Save();

            foreach (var pair in App.PendingSettingTasks)
            {
                var task = pair.Value;

                if (task.Changed)
                {
                    App.Logger.WriteLine(LOG_IDENT, $"Executing pending task '{task}'");
                    task.Execute();
                }
            }

            App.PendingSettingTasks.Clear();

            RequestSaveNoticeEvent?.Invoke(this, EventArgs.Empty);
        }

        private void SaveAndLaunch()
        {
            SaveSettings();
            LaunchAfterClose = true;
            CloseWindow();
        }

        private void Restart()
        {
            SaveSettings();
            RestartAfterClose = true;
            CloseWindow();
        }
    }
}
