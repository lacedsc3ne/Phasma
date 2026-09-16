using System.Collections.ObjectModel;
using System.Windows.Input;

using Microsoft.Win32;

using CommunityToolkit.Mvvm.Input;

using PhasmaStrap.UI.Elements.ContextMenu;
using PhasmaStrap.UI.Elements.Dialogs;

namespace PhasmaStrap.UI.ViewModels.Settings
{
    public class IntegrationsViewModel : NotifyPropertyChangedViewModel
    {
        public ICommand AddIntegrationCommand => new RelayCommand(AddIntegration);

        public ICommand DeleteIntegrationCommand => new RelayCommand(DeleteIntegration);

        public ICommand BrowseIntegrationLocationCommand => new RelayCommand(BrowseIntegrationLocation);

        public ICommand AddRPCTemplateCommand => new RelayCommand(AddRPCTemplate);

        public ICommand DeleteRPCTemplateCommand => new RelayCommand(DeleteRPCTemplate);

        // opens the Custom RPC template editor (list/detail editor for per-game Discord presence
        // templates) - moved out of the inline tab into its own window to match Voidstrap's "Custom
        // RPC" card + View button layout, without losing the multi-template editing this already had.
        public ICommand OpenCustomRPCCommand => new RelayCommand(() => new RPCTemplatesWindow(this).Show());

        // opens the standalone Roblox account switcher (Integrations > Roblox tab) - see
        // AccountSwitcherViewModel.cs for how switching an account actually works end to end
        public ICommand AccountWindowCommand => new RelayCommand(() => new AccountSwitcherWindow().Show());

        // Settings is always its own separate process from an active Roblox launch (see LaunchHandler.cs/
        // Watcher.cs - DiscordRichPresence only exists on a live Watcher instance, which Settings never
        // has access to), so there's no way to show this tab's preview card from real, currently-playing
        // data. Instead it cycles through a couple of static, clearly-illustrative examples of what a
        // Discord Rich Presence card looks like - same spirit as the Appearance page's theme previews.
        private static readonly (string Game, string Creator, string Elapsed)[] _previewSamples = new[]
        {
            ("Blade Ball", "Wiggity.", "00:39 elapsed"),
            ("Adopt Me!", "DreamCraft", "12:04 elapsed"),
            ("Brookhaven RP", "Wolfpaq", "03:21 elapsed"),
        };

        private int _previewIndex;

        public string PreviewGame => _previewSamples[_previewIndex].Game;
        public string PreviewCreator => _previewSamples[_previewIndex].Creator;
        public string PreviewElapsed => _previewSamples[_previewIndex].Elapsed;

        public ICommand CyclePreviewCommand => new RelayCommand(CyclePreview);

        private void CyclePreview()
        {
            _previewIndex = (_previewIndex + 1) % _previewSamples.Length;

            OnPropertyChanged(nameof(PreviewGame));
            OnPropertyChanged(nameof(PreviewCreator));
            OnPropertyChanged(nameof(PreviewElapsed));
        }

        private void AddIntegration()
        {
            CustomIntegrations.Add(new CustomIntegration()
            {
                Name = Strings.Menu_Integrations_Custom_NewIntegration
            });

            SelectedCustomIntegrationIndex = CustomIntegrations.Count - 1;

            OnPropertyChanged(nameof(SelectedCustomIntegrationIndex));
            OnPropertyChanged(nameof(IsCustomIntegrationSelected));
        }

        private void DeleteIntegration()
        {
            if (SelectedCustomIntegration is null)
                return;

            CustomIntegrations.Remove(SelectedCustomIntegration);

            if (CustomIntegrations.Count > 0)
            {
                SelectedCustomIntegrationIndex = CustomIntegrations.Count - 1;
                OnPropertyChanged(nameof(SelectedCustomIntegrationIndex));
            }

            OnPropertyChanged(nameof(IsCustomIntegrationSelected));
        }

        private void BrowseIntegrationLocation()
        {
            if (SelectedCustomIntegration is null)
                return;

            var dialog = new OpenFileDialog
            {
                Filter = $"{Strings.Menu_AllFiles}|*.*"
            };

            if (dialog.ShowDialog() != true)
                return;

            SelectedCustomIntegration.Name = dialog.SafeFileName;
            SelectedCustomIntegration.Location = dialog.FileName;
            OnPropertyChanged(nameof(SelectedCustomIntegration));
        }

        public bool ActivityTrackingEnabled
        {
            get => App.Settings.Prop.EnableActivityTracking;
            set
            {
                App.Settings.Prop.EnableActivityTracking = value;

                if (!value)
                {
                    ShowServerDetailsEnabled = value;
                    DisableAppPatchEnabled = value;
                    DiscordActivityEnabled = value;
                    DiscordActivityJoinEnabled = value;

                    OnPropertyChanged(nameof(ShowServerDetailsEnabled));
                    OnPropertyChanged(nameof(DisableAppPatchEnabled));
                    OnPropertyChanged(nameof(DiscordActivityEnabled));
                    OnPropertyChanged(nameof(DiscordActivityJoinEnabled));
                }
            }
        }

        public bool ShowServerDetailsEnabled
        {
            get => App.Settings.Prop.ShowServerDetails;
            set => App.Settings.Prop.ShowServerDetails = value;
        }

        public bool DiscordActivityEnabled
        {
            get => App.Settings.Prop.UseDiscordRichPresence;
            set
            {
                App.Settings.Prop.UseDiscordRichPresence = value;

                if (!value)
                {
                    DiscordActivityJoinEnabled = value;
                    DiscordAccountOnProfile = value;
                    OnPropertyChanged(nameof(DiscordActivityJoinEnabled));
                    OnPropertyChanged(nameof(DiscordAccountOnProfile));
                }
            }
        }

        public bool DiscordActivityJoinEnabled
        {
            get => !App.Settings.Prop.HideRPCButtons;
            set => App.Settings.Prop.HideRPCButtons = !value;
        }

        public bool DiscordAccountOnProfile
        {
            get => App.Settings.Prop.ShowAccountOnRichPresence;
            set => App.Settings.Prop.ShowAccountOnRichPresence = value;
        }

        public bool DisableAppPatchEnabled
        {
            get => App.Settings.Prop.UseDisableAppPatch;
            set => App.Settings.Prop.UseDisableAppPatch = value;
        }
        public ObservableCollection<CustomIntegration> CustomIntegrations
        {
            get => App.Settings.Prop.CustomIntegrations;
            set => App.Settings.Prop.CustomIntegrations = value;
        }

        public CustomIntegration? SelectedCustomIntegration { get; set; }
        public int SelectedCustomIntegrationIndex { get; set; }
        public bool IsCustomIntegrationSelected => SelectedCustomIntegration is not null;

        // user-authored Discord Rich Presence templates (per-game), ported/scoped-down from Voidstrap's
        // RPCCustomizer feature - see Models/RPCTemplate.cs and Integrations/DiscordRichPresence.cs
        public ObservableCollection<RPCTemplate> RPCTemplates
        {
            get => App.Settings.Prop.RPCTemplates;
            set => App.Settings.Prop.RPCTemplates = value;
        }

        public RPCTemplate? SelectedRPCTemplate { get; set; }
        public int SelectedRPCTemplateIndex { get; set; }
        public bool IsRPCTemplateSelected => SelectedRPCTemplate is not null;

        private void AddRPCTemplate()
        {
            RPCTemplates.Add(new RPCTemplate());

            SelectedRPCTemplateIndex = RPCTemplates.Count - 1;

            OnPropertyChanged(nameof(SelectedRPCTemplateIndex));
            OnPropertyChanged(nameof(IsRPCTemplateSelected));
        }

        private void DeleteRPCTemplate()
        {
            if (SelectedRPCTemplate is null)
                return;

            RPCTemplates.Remove(SelectedRPCTemplate);

            if (RPCTemplates.Count > 0)
            {
                SelectedRPCTemplateIndex = RPCTemplates.Count - 1;
                OnPropertyChanged(nameof(SelectedRPCTemplateIndex));
            }

            OnPropertyChanged(nameof(IsRPCTemplateSelected));
        }
    }
}
