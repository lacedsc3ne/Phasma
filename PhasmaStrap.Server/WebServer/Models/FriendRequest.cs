using PhasmaStrap.Server.WebServer.Enums;

namespace PhasmaStrap.Server.WebServer.Models;

internal class FriendRequest
{
	public int Inviter { get; set; }

	public int Invitee { get; set; }

	public FriendStatus? Status { get; set; }
}
