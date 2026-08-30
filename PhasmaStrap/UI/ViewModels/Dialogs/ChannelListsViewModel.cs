using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;
using PhasmaStrap.RobloxInterfaces;

namespace PhasmaStrap.UI.ViewModels.Dialogs
{
    /// <summary>
    /// Backs <see cref="Elements.Dialogs.ChannelListsDialog"/>.
    /// </summary>
    /// <remarks>
    /// Roblox does not publish a live "list every deployment channel" API - channel names are
    /// arbitrary and only discoverable by already knowing them. So unlike a page that lists e.g.
    /// GitHub releases, this dialog works from a small curated list of channel names (the same
    /// ones offered on the main Channel settings page) and resolves each one's currently deployed
    /// version live via the real Roblox client-version endpoint (<see cref="Deployment.GetInfo"/>,
    /// which hits clientsettingscdn.roblox.com / clientsettings.roblox.com).
    /// </remarks>
    public class ChannelListsViewModel : NotifyPropertyChangedViewModel
    {
        // same curated set of commonly-used channels offered on the Channel settings page
        private static readonly string[] KnownChannelNames =
        {
            "production",
            "zcanary",
            "zintegration",
            "zdevelopment",
            "zprerelease"
        };

        public ObservableCollection<DeployInfoDisplay> Channels { get; } = new();

        public ICommand RefreshCommand { get; }

        private bool _isLoading;
        public bool IsLoading
        {
            get => _isLoading;
            set
            {
                if (_isLoading != value)
                {
                    _isLoading = value;
                    OnPropertyChanged(nameof(IsLoading));
                }
            }
        }

        public ChannelListsViewModel()
        {
            RefreshCommand = new AsyncRelayCommand(RefreshAsync);
            _ = RefreshAsync();
        }

        public async Task RefreshAsync()
        {
            if (IsLoading)
                return;

            IsLoading = true;

            try
            {
                var tasks = KnownChannelNames.Select(async channel =>
                {
                    try
                    {
                        var info = await Deployment.GetInfo(channel);
                        return new DeployInfoDisplay
                        {
                            ChannelName = channel,
                            Version = info.Version,
                            VersionGuid = info.VersionGuid
                        };
                    }
                    catch (InvalidChannelException)
                    {
                        return null;
                    }
                    catch (Exception ex)
                    {
                        App.Logger.WriteLine("ChannelListsViewModel::RefreshAsync", $"Failed to resolve channel '{channel}': {ex.Message}");
                        return null;
                    }
                }).ToList();

                var results = await Task.WhenAll(tasks);

                Application.Current.Dispatcher.Invoke(() =>
                {
                    Channels.Clear();
                    foreach (var entry in results.Where(x => x is not null).OrderBy(x => x!.ChannelName))
                        Channels.Add(entry!);
                });
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine("ChannelListsViewModel::RefreshAsync", $"Failed to refresh channels: {ex.Message}");
            }
            finally
            {
                IsLoading = false;
            }
        }
    }

    public class DeployInfoDisplay
    {
        public string ChannelName { get; set; } = null!;
        public string Version { get; set; } = null!;
        public string VersionGuid { get; set; } = null!;
    }
}
