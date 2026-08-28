namespace PhasmaStrap.Models
{
    public sealed class ServerListItem
    {
        public string JobId { get; init; } = "";
        public int Playing { get; init; }
        public int MaxPlayers { get; init; }
        public int Ping { get; init; } = -1;

        public string PlayersText => MaxPlayers > 0 ? $"{Playing}/{MaxPlayers}" : Playing.ToString();
        public string PingText => Ping > 0 ? $"{Ping} ms" : "?";
    }
}
