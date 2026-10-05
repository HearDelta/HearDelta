using System.Globalization;
using HearDelta.Core;

namespace HearDelta.App.ViewModels;

/// <summary>Einheitliche Texte für Ergebnisse des adaptiven Zahlentests.</summary>
public static class AdaptiveResultText
{
    private static readonly CultureInfo German = CultureInfo.GetCultureInfo("de-DE");

    public static string Threshold(AdaptiveTrackResult? result, AdaptiveTrackParameter? parameter) =>
        result?.ThresholdDb is { } threshold
            ? $"{Signed(threshold)} dB{(parameter == AdaptiveTrackParameter.SignalToNoiseRatio ? " SNR" : string.Empty)}"
            : "–";

    public static string ThresholdWithSpread(AdaptiveTrackResult? result, AdaptiveTrackParameter? parameter) =>
        result?.StandardDeviationDb is { } spread
            ? $"{Threshold(result, parameter)}  ·  Streuung ±{spread.ToString("0.0", German)} dB"
            : Threshold(result, parameter);

    /// <summary>Positiv heißt: mit Hörgerät genügt ein leiserer Pegel bzw. ein ungünstigerer SNR.</summary>
    public static string Improvement(decimal? improvementDb) => improvementDb switch
    {
        null => "–",
        > 0 => $"{improvementDb.Value.ToString("0.0", German)} dB besser",
        < 0 => $"{(-improvementDb.Value).ToString("0.0", German)} dB schlechter",
        _ => "gleich"
    };

    public static string ShortImprovement(decimal? improvementDb) =>
        improvementDb is { } value ? $"{Signed(value)} dB" : "–";

    public static string ParameterLabel(AdaptiveTrackParameter? parameter) =>
        parameter == AdaptiveTrackParameter.SignalToNoiseRatio
            ? "Signal-Rausch-Abstand für 50 % richtig"
            : "Digitaler Sprachpegel für 50 % richtig";

    public static string Detail(MeasurementBlockResult block, AdaptiveTrackParameter? parameter)
    {
        if (block.TotalResponses == 0)
            return "Noch keine Antwort erfasst";
        var counts = $"{block.CorrectResponses} von {block.TotalResponses} richtig";
        return block.Adaptive is { ThresholdDb: not null, Note: null }
            ? $"{ParameterLabel(parameter)} · {counts}"
            : $"{block.Adaptive?.Note ?? ParameterLabel(parameter)} · {counts}";
    }

    public const string Explanation =
        "Adaptiv (1-hoch/1-runter): Nach einer richtigen Antwort wird es um eine Stufe schwerer, nach einer falschen leichter – " +
        "6 dB bis zur ersten falschen Antwort, danach 2 dB. Die Schwelle ist der Mittelwert der Werte ab der ersten falschen Antwort " +
        "und entspricht etwa 50 % richtig. Pegel sind digitale dB, keine dB SPL.";

    private static string Signed(decimal value) => value.ToString("+0.0;-0.0;0.0", German);
}
