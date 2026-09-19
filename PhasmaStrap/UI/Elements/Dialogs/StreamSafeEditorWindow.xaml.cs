using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

using PhasmaStrap.Integrations.Overlays;

namespace PhasmaStrap.UI.Elements.Dialogs
{
    // Marks the parts of the game picture that stream-safe mode hides. The boxes sit on a still of
    // the Roblox window (or a screenshot the user picks), and are kept as fractions of the window.
    public partial class StreamSafeEditorWindow
    {
        public sealed class AreaItem : INotifyPropertyChanged
        {
            private string _name = "";

            public string Name
            {
                get => _name;
                set { _name = value; Changed(nameof(Name)); NameChanged?.Invoke(); }
            }

            public double X { get; set; }
            public double Y { get; set; }
            public double W { get; set; }
            public double H { get; set; }

            public string SizeText => $"{W * 100:0}% x {H * 100:0}%";

            internal Border? Box;
            internal TextBlock? Label;
            internal Action? NameChanged;

            public event PropertyChangedEventHandler? PropertyChanged;

            internal void Changed(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

            public StreamSafeRegion ToRegion() => new StreamSafeRegion { Name = Name.Trim(), X = X, Y = Y, W = W, H = H }.Clamped();
        }

        [Flags]
        private enum Edges { None = 0, Left = 1, Top = 2, Right = 4, Bottom = 8 }

        private const double MinSize = 0.02;
        private const double EdgeGrab = 8;

        private readonly ObservableCollection<AreaItem> _areas = new();
        private bool _changed;
        private bool _applied;

        private AreaItem? _selected;
        private BitmapSource? _picture;

        // the drag in progress
        private AreaItem? _dragItem;
        private Edges _dragEdges;
        private bool _dragCreating;
        private Point _dragStart;
        private Rect _dragFrom;

        public bool Applied => _applied;

        public StreamSafeEditorWindow()
        {
            InitializeComponent();

            AreaList.ItemsSource = _areas;
            foreach (StreamSafeRegion region in StreamSafe.Regions)
                AddArea(region.Name, region.X, region.Y, region.W, region.H);

            _changed = false;
            UpdateButtons();

            Loaded += (_, _) => GrabFromRoblox();
            Closing += OnClosing;
            PreviewKeyDown += OnPreviewKeyDown;
        }

        // ------------------------------------------------------------------ areas

        private AreaItem AddArea(string name, double x, double y, double w, double h)
        {
            var item = new AreaItem { Name = name, X = x, Y = y, W = w, H = h };

            var label = new TextBlock
            {
                Margin = new Thickness(6, 4, 6, 0),
                FontSize = 12,
                FontWeight = FontWeights.SemiBold,
                Foreground = Brushes.White,
                Text = name,
                IsHitTestVisible = false,
                TextTrimming = TextTrimming.CharacterEllipsis,
            };
            label.Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 3, ShadowDepth = 0, Opacity = 0.9 };

            var box = new Border
            {
                Background = FillBrush(),
                BorderThickness = new Thickness(1.5),
                Child = label,
                Cursor = Cursors.SizeAll,
            };
            box.SetResourceReference(Border.BorderBrushProperty, "AccentFillColorDefaultBrush");
            box.MouseLeftButtonDown += (_, e) => BeginDrag(item, e);
            box.MouseMove += (_, e) =>
            {
                if (_dragItem is null)
                    box.Cursor = CursorFor(HitEdges(box, e.GetPosition(box)));
            };

            item.Box = box;
            item.Label = label;
            item.NameChanged = () =>
            {
                label.Text = item.Name;
                _changed = true;
            };

            Stage.Children.Add(box);
            _areas.Add(item);
            PlaceBox(item);
            _changed = true;
            UpdateButtons();
            return item;
        }

        private void RemoveArea(AreaItem item)
        {
            if (item.Box is not null)
                Stage.Children.Remove(item.Box);
            _areas.Remove(item);
            if (ReferenceEquals(_selected, item))
                Select(null);
            _changed = true;
            UpdateButtons();
        }

        private void Select(AreaItem? item)
        {
            _selected = item;
            foreach (AreaItem area in _areas)
            {
                if (area.Box is null)
                    continue;
                bool selected = ReferenceEquals(area, item);
                area.Box.BorderThickness = new Thickness(selected ? 3 : 1.5);
                Panel.SetZIndex(area.Box, selected ? 2 : 1);
            }

            if (!ReferenceEquals(AreaList.SelectedItem, item))
                AreaList.SelectedItem = item;
            UpdateButtons();
        }

        private void UpdateButtons()
        {
            AddButton.IsEnabled = _areas.Count < StreamSafe.MaxRegions;
            DeleteButton.IsEnabled = _selected is not null;
            EmptyText.Visibility = _areas.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            StatusText.Text = _areas.Count >= StreamSafe.MaxRegions ? $"{StreamSafe.MaxRegions} areas is the most there can be." : "";
        }

        // how a hidden area looks: a coarse checker for "Pixelate", nearly solid black for "Black"
        private static Brush FillBrush()
        {
            if (App.Settings.Prop.StreamSafeStyle == StreamSafe.StyleBlack)
                return new SolidColorBrush(Color.FromArgb(0xE0, 0, 0, 0));

            var checker = new DrawingGroup();
            checker.Children.Add(new GeometryDrawing(new SolidColorBrush(Color.FromArgb(0xB0, 0x5A, 0x60, 0x6C)), null, new RectangleGeometry(new Rect(0, 0, 2, 2))));
            checker.Children.Add(new GeometryDrawing(new SolidColorBrush(Color.FromArgb(0xB0, 0x3A, 0x3E, 0x47)), null, new RectangleGeometry(new Rect(0, 0, 1, 1))));
            checker.Children.Add(new GeometryDrawing(new SolidColorBrush(Color.FromArgb(0xB0, 0x3A, 0x3E, 0x47)), null, new RectangleGeometry(new Rect(1, 1, 1, 1))));

            var brush = new DrawingBrush(checker)
            {
                TileMode = TileMode.Tile,
                Viewport = new Rect(0, 0, 24, 24),
                ViewportUnits = BrushMappingMode.Absolute,
                Stretch = Stretch.Fill,
            };
            brush.Freeze();
            return brush;
        }

        // ------------------------------------------------------------------ layout

        private double AspectRatio => _picture is not null && _picture.PixelHeight > 0 ? (double)_picture.PixelWidth / _picture.PixelHeight : 16.0 / 9.0;

        private void StageHost_SizeChanged(object sender, SizeChangedEventArgs e) => LayoutStage();

        private void LayoutStage()
        {
            double hostW = Math.Max(0, StageHost.ActualWidth - 2);
            double hostH = Math.Max(0, StageHost.ActualHeight - 2);
            if (hostW < 1 || hostH < 1)
                return;

            double aspect = AspectRatio;
            double w = Math.Min(hostW, hostH * aspect);
            double h = w / aspect;

            Stage.Width = w;
            Stage.Height = h;
            Picture.Width = w;
            Picture.Height = h;

            PicturePlaceholder.Visibility = _picture is null ? Visibility.Visible : Visibility.Collapsed;
            PicturePlaceholder.Width = Math.Min(420, w - 40);
            Canvas.SetLeft(PicturePlaceholder, (w - PicturePlaceholder.Width) / 2);
            Canvas.SetTop(PicturePlaceholder, h / 2 - 20);

            foreach (AreaItem item in _areas)
                PlaceBox(item);
        }

        private void PlaceBox(AreaItem item)
        {
            if (item.Box is null || double.IsNaN(Stage.Width))
                return;

            Canvas.SetLeft(item.Box, item.X * Stage.Width);
            Canvas.SetTop(item.Box, item.Y * Stage.Height);
            item.Box.Width = Math.Max(1, item.W * Stage.Width);
            item.Box.Height = Math.Max(1, item.H * Stage.Height);
            item.Changed(nameof(AreaItem.SizeText));
        }

        // ------------------------------------------------------------------ dragging

        private static Edges HitEdges(FrameworkElement box, Point p)
        {
            Edges edges = Edges.None;
            if (p.X < EdgeGrab) edges |= Edges.Left;
            if (p.X > box.ActualWidth - EdgeGrab) edges |= Edges.Right;
            if (p.Y < EdgeGrab) edges |= Edges.Top;
            if (p.Y > box.ActualHeight - EdgeGrab) edges |= Edges.Bottom;

            // a box too small to tell its edges apart resizes from the bottom right
            if (edges.HasFlag(Edges.Left | Edges.Right)) edges &= ~Edges.Left;
            if (edges.HasFlag(Edges.Top | Edges.Bottom)) edges &= ~Edges.Top;
            return edges;
        }

        private static Cursor CursorFor(Edges edges) => edges switch
        {
            Edges.Left or Edges.Right => Cursors.SizeWE,
            Edges.Top or Edges.Bottom => Cursors.SizeNS,
            Edges.Left | Edges.Top or Edges.Right | Edges.Bottom => Cursors.SizeNWSE,
            Edges.Right | Edges.Top or Edges.Left | Edges.Bottom => Cursors.SizeNESW,
            _ => Cursors.SizeAll,
        };

        private Point StagePoint(MouseEventArgs e)
        {
            Point p = e.GetPosition(Stage);
            return new Point(Math.Clamp(p.X / Stage.Width, 0, 1), Math.Clamp(p.Y / Stage.Height, 0, 1));
        }

        private void BeginDrag(AreaItem item, MouseButtonEventArgs e)
        {
            Select(item);
            _dragItem = item;
            _dragEdges = HitEdges(item.Box!, e.GetPosition(item.Box));
            _dragCreating = false;
            _dragStart = StagePoint(e);
            _dragFrom = new Rect(item.X, item.Y, item.W, item.H);
            Stage.CaptureMouse();
            e.Handled = true;
        }

        private void Stage_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.Handled || _areas.Count >= StreamSafe.MaxRegions)
                return;

            // a new area, drawn from where the drag starts
            Point p = StagePoint(e);
            AreaItem item = AddArea($"Area {_areas.Count + 1}", p.X, p.Y, 0, 0);
            Select(item);
            _dragItem = item;
            _dragEdges = Edges.Right | Edges.Bottom;
            _dragCreating = true;
            _dragStart = p;
            _dragFrom = new Rect(p.X, p.Y, 0, 0);
            Stage.CaptureMouse();
            e.Handled = true;
        }

        private void Stage_MouseMove(object sender, MouseEventArgs e)
        {
            AreaItem? item = _dragItem;
            if (item is null)
                return;

            Point p = StagePoint(e);

            if (_dragCreating)
            {
                item.X = Math.Min(_dragStart.X, p.X);
                item.Y = Math.Min(_dragStart.Y, p.Y);
                item.W = Math.Abs(p.X - _dragStart.X);
                item.H = Math.Abs(p.Y - _dragStart.Y);
            }
            else
            {
                double dx = p.X - _dragStart.X, dy = p.Y - _dragStart.Y;
                Rect r = _dragFrom;

                if (_dragEdges == Edges.None)
                {
                    item.X = Math.Clamp(r.X + dx, 0, 1 - r.Width);
                    item.Y = Math.Clamp(r.Y + dy, 0, 1 - r.Height);
                }
                else
                {
                    double left = r.Left, top = r.Top, right = r.Right, bottom = r.Bottom;
                    if (_dragEdges.HasFlag(Edges.Left)) left = Math.Clamp(r.Left + dx, 0, right - MinSize);
                    if (_dragEdges.HasFlag(Edges.Right)) right = Math.Clamp(r.Right + dx, left + MinSize, 1);
                    if (_dragEdges.HasFlag(Edges.Top)) top = Math.Clamp(r.Top + dy, 0, bottom - MinSize);
                    if (_dragEdges.HasFlag(Edges.Bottom)) bottom = Math.Clamp(r.Bottom + dy, top + MinSize, 1);

                    item.X = left;
                    item.Y = top;
                    item.W = right - left;
                    item.H = bottom - top;
                }
            }

            _changed = true;
            PlaceBox(item);
        }

        private void Stage_MouseLeftButtonUp(object sender, MouseButtonEventArgs e) => EndDrag();

        private void Stage_LostMouseCapture(object sender, MouseEventArgs e) => EndDrag();

        private void EndDrag()
        {
            AreaItem? item = _dragItem;
            if (item is null)
                return;

            _dragItem = null;
            if (Stage.IsMouseCaptured)
                Stage.ReleaseMouseCapture();

            // a click without a drag doesn't make an area
            if (_dragCreating && (item.W < MinSize || item.H < MinSize))
                RemoveArea(item);
            _dragCreating = false;
        }

        // ------------------------------------------------------------------ list and buttons

        private void AreaList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!ReferenceEquals(AreaList.SelectedItem, _selected))
                Select(AreaList.SelectedItem as AreaItem);
        }

        // typing in an area's name selects that area
        private void AreaItem_GotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
        {
            if (sender is ListBoxItem container)
                container.IsSelected = true;
        }

        private void OnPreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Delete && _selected is not null && Keyboard.FocusedElement is not TextBox)
            {
                RemoveArea(_selected);
                e.Handled = true;
            }
        }

        private void Add_Click(object sender, RoutedEventArgs e)
        {
            if (_areas.Count >= StreamSafe.MaxRegions)
                return;

            // cascade new areas so they don't land exactly on top of each other
            double offset = 0.03 * (_areas.Count % 8);
            Select(AddArea($"Area {_areas.Count + 1}", 0.35 + offset, 0.35 + offset, 0.25, 0.2));
        }

        private void Delete_Click(object sender, RoutedEventArgs e)
        {
            if (_selected is not null)
                RemoveArea(_selected);
        }

        private void Reset_Click(object sender, RoutedEventArgs e)
        {
            foreach (AreaItem item in _areas.ToList())
                RemoveArea(item);
            foreach (StreamSafeRegion region in StreamSafe.Defaults())
                AddArea(region.Name, region.X, region.Y, region.W, region.H);
            Select(null);
        }

        private void Apply()
        {
            App.Settings.Prop.StreamSafeRegions = _areas.Select(a => a.ToRegion()).Where(r => r.W > 0.001 && r.H > 0.001).ToList();
            _changed = false;
            _applied = true;
            App.Logger.WriteLine("StreamSafeEditor", $"{App.Settings.Prop.StreamSafeRegions.Count} areas set - kept on Save");
        }

        private void Apply_Click(object sender, RoutedEventArgs e)
        {
            Apply();
            StatusText.Text = "Applied. Press Save in PhasmaStrap to keep these areas.";
        }

        private void Close_Click(object sender, RoutedEventArgs e) => Close();

        private void OnClosing(object? sender, CancelEventArgs e)
        {
            if (!_changed)
                return;

            MessageBoxResult answer = Frontend.ShowMessageBox("Use these areas?\n\nYes applies them, No closes without changing anything.", MessageBoxImage.Question, MessageBoxButton.YesNoCancel);

            if (answer == MessageBoxResult.Cancel)
                e.Cancel = true;
            else if (answer == MessageBoxResult.Yes)
                Apply();
        }

        // ------------------------------------------------------------------ the picture

        private void Grab_Click(object sender, RoutedEventArgs e) => GrabFromRoblox();

        private void Browse_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Pick a screenshot of Roblox",
                Filter = "Pictures|*.png;*.jpg;*.jpeg;*.bmp|All files|*.*",
            };

            string robloxShots = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "Roblox");
            if (Directory.Exists(robloxShots))
                dialog.InitialDirectory = robloxShots;

            if (dialog.ShowDialog(this) != true)
                return;

            try
            {
                var image = new BitmapImage();
                image.BeginInit();
                image.CacheOption = BitmapCacheOption.OnLoad;
                image.UriSource = new Uri(dialog.FileName);
                image.EndInit();
                image.Freeze();
                ShowPicture(image, $"Showing {Path.GetFileName(dialog.FileName)}.");
            }
            catch (Exception ex)
            {
                PictureStatus.Text = $"That picture couldn't be opened ({ex.Message}).";
            }
        }

        private void ShowPicture(BitmapSource? picture, string status)
        {
            if (picture is not null)
            {
                _picture = picture;
                Picture.Source = picture;
            }
            PictureStatus.Text = status;
            LayoutStage();
        }

        // Copies the Roblox window's current picture. It runs on its own thread and is given up
        // after a couple of seconds, so a busy or frozen Roblox can't hang this window.
        private void GrabFromRoblox()
        {
            PictureStatus.Text = "Looking for Roblox...";

            BitmapSource? result = null;
            string? failure = null;
            var thread = new Thread(() =>
            {
                try
                {
                    result = CaptureRoblox(out failure);
                }
                catch (Exception ex)
                {
                    failure = $"Roblox's picture couldn't be copied ({ex.Message}).";
                }
            })
            {
                IsBackground = true,
                Name = "StreamSafeGrab",
            };
            thread.Start();

            Task.Run(() => thread.Join(2500)).ContinueWith(done =>
            {
                Dispatcher.BeginInvoke(() =>
                {
                    if (!done.Result)
                        ShowPicture(null, "Roblox didn't answer in time. Try again, or use a screenshot.");
                    else if (result is not null)
                        ShowPicture(result, "Showing Roblox as it is now.");
                    else
                        ShowPicture(null, failure ?? "Roblox isn't open. Use a screenshot instead, or place the boxes without one.");
                });
            });
        }

        private static BitmapSource? CaptureRoblox(out string? failure)
        {
            failure = null;

            IntPtr hwnd = IntPtr.Zero;
            foreach (Process process in Process.GetProcessesByName("RobloxPlayerBeta"))
            {
                using (process)
                {
                    if (hwnd == IntPtr.Zero && process.MainWindowHandle != IntPtr.Zero)
                        hwnd = process.MainWindowHandle;
                }
            }

            if (hwnd == IntPtr.Zero)
                return null;

            if (IsIconic(hwnd))
            {
                failure = "Roblox is minimised. Bring it back, then press Grab from Roblox.";
                return null;
            }

            if (!GetClientRect(hwnd, out RECT client) || client.Right < 16 || client.Bottom < 16)
                return null;

            int width = client.Right, height = client.Bottom;
            using var bitmap = new System.Drawing.Bitmap(width, height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            using (var graphics = System.Drawing.Graphics.FromImage(bitmap))
            {
                IntPtr hdc = graphics.GetHdc();
                try
                {
                    // PW_CLIENTONLY | PW_RENDERFULLCONTENT - the second is what makes DirectX windows copy
                    if (!PrintWindow(hwnd, hdc, 0x1 | 0x2))
                        return null;
                }
                finally
                {
                    graphics.ReleaseHdc(hdc);
                }
            }

            var data = bitmap.LockBits(new System.Drawing.Rectangle(0, 0, width, height), System.Drawing.Imaging.ImageLockMode.ReadOnly, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            try
            {
                // some setups hand back an all-black picture instead of failing
                bool anything = false;
                for (int i = 1; i < 64 && !anything; i++)
                {
                    int x = width * (i % 8) / 8 + width / 16, y = height * (i / 8) / 8 + height / 16;
                    int pixel = Marshal.ReadInt32(data.Scan0, y * data.Stride + x * 4);
                    anything = (pixel & 0xFFFFFF) > 0x101010;
                }

                if (!anything)
                {
                    failure = "Roblox's picture came back black. Use a screenshot instead.";
                    return null;
                }

                var source = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgr32, null, data.Scan0, data.Stride * height, data.Stride);
                source.Freeze();
                return source;
            }
            finally
            {
                bitmap.UnlockBits(data);
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT
        {
            public int Left, Top, Right, Bottom;
        }

        [DllImport("user32.dll")]
        private static extern bool PrintWindow(IntPtr hwnd, IntPtr hdc, uint flags);

        [DllImport("user32.dll")]
        private static extern bool GetClientRect(IntPtr hwnd, out RECT rect);

        [DllImport("user32.dll")]
        private static extern bool IsIconic(IntPtr hwnd);
    }
}
