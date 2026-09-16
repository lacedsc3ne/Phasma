using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

using PhasmaStrap.UI.ViewModels.Settings;
using PhasmaStrap.Utility;

namespace PhasmaStrap.UI.Elements.Settings.Pages
{
    /// <summary>
    /// Interaction logic for ModsPage.xaml
    /// </summary>
    public partial class ModsPage
    {
        private const string ManagedModDragFormat = "PhasmaStrap.ManagedModItem";

        private Point _dragHandleStartPoint;
        private ManagedModItem? _dragHandleItem;

        private ModsViewModel ViewModel => (ModsViewModel)DataContext;

        public ModsPage()
        {
            DataContext = new ModsViewModel();
            InitializeComponent();
        }

        #region Mod Management - drag handle reordering

        private void ManagedModDragHandle_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            _dragHandleStartPoint = e.GetPosition(null);
            _dragHandleItem = (sender as FrameworkElement)?.DataContext as ManagedModItem;
        }

        private void ManagedModDragHandle_MouseMove(object sender, MouseEventArgs e)
        {
            if (_dragHandleItem is null || e.LeftButton != MouseButtonState.Pressed)
                return;

            Point position = e.GetPosition(null);
            Vector delta = _dragHandleStartPoint - position;

            if (Math.Abs(delta.X) < SystemParameters.MinimumHorizontalDragDistance &&
                Math.Abs(delta.Y) < SystemParameters.MinimumVerticalDragDistance)
                return;

            var data = new DataObject(ManagedModDragFormat, _dragHandleItem);
            DragDrop.DoDragDrop((DependencyObject)sender, data, DragDropEffects.Move);
            _dragHandleItem = null;
        }

        private void ManagedModCard_DragEnter(object sender, DragEventArgs e)
        {
            if (sender is Border border && e.Data.GetDataPresent(ManagedModDragFormat))
                border.BorderBrush = Brushes.DodgerBlue;
        }

        private void ManagedModCard_DragLeave(object sender, DragEventArgs e)
        {
            if (sender is Border border)
                border.ClearValue(Border.BorderBrushProperty);
        }

        private void ManagedModCard_DragOver(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(ManagedModDragFormat))
            {
                e.Effects = DragDropEffects.Move;
                e.Handled = true;
            }
            else if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                e.Effects = DragDropEffects.Copy;
                e.Handled = true;
            }
            else
            {
                e.Effects = DragDropEffects.None;
            }
        }

        private async void ManagedModCard_Drop(object sender, DragEventArgs e)
        {
            if (sender is not Border border)
                return;

            border.ClearValue(Border.BorderBrushProperty);

            if (border.DataContext is not ManagedModItem target)
                return;

            if (e.Data.GetDataPresent(ManagedModDragFormat))
            {
                if (e.Data.GetData(ManagedModDragFormat) is not ManagedModItem source)
                    return;

                // top half of the card = insert before, bottom half = insert after
                double relativeY = e.GetPosition(border).Y;
                bool insertAfter = relativeY > border.ActualHeight / 2;

                e.Handled = true;
                await ViewModel.ReorderManagedModAsync(source, target, insertAfter);
                return;
            }

            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                e.Handled = true;
                CopyDroppedFilesIntoManagedMod(target, (string[])e.Data.GetData(DataFormats.FileDrop));
                await ViewModel.LoadManagedModsAsync();
            }
        }

        private void ManagedModsList_DragOver(object sender, DragEventArgs e)
        {
            e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
            e.Handled = true;
        }

        private async void ManagedModsList_Drop(object sender, DragEventArgs e)
        {
            // dropped on empty space rather than an existing card - create a new managed mod
            // from whatever was dropped
            if (!e.Data.GetDataPresent(DataFormats.FileDrop))
                return;

            string[] paths = (string[])e.Data.GetData(DataFormats.FileDrop);
            if (paths.Length == 0)
                return;

            try
            {
                string name = paths.Length == 1 ? Path.GetFileNameWithoutExtension(paths[0]) : "New Mod";
                var record = ManagedModStore.Create(name);
                string folder = ManagedModStore.GetFolder(record.Id);

                CopyDroppedPathsInto(folder, paths);

                await ViewModel.LoadManagedModsAsync();
            }
            catch (Exception ex)
            {
                Frontend.ShowMessageBox("The dropped files could not be added as a mod:\n" + ex.Message, MessageBoxImage.Warning);
            }
        }

        private static void CopyDroppedFilesIntoManagedMod(ManagedModItem item, string[] paths)
        {
            try
            {
                string folder = ManagedModStore.GetFolder(item.Id);
                CopyDroppedPathsInto(folder, paths);
            }
            catch (Exception ex)
            {
                Frontend.ShowMessageBox("Those files could not be added to the mod:\n" + ex.Message, MessageBoxImage.Warning);
            }
        }

        private static void CopyDroppedPathsInto(string folder, string[] paths)
        {
            Directory.CreateDirectory(folder);

            foreach (string path in paths)
            {
                if (Directory.Exists(path))
                {
                    CopyDirectoryInto(path, Path.Combine(folder, Path.GetFileName(path)));
                }
                else if (File.Exists(path))
                {
                    File.Copy(path, Path.Combine(folder, Path.GetFileName(path)), true);
                }
            }
        }

        private static void CopyDirectoryInto(string sourceDirectory, string destinationDirectory)
        {
            Directory.CreateDirectory(destinationDirectory);

            foreach (string file in Directory.GetFiles(sourceDirectory))
                File.Copy(file, Path.Combine(destinationDirectory, Path.GetFileName(file)), true);

            foreach (string subdirectory in Directory.GetDirectories(sourceDirectory))
                CopyDirectoryInto(subdirectory, Path.Combine(destinationDirectory, Path.GetFileName(subdirectory)));
        }

        #endregion
    }
}
