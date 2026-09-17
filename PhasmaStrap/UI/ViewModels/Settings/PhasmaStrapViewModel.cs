using System.Windows;
using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;
using ICSharpCode.SharpZipLib.Zip;
using Microsoft.Win32;

namespace PhasmaStrap.UI.ViewModels.Settings
{
    public class PhasmaStrapViewModel : NotifyPropertyChangedViewModel
    {
        public WebEnvironment[] WebEnvironments => Enum.GetValues<WebEnvironment>();

        public bool UpdateCheckingEnabled
        {
            get => App.Settings.Prop.CheckForUpdates;
            set => App.Settings.Prop.CheckForUpdates = value;
        }

        public bool AnalyticsEnabled
        {
            get => App.Settings.Prop.EnableAnalytics;
            set => App.Settings.Prop.EnableAnalytics = value;
        }

        public bool LaunchAtStartupEnabled
        {
            get => App.Settings.Prop.LaunchAtStartup;
            set
            {
                App.Settings.Prop.LaunchAtStartup = value;

                try
                {
                    if (value)
                        WindowsRegistry.RegisterStartup();
                    else
                        WindowsRegistry.UnregisterStartup();
                }
                catch (Exception ex)
                {
                    App.Logger.WriteException("PhasmaStrapViewModel::LaunchAtStartupEnabled", ex);
                }
            }
        }

        public bool MinimizeToTrayOnCloseEnabled
        {
            get => App.Settings.Prop.MinimizeToTrayOnClose;
            set => App.Settings.Prop.MinimizeToTrayOnClose = value;
        }

        public WebEnvironment WebEnvironment
        {
            get => App.Settings.Prop.WebEnvironment;
            set => App.Settings.Prop.WebEnvironment = value;
        }

        public Visibility WebEnvironmentVisibility => App.Settings.Prop.DeveloperMode ? Visibility.Visible : Visibility.Collapsed;

        public bool ShouldExportConfig { get; set; } = true;

        public bool ShouldExportLogs { get; set; } = true;

        // for bug reports: OS/.NET/GPU/PhasmaStrap version, not personal data - reuses
        // GpuInventory.Summary (already computed for the Overlays/RiShade/NVIDIA pages) rather
        // than querying WMI a second time here
        public bool ShouldExportSystemInfo { get; set; } = true;

        public ICommand ExportDataCommand => new RelayCommand(ExportData);

        private void ExportData()
        {
            string timestamp = DateTime.UtcNow.ToString("yyyyMMdd'T'HHmmss'Z'");

            var dialog = new SaveFileDialog 
            { 
                FileName = $"PhasmaStrap-export-{timestamp}.zip",
                Filter = $"{Strings.FileTypes_ZipArchive}|*.zip" 
            };

            if (dialog.ShowDialog() != true)
                return;

            using var memStream = new MemoryStream();
            using var zipStream = new ZipOutputStream(memStream);

            if (ShouldExportConfig)
            {
                var files = new List<string>()
                {
                    App.Settings.FileLocation,
                    App.State.FileLocation,
                    App.FastFlags.FileLocation
                };

                AddFilesToZipStream(zipStream, files, "Config/");
            }

            if (ShouldExportLogs && Directory.Exists(Paths.Logs))
            {
                var files = Directory.GetFiles(Paths.Logs)
                    .Where(x => !x.Equals(App.Logger.FileLocation, StringComparison.OrdinalIgnoreCase));

                AddFilesToZipStream(zipStream, files, "Logs/");
            }

            if (ShouldExportSystemInfo)
            {
                string specs = string.Join(Environment.NewLine,
                    $"PhasmaStrap {App.Version}",
                    $"OS: {Environment.OSVersion.VersionString} ({(Environment.Is64BitOperatingSystem ? "64" : "32")}-bit)",
                    $".NET: {Environment.Version}",
                    $"GPU(s): {PhasmaStrap.Utility.GpuInventory.Summary}",
                    $"Generated: {DateTime.UtcNow:u}");

                var entry = new ZipEntry("SystemInfo.txt") { DateTime = DateTime.Now };
                zipStream.PutNextEntry(entry);
                byte[] bytes = Encoding.UTF8.GetBytes(specs);
                zipStream.Write(bytes, 0, bytes.Length);
            }

            zipStream.CloseEntry();
            zipStream.Finish();
            memStream.Position = 0;

            using var outputStream = File.OpenWrite(dialog.FileName);
            memStream.CopyTo(outputStream);

            Process.Start("explorer.exe", $"/select,\"{dialog.FileName}\"");
        }

        // Restores whatever "Config/" entries an export made with ExportData above contains -
        // matched back to their real target file by name, not by hardcoding the 3 filenames
        // twice, so this can never drift out of sync with what export actually writes.
        public ICommand ImportDataCommand => new RelayCommand(ImportData);

        private void ImportData()
        {
            const string LOG_IDENT = "PhasmaStrapViewModel::ImportData";

            var dialog = new OpenFileDialog
            {
                Filter = $"{Strings.FileTypes_ZipArchive}|*.zip"
            };

            if (dialog.ShowDialog() != true)
                return;

            MessageBoxResult confirm = Frontend.ShowMessageBox(
                "This overwrites your current PhasmaStrap settings, saved state, and FastFlags with whatever's in this export. This cannot be undone.\n\nContinue?",
                MessageBoxImage.Warning,
                MessageBoxButton.YesNo,
                MessageBoxResult.No);

            if (confirm != MessageBoxResult.Yes)
                return;

            var targets = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [Path.GetFileName(App.Settings.FileLocation)] = App.Settings.FileLocation,
                [Path.GetFileName(App.State.FileLocation)] = App.State.FileLocation,
                [Path.GetFileName(App.FastFlags.FileLocation)] = App.FastFlags.FileLocation,
            };

            int imported = 0;

            try
            {
                using var zip = new ZipFile(dialog.FileName);

                foreach (ZipEntry entry in zip)
                {
                    if (!entry.IsFile || !entry.Name.StartsWith("Config/", StringComparison.OrdinalIgnoreCase))
                        continue;

                    string fileName = Path.GetFileName(entry.Name);
                    if (!targets.TryGetValue(fileName, out string? destination))
                        continue;

                    using Stream zipStream = zip.GetInputStream(entry);
                    using FileStream outStream = File.Create(destination);
                    zipStream.CopyTo(outStream);
                    imported++;
                }
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine(LOG_IDENT, $"Import failed: {ex.Message}");
                App.Logger.WriteException(LOG_IDENT, ex);
                Frontend.ShowMessageBox($"Import failed: {ex.Message}", MessageBoxImage.Error);
                return;
            }

            if (imported == 0)
            {
                Frontend.ShowMessageBox("No PhasmaStrap config files were found in that archive.", MessageBoxImage.Warning);
                return;
            }

            // Load() reassigns each manager's Prop backing field, so every already-open settings
            // page picks up the new values on its next read - no restart needed for the values
            // themselves (some derived UI state, like which page is currently displayed, may
            // still look stale until you navigate away and back).
            App.Settings.Load(alertFailure: false);
            App.State.Load(alertFailure: false);
            App.FastFlags.Load(alertFailure: false);

            Frontend.ShowMessageBox($"Imported {imported} config file(s).", MessageBoxImage.Information);
        }

        private void AddFilesToZipStream(ZipOutputStream zipStream, IEnumerable<string> files, string directory)
        {
            const string LOG_IDENT = "PhasmaStrapViewModel::AddFilesToZipStream";

            foreach (string file in files)
            {
                if (!File.Exists(file))
                    continue;

                try
                {
                    using FileStream fileStream = File.OpenRead(file);

                    var entry = new ZipEntry(directory + Path.GetFileName(file));
                    entry.DateTime = DateTime.Now;

                    zipStream.PutNextEntry(entry);

                    fileStream.CopyTo(zipStream);
                }
                catch (IOException ex)
                {
                    App.Logger.WriteLine(LOG_IDENT, $"Failed to open '{file}'");
                    App.Logger.WriteException(LOG_IDENT, ex);
                }
            }
        }
    }
}
