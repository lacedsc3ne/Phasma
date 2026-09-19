using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;

using PhasmaStrap.Models;

namespace PhasmaStrap.UI.Elements.Dialogs
{
    // The join-time server picker's window (see Networking.JoinPickerPolicy). The game's join
    // request is on hold while this is open, so it closes by itself when the time is up -
    // ChosenJobId stays null then, which means "let Roblox choose".
    public partial class ServerPickerWindow
    {
        private sealed class Row
        {
            public string JobId { get; init; } = "";
            public string Region { get; init; } = "";
            public string Detail { get; init; } = "";
            public string Players { get; init; } = "";
            public string Ping { get; init; } = "";
        }

        private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(100) };
        private readonly DateTime _opened = DateTime.UtcNow;
        private readonly TimeSpan _limit;

        public string? ChosenJobId { get; private set; }

        public ServerPickerWindow(long placeId, Task<List<MatchmakerCandidate>> search, TimeSpan limit)
        {
            InitializeComponent();

            // a little less than the policy's own limit: the answer still has to reach Roblox
            _limit = limit - TimeSpan.FromSeconds(3);

            _timer.Tick += (_, _) =>
            {
                double left = 1 - (DateTime.UtcNow - _opened).TotalSeconds / _limit.TotalSeconds;
                TimeBar.Value = Math.Max(0, left);

                if (left <= 0)
                    Close();
            };
            _timer.Start();

            Closed += (_, _) => _timer.Stop();

            _ = FillAsync(placeId, search);
        }

        private async Task FillAsync(long placeId, Task<List<MatchmakerCandidate>> search)
        {
            List<MatchmakerCandidate> servers;

            try
            {
                servers = await search;
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine("ServerPickerWindow", $"Server search failed: {ex.Message}");
                servers = new List<MatchmakerCandidate>();
            }

            if (!IsVisible)
                return;

            Searching.Visibility = Visibility.Collapsed;

            if (servers.Count == 0)
            {
                // nothing to choose from - no reason to keep the game waiting
                HeadlineText.Text = "No servers could be listed";
                DetailText.Text = "Roblox will choose one. (The picker needs you to be signed in, and the game to have public servers.)";
                await Task.Delay(1800);
                Close();
                return;
            }

            ServerList.ItemsSource = servers.Select(server => new Row
            {
                JobId = server.JobId,
                Region = server.DatacenterName,
                Detail = $"{server.DistanceKm:0} km away",
                Players = server.MaxPlayers > 0 ? $"{server.Playing} / {server.MaxPlayers}" : $"{server.Playing} playing",
                Ping = $"~{server.EstimatedPingMs} ms",
            }).ToList();

            ServerList.Visibility = Visibility.Visible;
            ServerList.SelectedIndex = 0;
            ServerList.Focus();

            int regions = servers.Select(s => s.DatacenterName).Distinct().Count();
            HeadlineText.Text = $"{servers.Count} servers in {regions} region{(regions == 1 ? "" : "s")}";
        }

        private void ServerList_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e) =>
            JoinButton.IsEnabled = ServerList.SelectedItem is Row;

        private void Choose()
        {
            if (ServerList.SelectedItem is not Row row)
                return;

            ChosenJobId = row.JobId;
            Close();
        }

        private void Join_Click(object sender, RoutedEventArgs e) => Choose();

        private void ServerList_MouseDoubleClick(object sender, MouseButtonEventArgs e) => Choose();

        private void Skip_Click(object sender, RoutedEventArgs e) => Close();

        private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                Choose();
                e.Handled = true;
            }
            else if (e.Key == Key.Escape)
            {
                Close();
                e.Handled = true;
            }
        }
    }
}
