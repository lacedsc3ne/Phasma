using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

// Adapted from Voidstrap (UI/GlobalBackground.cs). Voidstrap's version is a large static
// subsystem: it persists its own JSON settings file, can target every open window, supports
// video backgrounds via MediaElement, and reparents each window's Content into a dedicated host
// Grid. That doesn't fit PhasmaStrap cleanly - the settings/state model here is a single
// Settings.cs object (not a side file), and only the settings window's background is in scope.
//
// This trimmed port keeps only the visible payoff: a static or animated-GIF image plus a dimming
// overlay, built as plain elements that WpfUiWindow inserts directly into its root Grid (the same
// place it already inserts its glass tint Border) - no window reparenting, no video support.
namespace PhasmaStrap.UI
{
    internal static class GlobalBackground
    {
        // builds the (image, overlay) pair to insert behind a window's content; returns null if
        // the configured background image can't be used (missing/unreadable file)
        public static (FrameworkElement Image, FrameworkElement Overlay)? TryCreateLayers(string filePath, double overlayOpacity)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
                    return null;

                var image = new Image
                {
                    Stretch = Stretch.UniformToFill,
                    IsHitTestVisible = false,
                    ClipToBounds = true
                };
                GifImageBehavior.SetSourcePath(image, filePath);
                ImageFx.SetSmoothLoad(image, true);

                var overlay = new Border
                {
                    Background = Brushes.Black,
                    Opacity = Math.Clamp(overlayOpacity, 0.0, 1.0),
                    IsHitTestVisible = false
                };

                return (image, overlay);
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine("GlobalBackground::TryCreateLayers", $"Failed to build background layers: {ex.Message}");
                return null;
            }
        }
    }
}
