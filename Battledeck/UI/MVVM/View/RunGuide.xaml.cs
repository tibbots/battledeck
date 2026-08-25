using System.Windows;

namespace Battledeck.UI.MVVM.View
{
    /// <summary>
    ///     Interaction logic for RunGuide.xaml
    /// </summary>
    public partial class RunGuide : Window
    {
        public RunGuide()
        {
            InitializeComponent();
            DialogBounds.FitToMainWindow(this);

            // THE RUN STARTS ON Loaded AND NOT IN THE VIEW MODEL'S CONSTRUCTOR, and that is the
            // whole reason this file has a line in it at all. The first thing the run does is
            // wait for a human to bring the game to the front - so there has to be a window on
            // screen telling them to, and at construction time there is not one yet.
            Loaded += (_, _) =>
            {
                if (DataContext is RunGuideViewModel guide) guide.Start();
            };
        }
    }
}
