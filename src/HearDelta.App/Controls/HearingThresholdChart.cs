using System.Collections;
using System.Collections.Specialized;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using HearDelta.App.ViewModels;
using HearDelta.Core;

namespace HearDelta.App.Controls;

public sealed class HearingThresholdChart : Control
{
    private const double DefaultMinimumFrequencyHz = 62.5d;
    private const double DefaultMaximumFrequencyHz = 10_000d;
    private const double MinimumLevelDbfs = -100d;
    private const double DefaultMaximumLevelDbfs = -6d;

    /// <summary>Hauptteilung; ohne Beschriftung wird die Frequenz in der Oberflächensprache formatiert.</summary>
    private static readonly (double FrequencyHz, string? Label)[] FrequencyTicks =
    [
        (31.25d, null),
        (62.5d, null),
        (125d, "125"),
        (250d, "250"),
        (500d, "500"),
        (1_000d, "1k"),
        (2_000d, "2k"),
        (4_000d, "4k"),
        (8_000d, "8k"),
        (10_000d, "10k"),
        (12_000d, "12k"),
        (16_000d, "16k")
    ];

    private static readonly double[] MinorFrequencyTicks =
        [375d, 750d, 1_500d, 3_000d, 6_000d];

    private INotifyCollectionChanged? observedCollection;

    public static readonly DependencyProperty ItemsSourceProperty = DependencyProperty.Register(
        nameof(ItemsSource),
        typeof(IEnumerable),
        typeof(HearingThresholdChart),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender, OnItemsSourceChanged));

    public static readonly DependencyProperty EarProperty = DependencyProperty.Register(
        nameof(Ear),
        typeof(TestedEar),
        typeof(HearingThresholdChart),
        new FrameworkPropertyMetadata(TestedEar.Left, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>Optionale Vergleichskurven; wenn gesetzt, ersetzen sie <see cref="ItemsSource"/>.</summary>
    public static readonly DependencyProperty SeriesProperty = DependencyProperty.Register(
        nameof(Series),
        typeof(IEnumerable),
        typeof(HearingThresholdChart),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender, OnItemsSourceChanged));

    public static readonly DependencyProperty MaximumAttenuationDbfsProperty = DependencyProperty.Register(
        nameof(MaximumAttenuationDbfs),
        typeof(double),
        typeof(HearingThresholdChart),
        new FrameworkPropertyMetadata(DefaultMaximumLevelDbfs, FrameworkPropertyMetadataOptions.AffectsRender));

    public IEnumerable? ItemsSource
    {
        get => (IEnumerable?)GetValue(ItemsSourceProperty);
        set => SetValue(ItemsSourceProperty, value);
    }

    public IEnumerable? Series
    {
        get => (IEnumerable?)GetValue(SeriesProperty);
        set => SetValue(SeriesProperty, value);
    }

    public TestedEar Ear
    {
        get => (TestedEar)GetValue(EarProperty);
        set => SetValue(EarProperty, value);
    }

    public double MaximumAttenuationDbfs
    {
        get => (double)GetValue(MaximumAttenuationDbfsProperty);
        set => SetValue(MaximumAttenuationDbfsProperty, value);
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);

        var bounds = new Rect(0, 0, ActualWidth, ActualHeight);
        drawingContext.DrawRectangle(Brushes.Transparent, null, bounds);
        if (ActualWidth < 320 || ActualHeight < 260)
            return;

        var ink = FindBrush("InkBrush", Color.FromRgb(8, 36, 90));
        var muted = FindBrush("MutedBrush", Color.FromRgb(86, 101, 126));
        var border = FindBrush("BorderBrush", Color.FromRgb(203, 211, 223));
        var majorGrid = new Pen(border, 1d);
        var minorGrid = new Pen(new SolidColorBrush(Color.FromRgb(225, 230, 237)), 1d)
        {
            DashStyle = DashStyles.Dot
        };
        var axis = new Pen(ink, 1.4d);
        var curves = CreateCurves();
        var points = curves.SelectMany(curve => curve.Rows).ToArray();
        var minimumFrequencyHz = points.Any(item => item.FrequencyHz < DefaultMinimumFrequencyHz)
            ? 31.25d
            : DefaultMinimumFrequencyHz;
        var maximumFrequencyHz = points.Any(item => item.FrequencyHz > DefaultMaximumFrequencyHz)
            ? 16_000d
            : DefaultMaximumFrequencyHz;
        var maximumLevelDbfs = Math.Clamp(MaximumAttenuationDbfs, MinimumLevelDbfs + 1d, 0d);

        var legendHeight = MeasureLegendHeight(curves, maximumLevelDbfs, ActualWidth - 108);
        var plot = new Rect(78, 20 + legendHeight, Math.Max(1, ActualWidth - 108), Math.Max(1, ActualHeight - 88 - legendHeight));
        DrawLegend(drawingContext, curves, ink, muted, maximumLevelDbfs, plot.Left, plot.Right);

        for (var level = -100; level <= maximumLevelDbfs - 5d; level += 10)
        {
            var y = MapLevel(level, plot, maximumLevelDbfs);
            var isMaximum = Math.Abs(level - maximumLevelDbfs) < 0.001d;
            drawingContext.DrawLine(level % 20 == 0 || isMaximum ? majorGrid : minorGrid, new Point(plot.Left, y), new Point(plot.Right, y));
            if (level % 20 == 0 || isMaximum)
                DrawText(drawingContext, level.ToString(CultureInfo.InvariantCulture), 13, muted, plot.Left - 12, y, TextAnchor.RightCenter);
        }

        var maximumY = MapLevel(maximumLevelDbfs, plot, maximumLevelDbfs);
        if (maximumLevelDbfs % 10d != 0d)
        {
            drawingContext.DrawLine(majorGrid, new Point(plot.Left, maximumY), new Point(plot.Right, maximumY));
            DrawText(drawingContext, FormatLevel(maximumLevelDbfs), 13, muted, plot.Left - 12, maximumY, TextAnchor.RightCenter);
        }

        foreach (var tick in MinorFrequencyTicks)
        {
            if (tick < minimumFrequencyHz || tick > maximumFrequencyHz)
                continue;
            var x = MapFrequency(tick, plot, minimumFrequencyHz, maximumFrequencyHz);
            drawingContext.DrawLine(minorGrid, new Point(x, plot.Top), new Point(x, plot.Bottom));
        }

        foreach (var tick in FrequencyTicks)
        {
            if (tick.FrequencyHz < minimumFrequencyHz || tick.FrequencyHz > maximumFrequencyHz)
                continue;
            var x = MapFrequency(tick.FrequencyHz, plot, minimumFrequencyHz, maximumFrequencyHz);
            drawingContext.DrawLine(majorGrid, new Point(x, plot.Top), new Point(x, plot.Bottom));
            DrawText(drawingContext, tick.Label ?? tick.FrequencyHz.ToString("0.##", CultureInfo.CurrentCulture), 13, muted, x, plot.Bottom + 12, TextAnchor.CenterTop);
        }

        drawingContext.DrawLine(axis, plot.TopLeft, plot.BottomLeft);
        drawingContext.DrawLine(axis, plot.BottomLeft, plot.BottomRight);
        DrawText(drawingContext, Strings.Chart_FrequencyAxis, 14, ink, plot.Left + plot.Width / 2, ActualHeight - 8, TextAnchor.CenterBottom);

        var yLabel = CreateText(Strings.Chart_LevelAxis, 14, ink);
        drawingContext.PushTransform(new RotateTransform(-90, 18, plot.Top + plot.Height / 2));
        drawingContext.DrawText(yLabel, new Point(18 - yLabel.Width / 2, plot.Top + plot.Height / 2 - yLabel.Height / 2));
        drawingContext.Pop();

        if (points.Length == 0)
        {
            DrawText(drawingContext, Strings.Chart_NoData, 16, muted, plot.Left + plot.Width / 2, plot.Top + plot.Height / 2, TextAnchor.Center);
            return;
        }

        foreach (var curve in curves)
        {
            Point? previousHeardPoint = null;
            foreach (var item in curve.Rows)
            {
                var x = MapFrequency(item.FrequencyHz, plot, minimumFrequencyHz, maximumFrequencyHz);
                if (item.Heard && item.ThresholdAttenuationDbfs is { } threshold)
                {
                    var point = new Point(x, MapLevel((double)threshold, plot, maximumLevelDbfs));
                    if (previousHeardPoint is { } previous)
                        drawingContext.DrawLine(curve.Pen, previous, point);

                    DrawHearingMarker(drawingContext, point, curve.Pen, curve.Ear);
                    previousHeardPoint = point;
                }
                else
                {
                    DrawNoResponseMarker(drawingContext, new Point(x, maximumY), curve.Pen, curve.Ear);
                    previousHeardPoint = null;
                }
            }
        }
    }

    private sealed record Curve(string Label, TestedEar Ear, Pen Pen, IReadOnlyList<HearingThresholdResultRow> Rows);

    private IReadOnlyList<Curve> CreateCurves()
    {
        var series = Series?.OfType<ThresholdChartSeries>().ToArray() ?? [];
        if (series.Length > 0)
            return series.Select(item => new Curve(
                    item.Label,
                    item.Ear,
                    new Pen(new SolidColorBrush(item.Color), 2.2d),
                    item.Rows.OrderBy(row => row.FrequencyHz).ToArray()))
                .ToArray();

        var brush = Ear == TestedEar.Left
            ? FindBrush("EarLeftBrush", Color.FromRgb(31, 90, 166))
            : FindBrush("EarRightBrush", Color.FromRgb(193, 58, 69));
        var rows = ItemsSource?.OfType<HearingThresholdResultRow>().OrderBy(item => item.FrequencyHz).ToArray() ?? [];
        return [new Curve(Ear == TestedEar.Left ? Strings.Common_LeftEar : Strings.Common_RightEar, Ear, new Pen(brush, 2.2d), rows)];
    }

    private static void OnItemsSourceChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs eventArgs)
    {
        var chart = (HearingThresholdChart)dependencyObject;
        if (chart.observedCollection is not null)
            chart.observedCollection.CollectionChanged -= chart.OnCollectionChanged;

        chart.observedCollection = eventArgs.NewValue as INotifyCollectionChanged;
        if (chart.observedCollection is not null)
            chart.observedCollection.CollectionChanged += chart.OnCollectionChanged;
    }

    private void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs eventArgs) => InvalidateVisual();

    private const double LegendRowHeight = 24d;

    private IEnumerable<(Curve? Curve, string Text, double Width)> LegendEntries(IReadOnlyList<Curve> curves, double maximumLevelDbfs)
    {
        foreach (var curve in curves)
            yield return (curve, curve.Label, 22 + CreateText(curve.Label, 14, Brushes.Black).Width + 24);
        var noResponse = string.Format(Strings.Chart_NotHeardUpTo, FormatLevel(maximumLevelDbfs));
        yield return (null, noResponse, 22 + CreateText(noResponse, 14, Brushes.Black).Width);
    }

    private double MeasureLegendHeight(IReadOnlyList<Curve> curves, double maximumLevelDbfs, double availableWidth)
    {
        var rows = 1;
        var x = 0d;
        foreach (var entry in LegendEntries(curves, maximumLevelDbfs))
        {
            if (x > 0 && x + entry.Width > availableWidth)
            {
                rows++;
                x = 0;
            }
            x += entry.Width;
        }
        return rows * LegendRowHeight;
    }

    private void DrawLegend(
        DrawingContext drawingContext,
        IReadOnlyList<Curve> curves,
        Brush ink,
        Brush muted,
        double maximumLevelDbfs,
        double left,
        double right)
    {
        var x = left;
        var y = 12d;
        var noResponsePen = curves.Count == 1 ? curves[0].Pen : new Pen(muted, 2.2d);
        var noResponseEar = curves.Count == 1 ? curves[0].Ear : TestedEar.Right;
        foreach (var entry in LegendEntries(curves, maximumLevelDbfs))
        {
            if (x > left && x + entry.Width > right)
            {
                x = left;
                y += LegendRowHeight;
            }
            if (entry.Curve is { } curve)
            {
                DrawHearingMarker(drawingContext, new Point(x + 6, y), curve.Pen, curve.Ear);
                DrawText(drawingContext, entry.Text, 14, ink, x + 20, y, TextAnchor.LeftCenter);
            }
            else
            {
                DrawNoResponseMarker(drawingContext, new Point(x + 6, y - 2), noResponsePen, noResponseEar);
                DrawText(drawingContext, entry.Text, 14, muted, x + 20, y, TextAnchor.LeftCenter);
            }
            x += entry.Width;
        }
    }

    private static void DrawHearingMarker(DrawingContext drawingContext, Point point, Pen resultPen, TestedEar ear)
    {
        const double radius = 5.5d;
        if (ear == TestedEar.Left)
        {
            drawingContext.DrawLine(resultPen, new Point(point.X - radius, point.Y - radius), new Point(point.X + radius, point.Y + radius));
            drawingContext.DrawLine(resultPen, new Point(point.X - radius, point.Y + radius), new Point(point.X + radius, point.Y - radius));
        }
        else
        {
            drawingContext.DrawEllipse(Brushes.White, resultPen, point, radius, radius);
        }
    }

    private static void DrawNoResponseMarker(DrawingContext drawingContext, Point point, Pen resultPen, TestedEar ear)
    {
        DrawHearingMarker(drawingContext, new Point(point.X, point.Y - 8), resultPen, ear);
        drawingContext.DrawLine(resultPen, new Point(point.X, point.Y - 1), new Point(point.X, point.Y + 10));
        drawingContext.DrawLine(resultPen, new Point(point.X, point.Y + 10), new Point(point.X - 4, point.Y + 5));
        drawingContext.DrawLine(resultPen, new Point(point.X, point.Y + 10), new Point(point.X + 4, point.Y + 5));
    }

    private static double MapFrequency(
        double frequencyHz,
        Rect plot,
        double minimumFrequencyHz,
        double maximumFrequencyHz)
    {
        var minimum = Math.Log2(minimumFrequencyHz);
        var maximum = Math.Log2(maximumFrequencyHz);
        var normalized = (Math.Log2(Math.Clamp(frequencyHz, minimumFrequencyHz, maximumFrequencyHz)) - minimum) / (maximum - minimum);
        return plot.Left + normalized * plot.Width;
    }

    private static double MapLevel(double levelDbfs, Rect plot, double maximumLevelDbfs)
    {
        var normalized = (Math.Clamp(levelDbfs, MinimumLevelDbfs, maximumLevelDbfs) - MinimumLevelDbfs) /
                         (maximumLevelDbfs - MinimumLevelDbfs);
        return plot.Top + normalized * plot.Height;
    }

    private static string FormatLevel(double levelDbfs) =>
        levelDbfs.ToString("0.##", CultureInfo.CurrentCulture).Replace('-', '−');

    private Brush FindBrush(string resourceKey, Color fallback) =>
        TryFindResource(resourceKey) as Brush ?? new SolidColorBrush(fallback);

    private void DrawText(
        DrawingContext drawingContext,
        string text,
        double fontSize,
        Brush brush,
        double x,
        double y,
        TextAnchor anchor)
    {
        var formatted = CreateText(text, fontSize, brush);
        var origin = anchor switch
        {
            TextAnchor.Center => new Point(x - formatted.Width / 2, y - formatted.Height / 2),
            TextAnchor.CenterTop => new Point(x - formatted.Width / 2, y),
            TextAnchor.CenterBottom => new Point(x - formatted.Width / 2, y - formatted.Height),
            TextAnchor.LeftCenter => new Point(x, y - formatted.Height / 2),
            TextAnchor.RightCenter => new Point(x - formatted.Width, y - formatted.Height / 2),
            _ => new Point(x, y)
        };
        drawingContext.DrawText(formatted, origin);
    }

    private FormattedText CreateText(string text, double fontSize, Brush brush) => new(
        text,
        CultureInfo.CurrentUICulture,
        FlowDirection.LeftToRight,
        new Typeface(FontFamily, FontStyle, FontWeight, FontStretch),
        fontSize,
        brush,
        VisualTreeHelper.GetDpi(this).PixelsPerDip);

    private enum TextAnchor
    {
        Center,
        CenterTop,
        CenterBottom,
        LeftCenter,
        RightCenter
    }
}
