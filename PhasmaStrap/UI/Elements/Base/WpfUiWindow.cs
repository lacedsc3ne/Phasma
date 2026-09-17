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
        private readonly IThemeService _themeService = new ThemeService();

        // Phasma brand accent (coral-red), matches PhasmaMacro's --accent
        private static readonly Color PhasmaAccent = Color.FromRgb(0xF4, 0x55, 0x4B);

        // near-opaque tint over the acrylic blur - glassy at the edges, but reads as a solid
        // surface overall. One per theme: the dark one used to be applied unconditionally, which
        // is why the Light theme came out as dark text painted over a dark window.
        private static readonly SolidColorBrush DarkGlassTintBrush = new(Color.FromArgb(232, 0x0E, 0x0E, 0x12));
        private static readonly SolidColorBrush LightGlassTintBrush = new(Color.FromArgb(225, 0xF6, 0xF6, 0xF9));

        // the layers OnSourceInitialized inserts behind the content, kept so they can be
        // swapped live when the theme or background image settings change
        private Border? _tintLayer;
        private FrameworkElement? _backgroundImageLayer;
        private FrameworkElement? _backgroundOverlayLayer;
        private Grid? _rootGrid;

        private static SolidColorBrush CurrentGlassTint =>
            App.Settings.Prop.Theme.GetFinal() == Enums.Theme.Dark ? DarkGlassTintBrush : LightGlassTintBrush;

        // FluentDialog implements its own richer entrance (elastic bounce, mist), so it opts out of this
        protected virtual bool UseDefaultEntranceAnimation => true;

        private readonly ScaleTransform _entranceScale = new(0.95, 0.95);
        private readonly TranslateTransform _entranceTranslate = new(0, 18);

        public WpfUiWindow()
        {
            ApplyTheme();

            // FontFamily is an inherited DP, so setting it here cascades to every
            // child control that doesn't set its own FontFamily explicitly (e.g. icon glyphs)
            if (Application.Current.Resources["PhasmaBody"] is System.Windows.Media.FontFamily phasmaBody)
                FontFamily = phasmaBody;

            if (UseDefaultEntranceAnimation)
                Loaded += WpfUiWindow_Loaded;
        }

        // Window itself can't have a RenderTransform (WPF explicitly disallows it), so the
        // entrance animation is applied to its Content instead, once that's actually available.
        // Animating the transform objects directly (rather than via a Storyboard property path)
        // sidesteps WPF's fragile path resolution through nested TransformGroup.Children indices.
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
            const int customThemeIndex = 2; // index for CustomTheme merged dictionary

            _themeService.SetTheme(App.Settings.Prop.Theme.GetFinal() == Enums.Theme.Dark ? ThemeType.Dark : ThemeType.Light);
            _themeService.SetAccent(PhasmaAccent);

            // there doesn't seem to be a way to query the name for merged dictionaries
            var dict = new ResourceDictionary { Source = new Uri($"pack://application:,,,/UI/Style/{Enum.GetName(App.Settings.Prop.Theme.GetFinal())}.xaml") };
            Application.Current.Resources.MergedDictionaries[customThemeIndex] = dict;

            ApplyAppColorTheme();

            if (_tintLayer is not null)
                _tintLayer.Background = CurrentGlassTint;

#if QA_BUILD
            this.BorderBrush = System.Windows.Media.Brushes.Red;
            this.BorderThickness = new Thickness(4);
#endif
        }

        // index 0: ui:ThemesDictionary, 1: ui:ControlsDictionary, 2: base Dark/Light skin (see
        // customThemeIndex above), 3: Default.xaml - this is a 5th slot for the user-editable
        // AppColorTheme override (Utility/AppColorTheme.cs), merged on top of everything else so
        // its values win. Kept separate from customThemeIndex so it survives ApplyTheme() being
        // called repeatedly as the base skin is swapped out.
        private const int AppColorThemeIndex = 4;

        private void ApplyAppColorTheme()
        {
            var dictionaries = Application.Current.Resources.MergedDictionaries;

            if (!App.Settings.Prop.CustomColorThemeEnabled)
            {
                if (dictionaries.Count > AppColorThemeIndex)
                    dictionaries.RemoveAt(AppColorThemeIndex);
                return;
            }

            var overrides = PhasmaStrap.Utility.AppColorTheme.LoadForApp();

            if (dictionaries.Count > AppColorThemeIndex)
                dictionaries[AppColorThemeIndex] = overrides;
            else
                dictionaries.Add(overrides);
        }

        /// <summary>
        /// Re-applies the theme (including the AppColorTheme override) to every currently open
        /// WpfUiWindow, so changes made in the colour theme editor show immediately everywhere.
        /// </summary>
        public static void ApplyThemeToAllOpenWindows()
        {
            foreach (Window window in Application.Current.Windows)
            {
                if (window is WpfUiWindow wpfUiWindow)
                    wpfUiWindow.ApplyTheme();
            }
        }

        /// <summary>
        /// Re-reads the settings-window background image settings and swaps the image/dim layers
        /// on the open settings window in place, so toggling or picking a file on the Appearance
        /// page shows up immediately instead of only after a relaunch.
        /// </summary>
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

            if (_backgroundImageLayer is not null)
                _rootGrid.Children.Remove(_backgroundImageLayer);
            if (_backgroundOverlayLayer is not null)
                _rootGrid.Children.Remove(_backgroundOverlayLayer);
            _backgroundImageLayer = null;
            _backgroundOverlayLayer = null;

            var layers = App.Settings.Prop.GlobalBackgroundEnabled
                ? PhasmaStrap.UI.GlobalBackground.TryCreateLayers(App.Settings.Prop.GlobalBackgroundFilePath, App.Settings.Prop.GlobalBackgroundOverlayOpacity)
                : null;

            // the near-opaque glass tint would hide the picture almost entirely - while an image is
            // showing, the user's own dim overlay (opacity slider) is what keeps text readable instead
            if (_tintLayer is not null)
                _tintLayer.Visibility = layers is null ? Visibility.Visible : Visibility.Collapsed;

            if (layers is null)
                return;

            int rowSpan = Math.Max(1, _rootGrid.RowDefinitions.Count);
            int columnSpan = Math.Max(1, _rootGrid.ColumnDefinitions.Count);

            // image goes under the glass tint (index 0), the dim overlay sits between them - the
            // tint itself stays where it is so the ordering matches OnSourceInitialized's
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
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            // WindowBackdropType can only be applied once ExtendsContentIntoTitleBar is set, which
            // derived windows do via their own XAML - that hasn't run yet in the constructor, so
            // this has to wait until here. Windows that don't extend content into the title bar
            // (small message-style dialogs) just keep the plain dark background instead of glass.
            if (ExtendsContentIntoTitleBar && WindowBackdropType == Wpf.Ui.Appearance.BackgroundType.None)
            {
                WindowBackdropType = ResolveBackdropType(App.Settings.Prop.WindowBackdropStyle);

                // Wpf.Ui's Acrylic/Mica implementation forcibly clears Window.Background to enable
                // the native backdrop, so a tint has to live on a child element instead - insert one
                // behind everything else in the root Grid, spanning its full size
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

                    // optional background image, settings window only (UI polish port from Voidstrap) -
                    // inserted underneath the tint by RefreshGlobalBackground, which is also what the
                    // Appearance page calls to swap it live
                    RefreshGlobalBackground();

                    // decorative snow overlay, settings window only, drawn on top of everything else
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

            // set the hidden starting state here (before the window is shown) rather than in
            // Loaded, so there's no flash of the fully-visible window on the first frame
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
            _ => Wpf.Ui.Appearance.BackgroundType.Acrylic // Default - preserves prior hardcoded behaviour
        };
    }
}
