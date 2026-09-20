using System;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;

namespace PhasmaStrap.UI.Elements.Controls
{
    public class SectionItem
    {
        public string Label { get; set; } = "";

        public Type? PageType { get; set; }
    }

    [ContentProperty(nameof(Sections))]
    public partial class SectionHost : UserControl
    {
        private readonly Dictionary<Type, Page> _cache = new();

        public ObservableCollection<SectionItem> Sections { get; } = new();

        public SectionHost()
        {
            InitializeComponent();
            RailItems.ItemsSource = Sections;
            Loaded += OnLoaded;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            RailColumn.Width = Sections.Count > 1 ? GridLength.Auto : new GridLength(0);
            Rail.Visibility = Sections.Count > 1 ? Visibility.Visible : Visibility.Collapsed;

            if (RailItems.SelectedIndex < 0 && Sections.Count > 0)
                RailItems.SelectedIndex = 0;
        }

        public void Show(Type pageType)
        {
            for (int i = 0; i < Sections.Count; i++)
            {
                if (Sections[i].PageType != pageType)
                    continue;

                RailItems.SelectedIndex = i;
                return;
            }
        }

        private void RailItems_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (RailItems.SelectedItem is not SectionItem section || section.PageType is null)
                return;

            if (!_cache.TryGetValue(section.PageType, out Page? page))
            {
                object? created = Activator.CreateInstance(section.PageType);
                if (created is not Page made)
                    return;

                page = made;
                _cache[section.PageType] = page;
            }

            SectionFrame.Navigate(page);
        }
    }
}
