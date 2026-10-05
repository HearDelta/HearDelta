namespace HearDelta.Core;

/// <summary>Größe, die das adaptive Verfahren von Stimulus zu Stimulus verändert.</summary>
public enum AdaptiveTrackParameter
{
    /// <summary>Digitaler Sprachpegel in Ruhe (dB, digitale Absenkung).</summary>
    SpeechLevel = 1,

    /// <summary>Signal-Rausch-Abstand bei festem Sprachpegel (dB).</summary>
    SignalToNoiseRatio = 2
}

/// <summary>
/// Unveränderliche Regel eines adaptiven Blocks: 1-hoch/1-runter (Ziel 50 % richtig). Richtig → schwerer
/// (Wert sinkt), falsch → leichter (Wert steigt). Bis zur ersten falschen Antwort gilt die Anfangsschrittweite,
/// danach die Endschrittweite. Der Wert bleibt in [Minimum, Maximum].
/// </summary>
public sealed record AdaptiveTrackSettings(
    int Version,
    AdaptiveTrackParameter Parameter,
    decimal StartValueDb,
    decimal InitialStepDb,
    decimal FinalStepDb,
    decimal MinimumValueDb,
    decimal MaximumValueDb);

/// <summary>Ein dargebotener Stimulus des adaptiven Verlaufs.</summary>
public sealed record AdaptiveTrackTrial(decimal ValueDb, bool IsCorrect);

/// <summary>
/// Ergebnis eines adaptiven Blocks. <see cref="ThresholdDb"/> ist der Mittelwert aller Werte ab der ersten falschen
/// Antwort einschließlich des Werts, mit dem der nächste Stimulus dargeboten worden wäre.
/// </summary>
public sealed record AdaptiveTrackResult(
    decimal? ThresholdDb,
    int AveragedTrialCount,
    decimal? StandardDeviationDb,
    bool ReachedMaximum,
    string? Note);

public static class AdaptiveTrackProtocol
{
    public const int CurrentVersion = 1;
    public const decimal InitialStepDb = 6m;
    public const decimal FinalStepDb = 2m;

    /// <summary>Mindestzahl gemittelter Werte, ab der eine Schwelle angegeben wird.</summary>
    public const int MinimumAveragedTrials = 8;

    /// <summary>Leisester Sprachpegel des Verfahrens in Ruhe.</summary>
    public const decimal MinimumSpeechLevelDb = -90m;

    public const decimal MinimumSignalToNoiseRatioDb = -20m;
    public const decimal MaximumSignalToNoiseRatioDb = 30m;

    /// <summary>Adaptiver Sprachpegel in Ruhe: Start beim Startpegel des Messprofils, höchstens bis zur Pegelobergrenze.</summary>
    public static AdaptiveTrackSettings CreateSpeechLevelTrack(decimal startVolumeDb, decimal maximumVolumeDb) => new(
        CurrentVersion,
        AdaptiveTrackParameter.SpeechLevel,
        startVolumeDb,
        InitialStepDb,
        FinalStepDb,
        MinimumSpeechLevelDb,
        maximumVolumeDb);

    /// <summary>
    /// Adaptiver Signal-Rausch-Abstand bei festem Rauschpegel; der Sprachpegel folgt dem SNR. Die Obergrenze kann unter
    /// +30 dB liegen, wenn sonst die Pegelobergrenze für die Sprache überschritten würde.
    /// </summary>
    public static AdaptiveTrackSettings CreateSignalToNoiseTrack(decimal startSignalToNoiseRatioDb, decimal? maximumSignalToNoiseRatioDb = null) => new(
        CurrentVersion,
        AdaptiveTrackParameter.SignalToNoiseRatio,
        startSignalToNoiseRatioDb,
        InitialStepDb,
        FinalStepDb,
        MinimumSignalToNoiseRatioDb,
        Math.Min(MaximumSignalToNoiseRatioDb, Math.Floor(maximumSignalToNoiseRatioDb ?? MaximumSignalToNoiseRatioDb)));
}

public static class AdaptiveTrackRules
{
    public static IReadOnlyList<string> Validate(AdaptiveTrackSettings settings, ListeningEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var errors = new List<string>();
        if (settings.Version != AdaptiveTrackProtocol.CurrentVersion)
            errors.Add($"Die Version {settings.Version} des adaptiven Verfahrens wird nicht unterstützt.");
        if (!Enum.IsDefined(settings.Parameter))
            errors.Add("Die adaptive Größe ist ungültig.");
        else if ((settings.Parameter == AdaptiveTrackParameter.SpeechLevel) != (environment == ListeningEnvironment.Quiet))
            errors.Add("In Ruhe wird der Sprachpegel, im Störgeräusch der Signal-Rausch-Abstand adaptiv verändert.");
        if (settings.InitialStepDb <= 0 || settings.FinalStepDb <= 0)
            errors.Add("Die Schrittweiten des adaptiven Verfahrens müssen positiv sein.");
        if (settings.MinimumValueDb >= settings.MaximumValueDb)
            errors.Add("Der Wertebereich des adaptiven Verfahrens ist leer.");
        else if (settings.StartValueDb < settings.MinimumValueDb || settings.StartValueDb > settings.MaximumValueDb)
            errors.Add("Der Startwert des adaptiven Verfahrens liegt außerhalb des erlaubten Bereichs.");
        return errors;
    }

    /// <summary>Wert, mit dem der Stimulus nach den bisherigen <paramref name="trials"/> dargeboten wird.</summary>
    public static decimal NextValue(AdaptiveTrackSettings settings, IReadOnlyList<AdaptiveTrackTrial> trials)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(trials);
        var value = settings.StartValueDb;
        var hadIncorrect = false;
        foreach (var trial in trials)
        {
            hadIncorrect |= !trial.IsCorrect;
            var step = hadIncorrect ? settings.FinalStepDb : settings.InitialStepDb;
            value = Math.Clamp(trial.IsCorrect ? trial.ValueDb - step : trial.ValueDb + step,
                settings.MinimumValueDb,
                settings.MaximumValueDb);
        }
        return value;
    }

    /// <summary>Prüft, dass jeder gespeicherte Wert aus der Regel und den vorangehenden Antworten folgt.</summary>
    public static bool IsConsistent(AdaptiveTrackSettings settings, IReadOnlyList<AdaptiveTrackTrial> trials)
    {
        for (var index = 0; index < trials.Count; index++)
        {
            if (trials[index].ValueDb != NextValue(settings, trials.Take(index).ToArray()))
                return false;
        }
        return true;
    }

    public static AdaptiveTrackResult Evaluate(AdaptiveTrackSettings settings, IReadOnlyList<AdaptiveTrackTrial> trials)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(trials);
        var reachedMaximum = trials.Any(trial => !trial.IsCorrect && trial.ValueDb >= settings.MaximumValueDb);
        var firstIncorrect = trials.ToList().FindIndex(trial => !trial.IsCorrect);
        if (firstIncorrect < 0)
            return new AdaptiveTrackResult(null, 0, null, reachedMaximum,
                trials.Count == 0
                    ? "Noch keine Antwort erfasst."
                    : "Keine falsche Antwort – die Schwelle liegt unterhalb des leisesten erreichten Werts.");

        var values = trials.Skip(firstIncorrect).Select(trial => trial.ValueDb)
            .Append(NextValue(settings, trials))
            .ToArray();
        if (values.Length < AdaptiveTrackProtocol.MinimumAveragedTrials)
            return new AdaptiveTrackResult(null, values.Length, null, reachedMaximum,
                $"Zu wenige Darbietungen nach der ersten falschen Antwort ({values.Length} von mindestens {AdaptiveTrackProtocol.MinimumAveragedTrials}).");

        var mean = values.Average();
        var variance = values.Sum(value => (value - mean) * (value - mean)) / (values.Length - 1);
        var standardDeviation = (decimal)Math.Sqrt((double)variance);
        return new AdaptiveTrackResult(
            Math.Round(mean, 1, MidpointRounding.AwayFromZero),
            values.Length,
            Math.Round(standardDeviation, 1, MidpointRounding.AwayFromZero),
            reachedMaximum,
            reachedMaximum ? "Die Obergrenze wurde erreicht; die tatsächliche Schwelle kann höher liegen." : null);
    }

    /// <summary>Adaptiv veränderter Wert einer gespeicherten Darbietung.</summary>
    public static decimal? GetPresentedValue(AdaptiveTrackSettings settings, StimulusRenderMetadata metadata) =>
        settings.Parameter == AdaptiveTrackParameter.SpeechLevel
            ? metadata.DigitalAttenuationDb
            : metadata.SignalToNoiseRatioDb;
}
