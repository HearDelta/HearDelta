using System.Globalization;
using HearDelta.Core;

namespace HearDelta.App.ViewModels;

/// <summary>Einheitliche Texte für Ergebnisse des adaptiven Zahlentests.</summary>
public static class AdaptiveResultText
{
    public static string Threshold(AdaptiveTrackResult? result, AdaptiveTrackParameter? parameter) =>
        result?.ThresholdDb is { } threshold
            ? $"{Signed(threshold)} dB{(parameter == AdaptiveTrackParameter.SignalToNoiseRatio ? " SNR" : string.Empty)}"
            : "–";

    public static string ThresholdWithSpread(AdaptiveTrackResult? result, AdaptiveTrackParameter? parameter) =>
        result?.StandardDeviationDb is { } spread
            ? string.Format(Strings.Adaptive_Spread, Threshold(result, parameter), spread)
            : Threshold(result, parameter);

    /// <summary>Positiv heißt: mit Hörgerät genügt ein leiserer Pegel bzw. ein ungünstigerer SNR.</summary>
    public static string Improvement(decimal? improvementDb) => improvementDb switch
    {
        null => "–",
        > 0 => string.Format(Strings.Adaptive_Better, improvementDb.Value),
        < 0 => string.Format(Strings.Adaptive_Worse, -improvementDb.Value),
        _ => Strings.History_Same
    };

    public static string ShortImprovement(decimal? improvementDb) =>
        improvementDb is { } value ? $"{Signed(value)} dB" : "–";

    public static string ParameterLabel(AdaptiveTrackParameter? parameter) =>
        parameter == AdaptiveTrackParameter.SignalToNoiseRatio
            ? Strings.Adaptive_SnrLabel
            : Strings.Adaptive_LevelLabel;

    public static string Detail(MeasurementBlockResult block, AdaptiveTrackParameter? parameter)
    {
        if (block.TotalResponses == 0)
            return Strings.Measure_NoAnswerYet;
        var counts = string.Format(Strings.Adaptive_Counts, block.CorrectResponses, block.TotalResponses);
        return block.Adaptive is { ThresholdDb: not null, Note: null }
            ? $"{ParameterLabel(parameter)} · {counts}"
            : $"{block.Adaptive?.Note ?? ParameterLabel(parameter)} · {counts}";
    }

    public static string Explanation => Strings.Adaptive_Explanation;

    private static string Signed(decimal value) => value.ToString("+0.0;-0.0;0.0", CultureInfo.CurrentCulture);
}
