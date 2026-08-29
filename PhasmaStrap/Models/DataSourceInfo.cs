namespace PhasmaStrap.Models
{
    // Tracks the fetch status of one public FastFlag tracker source while
    // FFlagSearchDialog loads its in-memory flag database. Not currently bound to any
    // visible UI (Voidstrap's original didn't display this list either), so unlike
    // Voidstrap's DataSourceInfo this is a plain POCO instead of INotifyPropertyChanged.
    public class DataSourceInfo
    {
        public string Name { get; set; } = "";

        public string Url { get; set; } = "";

        public string Status { get; set; } = "";

        public int FlagCount { get; set; }
    }
}
