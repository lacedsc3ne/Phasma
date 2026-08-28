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
    }
}
