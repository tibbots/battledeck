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
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace Battledeck.UI.MVVM.View
{
    /// <summary>
    /// Interaction logic for ErrorBox.xaml
    /// </summary>
    public partial class ErrorBox : Window
    {
        public ErrorBox()
        {
            InitializeComponent();

            // At 400x250 the call changes nothing today - it stands here so the rule still
            // applies if someone enlarges this dialog later. See DialogBounds.
            DialogBounds.FitToMainWindow(this);
        }
    }
}
