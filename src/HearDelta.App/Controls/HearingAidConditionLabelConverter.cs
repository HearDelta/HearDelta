using System.Globalization;
using System.Windows.Data;
using HearDelta.Core;

namespace HearDelta.App.Controls;

/// <summary>Zeigt eine Hörgerätebedingung in der Oberflächensprache an.</summary>
public sealed class HearingAidConditionLabelConverter : IValueConverter
{
    public static string Label(HearingAidCondition condition) => condition == HearingAidCondition.WithHearingAid
        ? Strings.Common_WithAid
        : Strings.Common_WithoutAid;

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is HearingAidCondition condition ? Label(condition) : value;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
