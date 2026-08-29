using System.Text.Json.Serialization;

namespace PhasmaStrap.Server.WebServer.Enums;

[JsonConverter(typeof(JsonStringEnumConverter))]
internal enum CharacterLoadType
{
	Fetch,
	Whole
}
