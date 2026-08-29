using System.Text.Json.Serialization;

namespace PhasmaStrap.Server.WebServer;

internal class AssetLocation
{
	[JsonPropertyName("location")]
	public string? Location { get; set; }
}
