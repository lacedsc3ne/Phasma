using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

using PhasmaStrap.UI.ViewModels.Settings;

namespace PhasmaStrap.UI.Elements.Settings.Pages
{
    /// <summary>
    /// Interaction logic for FastFlagSettingsPage.xaml - the FastFlag tab host: Roblox FFlags,
    /// Fast Flag Editor, Per-game flags, NVIDIA FFlags, Asset Warp and Asset Engine. Most tabs
    /// embed their own page via Frame, each constructing and owning its own ViewModel; only the
    /// Asset Warp tab's content is bound directly against this page's own DataContext.
    /// </summary>
    public partial class FastFlagSettingsPage
    {
        public FastFlagSettingsPage()
        {
            DataContext = new AssetWarpViewModel();
            InitializeComponent();
        }

        // Switches the tab host that `from` sits in to the tab whose Frame shows `pageName`
        // (for example "FastFlagEditorPage") - lets the tabs link to each other.
        public static void SelectTab(DependencyObject from, string pageName)
        {
            DependencyObject? node = from;

            while (node is not null)
            {
                if (node is TabControl tabs)
                {
                    foreach (TabItem item in tabs.Items.OfType<TabItem>())
                    {
                        if (item.Content is Frame frame && frame.Source?.OriginalString.Contains(pageName, StringComparison.OrdinalIgnoreCase) == true)
                        {
                            tabs.SelectedItem = item;
                            return;
                        }
                    }
                }

                node = (node is Visual ? VisualTreeHelper.GetParent(node) : null) ?? LogicalTreeHelper.GetParent(node);
            }
        }
    }
}
