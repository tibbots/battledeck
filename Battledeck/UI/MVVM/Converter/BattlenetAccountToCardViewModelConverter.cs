using System.Globalization;
using System.Windows;
using System.Windows.Data;
using Battledeck.Backend.Entity;
using Battledeck.UI.MVVM.ViewModel;

namespace Battledeck.UI.MVVM.Converter;

/// <summary>
///     Turns a row of the list into its view model.
///     <para>
///         <b>The name has not been accurate since 21.08.2026</b>: what is converted is not a
///         <c>BattlenetAccount</c>, but an <see cref="AccountRegion" /> - an account in
///         one of its regions. The name depends on the key in <c>AccountsView.xaml</c> and
///         will be renamed together with the other card names, not incidentally.
///     </para>
/// </summary>
[ValueConversion(typeof(AccountRegion), typeof(AccountCardViewModel))]
class BattlenetAccountToCardViewModelConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is AccountRegion row)
        {
            return new AccountCardViewModel(row);
        }
        return DependencyProperty.UnsetValue;
    }

    public object? ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is AccountCardViewModel model)
        {
            return model.Row;
        }
        return DependencyProperty.UnsetValue;
    }
}