using System;
using System.Collections.Generic;
using System.Linq;

using PhasmaStrap.UI.Elements.Settings.Pages;

namespace PhasmaStrap.UI.Elements.Settings
{
    internal static class SectionHosts
    {
        private static readonly Dictionary<Type, Type> Hosts = Build();

        private static Dictionary<Type, Type> Build()
        {
            var map = new Dictionary<Type, Type>();

            void Add(Type host, params Type[] sections)
            {
                foreach (Type section in sections)
                    map[section] = host;
            }

            Add(typeof(PeoplePage), typeof(AccountsPage), typeof(FriendsPage), typeof(ActivityPage));
            Add(typeof(PerformancePage),
                typeof(RenderingLowEndPage), typeof(RenderingSystemPage), typeof(RenderingFrameRatePage),
                typeof(RenderingBoostPage), typeof(RenderingResolutionPage), typeof(RenderingPerGamePage),
                typeof(OverlaysHudPage), typeof(OverlaysCrosshairPage), typeof(OverlaysStreamPage),
                typeof(RiShadePage), typeof(NvidiaPage), typeof(NetworkingPage), typeof(GBSEditorPage));
            Add(typeof(LaunchingPage), typeof(BehaviourPage), typeof(ClassicClientPage), typeof(ModsPage));
            Add(typeof(LookAndFeelPage), typeof(AppearancePage), typeof(NotificationsPage), typeof(HotkeysPage), typeof(ShortcutsPage));
            Add(typeof(AppPage), typeof(PhasmaStrapPage), typeof(IntegrationsPage), typeof(ReleasesPage), typeof(DeveloperToolsPage));
            Add(typeof(FastFlagSettingsPage), typeof(FastFlagsPage), typeof(FastFlagEditorPage), typeof(FastFlagGamesPage), typeof(AssetWarpPage));
            Add(typeof(CapturePage), typeof(CaptureScreenshotsPage), typeof(CaptureReplayPage), typeof(CaptureStoragePage));

            return map;
        }

        public static Type? HostFor(Type? pageType) =>
            pageType is not null && Hosts.TryGetValue(pageType, out Type? host) ? host : null;

        public static Type Resolve(Type pageType) => HostFor(pageType) ?? pageType;

        public static bool IsSection(Type pageType) => Hosts.ContainsKey(pageType);
    }
}
