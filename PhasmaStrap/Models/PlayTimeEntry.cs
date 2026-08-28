namespace PhasmaStrap.Models
{
    public sealed class PlayTimeEntry
    {
        public long PlaceId { get; set; }
        public long UniverseId { get; set; }
        public string Name { get; set; } = "";
        public string IconUrl { get; set; } = "";
        public double TotalMinutes { get; set; }
        public DateTime LastPlayed { get; set; }

        public string TotalTimeText
        {
            get
            {
                int totalMinutes = (int)Math.Round(TotalMinutes);
                int hours = totalMinutes / 60;
                int minutes = totalMinutes % 60;

                if (hours > 0)
                    return $"{hours}h {minutes}m";

                return $"{minutes}m";
            }
        }

        public string LastPlayedText
        {
            get
            {
                if (LastPlayed == default)
                    return "";

                TimeSpan elapsed = DateTime.Now - LastPlayed;

                if (elapsed < TimeSpan.Zero)
                    elapsed = TimeSpan.Zero;

                if (elapsed.TotalMinutes < 1)
                    return "Just now";

                if (elapsed.TotalHours < 1)
                    return $"{(int)elapsed.TotalMinutes}m ago";

                if (elapsed.TotalDays < 1)
                    return $"{(int)elapsed.TotalHours}h ago";

                if (elapsed.TotalDays < 30)
                    return $"{(int)elapsed.TotalDays}d ago";

                return LastPlayed.ToString("yyyy-MM-dd");
            }
        }
    }

    public sealed class PlayTimeData
    {
        public Dictionary<long, PlayTimeEntry> Places { get; set; } = new();
        public int SchemaVersion { get; set; } = 1;
    }
}
