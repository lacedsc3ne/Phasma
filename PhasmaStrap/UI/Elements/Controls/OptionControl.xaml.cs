using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace PhasmaStrap.UI.Elements.Controls
{
    /// <summary>
    /// Interaction logic for OptionControl.xaml
    /// </summary
    [ContentProperty(nameof(InnerContent))]
    public partial class OptionControl : UserControl
    {
        public static readonly DependencyProperty HeaderProperty =
            DependencyProperty.Register(nameof(Header), typeof(string), typeof(OptionControl));

        public static readonly DependencyProperty DescriptionProperty =
            DependencyProperty.Register(nameof(Description), typeof(string), typeof(OptionControl));

        public static readonly DependencyProperty HelpLinkProperty =
            DependencyProperty.Register(nameof(HelpLink), typeof(string), typeof(OptionControl));

        public static readonly DependencyProperty InnerContentProperty =
            DependencyProperty.Register(nameof(InnerContent), typeof(object), typeof(OptionControl));

        public string Header
        {
            get { return (string)GetValue(HeaderProperty); }
            set { SetValue(HeaderProperty, value); }
        }

        public string Description
        {
            get { return (string)GetValue(DescriptionProperty); }
            set { SetValue(DescriptionProperty, value); }
        }

        public string HelpLink
        {
            get { return (string)GetValue(HelpLinkProperty); }
            set { SetValue(HelpLinkProperty, value); }
        }

        public object InnerContent
        {
            get { return GetValue(InnerContentProperty); }
            set { SetValue(InnerContentProperty, value); }
        }

        public static readonly DependencyProperty FlatProperty =
            DependencyProperty.Register(nameof(Flat), typeof(bool), typeof(OptionControl), new PropertyMetadata(false));

        /// <summary>
        /// Renders as a borderless row instead of a standalone card. Set automatically when the
        /// control is placed inside a CardExpander/Card (so groups don't show nested card borders);
        /// can be set explicitly in XAML to force either look.
        /// </summary>
        public bool Flat
        {
            get { return (bool)GetValue(FlatProperty); }
            set { SetValue(FlatProperty, value); }
        }

        private bool _flatExplicitlySet;

        public OptionControl()
        {
            InitializeComponent();
            Loaded += OnLoaded;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            if (_flatExplicitlySet || ReadLocalValue(FlatProperty) != DependencyProperty.UnsetValue)
            {
                _flatExplicitlySet = true;
                return;
            }

            DependencyObject? parent = VisualTreeHelper.GetParent(this);
            while (parent is not null)
            {
                if (parent is Wpf.Ui.Controls.CardExpander || parent is Wpf.Ui.Controls.Card || parent is Expander)
                {
                    Flat = true;
                    return;
                }

                // stop at the page boundary - anything above it is window chrome
                if (parent is Page)
                    return;

                parent = VisualTreeHelper.GetParent(parent);
            }
        }
    }
}
