using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;

namespace Smurftown.UI.MVVM.View
{
    public static class RichTextBoxHelper
    {
        public static readonly DependencyProperty DocumentProperty =
            DependencyProperty.RegisterAttached("Document", typeof(string), typeof(RichTextBoxHelper),
                new PropertyMetadata(default(string), OnDocumentChanged));

        public static string GetDocument(DependencyObject obj)
        {
            return (string)obj.GetValue(DocumentProperty);
        }

        public static void SetDocument(DependencyObject obj, string value)
        {
            obj.SetValue(DocumentProperty, value);
        }

        private static void OnDocumentChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is RichTextBox richTextBox)
            {
                var newDocument = e.NewValue as string;
                if (newDocument != null)
                {
                    richTextBox.Document.Blocks.Clear();
                    richTextBox.Document.Blocks.Add(new Paragraph(new Run(newDocument)));
                    richTextBox.TextChanged += (sender, args) =>
                    {
                        var rtb = sender as RichTextBox;
                        if (rtb != null)
                        {
                            SetDocument(rtb, new TextRange(rtb.Document.ContentStart, rtb.Document.ContentEnd).Text);
                        }
                    };
                }
            }
        }
    }
}