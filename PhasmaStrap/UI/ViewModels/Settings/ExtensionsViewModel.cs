using System.Collections.ObjectModel;
using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using PhasmaStrap.Models;

namespace PhasmaStrap.UI.ViewModels.Settings
{
    public class ExtensionEntry : NotifyPropertyChangedViewModel
    {
        public Extension Extension { get; }

        public ExtensionEntry(Extension extension) => Extension = extension;

        public string DisplayName => Extension.DisplayName;
        public string Description => Extension.Description;

        public bool IsInstalled => ExtensionManager.IsInstalled(Extension.Id);

        public string StatusText => IsInstalled
            ? ExtensionManager.GetSavedPath(Extension.Id)!
            : "Not located - browse to the executable to enable this";

        public void Refresh()
        {
            OnPropertyChanged(nameof(IsInstalled));
            OnPropertyChanged(nameof(StatusText));
        }
    }

    public class ExtensionsViewModel : NotifyPropertyChangedViewModel
    {
        public ObservableCollection<ExtensionEntry> Extensions { get; } =
            new(ExtensionManager.KnownExtensions.Select(x => new ExtensionEntry(x)));

        public ICommand BrowseCommand => new RelayCommand<ExtensionEntry>(Browse);
        public ICommand LaunchCommand => new RelayCommand<ExtensionEntry>(entry => ExtensionManager.Launch(entry!.Extension.Id));
        public ICommand ClearCommand => new RelayCommand<ExtensionEntry>(Clear);

        private void Browse(ExtensionEntry? entry)
        {
            if (entry is null)
                return;

            var dialog = new OpenFileDialog
            {
                Title = $"Locate {entry.DisplayName}",
                Filter = $"{entry.Extension.ExecutableName}|{entry.Extension.ExecutableName}|Executable files (*.exe)|*.exe"
            };

            if (dialog.ShowDialog() == true)
            {
                ExtensionManager.SetSavedPath(entry.Extension.Id, dialog.FileName);
                App.Settings.Save();
                entry.Refresh();
            }
        }

        private void Clear(ExtensionEntry? entry)
        {
            if (entry is null)
                return;

            ExtensionManager.ClearSavedPath(entry.Extension.Id);
            App.Settings.Save();
            entry.Refresh();
        }
    }
}
