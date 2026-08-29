using System.Windows;
using System.Windows.Media;

using PhasmaStrap.UI.Elements.Base;
using PhasmaStrap.Utility;

// Heavily simplified port of Voidstrap's UI/Elements/Dialogs/ThemeChangesDialog.xaml.cs.
//
// The original was a review view for Voidstrap's theme *publishing* flow: it diffed a whole
// folder of files (XAML, images, fonts) between "on this PC" and "published to the website",
// with a line-by-line text differ and side-by-side image/font previews. None of that applies
// here - publishing is explicitly out of scope, and PhasmaStrap's app colour theme is a single
// small XAML file of colour/brush overrides, not a package of assets. What's kept is the actual
// useful idea: a review step shown before committing changes, listing what will change. Here
// that's simply "which of the schema's colours changed, from what, to what" - shown as an
// old-swatch -> new-swatch list per key, confirmed with Save/Cancel like PhasmaStrap's other
// simple dialogs (see AddFastFlagDialog.xaml.cs).
namespace PhasmaStrap.UI.Elements.Dialogs
{
    public sealed class ThemeChangeRow
    {
        public string Label { get; init; } = "";

        public string Group { get; init; } = "";

        public bool IsNew { get; init; }

        public bool HasOld => !IsNew;

        public string OldHex { get; init; } = "";

        public string NewHex { get; init; } = "";

        public Brush OldSwatch { get; init; } = Brushes.Transparent;

        public Brush NewSwatch { get; init; } = Brushes.Transparent;
    }

    public partial class ThemeChangesDialog : WpfUiWindow
    {
        public MessageBoxResult Result = MessageBoxResult.Cancel;

        public ThemeChangesDialog(IReadOnlyList<ThemeChangeRow> changes)
        {
            InitializeComponent();

            ChangesList.ItemsSource = changes;

            SummaryText.Text = changes.Count == 1
                ? Strings.Menu_Appearance_ColorTheme_Changes_SummarySingular
                : string.Format(Strings.Menu_Appearance_ColorTheme_Changes_SummaryFormat, changes.Count);
        }

        /// <summary>
        /// Builds the list of rows to review: one per schema key whose resolved colour differs
        /// between the last saved theme and the one about to be saved.
        /// </summary>
        public static List<ThemeChangeRow> BuildChanges(IReadOnlyList<ThemeKeyInfo> schema, Dictionary<string, Color> oldMap, Dictionary<string, Color> newMap)
        {
            List<ThemeChangeRow> changes = new();

            foreach (ThemeKeyInfo info in schema)
            {
                bool hadOld = oldMap.TryGetValue(info.Key, out Color oldColor);
                bool hasNew = newMap.TryGetValue(info.Key, out Color newColor);

                if (!hasNew)
                    continue;

                if (hadOld && oldColor == newColor)
                    continue;

                changes.Add(new ThemeChangeRow
                {
                    Label = info.Label,
                    Group = info.Group,
                    IsNew = !hadOld,
                    OldHex = hadOld ? AppColorTheme.ToHex(oldColor) : "",
                    NewHex = AppColorTheme.ToHex(newColor),
                    OldSwatch = hadOld ? new SolidColorBrush(oldColor) : Brushes.Transparent,
                    NewSwatch = new SolidColorBrush(newColor),
                });
            }

            return changes;
        }

        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            Result = MessageBoxResult.OK;
            Close();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            Result = MessageBoxResult.Cancel;
            Close();
        }
    }
}
