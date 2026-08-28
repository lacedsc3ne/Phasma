using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;
using PhasmaStrap.Integrations;
using PhasmaStrap.Models;

namespace PhasmaStrap.UI.ViewModels.Settings
{
    public class HistoryViewModel : NotifyPropertyChangedViewModel
    {
        private string _statusText = "";

        public ObservableCollection<PlayTimeEntry> Entries { get; } = new();

        public string StatusText
        {
            get => _statusText;
            private set { _statusText = value; OnPropertyChanged(nameof(StatusText)); }
        }

        public ICommand RefreshCommand => new RelayCommand(LoadEntries);

        public ICommand LaunchCommand => new RelayCommand<PlayTimeEntry>(Launch);

        public ICommand CopyLinkCommand => new RelayCommand<PlayTimeEntry>(CopyLink);

        public HistoryViewModel()
        {
            LoadEntries();
        }

        private void LoadEntries()
        {
            Entries.Clear();

            foreach (PlayTimeEntry entry in PlayTimeStore.GetAll())
                Entries.Add(entry);

            StatusText = Entries.Count == 0
                ? "No games played yet - your play time will show up here once you've played something."
                : $"{Entries.Count} game(s) tracked.";
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
                App.Logger.WriteLine("HistoryViewModel", $"Failed to copy link: {ex.Message}");
            }
        }
    }
}
