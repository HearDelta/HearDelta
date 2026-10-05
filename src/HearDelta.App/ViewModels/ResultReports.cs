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
    /// <summary>Druckt und liefert die neue Statusmeldung; ein abgebrochener Druckdialog lässt sie unverändert.</summary>
    public static string Print(IReportPrinter? printer, PrintReport report, string currentStatus)
    {
        if (printer is null)
            return currentStatus;
        try
        {
            return printer.Print(report)
                ? string.Format(Strings.Report_SentToPrinter, report.Title)
                : currentStatus;
        }
        catch (Exception exception)
        {
            return string.Format(Strings.Report_PrintFailed, exception.Message);
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
                new(Strings.Report_Person, personName),
                new(Strings.Report_Comment, item.Comment),
                new(Strings.Report_Ear, item.EarText),
                new(Strings.Report_Masking, session.IsLegacyWithHearingAid ? null : item.ConditionText),
                new(Strings.Report_Aid, session.IsLegacyWithHearingAid ? string.Format(Strings.Report_LegacyAid, session.HearingAid?.DisplayName) : null),
                new(Strings.Report_Status, item.StatusText),
                new(Strings.Report_Result, item.ResultText),
                new(Strings.Report_Procedure, string.Format(
                    Strings.Report_ThresholdProcedure, session.ProtocolVersion, FormatToneOrder(session.ToneOrder), FormatDb(session.MaximumAttenuationDbfs)))
            ]),
            new PrintThresholdChart(session.Ear, rows, [], item.MaximumAttenuationDbfs),
            new PrintHeading(Strings.Report_ThresholdsPerFrequency),
            new PrintTable([Strings.Report_Frequency, Strings.Report_Threshold], rows.Select(row => (IReadOnlyList<string>)[row.Frequency, row.Threshold]).ToArray(), [1d, 3d]),
            new PrintHeading(Strings.Report_Setup),
            new PrintFacts(HardwareFacts(session.Hardware))
        };
        return new PrintReport(item.Name, string.Format(Strings.History_ThresholdTestFrom, item.StartedAtText), WithFooter(blocks, printedAt));
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
            blocks.Add(new PrintFacts([new(Strings.Report_Person, personName)]));
        blocks.Add(new PrintTable(
            [Strings.Report_Test, Strings.Report_Name, Strings.Report_Condition, Strings.Report_Profile, Strings.Report_Status],
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
            blocks.Add(new PrintParagraph(string.Format(Strings.Report_DifferentSetup, warningText), PrintTextStyle.Warning));
        blocks.Add(new PrintThresholdChart(TestedEar.Left, [], series, maximumAttenuationDbfs));
        blocks.Add(new PrintParagraph(Strings.History_SymbolNote, PrintTextStyle.Muted));

        blocks.Add(new PrintHeading(Strings.Report_ThresholdsPerFrequency));
        if (differenceRows.Count > 0)
        {
            blocks.Add(new PrintTable(
                [Strings.Report_Frequency, Strings.History_ColTest1, Strings.History_ColTest2, Strings.History_ColTest2VsTest1],
                differenceRows.Select(row => (IReadOnlyList<string>)[row.FrequencyText, row.FirstText, row.SecondText, row.DifferenceText]).ToArray(),
                [1d, 1.4d, 1.4d, 1.8d]));
            blocks.Add(new PrintParagraph(Strings.Report_BetterNote, PrintTextStyle.Muted));
        }
        else
        {
            var frequencies = series.SelectMany(curve => curve.Rows).Select(row => row.FrequencyHz).Distinct().Order().ToArray();
            blocks.Add(new PrintTable(
                [Strings.Report_Frequency, .. series.Select((_, index) => string.Format(Strings.Report_TestN, index + 1))],
                frequencies.Select(frequency => (IReadOnlyList<string>)
                [
                    series.SelectMany(curve => curve.Rows).First(row => row.FrequencyHz == frequency).Frequency,
                    .. series.Select(curve => curve.Rows.FirstOrDefault(row => row.FrequencyHz == frequency)?.Threshold ?? Strings.History_NotTested)
                ]).ToArray()));
        }
        return new PrintReport(Strings.History_ThresholdComparison, string.Format(Strings.Report_ThresholdTestCount, tests.Count), WithFooter(blocks, printedAt));
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
            new PrintFacts([.. WordSessionFacts(item, personName), new(Strings.Report_Status, item.StatusText)]),
            new PrintHeading(Strings.Report_Result),
            new PrintTable(
                [Strings.Report_Condition, Strings.Report_Result, Strings.Report_Details],
                [
                    [Strings.Common_WithoutAid, summary.WithoutResult, summary.WithoutDetail],
                    [Strings.Common_WithAid, summary.WithResult, summary.WithDetail],
                    [summary.DifferenceLabel, summary.DifferenceText, string.Empty]
                ],
                [1.6d, 1.2d, 2.4d]),
            new PrintParagraph(summary.Explanation, PrintTextStyle.Muted)
        };
        if (confusions.Count > 0)
        {
            blocks.Add(new PrintHeading(Strings.Measure_Confusions));
            blocks.Add(new PrintTable(
                [Strings.Measure_ColCondition, Strings.Measure_ColPosition, Strings.Measure_ColGroup, Strings.Measure_ColTarget, Strings.Measure_ColChoice, "n"],
                confusions.Select(cell => (IReadOnlyList<string>)
                [
                    Controls.HearingAidConditionLabelConverter.Label(cell.Condition),
                    cell.ContrastPosition,
                    cell.ContrastGroupId,
                    cell.TargetResponse,
                    MeasurementViewModel.ConfusionResponseLabel(cell.SelectedResponse),
                    cell.Count.ToString(CultureInfo.CurrentCulture)
                ]).ToArray(),
                [1.2d, 1d, 1.1d, 1d, 1.2d, 0.4d]));
            blocks.Add(new PrintParagraph(Strings.Measure_ConfusionsIntro, PrintTextStyle.Muted));
        }
        blocks.Add(new PrintHeading(Strings.Report_Setup));
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
            blocks.Add(new PrintFacts([new(Strings.Report_Person, personName)]));
        blocks.Add(new PrintParagraph(
            isDirectlyComparable ? assessmentText : string.Format(Strings.Report_NotDirectlyComparable, assessmentText),
            isDirectlyComparable ? PrintTextStyle.Normal : PrintTextStyle.Warning));
        blocks.Add(new PrintTable(
            ["", Strings.Report_Measurement1, Strings.Report_Measurement2],
            [
                Row(Strings.Report_Name, item => item.Name),
                Row(Strings.Report_Comment, item => item.Comment),
                Row(Strings.Report_DateEarEnvironment, item => item.ComparisonSubtitle),
                Row(Strings.Report_Material, item => item.MaterialEnvironmentText),
                Row(Strings.Report_Aid, item => item.HearingAidText),
                Row(Strings.Common_WithoutAid, item => item.WithoutEstimateText),
                Row(Strings.Common_WithAid, item => item.WithEstimateText),
                Row(first.DifferenceCaption, item => item.DifferenceEstimateText),
                Row(Strings.Report_Profile, item => item.HardwareText)
            ],
            [1.3d, 2d, 2d]));
        blocks.Add(new PrintParagraph(
            Strings.History_IntervalNote,
            PrintTextStyle.Muted));
        return new PrintReport(Strings.Report_WordComparisonTitle, string.Format(Strings.Report_AndDates, first.StartedAtText, second.StartedAtText), WithFooter(blocks, printedAt));
    }

    private static IEnumerable<PrintFact> WordSessionFacts(HistorySessionItemViewModel item, string? personName) =>
    [
        new(Strings.Report_Person, personName),
        new(Strings.Report_Comment, item.Comment),
        new(Strings.Report_Ear, item.Session.Ear == TestedEar.Left ? Strings.Common_LeftEar : Strings.Common_RightEar),
        new(Strings.Report_Aid, item.HearingAidText),
        new(Strings.Report_Material, item.MaterialEnvironmentText),
        new(Strings.Report_Catalog, item.Session.MaterialIdentity.CatalogId)
    ];

    private static IReadOnlyList<PrintFact> HardwareFacts(MeasurementHardwareSnapshot hardware) =>
    [
        new(Strings.Report_Profile, hardware.ProfileName),
        new(Strings.Report_AudioOutput, string.Format(Strings.Report_AudioOutputValue, hardware.EndpointName, hardware.SampleRate, hardware.BitsPerSample)),
        new(Strings.Report_Headphones, JoinNonEmpty(
            $"{hardware.HeadphoneManufacturer} {hardware.HeadphoneModel}".Trim(),
            string.IsNullOrWhiteSpace(hardware.HeadphoneDesign) ? null : SettingsViewModel.DesignLabel(hardware.HeadphoneDesign),
            hardware.HeadphoneImpedanceOhms is { } ohms ? $"{ohms} Ω" : null)),
        new(Strings.Report_Amplifier, JoinNonEmpty(hardware.AmplifierOutput, string.IsNullOrWhiteSpace(hardware.Gain) ? null : string.Format(Strings.Report_Gain, hardware.Gain))),
        new(Strings.Report_Volume, string.Format(Strings.Report_VolumeValue, FormatDb(hardware.StartVolumeDb), FormatDb(hardware.MaximumVolumeDb))),
        new(Strings.Report_Equalization, hardware.HeadphoneEqualization?.Label ?? Strings.Report_NoEqualization)
    ];

    private static IReadOnlyList<PrintBlock> WithFooter(List<PrintBlock> blocks, DateTimeOffset printedAt)
    {
        blocks.Add(new PrintParagraph($"{PrintReport.PrintedAtText(printedAt)} · {PrintReport.Disclaimer}", PrintTextStyle.Muted));
        return blocks;
    }

    private static string JoinNonEmpty(params string?[] values) =>
        string.Join(" · ", values.Where(value => !string.IsNullOrWhiteSpace(value)));

    private static string FormatDb(decimal value) => value.ToString("0.##", CultureInfo.CurrentCulture).Replace('-', '−');

    private static string FormatToneOrder(ThresholdToneOrder order) => order switch
    {
        ThresholdToneOrder.Random => Strings.History_OrderRandom,
        _ => Strings.History_OrderCenterOut
    };
}
