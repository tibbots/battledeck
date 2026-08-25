using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using Serilog;

namespace Battledeck.UI.MVVM.View;

public partial class AddOrEditAccount : Window
{
    public AddOrEditAccount()
    {
        InitializeComponent();

        // Must stand here and not later: CenterOwner computes the position from the size,
        // and whoever clamps after showing it has centered on the old one. See DialogBounds.
        DialogBounds.FitToMainWindow(this);

        Log.Information($"dataContext: {DataContext}");
    }

    private void PasswordChanged(object sender, RoutedEventArgs e)
    {
        if (this.DataContext != null)
        {
            ((dynamic)this.DataContext).Password = ((PasswordBox)sender).Password;
        }
    }

    private void RichTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (this.DataContext == null) return;
        var richtTextBox = ((RichTextBox)sender);

        ((dynamic)this.DataContext).Notes =
            new TextRange(richtTextBox.Document.ContentStart, richtTextBox.Document.ContentEnd).Text;
    }

    private void Window_DataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.NewValue is not AddOrEditAccountViewModel viewModel) return;
        NotesTextBox.AppendText(viewModel.Notes);
    }
}