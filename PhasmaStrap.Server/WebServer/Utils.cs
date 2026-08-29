using PhasmaStrap.Server.Common;

namespace PhasmaStrap.Server.WebServer;

public class Utils
{
	public static string GetMapsDirectory()
	{
		return (!string.IsNullOrWhiteSpace(Config.Instance.User.Launch.CustomMapsDirectory)) ? Config.Instance.User.Launch.CustomMapsDirectory : PathHelper.Maps;
	}
}
