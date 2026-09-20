using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace PhasmaStrap.UI.Elements.Controls
{
    public static class ScrollBubbling
    {
        public static readonly DependencyProperty EnabledProperty = DependencyProperty.RegisterAttached(
            "Enabled", typeof(bool), typeof(ScrollBubbling), new PropertyMetadata(false, OnEnabledChanged));

        public static bool GetEnabled(DependencyObject obj) => (bool)obj.GetValue(EnabledProperty);

        public static void SetEnabled(DependencyObject obj, bool value) => obj.SetValue(EnabledProperty, value);

        private static void OnEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is not UIElement element)
                return;

            if ((bool)e.NewValue)
                element.PreviewMouseWheel += OnPreviewMouseWheel;
            else
                element.PreviewMouseWheel -= OnPreviewMouseWheel;
        }

        private static void OnPreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (sender is not UIElement element || e.Handled)
                return;

            ScrollViewer? inner = element as ScrollViewer ?? FindDescendant<ScrollViewer>(element);
            if (inner is null)
                return;

            bool atTop = inner.VerticalOffset <= 0.5;
            bool atBottom = inner.VerticalOffset >= inner.ScrollableHeight - 0.5;
            bool scrollingUp = e.Delta > 0;

            bool innerCanHandle = inner.ScrollableHeight > 0 && ((scrollingUp && !atTop) || (!scrollingUp && !atBottom));
            if (innerCanHandle)
                return;

            e.Handled = true;

            var forwarded = new MouseWheelEventArgs(e.MouseDevice, e.Timestamp, e.Delta)
            {
                RoutedEvent = UIElement.MouseWheelEvent,
                Source = sender,
            };

            DependencyObject? parent = VisualTreeHelper.GetParent(element);
            (parent as UIElement)?.RaiseEvent(forwarded);
        }

        private static T? FindDescendant<T>(DependencyObject root) where T : DependencyObject
        {
            int count = VisualTreeHelper.GetChildrenCount(root);
            for (int i = 0; i < count; i++)
            {
                DependencyObject child = VisualTreeHelper.GetChild(root, i);
                if (child is T match)
                    return match;

                T? nested = FindDescendant<T>(child);
                if (nested is not null)
                    return nested;
            }

            return null;
        }
    }
}
