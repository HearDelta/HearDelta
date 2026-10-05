using System.Globalization;
using System.Windows.Data;
using HearDelta.App.ViewModels;

namespace HearDelta.App.Controls;

public sealed class InitialsConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        PersonInitials.From(value as string);

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
