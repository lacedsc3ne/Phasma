using System.Windows;
using System.Windows.Input;

using Microsoft.Win32;

using Windows.Win32;
using Windows.Win32.UI.Shell;
using Windows.Win32.Foundation;

using CommunityToolkit.Mvvm.Input;

using PhasmaStrap.Models.SettingTasks;
using PhasmaStrap.AppData;
using PhasmaStrap.UI.Elements.Dialogs;

namespace PhasmaStrap.UI.ViewModels.Settings
{
    public class ModsViewModel : NotifyPropertyChangedViewModel
    {
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
                using var dialog = new System.Windows.Forms.FolderBrowserDialog
                {
                    Description = "Select a folder containing your custom cursor images (ArrowCursor.png, ArrowFarCursor.png, IBeamCursor.png, MouseLockedCursor.png)."
                };

                if (dialog.ShowDialog() != System.Windows.Forms.DialogResult.OK)
                    return;

                bool foundAny = CustomCursorModPresetTask.RecognizedFileNames
                    .Any(fileName => File.Exists(Path.Combine(dialog.SelectedPath, fileName)));

                if (!foundAny)
                {
                    Frontend.ShowMessageBox(
                        "The selected folder doesn't contain any recognized cursor images (ArrowCursor.png, ArrowFarCursor.png, IBeamCursor.png, MouseLockedCursor.png).",
                        MessageBoxImage.Error);
                    return;
                }

                CustomCursorSetTask.NewState = dialog.SelectedPath;

                // a custom folder and the bundled preset both write the same target files -
                // applying one should take precedence over the other, so clear the bundled pick
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
    }
}
