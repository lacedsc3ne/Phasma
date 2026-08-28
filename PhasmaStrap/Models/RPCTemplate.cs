namespace PhasmaStrap.Models
{
    // user-authored Discord Rich Presence template for a specific game, ported (scoped down) from
    // Voidstrap's RPCCustomizer feature. unlike Voidstrap's version (which runs an entirely separate
    // DiscordRpcClient/"app"), this template is applied as the baseline presence for the matching
    // game within PhasmaStrap's existing single Discord RPC connection - see DiscordRichPresence.cs
    // for how it interacts with the BloxstrapRPC in-game protocol.
    public class RPCTemplate
    {
        public bool Enabled { get; set; } = true;

        // roblox place ID this template applies to
        public string GameID { get; set; } = "";

        // supports placeholder tokens: {gameName}, {status}, {creator}, {placeId}, {universeId}
        public string DetailsTemplate { get; set; } = "";
        public string StateTemplate { get; set; } = "";

        // leave blank to keep the default (fetched) image
        public string LargeImageUrl { get; set; } = "";
        public string SmallImageUrl { get; set; } = "";

        // leave blank to keep the default button(s)
        public string ButtonLabel { get; set; } = "";
        public string ButtonUrl { get; set; } = "";
    }
}
