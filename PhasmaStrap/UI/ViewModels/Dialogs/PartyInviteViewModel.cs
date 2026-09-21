using System.Collections.ObjectModel;
using System.Windows.Input;

using CommunityToolkit.Mvvm.Input;

using PhasmaStrap.Integrations;
using PhasmaStrap.Utility;

namespace PhasmaStrap.UI.ViewModels.Dialogs
{
    public sealed class PartyInviteRow : NotifyPropertyChangedViewModel
    {
        public long UserId { get; init; }

        public string Name { get; init; } = "";

        public string Detail { get; init; } = "";

        public string Avatar { get; init; } = "";

        private bool _invited;

        public bool Invited
        {
            get => _invited;
            set
            {
                _invited = value;
                OnPropertyChanged(nameof(Invited));
                OnPropertyChanged(nameof(ButtonText));
                OnPropertyChanged(nameof(CanInvite));
            }
        }

        public string ButtonText => Invited ? "Invited" : "Invite";

        public bool CanInvite => !Invited;
    }

    public class PartyInviteViewModel : NotifyPropertyChangedViewModel
    {
        public PartyInviteViewModel()
        {
            RefreshCommand = new AsyncRelayCommand(RefreshAsync);
            InviteCommand = new AsyncRelayCommand<PartyInviteRow?>(InviteAsync);

            _ = RefreshAsync();
        }

        public ObservableCollection<PartyInviteRow> Friends { get; } = new();

        private string _status = "Loading your friends list.";

        public string Status
        {
            get => _status;
            private set
            {
                _status = value;
                OnPropertyChanged(nameof(Status));
            }
        }

        public ICommand RefreshCommand { get; }

        public ICommand InviteCommand { get; }

        private async Task RefreshAsync()
        {
            Friends.Clear();

            if (!PartyService.InParty)
            {
                Status = "Start a party first, then you can invite people into it.";
                return;
            }

            Status = "Loading your friends list.";

            try
            {
                RobloxCookie.RobloxAccount? me = await RobloxCookie.GetAccountAsync();

                if (me is null)
                {
                    Status = "Sign into Roblox to see who you can invite.";
                    return;
                }

                List<FriendInfo> friends = await FriendsService.GetFriendsAsync(me.UserId);

                if (friends.Count == 0)
                {
                    Status = "No friends came back from Roblox.";
                    return;
                }

                Dictionary<long, FriendPresence> presence = await FriendsService.GetPresenceAsync(friends.Select(f => f.UserId));
                Dictionary<long, string> avatars = await FriendsService.GetAvatarsAsync(friends.Select(f => f.UserId));

                foreach (FriendInfo friend in friends.OrderBy(f => f.DisplayName, StringComparer.OrdinalIgnoreCase))
                {
                    presence.TryGetValue(friend.UserId, out FriendPresence? state);
                    avatars.TryGetValue(friend.UserId, out string? avatar);

                    Friends.Add(new PartyInviteRow
                    {
                        UserId = friend.UserId,
                        Name = string.IsNullOrWhiteSpace(friend.DisplayName) ? friend.Username : friend.DisplayName,
                        Detail = state is null || state.Type == FriendPresenceType.Offline
                            ? "Offline"
                            : string.IsNullOrEmpty(state.LastLocation) ? "Online" : state.LastLocation,
                        Avatar = avatar ?? "",
                    });
                }

                Status = $"Pick who to pull in. They need PhasmaStrap with a linked Roblox account to see the invite.";
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine("PartyInviteViewModel", $"Could not load friends: {ex.Message}");
                Status = "Could not load your friends list: " + ex.Message;
            }
        }

        private static async Task InviteAsync(PartyInviteRow? row)
        {
            if (row is null || row.Invited)
                return;

            row.Invited = await PartyService.InviteAsync(row.UserId);

            if (!row.Invited)
                NotificationCenter.Notify(
                    "Could not invite them",
                    $"{row.Name} needs PhasmaStrap with a linked Roblox account, and you need to be in a party.",
                    NotificationCategory.General,
                    kind: NotificationKindId.Party);
        }
    }
}
