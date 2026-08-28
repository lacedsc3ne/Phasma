namespace PhasmaStrap.Models
{
    public sealed class RobloxDatacenter
    {
        public string City { get; init; } = "";
        public string Region { get; init; } = "";
        public string Country { get; init; } = "";
        public double Lat { get; init; }
        public double Lon { get; init; }
    }

    public sealed class SeedCidrEntry
    {
        public string Cidr { get; init; } = "";
        public string City { get; init; } = "";
        public string Region { get; init; } = "";
        public string Country { get; init; } = "";
        public double Lat { get; init; }
        public double Lon { get; init; }
    }

    public sealed class UserGeo
    {
        public double Lat { get; init; }
        public double Lon { get; init; }
        public string City { get; init; } = "";
        public string Region { get; init; } = "";
        public string Country { get; init; } = "";
    }

    public sealed class MatchmakerCandidate
    {
        public string JobId { get; init; } = "";
        public string MachineAddress { get; init; } = "";
        public int Port { get; init; }
        public RobloxDatacenter? Datacenter { get; init; }
        public double DistanceKm { get; init; }
        public int Playing { get; init; }
        public int MaxPlayers { get; init; }
        public int Ping { get; init; }
        public int EstimatedPingMs { get; init; }
        public double Score { get; init; }
        public string? BlockedClosestCity { get; init; }
        public double BlockedClosestDistanceKm { get; init; }

        public string DatacenterName => Datacenter is null
            ? "unknown"
            : (string.IsNullOrEmpty(Datacenter.Country) ? Datacenter.City : $"{Datacenter.City}, {Datacenter.Country}");
    }

    public sealed class LearnedServerEntry
    {
        public string Cidr { get; set; } = "";
        public string City { get; set; } = "";
        public string Region { get; set; } = "";
        public string Country { get; set; } = "";
        public double Lat { get; set; }
        public double Lon { get; set; }
        public DateTime FirstSeenUtc { get; set; } = DateTime.UtcNow;
        public DateTime LastSeenUtc { get; set; } = DateTime.UtcNow;
        public int SeenCount { get; set; }

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public List<string>? IPs { get; set; }

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public List<int>? PingSamplesMs { get; set; }
    }

    public sealed class ServerFetchData
    {
        public Dictionary<string, LearnedServerEntry> Servers { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;
        public int SchemaVersion { get; set; } = 1;
    }
}
