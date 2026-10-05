using HearDelta.Core;

namespace HearDelta.App.ViewModels;

public sealed record HearingThresholdResultRow(
    double FrequencyHz,
    decimal? ThresholdAttenuationDbfs,
    bool Heard,
    string Frequency,
    string Threshold);

/// <summary>Eine Kurve im Vergleichsdiagramm.</summary>
public sealed record ThresholdChartSeries(string Label, TestedEar Ear, System.Windows.Media.Color Color, IReadOnlyList<HearingThresholdResultRow> Rows);

public static class HearingThresholdResultPresentation
{
    public const string DefaultName = "Hörschwelle";
    public const string DefaultMaskedName = "Hörschwelle mit Vertäubung";

    /// <summary>Vorgabename eines Tests; mit Hörgerät gemessene Altbestände behalten den Gerätenamen.</summary>
    public static string DefaultNameFor(HearingThresholdSession session) =>
        session.IsLegacyWithHearingAid
            ? MeasurementAnnotationRules.DefaultName(session.HearingAid)
            : DefaultNameFor(masked: session.Masking is not null);

    public static string DefaultNameFor(bool masked) => masked ? DefaultMaskedName : DefaultName;

    /// <summary>Kurzbeschreibung der Bedingung: Vertäubung des Gegenohrs bzw. Hörgerät eines Altbestands.</summary>
    public static string ConditionText(HearingThresholdSession session) =>
        session.IsLegacyWithHearingAid
            ? $"Mit Hörgerät (älteres Protokoll v{session.ProtocolVersion})"
            : MaskingText(session.Masking);

    public static string MaskingText(ThresholdMasking? masking) => masking is null
        ? "Ohne Vertäubung"
        : $"Gegenohr vertäubt · {FormatDbfs(masking.LevelDbfs)} dBFS";

    public static BadgeTone ConditionTone(HearingThresholdSession session) =>
        session.IsLegacyWithHearingAid ? BadgeTone.Warning : BadgeTone.Neutral;

    public static string FormatDbfs(decimal value) =>
        value.ToString("0.##", System.Globalization.CultureInfo.GetCultureInfo("de-DE")).Replace('-', '−');

    public static IReadOnlyList<HearingThresholdResultRow> CreateRows(HearingThresholdSession session) =>
        session.Observations
            .Select(observation => new
            {
                Observation = observation,
                Tone = session.Tones.Single(tone => tone.PresentationOrder == observation.PresentationOrder)
            })
            .OrderBy(value => value.Tone.FrequencyHz)
            .Select(value => new HearingThresholdResultRow(
                value.Tone.FrequencyHz,
                value.Observation.ThresholdAttenuationDbfs,
                value.Observation.Heard,
                value.Tone.DisplayLabel ?? $"{value.Tone.FrequencyHz:0.#} Hz",
                FormatThreshold(session, value.Observation)))
            .ToArray();

    private static string FormatThreshold(HearingThresholdSession session, HearingThresholdObservation observation) =>
        observation switch
        {
            { ThresholdAttenuationDbfs: { } threshold } => $"{threshold:0.##} dBFS",
            { Confirmation: not null } =>
                $"nicht bestätigt bis {session.MaximumAttenuationDbfs:0.##} dBFS (erste Reaktion bei {observation.Presentation.EndAttenuationDbfs:0.##} dBFS)",
            _ => $"nicht gehört bis {session.MaximumAttenuationDbfs:0.##} dBFS"
        };
}
