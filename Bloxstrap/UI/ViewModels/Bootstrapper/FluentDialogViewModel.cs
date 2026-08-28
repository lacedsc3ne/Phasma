using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Media;
using Wpf.Ui.Appearance;

namespace Bloxstrap.UI.ViewModels.Bootstrapper
{
    public class FluentDialogViewModel : BootstrapperDialogViewModel
    {
        public BackgroundType WindowBackdropType { get; set; } = BackgroundType.Acrylic;

        // dark, near-opaque tint over the acrylic blur - glassy at the edges, but reads dark overall
        public SolidColorBrush BackgroundColourBrush { get; set; } = new SolidColorBrush(Color.FromArgb(230, 0x0E, 0x0E, 0x12));

        [Obsolete("Do not use this! This is for the designer only.", true)]
        public FluentDialogViewModel() : base()
        { }

        public FluentDialogViewModel(IBootstrapperDialog dialog, bool aero) : base(dialog)
        {
            const int alpha = 128;

            WindowBackdropType = aero ? BackgroundType.Aero : BackgroundType.Acrylic;

            if (aero)
            {
                BackgroundColourBrush = App.Settings.Prop.Theme.GetFinal() == Enums.Theme.Light ?
                    new SolidColorBrush(Color.FromArgb(alpha, 225, 225, 225)) :
                    new SolidColorBrush(Color.FromArgb(alpha, 30, 30, 30));
            }
        }
    }
}
