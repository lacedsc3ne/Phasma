using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

using Microsoft.Win32;

using Windows.Win32;
using Windows.Win32.UI.Shell;
using Windows.Win32.Foundation;

using CommunityToolkit.Mvvm.Input;

using PhasmaStrap.Models.SettingTasks;
using PhasmaStrap.AppData;
using PhasmaStrap.Integrations;
using PhasmaStrap.UI.Elements.Dialogs;
using PhasmaStrap.Utility;

namespace PhasmaStrap.UI.ViewModels.Settings
{
    public class ModsViewModel : NotifyPropertyChangedViewModel
    {
        public ModsViewModel()
        {
            _ = LoadManagedModsAsync();
            LoadCursorSets();
        }

        private void OpenModsFolder() => Process.Start("explorer.exe", Paths.Modifications);

        private readonly Dictionary<string, byte[]> FontHeaders = new()
        {
            { "ttf", new byte[4] { 0x00, 0x01, 0x00, 0x00 } },
            { "otf", new byte[4] { 0x4F, 0x54, 0x54, 0x4F } },
            { "ttc", new byte[4] { 0x74, 0x74, 0x63, 0x66 } }
        };

        private void ManageCustomFont()
        {
            if (!String.IsNullOrEmpty(TextFontTask.NewState))
            {
                TextFontTask.NewState = "";
            }
            else
            {
                var dialog = new OpenFileDialog
                {
                    Filter = $"{Strings.Menu_FontFiles}|*.ttf;*.otf;*.ttc"
                };

                if (dialog.ShowDialog() != true)
                    return;

                string type = dialog.FileName.Substring(dialog.FileName.Length-3, 3).ToLowerInvariant();

                if (!FontHeaders.ContainsKey(type)
                    || !FontHeaders.Any(x => File.ReadAllBytes(dialog.FileName).Take(4).SequenceEqual(x.Value)))
                {
                    Frontend.ShowMessageBox(Strings.Menu_Mods_Misc_CustomFont_Invalid, MessageBoxImage.Error);
                    return;
                }

                TextFontTask.NewState = dialog.FileName;
            }

            OnPropertyChanged(nameof(ChooseCustomFontVisibility));
            OnPropertyChanged(nameof(DeleteCustomFontVisibility));
        }

        private void BrowseGoogleFonts()
        {
            var dialog = new UI.Elements.Dialogs.GoogleFontsDialog();

            if (dialog.ShowDialog() != true || string.IsNullOrEmpty(dialog.SelectedFontPath))
                return;

            TextFontTask.NewState = dialog.SelectedFontPath;

            OnPropertyChanged(nameof(ChooseCustomFontVisibility));
            OnPropertyChanged(nameof(DeleteCustomFontVisibility));
        }

        public ICommand BrowseGoogleFontsCommand => new RelayCommand(BrowseGoogleFonts);

        public ICommand OpenModsFolderCommand => new RelayCommand(OpenModsFolder);

        public Visibility ChooseCustomFontVisibility => !String.IsNullOrEmpty(TextFontTask.NewState) ? Visibility.Collapsed : Visibility.Visible;

        public Visibility DeleteCustomFontVisibility => !String.IsNullOrEmpty(TextFontTask.NewState) ? Visibility.Visible : Visibility.Collapsed;

        public ICommand ManageCustomFontCommand => new RelayCommand(ManageCustomFont);

        public ICommand OpenCompatSettingsCommand => new RelayCommand(OpenCompatSettings);

        public ModPresetTask OldAvatarBackgroundTask { get; } = new("OldAvatarBackground", @"ExtraContent\places\Mobile.rbxl", "OldAvatarBackground.rbxl");

        public ModPresetTask OldCharacterSoundsTask { get; } = new("OldCharacterSounds", new()
        {
            { @"content\sounds\action_footsteps_plastic.mp3", "Sounds.OldWalk.mp3"  },
            { @"content\sounds\action_jump.mp3",              "Sounds.OldJump.mp3"  },
            { @"content\sounds\action_get_up.mp3",            "Sounds.OldGetUp.mp3" },
            { @"content\sounds\action_falling.mp3",           "Sounds.Empty.mp3"    },
            { @"content\sounds\action_jump_land.mp3",         "Sounds.Empty.mp3"    },
            { @"content\sounds\action_swim.mp3",              "Sounds.Empty.mp3"    },
            { @"content\sounds\impact_water.mp3",             "Sounds.Empty.mp3"    }
        });

        public EmojiModPresetTask EmojiFontTask { get; } = new();

        public EnumModPresetTask<Enums.CursorType> CursorTypeTask { get; } = new("CursorType", new()
        {
            {
                Enums.CursorType.From2006, new()
                {
                    { @"content\textures\Cursors\KeyboardMouse\ArrowCursor.png",    "Cursor.From2006.ArrowCursor.png"    },
                    { @"content\textures\Cursors\KeyboardMouse\ArrowFarCursor.png", "Cursor.From2006.ArrowFarCursor.png" }
                }
            },
            {
                Enums.CursorType.From2013, new()
                {
                    { @"content\textures\Cursors\KeyboardMouse\ArrowCursor.png",    "Cursor.From2013.ArrowCursor.png"    },
                    { @"content\textures\Cursors\KeyboardMouse\ArrowFarCursor.png", "Cursor.From2013.ArrowFarCursor.png" }
                }
            }
        });

        public FontModPresetTask TextFontTask { get; } = new();

        public CustomCursorModPresetTask CustomCursorSetTask { get; } = new();

        public string CustomCursorSetFolderDisplay => String.IsNullOrEmpty(CustomCursorSetTask.NewState) ? "No folder selected." : CustomCursorSetTask.NewState;

        public Visibility ChooseCustomCursorSetVisibility => String.IsNullOrEmpty(CustomCursorSetTask.NewState) ? Visibility.Visible : Visibility.Collapsed;

        public Visibility DeleteCustomCursorSetVisibility => String.IsNullOrEmpty(CustomCursorSetTask.NewState) ? Visibility.Collapsed : Visibility.Visible;

        public Visibility PreviewCustomCursorSetVisibility => DeleteCustomCursorSetVisibility;

        private void ManageCustomCursorSet()
        {
            if (!String.IsNullOrEmpty(CustomCursorSetTask.NewState))
            {
                CustomCursorSetTask.NewState = "";
            }
            else
            {
                var dialog = new OpenFileDialog
                {
                    Title = "Pick any cursor image in the folder you want to use",
                    Filter = CursorImages.PickerFilter,
                    CheckFileExists = true
                };

                if (Directory.Exists(CustomCursorSetTask.NewState))
                    dialog.InitialDirectory = CustomCursorSetTask.NewState;

                if (dialog.ShowDialog() != true)
                    return;

                string? folder = Path.GetDirectoryName(dialog.FileName);

                if (string.IsNullOrEmpty(folder))
                    return;

                bool foundAny = CursorImages.AnyIn(folder, CustomCursorModPresetTask.RecognizedFileNames);

                if (!foundAny)
                {
                    Frontend.ShowMessageBox(
                        $"Nothing in \"{Path.GetFileName(folder)}\" is named like a Roblox cursor, so none of it would be used.\n\n" +
                        "That folder needs at least one image named ArrowCursor, ArrowFarCursor, IBeamCursor or MouseLockedCursor, as " + CursorImages.ReadableList + ".\n\n" +
                        "To build a set out of pictures with any name, make one under Cursor sets below and use the Browse button next to each cursor.",
                        MessageBoxImage.Error);
                    return;
                }

                CustomCursorSetTask.NewState = folder;

                if (!CursorTypeTask.NewState.Equals(default(Enums.CursorType)))
                    CursorTypeTask.NewState = default;
            }

            OnPropertyChanged(nameof(CustomCursorSetFolderDisplay));
            OnPropertyChanged(nameof(ChooseCustomCursorSetVisibility));
            OnPropertyChanged(nameof(DeleteCustomCursorSetVisibility));
            OnPropertyChanged(nameof(PreviewCustomCursorSetVisibility));
        }

        private void PreviewCustomCursorSet()
        {
            if (String.IsNullOrEmpty(CustomCursorSetTask.NewState))
                return;

            new CursorPreviewDialog(CustomCursorSetTask.NewState).ShowDialog();
        }

        public ICommand ManageCustomCursorSetCommand => new RelayCommand(ManageCustomCursorSet);

        public ICommand PreviewCustomCursorSetCommand => new RelayCommand(PreviewCustomCursorSet);

        private void OpenCompatSettings()
        {
            string path = new RobloxPlayerData().ExecutablePath;

            if (File.Exists(path))
                PInvoke.SHObjectProperties(HWND.Null, SHOP_TYPE.SHOP_FILEPATH, path, "Compatibility");
            else
                Frontend.ShowMessageBox(Strings.Common_RobloxNotInstalled, MessageBoxImage.Error);
        }

        #region Preset Mod - mod apply target

        public IEnumerable<Enums.ModApplyTarget> ModApplyTargets { get; } = Enum.GetValues(typeof(Enums.ModApplyTarget)).Cast<Enums.ModApplyTarget>();

        public Enums.ModApplyTarget ModApplyTarget
        {
            get => App.Settings.Prop.ModApplyTarget;
            set
            {
                App.Settings.Prop.ModApplyTarget = value;
                OnPropertyChanged(nameof(ModApplyTarget));
            }
        }

        #endregion

        #region Preset Mod - skybox manager

        private readonly Dictionary<string, string> _pendingSkyboxFaces = new(StringComparer.OrdinalIgnoreCase);

        public bool HasCustomSkybox => SkyboxImageConverter.HasCustomPack();

        private string FaceDisplay(string faceFileName) =>
            _pendingSkyboxFaces.TryGetValue(faceFileName, out string? path) ? Path.GetFileName(path) : "Not set";

        public string CustomSkyboxBack => FaceDisplay("sky512_bk.tex");
        public string CustomSkyboxDown => FaceDisplay("sky512_dn.tex");
        public string CustomSkyboxFront => FaceDisplay("sky512_ft.tex");
        public string CustomSkyboxLeft => FaceDisplay("sky512_lf.tex");
        public string CustomSkyboxRight => FaceDisplay("sky512_rt.tex");
        public string CustomSkyboxUp => FaceDisplay("sky512_up.tex");

        public bool CustomSkyboxCanApply => _pendingSkyboxFaces.Count == SkyboxImageConverter.Faces.Length;

        private void RaiseSkyboxFaceProperties()
        {
            OnPropertyChanged(nameof(CustomSkyboxBack));
            OnPropertyChanged(nameof(CustomSkyboxDown));
            OnPropertyChanged(nameof(CustomSkyboxFront));
            OnPropertyChanged(nameof(CustomSkyboxLeft));
            OnPropertyChanged(nameof(CustomSkyboxRight));
            OnPropertyChanged(nameof(CustomSkyboxUp));
            OnPropertyChanged(nameof(CustomSkyboxCanApply));
        }

        private void ChooseSkyboxFace(object? parameter)
        {
            string? faceName = parameter as string;
            (string FaceName, string FileName)? face = SkyboxImageConverter.Faces.FirstOrDefault(f => f.FaceName == faceName);

            if (face is null || string.IsNullOrEmpty(face.Value.FileName))
                return;

            var dialog = new OpenFileDialog { Filter = "Images|*.png;*.jpg;*.jpeg;*.bmp" };

            if (dialog.ShowDialog() != true)
                return;

            _pendingSkyboxFaces[face.Value.FileName] = dialog.FileName;
            RaiseSkyboxFaceProperties();
        }

        private void ChooseSingleSkyboxImage()
        {
            var dialog = new OpenFileDialog { Filter = "Images|*.png;*.jpg;*.jpeg;*.bmp" };

            if (dialog.ShowDialog() != true)
                return;

            foreach (var face in SkyboxImageConverter.Faces)
                _pendingSkyboxFaces[face.FileName] = dialog.FileName;

            RaiseSkyboxFaceProperties();
        }

        private void ApplyCustomSkybox()
        {
            if (!CustomSkyboxCanApply)
                return;

            try
            {
                bool allSameImage = _pendingSkyboxFaces.Values.Distinct().Count() == 1;

                if (allSameImage)
                    SkyboxImageConverter.ImportSingleImage(_pendingSkyboxFaces.Values.First());
                else
                    SkyboxImageConverter.ImportPerFace(_pendingSkyboxFaces);

                _pendingSkyboxFaces.Clear();
                RaiseSkyboxFaceProperties();
                OnPropertyChanged(nameof(HasCustomSkybox));
            }
            catch (Exception ex)
            {
                Frontend.ShowMessageBox("The skybox could not be applied:\n" + ex.Message, MessageBoxImage.Warning);
            }
        }

        private void RemoveCustomSkybox()
        {
            try
            {
                SkyboxImageConverter.Remove();
            }
            catch (Exception ex)
            {
                Frontend.ShowMessageBox("The custom skybox could not be removed:\n" + ex.Message, MessageBoxImage.Warning);
            }

            OnPropertyChanged(nameof(HasCustomSkybox));
        }

        public ICommand ChooseSkyboxFaceCommand => new RelayCommand<object>(ChooseSkyboxFace);

        public ICommand ChooseSingleSkyboxImageCommand => new RelayCommand(ChooseSingleSkyboxImage);

        public ICommand ApplyCustomSkyboxCommand => new RelayCommand(ApplyCustomSkybox);

        public ICommand RemoveCustomSkyboxCommand => new RelayCommand(RemoveCustomSkybox);

        #endregion

        #region Preset Mod - custom death sound

        public CustomDeathSoundModPresetTask CustomDeathSoundTask { get; } = new();

        public Visibility ChooseCustomDeathSoundVisibility => String.IsNullOrEmpty(CustomDeathSoundTask.NewState) ? Visibility.Visible : Visibility.Collapsed;

        public Visibility DeleteCustomDeathSoundVisibility => String.IsNullOrEmpty(CustomDeathSoundTask.NewState) ? Visibility.Collapsed : Visibility.Visible;

        private void AddCustomDeathSound()
        {
            var dialog = new OpenFileDialog { Filter = "Audio files|*.ogg;*.mp3;*.wav" };

            if (dialog.ShowDialog() != true)
                return;

            CustomDeathSoundTask.NewState = dialog.FileName;

            OnPropertyChanged(nameof(ChooseCustomDeathSoundVisibility));
            OnPropertyChanged(nameof(DeleteCustomDeathSoundVisibility));
        }

        private void RemoveCustomDeathSound()
        {
            CustomDeathSoundTask.NewState = "";

            OnPropertyChanged(nameof(ChooseCustomDeathSoundVisibility));
            OnPropertyChanged(nameof(DeleteCustomDeathSoundVisibility));
        }

        public ICommand AddCustomDeathSoundCommand => new RelayCommand(AddCustomDeathSound);

        public ICommand RemoveCustomDeathSoundCommand => new RelayCommand(RemoveCustomDeathSound);

        #endregion

        #region Preset Mod - custom cursor set manager (named library)

        public ObservableCollection<CursorSetItem> CursorSets { get; } = new();

        private CursorSetItem? _selectedCursorSet;
        public CursorSetItem? SelectedCursorSet
        {
            get => _selectedCursorSet;
            set
            {
                _selectedCursorSet = value;
                OnPropertyChanged(nameof(SelectedCursorSet));
                OnPropertyChanged(nameof(IsCustomCursorSetSelected));
                OnPropertyChanged(nameof(NoCursorSetSelected));
            }
        }

        public bool IsCustomCursorSetSelected => SelectedCursorSet is not null;

        public bool NoCursorSetSelected => SelectedCursorSet is null;

        private void LoadCursorSets()
        {
            string? selectedId = SelectedCursorSet?.Id;

            CursorSets.Clear();
            foreach (var record in CursorSetStore.Load())
                CursorSets.Add(new CursorSetItem(record));

            SelectedCursorSet = selectedId is null ? null : CursorSets.FirstOrDefault(item => item.Id == selectedId);
        }

        private void AddCustomCursorSet()
        {
            var dialog = new TextInputDialog("New Cursor Set", "Name for this cursor set:", "Custom Cursor Set " + (CursorSets.Count + 1));
            dialog.ShowDialog();

            if (!dialog.Confirmed)
                return;

            try
            {
                CursorSetStore.Create(dialog.Value);
                LoadCursorSets();
                SelectedCursorSet = CursorSets.LastOrDefault();
            }
            catch (Exception ex)
            {
                Frontend.ShowMessageBox("The cursor set could not be created:\n" + ex.Message, MessageBoxImage.Warning);
            }
        }

        private void RenameCustomCursorSet()
        {
            if (SelectedCursorSet is null)
                return;

            var dialog = new TextInputDialog("Rename Cursor Set", "New name:", SelectedCursorSet.Name);
            dialog.ShowDialog();

            if (!dialog.Confirmed)
                return;

            try
            {
                CursorSetStore.Rename(SelectedCursorSet.Id, dialog.Value);
                LoadCursorSets();
            }
            catch (Exception ex)
            {
                Frontend.ShowMessageBox("The cursor set could not be renamed:\n" + ex.Message, MessageBoxImage.Warning);
            }
        }

        private void DeleteCustomCursorSet()
        {
            if (SelectedCursorSet is null)
                return;

            if (Frontend.ShowMessageBox($"Delete \"{SelectedCursorSet.Name}\"?", MessageBoxImage.Warning, MessageBoxButton.YesNo, MessageBoxResult.No) != MessageBoxResult.Yes)
                return;

            try
            {
                CursorSetStore.Delete(SelectedCursorSet.Id);
                SelectedCursorSet = null;
                LoadCursorSets();
            }
            catch (Exception ex)
            {
                Frontend.ShowMessageBox("The cursor set could not be deleted:\n" + ex.Message, MessageBoxImage.Warning);
            }
        }

        private void ApplyCursorSet()
        {
            if (SelectedCursorSet is null)
                return;

            try
            {
                CursorSetStore.Apply(SelectedCursorSet.Id);

                if (!String.IsNullOrEmpty(CustomCursorSetTask.NewState))
                    CustomCursorSetTask.NewState = "";
                if (!CursorTypeTask.NewState.Equals(default(Enums.CursorType)))
                    CursorTypeTask.NewState = default;

                OnPropertyChanged(nameof(CustomCursorSetFolderDisplay));
                OnPropertyChanged(nameof(ChooseCustomCursorSetVisibility));
                OnPropertyChanged(nameof(DeleteCustomCursorSetVisibility));

                Frontend.ShowMessageBox($"\"{SelectedCursorSet.Name}\" has been applied.", MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                Frontend.ShowMessageBox("The cursor set could not be applied:\n" + ex.Message, MessageBoxImage.Warning);
            }
        }

        private void GetCurrentCursorSet()
        {
            if (SelectedCursorSet is null)
                return;

            try
            {
                int copied = CursorSetStore.FetchFromCurrent(SelectedCursorSet.Id);
                SelectedCursorSet.RefreshPreviews();

                if (copied == 0)
                    Frontend.ShowMessageBox("No applied cursor files were found to copy.", MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                Frontend.ShowMessageBox("The current cursors could not be fetched:\n" + ex.Message, MessageBoxImage.Warning);
            }
        }

        private void ImportCursorSet()
        {
            var dialog = new OpenFileDialog { Filter = $"{Strings.FileTypes_ZipArchive}|*.zip" };

            if (dialog.ShowDialog() != true)
                return;

            try
            {
                var record = CursorSetStore.Import(dialog.FileName, Path.GetFileNameWithoutExtension(dialog.FileName));
                LoadCursorSets();
                SelectedCursorSet = CursorSets.FirstOrDefault(item => item.Id == record.Id);
            }
            catch (Exception ex)
            {
                Frontend.ShowMessageBox("The cursor set could not be imported:\n" + ex.Message, MessageBoxImage.Warning);
            }
        }

        private void ExportCursorSet()
        {
            if (SelectedCursorSet is null)
                return;

            var dialog = new SaveFileDialog
            {
                Filter = $"{Strings.FileTypes_ZipArchive}|*.zip",
                FileName = SelectedCursorSet.Name + ".zip"
            };

            if (dialog.ShowDialog() != true)
                return;

            try
            {
                CursorSetStore.Export(SelectedCursorSet.Id, dialog.FileName);
                Frontend.ShowMessageBox("Exported to " + dialog.FileName, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                Frontend.ShowMessageBox("The cursor set could not be exported:\n" + ex.Message, MessageBoxImage.Warning);
            }
        }

        private void BrowseCursorSetFile(object? parameter)
        {
            if (SelectedCursorSet is null || parameter is not string fileName)
                return;

            var dialog = new OpenFileDialog { Filter = CursorImages.PickerFilter };

            if (dialog.ShowDialog() != true)
                return;

            try
            {
                Directory.CreateDirectory(SelectedCursorSet.Folder);

                foreach (string extension in CursorImages.Extensions)
                {
                    string existing = Path.Combine(SelectedCursorSet.Folder, Path.GetFileNameWithoutExtension(fileName) + extension);

                    if (File.Exists(existing))
                        File.Delete(existing);
                }

                CursorImages.WritePng(dialog.FileName, Path.Combine(SelectedCursorSet.Folder, fileName));
                SelectedCursorSet.RefreshPreviews();
            }
            catch (Exception ex)
            {
                Frontend.ShowMessageBox("That image could not be added:\n" + ex.Message, MessageBoxImage.Warning);
            }
        }

        private void RemoveCursorSetFile(object? parameter)
        {
            if (SelectedCursorSet is null || parameter is not string fileName)
                return;

            string path = Path.Combine(SelectedCursorSet.Folder, fileName);

            if (File.Exists(path))
                File.Delete(path);

            SelectedCursorSet.RefreshPreviews();
        }

        public ICommand AddCustomCursorSetCommand => new RelayCommand(AddCustomCursorSet);
        public ICommand RenameCustomCursorSetCommand => new RelayCommand(RenameCustomCursorSet);
        public ICommand DeleteCustomCursorSetCommand => new RelayCommand(DeleteCustomCursorSet);
        public ICommand ApplyCursorSetCommand => new RelayCommand(ApplyCursorSet);
        public ICommand GetCurrentCursorSetCommand => new RelayCommand(GetCurrentCursorSet);
        public ICommand ImportCursorSetCommand => new RelayCommand(ImportCursorSet);
        public ICommand ExportCursorSetCommand => new RelayCommand(ExportCursorSet);
        public ICommand BrowseCursorSetFileCommand => new RelayCommand<object>(BrowseCursorSetFile);
        public ICommand RemoveCursorSetFileCommand => new RelayCommand<object>(RemoveCursorSetFile);

        #endregion

        #region Mod Management

        private readonly List<ManagedModItem> _allManagedMods = new();

        public ObservableCollection<ManagedModItem> ManagedMods { get; } = new();

        private bool _managedModsBusy;
        public bool ManagedModsBusy
        {
            get => _managedModsBusy;
            set { _managedModsBusy = value; OnPropertyChanged(nameof(ManagedModsBusy)); }
        }

        private string _managedModsSummary = "No managed mods";
        public string ManagedModsSummary
        {
            get => _managedModsSummary;
            set { _managedModsSummary = value; OnPropertyChanged(nameof(ManagedModsSummary)); }
        }

        private string _managedModsEmptyTitle = "Your managed mod library is empty";
        public string ManagedModsEmptyTitle
        {
            get => _managedModsEmptyTitle;
            set { _managedModsEmptyTitle = value; OnPropertyChanged(nameof(ManagedModsEmptyTitle)); }
        }

        private string _managedModsEmptyDescription = "Add a mod to create its indexed folder, then place its files inside.";
        public string ManagedModsEmptyDescription
        {
            get => _managedModsEmptyDescription;
            set { _managedModsEmptyDescription = value; OnPropertyChanged(nameof(ManagedModsEmptyDescription)); }
        }

        private string _managedModSearchText = "";
        public string ManagedModSearchText
        {
            get => _managedModSearchText;
            set
            {
                _managedModSearchText = value;
                OnPropertyChanged(nameof(ManagedModSearchText));
                ApplyManagedModFilter();
            }
        }

        public async Task LoadManagedModsAsync()
        {
            ManagedModsBusy = true;
            try
            {
                ManagedModItem[] items = await Task.Run(() =>
                {
                    IReadOnlyList<ManagedModRecord> records = ManagedModStore.Load();
                    ManagedModScanResult scan = ManagedModStore.ScanEnabledFiles();
                    Dictionary<string, int> pathCounts = new(StringComparer.OrdinalIgnoreCase);
                    Dictionary<string, HashSet<string>> pathsByMod = new(StringComparer.OrdinalIgnoreCase);

                    try
                    {
                        if (Directory.Exists(Paths.Modifications))
                        {
                            foreach (string file in Directory.EnumerateFiles(Paths.Modifications, "*", SearchOption.AllDirectories))
                            {
                                string relative = Path.GetRelativePath(Paths.Modifications, file);
                                if (!relative.EndsWith(".lock", StringComparison.OrdinalIgnoreCase))
                                    pathCounts[relative] = pathCounts.GetValueOrDefault(relative) + 1;
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        App.Logger.WriteLine("ModsViewModel::LoadManagedMods", "Could not compare the standard mod folder: " + ex.Message);
                    }

                    foreach (ManagedModFile file in scan.Files)
                    {
                        if (!pathsByMod.TryGetValue(file.Mod.Id, out HashSet<string>? paths))
                        {
                            paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                            pathsByMod[file.Mod.Id] = paths;
                        }
                        paths.Add(file.Relative);
                    }

                    Dictionary<string, int> managedClaimCounts = new(StringComparer.OrdinalIgnoreCase);
                    foreach (var paths in pathsByMod.Values)
                        foreach (string path in paths)
                            managedClaimCounts[path] = managedClaimCounts.GetValueOrDefault(path) + 1;

                    return records.Select(record =>
                    {
                        string scanError = scan.Failures.GetValueOrDefault(record.Id) ?? string.Empty;
                        ManagedModStatistics statistics;
                        try
                        {
                            statistics = ManagedModStore.GetStatistics(record.Id);
                        }
                        catch (Exception ex)
                        {
                            App.Logger.WriteLine("ModsViewModel::LoadManagedMods", "Could not inspect " + record.Name + ": " + ex.Message);
                            scanError = ex.Message;
                            statistics = new ManagedModStatistics(0, 0);
                        }

                        int conflicts = 0;
                        if (pathsByMod.TryGetValue(record.Id, out HashSet<string>? ownPaths))
                        {
                            conflicts = ownPaths.Count(path =>
                                managedClaimCounts.GetValueOrDefault(path) > 1 ||
                                pathCounts.GetValueOrDefault(path) > managedClaimCounts.GetValueOrDefault(path));
                        }

                        return new ManagedModItem(record.Id, record.Name, record.Enabled, record.CreatedUtc, statistics.FileCount, statistics.TotalBytes, conflicts, scanError);
                    }).ToArray();
                });

                _allManagedMods.Clear();
                _allManagedMods.AddRange(items);
                ApplyManagedModFilter();

                int enabled = items.Count(item => item.Enabled);
                int files = items.Sum(item => item.FileCount);
                ManagedModsSummary = items.Length == 0 ? "No managed mods" : $"{items.Length} mods, {enabled} enabled, {files} files";
            }
            catch (Exception ex)
            {
                App.Logger.WriteException("ModsViewModel::LoadManagedMods", ex);
                Frontend.ShowMessageBox("The managed mod library could not be loaded:\n" + ex.Message, MessageBoxImage.Warning);
            }
            finally
            {
                ManagedModsBusy = false;
            }
        }

        private void ApplyManagedModFilter()
        {
            string query = ManagedModSearchText.Trim();
            IEnumerable<ManagedModItem> filtered = string.IsNullOrEmpty(query)
                ? _allManagedMods
                : _allManagedMods.Where(item => item.Name.Contains(query, StringComparison.OrdinalIgnoreCase) || item.Id.Contains(query, StringComparison.OrdinalIgnoreCase));

            ManagedMods.Clear();
            foreach (ManagedModItem item in filtered)
                ManagedMods.Add(item);

            bool searchHasNoResults = ManagedMods.Count == 0 && _allManagedMods.Count > 0 && !string.IsNullOrEmpty(query);
            ManagedModsEmptyTitle = searchHasNoResults ? "No managed mods match your search" : "Your managed mod library is empty";
            ManagedModsEmptyDescription = searchHasNoResults ? "Try a different name or identifier." : "Add a mod to create its indexed folder, then place its files inside.";
        }

        private static string? AskForManagedModName(string title, string initial)
        {
            var dialog = new TextInputDialog(title, "Mod name:", initial);
            dialog.ShowDialog();
            return dialog.Confirmed ? dialog.Value : null;
        }

        private async Task AddManagedModAsync()
        {
            string? name = AskForManagedModName("Add Mod", "New Mod");
            if (name is null)
                return;

            ManagedModRecord? record = null;
            try
            {
                record = await Task.Run(() => ManagedModStore.Create(name));
                await LoadManagedModsAsync();
            }
            catch (Exception ex)
            {
                Frontend.ShowMessageBox("The mod could not be added:\n" + ex.Message, MessageBoxImage.Warning);
            }

            if (record is not null)
                OpenManagedFolder(ManagedModStore.GetFolder(record.Id));
        }

        private async Task RenameManagedModAsync(ManagedModItem? item)
        {
            if (item is null)
                return;

            string? name = AskForManagedModName("Rename Mod", item.Name);
            if (name is null)
                return;

            try
            {
                await Task.Run(() => ManagedModStore.Rename(item.Id, name));
                await LoadManagedModsAsync();
            }
            catch (Exception ex)
            {
                Frontend.ShowMessageBox("The mod could not be renamed:\n" + ex.Message, MessageBoxImage.Warning);
            }
        }

        private async Task RemoveManagedModAsync(ManagedModItem? item)
        {
            if (item is null || Frontend.ShowMessageBox($"Remove \"{item.Name}\" and all files in its managed folder?", MessageBoxImage.Warning, MessageBoxButton.YesNo, MessageBoxResult.No) != MessageBoxResult.Yes)
                return;

            try
            {
                await Task.Run(() => ManagedModStore.Delete(item.Id));
                await LoadManagedModsAsync();
            }
            catch (Exception ex)
            {
                Frontend.ShowMessageBox("The mod could not be removed:\n" + ex.Message, MessageBoxImage.Warning);
            }
        }

        public async Task ReorderManagedModAsync(ManagedModItem source, ManagedModItem target, bool insertAfter)
        {
            if (source.Id == target.Id)
                return;

            try
            {
                await Task.Run(() => ManagedModStore.MoveRelative(source.Id, target.Id, insertAfter));
                await LoadManagedModsAsync();
            }
            catch (Exception ex)
            {
                Frontend.ShowMessageBox("The mod order could not be changed:\n" + ex.Message, MessageBoxImage.Warning);
            }
        }

        private async Task ToggleManagedModAsync(ManagedModItem? item)
        {
            if (item is null)
                return;

            try
            {
                await Task.Run(() => ManagedModStore.SetEnabled(item.Id, !item.Enabled));
                await LoadManagedModsAsync();
            }
            catch (Exception ex)
            {
                Frontend.ShowMessageBox("The mod state could not be saved:\n" + ex.Message, MessageBoxImage.Warning);
            }
        }

        private static void OpenManagedFolder(string folder)
        {
            Directory.CreateDirectory(folder);
            Process.Start("explorer.exe", folder);
        }

        private static void OpenManagedModsRoot()
        {
            try
            {
                OpenManagedFolder(Paths.ManagedModPackages);
            }
            catch (Exception ex)
            {
                Frontend.ShowMessageBox("The managed mod library could not be opened:\n" + ex.Message, MessageBoxImage.Warning);
            }
        }

        private static void OpenManagedMod(ManagedModItem? item)
        {
            if (item is null)
                return;

            try
            {
                OpenManagedFolder(ManagedModStore.GetFolder(item.Id));
            }
            catch (Exception ex)
            {
                Frontend.ShowMessageBox("That mod's folder could not be opened:\n" + ex.Message, MessageBoxImage.Warning);
            }
        }

        private static void CopyManagedModId(ManagedModItem? item)
        {
            if (item is null)
                return;

            try
            {
                Clipboard.SetText(item.Id);
            }
            catch
            {
            }
        }

        public ICommand AddManagedModCommand => new AsyncRelayCommand(AddManagedModAsync);
        public ICommand RefreshManagedModsCommand => new AsyncRelayCommand(LoadManagedModsAsync);
        public ICommand OpenManagedModsRootCommand => new RelayCommand(OpenManagedModsRoot);
        public ICommand ToggleManagedModCommand => new AsyncRelayCommand<ManagedModItem>(ToggleManagedModAsync);
        public ICommand RenameManagedModCommand => new AsyncRelayCommand<ManagedModItem>(RenameManagedModAsync);
        public ICommand RemoveManagedModCommand => new AsyncRelayCommand<ManagedModItem>(RemoveManagedModAsync);
        public ICommand OpenManagedModCommand => new RelayCommand<ManagedModItem>(OpenManagedMod);
        public ICommand CopyManagedModIdCommand => new RelayCommand<ManagedModItem>(CopyManagedModId);

        #endregion

        #region Overlays - homepage background

        public bool TopBarPhasmaLogo
        {
            get => App.Settings.Prop.TopBarPhasmaLogo;
            set { App.Settings.Prop.TopBarPhasmaLogo = value; OnPropertyChanged(nameof(TopBarPhasmaLogo)); }
        }

        public bool HomepageBackgroundEnabled
        {
            get => App.Settings.Prop.HomepageBackgroundEnabled;
            set { App.Settings.Prop.HomepageBackgroundEnabled = value; OnPropertyChanged(nameof(HomepageBackgroundEnabled)); }
        }

        public IEnumerable<Enums.HomepageBackgroundMode> HomepageBackgroundModes { get; } = Enum.GetValues(typeof(Enums.HomepageBackgroundMode)).Cast<Enums.HomepageBackgroundMode>();

        public Enums.HomepageBackgroundMode SelectedHomepageBackgroundMode
        {
            get => App.Settings.Prop.HomepageBackgroundMode;
            set
            {
                App.Settings.Prop.HomepageBackgroundMode = value;
                OnPropertyChanged(nameof(SelectedHomepageBackgroundMode));
                OnPropertyChanged(nameof(ShowHomepageSolidColor));
                OnPropertyChanged(nameof(ShowHomepageGradient));
                OnPropertyChanged(nameof(PreviewHomepageBackgroundEnabled));
            }
        }

        public bool ShowHomepageSolidColor => SelectedHomepageBackgroundMode == Enums.HomepageBackgroundMode.Solid;

        public bool ShowHomepageGradient => SelectedHomepageBackgroundMode == Enums.HomepageBackgroundMode.Gradient;

        public bool PreviewHomepageBackgroundEnabled => SelectedHomepageBackgroundMode != Enums.HomepageBackgroundMode.None;

        public string HomepageBackgroundOverlayColor
        {
            get => App.Settings.Prop.HomepageBackgroundColor;
            set { App.Settings.Prop.HomepageBackgroundColor = value; OnPropertyChanged(nameof(HomepageBackgroundOverlayColor)); }
        }

        public string HomepageBackgroundOverlayGradientColor
        {
            get => App.Settings.Prop.HomepageBackgroundGradientColor;
            set { App.Settings.Prop.HomepageBackgroundGradientColor = value; OnPropertyChanged(nameof(HomepageBackgroundOverlayGradientColor)); }
        }

        public double HomepageBackgroundOverlayGradientAngle
        {
            get => App.Settings.Prop.HomepageBackgroundGradientAngle;
            set
            {
                App.Settings.Prop.HomepageBackgroundGradientAngle = value;
                OnPropertyChanged(nameof(HomepageBackgroundOverlayGradientAngle));
                OnPropertyChanged(nameof(HomepageBackgroundOverlayGradientAngleDisplay));
            }
        }

        public string HomepageBackgroundOverlayGradientAngleDisplay => $"{HomepageBackgroundOverlayGradientAngle:0}°";

        private void PickHomepageBackgroundColor()
        {
            var current = HomepageBackgroundRenderer.ParseColorOrDefault(HomepageBackgroundOverlayColor, Colors.Black);

            using var dialog = new System.Windows.Forms.ColorDialog
            {
                Color = System.Drawing.Color.FromArgb(current.A, current.R, current.G, current.B),
                FullOpen = true
            };

            if (dialog.ShowDialog() != System.Windows.Forms.DialogResult.OK)
                return;

            HomepageBackgroundOverlayColor = $"#{dialog.Color.R:X2}{dialog.Color.G:X2}{dialog.Color.B:X2}";
        }

        private void PickHomepageBackgroundGradientColor()
        {
            var current = HomepageBackgroundRenderer.ParseColorOrDefault(HomepageBackgroundOverlayGradientColor, Colors.Black);

            using var dialog = new System.Windows.Forms.ColorDialog
            {
                Color = System.Drawing.Color.FromArgb(current.A, current.R, current.G, current.B),
                FullOpen = true
            };

            if (dialog.ShowDialog() != System.Windows.Forms.DialogResult.OK)
                return;

            HomepageBackgroundOverlayGradientColor = $"#{dialog.Color.R:X2}{dialog.Color.G:X2}{dialog.Color.B:X2}";
        }

        private void PreviewHomepageBackground()
        {
            var solid = HomepageBackgroundRenderer.ParseColorOrDefault(HomepageBackgroundOverlayColor, Colors.Black);
            var gradient = HomepageBackgroundRenderer.ParseColorOrDefault(HomepageBackgroundOverlayGradientColor, Colors.DarkBlue);

            new HomepageBackgroundPreviewWindow(SelectedHomepageBackgroundMode, solid, gradient, HomepageBackgroundOverlayGradientAngle).Show();
        }

        public ICommand PickHomepageBackgroundColorCommand => new RelayCommand(PickHomepageBackgroundColor);
        public ICommand PickHomepageBackgroundGradientColorCommand => new RelayCommand(PickHomepageBackgroundGradientColor);
        public ICommand PreviewHomepageBackgroundCommand => new RelayCommand(PreviewHomepageBackground);

        #endregion
    }
}
