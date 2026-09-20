using System.Windows.Media;

using Wpf.Ui.Common;

namespace PhasmaStrap.UI
{
    public sealed record NotificationStyle(SymbolRegular Symbol, Color Accent, string Label);

    public static class NotificationStyles
    {
        private static Color Hex(string value) => (Color)ColorConverter.ConvertFromString(value);

        private static readonly Dictionary<NotificationKindId, NotificationStyle> Map = new()
        {
            [NotificationKindId.ServerJoined] = new(SymbolRegular.PlayCircle24, Hex("#3FA45B"), "Session"),
            [NotificationKindId.ServerLeft] = new(SymbolRegular.DoorArrowRight20, Hex("#C98A2E"), "Session"),
            [NotificationKindId.ServerRegion] = new(SymbolRegular.GlobeLocation24, Hex("#3E8ED0"), "Session"),
            [NotificationKindId.RobloxClosed] = new(SymbolRegular.Warning24, Hex("#D9534F"), "Crash"),
            [NotificationKindId.AutoRejoin] = new(SymbolRegular.ArrowClockwise24, Hex("#E0873A"), "Rejoin"),
            [NotificationKindId.FastFlagProfile] = new(SymbolRegular.Flag24, Hex("#9A6BD8"), "FastFlags"),
            [NotificationKindId.FrameRateLimit] = new(SymbolRegular.TopSpeed24, Hex("#2FA7A0"), "Frame rate"),
            [NotificationKindId.RamCleaned] = new(SymbolRegular.Broom24, Hex("#4FB3A3"), "Memory"),
            [NotificationKindId.OverlayFocusMode] = new(SymbolRegular.EyeOff24, Hex("#7E8AA2"), "Overlays"),
            [NotificationKindId.PerformanceRun] = new(SymbolRegular.DataTrending24, Hex("#5C7CE0"), "Performance"),
            [NotificationKindId.InviteLink] = new(SymbolRegular.Link24, Hex("#3E8ED0"), "Invite"),
            [NotificationKindId.Screenshot] = new(SymbolRegular.Camera24, Hex("#D06BA8"), "Capture"),
            [NotificationKindId.Replay] = new(SymbolRegular.Video24, Hex("#B76BD0"), "Capture"),
            [NotificationKindId.ReplayNotReady] = new(SymbolRegular.VideoOff24, Hex("#8C8F99"), "Capture"),
            [NotificationKindId.FriendOnline] = new(SymbolRegular.Person24, Hex("#3FA45B"), "Friends"),
            [NotificationKindId.FriendPlaying] = new(SymbolRegular.PeopleTeam24, Hex("#57A87A"), "Friends"),
            [NotificationKindId.AccountGuard] = new(SymbolRegular.ShieldError24, Hex("#D9534F"), "Account Guard"),
            [NotificationKindId.ProxyCertificate] = new(SymbolRegular.Certificate24, Hex("#D9534F"), "Networking"),
            [NotificationKindId.FlagsRemoved] = new(SymbolRegular.Wrench24, Hex("#C98A2E"), "Updates"),
        };

        public static NotificationStyle For(NotificationKindId kind) =>
            Map.TryGetValue(kind, out NotificationStyle? style) ? style : Fallback();

        private static NotificationStyle Fallback()
        {
            Color accent = System.Windows.Application.Current?.Resources["SystemAccentColorPrimary"] is Color themed
                ? themed
                : Hex("#E75147");

            return new NotificationStyle(SymbolRegular.Info24, accent, "PhasmaStrap");
        }
    }
}
