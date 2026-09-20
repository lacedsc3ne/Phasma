namespace PhasmaStrap.Models
{
    public class RPCTemplate
    {
        public bool Enabled { get; set; } = true;

        public string GameID { get; set; } = "";

        public string DetailsTemplate { get; set; } = "";
        public string StateTemplate { get; set; } = "";

        public string LargeImageUrl { get; set; } = "";
        public string SmallImageUrl { get; set; } = "";

        public string ButtonLabel { get; set; } = "";
        public string ButtonUrl { get; set; } = "";
    }
}
