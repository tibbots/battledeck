using System.Windows;

namespace Battledeck.UI.MVVM.View
{
    /// <summary>
    ///     Interaction logic for ReuseGuide.xaml
    /// </summary>
    public partial class ReuseGuide : Window
    {
        public ReuseGuide()
        {
            InitializeComponent();
            DialogBounds.FitToMainWindow(this);

            // THE RUN STARTS ON Loaded AND NOT IN THE VIEW MODEL'S CONSTRUCTOR - same reason as
            // RunGuide: the first thing it does is wait for a human to bring the game to the
            // front, and there has to be a window on screen telling them to.
            Loaded += (_, _) =>
            {
                if (DataContext is ReuseGuideViewModel guide) guide.Start();
            };
        }
    }
}
