using System.Windows;
using System.Windows.Media;

using PhasmaStrap.UI.Elements.Base;
using PhasmaStrap.Utility;

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
