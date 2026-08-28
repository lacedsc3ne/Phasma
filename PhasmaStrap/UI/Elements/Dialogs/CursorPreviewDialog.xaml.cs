using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

using PhasmaStrap.Models.SettingTasks;
using PhasmaStrap.UI.Elements.Base;

namespace PhasmaStrap.UI.Elements.Dialogs
{
    /// <summary>
    /// Shows thumbnails of whatever recognized cursor image files (ArrowCursor.png,
    /// ArrowFarCursor.png, IBeamCursor.png, MouseLockedCursor.png) exist directly inside a
    /// user-chosen folder, so they can see what a "browse to custom cursor folder" pick will
    /// apply before actually applying it via <see cref="CustomCursorModPresetTask"/>.
    /// </summary>
    public partial class CursorPreviewDialog : WpfUiWindow
    {
        public CursorPreviewDialog(string folderPath)
        {
            InitializeComponent();
            LoadCursorPreviews(folderPath);
        }

        private void LoadCursorPreviews(string folderPath)
        {
            bool foundAny = false;

            foreach (string fileName in CustomCursorModPresetTask.RecognizedFileNames)
            {
                string filePath = Path.Combine(folderPath, fileName);

                if (!File.Exists(filePath))
                    continue;

                foundAny = true;
                CursorStackPanel.Children.Add(CreateCursorPreviewItem(fileName, filePath));
            }

            if (!foundAny)
            {
                CursorStackPanel.Children.Add(new TextBlock
                {
                    Text = "No recognized cursor images were found in this folder.",
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(4)
                });
            }
        }

        private static FrameworkElement CreateCursorPreviewItem(string fileName, string filePath)
        {
            var border = new Border
            {
                BorderBrush = new SolidColorBrush(Colors.Gray),
                BorderThickness = new Thickness(1.0),
                Margin = new Thickness(0, 0, 0, 8),
                Padding = new Thickness(10.0),
                Background = new SolidColorBrush(Colors.Transparent)
            };

            var stackPanel = new StackPanel { Orientation = Orientation.Horizontal };

            var image = new Image
            {
                Width = 32.0,
                Height = 32.0,
                Margin = new Thickness(0.0, 0.0, 10.0, 0.0)
            };

            try
            {
                image.Source = LoadBitmapSafely(filePath);
            }
            catch (Exception ex)
            {
                App.Logger.WriteException("CursorPreviewDialog::CreateCursorPreviewItem", ex);
                image.Source = null;
            }

            var text = new TextBlock
            {
                Text = fileName,
                VerticalAlignment = VerticalAlignment.Center,
                FontSize = 14.0
            };

            stackPanel.Children.Add(image);
            stackPanel.Children.Add(text);
            border.Child = stackPanel;

            return border;
        }

        private static BitmapImage LoadBitmapSafely(string path)
        {
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.UriSource = new Uri(path, UriKind.Absolute);
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
            bitmap.EndInit();
            bitmap.Freeze();
            return bitmap;
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();
    }
}
