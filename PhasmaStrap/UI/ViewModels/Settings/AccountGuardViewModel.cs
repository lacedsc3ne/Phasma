using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;

using CommunityToolkit.Mvvm.Input;

using PhasmaStrap.Utility;

namespace PhasmaStrap.UI.ViewModels.Settings
{
    /// <summary>
    /// Accounts page > Account guard. The toggles apply when Save is pressed (that's also when the
    /// background watcher is started or its start-up entry removed).
    /// </summary>
    public sealed class AccountGuardViewModel : NotifyPropertyChangedViewModel
    {
        public ObservableCollection<GuardFinding> Findings { get; } = new();

        public ObservableCollection<GuardAlert> History { get; } = new();

        public AccountGuardViewModel()
        {
            LoadHistory();
        }

        public bool Enabled
        {
            get => App.Settings.Prop.AccountGuardEnabled;
            set
            {
                App.Settings.Prop.AccountGuardEnabled = value;
                OnPropertyChanged(nameof(Enabled));
            }
        }

        public bool Background
        {
            get => App.Settings.Prop.AccountGuardBackground;
            set { App.Settings.Prop.AccountGuardBackground = value; OnPropertyChanged(nameof(Background)); }
        }

        private string _status = "";
        public string Status { get => _status; private set { _status = value; OnPropertyChanged(nameof(Status)); } }

        public Visibility HistoryVisibility => History.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

        private void LoadHistory()
        {
            History.Clear();
            foreach (GuardAlert alert in AccountGuard.Load().History.OrderByDescending(a => a.Utc).Take(10))
                History.Add(alert);
            OnPropertyChanged(nameof(HistoryVisibility));
        }

        public ICommand CheckNowCommand => new AsyncRelayCommand(async () =>
        {
            Status = "Checking...";
            List<GuardFinding> findings = await Task.Run(AccountGuard.Scan);

            Findings.Clear();
            foreach (GuardFinding finding in findings)
                Findings.Add(finding);

            Status = findings.Count == 0
                ? $"Nothing wrong found ({DateTime.Now:t}): no Roblox address redirected, no interception certificates, Roblox's certificate list untouched."
                : $"{findings.Count} thing(s) to look at:";

            LoadHistory();
        });

        public ICommand AcceptCommand => new RelayCommand<GuardFinding>(finding =>
        {
            if (finding is null)
                return;

            AccountGuard.Accept(finding);
            Findings.Remove(finding);
            Status = "Okay - that one won't be mentioned again.";
        });
    }
}
