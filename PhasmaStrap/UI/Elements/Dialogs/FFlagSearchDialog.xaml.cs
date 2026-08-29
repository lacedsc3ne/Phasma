using System.Collections.ObjectModel;
using System.Text.Encodings.Web;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;

using Microsoft.Win32;

using PhasmaStrap.UI.Elements.Base;

namespace PhasmaStrap.UI.Elements.Dialogs
{
    // A searchable browser over the public FastFlag trackers maintained by MaximumADHD
    // (Roblox-FFlag-Tracker / Roblox-Client-Tracker on GitHub), plus a bulk-validation tool
    // for checking whether a block of flags a user already has actually exist. Selecting a
    // result and clicking "Add to Flags" reuses FastFlagEditorPage's own AddSingle/validation
    // logic via the callback passed into the constructor, rather than duplicating it here.
    //
    // Ported from Voidstrap's UI/Elements/Dialogs/FFlagSearchDialog.xaml.cs. See the class
    // remarks below for what was simplified relative to the original.
    public partial class FFlagSearchDialog : WpfUiWindow
    {
        // Voidstrap's original dialog also fabricated fake "date added" timestamps
        // (Random.Shared over the last 24h) for a "Recent Flags (24h)" tab, even though the
        // underlying trackers don't expose real modification times. That's misleading, so
        // this port renames the tab to "Browse All" and drops the fake dates - it's a plain
        // sample of the loaded flag database instead of a false claim about recency.
        //
        // Voidstrap's HTTP layer used a custom VpnHttpClient with its own network-change-aware
        // retry handler. That's not Voidstrap-owned infrastructure (no proxy/endpoint of
        // theirs is involved - it only wraps the same MaximumADHD/Roblox first-party URLs),
        // but PhasmaStrap already has an equivalent shared App.HttpClient + bounded-read
        // helper (PhasmaStrap.Utility.Http.ReadStringBoundedAsync) used elsewhere (e.g.
        // GameChat), so this port reuses that instead of introducing a second HTTP stack.

        private const int MaximumValidationFileBytes = 4 * 1024 * 1024;
        private const int MaximumFlagsPerSource = 100_000;
        private const int MaximumTotalFlags = 250_000;
        private const int MaximumValidationFlags = 50_000;
        private const int MaximumVisibleResults = 1_000;
        private const int MaximumValidationCharacters = 4_000_000;
        private const int MaximumResponseBytes = 16 * 1024 * 1024;

        private readonly ObservableCollection<FlagSearchResult> _searchResults = new();
        private readonly ObservableCollection<FlagValidationResult> _validationResults = new();
        private readonly ObservableCollection<FlagSearchResult> _browseResults = new();

        private readonly DataSourceInfo[] _dataSources =
        {
            new DataSourceInfo
            {
                Name = "PCClientBootstrapper",
                Url = "https://raw.githubusercontent.com/MaximumADHD/Roblox-FFlag-Tracker/refs/heads/main/PCClientBootstrapper.json"
            },
            new DataSourceInfo
            {
                Name = "PCStudioApp",
                Url = "https://raw.githubusercontent.com/MaximumADHD/Roblox-FFlag-Tracker/refs/heads/main/PCStudioApp.json"
            },
            new DataSourceInfo
            {
                Name = "PCDesktopClient",
                Url = "https://raw.githubusercontent.com/MaximumADHD/Roblox-FFlag-Tracker/refs/heads/main/PCDesktopClient.json"
            },
            new DataSourceInfo
            {
                Name = "FVariables.txt",
                Url = "https://raw.githubusercontent.com/MaximumADHD/Roblox-Client-Tracker/refs/heads/roblox/FVariables.txt"
            },
        };

        private Dictionary<string, object> _allFlags = new();
        private Dictionary<string, FlagMetadata> _flagMetadata = new();

        private readonly CancellationTokenSource _lifetimeCancellation = new();

        private List<FlagSearchResult> _lastSearchResults = new();
        private List<FlagValidationResult> _lastValidationResults = new();

        private int _searchGeneration;

        private readonly Action<string, string>? _addFlagCallback;

        public FFlagSearchDialog(Action<string, string>? addFlagCallback = null)
        {
            _addFlagCallback = addFlagCallback;

            InitializeComponent();

            SearchResultsDataGrid.ItemsSource = _searchResults;
            ValidationResultsDataGrid.ItemsSource = _validationResults;
            BrowseResultsDataGrid.ItemsSource = _browseResults;

            AddFromSearchButton.IsEnabled = _addFlagCallback is not null;
            AddFromBrowseButton.IsEnabled = _addFlagCallback is not null;

            UpdateSearchResultsCount();
            ValidationResultsCount.Text = String.Format(Strings.Dialog_FFlagSearch_ResultsCount, 0);
            BrowseResultsCount.Text = String.Format(Strings.Dialog_FFlagSearch_BrowseCount, 0);

            _ = LoadDataAsync(_lifetimeCancellation.Token);
        }

        private async Task LoadDataAsync(CancellationToken token)
        {
            await UpdateStatusAsync(Strings.Dialog_FFlagSearch_StatusLoading);
            ShowProgress(true);

            try
            {
                var allFlags = new Dictionary<string, object>();
                var flagMetadata = new Dictionary<string, FlagMetadata>();

                foreach (DataSourceInfo source in _dataSources)
                {
                    token.ThrowIfCancellationRequested();

                    try
                    {
                        Dictionary<string, object> fetched = await FetchFlagsFromSourceAsync(source.Url, source.Name, token);

                        foreach (KeyValuePair<string, object> item in fetched)
                        {
                            if (allFlags.Count >= MaximumTotalFlags)
                                break;

                            if (item.Key.Length is > 0 and <= 512 && !allFlags.ContainsKey(item.Key))
                            {
                                allFlags[item.Key] = item.Value;
                                flagMetadata[item.Key] = new FlagMetadata
                                {
                                    Source = source.Name,
                                    DateAdded = DateTime.Now
                                };
                            }
                        }

                        source.FlagCount = fetched.Count;
                    }
                    catch (OperationCanceledException) when (token.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        App.Logger.WriteException("FFlagSearch", ex);
                    }
                }

                _allFlags = allFlags;
                _flagMetadata = flagMetadata;

                await UpdateStatusAsync(String.Format(Strings.Dialog_FFlagSearch_StatusLoaded, allFlags.Count));
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
            }
            catch (Exception ex)
            {
                await UpdateStatusAsync(Strings.Dialog_FFlagSearch_StatusLoadError);
                App.Logger.WriteException("FFlagSearch", ex);
            }
            finally
            {
                ShowProgress(false);
            }
        }

        private static async Task<Dictionary<string, object>> FetchFlagsFromSourceAsync(string url, string sourceName, CancellationToken token)
        {
            var flags = new Dictionary<string, object>();
            string response = String.Empty;

            try
            {
                using HttpResponseMessage httpResponse = await App.HttpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, token);
                httpResponse.EnsureSuccessStatusCode();
                response = await Http.ReadStringBoundedAsync(httpResponse.Content, MaximumResponseBytes, token);

                if (url.EndsWith(".json"))
                {
                    ParseJsonFlags(response, flags);
                }
                else if (url.EndsWith(".txt"))
                {
                    ParseTextFlags(response, flags);
                }
                else
                {
                    ParseJsonFlags(response, flags);
                }
            }
            catch (JsonException)
            {
                if (!String.IsNullOrEmpty(response))
                    ParseTextFlags(response, flags);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                throw;
            }
            catch (HttpRequestException ex)
            {
                App.Logger.WriteLine("FFlagSearch", $"Failed to fetch from {sourceName}: {ex.Message}");
                throw;
            }

            return flags;
        }

        private static void ParseJsonFlags(string response, Dictionary<string, object> flags)
        {
            using JsonDocument document = JsonDocument.Parse(response);

            if (document.RootElement.ValueKind != JsonValueKind.Object)
                return;

            foreach (JsonProperty item in document.RootElement.EnumerateObject())
            {
                if (flags.Count >= MaximumFlagsPerSource)
                    break;

                flags[item.Name] = JsonValueToObject(item.Value);
            }
        }

        private static void ParseTextFlags(string response, Dictionary<string, object> flags)
        {
            foreach (string line in response.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                if (flags.Count >= MaximumFlagsPerSource)
                    break;

                string[] parts = line.Split('=', 2);
                if (parts.Length != 2)
                    continue;

                string key = parts[0].Trim();
                string text = parts[1].Trim();

                flags[key] = ParseValue(text);
            }
        }

        private static object JsonValueToObject(JsonElement value) => value.ValueKind switch
        {
            JsonValueKind.String => value.GetString() ?? "",
            JsonValueKind.Number => value.TryGetInt32(out int number) ? number : value.GetDouble(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => value.GetRawText(),
        };

        private async void SearchTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            int generation = Interlocked.Increment(ref _searchGeneration);
            string? searchTerm = SearchTextBox.Text?.Trim();

            if (String.IsNullOrEmpty(searchTerm))
            {
                _searchResults.Clear();
                _lastSearchResults.Clear();
                ExportSearchResultsButton.IsEnabled = false;
                UpdateSearchResultsCount();
                return;
            }

            try
            {
                await Task.Delay(300, _lifetimeCancellation.Token);

                if (SearchTextBox.Text?.Trim() == searchTerm)
                    await PerformSearchAsync(searchTerm, generation, _lifetimeCancellation.Token);
            }
            catch (OperationCanceledException) when (_lifetimeCancellation.IsCancellationRequested)
            {
            }
        }

        private async Task PerformSearchAsync(string searchTerm, int generation, CancellationToken token)
        {
            bool trueFlagsOnly = TrueFlagsOnlyCheckBox.IsChecked == true;
            bool falseFlagsOnly = FalseFlagsOnlyCheckBox.IsChecked == true;

            Dictionary<string, object> flags = _allFlags;
            Dictionary<string, FlagMetadata> metadata = _flagMetadata;

            List<FlagSearchResult> results;

            try
            {
                results = await Task.Run(() =>
                {
                    var matches = new List<FlagSearchResult>();
                    int scanned = 0;

                    foreach (KeyValuePair<string, object> flag in flags)
                    {
                        if ((scanned++ & 1023) == 0)
                            token.ThrowIfCancellationRequested();

                        if (!flag.Key.Contains(searchTerm, StringComparison.OrdinalIgnoreCase))
                            continue;

                        if (trueFlagsOnly && !IsTrueValue(flag.Value))
                            continue;

                        if (falseFlagsOnly && !IsFalseValue(flag.Value))
                            continue;

                        string source = metadata.TryGetValue(flag.Key, out FlagMetadata? meta) ? meta.Source : "Unknown";

                        matches.Add(new FlagSearchResult
                        {
                            Name = flag.Key,
                            Value = flag.Value?.ToString() ?? "null",
                            Source = source
                        });
                    }

                    return matches;
                }, token);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                return;
            }

            if (token.IsCancellationRequested || generation != Volatile.Read(ref _searchGeneration))
                return;

            _lastSearchResults = results;
            _searchResults.Clear();

            foreach (FlagSearchResult item in results.Take(MaximumVisibleResults))
                _searchResults.Add(item);

            UpdateSearchResultsCount();
            ExportSearchResultsButton.IsEnabled = results.Count > 0;

            if (results.Count > MaximumVisibleResults)
                StatusText.Text = String.Format(Strings.Dialog_FFlagSearch_ShowingFirstN, MaximumVisibleResults, results.Count);
        }

        private async void ValidateButton_Click(object sender, RoutedEventArgs e)
        {
            string? text = ValidationInputTextBox.Text?.Trim();

            if (String.IsNullOrEmpty(text))
            {
                Frontend.ShowMessageBox(Strings.Dialog_FFlagSearch_NoInput, MessageBoxImage.Exclamation);
            }
            else if (text.Length > MaximumValidationCharacters)
            {
                Frontend.ShowMessageBox(Strings.Dialog_FFlagSearch_InputTooLarge, MessageBoxImage.Exclamation);
            }
            else
            {
                await ValidateFlagsAsync(text);
            }
        }

        private async Task ValidateFlagsAsync(string input)
        {
            ShowProgress(true);

            try
            {
                CancellationToken token = _lifetimeCancellation.Token;
                (Dictionary<string, object> parsed, HashSet<string> duplicates) = await Task.Run(() => ParseValidationInput(input, token), token);

                if (duplicates.Count > 0)
                {
                    Frontend.ShowMessageBox(
                        String.Format(Strings.Dialog_FFlagSearch_DuplicatesDetected, String.Join(", ", duplicates.Take(25))),
                        MessageBoxImage.Exclamation
                    );
                }

                Dictionary<string, object> knownFlags = _allFlags;

                List<FlagValidationResult> results = await Task.Run(() =>
                {
                    var list = new List<FlagValidationResult>(parsed.Count);

                    foreach (KeyValuePair<string, object> item in parsed)
                    {
                        token.ThrowIfCancellationRequested();

                        var result = new FlagValidationResult
                        {
                            Name = item.Key,
                            InputValue = item.Value?.ToString() ?? "null"
                        };

                        if (knownFlags.TryGetValue(item.Key, out object? value))
                        {
                            result.Status = Strings.Dialog_FFlagSearch_ValidStatus;
                            result.ValidValue = value?.ToString() ?? "null";
                            result.Notes = Strings.Dialog_FFlagSearch_ValidNotes;
                        }
                        else
                        {
                            result.Status = Strings.Dialog_FFlagSearch_InvalidStatus;
                            result.ValidValue = "N/A";
                            result.Notes = Strings.Dialog_FFlagSearch_InvalidNotes;
                        }

                        list.Add(result);
                    }

                    return list;
                }, token);

                _lastValidationResults = results;
                _validationResults.Clear();

                foreach (FlagValidationResult item in results.Take(MaximumVisibleResults))
                    _validationResults.Add(item);

                ValidationResultsCount.Text = String.Format(Strings.Dialog_FFlagSearch_ResultsCount, results.Count);
                ExportValidResultsButton.IsEnabled = results.Any(r => r.Status == Strings.Dialog_FFlagSearch_ValidStatus);

                int validCount = results.Count(r => r.Status == Strings.Dialog_FFlagSearch_ValidStatus);
                int invalidCount = results.Count(r => r.Status == Strings.Dialog_FFlagSearch_InvalidStatus);

                await UpdateStatusAsync(String.Format(Strings.Dialog_FFlagSearch_ValidatedSummary, results.Count, validCount, invalidCount));
            }
            catch (OperationCanceledException) when (_lifetimeCancellation.IsCancellationRequested)
            {
            }
            catch (Exception ex)
            {
                await UpdateStatusAsync(Strings.Dialog_FFlagSearch_ValidationErrorTitle);
                Frontend.ShowMessageBox(String.Format(Strings.Dialog_FFlagSearch_ValidationError, ex.Message), MessageBoxImage.Hand);
            }
            finally
            {
                ShowProgress(false);
            }
        }

        private static (Dictionary<string, object> Flags, HashSet<string> Duplicates) ParseValidationInput(string input, CancellationToken token)
        {
            var flags = new Dictionary<string, object>();
            var duplicates = new HashSet<string>();

            try
            {
                using JsonDocument document = JsonDocument.Parse(input);

                if (document.RootElement.ValueKind != JsonValueKind.Object)
                    throw new JsonException("Flag input must be an object");

                foreach (JsonProperty item in document.RootElement.EnumerateObject())
                {
                    token.ThrowIfCancellationRequested();

                    if (flags.Count >= MaximumValidationFlags)
                        throw new InvalidDataException("The flag input contains too many values");

                    string name = item.Name.Trim();
                    if (name.Length is 0 or > 512)
                        continue;

                    object value = JsonValueToObject(item.Value);

                    if (!flags.TryAdd(name, value))
                    {
                        duplicates.Add(name);
                        flags[name] = value;
                    }
                }
            }
            catch (JsonException)
            {
                flags.Clear();
                duplicates.Clear();

                foreach (string line in input.Split('\n', StringSplitOptions.RemoveEmptyEntries))
                {
                    token.ThrowIfCancellationRequested();

                    if (flags.Count >= MaximumValidationFlags)
                        throw new InvalidDataException("The flag input contains too many values");

                    string[] pair = line.Split('=', 2);
                    if (pair.Length != 2)
                        continue;

                    string name = pair[0].Trim();
                    if (name.Length is 0 or > 512)
                        continue;

                    string value = pair[1].Trim();

                    if (!flags.TryAdd(name, value))
                    {
                        duplicates.Add(name);
                        flags[name] = value;
                    }
                }
            }

            return (flags, duplicates);
        }

        private async void FetchSampleButton_Click(object sender, RoutedEventArgs e)
        {
            ShowProgress(true);

            try
            {
                List<FlagSearchResult> list = _allFlags.Take(MaximumVisibleResults)
                    .Select(flag => new FlagSearchResult
                    {
                        Name = flag.Key,
                        Value = flag.Value?.ToString() ?? "null",
                        Source = _flagMetadata.TryGetValue(flag.Key, out FlagMetadata? meta) ? meta.Source : "Unknown"
                    })
                    .ToList();

                _browseResults.Clear();

                foreach (FlagSearchResult item in list)
                    _browseResults.Add(item);

                BrowseResultsCount.Text = String.Format(Strings.Dialog_FFlagSearch_BrowseCount, list.Count);

                bool hasResults = list.Count > 0;
                DownloadAllBrowseButton.IsEnabled = hasResults;
                DownloadTrueBrowseButton.IsEnabled = hasResults;
                DownloadFalseBrowseButton.IsEnabled = hasResults;

                await UpdateStatusAsync(String.Format(Strings.Dialog_FFlagSearch_BrowseCount, list.Count));
            }
            catch (Exception ex)
            {
                await UpdateStatusAsync(Strings.Dialog_FFlagSearch_StatusLoadError);
                App.Logger.WriteException("FFlagSearch", ex);
            }
            finally
            {
                ShowProgress(false);
            }
        }

        private async void LoadFileButton_Click(object sender, RoutedEventArgs e)
        {
            var openFileDialog = new OpenFileDialog
            {
                Filter = $"{Strings.FileTypes_JSONFiles}|*.json|{Strings.FileTypes_TextFiles}|*.txt",
                Title = Strings.Dialog_FFlagSearch_LoadFile
            };

            if (openFileDialog.ShowDialog() != true)
                return;

            try
            {
                string text = await ReadValidationFileAsync(openFileDialog.FileName, _lifetimeCancellation.Token);
                ValidationInputTextBox.Text = text;
            }
            catch (OperationCanceledException) when (_lifetimeCancellation.IsCancellationRequested)
            {
            }
            catch (Exception ex)
            {
                Frontend.ShowMessageBox(String.Format(Strings.Dialog_FFlagSearch_FileError, ex.Message), MessageBoxImage.Hand);
            }
        }

        private static async Task<string> ReadValidationFileAsync(string path, CancellationToken token)
        {
            await using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, FileOptions.Asynchronous | FileOptions.SequentialScan);

            if (stream.Length <= 0 || stream.Length > MaximumValidationFileBytes)
                throw new InvalidDataException("The flag file size is invalid");

            byte[] data = new byte[checked((int)stream.Length)];
            int offset = 0;

            while (offset < data.Length)
            {
                int read = await stream.ReadAsync(data.AsMemory(offset), token);
                if (read == 0)
                    throw new EndOfStreamException();
                offset += read;
            }

            token.ThrowIfCancellationRequested();
            using var reader = new StreamReader(new MemoryStream(data, writable: false), Encoding.UTF8, true);
            string text = await reader.ReadToEndAsync();

            if (text.Length > MaximumValidationCharacters)
                throw new InvalidDataException("The flag input is too large");

            return text;
        }

        private void ClearValidationButton_Click(object sender, RoutedEventArgs e)
        {
            ValidationInputTextBox.Clear();
            _validationResults.Clear();
            _lastValidationResults.Clear();
            ValidationResultsCount.Text = String.Format(Strings.Dialog_FFlagSearch_ResultsCount, 0);
            ExportValidResultsButton.IsEnabled = false;
        }

        private async void ExportSearchResultsButton_Click(object sender, RoutedEventArgs e) =>
            await ExportFlagsAsync(_lastSearchResults.ToDictionary(r => r.Name, r => ParseValue(r.Value)), "search_results");

        private async void ExportValidResultsButton_Click(object sender, RoutedEventArgs e)
        {
            Dictionary<string, object> flags = _lastValidationResults
                .Where(r => r.Status == Strings.Dialog_FFlagSearch_ValidStatus)
                .ToDictionary(r => r.Name, r => ParseValue(r.ValidValue));

            await ExportFlagsAsync(flags, "valid_flags");
        }

        private async void DownloadAllBrowseButton_Click(object sender, RoutedEventArgs e) =>
            await ExportFlagsAsync(_browseResults.ToDictionary(r => r.Name, r => ParseValue(r.Value)), "browse_flags_all");

        private async void DownloadTrueBrowseButton_Click(object sender, RoutedEventArgs e)
        {
            Dictionary<string, object> flags = _browseResults
                .Where(r => IsTrueValue(ParseValue(r.Value)))
                .ToDictionary(r => r.Name, r => ParseValue(r.Value));

            await ExportFlagsAsync(flags, "browse_flags_true");
        }

        private async void DownloadFalseBrowseButton_Click(object sender, RoutedEventArgs e)
        {
            Dictionary<string, object> flags = _browseResults
                .Where(r => IsFalseValue(ParseValue(r.Value)))
                .ToDictionary(r => r.Name, r => ParseValue(r.Value));

            await ExportFlagsAsync(flags, "browse_flags_false");
        }

        private async Task ExportFlagsAsync(Dictionary<string, object> flags, string defaultName)
        {
            var dialog = new SaveFileDialog
            {
                Filter = $"{Strings.FileTypes_JSONFiles}|*.json",
                FileName = $"{defaultName}_{DateTime.Now:yyyyMMdd_HHmmss}.json"
            };

            if (dialog.ShowDialog() != true)
                return;

            try
            {
                var options = new JsonSerializerOptions
                {
                    WriteIndented = true,
                    Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
                };

                string contents = JsonSerializer.Serialize(flags, options);
                await File.WriteAllTextAsync(dialog.FileName, contents);

                Frontend.ShowMessageBox(
                    String.Format(Strings.Dialog_FFlagSearch_ExportComplete, flags.Count, dialog.FileName),
                    MessageBoxImage.Asterisk
                );
            }
            catch (Exception ex)
            {
                Frontend.ShowMessageBox(String.Format(Strings.Dialog_FFlagSearch_ExportError, ex.Message), MessageBoxImage.Hand);
            }
        }

        private void AddFromSearchButton_Click(object sender, RoutedEventArgs e)
        {
            if (SearchResultsDataGrid.SelectedItem is FlagSearchResult result)
                _addFlagCallback?.Invoke(result.Name, result.Value);
        }

        private void AddFromBrowseButton_Click(object sender, RoutedEventArgs e)
        {
            if (BrowseResultsDataGrid.SelectedItem is FlagSearchResult result)
                _addFlagCallback?.Invoke(result.Name, result.Value);
        }

        private void SearchResultsDataGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (SearchResultsDataGrid.SelectedItem is FlagSearchResult result)
                _addFlagCallback?.Invoke(result.Name, result.Value);
        }

        private void BrowseResultsDataGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (BrowseResultsDataGrid.SelectedItem is FlagSearchResult result)
                _addFlagCallback?.Invoke(result.Name, result.Value);
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

        private async Task UpdateStatusAsync(string status) =>
            await Dispatcher.InvokeAsync(() => StatusText.Text = status);

        private void ShowProgress(bool show) =>
            Dispatcher.Invoke(() => LoadingProgress.Visibility = show ? Visibility.Visible : Visibility.Collapsed);

        private void UpdateSearchResultsCount() =>
            SearchResultsCount.Text = String.Format(Strings.Dialog_FFlagSearch_ResultsCount, _searchResults.Count);

        private static bool IsTrueValue(object? value) => value switch
        {
            bool b => b,
            string s => s.Equals("true", StringComparison.OrdinalIgnoreCase),
            int i => i != 0,
            _ => false,
        };

        private static bool IsFalseValue(object? value) => value switch
        {
            bool b => !b,
            string s => s.Equals("false", StringComparison.OrdinalIgnoreCase),
            int i => i == 0,
            _ => false,
        };

        private static object ParseValue(string value)
        {
            if (bool.TryParse(value, out bool boolResult))
                return boolResult;

            if (int.TryParse(value, out int intResult))
                return intResult;

            if (double.TryParse(value, out double doubleResult))
                return doubleResult;

            return value;
        }

        protected override void OnClosed(EventArgs e)
        {
            Interlocked.Increment(ref _searchGeneration);
            _lifetimeCancellation.Cancel();
            _lifetimeCancellation.Dispose();
            base.OnClosed(e);
        }

        private void TrueFlagsOnlyCheckBox_CheckedChanged(object sender, RoutedEventArgs e) => RerunCurrentSearch();

        private void FalseFlagsOnlyCheckBox_CheckedChanged(object sender, RoutedEventArgs e) => RerunCurrentSearch();

        private void RerunCurrentSearch()
        {
            string? text = SearchTextBox.Text?.Trim();
            if (String.IsNullOrEmpty(text))
                return;

            int generation = Interlocked.Increment(ref _searchGeneration);
            _ = PerformSearchAsync(text, generation, _lifetimeCancellation.Token);
        }

        private void PasteMenuItem_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (!Clipboard.ContainsText())
                    return;

                string text = Clipboard.GetText();
                ValidationInputTextBox.Focus();

                RoutedUICommand paste = ApplicationCommands.Paste;
                if (paste.CanExecute(null, ValidationInputTextBox))
                    paste.Execute(null, ValidationInputTextBox);
                else
                    ValidationInputTextBox.Text = text;
            }
            catch (Exception ex)
            {
                Frontend.ShowMessageBox(String.Format(Strings.Dialog_FFlagSearch_PasteError, ex.Message), MessageBoxImage.Hand);
            }
        }

        private void ClearMenuItem_Click(object sender, RoutedEventArgs e) => ValidationInputTextBox.Clear();

        private void SelectAllMenuItem_Click(object sender, RoutedEventArgs e) => ValidationInputTextBox.SelectAll();

        private void SampleFormatButton_Click(object sender, RoutedEventArgs e)
        {
            ValidationInputTextBox.Text =
                "{\n  \"FFlagDebugDisplayFPS\": \"True\",\n  \"DFIntTaskSchedulerTargetFps\": \"120\",\n  \"FFlagDisablePostFx\": \"False\"\n}";
        }
    }
}
