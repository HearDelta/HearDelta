using System.Globalization;
using HearDelta.App.Services;
using HearDelta.Core;

namespace HearDelta.App.ViewModels;

/// <summary>Kennwerte eines Worttest-Ergebnisses so, wie sie die Ergebnisseite anzeigt.</summary>
public sealed record WordResultSummary(
    string WithoutResult,
    string WithoutDetail,
    string WithResult,
    string WithDetail,
    string DifferenceLabel,
    string DifferenceText,
    string Explanation);

/// <summary>Stellt die druckbaren Berichte für Ergebnisse und Diagramme zusammen.</summary>
public static class ResultReports
{
    private static readonly CultureInfo German = CultureInfo.GetCultureInfo("de-DE");

    /// <summary>Druckt und liefert die neue Statusmeldung; ein abgebrochener Druckdialog lässt sie unverändert.</summary>
    public static string Print(IReportPrinter? printer, PrintReport report, string currentStatus)
    {
        if (printer is null)
            return currentStatus;
        try
        {
            return printer.Print(report)
                ? $"„{report.Title}“ an den Drucker übergeben."
                : currentStatus;
        }
        catch (Exception exception)
        {
            return $"Drucken fehlgeschlagen: {exception.Message}";
        }
    }

    public static PrintReport HearingThreshold(
        HearingThresholdHistoryItem item,
        IReadOnlyList<HearingThresholdResultRow> rows,
        string? personName,
        DateTimeOffset printedAt)
    {
        var session = item.Session;
        var blocks = new List<PrintBlock>
        {
            new PrintFacts(
            [
                new("Person", personName),
                new("Kommentar", item.Comment),
                new("Ohr", item.EarText),
                new("Vertäubung", session.IsLegacyWithHearingAid ? null : item.ConditionText),
                new("Hörgerät", session.IsLegacyWithHearingAid ? $"{session.HearingAid?.DisplayName} (älteres Protokoll)" : null),
                new("Status", item.StatusText),
                new("Ergebnis", item.ResultText),
                new("Ablauf", $"Protokoll v{session.ProtocolVersion} · {FormatToneOrder(session.ToneOrder)} · Pegelobergrenze {FormatDb(session.MaximumAttenuationDbfs)} dBFS")
            ]),
            new PrintThresholdChart(session.Ear, rows, [], item.MaximumAttenuationDbfs),
            new PrintHeading("Hörschwellen je Frequenz"),
            new PrintTable(["Frequenz", "Hörschwelle"], rows.Select(row => (IReadOnlyList<string>)[row.Frequency, row.Threshold]).ToArray(), [1d, 3d]),
            new PrintHeading("Messaufbau"),
            new PrintFacts(HardwareFacts(session.Hardware))
        };
        return new PrintReport(item.Name, $"Hörschwellentest vom {item.StartedAtText}", WithFooter(blocks, printedAt));
    }

    public static PrintReport ThresholdComparison(
        IReadOnlyList<HearingThresholdHistoryItem> tests,
        IReadOnlyList<ThresholdChartSeries> series,
        double maximumAttenuationDbfs,
        string warningText,
        IReadOnlyList<ThresholdComparisonRowItem> differenceRows,
        string? personName,
        DateTimeOffset printedAt)
    {
        var blocks = new List<PrintBlock>();
        if (personName is not null)
            blocks.Add(new PrintFacts([new("Person", personName)]));
        blocks.Add(new PrintTable(
            ["Test", "Name", "Bedingung", "Messprofil", "Status"],
            tests.Select((item, index) => (IReadOnlyList<string>)
            [
                series.ElementAtOrDefault(index)?.Label ?? $"{index + 1}",
                string.IsNullOrWhiteSpace(item.Comment) ? item.Name : $"{item.Name} – {item.Comment}",
                item.ConditionText,
                item.ProfileText,
                item.StatusText
            ]).ToArray(),
            [2.2d, 2d, 1.4d, 1.2d, 1d]));
        if (!string.IsNullOrWhiteSpace(warningText))
            blocks.Add(new PrintParagraph($"Unterschiedlicher Messaufbau: {warningText}", PrintTextStyle.Warning));
        blocks.Add(new PrintThresholdChart(TestedEar.Left, [], series, maximumAttenuationDbfs));
        blocks.Add(new PrintParagraph("Ohr als Symbol wie im Audiogramm: X links, O rechts. Farben unterscheiden die Tests.", PrintTextStyle.Muted));

        blocks.Add(new PrintHeading("Hörschwellen je Frequenz"));
        if (differenceRows.Count > 0)
        {
            blocks.Add(new PrintTable(
                ["Frequenz", "Test 1", "Test 2", "Test 2 gegenüber Test 1"],
                differenceRows.Select(row => (IReadOnlyList<string>)[row.FrequencyText, row.FirstText, row.SecondText, row.DifferenceText]).ToArray(),
                [1d, 1.4d, 1.4d, 1.8d]));
            blocks.Add(new PrintParagraph("„Besser“ heißt: Test 2 hat einen leiseren Ton gehört.", PrintTextStyle.Muted));
        }
        else
        {
            var frequencies = series.SelectMany(curve => curve.Rows).Select(row => row.FrequencyHz).Distinct().Order().ToArray();
            blocks.Add(new PrintTable(
                ["Frequenz", .. series.Select((_, index) => $"Test {index + 1}")],
                frequencies.Select(frequency => (IReadOnlyList<string>)
                [
                    series.SelectMany(curve => curve.Rows).First(row => row.FrequencyHz == frequency).Frequency,
                    .. series.Select(curve => curve.Rows.FirstOrDefault(row => row.FrequencyHz == frequency)?.Threshold ?? "nicht geprüft")
                ]).ToArray()));
        }
        return new PrintReport("Vergleich der Hörschwellen", $"{tests.Count} Hörschwellentests", WithFooter(blocks, printedAt));
    }

    public static PrintReport WordResult(
        string title,
        HistorySessionItemViewModel item,
        WordResultSummary summary,
        IReadOnlyList<PhonemeConfusionCell> confusions,
        string? personName,
        DateTimeOffset printedAt)
    {
        var blocks = new List<PrintBlock>
        {
            new PrintFacts([.. WordSessionFacts(item, personName), new("Status", item.StatusText)]),
            new PrintHeading("Ergebnis"),
            new PrintTable(
                ["Bedingung", "Ergebnis", "Einzelheiten"],
                [
                    ["Ohne Hörgerät", summary.WithoutResult, summary.WithoutDetail],
                    ["Mit Hörgerät", summary.WithResult, summary.WithDetail],
                    [summary.DifferenceLabel, summary.DifferenceText, string.Empty]
                ],
                [1.6d, 1.2d, 2.4d]),
            new PrintParagraph(summary.Explanation, PrintTextStyle.Muted)
        };
        if (confusions.Count > 0)
        {
            blocks.Add(new PrintHeading("Verwechslungen im Phonemtest"));
            blocks.Add(new PrintTable(
                ["Bedingung", "Position", "Gruppe", "Ziel", "Auswahl", "n"],
                confusions.Select(cell => (IReadOnlyList<string>)
                [
                    cell.Condition == HearingAidCondition.WithHearingAid ? "mit Hörgerät" : "ohne Hörgerät",
                    cell.ContrastPosition,
                    cell.ContrastGroupId,
                    cell.TargetResponse,
                    cell.SelectedResponse,
                    cell.Count.ToString(German)
                ]).ToArray(),
                [1.2d, 1d, 1.1d, 1d, 1.2d, 0.4d]));
            blocks.Add(new PrintParagraph("Fallzahlen je Ziel und Auswahl. Eine Beschreibung der Antworten, keine Frequenzdiagnose oder Verstärkungsempfehlung.", PrintTextStyle.Muted));
        }
        blocks.Add(new PrintHeading("Messaufbau"));
        blocks.Add(new PrintFacts(HardwareFacts(item.Session.Hardware)));
        return new PrintReport(item.Name, $"{title} · {item.StartedAtText}", WithFooter(blocks, printedAt));
    }

    public static PrintReport WordComparison(
        HistorySessionItemViewModel first,
        HistorySessionItemViewModel second,
        bool isDirectlyComparable,
        string assessmentText,
        string? personName,
        DateTimeOffset printedAt)
    {
        IReadOnlyList<string> Row(string label, Func<HistorySessionItemViewModel, string?> value) =>
            [label, value(first) ?? string.Empty, value(second) ?? string.Empty];

        var blocks = new List<PrintBlock>();
        if (personName is not null)
            blocks.Add(new PrintFacts([new("Person", personName)]));
        blocks.Add(new PrintParagraph(
            isDirectlyComparable ? assessmentText : $"Nicht direkt vergleichbar: {assessmentText}",
            isDirectlyComparable ? PrintTextStyle.Normal : PrintTextStyle.Warning));
        blocks.Add(new PrintTable(
            ["", "Messung 1", "Messung 2"],
            [
                Row("Name", item => item.Name),
                Row("Kommentar", item => item.Comment),
                Row("Datum · Ohr · Umgebung", item => item.ComparisonSubtitle),
                Row("Material", item => item.MaterialEnvironmentText),
                Row("Hörgerät", item => item.HearingAidText),
                Row("Ohne Hörgerät", item => item.WithoutEstimateText),
                Row("Mit Hörgerät", item => item.WithEstimateText),
                Row(first.DifferenceCaption, item => item.DifferenceEstimateText),
                Row("Messprofil", item => item.HardwareText)
            ],
            [1.3d, 2d, 2d]));
        blocks.Add(new PrintParagraph(
            "Prozentwerte: 95-%-Intervalle der jeweils 25 Antworten. Adaptive Zahlentests: Schwelle für 50 % richtig mit Streuung der gemittelten Werte. Beides ist eine deskriptive Orientierung, kein Nachweis eines Geräteunterschieds.",
            PrintTextStyle.Muted));
        return new PrintReport("Vergleich zweier Worttests", $"{first.StartedAtText} und {second.StartedAtText}", WithFooter(blocks, printedAt));
    }

    private static IEnumerable<PrintFact> WordSessionFacts(HistorySessionItemViewModel item, string? personName) =>
    [
        new("Person", personName),
        new("Kommentar", item.Comment),
        new("Ohr", item.Session.Ear == TestedEar.Left ? "Linkes Ohr" : "Rechtes Ohr"),
        new("Hörgerät", item.HearingAidText),
        new("Material", item.MaterialEnvironmentText),
        new("Katalog", item.Session.MaterialIdentity.CatalogId)
    ];

    private static IReadOnlyList<PrintFact> HardwareFacts(MeasurementHardwareSnapshot hardware) =>
    [
        new("Messprofil", hardware.ProfileName),
        new("Audioausgang", $"{hardware.EndpointName} · {hardware.SampleRate.ToString(German)} Hz · {hardware.BitsPerSample} bit"),
        new("Kopfhörer", JoinNonEmpty(
            $"{hardware.HeadphoneManufacturer} {hardware.HeadphoneModel}".Trim(),
            hardware.HeadphoneDesign,
            hardware.HeadphoneImpedanceOhms is { } ohms ? $"{ohms} Ω" : null)),
        new("Verstärker", JoinNonEmpty(hardware.AmplifierOutput, string.IsNullOrWhiteSpace(hardware.Gain) ? null : $"Gain {hardware.Gain}")),
        new("Lautstärke", $"{FormatDb(hardware.StartVolumeDb)} dB · Obergrenze {FormatDb(hardware.MaximumVolumeDb)} dB (digitale Absenkung)"),
        new("Kopfhörerentzerrung", hardware.HeadphoneEqualization?.Label ?? "keine")
    ];

    private static IReadOnlyList<PrintBlock> WithFooter(List<PrintBlock> blocks, DateTimeOffset printedAt)
    {
        blocks.Add(new PrintParagraph($"{PrintReport.PrintedAtText(printedAt)} · {PrintReport.Disclaimer}", PrintTextStyle.Muted));
        return blocks;
    }

    private static string JoinNonEmpty(params string?[] values) =>
        string.Join(" · ", values.Where(value => !string.IsNullOrWhiteSpace(value)));

    private static string FormatDb(decimal value) => value.ToString("0.##", German).Replace('-', '−');

    private static string FormatToneOrder(ThresholdToneOrder order) => order switch
    {
        ThresholdToneOrder.Random => "zufällige Reihenfolge nach 500 Hz",
        _ => "vom 500-Hz-Zentrum nach außen"
    };
}
