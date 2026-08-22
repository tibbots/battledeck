using System.Windows;

namespace Smurftown.UI.MVVM.View
{
    /// <summary>
    ///     Interaction logic for RegionPicker.xaml
    /// </summary>
    public partial class RegionPicker : Window
    {
        public RegionPicker()
        {
            InitializeComponent();

            // At 440x260 the call changes nothing today - it stands here so the rule still
            // applies if somebody enlarges this dialog later. See DialogBounds.
            DialogBounds.FitToMainWindow(this);
        }
    }
}
