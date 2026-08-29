using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

using PhasmaStrap.Resources;

namespace PhasmaStrap.UI.Elements.Dialogs
{
    // A small helper dialog that offers commonly-used FastFlag values (booleans, round
    // numbers, FPS caps, etc) so users don't have to remember/retype them by hand when
    // filling in a flag's value in AddFastFlagDialog. This is unrelated to
    // FastFlagManager.PresetFlags (which maps friendly names to individual flags) or the
    // curated optimization toggles on FastFlagsPage - it's purely a value picker.
    //
    // Ported from Voidstrap's UI/Elements/Dialogs/FFlagPresetsDialog.xaml.cs.
    public partial class FFlagPresetsDialog
    {
        private readonly (string CategoryKey, string[] Values)[] _presetCategories = new (string, string[])[]
        {
            (Strings.Dialog_FFlagPresetValues_Category_Boolean, new[] { "True", "False" }),
            (Strings.Dialog_FFlagPresetValues_Category_BasicNumbers, new[] { "0", "1", "10", "100", "1000" }),
            (Strings.Dialog_FFlagPresetValues_Category_LargeNumbers, new[] { "10000", "100000", "1000000", "2147483647" }),
            (Strings.Dialog_FFlagPresetValues_Category_Percentages, new[] { "0", "25", "50", "75", "100" }),
            (Strings.Dialog_FFlagPresetValues_Category_FPSValues, new[] { "30", "60", "120", "144", "240", "360" }),
            (Strings.Dialog_FFlagPresetValues_Category_QualityLevels, new[] { "0", "1", "2", "3", "4", "5", "10", "21" }),
            (Strings.Dialog_FFlagPresetValues_Category_SpecialValues, new[] { "-1", "null", "\"\"" }),
            (Strings.Dialog_FFlagPresetValues_Category_MemoryValues, new[] { "1024", "2048", "4096", "8192", "16384" }),
        };

        public MessageBoxResult Result = MessageBoxResult.Cancel;

        public string? SelectedValue { get; private set; }

        public FFlagPresetsDialog()
        {
            InitializeComponent();
            LoadPresetCategories();
        }

        private void LoadPresetCategories()
        {
            foreach ((string categoryKey, string[] values) in _presetCategories)
            {
                Expander expander = new Expander
                {
                    Header = categoryKey,
                    Margin = new Thickness(0, 5, 0, 5),
                    IsExpanded = categoryKey == Strings.Dialog_FFlagPresetValues_Category_Boolean,
                };

                StackPanel stackPanel = new StackPanel();

                foreach (string value in values)
                {
                    Button button = new Button
                    {
                        Content = value,
                        Tag = value,
                        Margin = new Thickness(2),
                        Padding = new Thickness(8, 4, 8, 4),
                        HorizontalAlignment = HorizontalAlignment.Stretch,
                        HorizontalContentAlignment = HorizontalAlignment.Left,
                    };
                    button.Click += OnPresetClick;
                    stackPanel.Children.Add(button);
                }

                expander.Content = stackPanel;
                PresetStackPanel.Children.Add(expander);
            }
        }

        private void OnPresetClick(object sender, RoutedEventArgs e)
        {
            if (sender is not Button { Tag: string value })
                return;

            SelectedValue = value;
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
