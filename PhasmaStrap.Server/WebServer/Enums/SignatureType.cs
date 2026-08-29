using System.Text.Json.Serialization;

namespace PhasmaStrap.Server.WebServer.Enums;

[JsonConverter(typeof(JsonStringEnumConverter))]
internal enum SignatureType
{
	None,
	Legacy,
	RbxSig,
	RbxSig2,
	RbxSig4
}
