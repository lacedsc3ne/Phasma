using FontFamily = System.Windows.Media.FontFamily;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

using PhasmaStrap.UI.Elements.Base;

namespace PhasmaStrap.UI.Elements.Dialogs
{
    /// <summary>
    /// Lets the user search the Google Fonts catalog, preview a font (rendered live once
    /// downloaded), and apply it as the Roblox client font mod and/or PhasmaStrap's own app
    /// font. See GoogleFontsService for the catalog source and download/cache logic.
    /// </summary>
    public partial class GoogleFontsDialog : WpfUiWindow
    {
        /// <summary>
        /// Absolute path to the downloaded/cached .ttf that was applied, set once the dialog
        /// closes with a successful Apply. Null if the dialog was cancelled.
        /// </summary>
        public string? SelectedFontPath { get; private set; }

        private IReadOnlyList<GoogleFontOption> _allFonts = Array.Empty<GoogleFontOption>();

        private GoogleFontOption? _selectedFont;

        private CancellationTokenSource? _previewCts;

        public GoogleFontsDialog()
        {
            InitializeComponent();
            Loaded += GoogleFontsDialog_Loaded;
        }

        private async void GoogleFontsDialog_Loaded(object sender, RoutedEventArgs e)
        {
            LoadingRing.Visibility = Visibility.Visible;
            try
            {
                _allFonts = await GoogleFontsService.LoadCatalogAsync(false, CancellationToken.None);
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine("GoogleFontsDialog::Load", "Loading the font catalog failed: " + ex.Message);
                _allFonts = Array.Empty<GoogleFontOption>();
            }
            finally
            {
                LoadingRing.Visibility = Visibility.Collapsed;
            }

            ApplyFilter();
        }

        private void SearchTextBox_TextChanged(object sender, TextChangedEventArgs e) => ApplyFilter();

        private void ApplyFilter()
        {
            string filter = SearchTextBox.Text?.Trim() ?? "";

            IEnumerable<GoogleFontOption> filtered = string.IsNullOrEmpty(filter)
                ? _allFonts
                : _allFonts.Where(font => font.Family.Contains(filter, StringComparison.OrdinalIgnoreCase));

            List<GoogleFontOption> results = filtered.Take(200).ToList();

            FontListBox.ItemsSource = results;
            NoResultsText.Visibility = results.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        private async void FontListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            _selectedFont = FontListBox.SelectedItem as GoogleFontOption;
            ApplyButton.IsEnabled = _selectedFont != null;

            if (_selectedFont == null)
            {
                PreviewText.ClearValue(TextBlock.FontFamilyProperty);
                return;
            }

            _previewCts?.Cancel();
            CancellationTokenSource cts = new();
            _previewCts = cts;

            GoogleFontOption font = _selectedFont;
            PreviewLoadingRing.Visibility = Visibility.Visible;
            try
            {
                string path = await GoogleFontsService.DownloadAsync(font, cts.Token);

                if (cts.IsCancellationRequested || !ReferenceEquals(_selectedFont, font))
                    return;

                FontFamily? family = Fonts.GetFontFamilies(new Uri(path, UriKind.Absolute)).FirstOrDefault();
                PreviewText.FontFamily = family ?? new FontFamily("Segoe UI");
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine("GoogleFontsDialog::Preview", "Preview download failed: " + ex.Message);
            }
            finally
            {
                if (ReferenceEquals(_previewCts, cts))
                    PreviewLoadingRing.Visibility = Visibility.Collapsed;
            }
        }

        private async void ApplyButton_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedFont == null)
                return;

            GoogleFontOption font = _selectedFont;
            ApplyButton.IsEnabled = false;
            try
            {
                string path = await GoogleFontsService.DownloadAsync(font, CancellationToken.None);
                SelectedFontPath = path;

                if (ApplyToAppCheckBox.IsChecked == true)
                {
                    try
                    {
                        AppFont.SetFromFile(path);
                    }
                    catch (Exception ex)
                    {
                        App.Logger.WriteLine("GoogleFontsDialog::Apply", "Applying the app font failed: " + ex.Message);
                    }
                }

                DialogResult = true;
                Close();
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine("GoogleFontsDialog::Apply", "Font download failed: " + ex.Message);
                Frontend.ShowMessageBox(Strings.Dialog_GoogleFonts_DownloadFailed, MessageBoxImage.Error);
            }
            finally
            {
                ApplyButton.IsEnabled = _selectedFont != null;
            }
        }
    }
}
