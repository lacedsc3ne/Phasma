using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;

using Wpf.Ui.Controls.Interfaces;

namespace PhasmaStrap.UI
{
    /// <summary>
    /// Forces every currently open window to pick up a language change immediately, instead of
    /// requiring an app restart.
    ///
    /// WPF's <c>{x:Static resources:Strings.X}</c> bindings (used everywhere for localized text)
    /// are resolved exactly once, when the XAML containing them is parsed/loaded - there's no
    /// first-class "live x:Static" mechanism in WPF, unlike a normal <c>{Binding}</c> which can
    /// re-evaluate on a change notification. So changing <see cref="Locale.CurrentCulture"/> (and
    /// therefore what <c>Strings.X</c> now returns) has no effect on text already resolved into an
    /// already-open window - it only affects windows/pages constructed from that point onward.
    ///
    /// The only reliable way to make already-open content pick up the new language is to force it
    /// to be reconstructed, which re-parses its XAML and re-resolves every x:Static reference
    /// against whatever <c>Strings.X</c> now returns for the newly active culture. This mirrors
    /// Voidstrap's LiveLanguageRefresher: for a window hosting Wpf.Ui navigation (the settings,
    /// about and installer windows all do), it clears the navigation's page cache and re-navigates
    /// to whatever page is currently displayed, discarding and rebuilding that <see cref="System.Windows.Controls.Page"/>
    /// instance in place.
    ///
    /// Window chrome - <see cref="Window.Title"/>, <c>ui:TitleBar.Title</c> - is bound the same
    /// x:Static way but lives outside any navigated Page, so it isn't rebuilt by this and stays in
    /// the previous language until the window itself is closed and reopened. Voidstrap has this
    /// same limitation for a plain resx language switch (its window-title refresh only kicks in
    /// as a side effect of its separate machine-translation feature, which isn't part of this).
    /// </summary>
    internal static class LiveLanguageRefresher
    {
        /// <summary>
        /// Re-applies flow direction and reloads the currently displayed page in every open
        /// window, so a language change takes effect immediately instead of after a restart.
        /// </summary>
        public static void RefreshAllOpenWindows()
        {
            Application? app = Application.Current;
            if (app is null)
                return;

            app.Dispatcher.BeginInvoke((Action)(() =>
            {
                foreach (Window window in app.Windows)
                {
                    try
                    {
                        ApplyFlowDirection(window);
                        RefreshWindow(window);
                    }
                    catch
                    {
                        // best-effort - a window that can't be refreshed just keeps showing its
                        // current text until it's closed and reopened, same as before this existed
                    }
                }
            }), DispatcherPriority.Background);
        }

        private static void ApplyFlowDirection(Window window)
        {
            FlowDirection flowDirection = Locale.RightToLeft ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
            window.FlowDirection = flowDirection;

            if (window.ContextMenu is { } contextMenu)
                contextMenu.FlowDirection = flowDirection;
        }

        private static void RefreshWindow(Window window)
        {
            INavigation? navigation = FindNavigation(window);
            if (navigation is null)
                return;

            int currentIndex;
            try
            {
                currentIndex = navigation.SelectedPageIndex;
            }
            catch
            {
                return;
            }

            if (currentIndex < 0)
                return;

            try
            {
                navigation.ClearCache();
            }
            catch
            {
                return;
            }

            window.Dispatcher.BeginInvoke((Action)(() =>
            {
                try
                {
                    navigation.Navigate(currentIndex);
                }
                catch
                {
                }
            }), DispatcherPriority.Background);
        }

        private static INavigation? FindNavigation(DependencyObject root)
        {
            int count = VisualTreeHelper.GetChildrenCount(root);
            for (int i = 0; i < count; i++)
            {
                DependencyObject child = VisualTreeHelper.GetChild(root, i);

                if (child is INavigation navigation)
                    return navigation;

                INavigation? found = FindNavigation(child);
                if (found is not null)
                    return found;
            }

            return null;
        }
    }
}
