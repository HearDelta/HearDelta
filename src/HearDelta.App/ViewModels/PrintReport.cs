using System.Globalization;
using HearDelta.Core;

namespace HearDelta.App.ViewModels;

/// <summary>Druckbarer Bericht aus Ergebnissen und Diagrammen, unabhängig von der Bildschirmdarstellung.</summary>
public sealed record PrintReport(string Title, string Subtitle, IReadOnlyList<PrintBlock> Blocks)
{
    public const string Disclaimer =
        "Persönlicher relativer Vergleich. Pegel sind digitale Absenkungen in dBFS ohne Kupplerkalibrierung, keine dB SPL. " +
        "Kein klinisches Audiogramm, keine validierte Sprachaudiometrie und kein Ersatz für eine medizinische Untersuchung.";

    public static string PrintedAtText(DateTimeOffset printedAt) =>
        $"Gedruckt am {printedAt.ToLocalTime().ToString("dd.MM.yyyy HH:mm", CultureInfo.GetCultureInfo("de-DE"))}";
}

public abstract record PrintBlock;

public sealed record PrintHeading(string Text) : PrintBlock;

public sealed record PrintParagraph(string Text, PrintTextStyle Style = PrintTextStyle.Normal) : PrintBlock;

public enum PrintTextStyle
{
    Normal,
    Muted,
    Warning,
    Emphasis
}

/// <summary>Bezeichnung-Wert-Liste; leere Werte werden nicht gedruckt.</summary>
public sealed record PrintFacts(IReadOnlyList<PrintFact> Items) : PrintBlock;

public sealed record PrintFact(string Label, string? Value);

public sealed record PrintTable(IReadOnlyList<string> Headers, IReadOnlyList<IReadOnlyList<string>> Rows, IReadOnlyList<double>? RelativeWidths = null) : PrintBlock;

/// <summary>Frequenzdiagramm wie in der App: entweder eine Kurve (<see cref="Rows"/>) oder Vergleichskurven (<see cref="Series"/>).</summary>
public sealed record PrintThresholdChart(
    TestedEar Ear,
    IReadOnlyList<HearingThresholdResultRow> Rows,
    IReadOnlyList<ThresholdChartSeries> Series,
    double MaximumAttenuationDbfs) : PrintBlock;
