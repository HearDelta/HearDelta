using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using HearDelta.App.ViewModels;

namespace HearDelta.App.Controls;

/// <summary>
/// Liefert zu einem <see cref="BadgeTone"/> die helle Hintergrund- oder die kräftige Vordergrundfarbe
/// aus den Anwendungsressourcen (<c>{Tone}SoftBrush</c> bzw. <c>{Tone}Brush</c>).
/// </summary>
public sealed class ToneBrushConverter : IValueConverter
{
    public string Part { get; set; } = "Background";

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var tone = value is BadgeTone badgeTone ? badgeTone : BadgeTone.Neutral;
        var key = Part == "Background" ? $"{tone}SoftBrush" : $"{tone}Brush";
        return Application.Current?.TryFindResource(key) as Brush ?? Brushes.Transparent;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
