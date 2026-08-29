using System.ComponentModel;

namespace PhasmaStrap.Server.Common.Enums;

public enum MembershipType
{
	None,
	[Description("Builders Club")]
	BuildersClub,
	[Description("Turbo Builders Club")]
	TurboBuildersClub,
	[Description("Outrageous Builders Club")]
	OutrageousBuildersClub
}
