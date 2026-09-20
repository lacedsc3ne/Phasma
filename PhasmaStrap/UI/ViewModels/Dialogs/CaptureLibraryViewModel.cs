using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;

using CommunityToolkit.Mvvm.Input;

using PhasmaStrap.Utility;

namespace PhasmaStrap.UI.ViewModels.Dialogs
{
    public sealed class CaptureItem : NotifyPropertyChangedViewModel
    {
        public CaptureFile File { get; }
        public string? Game { get; }

        public CaptureItem(CaptureFile file, string? game)
        {
            File = file;
            Game = game;
        }

        public string Path => File.Path;
        public string Name => System.IO.Path.GetFileNameWithoutExtension(File.Path);
        public string FileName => System.IO.Path.GetFileName(File.Path);

        public string Subtitle
        {
            get
            {
                var parts = new List<string>();
                parts.Add(File.Taken.ToString("d MMM yyyy, HH:mm"));
                parts.Add(File.Bytes >= 1048576 ? $"{File.Bytes / 1048576.0:0.0} MB" : $"{File.Bytes / 1024.0:0} KB");
                return string.Join("  ·  ", parts);
            }
        }

        public string GameText => Game ?? "";
        public Visibility GameVisibility => Game is null ? Visibility.Collapsed : Visibility.Visible;

        public bool CanEdit => File.Kind != CaptureKind.Gif;
        public Visibility EditVisibility => CanEdit ? Visibility.Visible : Visibility.Collapsed;
        public Visibility BadgeVisibility => File.Kind == CaptureKind.Screenshot ? Visibility.Collapsed : Visibility.Visible;
        public Wpf.Ui.Common.SymbolRegular Symbol => File.Kind switch
        {
            CaptureKind.Gif => Wpf.Ui.Common.SymbolRegular.Gif24,
            CaptureKind.Video => Wpf.Ui.Common.SymbolRegular.VideoClip24,
            _ => Wpf.Ui.Common.SymbolRegular.Image24,
        };

        private System.Windows.Media.Imaging.BitmapSource? _thumbnail;
        private bool _requested;

        public System.Windows.Media.Imaging.BitmapSource? Thumbnail
        {
            get
            {
                if (!_requested)
                {
                    _requested = true;
                    var dispatcher = Application.Current.Dispatcher;
                    ClipThumbnails.Request(Path, image => dispatcher.BeginInvoke(() =>
                    {
                        _thumbnail = image;
                        OnPropertyChanged(nameof(Thumbnail));
                        OnPropertyChanged(nameof(PlaceholderVisibility));
                    }));
                }
                return _thumbnail;
            }
        }

        public Visibility PlaceholderVisibility => _thumbnail is null ? Visibility.Visible : Visibility.Collapsed;
    }

    public sealed class CaptureRow
    {
        public List<CaptureItem> Items { get; init; } = new();
    }

    public sealed class CaptureLibraryViewModel : NotifyPropertyChangedViewModel, IDisposable
    {
        public const int ScreenshotsTab = 0;
        public const int ClipsTab = 1;

        private List<CaptureItem> _screenshots = new();
        private List<CaptureItem> _clips = new();
        private List<CaptureItem> _shown = new();
        private int _columns = 3;

        public ObservableCollection<CaptureRow> Rows { get; } = new();

        public CaptureLibraryViewModel(int tab)
        {
            _tab = tab;
            Reload();
            Watch(ScreenshotCapture.ScreenshotsDir);
            Watch(InstantReplayRecorder.ClipsDir);
        }

        private int _tab;
        public int Tab
        {
            get => _tab;
            set
            {
                if (_tab == value)
                    return;
                _tab = value;
                OnPropertyChanged(nameof(Tab));
                OnPropertyChanged(nameof(TypeVisibility));
                BuildGameOptions();
                Apply();
            }
        }

        public string ScreenshotsHeader => $"Screenshots ({_screenshots.Count})";
        public string ClipsHeader => $"Clips ({_clips.Count})";

        public Visibility TypeVisibility => _tab == ClipsTab ? Visibility.Visible : Visibility.Collapsed;

        private string _search = "";
        public string Search { get => _search; set { if (_search == value) return; _search = value; OnPropertyChanged(nameof(Search)); Apply(); } }

        public const string AllGames = "All games";
        public const string NoGame = "No game recorded";

        public ObservableCollection<string> GameOptions { get; } = new();

        private string _game = AllGames;
        public string? Game { get => _game; set { if (value is null || _game == value) return; _game = value; OnPropertyChanged(nameof(Game)); Apply(); } }

        private int _dateIndex;
        public int DateIndex { get => _dateIndex; set { if (_dateIndex == value) return; _dateIndex = value; OnPropertyChanged(nameof(DateIndex)); Apply(); } }

        private int _typeIndex;
        public int TypeIndex { get => _typeIndex; set { if (_typeIndex == value) return; _typeIndex = value; OnPropertyChanged(nameof(TypeIndex)); Apply(); } }

        private int _sortIndex;
        public int SortIndex { get => _sortIndex; set { if (_sortIndex == value) return; _sortIndex = value; OnPropertyChanged(nameof(SortIndex)); Apply(); } }

        private string _summary = "";
        public string Summary { get => _summary; private set { _summary = value; OnPropertyChanged(nameof(Summary)); } }

        private string _status = "";
        public string Status { get => _status; private set { _status = value; OnPropertyChanged(nameof(Status)); } }

        public Visibility EmptyVisibility => _shown.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        public string EmptyText => (_tab == ScreenshotsTab ? _screenshots : _clips).Count == 0
            ? (_tab == ScreenshotsTab ? "No screenshots yet - press your screenshot hotkey in a game." : "No clips yet - press your Instant Replay hotkey in a game.")
            : "Nothing matches these filters.";

        public ICommand ClearFiltersCommand => new RelayCommand(() =>
        {
            _search = "";
            _game = AllGames;
            _dateIndex = _typeIndex = _sortIndex = 0;
            OnPropertyChanged(nameof(Search));
            OnPropertyChanged(nameof(Game));
            OnPropertyChanged(nameof(DateIndex));
            OnPropertyChanged(nameof(TypeIndex));
            OnPropertyChanged(nameof(SortIndex));
            Apply();
        });

        public void SetColumns(int columns)
        {
            columns = Math.Max(1, columns);
            if (columns == _columns)
                return;
            _columns = columns;
            BuildRows();
        }

        private void Reload()
        {
            var index = new CaptureLibrary.GameIndex();

            List<CaptureItem> Merge(List<CaptureItem> old, List<CaptureFile> files)
            {
                var known = old.ToDictionary(i => i.Path, StringComparer.OrdinalIgnoreCase);
                return files.Select(f => known.TryGetValue(f.Path, out CaptureItem? item) && item.File == f ? item : new CaptureItem(f, index.GameAt(f.Taken))).ToList();
            }

            _screenshots = Merge(_screenshots, CaptureLibrary.Screenshots());
            _clips = Merge(_clips, CaptureLibrary.Clips());

            OnPropertyChanged(nameof(ScreenshotsHeader));
            OnPropertyChanged(nameof(ClipsHeader));
            BuildGameOptions();
            Apply();
        }

        private void BuildGameOptions()
        {
            List<CaptureItem> source = _tab == ScreenshotsTab ? _screenshots : _clips;

            var options = new List<string> { AllGames };
            options.AddRange(source.Where(i => i.Game is not null).Select(i => i.Game!).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(g => g, StringComparer.OrdinalIgnoreCase));
            if (source.Any(i => i.Game is null))
                options.Add(NoGame);

            if (!options.Contains(_game))
                _game = AllGames;

            if (options.SequenceEqual(GameOptions))
                return;

            GameOptions.Clear();
            foreach (string option in options)
                GameOptions.Add(option);

            Application.Current.Dispatcher.BeginInvoke(() => OnPropertyChanged(nameof(Game)), System.Windows.Threading.DispatcherPriority.Background);
        }

        private void Apply()
        {
            IEnumerable<CaptureItem> items = _tab == ScreenshotsTab ? _screenshots : _clips;

            string search = _search.Trim();
            if (search.Length > 0)
                items = items.Where(i => i.FileName.Contains(search, StringComparison.OrdinalIgnoreCase) || (i.Game?.Contains(search, StringComparison.OrdinalIgnoreCase) ?? false));

            if (_game == NoGame)
                items = items.Where(i => i.Game is null);
            else if (_game != AllGames)
                items = items.Where(i => string.Equals(i.Game, _game, StringComparison.OrdinalIgnoreCase));

            DateTime now = DateTime.Now;
            items = _dateIndex switch
            {
                1 => items.Where(i => i.File.Taken.Date == now.Date),
                2 => items.Where(i => i.File.Taken >= now.AddDays(-7)),
                3 => items.Where(i => i.File.Taken >= now.AddDays(-30)),
                _ => items,
            };

            if (_tab == ClipsTab)
            {
                items = _typeIndex switch
                {
                    1 => items.Where(i => i.File.Kind == CaptureKind.Video),
                    2 => items.Where(i => i.File.Kind == CaptureKind.Gif),
                    _ => items,
                };
            }

            items = _sortIndex switch
            {
                1 => items.OrderBy(i => i.File.Taken),
                2 => items.OrderBy(i => i.Name, StringComparer.CurrentCultureIgnoreCase),
                3 => items.OrderByDescending(i => i.File.Bytes),
                _ => items.OrderByDescending(i => i.File.Taken),
            };

            _shown = items.ToList();

            int total = (_tab == ScreenshotsTab ? _screenshots : _clips).Count;
            long bytes = _shown.Sum(i => i.File.Bytes);
            Summary = _shown.Count == total
                ? $"{total} · {bytes / 1048576.0:0} MB"
                : $"Showing {_shown.Count} of {total}";

            OnPropertyChanged(nameof(EmptyVisibility));
            OnPropertyChanged(nameof(EmptyText));
            BuildRows();
        }

        private void BuildRows()
        {
            Rows.Clear();
            for (int i = 0; i < _shown.Count; i += _columns)
                Rows.Add(new CaptureRow { Items = _shown.Skip(i).Take(_columns).ToList() });
        }

        private readonly List<FileSystemWatcher> _watchers = new();
        private System.Windows.Threading.DispatcherTimer? _debounce;

        private void Watch(string folder)
        {
            try
            {
                Directory.CreateDirectory(folder);
                _debounce ??= CreateDebounce();

                var watcher = new FileSystemWatcher(folder) { NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size };
                var dispatcher = Application.Current.Dispatcher;
                void Changed(object? sender, FileSystemEventArgs e) => dispatcher.BeginInvoke(() =>
                {
                    _debounce?.Stop();
                    _debounce?.Start();
                });

                watcher.Created += Changed;
                watcher.Changed += Changed;
                watcher.Deleted += Changed;
                watcher.Renamed += (s, e) => Changed(s, e);
                watcher.EnableRaisingEvents = true;
                _watchers.Add(watcher);
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine("CaptureLibrary", $"Can't watch {folder}: {ex.Message}");
            }
        }

        private System.Windows.Threading.DispatcherTimer CreateDebounce()
        {
            var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(800) };
            timer.Tick += (_, _) =>
            {
                timer.Stop();
                Reload();
            };
            return timer;
        }

        public void Dispose()
        {
            _debounce?.Stop();
            foreach (FileSystemWatcher watcher in _watchers)
                watcher.Dispose();
            _watchers.Clear();
        }

        public ICommand OpenCommand => new RelayCommand<CaptureItem>(item =>
        {
            if (item is not null && System.IO.File.Exists(item.Path))
                Process.Start(new ProcessStartInfo(item.Path) { UseShellExecute = true });
        });

        public Window? Owner { get; set; }

        public ICommand EditCommand => new RelayCommand<CaptureItem>(item =>
        {
            if (item is null || !item.CanEdit)
                return;
            CaptureLibrary.OpenEditor(item.Path, Owner);
            Reload();
        });

        public ICommand CopyCommand => new RelayCommand<CaptureItem>(item =>
        {
            if (item is null)
                return;
            ClipboardShare.Log ??= message => App.Logger.WriteLine("ClipboardShare", message);
            bool ok = item.File.Kind == CaptureKind.Screenshot ? ClipboardShare.CopyImageFile(item.Path) : ClipboardShare.CopyFile(item.Path);
            Status = ok ? $"Copied {item.FileName} - paste it into a chat with Ctrl+V." : "Could not reach the clipboard - another program is holding it. Try again.";
        });

        public ICommand CopyPathCommand => new RelayCommand<CaptureItem>(item =>
        {
            if (item is null)
                return;
            Status = ClipboardShare.CopyText(item.Path) ? "Copied the file path." : "Could not reach the clipboard - another program is holding it. Try again.";
        });

        public ICommand RevealCommand => new RelayCommand<CaptureItem>(item =>
        {
            if (item is not null)
                NotificationCenter.RevealFile(item.Path)();
        });

        public ICommand RenameCommand => new RelayCommand<CaptureItem>(item =>
        {
            if (item is null)
                return;

            string current = item.Name;
            while (true)
            {
                var dialog = new PhasmaStrap.UI.Elements.Dialogs.TextInputDialog("Rename", $"New name for {item.FileName}:", current) { Owner = Owner };
                dialog.ShowDialog();
                if (!dialog.Confirmed)
                    return;

                string? renamed = CaptureLibrary.Rename(item.Path, dialog.Value, out string? error);
                if (renamed is not null)
                {
                    Status = $"Renamed to {System.IO.Path.GetFileName(renamed)}.";
                    Reload();
                    return;
                }

                Frontend.ShowMessageBox(error ?? "That name can't be used.", MessageBoxImage.Warning);
                current = dialog.Value;
            }
        });

        public ICommand DeleteCommand => new RelayCommand<CaptureItem>(item =>
        {
            if (item is null)
                return;

            if (CaptureLibrary.Recycle(item.Path, out string? error))
            {
                Status = $"{item.FileName} moved to the Recycle Bin.";
                Reload();
            }
            else
            {
                Status = $"Couldn't delete {item.FileName}: {error}";
            }
        });
    }
}
