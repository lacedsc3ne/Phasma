using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;
using Wpf.Ui.Mvvm.Contracts;
using Wpf.Ui.Mvvm.Services;

namespace Bloxstrap.UI.Elements.Base
{
    public abstract class WpfUiWindow : UiWindow
    {
        private readonly IThemeService _themeService = new ThemeService();

        // Phasma brand accent (coral-red), matches PhasmaMacro's --accent
        private static readonly Color PhasmaAccent = Color.FromRgb(0xF4, 0x55, 0x4B);

        // dark, near-opaque tint over the acrylic blur - glassy at the edges, but reads dark overall
        private static readonly SolidColorBrush GlassTintBrush = new(Color.FromArgb(232, 0x0E, 0x0E, 0x12));

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

#if QA_BUILD
            this.BorderBrush = System.Windows.Media.Brushes.Red;
            this.BorderThickness = new Thickness(4);
#endif
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            // WindowBackdropType can only be applied once ExtendsContentIntoTitleBar is set, which
            // derived windows do via their own XAML - that hasn't run yet in the constructor, so
            // this has to wait until here. Windows that don't extend content into the title bar
            // (small message-style dialogs) just keep the plain dark background instead of glass.
            if (ExtendsContentIntoTitleBar && WindowBackdropType == Wpf.Ui.Appearance.BackgroundType.None)
            {
                WindowBackdropType = Wpf.Ui.Appearance.BackgroundType.Acrylic;

                // Wpf.Ui's Acrylic/Mica implementation forcibly clears Window.Background to enable
                // the native backdrop, so a tint has to live on a child element instead - insert one
                // behind everything else in the root Grid, spanning its full size
                if (Content is Grid rootGrid)
                {
                    var tint = new Border { Background = GlassTintBrush, IsHitTestVisible = false };
                    Grid.SetRowSpan(tint, Math.Max(1, rootGrid.RowDefinitions.Count));
                    Grid.SetColumnSpan(tint, Math.Max(1, rootGrid.ColumnDefinitions.Count));
                    rootGrid.Children.Insert(0, tint);
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
    }
}
