using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace PhasmaStrap.UI
{
    internal static class GlobalBackground
    {
        public static (FrameworkElement Image, FrameworkElement Overlay)? TryCreateLayers(string filePath, double overlayOpacity)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
                    return null;

                var overlayLayer = new Border
                {
                    Background = Brushes.Black,
                    Opacity = Math.Clamp(overlayOpacity, 0.0, 1.0),
                    IsHitTestVisible = false
                };

                if (BackgroundLibrary.IsVideo(filePath))
                    return (new VideoBackground(filePath), overlayLayer);

                var image = new Image
                {
                    Stretch = Stretch.UniformToFill,
                    IsHitTestVisible = false,
                    ClipToBounds = true
                };

                GifImageBehavior.SetSourcePath(image, filePath);

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
