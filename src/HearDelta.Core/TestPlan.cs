namespace HearDelta.Core;

/// <summary>Schritte des empfohlenen Testablaufs je Ohr. Die Reihenfolge ist eine Empfehlung, keine Sperre.</summary>
public enum TestPlanStep
{
    HearingThreshold = 1,
    NumbersQuiet = 2,
    NumbersNoise = 3,
    PhonemesQuiet = 4,
    PhonemesNoise = 5
}

/// <summary>
/// Ergebnisse der Vortests eines Ohrs, jeweils ohne Hörgerät und nur aus Messungen mit kompatiblem Messaufbau.
/// Pegel sind digitale dB (Absenkung bzw. dBFS), keine dB SPL.
/// </summary>
public sealed record PretestResults(
    decimal? ToneAverageThresholdDbfs = null,
    DateTimeOffset? ToneTestedAt = null,
    decimal? QuietSpeechThresholdDb = null,
    DateTimeOffset? QuietTestedAt = null,
    decimal? NoiseSnrThresholdDb = null,
    DateTimeOffset? NoiseTestedAt = null)
{
    public static readonly PretestResults None = new();
}

/// <summary>Vorgeschlagene Sprachlautstärke und SNR; <c>null</c> heißt ohne Grundlage, die bisherige Einstellung bleibt.</summary>
public sealed record LevelRecommendation(
    decimal? SpeechLevelDb,
    decimal? SignalToNoiseRatioDb,
    string Source);

/// <summary>
/// Persönliche Faustregeln für die Pegelwahl. Ziel ist, jeden Test im empfindlichen Bereich zwischen „nichts“ und
/// „alles verstanden“ zu beginnen. Die Abstände sind Startwerte für einen relativen Vergleich, keine Normwerte.
/// </summary>
public static class TestLevelRules
{
    /// <summary>Sprachschwelle in Ruhe liegt erfahrungsgemäß etwa so weit über dem Tonmittel 0,5–2 kHz (beides digital).</summary>
    public const decimal ToneToSpeechThresholdOffsetDb = 30m;

    /// <summary>Start des adaptiven Ruhetests über der geschätzten bzw. gemessenen Ruheschwelle: deutlich hörbar.</summary>
    public const decimal QuietAdaptiveStartAboveThresholdDb = 12m;

    /// <summary>Sprachlautstärke im Störgeräusch über der Ruheschwelle, damit Sprache und Rauschen klar hörbar sind.</summary>
    public const decimal NoiseSpeechAboveQuietThresholdDb = 25m;

    /// <summary>Start-SNR des adaptiven Störgeräuschtests über der gemessenen SNR-Schwelle bzw. ohne Messung.</summary>
    public const decimal NoiseAdaptiveStartAboveSnrThresholdDb = 8m;
    public const decimal DefaultNoiseStartSnrDb = 0m;

    /// <summary>Fester Pegel bzw. SNR für Tests mit Prozentergebnis: etwas über der 50-%-Schwelle.</summary>
    public const decimal NumbersQuietFixedAboveThresholdDb = 3m;
    public const decimal NumbersNoiseFixedAboveSnrThresholdDb = 2m;
    public const decimal PhonemesQuietAboveThresholdDb = 10m;
    public const decimal PhonemesNoiseAboveSnrThresholdDb = 6m;

    public static (SpeechMaterial Material, ListeningEnvironment Environment, bool Adaptive) Configuration(TestPlanStep step) => step switch
    {
        TestPlanStep.NumbersQuiet => (SpeechMaterial.Numbers, ListeningEnvironment.Quiet, true),
        TestPlanStep.NumbersNoise => (SpeechMaterial.Numbers, ListeningEnvironment.BackgroundNoise, true),
        TestPlanStep.PhonemesQuiet => (SpeechMaterial.PhonemeContrasts, ListeningEnvironment.Quiet, false),
        TestPlanStep.PhonemesNoise => (SpeechMaterial.PhonemeContrasts, ListeningEnvironment.BackgroundNoise, false),
        _ => throw new ArgumentOutOfRangeException(nameof(step), "Der Hörschwellentest ist kein Worttest.")
    };

    public static LevelRecommendation Recommend(
        SpeechMaterial material,
        ListeningEnvironment environment,
        bool adaptive,
        PretestResults pretests)
    {
        ArgumentNullException.ThrowIfNull(pretests);
        var quiet = QuietThreshold(pretests);
        if (environment == ListeningEnvironment.Quiet)
        {
            if (quiet is null)
                return new LevelRecommendation(null, null, CoreStrings.Level_NoPretest);
            var offset = material == SpeechMaterial.Numbers
                ? adaptive ? QuietAdaptiveStartAboveThresholdDb : NumbersQuietFixedAboveThresholdDb
                : PhonemesQuietAboveThresholdDb;
            return new LevelRecommendation(Round(quiet.Value.Db + offset), null, string.Format(CoreStrings.Level_SourceOffset, quiet.Value.Source, Signed(offset)));
        }

        var speech = quiet is null ? (decimal?)null : Round(quiet.Value.Db + NoiseSpeechAboveQuietThresholdDb);
        var speechSource = quiet is null
            ? CoreStrings.Level_NoQuietPretest
            : string.Format(CoreStrings.Level_SpeechSource, quiet.Value.Source, Signed(NoiseSpeechAboveQuietThresholdDb));
        decimal? snr;
        string snrSource;
        if (pretests.NoiseSnrThresholdDb is { } snrThreshold)
        {
            var offset = material == SpeechMaterial.Numbers
                ? adaptive ? NoiseAdaptiveStartAboveSnrThresholdDb : NumbersNoiseFixedAboveSnrThresholdDb
                : PhonemesNoiseAboveSnrThresholdDb;
            snr = Round(snrThreshold + offset);
            snrSource = string.Format(CoreStrings.Level_SnrSource, Signed(snrThreshold), Date(pretests.NoiseTestedAt), Signed(offset));
        }
        else if (material == SpeechMaterial.Numbers && adaptive)
        {
            snr = DefaultNoiseStartSnrDb;
            snrSource = CoreStrings.Level_NoNoisePretestStart;
        }
        else
        {
            snr = null;
            snrSource = CoreStrings.Level_NoNoisePretest;
        }
        return new LevelRecommendation(speech, snr, string.Format(CoreStrings.Level_Combined, speechSource, snrSource));
    }

    /// <summary>Gemessene Ruheschwelle, sonst Schätzung aus dem Tonmittel der Hörschwelle.</summary>
    public static (decimal Db, string Source)? QuietThreshold(PretestResults pretests) =>
        pretests.QuietSpeechThresholdDb is { } measured
            ? (measured, string.Format(CoreStrings.Level_QuietThreshold, measured, Date(pretests.QuietTestedAt)))
            : pretests.ToneAverageThresholdDbfs is { } tone
                ? (tone + ToneToSpeechThresholdOffsetDb,
                    string.Format(
                        CoreStrings.Level_EstimatedQuietThreshold,
                        tone + ToneToSpeechThresholdOffsetDb,
                        tone,
                        Date(pretests.ToneTestedAt),
                        Signed(ToneToSpeechThresholdOffsetDb)))
                : null;

    /// <summary>Mittel der gehörten Tonschwellen zwischen 400 Hz und 2,5 kHz; mindestens zwei Töne.</summary>
    public static decimal? ToneAverage(IEnumerable<(double FrequencyHz, decimal? ThresholdDbfs)> tones)
    {
        var values = tones
            .Where(tone => tone.FrequencyHz is >= 400 and <= 2_500 && tone.ThresholdDbfs is not null)
            .Select(tone => tone.ThresholdDbfs!.Value)
            .ToArray();
        return values.Length >= 2 ? Math.Round(values.Average(), 1, MidpointRounding.AwayFromZero) : null;
    }

    private static decimal Round(decimal value) => Math.Round(value, 0, MidpointRounding.AwayFromZero);

    private static string Signed(decimal value) => value.ToString("+0.#;-0.#;0", System.Globalization.CultureInfo.CurrentCulture);

    private static string Date(DateTimeOffset? at) =>
        at is { } value ? string.Format(CoreStrings.Level_DateSuffix, value.ToLocalTime()) : string.Empty;
}
