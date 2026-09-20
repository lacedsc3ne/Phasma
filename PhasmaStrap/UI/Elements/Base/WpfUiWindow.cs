using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;
using Wpf.Ui.Mvvm.Contracts;
using Wpf.Ui.Mvvm.Services;

namespace PhasmaStrap.UI.Elements.Base
{
    public abstract class WpfUiWindow : UiWindow
    {
        private static readonly IThemeService _themeService = new ThemeService();

        private static string? _appliedThemeKey;

        private static readonly Color PhasmaAccent = Color.FromRgb(0xF4, 0x55, 0x4B);

        private static readonly SolidColorBrush DarkGlassTintBrush = new(Color.FromArgb(232, 0x0E, 0x0E, 0x12));
        private static readonly SolidColorBrush LightGlassTintBrush = new(Color.FromArgb(225, 0xF6, 0xF6, 0xF9));

        private Border? _tintLayer;
        private FrameworkElement? _backgroundImageLayer;
        private FrameworkElement? _backgroundOverlayLayer;
        private string _backgroundPath = "";
        private Grid? _rootGrid;

        private static SolidColorBrush CurrentGlassTint =>
            App.Settings.Prop.Theme.GetFinal() == Enums.Theme.Dark ? DarkGlassTintBrush : LightGlassTintBrush;

        protected virtual bool UseDefaultEntranceAnimation => true;

        private readonly ScaleTransform _entranceScale = new(0.95, 0.95);
        private readonly TranslateTransform _entranceTranslate = new(0, 18);

        public WpfUiWindow()
        {
            ApplyAppTheme(force: false);
            ApplyWindowTheme();

            if (Application.Current.Resources["PhasmaBody"] is System.Windows.Media.FontFamily phasmaBody)
                FontFamily = phasmaBody;

            if (UseDefaultEntranceAnimation)
                Loaded += WpfUiWindow_Loaded;
        }

        private void WpfUiWindow_Loaded(object sender, RoutedEventArgs e)
        {
            if (Content is not FrameworkElement content)
                return;

            var scaleEase = new QuadraticEase { EasingMode = EasingMode.EaseOut };

            content.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromSeconds(0.28)) { EasingFunction = scaleEase });
            _entranceScale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(0.95, 1, TimeSpan.FromSeconds(0.35)) { EasingFunction = scaleEase });
            _entranceScale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(0.95, 1, TimeSpan.FromSeconds(0.35)) { EasingFunction = scaleEase });
            _entranceTranslate.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(18, 0, TimeSpan.FromSeconds(0.35)) { EasingFunction = scaleEase });
        }

        public void ApplyTheme()
        {
            ApplyAppTheme(force: true);
            ApplyWindowTheme();
        }

        private static void ApplyAppTheme(bool force)
        {
            const int customThemeIndex = 2;

            string key = $"{App.Settings.Prop.Theme.GetFinal()}|{App.Settings.Prop.CustomColorThemeEnabled}";
            if (!force && key == _appliedThemeKey)
                return;

            _appliedThemeKey = key;

            _themeService.SetTheme(App.Settings.Prop.Theme.GetFinal() == Enums.Theme.Dark ? ThemeType.Dark : ThemeType.Light);
            _themeService.SetAccent(PhasmaAccent);

            var dict = new ResourceDictionary { Source = new Uri($"pack://application:,,,/UI/Style/{Enum.GetName(App.Settings.Prop.Theme.GetFinal())}.xaml") };
            Application.Current.Resources.MergedDictionaries[customThemeIndex] = dict;

            ApplyAppColorTheme();
            PublishAccentBrushes();
        }

        private void ApplyWindowTheme()
        {
            if (_tintLayer is not null)
                _tintLayer.Background = CurrentGlassTint;

#if QA_BUILD
            this.BorderBrush = System.Windows.Media.Brushes.Red;
            this.BorderThickness = new Thickness(4);
#endif
        }

        private const int AppColorThemeIndex = 4;

        public static void ApplyAccentFrom(ResourceDictionary? theme)
        {
            Color accent = PhasmaAccent;

            if (theme is not null && theme.Contains(PhasmaStrap.Utility.AppColorTheme.AccentColorKey)
                && theme[PhasmaStrap.Utility.AppColorTheme.AccentColorKey] is Color custom)
            {
                accent = Color.FromRgb(custom.R, custom.G, custom.B);
            }

            Wpf.Ui.Appearance.Accent.Apply(accent, Wpf.Ui.Appearance.Theme.GetAppTheme());
            PublishAccentBrushes();
        }

        private static readonly string[] AccentColorKeys =
        {
            "SystemAccentColor",
            "SystemAccentColorPrimary",
            "SystemAccentColorSecondary",
            "SystemAccentColorTertiary",
        };

        public static void PublishAccentBrushes()
        {
            foreach (string colorKey in AccentColorKeys)
            {
                if (Application.Current.Resources[colorKey] is not Color color)
                    continue;

                var brush = new SolidColorBrush(color);
                brush.Freeze();

                Application.Current.Resources[colorKey + "Brush"] = brush;
            }

            if (Application.Current.Resources["SystemAccentColorPrimary"] is Color primary)
                App.Logger.WriteLine("Theme", $"Accent brushes now follow the app accent ({primary})");
        }

        private static void ApplyAppColorTheme()
        {
            var dictionaries = Application.Current.Resources.MergedDictionaries;

            if (!App.Settings.Prop.CustomColorThemeEnabled)
            {
                if (dictionaries.Count > AppColorThemeIndex)
                    dictionaries.RemoveAt(AppColorThemeIndex);
                return;
            }

            var overrides = PhasmaStrap.Utility.AppColorTheme.LoadForApp();
            ApplyAccentFrom(overrides);

            if (dictionaries.Count > AppColorThemeIndex)
                dictionaries[AppColorThemeIndex] = overrides;
            else
                dictionaries.Add(overrides);
        }

        public static void ApplyThemeToAllOpenWindows()
        {
            ApplyAppTheme(force: true);

            foreach (Window window in Application.Current.Windows)
            {
                if (window is WpfUiWindow wpfUiWindow)
                    wpfUiWindow.ApplyWindowTheme();
            }
        }

        public static void RefreshGlobalBackgroundOnAllWindows()
        {
            foreach (Window window in Application.Current.Windows)
            {
                if (window is WpfUiWindow wpfUiWindow)
                    wpfUiWindow.RefreshGlobalBackground();
            }
        }

        private void RefreshGlobalBackground()
        {
            if (_rootGrid is null || this is not PhasmaStrap.UI.Elements.Settings.MainWindow)
                return;

            string wantedPath = App.Settings.Prop.GlobalBackgroundEnabled ? App.Settings.Prop.GlobalBackgroundFilePath ?? "" : "";

            string? testBackground = Environment.GetEnvironmentVariable("PHASMASTRAP_UITEST_BACKGROUND") == "1" ? Environment.GetEnvironmentVariable("PHASMASTRAP_UITEST_BACKGROUNDFILE") : null;
            if (!string.IsNullOrEmpty(testBackground))
                wantedPath = testBackground;

            if (_backgroundImageLayer is not null && _backgroundOverlayLayer is not null
                && wantedPath.Length > 0 && string.Equals(wantedPath, _backgroundPath, StringComparison.OrdinalIgnoreCase))
            {
                _backgroundOverlayLayer.Opacity = Math.Clamp(App.Settings.Prop.GlobalBackgroundOverlayOpacity, 0.0, 1.0);
                return;
            }

            if (_backgroundImageLayer is not null)
                _rootGrid.Children.Remove(_backgroundImageLayer);
            if (_backgroundOverlayLayer is not null)
                _rootGrid.Children.Remove(_backgroundOverlayLayer);
            _backgroundImageLayer = null;
            _backgroundOverlayLayer = null;
            _backgroundPath = "";

            var layers = wantedPath.Length > 0
                ? PhasmaStrap.UI.GlobalBackground.TryCreateLayers(wantedPath, App.Settings.Prop.GlobalBackgroundOverlayOpacity)
                : null;

            if (_tintLayer is not null)
                _tintLayer.Visibility = layers is null ? Visibility.Visible : Visibility.Collapsed;

            if (layers is null)
            {
                UpdateForegroundCache(false);
                return;
            }

            int rowSpan = Math.Max(1, _rootGrid.RowDefinitions.Count);
            int columnSpan = Math.Max(1, _rootGrid.ColumnDefinitions.Count);

            int tintIndex = _tintLayer is not null ? _rootGrid.Children.IndexOf(_tintLayer) : 0;
            if (tintIndex < 0)
                tintIndex = 0;

            Grid.SetRowSpan(layers.Value.Image, rowSpan);
            Grid.SetColumnSpan(layers.Value.Image, columnSpan);
            _rootGrid.Children.Insert(tintIndex, layers.Value.Image);

            Grid.SetRowSpan(layers.Value.Overlay, rowSpan);
            Grid.SetColumnSpan(layers.Value.Overlay, columnSpan);
            _rootGrid.Children.Insert(tintIndex + 1, layers.Value.Overlay);

            _backgroundImageLayer = layers.Value.Image;
            _backgroundOverlayLayer = layers.Value.Overlay;
            _backgroundPath = wantedPath;

            UpdateForegroundCache(PhasmaStrap.UI.BackgroundLibrary.IsVideo(wantedPath) || wantedPath.EndsWith(".gif", StringComparison.OrdinalIgnoreCase));
        }

        private void UpdateForegroundCache(bool moving)
        {
            if (_rootGrid is null)
                return;

            foreach (UIElement child in _rootGrid.Children)
            {
                if (child == _tintLayer || child == _backgroundImageLayer || child == _backgroundOverlayLayer)
                    continue;

                if (moving)
                    child.CacheMode ??= new BitmapCache { SnapsToDevicePixels = true };
                else if (child.CacheMode is BitmapCache)
                    child.CacheMode = null;
            }
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            if (ExtendsContentIntoTitleBar && WindowBackdropType == Wpf.Ui.Appearance.BackgroundType.None)
            {
                WindowBackdropType = ResolveBackdropType(App.Settings.Prop.WindowBackdropStyle);

                if (Content is Grid rootGrid)
                {
                    _rootGrid = rootGrid;
                    int rowSpan = Math.Max(1, rootGrid.RowDefinitions.Count);
                    int columnSpan = Math.Max(1, rootGrid.ColumnDefinitions.Count);
                    bool isSettingsWindow = this is PhasmaStrap.UI.Elements.Settings.MainWindow;

                    var tint = new Border { Background = CurrentGlassTint, IsHitTestVisible = false };
                    Grid.SetRowSpan(tint, rowSpan);
                    Grid.SetColumnSpan(tint, columnSpan);
                    rootGrid.Children.Insert(0, tint);
                    _tintLayer = tint;

                    RefreshGlobalBackground();

                    if (isSettingsWindow && App.Settings.Prop.SnowEffectEnabled)
                    {
                        var snow = new PhasmaStrap.UI.SmoothSnowLayer();
                        Grid.SetRowSpan(snow, rowSpan);
                        Grid.SetColumnSpan(snow, columnSpan);
                        rootGrid.Children.Add(snow);
                        snow.SetActive(true);
                    }
                }
            }

            if (UseDefaultEntranceAnimation && Content is FrameworkElement content)
            {
                content.RenderTransformOrigin = new Point(0.5, 0.5);
                content.RenderTransform = new TransformGroup { Children = { _entranceScale, _entranceTranslate } };
                content.Opacity = 0;
            }

            if (App.Settings.Prop.WPFSoftwareRender || App.LaunchSettings.NoGPUFlag.Active)
            {
                if (PresentationSource.FromVisual(this) is HwndSource hwndSource)
                    hwndSource.CompositionTarget.RenderMode = RenderMode.SoftwareOnly;
            }

            base.OnSourceInitialized(e);
        }

        private static Wpf.Ui.Appearance.BackgroundType ResolveBackdropType(Enums.BackdropStyle style) => style switch
        {
            Enums.BackdropStyle.Mica => Wpf.Ui.Appearance.BackgroundType.Mica,
            Enums.BackdropStyle.Acrylic => Wpf.Ui.Appearance.BackgroundType.Acrylic,
            Enums.BackdropStyle.None => Wpf.Ui.Appearance.BackgroundType.None,
            _ => Wpf.Ui.Appearance.BackgroundType.Acrylic
        };
    }
}
