using PhasmaStrap.UI.Elements.Base;
using PhasmaStrap.Utility;

namespace PhasmaStrap.UI.Elements.Dialogs
{
    // Shows exactly which flags a game starts with, and where each one comes from
    public partial class FlagPreviewDialog : WpfUiWindow
    {
        public sealed record Row(string Name, string Value, string From, string Tone);

        public FlagPreviewDialog(string gameName, FlagProfile? profile)
        {
            InitializeComponent();

            Title = "Flags for " + gameName;
            TitleBarElement.Title = Title;
            HeadingText.Text = gameName;

            List<FlagLayers.EffectiveFlag> flags = FlagLayers.Explain(App.FastFlags.Prop, profile);

            FlagGrid.ItemsSource = flags.Select(f => f.Source switch
            {
                FlagLayers.Source.Added => new Row(f.Name, f.Value, $"Profile: {profile?.Name}", "Added"),
                FlagLayers.Source.Changed => new Row(f.Name, f.Value, $"Profile (yours is {f.YourValue})", "Changed"),
                FlagLayers.Source.TurnedOff => new Row(f.Name, "-", "Turned off by the profile", "Off"),
                _ => new Row(f.Name, f.Value, "Your flags", ""),
            }).ToList();

            int total = flags.Count(f => f.Source != FlagLayers.Source.TurnedOff);
            int added = flags.Count(f => f.Source == FlagLayers.Source.Added);
            int changed = flags.Count(f => f.Source == FlagLayers.Source.Changed);
            int off = flags.Count(f => f.Source == FlagLayers.Source.TurnedOff);

            string unsaved = App.FastFlags.Changed || App.FlagProfiles.Changed ? " This includes changes you haven't saved yet." : "";

            SummaryText.Text = profile is null
                ? $"No profile - this game starts with your {total} flag(s).{unsaved}"
                : $"Starts with {total} flag(s): your flags, with \"{profile.Name}\" adding {added}, changing {changed} and turning off {off}.{unsaved}";
        }
    }
}
