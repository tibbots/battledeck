using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Controls;
using System.Windows;

namespace Smurftown.UI.MVVM.View
{
    /// <summary>
    ///     Carries a password from the ViewModel into a <see cref="PasswordBox" />.
    ///     <para>
    ///         The other direction does not run through here: <c>PasswordBox.Password</c> is no
    ///         dependency property - deliberately so, a bindable one would leave the value in
    ///         WPF's property store - so the box reports its content through the
    ///         <c>PasswordChanged</c> handler in <c>AddOrEditAccount.xaml.cs</c>.
    ///     </para>
    /// </summary>
    public static class PasswordBoxHelper
    {
        public static readonly DependencyProperty BoundPasswordProperty =
            DependencyProperty.RegisterAttached("BoundPassword", typeof(string), typeof(PasswordBoxHelper),
            new PropertyMetadata(string.Empty, OnBoundPasswordChanged));

        public static string GetBoundPassword(DependencyObject dp)
        {
            return (string)dp.GetValue(BoundPasswordProperty);
        }

        public static void SetBoundPassword(DependencyObject dp, string value)
        {
            dp.SetValue(BoundPasswordProperty, value);
        }

        /// <summary>
        ///     Writes the value into the box - <b>unless the box is the one it came from</b>.
        ///     <para>
        ///         <b>That guard is what makes the field typeable, and without it the password
        ///         came out backwards.</b> Every keystroke ran a full circle: the box reports
        ///         it, the handler in the code-behind writes it to the ViewModel, the ViewModel
        ///         notifies, the binding arrives here - and assigning <c>Password</c> puts the
        ///         caret back to position 0, because WPF replaces the whole content rather than
        ///         editing it. The next character was therefore inserted in FRONT of everything
        ///         typed so far, and "secret" arrived as "terces".
        ///     </para>
        ///     <para>
        ///         The comparison is against the box and not against a flag: the two cases that
        ///         must still get through - the dialog opening on an existing account, and a
        ///         ViewModel that corrects the value - both differ from what stands there, and
        ///         the echo of one's own keystroke never does.
        ///     </para>
        /// </summary>
        private static void OnBoundPasswordChanged(DependencyObject dp, DependencyPropertyChangedEventArgs e)
        {
            if (dp is not PasswordBox passwordBox) return;

            var value = (string)e.NewValue ?? "";
            if (passwordBox.Password == value) return;

            passwordBox.Password = value;
        }
    }
}
