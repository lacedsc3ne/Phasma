using System.Collections.ObjectModel;
using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;
using PhasmaStrap.Integrations;
using PhasmaStrap.Models;

namespace PhasmaStrap.UI.ViewModels.Settings
{
    public class ServerBrowserViewModel : NotifyPropertyChangedViewModel
    {
        private string _placeId = "";
        private bool _isSearching;
        private string _statusText = "Enter a place ID and search.";

        public string PlaceId
        {
            get => _placeId;
            set { _placeId = value; OnPropertyChanged(nameof(PlaceId)); }
        }

        public bool IsSearching
        {
            get => _isSearching;
            private set { _isSearching = value; OnPropertyChanged(nameof(IsSearching)); }
        }

        public string StatusText
        {
            get => _statusText;
            private set { _statusText = value; OnPropertyChanged(nameof(StatusText)); }
        }

        public ObservableCollection<ServerListItem> Servers { get; } = new();

        public ICommand SearchCommand => new AsyncRelayCommand(SearchAsync);

        public ICommand JoinCommand => new RelayCommand<ServerListItem>(server =>
        {
            if (server is null || !long.TryParse(PlaceId.Trim(), out long placeId))
                return;

            ServerBrowser.JoinServer(placeId, server.JobId);
        });

        private async Task SearchAsync()
        {
            if (!long.TryParse(PlaceId.Trim(), out long placeId) || placeId <= 0)
            {
                StatusText = "Enter a valid numeric place ID.";
                return;
            }

            IsSearching = true;
            StatusText = "Searching...";
            Servers.Clear();

            try
            {
                List<ServerListItem> servers = await ServerBrowser.ListPublicServersAsync(placeId);

                foreach (ServerListItem server in servers)
                    Servers.Add(server);

                StatusText = servers.Count > 0 ? $"Found {servers.Count} public server(s)." : "No public servers found for this place.";
            }
            catch (Exception ex)
            {
                StatusText = $"Search failed: {ex.Message}";
            }
            finally
            {
                IsSearching = false;
            }
        }
    }
}
