using System.Globalization;
using System.Windows;
using System.Windows.Media;
using HearDelta.Core;

namespace HearDelta.App.Controls;

/// <summary>
/// Zeigt den Frequenzgang der Filterkette einer Kopfhörerentzerrung (ohne Vorabsenkung) von 20 Hz bis 20 kHz.
/// Punkte markieren die Prüffrequenzen des Hörschwellentests.
/// </summary>
public sealed class EqualizationCurve : FrameworkElement
{
    private const double MinimumFrequencyHz = 20d;
    private const double MaximumFrequencyHz = 20_000d;
    private const double LeftMargin = 34d;
    private const double BottomMargin = 18d;
    private const double TopMargin = 6d;

    private static readonly (double FrequencyHz, string Label)[] FrequencyTicks =
        [(50d, "50"), (100d, "100"), (200d, "200"), (500d, "500"), (1_000d, "1k"), (2_000d, "2k"), (5_000d, "5k"), (10_000d, "10k"), (20_000d, "20k")];

    private static readonly double[] TestFrequencies =
        [62.5d, 125d, 250d, 375d, 500d, 750d, 1_000d, 1_500d, 2_000d, 3_000d, 4_000d, 6_000d, 8_000d, 10_000d];

    public static readonly DependencyProperty EqualizationProperty = DependencyProperty.Register(
        nameof(Equalization),
        typeof(HeadphoneEqualization),
        typeof(EqualizationCurve),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty SampleRateProperty = DependencyProperty.Register(
        nameof(SampleRate),
        typeof(int),
        typeof(EqualizationCurve),
        new FrameworkPropertyMetadata(48_000, FrameworkPropertyMetadataOptions.AffectsRender));

    public HeadphoneEqualization? Equalization
    {
        get => (HeadphoneEqualization?)GetValue(EqualizationProperty);
        set => SetValue(EqualizationProperty, value);
    }

    public int SampleRate
    {
        get => (int)GetValue(SampleRateProperty);
        set => SetValue(SampleRateProperty, value);
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        var width = ActualWidth;
        var height = ActualHeight;
        if (width <= LeftMargin + 10 || height <= TopMargin + BottomMargin + 10)
            return;

        var gridPen = new Pen(new SolidColorBrush(Color.FromRgb(0xE3, 0xE6, 0xEB)), 1);
        var zeroPen = new Pen(new SolidColorBrush(Color.FromRgb(0x9A, 0xA3, 0xAF)), 1);
        var curvePen = new Pen(TryFindResource("AccentBrush") as Brush ?? Brushes.SteelBlue, 2);
        var labelBrush = TryFindResource("MutedBrush") as Brush ?? Brushes.Gray;
        var plotWidth = width - LeftMargin;
        var plotHeight = height - TopMargin - BottomMargin;
        var upper = Math.Min(MaximumFrequencyHz, SampleRate * 0.45);

        double X(double frequency) =>
            LeftMargin + (Math.Log(frequency / MinimumFrequencyHz) / Math.Log(MaximumFrequencyHz / MinimumFrequencyHz) * plotWidth);

        var equalization = Equalization;
        var points = new List<Point>();
        var maximumAbsoluteDb = 6d;
        if (equalization is not null)
        {
            for (var frequency = MinimumFrequencyHz; frequency <= upper; frequency *= Math.Pow(2, 1d / 24))
            {
                var response = HeadphoneEqualizer.GetFilterResponseDb(equalization, frequency, SampleRate);
                maximumAbsoluteDb = Math.Max(maximumAbsoluteDb, Math.Abs(response));
                points.Add(new Point(X(frequency), response));
            }
        }

        var range = Math.Ceiling(maximumAbsoluteDb / 6) * 6;
        double Y(double decibels) => TopMargin + ((range - decibels) / (2 * range) * plotHeight);

        for (var level = -range; level <= range; level += 6)
        {
            drawingContext.DrawLine(level == 0 ? zeroPen : gridPen, new Point(LeftMargin, Y(level)), new Point(width, Y(level)));
            DrawText(drawingContext, level.ToString("+0;−0;0", CultureInfo.InvariantCulture), labelBrush, LeftMargin - 4, Y(level), alignRight: true);
        }
        foreach (var (frequency, label) in FrequencyTicks)
        {
            drawingContext.DrawLine(gridPen, new Point(X(frequency), TopMargin), new Point(X(frequency), TopMargin + plotHeight));
            DrawText(drawingContext, label, labelBrush, X(frequency), height - (BottomMargin / 2), alignRight: false, center: true);
        }

        if (points.Count < 2 || equalization is null)
            return;
        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            context.BeginFigure(new Point(points[0].X, Y(points[0].Y)), false, false);
            context.PolyLineTo(points.Skip(1).Select(point => new Point(point.X, Y(point.Y))).ToList(), true, true);
        }
        geometry.Freeze();
        drawingContext.DrawGeometry(null, curvePen, geometry);

        foreach (var frequency in TestFrequencies.Where(frequency => frequency <= upper))
        {
            var response = HeadphoneEqualizer.GetFilterResponseDb(equalization, frequency, SampleRate);
            drawingContext.DrawEllipse(curvePen.Brush, null, new Point(X(frequency), Y(response)), 2.5, 2.5);
        }
    }

    private void DrawText(DrawingContext drawingContext, string text, Brush brush, double x, double y, bool alignRight, bool center = false)
    {
        var formatted = new FormattedText(
            text,
            CultureInfo.CurrentUICulture,
            FlowDirection.LeftToRight,
            new Typeface("Segoe UI"),
            10,
            brush,
            VisualTreeHelper.GetDpi(this).PixelsPerDip);
        var left = alignRight ? x - formatted.Width : center ? x - (formatted.Width / 2) : x;
        drawingContext.DrawText(formatted, new Point(left, y - (formatted.Height / 2)));
    }
}
