using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;

using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;

using PhasmaStrap.Integrations;
using PhasmaStrap.Networking;

namespace PhasmaStrap.UI.ViewModels.Settings
{
    public sealed class CachedGameRow : NotifyPropertyChangedViewModel
    {
        public long PlaceId { get; init; }
        public string Name { get; init; } = "";
        public int Known { get; set; }
        public int Cached { get; set; }

        public string Detail => Known == Cached
            ? $"{Known} assets known, all on disk"
            : $"{Known} assets known, {Cached} on disk, {Known - Cached} would be downloaded";

        public bool CanPrefetch => Known > Cached && !Busy;

        private bool _busy;
        public bool Busy { get => _busy; set { _busy = value; OnPropertyChanged(nameof(Busy)); OnPropertyChanged(nameof(CanPrefetch)); } }

        private double _progress;
        public double Progress { get => _progress; set { _progress = value; OnPropertyChanged(nameof(Progress)); } }

        public void Changed()
        {
            OnPropertyChanged(nameof(Detail));
            OnPropertyChanged(nameof(CanPrefetch));
        }
    }

    public sealed class SwapRow
    {
        public SwapPackRow Pack { get; init; } = null!;
        public long AssetId { get; init; }
        public string Text { get; init; } = "";
    }

    public sealed class SwapPackRow : NotifyPropertyChangedViewModel
    {
        private readonly Action _changed;

        public SwapPack Pack { get; }

        public SwapPackRow(SwapPack pack, Action changed)
        {
            Pack = pack;
            _changed = changed;
        }

        public string Name => Pack.Name;
        public string Detail => $"{Pack.Swaps.Count} replacement{(Pack.Swaps.Count == 1 ? "" : "s")}{(Pack.Author.Length > 0 ? $"  ·  by {Pack.Author}" : "")}";
        public List<SwapRow> Swaps => Pack.Swaps.Select(s => new SwapRow { Pack = this, AssetId = s.AssetId, Text = $"{s.AssetId}  →  {s.File}{(s.Note.Length > 0 ? $"   ({s.Note})" : "")}" }).ToList();

        public bool Enabled
        {
            get => Pack.Enabled;
            set { Pack.Enabled = value; AssetContentService.Packs.Save(Pack); OnPropertyChanged(nameof(Enabled)); _changed(); }
        }

        // "" = every game; otherwise place IDs separated by commas
        public string Places
        {
            get => string.Join(", ", Pack.Places);
            set
            {
                Pack.Places = (value ?? "").Split(new[] { ',', ' ', ';' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(part => long.TryParse(part, out long id) ? id : 0).Where(id => id > 0).Distinct().ToList();
                AssetContentService.Packs.Save(Pack);
                OnPropertyChanged(nameof(Places));
            }
        }
    }

    public sealed class TrafficRow
    {
        public string Name { get; init; } = "";
        public string Numbers { get; init; } = "";
        public string Types { get; init; } = "";
        public string Hosts { get; init; } = "";
    }

    /// <summary>
    /// Backs the "Asset engine" tab: routing asset downloads through the proxy, and what that
    /// makes possible - the persistent cache, the texture shrinker, swap packs, the traffic report.
    /// </summary>
    public sealed class AssetEngineViewModel : NotifyPropertyChangedViewModel
    {
        public ObservableCollection<CachedGameRow> Games { get; } = new();
        public ObservableCollection<SwapPackRow> Packs { get; } = new();
        public ObservableCollection<TrafficRow> Traffic { get; } = new();

        public AssetEngineViewModel()
        {
            Refresh();
        }

        private static string Size(long bytes) => bytes >= 1073741824L ? $"{bytes / 1073741824.0:0.0} GB" : $"{bytes / 1048576.0:0} MB";

        private static Dictionary<long, string> GameNames()
        {
            var names = new Dictionary<long, string>();
            try
            {
                foreach (Models.PlayTimeEntry entry in PlayTimeStore.GetAll())
                {
                    if (entry.Name.Length > 0)
                        names[entry.PlaceId] = entry.Name;
                }
            }
            catch
            {
            }
            return names;
        }

        // ------------------------------------------------------------------ status + master switch

        public string StatusText
        {
            get
            {
                if (!App.Settings.Prop.NetworkingProxyEnabled)
                    return "The proxy is switched off (Networking page), so none of this is active. Everything below needs it, because the proxy is what the game's asset requests pass through.";

                return App.Settings.Prop.AssetRouteEnabled
                    ? "Active: asset downloads pass through PhasmaStrap. Roblox has to be started after this was switched on."
                    : "The proxy is on, but asset downloads still go straight to Roblox. Swap packs work either way; the cache, the shrinker and the traffic report need the switch below.";
            }
        }

        public bool RouteEnabled
        {
            get => App.Settings.Prop.AssetRouteEnabled;
            set { App.Settings.Prop.AssetRouteEnabled = value; App.Settings.SaveDeferred(); OnPropertyChanged(nameof(RouteEnabled)); OnPropertyChanged(nameof(StatusText)); }
        }

        // ------------------------------------------------------------------ cache

        private static readonly int[] LimitValuesMb = { 1024, 2048, 4096, 8192, 16384, 32768 };
        public string[] CacheLimitOptions { get; } = { "1 GB", "2 GB", "4 GB", "8 GB", "16 GB", "32 GB" };

        public bool CacheEnabled
        {
            get => App.Settings.Prop.AssetCacheEnabled;
            set { App.Settings.Prop.AssetCacheEnabled = value; App.Settings.SaveDeferred(); OnPropertyChanged(nameof(CacheEnabled)); }
        }

        public string SelectedCacheLimit
        {
            get => CacheLimitOptions[Math.Max(0, Array.IndexOf(LimitValuesMb, App.Settings.Prop.AssetCacheLimitMb))];
            set
            {
                int index = Array.IndexOf(CacheLimitOptions, value);
                if (index < 0)
                    return;

                App.Settings.Prop.AssetCacheLimitMb = LimitValuesMb[index];
                App.Settings.SaveDeferred();
                OnPropertyChanged(nameof(SelectedCacheLimit));
            }
        }

        private string _cacheUsage = "";
        public string CacheUsage { get => _cacheUsage; private set { _cacheUsage = value; OnPropertyChanged(nameof(CacheUsage)); } }

        public Visibility GamesEmptyVisibility => Games.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        public ICommand RefreshCommand => new RelayCommand(Refresh);

        public ICommand ClearCacheCommand => new RelayCommand(() =>
        {
            var answer = Frontend.ShowMessageBox("Delete every cached asset?\n\nNothing breaks - games simply download their assets again the next time.", MessageBoxImage.Question, MessageBoxButton.YesNo);
            if (answer != MessageBoxResult.Yes)
                return;

            AssetContentService.Cache.Clear();
            Refresh();
        });

        public ICommand PrefetchCommand => new AsyncRelayCommand<CachedGameRow?>(PrefetchAsync);

        private async Task PrefetchAsync(CachedGameRow? row)
        {
            if (row is null || row.Busy)
                return;

            row.Busy = true;
            row.Progress = 0;

            try
            {
                var result = await Task.Run(() => AssetContentService.PrefetchAsync(row.PlaceId,
                    progress => Application.Current.Dispatcher.BeginInvoke(new Action(() => row.Progress = progress)), CancellationToken.None));

                PrefetchStatus = $"{row.Name}: {result.Fetched} downloaded, {result.AlreadyThere} were already on disk{(result.Failed > 0 ? $", {result.Failed} could not be fetched (deleted or private assets)" : "")}.";
            }
            catch (Exception ex)
            {
                App.Logger.WriteException("AssetEngineViewModel::Prefetch", ex);
                PrefetchStatus = $"Prefetch failed: {ex.Message}";
            }
            finally
            {
                row.Busy = false;
                Refresh();
            }
        }

        private string _prefetchStatus = "";
        public string PrefetchStatus { get => _prefetchStatus; private set { _prefetchStatus = value; OnPropertyChanged(nameof(PrefetchStatus)); } }

        // ------------------------------------------------------------------ shrinker

        private static readonly int[] ShrinkValues = { 1024, 512, 256, 128 };
        public string[] ShrinkOptions { get; } = { "1024 px (barely visible)", "512 px (recommended)", "256 px (soft)", "128 px (blurry, smallest)" };

        public bool ShrinkEnabled
        {
            get => App.Settings.Prop.TextureShrinkEnabled;
            set { App.Settings.Prop.TextureShrinkEnabled = value; App.Settings.SaveDeferred(); OnPropertyChanged(nameof(ShrinkEnabled)); }
        }

        public string SelectedShrink
        {
            get => ShrinkOptions[Math.Max(0, Array.IndexOf(ShrinkValues, App.Settings.Prop.TextureShrinkMaxSize))];
            set
            {
                int index = Array.IndexOf(ShrinkOptions, value);
                if (index < 0)
                    return;

                App.Settings.Prop.TextureShrinkMaxSize = ShrinkValues[index];
                App.Settings.SaveDeferred();
                OnPropertyChanged(nameof(SelectedShrink));
            }
        }

        // ------------------------------------------------------------------ swap packs

        public bool SwapsEnabled
        {
            get => App.Settings.Prop.SwapPacksEnabled;
            set { App.Settings.Prop.SwapPacksEnabled = value; App.Settings.SaveDeferred(); OnPropertyChanged(nameof(SwapsEnabled)); }
        }

        public Visibility PacksEmptyVisibility => Packs.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        private string _newPackName = "";
        public string NewPackName { get => _newPackName; set { _newPackName = value ?? ""; OnPropertyChanged(nameof(NewPackName)); } }

        private string _swapAssetId = "";
        public string SwapAssetId { get => _swapAssetId; set { _swapAssetId = value ?? ""; OnPropertyChanged(nameof(SwapAssetId)); } }

        private string _swapNote = "";
        public string SwapNote { get => _swapNote; set { _swapNote = value ?? ""; OnPropertyChanged(nameof(SwapNote)); } }

        private SwapPackRow? _selectedPack;
        public SwapPackRow? SelectedPack { get => _selectedPack; set { _selectedPack = value; OnPropertyChanged(nameof(SelectedPack)); } }

        private string _packStatus = "";
        public string PackStatus { get => _packStatus; private set { _packStatus = value; OnPropertyChanged(nameof(PackStatus)); } }

        public ICommand CreatePackCommand => new RelayCommand(() =>
        {
            string name = _newPackName.Trim();
            if (name.Length == 0)
            {
                PackStatus = "Give the pack a name first.";
                return;
            }

            AssetContentService.Packs.Save(new SwapPack { Name = name, Author = Environment.UserName });
            NewPackName = "";
            PackStatus = $"Created \"{name}\". Add replacements to it below.";
            RefreshPacks(name);
        });

        public ICommand AddSwapCommand => new RelayCommand(() =>
        {
            if (_selectedPack is null)
            {
                PackStatus = "Pick the pack the replacement goes into.";
                return;
            }

            if (!long.TryParse(_swapAssetId.Trim(), out long assetId) || assetId <= 0)
            {
                PackStatus = "The asset ID is the number in the asset's URL (roblox.com/library/<number>/...).";
                return;
            }

            var dialog = new OpenFileDialog
            {
                Title = $"Replacement for asset {assetId}",
                Filter = "Images, sounds and meshes|*.png;*.jpg;*.jpeg;*.bmp;*.ogg;*.mp3;*.wav;*.mesh;*.rbxm;*.rbxmx|All files|*.*",
            };

            if (dialog.ShowDialog() != true)
                return;

            try
            {
                AssetContentService.Packs.AddSwap(_selectedPack.Pack, assetId, dialog.FileName, _swapNote.Trim());
                PackStatus = $"Asset {assetId} is now replaced by {System.IO.Path.GetFileName(dialog.FileName)} in \"{_selectedPack.Name}\". It takes effect the next time a game downloads that asset - Roblox keeps its own copy of what it already has, so that can mean after its cache expires.";
                SwapAssetId = "";
                SwapNote = "";
                RefreshPacks(_selectedPack.Name);
            }
            catch (Exception ex)
            {
                PackStatus = $"Could not add it: {ex.Message}";
            }
        });

        public ICommand RemoveSwapCommand => new RelayCommand<SwapRow?>(row =>
        {
            if (row is null)
                return;

            AssetContentService.Packs.RemoveSwap(row.Pack.Pack, row.AssetId);
            RefreshPacks(row.Pack.Name);
        });

        public ICommand DeletePackCommand => new RelayCommand<SwapPackRow?>(row =>
        {
            if (row is null)
                return;

            var answer = Frontend.ShowMessageBox($"Delete the pack \"{row.Name}\" and its {row.Pack.Swaps.Count} replacement file(s)?", MessageBoxImage.Warning, MessageBoxButton.YesNo);
            if (answer != MessageBoxResult.Yes)
                return;

            AssetContentService.Packs.Delete(row.Pack);
            RefreshPacks(null);
        });

        public ICommand ExportPackCommand => new RelayCommand<SwapPackRow?>(row =>
        {
            if (row is null)
                return;

            var dialog = new SaveFileDialog { FileName = row.Pack.Folder + SwapPackStore.Extension, Filter = $"PhasmaStrap swap pack|*{SwapPackStore.Extension}" };
            if (dialog.ShowDialog() != true)
                return;

            try
            {
                AssetContentService.Packs.Export(row.Pack, dialog.FileName);
                PackStatus = $"Exported to {dialog.FileName}.";
            }
            catch (Exception ex)
            {
                PackStatus = $"Export failed: {ex.Message}";
            }
        });

        public ICommand ImportPackCommand => new RelayCommand(() =>
        {
            var dialog = new OpenFileDialog { Filter = $"PhasmaStrap swap pack|*{SwapPackStore.Extension}|Zip files|*.zip" };
            if (dialog.ShowDialog() != true)
                return;

            try
            {
                SwapPack pack = AssetContentService.Packs.Import(dialog.FileName);
                PackStatus = $"Imported \"{pack.Name}\" ({pack.Swaps.Count} replacements). It arrives switched off - look through it, then switch it on.";
                RefreshPacks(pack.Name);
            }
            catch (Exception ex)
            {
                PackStatus = $"Not imported: {ex.Message}";
            }
        });

        public ICommand OpenPacksFolderCommand => new RelayCommand(() =>
        {
            Directory.CreateDirectory(AssetContentService.Packs.Root);
            Process.Start("explorer.exe", AssetContentService.Packs.Root);
        });

        private void RefreshPacks(string? select)
        {
            Packs.Clear();
            foreach (SwapPack pack in AssetContentService.Packs.List())
                Packs.Add(new SwapPackRow(pack, () => OnPropertyChanged(nameof(StatusText))));

            SelectedPack = Packs.FirstOrDefault(p => p.Name == select) ?? Packs.FirstOrDefault();
            OnPropertyChanged(nameof(PacksEmptyVisibility));
        }

        // ------------------------------------------------------------------ traffic

        public bool TrafficEnabled
        {
            get => App.Settings.Prop.TrafficReportEnabled;
            set { App.Settings.Prop.TrafficReportEnabled = value; App.Settings.SaveDeferred(); OnPropertyChanged(nameof(TrafficEnabled)); }
        }

        public Visibility TrafficEmptyVisibility => Traffic.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        public ICommand ClearTrafficCommand => new RelayCommand(() =>
        {
            AssetContentService.Traffic.Clear();
            Refresh();
        });

        // ------------------------------------------------------------------ refresh

        private void Refresh()
        {
            Dictionary<long, string> names = GameNames();

            (int count, long bytes) = AssetContentService.Cache.Size();
            CacheUsage = count == 0 ? "The cache is empty." : $"{count:N0} assets, {Size(bytes)} on disk.";

            Games.Clear();
            foreach ((long placeId, int known, int cached) in AssetContentService.ListManifests().OrderByDescending(m => m.Known).Take(40))
                Games.Add(new CachedGameRow { PlaceId = placeId, Name = names.GetValueOrDefault(placeId) ?? $"Place {placeId}", Known = known, Cached = cached });
            OnPropertyChanged(nameof(GamesEmptyVisibility));

            RefreshPacks(_selectedPack?.Name);

            Traffic.Clear();
            foreach (PlaceTraffic place in AssetContentService.Traffic.Load().Places.Values.OrderByDescending(p => p.LastUtc).Take(40))
            {
                TrafficBucket total = place.Total;
                long toGame = total.DownloadedBytes - total.ShrinkSavedBytes + total.ServedFromCacheBytes;
                double hitRate = total.Requests > 0 ? total.CacheHits * 100.0 / total.Requests : 0;

                Traffic.Add(new TrafficRow
                {
                    Name = $"{(place.PlaceId > 0 ? names.GetValueOrDefault(place.PlaceId) ?? $"Place {place.PlaceId}" : "Outside a game (menus, avatar)")}  ·  last {place.LastUtc.ToLocalTime():d MMM, HH:mm}",
                    Numbers = $"{total.Requests:N0} assets  ·  {Size(total.DownloadedBytes)} downloaded  ·  {hitRate:0} % came from the cache ({Size(total.ServedFromCacheBytes)} not downloaded again)"
                        + (total.ShrinkSavedBytes > 0 ? $"  ·  shrinker took off {Size(total.ShrinkSavedBytes)}" : "")
                        + (total.Swapped > 0 ? $"  ·  {total.Swapped} swapped" : "")
                        + (total.Failed > 0 ? $"  ·  {total.Failed} handed back to the CDN" : ""),
                    Types = string.Join("   ", place.ByType.OrderByDescending(t => t.Value.DownloadedBytes + t.Value.ServedFromCacheBytes).Take(5)
                        .Select(t => $"{t.Key}: {t.Value.Requests:N0} ({Size(t.Value.DownloadedBytes + t.Value.ServedFromCacheBytes)})")),
                    Hosts = place.Hosts.Count == 0 ? "" : "Slowest servers: " + string.Join("   ", place.Hosts.OrderByDescending(h => h.Value.AverageMs).Take(3)
                        .Select(h => $"{h.Key} {h.Value.AverageMs:0} ms on average (worst {h.Value.SlowestMs} ms, {h.Value.Requests} requests)")),
                });
            }
            OnPropertyChanged(nameof(TrafficEmptyVisibility));

            OnPropertyChanged(nameof(StatusText));
        }
    }
}
