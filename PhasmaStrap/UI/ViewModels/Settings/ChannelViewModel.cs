using System.Collections.ObjectModel;
using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;
using PhasmaStrap.Integrations;
using PhasmaStrap.RobloxInterfaces;

namespace PhasmaStrap.UI.ViewModels.Settings
{
    public class ChannelViewModel : NotifyPropertyChangedViewModel
    {
        // known public Roblox deployment channels people commonly switch to
        public IEnumerable<string> KnownChannels { get; } = new[]
        {
            "production",
            "zcanary",
            "zintegration",
            "zdevelopment",
            "zprerelease"
        };

        public string RobloxChannel
        {
            get => App.Settings.Prop.RobloxChannel;
            set => App.Settings.Prop.RobloxChannel = value?.Trim().ToLowerInvariant() ?? "";
        }

        public IEnumerable<ChannelChangeMode> ChannelChangeModes { get; } = Enum.GetValues(typeof(ChannelChangeMode)).Cast<ChannelChangeMode>();

        public ChannelChangeMode SelectedChannelChangeMode
        {
            get => App.Settings.Prop.ChannelChangeMode;
            set => App.Settings.Prop.ChannelChangeMode = value;
        }

        public string CurrentActiveChannel => Deployment.Channel;

        public record MirrorChoice(string Display, string Url);

        // "" represents auto-selecting the fastest responding mirror
        public IEnumerable<MirrorChoice> MirrorChoices { get; } =
            new[] { "" }.Concat(Deployment.Mirrors).Select(url => new MirrorChoice(Describe(url), url));

        public string PreferredMirror
        {
            get => App.Settings.Prop.PreferredMirror;
            set => App.Settings.Prop.PreferredMirror = value ?? "";
        }

        private static string Describe(string url) =>
            String.IsNullOrEmpty(url) ? "Auto (fastest responding server)" : new Uri(url).Host;

        // ---- Update heatmap (Integrations.RobloxUpdateHeatmapService, ported from Voidstrap) ----

        public sealed class DayBarItem
        {
            public string Label { get; init; } = "";
            public int Count { get; init; }
            public double BarHeight { get; init; } = 6;
        }

        private static readonly (DayOfWeek Day, string Label)[] WeekOrder =
        {
            (DayOfWeek.Monday, "Mon"),
            (DayOfWeek.Tuesday, "Tue"),
            (DayOfWeek.Wednesday, "Wed"),
            (DayOfWeek.Thursday, "Thu"),
            (DayOfWeek.Friday, "Fri"),
            (DayOfWeek.Saturday, "Sat"),
            (DayOfWeek.Sunday, "Sun")
        };

        private string _heatmapPlaceId = "";
        private bool _isHeatmapLoading;
        private string _heatmapStatusText = "Enter a place ID to see which days it typically updates on.";

        public string HeatmapPlaceId
        {
            get => _heatmapPlaceId;
            set { _heatmapPlaceId = value; OnPropertyChanged(nameof(HeatmapPlaceId)); }
        }

        public bool IsHeatmapLoading
        {
            get => _isHeatmapLoading;
            private set { _isHeatmapLoading = value; OnPropertyChanged(nameof(IsHeatmapLoading)); }
        }

        public string HeatmapStatusText
        {
            get => _heatmapStatusText;
            private set { _heatmapStatusText = value; OnPropertyChanged(nameof(HeatmapStatusText)); }
        }

        public ObservableCollection<DayBarItem> HeatmapDays { get; } = new();

        public ICommand LoadHeatmapCommand => new AsyncRelayCommand(LoadHeatmapAsync);

        private async Task LoadHeatmapAsync()
        {
            if (!long.TryParse(HeatmapPlaceId.Trim(), out long placeId) || placeId <= 0)
            {
                HeatmapStatusText = "Enter a valid numeric place ID.";
                return;
            }

            IsHeatmapLoading = true;
            HeatmapStatusText = "Resolving place...";
            HeatmapDays.Clear();

            try
            {
                long? universeId = await RobloxUpdateHeatmapService.ResolveUniverseIdAsync(placeId);
                if (universeId is null)
                {
                    HeatmapStatusText = "Could not resolve a universe for that place ID.";
                    return;
                }

                HeatmapStatusText = "Loading update history...";
                RobloxUpdateHeatmapResult result = await RobloxUpdateHeatmapService.GetAsync(universeId.Value);

                if (!result.Success || result.TotalEvents == 0)
                {
                    HeatmapStatusText = result.ErrorMessage ?? "No update history found for this place.";
                    return;
                }

                int max = result.DayCounts.Values.DefaultIfEmpty(0).Max();

                foreach ((DayOfWeek day, string label) in WeekOrder)
                {
                    int count = result.DayCounts.TryGetValue(day, out int c) ? c : 0;
                    double height = max > 0 ? 6 + (count / (double)max) * 74 : 6;
                    HeatmapDays.Add(new DayBarItem { Label = label, Count = count, BarHeight = height });
                }

                DayOfWeek topDay = result.DayCounts.OrderByDescending(kv => kv.Value).First().Key;
                HeatmapStatusText = $"{result.TotalEvents} update event(s) found. Most common day: {topDay}.";
            }
            catch (Exception ex)
            {
                HeatmapStatusText = $"Failed to load: {ex.Message}";
            }
            finally
            {
                IsHeatmapLoading = false;
            }
        }
    }
}
