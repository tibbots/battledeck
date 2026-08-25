using System.Windows;

namespace Battledeck.UI.MVVM.View;

/// <summary>
///     The hero picker. All behavior sits in <see cref="HeroPickerViewModel" /> -
///     there is nothing here that the binding cannot do.
/// </summary>
public partial class HeroPicker : Window
{
    public HeroPicker()
    {
        InitializeComponent();

        // Must stand here and not later: CenterOwner computes the position from the size,
        // and whoever clamps after showing it has centered on the old one. See DialogBounds.
        DialogBounds.FitToMainWindow(this);
    }
}
