using System.Globalization;
using System.Windows.Data;
using HearDelta.App.ViewModels;

namespace HearDelta.App.Controls;

/// <summary>Zeigt eine gespeicherte Kopfhörerbauform in der Oberflächensprache an.</summary>
public sealed class HeadphoneDesignLabelConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        SettingsViewModel.DesignLabel(value as string);

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
