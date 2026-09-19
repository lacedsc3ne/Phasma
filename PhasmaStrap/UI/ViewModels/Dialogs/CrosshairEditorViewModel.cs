using System.Collections.ObjectModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

using CommunityToolkit.Mvvm.Input;

using PhasmaStrap.Integrations.Overlays;

namespace PhasmaStrap.UI.ViewModels.Dialogs
{
    public sealed class CrosshairChoice
    {
        public CrosshairStyle Style { get; init; } = new();
        public string Name => Style.Name.Length > 0 ? Style.Name : "Untitled";
        public ImageSource? Thumbnail { get; init; }
        public bool IsMine { get; init; }
        public Visibility DeleteVisibility => IsMine ? Visibility.Visible : Visibility.Collapsed;
    }

    public sealed class CrosshairBackdrop
    {
        public string Name { get; init; } = "";
        public Brush Brush { get; init; } = Brushes.Black;
        public override string ToString() => Name;
    }

    public sealed class CrosshairEditorViewModel : NotifyPropertyChangedViewModel
    {
        private const int PreviewSize = 256;

        private CrosshairStyle _style;
        private string _original;

        public ObservableCollection<CrosshairChoice> BuiltIn { get; } = new();
        public ObservableCollection<CrosshairChoice> Mine { get; } = new();
        public List<CrosshairBackdrop> Backdrops { get; } = new();

        public event Action? Applied;

        public CrosshairEditorViewModel()
        {
            _style = CrosshairStyles.Current.Copy();
            _original = _style.Signature;

            foreach (CrosshairStyle style in CrosshairStyle.BuiltIn())
                BuiltIn.Add(new CrosshairChoice { Style = style, Thumbnail = Render(style, 64) });

            RefreshMine();
            BuildBackdrops();
            _backdrop = Backdrops[0];

            RenderPreview();
        }

        // ------------------------------------------------------------------ preview

        private ImageSource? _preview;
        public ImageSource? Preview { get => _preview; private set { _preview = value; OnPropertyChanged(nameof(Preview)); } }

        public int[] ZoomOptions { get; } = { 1, 2, 3, 4 };

        private int _zoom = 2;
        public int Zoom
        {
            get => _zoom;
            set { _zoom = value; OnPropertyChanged(nameof(Zoom)); OnPropertyChanged(nameof(PreviewPixels)); }
        }

        public double PreviewPixels => PreviewSize * _zoom;

        private CrosshairBackdrop _backdrop;
        public CrosshairBackdrop Backdrop { get => _backdrop; set { _backdrop = value ?? Backdrops[0]; OnPropertyChanged(nameof(Backdrop)); } }

        private void BuildBackdrops()
        {
            Backdrops.Add(new CrosshairBackdrop { Name = "Dark", Brush = Frozen(new SolidColorBrush(Color.FromRgb(0x24, 0x26, 0x2C))) });
            Backdrops.Add(new CrosshairBackdrop { Name = "Light", Brush = Frozen(new SolidColorBrush(Color.FromRgb(0xDC, 0xE1, 0xE8))) });
            Backdrops.Add(new CrosshairBackdrop
            {
                Name = "Sky and grass",
                Brush = Frozen(new LinearGradientBrush(new GradientStopCollection
                {
                    new GradientStop(Color.FromRgb(0x6F, 0xB6, 0xFF), 0.0),
                    new GradientStop(Color.FromRgb(0xCF, 0xE8, 0xFF), 0.49),
                    new GradientStop(Color.FromRgb(0x4E, 0x9A, 0x3A), 0.51),
                    new GradientStop(Color.FromRgb(0x2F, 0x6B, 0x22), 1.0),
                }, 90)),
            });

            // the newest screenshot, centred - the most honest background there is
            try
            {
                if (Directory.Exists(PhasmaStrap.Utility.ScreenshotCapture.ScreenshotsDir))
                {
                    FileInfo? newest = new DirectoryInfo(PhasmaStrap.Utility.ScreenshotCapture.ScreenshotsDir).GetFiles("*.png").OrderByDescending(f => f.LastWriteTime).FirstOrDefault();
                    if (newest is not null)
                    {
                        var image = new BitmapImage();
                        image.BeginInit();
                        image.CacheOption = BitmapCacheOption.OnLoad;
                        image.UriSource = new Uri(newest.FullName);
                        image.EndInit();
                        image.Freeze();

                        // 1:1 pixels around the middle of the screen, where the crosshair sits
                        var brush = new ImageBrush(image) { Stretch = Stretch.None, AlignmentX = AlignmentX.Center, AlignmentY = AlignmentY.Center };
                        Backdrops.Add(new CrosshairBackdrop { Name = "My last screenshot", Brush = Frozen(brush) });
                    }
                }
            }
            catch
            {
            }
        }

        private static Brush Frozen(Brush brush)
        {
            brush.Freeze();
            return brush;
        }

        private static ImageSource Render(CrosshairStyle style, int size)
        {
            // thumbnails show the design at its real in-game size
            int canvas = size;
            using System.Drawing.Bitmap bitmap = CrosshairRenderer.Render(style, canvas);

            var data = bitmap.LockBits(new System.Drawing.Rectangle(0, 0, canvas, canvas), System.Drawing.Imaging.ImageLockMode.ReadOnly, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            try
            {
                BitmapSource source = BitmapSource.Create(canvas, canvas, 96, 96, PixelFormats.Bgra32, null, data.Scan0, data.Stride * canvas, data.Stride);
                source.Freeze();
                return source;
            }
            finally
            {
                bitmap.UnlockBits(data);
            }
        }

        private void RenderPreview()
        {
            Preview = Render(_style, PreviewSize);
            OnPropertyChanged(nameof(ShareCode));
            OnPropertyChanged(nameof(HasChanges));
            OnPropertyChanged(nameof(EmptyVisibility));
        }

        public bool HasChanges => _style.Signature != _original;
        public Visibility EmptyVisibility => _style.HasVisibleParts ? Visibility.Collapsed : Visibility.Visible;

        // ------------------------------------------------------------------ the design's values

        private T Get<T>(Func<CrosshairStyle, T> read) => read(_style);

        private void Set(Action<CrosshairStyle> write, [CallerMemberName] string property = "")
        {
            write(_style);
            OnPropertyChanged(property);
            RenderPreview();
        }

        public bool Arms { get => Get(s => s.Arms); set => Set(s => s.Arms = value); }
        public int ArmLength { get => Get(s => s.ArmLength); set => Set(s => s.ArmLength = value); }
        public int ArmThickness { get => Get(s => s.ArmThickness); set => Set(s => s.ArmThickness = value); }
        public int Gap { get => Get(s => s.Gap); set => Set(s => s.Gap = value); }
        public bool TStyle { get => Get(s => s.TStyle); set => Set(s => s.TStyle = value); }
        public int Rotation { get => Get(s => s.Rotation); set => Set(s => s.Rotation = value); }

        public bool Dot { get => Get(s => s.Dot); set => Set(s => s.Dot = value); }
        public int DotSize { get => Get(s => s.DotSize); set => Set(s => s.DotSize = value); }
        public bool DotRound { get => Get(s => s.DotRound); set => Set(s => s.DotRound = value); }

        public bool Ring { get => Get(s => s.Ring); set => Set(s => s.Ring = value); }
        public int RingRadius { get => Get(s => s.RingRadius); set => Set(s => s.RingRadius = value); }
        public int RingThickness { get => Get(s => s.RingThickness); set => Set(s => s.RingThickness = value); }

        public double Opacity { get => Get(s => s.Opacity); set => Set(s => s.Opacity = value); }

        public bool Outline { get => Get(s => s.Outline); set => Set(s => s.Outline = value); }
        public int OutlineThickness { get => Get(s => s.OutlineThickness); set => Set(s => s.OutlineThickness = value); }

        // colours: typed hex, applied once it is a valid colour
        public string ColorText
        {
            get => Get(s => s.Color);
            set
            {
                string normalised = CrosshairStyle.NormaliseColor(value, "");
                if (normalised.Length > 0)
                    Set(s => s.Color = normalised, nameof(ColorText));
                OnPropertyChanged(nameof(ColorBrush));
            }
        }

        public string OutlineColorText
        {
            get => Get(s => s.OutlineColor);
            set
            {
                string normalised = CrosshairStyle.NormaliseColor(value, "");
                if (normalised.Length > 0)
                    Set(s => s.OutlineColor = normalised, nameof(OutlineColorText));
                OnPropertyChanged(nameof(OutlineColorBrush));
            }
        }

        public Brush ColorBrush => ToBrush(_style.Color);
        public Brush OutlineColorBrush => ToBrush(_style.OutlineColor);

        private static Brush ToBrush(string hex)
        {
            try
            {
                return Frozen(new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)));
            }
            catch
            {
                return Brushes.Transparent;
            }
        }

        public string[] Swatches { get; } = { "#00FF00", "#00FFFF", "#FFFFFF", "#FF3B3B", "#FFE066", "#FF66D9", "#7C5CFF", "#000000" };

        public ICommand PickColorCommand => new RelayCommand<string>(hex => { if (hex is not null) { ColorText = hex; OnPropertyChanged(nameof(ColorText)); } });
        public ICommand PickOutlineColorCommand => new RelayCommand<string>(hex => { if (hex is not null) { OutlineColorText = hex; OnPropertyChanged(nameof(OutlineColorText)); } });

        private void LoadStyle(CrosshairStyle style)
        {
            _style = style.Clamped().Copy();

            foreach (string property in new[]
            {
                nameof(Arms), nameof(ArmLength), nameof(ArmThickness), nameof(Gap), nameof(TStyle), nameof(Rotation),
                nameof(Dot), nameof(DotSize), nameof(DotRound), nameof(Ring), nameof(RingRadius), nameof(RingThickness),
                nameof(Opacity), nameof(Outline), nameof(OutlineThickness), nameof(ColorText), nameof(OutlineColorText),
                nameof(ColorBrush), nameof(OutlineColorBrush), nameof(DesignName),
            })
                OnPropertyChanged(property);

            RenderPreview();
        }

        public ICommand UseChoiceCommand => new RelayCommand<CrosshairChoice?>(choice =>
        {
            if (choice is not null)
                LoadStyle(choice.Style);
        });

        // ------------------------------------------------------------------ my designs

        public string DesignName { get => _style.Name; set { _style.Name = value ?? ""; OnPropertyChanged(nameof(DesignName)); } }

        private string _status = "";
        public string Status { get => _status; private set { _status = value; OnPropertyChanged(nameof(Status)); } }

        private void RefreshMine()
        {
            Mine.Clear();
            foreach (CrosshairStyle style in App.Settings.Prop.CrosshairLibrary)
                Mine.Add(new CrosshairChoice { Style = style.Clamped(), Thumbnail = Render(style, 64), IsMine = true });

            OnPropertyChanged(nameof(MineEmptyVisibility));
        }

        public Visibility MineEmptyVisibility => Mine.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        public ICommand SaveDesignCommand => new RelayCommand(() =>
        {
            string name = _style.Name.Trim();
            if (name.Length == 0)
            {
                Status = "Give the design a name first.";
                return;
            }

            CrosshairStyle copy = _style.Clamped();
            List<CrosshairStyle> library = App.Settings.Prop.CrosshairLibrary;
            int existing = library.FindIndex(s => string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase));

            if (existing >= 0)
                library[existing] = copy;
            else
                library.Add(copy);

            App.Settings.Save();
            RefreshMine();
            Status = existing >= 0 ? $"Updated \"{name}\"." : $"Saved \"{name}\" to your designs.";
        });

        public ICommand DeleteDesignCommand => new RelayCommand<CrosshairChoice?>(choice =>
        {
            if (choice is null)
                return;

            App.Settings.Prop.CrosshairLibrary.RemoveAll(s => string.Equals(s.Name, choice.Style.Name, StringComparison.OrdinalIgnoreCase));
            App.Settings.Save();
            RefreshMine();
            Status = $"Deleted \"{choice.Name}\".";
        });

        // ------------------------------------------------------------------ sharing

        public string ShareCode => _style.ToShareCode();

        private string _importCode = "";
        public string ImportCode { get => _importCode; set { _importCode = value ?? ""; OnPropertyChanged(nameof(ImportCode)); } }

        public ICommand CopyCodeCommand => new RelayCommand(() =>
        {
            Status = PhasmaStrap.Utility.ClipboardShare.CopyText(ShareCode) ? "Code copied - anyone with PhasmaStrap can paste it into their editor." : "Could not reach the clipboard - try again.";
        });

        public ICommand ImportCodeCommand => new RelayCommand(() =>
        {
            CrosshairStyle? style = CrosshairStyle.FromShareCode(_importCode);
            if (style is null)
            {
                Status = "That is not a crosshair code (they start with PHX1-).";
                return;
            }

            LoadStyle(style);
            ImportCode = "";
            Status = $"Loaded{(style.Name.Length > 0 ? $" \"{style.Name}\"" : "")}. Press Apply to use it, or save it to your designs.";
        });

        // ------------------------------------------------------------------ apply

        public ICommand ApplyCommand => new RelayCommand(Apply);

        public void Apply()
        {
            App.Settings.Prop.CrosshairActive = _style.Clamped();
            App.Settings.Save();
            _original = _style.Signature;
            OnPropertyChanged(nameof(HasChanges));
            Applied?.Invoke();
            Status = App.Settings.Prop.Crosshair
                ? "Applied. A game that is running picks it up within a second or two."
                : "Applied. The crosshair itself is switched off - turn it on on the Rendering page.";
        }
    }
}
