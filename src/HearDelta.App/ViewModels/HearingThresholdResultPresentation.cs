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
    public static string DefaultName => Strings.Threshold_DefaultName;
    public static string DefaultMaskedName => Strings.Threshold_DefaultMaskedName;

    /// <summary>Vorgabename eines Tests; mit Hörgerät gemessene Altbestände behalten den Gerätenamen.</summary>
    public static string DefaultNameFor(HearingThresholdSession session) =>
        session.IsLegacyWithHearingAid
            ? MeasurementAnnotationRules.DefaultName(session.HearingAid)
            : DefaultNameFor(masked: session.Masking is not null);

    public static string DefaultNameFor(bool masked) => masked ? DefaultMaskedName : DefaultName;

    /// <summary>Kurzbeschreibung der Bedingung: Vertäubung des Gegenohrs bzw. Hörgerät eines Altbestands.</summary>
    public static string ConditionText(HearingThresholdSession session) =>
        session.IsLegacyWithHearingAid
            ? string.Format(Strings.Threshold_LegacyWithAid, session.ProtocolVersion)
            : MaskingText(session.Masking);

    public static string MaskingText(ThresholdMasking? masking) => masking is null
        ? Strings.Threshold_NoMasking
        : string.Format(Strings.Threshold_Masked, FormatDbfs(masking.LevelDbfs));

    public static BadgeTone ConditionTone(HearingThresholdSession session) =>
        session.IsLegacyWithHearingAid ? BadgeTone.Warning : BadgeTone.Neutral;

    /// <summary>
    /// Frequenz in der Oberflächensprache. Die im Tonplan gespeicherte deutsche Beschriftung gehört zum Protokoll und
    /// wird deshalb nicht angezeigt.
    /// </summary>
    public static string FormatFrequency(double frequencyHz) => frequencyHz < 1_000d
        ? $"{frequencyHz.ToString("0.##", System.Globalization.CultureInfo.CurrentCulture)} Hz"
        : $"{(frequencyHz / 1_000d).ToString("0.##", System.Globalization.CultureInfo.CurrentCulture)} kHz";

    public static string FormatDbfs(decimal value) =>
        value.ToString("0.##", System.Globalization.CultureInfo.CurrentCulture).Replace('-', '−');

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
                FormatFrequency(value.Tone.FrequencyHz),
                FormatThreshold(session, value.Observation)))
            .ToArray();

    private static string FormatThreshold(HearingThresholdSession session, HearingThresholdObservation observation) =>
        observation switch
        {
            { ThresholdAttenuationDbfs: { } threshold } => string.Format(Strings.Threshold_ValueDbfs, threshold),
            { Confirmation: not null } =>
                string.Format(Strings.Threshold_NotConfirmed, session.MaximumAttenuationDbfs, observation.Presentation.EndAttenuationDbfs),
            _ => string.Format(Strings.Threshold_NotHeard, session.MaximumAttenuationDbfs)
        };
}
