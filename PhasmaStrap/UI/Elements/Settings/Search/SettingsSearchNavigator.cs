using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Navigation;
using System.Windows.Threading;

using Wpf.Ui.Controls;
using Wpf.Ui.Controls.Interfaces;

using PhasmaStrap.UI.Elements.Controls;

namespace PhasmaStrap.UI.Elements.Settings.Search
{
    /// <summary>
    /// Takes a search result and actually gets the user to it: navigates to the page, switches to
    /// the right tab, waits for any embedded page frame to load, expands the group the option lives
    /// in, scrolls it into view and flashes a highlight around it.
    /// </summary>
    internal static class SettingsSearchNavigator
    {
        private const string LOG_IDENT = "SettingsSearchNavigator";

        public static void Reveal(INavigation navigation, Frame frame, SettingsSearchEntry entry)
        {
            bool alreadyThere = frame.Content is FrameworkElement current && current.GetType() == entry.PageType;

            if (!alreadyThere)
            {
                NavigatedEventHandler? handler = null;
                handler = (_, _) =>
                {
                    frame.Navigated -= handler;
                    if (frame.Content is FrameworkElement page && page.GetType() == entry.PageType)
                        RunWhenLoaded(page, () => RevealOnPage(page, entry));
                };

                frame.Navigated += handler;
                navigation.Navigate(entry.PageType);
                return;
            }

            RunWhenLoaded((FrameworkElement)frame.Content, () => RevealOnPage((FrameworkElement)frame.Content, entry));
        }

        private static void RunWhenLoaded(FrameworkElement element, Action action)
        {
            if (element.IsLoaded)
            {
                element.Dispatcher.BeginInvoke(action, DispatcherPriority.Loaded);
                return;
            }

            RoutedEventHandler? handler = null;
            handler = (_, _) =>
            {
                element.Loaded -= handler;
                element.Dispatcher.BeginInvoke(action, DispatcherPriority.Loaded);
            };
            element.Loaded += handler;
        }

        // a page that was just navigated to (or a tab that was just selected) may not have its
        // visual tree built yet when the dispatcher gets to us - poll briefly rather than give up
        private const int MaxAttempts = 15;

        private static void RetryLater(FrameworkElement owner, Action action)
        {
            var timer = new DispatcherTimer(DispatcherPriority.Background, owner.Dispatcher) { Interval = TimeSpan.FromMilliseconds(120) };
            timer.Tick += (_, _) =>
            {
                timer.Stop();
                action();
            };
            timer.Start();
        }

        // which tab to open on the page the sidebar navigates to: for an entry living inside an
        // embedded page that's the host tab holding the frame; for a Tab entry it's the tab itself
        private static string TabOnHostPage(SettingsSearchEntry entry)
        {
            if (entry.NestedPageType is not null)
                return entry.HostTab;
            return entry.Kind == SettingsSearchEntryKind.Tab ? entry.Header : entry.Tab;
        }

        private static string TabInsideNestedPage(SettingsSearchEntry entry)
        {
            return entry.Kind == SettingsSearchEntryKind.Tab ? entry.Header : entry.Tab;
        }

        private static void RevealOnPage(FrameworkElement page, SettingsSearchEntry entry, int attempt = 0)
        {
            try
            {
                string tabName = TabOnHostPage(entry);

                // 1. tab - a TabItem's content is presented by the TabControl's content presenter,
                //    not inside the TabItem's own visual tree, so after selecting it the search for
                //    the control still runs from the page root (only the selected tab's content is
                //    realised, so there's no risk of matching another tab's controls)
                if (tabName.Length > 0)
                {
                    TabItem? tab = Descendants<TabItem>(page).FirstOrDefault(t => HeaderText(t.Header) == tabName);

                    if (tab is null)
                    {
                        if (attempt < MaxAttempts)
                        {
                            RetryLater(page, () => RevealOnPage(page, entry, attempt + 1));
                            return;
                        }

                        string seen = string.Join(" | ", Descendants<TabItem>(page).Select(t => HeaderText(t.Header)));
                        App.Logger.WriteLine(LOG_IDENT, $"Tab '{tabName}' not found on {page.GetType().Name} (tabs seen: {seen})");
                    }
                    else
                    {
                        bool changed = !tab.IsSelected;
                        SelectTab(tab);

                        if (entry.Kind == SettingsSearchEntryKind.Tab && entry.NestedPageType is null)
                        {
                            Highlight(tab);
                            return;
                        }

                        if (changed)
                        {
                            // let the newly selected tab's content get templated/laid out first
                            page.Dispatcher.BeginInvoke(() => RevealAfterTab(page, entry), DispatcherPriority.Loaded);
                            return;
                        }
                    }
                }

                RevealAfterTab(page, entry);
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine(LOG_IDENT, $"Reveal failed for '{entry.Header}': {ex.Message}");
            }
        }

        private static void SelectTab(TabItem tab)
        {
            if (ItemsControl.ItemsControlFromItemContainer(tab) is TabControl tabControl)
                tabControl.SelectedItem = tab;
            else if (tab.Parent is TabControl parent)
                parent.SelectedItem = tab;
        }

        private static void RevealAfterTab(FrameworkElement page, SettingsSearchEntry entry)
        {
            try
            {
                // 2. embedded page frame
                if (entry.NestedPageType is not null)
                {
                    Frame? nested = Descendants<Frame>(page).FirstOrDefault();
                    if (nested is null)
                    {
                        App.Logger.WriteLine(LOG_IDENT, $"No frame found for {entry.NestedPageType.Name}");
                        return;
                    }

                    if (nested.Content is FrameworkElement nestedPage && nestedPage.GetType() == entry.NestedPageType)
                    {
                        RunWhenLoaded(nestedPage, () => RevealInNestedPage(nestedPage, entry));
                    }
                    else
                    {
                        LoadCompletedEventHandler? handler = null;
                        handler = (_, _) =>
                        {
                            nested.LoadCompleted -= handler;
                            if (nested.Content is FrameworkElement loaded)
                                RunWhenLoaded(loaded, () => RevealInNestedPage(loaded, entry));
                        };
                        nested.LoadCompleted += handler;
                    }

                    return;
                }

                RevealTarget(page, entry);
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine(LOG_IDENT, $"Reveal failed for '{entry.Header}': {ex.Message}");
            }
        }

        // 3. inside an embedded page: open its own tab (if the entry sits on one), then find the control
        private static void RevealInNestedPage(FrameworkElement nestedPage, SettingsSearchEntry entry, int attempt = 0)
        {
            try
            {
                string tabName = TabInsideNestedPage(entry);

                if (tabName.Length > 0)
                {
                    TabItem? tab = Descendants<TabItem>(nestedPage).FirstOrDefault(t => HeaderText(t.Header) == tabName);

                    if (tab is null)
                    {
                        if (attempt < MaxAttempts)
                        {
                            RetryLater(nestedPage, () => RevealInNestedPage(nestedPage, entry, attempt + 1));
                            return;
                        }

                        App.Logger.WriteLine(LOG_IDENT, $"Tab '{tabName}' not found on embedded {nestedPage.GetType().Name}");
                    }
                    else
                    {
                        bool changed = !tab.IsSelected;
                        SelectTab(tab);

                        if (entry.Kind == SettingsSearchEntryKind.Tab)
                        {
                            Highlight(tab);
                            return;
                        }

                        if (changed)
                        {
                            nestedPage.Dispatcher.BeginInvoke(() => RevealTarget(nestedPage, entry), DispatcherPriority.Loaded);
                            return;
                        }
                    }
                }

                RevealTarget(nestedPage, entry);
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine(LOG_IDENT, $"Reveal failed for '{entry.Header}': {ex.Message}");
            }
        }

        private static void RevealTarget(DependencyObject scope, SettingsSearchEntry entry, int attempt = 0)
        {
            FrameworkElement? target = FindTarget(scope, entry);
            if (target is null)
            {
                if (attempt < MaxAttempts && scope is FrameworkElement owner)
                {
                    RetryLater(owner, () => RevealTarget(scope, entry, attempt + 1));
                    return;
                }

                App.Logger.WriteLine(LOG_IDENT, $"Could not find '{entry.Header}' ({entry.Kind}) on {entry.PageType.Name}");
                return;
            }

            ExpandAncestors(target);

            // expanding runs an animation and the expander's content only gets measured on a later
            // layout pass - scrolling straight away would aim at the still-collapsed position, so
            // scroll on a Background-priority pass, then once more after that layout settles
            target.Dispatcher.BeginInvoke(() =>
            {
                target.UpdateLayout();
                ScrollTo(target);

                target.Dispatcher.BeginInvoke(() =>
                {
                    ScrollTo(target);
                    target.Dispatcher.BeginInvoke(() => Highlight(target), DispatcherPriority.Background);
                }, DispatcherPriority.Background);
            }, DispatcherPriority.Background);
        }

        // centres the target in the nearest scrollable ancestor rather than BringIntoView's
        // "just barely visible at the bottom edge"
        private static void ScrollTo(FrameworkElement target)
        {
            ScrollViewer? viewer = null;
            DependencyObject? current = GetParent(target);
            while (current is not null)
            {
                if (current is ScrollViewer sv && sv.ScrollableHeight > 0 && !sv.CanContentScroll)
                {
                    viewer = sv;
                    break;
                }

                current = GetParent(current);
            }

            if (viewer is null || !target.IsVisible)
            {
                double height = target.ActualHeight > 0 ? target.ActualHeight : 40;
                target.BringIntoView(new Rect(0, -32, Math.Max(target.ActualWidth, 1), height + 64));
                return;
            }

            try
            {
                Point position = target.TransformToAncestor(viewer).Transform(new Point(0, 0));
                double targetTop = viewer.VerticalOffset + position.Y;
                double centred = targetTop - (viewer.ViewportHeight - target.ActualHeight) / 2;
                viewer.ScrollToVerticalOffset(Math.Max(0, Math.Min(centred, viewer.ScrollableHeight)));
            }
            catch (Exception)
            {
                target.BringIntoView();
            }
        }

        private static FrameworkElement? FindTarget(DependencyObject scope, SettingsSearchEntry entry)
        {
            switch (entry.Kind)
            {
                case SettingsSearchEntryKind.Option:
                    return (FrameworkElement?)Descendants<OptionControl>(scope).FirstOrDefault(o => (o.Header ?? "") == entry.Header)
                        ?? (FrameworkElement?)Descendants<ToggleSwitch>(scope).FirstOrDefault(t => HeaderText(t.Content) == entry.Header)
                        ?? (FrameworkElement?)Descendants<CheckBox>(scope).FirstOrDefault(c => HeaderText(c.Content) == entry.Header)
                        // rows built from data templates (hotkey bindings, lists) - land on the row's title text
                        ?? Descendants<TextBlock>(scope).FirstOrDefault(t => !IsIcon(t) && t.Text == entry.Header);

                case SettingsSearchEntryKind.Group:
                    return (FrameworkElement?)Descendants<CardExpander>(scope).FirstOrDefault(c => HeaderText(c.Header) == entry.Header)
                        ?? Descendants<System.Windows.Controls.Expander>(scope).FirstOrDefault(c => HeaderText(c.Header) == entry.Header);

                case SettingsSearchEntryKind.Section:
                    return Descendants<TextBlock>(scope).FirstOrDefault(t => !IsIcon(t) && t.Text == entry.Header);

                case SettingsSearchEntryKind.Action:
                    return Descendants<System.Windows.Controls.Button>(scope).FirstOrDefault(b => HeaderText(b.Content) == entry.Header);

                case SettingsSearchEntryKind.Tab:
                    return Descendants<TabItem>(scope).FirstOrDefault(t => HeaderText(t.Header) == entry.Header);

                default:
                    return null;
            }
        }

        private static void ExpandAncestors(DependencyObject element)
        {
            DependencyObject? current = element;
            while (current is not null)
            {
                switch (current)
                {
                    case System.Windows.Controls.Expander expander when !expander.IsExpanded:
                        expander.IsExpanded = true;
                        break;
                    case Controls.Expander custom when !custom.IsExpanded:
                        custom.IsExpanded = true;
                        break;
                    case TabItem tab when !tab.IsSelected:
                        if (ItemsControl.ItemsControlFromItemContainer(tab) is TabControl tabControl)
                            tabControl.SelectedItem = tab;
                        break;
                }

                current = GetParent(current);
            }
        }

        private static DependencyObject? GetParent(DependencyObject element)
        {
            DependencyObject? parent = element is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(element) : null;
            return parent ?? LogicalTreeHelper.GetParent(element);
        }

        /// <summary>Text of a Header/Content object: a string, or the first TextBlock inside an element.</summary>
        internal static string HeaderText(object? header)
        {
            switch (header)
            {
                case null:
                    return "";
                case string s:
                    return s;
                case TextBlock tb when !IsIcon(tb):
                    return tb.Text ?? "";
                case DependencyObject d:
                    // wpfui's SymbolIcon/FontIcon derive from TextBlock - skip them or a tab header
                    // like [icon][Text] would resolve to the icon's glyph
                    return Descendants<TextBlock>(d).FirstOrDefault(t => !IsIcon(t) && !string.IsNullOrWhiteSpace(t.Text))?.Text ?? header.ToString() ?? "";
                default:
                    return header.ToString() ?? "";
            }
        }

        // wpfui's SymbolIcon/FontIcon render their glyph through a TextBlock inside their template -
        // a TextBlock whose (near) ancestor is one of those is the icon glyph, not real text
        private static bool IsIcon(TextBlock tb)
        {
            DependencyObject? current = tb;
            for (int depth = 0; current is not null && depth < 6; depth++)
            {
                if (current is Wpf.Ui.Controls.SymbolIcon || current is Wpf.Ui.Controls.FontIcon)
                    return true;

                current = VisualTreeHelper.GetParent(current);
            }

            return false;
        }

        internal static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
        {
            var stack = new Stack<DependencyObject>();
            stack.Push(root);

            while (stack.Count > 0)
            {
                DependencyObject node = stack.Pop();

                if (!ReferenceEquals(node, root) && node is T match)
                    yield return match;

                // visual children first (this is what's actually on screen)...
                int visualChildren = node is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetChildrenCount(node) : 0;
                for (int i = visualChildren - 1; i >= 0; i--)
                    stack.Push(VisualTreeHelper.GetChild(node, i));

                // ...then logical children not yet realised as visuals (unselected TabItems' content,
                // collapsed expanders' InnerContent)
                if (visualChildren == 0)
                {
                    foreach (object child in LogicalTreeHelper.GetChildren(node))
                    {
                        if (child is DependencyObject d)
                            stack.Push(d);
                    }
                }
            }
        }

        // --- highlight ---

        private static void Highlight(FrameworkElement target)
        {
            try
            {
                AdornerLayer? layer = AdornerLayer.GetAdornerLayer(target);
                if (layer is null)
                    return;

                var adorner = new HighlightAdorner(target);
                layer.Add(adorner);

                var fade = new DoubleAnimation(1.0, 0.0, TimeSpan.FromMilliseconds(1800))
                {
                    BeginTime = TimeSpan.FromMilliseconds(500),
                    EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn },
                };
                fade.Completed += (_, _) => layer.Remove(adorner);
                adorner.BeginAnimation(UIElement.OpacityProperty, fade);
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine(LOG_IDENT, $"Highlight failed: {ex.Message}");
            }
        }

        private sealed class HighlightAdorner : Adorner
        {
            private readonly Pen _pen;
            private readonly Brush _fill;

            public HighlightAdorner(UIElement adorned) : base(adorned)
            {
                IsHitTestVisible = false;

                Brush accent = (adorned as FrameworkElement)?.TryFindResource("AccentFillColorDefaultBrush") as Brush ?? Brushes.DodgerBlue;
                _pen = new Pen(accent, 2) { LineJoin = PenLineJoin.Round };

                var fill = accent.Clone();
                fill.Opacity = 0.12;
                fill.Freeze();
                _fill = fill;
            }

            protected override void OnRender(DrawingContext drawingContext)
            {
                var rect = new Rect(AdornedElement.RenderSize);
                rect.Inflate(3, 3);
                drawingContext.DrawRoundedRectangle(_fill, _pen, rect, 8, 8);
            }
        }
    }
}
