using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;
using PhasmaStrap.Integrations;
using PhasmaStrap.Models;

namespace PhasmaStrap.UI.ViewModels.Settings
{
    public class HomeViewModel : NotifyPropertyChangedViewModel
    {
        private const int MaxContinuePlaying = 6;

        private string _statusText = "";

        public ObservableCollection<PlayTimeEntry> ContinuePlaying { get; } = new();

        public string WelcomeText => $"Welcome back, {Environment.UserName}.";

        public string StatusText
        {
            get => _statusText;
            private set { _statusText = value; OnPropertyChanged(nameof(StatusText)); }
        }

        public bool IsEmpty => ContinuePlaying.Count == 0;

        public ICommand RefreshCommand => new RelayCommand(LoadEntries);

        public ICommand LaunchCommand => new RelayCommand<PlayTimeEntry>(Launch);

        public ICommand CopyLinkCommand => new RelayCommand<PlayTimeEntry>(CopyLink);

        public ICommand LaunchRobloxCommand => new RelayCommand(LaunchRoblox);

        public HomeViewModel()
        {
            LoadEntries();
        }

        private void LoadEntries()
        {
            ContinuePlaying.Clear();

            foreach (PlayTimeEntry entry in PlayTimeStore.GetAll().OrderByDescending(x => x.LastPlayed).Take(MaxContinuePlaying))
                ContinuePlaying.Add(entry);

            StatusText = ContinuePlaying.Count == 0
                ? "No games played yet - games you play will show up here."
                : $"Showing your {ContinuePlaying.Count} most recently played game(s).";

            OnPropertyChanged(nameof(IsEmpty));
        }

        private static void Launch(PlayTimeEntry? entry)
        {
            if (entry is null || entry.PlaceId <= 0)
                return;

            string uri = $"roblox://experiences/start?placeId={entry.PlaceId}";
            Process.Start(Paths.Process, $"-player \"{uri}\"");
        }

        private static void CopyLink(PlayTimeEntry? entry)
        {
            if (entry is null || entry.PlaceId <= 0)
                return;

            try
            {
                Clipboard.SetText($"https://www.roblox.com/games/{entry.PlaceId}");
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine("HomeViewModel", $"Failed to copy link: {ex.Message}");
            }
        }

        private static void LaunchRoblox()
        {
            Process.Start(Paths.Process, "-player \"roblox://\"");
        }
    }
}
