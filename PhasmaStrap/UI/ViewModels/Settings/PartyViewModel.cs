using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;

using CommunityToolkit.Mvvm.Input;

using PhasmaStrap.Utility;

namespace PhasmaStrap.UI.ViewModels.Settings
{
    public sealed class PartyMemberRow
    {
        public string Name { get; init; } = "";

        public string Role { get; init; } = "";
    }

    public sealed class PartyInviteRow
    {
        public string Id { get; init; } = "";

        public string Code { get; init; } = "";

        public string Summary { get; init; } = "";
    }

    public class PartyViewModel : NotifyPropertyChangedViewModel
    {
        public PartyViewModel()
        {
            PartyService.Changed += OnPartyChanged;
        }

        public bool PartyEnabled
        {
            get => App.Settings.Prop.PartyEnabled;
            set
            {
                App.Settings.Prop.PartyEnabled = value;
                App.Settings.Save();

                PartyBackground.ApplyStartup();

                if (value)
                    PartyBackground.StartIfWanted();

                OnPropertyChanged(nameof(PartyEnabled));
                OnPropertyChanged(nameof(Status));
            }
        }

        public bool PartyBackgroundEnabled
        {
            get => App.Settings.Prop.PartyBackgroundEnabled;
            set
            {
                App.Settings.Prop.PartyBackgroundEnabled = value;
                App.Settings.Save();

                PartyBackground.ApplyStartup();

                if (value)
                    PartyBackground.StartIfWanted();

                OnPropertyChanged(nameof(PartyBackgroundEnabled));
            }
        }

        public string[] JoinModeOptions { get; } = { "Join straight away", "Ask if I am already in a game", "Always ask first" };

        private static readonly string[] JoinModeKeys = { "Always", "AskWhenInGame", "AskAlways" };

        public int JoinModeIndex
        {
            get
            {
                int index = Array.IndexOf(JoinModeKeys, App.Settings.Prop.PartyJoinMode);
                return index < 0 ? 1 : index;
            }
            set
            {
                if (value < 0 || value >= JoinModeKeys.Length)
                    return;

                App.Settings.Prop.PartyJoinMode = JoinModeKeys[value];
                App.Settings.Save();
                OnPropertyChanged(nameof(JoinModeIndex));
            }
        }

        public bool SignedIn => PhasmaAccount.SignedIn;

        public Visibility SignedOutVisibility => PhasmaAccount.SignedIn ? Visibility.Collapsed : Visibility.Visible;

        public Visibility InPartyVisibility => PartyService.InParty ? Visibility.Visible : Visibility.Collapsed;

        public Visibility NoPartyVisibility => PhasmaAccount.SignedIn && !PartyService.InParty ? Visibility.Visible : Visibility.Collapsed;

        public string Code => PartyService.Current.Code;

        public string Status
        {
            get
            {
                if (!PhasmaAccount.SignedIn)
                    return "Sign in to your PhasmaStrap account on the PhasmaStrap page to use parties.";

                if (!App.Settings.Prop.PartyEnabled)
                    return "Parties are turned off.";

                if (!PartyService.InParty)
                    return "You are not in a party.";

                return PartyService.IsLeader
                    ? $"You are the leader. Anyone in the party follows you into a game. Share the code {PartyService.Current.Code}."
                    : $"{PartyService.Current.LeaderName} is leading. You follow them into whatever they launch.";
            }
        }

        public ObservableCollection<PartyMemberRow> Members { get; } = new();

        public ObservableCollection<PartyInviteRow> Invites { get; } = new();

        public Visibility InvitesVisibility => Invites.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

        private string _joinCode = "";

        public string JoinCode
        {
            get => _joinCode;
            set
            {
                _joinCode = (value ?? "").Trim().ToUpperInvariant();
                OnPropertyChanged(nameof(JoinCode));
            }
        }

        private string _message = "";

        public string Message
        {
            get => _message;
            private set
            {
                _message = value;
                OnPropertyChanged(nameof(Message));
            }
        }

        public ICommand StartPartyCommand => new AsyncRelayCommand(async () =>
        {
            Message = "";
            await PartyService.CreateAsync();
        });

        public ICommand JoinPartyCommand => new AsyncRelayCommand(async () =>
        {
            if (JoinCode.Length == 0)
            {
                Message = "Type the code someone gave you first.";
                return;
            }

            Message = "";

            if (!await PartyService.JoinAsync(JoinCode))
                Message = "That code did not work. Check it and try again.";
            else
                JoinCode = "";
        });

        public ICommand LeavePartyCommand => new AsyncRelayCommand(async () => await PartyService.LeaveAsync());

        public ICommand CopyCodeCommand => new RelayCommand(() =>
        {
            try
            {
                System.Windows.Clipboard.SetText(PartyService.Current.Code);
                Message = "Code copied.";
            }
            catch (Exception ex)
            {
                Message = "Could not copy the code: " + ex.Message;
            }
        });

        public ICommand AcceptInviteCommand => new AsyncRelayCommand<PartyInviteRow?>(async row =>
        {
            if (row is null)
                return;

            await PartyService.JoinAsync(row.Code);
        });

        public ICommand DeclineInviteCommand => new AsyncRelayCommand<PartyInviteRow?>(async row =>
        {
            if (row is null)
                return;

            await PartyService.DeclineInviteAsync(row.Id);
        });

        private void OnPartyChanged(object? sender, EventArgs e)
        {
            System.Windows.Application.Current?.Dispatcher.BeginInvoke(new Action(Refresh));
        }

        public void Refresh()
        {
            Members.Clear();

            foreach (PartyMember member in PartyService.Current.Members)
                Members.Add(new PartyMemberRow { Name = member.Name, Role = member.Leader ? "Leader" : "Member" });

            Invites.Clear();

            foreach (PartyInvite invite in PartyService.Current.Invites)
                Invites.Add(new PartyInviteRow { Id = invite.Id, Code = invite.Code, Summary = $"{invite.From} invited you to a party of {invite.Members}." });

            OnPropertyChanged(string.Empty);
        }

        public void Attach()
        {
            PartyService.Changed -= OnPartyChanged;
            PartyService.Changed += OnPartyChanged;

            if (App.Settings.Prop.PartyEnabled)
                PartyService.Start();

            Refresh();
        }

        public void Detach()
        {
            PartyService.Changed -= OnPartyChanged;
        }
    }
}
