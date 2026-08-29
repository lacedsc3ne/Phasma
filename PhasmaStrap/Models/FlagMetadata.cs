namespace PhasmaStrap.Models
{
    // Tracks which tracker source a flag came from and when it was first indexed during
    // this session. Ported from Voidstrap's UI/Elements/Dialogs/FlagMetadata.cs.
    public class FlagMetadata
    {
        public string Source { get; set; } = "";

        public DateTime DateAdded { get; set; }
    }
}
