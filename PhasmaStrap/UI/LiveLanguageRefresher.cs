using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;

using Wpf.Ui.Controls.Interfaces;

namespace PhasmaStrap.UI
{
    internal static class LiveLanguageRefresher
    {
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
