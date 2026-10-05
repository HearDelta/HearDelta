using HearDelta.Core;

namespace HearDelta.App.Services;

/// <summary>Letzte Ergebnisse je Schritt des empfohlenen Ablaufs für ein Ohr, ohne Rücksicht auf den Messaufbau.</summary>
public sealed record TestPlanStatus(
    TestPlanStep Step,
    DateTimeOffset? LastCompletedAt,
    string? ResultText);

/// <summary>
/// Liest die Vortests einer Person. Pegelwerte stammen nur aus Messungen mit demselben Messprofil und kompatiblem
/// Hardware-Snapshot, weil digitale Pegel nur innerhalb derselben Wiedergabekette übertragbar sind.
/// </summary>
public sealed class PretestResultsService
{
    private readonly IMeasurementSessionRepository wordTests;
    private readonly IHearingThresholdSessionRepository thresholdTests;
    private readonly IReadOnlyDictionary<string, StimulusCatalog> catalogsById;

    public PretestResultsService(
        IMeasurementSessionRepository wordTests,
        IHearingThresholdSessionRepository thresholdTests,
        IEnumerable<LoadedStimulusPack> packs)
    {
        this.wordTests = wordTests;
        this.thresholdTests = thresholdTests;
        catalogsById = packs.ToDictionary(pack => pack.Catalog.Id, pack => pack.Catalog, StringComparer.Ordinal);
    }

    public PretestResults Load(Guid personId, TestedEar ear, MeasurementHardwareSnapshot hardware)
    {
        var tone = thresholdTests.LoadForPerson(personId)
            .Where(session => session.Ear == ear &&
                              !session.IsLegacyWithHearingAid &&
                              session is { CompletedAt: not null, AbortedAt: null } &&
                              IsCompatible(session.Hardware, hardware))
            .OrderByDescending(session => session.StartedAt)
            .Select(session => (session.StartedAt, Average: TestLevelRules.ToneAverage(session.Observations.Select(observation => (
                session.Tones.Single(tone => tone.PresentationOrder == observation.PresentationOrder).FrequencyHz,
                observation.ThresholdAttenuationDbfs)))))
            .FirstOrDefault(value => value.Average is not null);

        var scored = ScoredAdaptiveNumberTests(personId, ear)
            .Where(value => IsCompatible(value.Session.Hardware, hardware))
            .ToArray();
        var quiet = scored.FirstOrDefault(value => value.Session.Environment == ListeningEnvironment.Quiet);
        // Störgeräuschschwellen sind nur bei gleichem Rauschverfahren (Dauerrauschen) übertragbar.
        var noise = scored.FirstOrDefault(value =>
            value.Session.Environment == ListeningEnvironment.BackgroundNoise && value.Session.ContinuousNoise is not null);

        return new PretestResults(
            tone.Average,
            tone.Average is null ? null : tone.StartedAt,
            quiet.Threshold,
            quiet.Threshold is null ? null : quiet.Session.StartedAt,
            noise.Threshold,
            noise.Threshold is null ? null : noise.Session.StartedAt);
    }

    /// <summary>Status aller Schritte für die Übersicht; Ergebnisse unabhängig vom Messaufbau.</summary>
    public IReadOnlyList<TestPlanStatus> LoadStatus(Guid personId, TestedEar ear)
    {
        var tone = thresholdTests.LoadForPerson(personId)
            .Where(session => session.Ear == ear && !session.IsLegacyWithHearingAid &&
                              session is { CompletedAt: not null, AbortedAt: null })
            .OrderByDescending(session => session.StartedAt)
            .FirstOrDefault();
        var toneAverage = tone is null
            ? null
            : TestLevelRules.ToneAverage(tone.Observations.Select(observation => (
                tone.Tones.Single(value => value.PresentationOrder == observation.PresentationOrder).FrequencyHz,
                observation.ThresholdAttenuationDbfs)));

        var adaptive = ScoredAdaptiveNumberTests(personId, ear).ToArray();
        var phonemes = wordTests.LoadForPerson(personId)
            .Where(session => session.Ear == ear && session.Material == SpeechMaterial.PhonemeContrasts &&
                              session is { CompletedAt: not null, AbortedAt: null })
            .OrderByDescending(session => session.StartedAt)
            .ToArray();

        return
        [
            new TestPlanStatus(TestPlanStep.HearingThreshold, tone?.StartedAt,
                tone is null ? null : toneAverage is { } average ? $"Tonmittel 0,5–2 kHz {average:0.#} dBFS" : "abgeschlossen"),
            AdaptiveStatus(TestPlanStep.NumbersQuiet, adaptive.FirstOrDefault(value => value.Session.Environment == ListeningEnvironment.Quiet), " dB"),
            AdaptiveStatus(TestPlanStep.NumbersNoise, adaptive.FirstOrDefault(value => value.Session.Environment == ListeningEnvironment.BackgroundNoise), " dB SNR"),
            PercentStatus(TestPlanStep.PhonemesQuiet, phonemes.FirstOrDefault(session => session.Environment == ListeningEnvironment.Quiet)),
            PercentStatus(TestPlanStep.PhonemesNoise, phonemes.FirstOrDefault(session => session.Environment == ListeningEnvironment.BackgroundNoise))
        ];
    }

    private static TestPlanStatus AdaptiveStatus(
        TestPlanStep step,
        (PairedMeasurementSession Session, decimal? Threshold, PairedMeasurementResult Result) value,
        string unit) =>
        value.Session is null
            ? new TestPlanStatus(step, null, null)
            : new TestPlanStatus(step, value.Session.StartedAt,
                $"ohne {Format(value.Result.WithoutHearingAid.Adaptive?.ThresholdDb)}{unit} · mit {Format(value.Result.WithHearingAid.Adaptive?.ThresholdDb)}{unit}");

    private TestPlanStatus PercentStatus(TestPlanStep step, PairedMeasurementSession? session)
    {
        if (session is null)
            return new TestPlanStatus(step, null, null);
        var result = TryScore(session);
        return new TestPlanStatus(step, session.StartedAt, result is null
            ? "abgeschlossen"
            : $"ohne {result.WithoutHearingAid.PercentCorrect:0.#} % · mit {result.WithHearingAid.PercentCorrect:0.#} %");
    }

    private IEnumerable<(PairedMeasurementSession Session, decimal? Threshold, PairedMeasurementResult Result)> ScoredAdaptiveNumberTests(
        Guid personId,
        TestedEar ear) =>
        wordTests.LoadForPerson(personId)
            .Where(session => session.Ear == ear && session.IsAdaptive &&
                              session is { CompletedAt: not null, AbortedAt: null })
            .OrderByDescending(session => session.StartedAt)
            .Select(session => (Session: session, Result: TryScore(session)))
            .Where(value => value.Result?.WithoutHearingAid.Adaptive?.ThresholdDb is not null)
            .Select(value => (value.Session, value.Result!.WithoutHearingAid.Adaptive!.ThresholdDb, value.Result!));

    private PairedMeasurementResult? TryScore(PairedMeasurementSession session)
    {
        if (!catalogsById.TryGetValue(session.MaterialIdentity.CatalogId, out var catalog))
            return null;
        try
        {
            return MeasurementScoring.Score(session, catalog);
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or KeyNotFoundException)
        {
            return null;
        }
    }

    /// <summary>Gleiches Messprofil und keine Abweichung außer der eingestellten Lautstärke.</summary>
    private static bool IsCompatible(MeasurementHardwareSnapshot measured, MeasurementHardwareSnapshot current) =>
        measured.ProfileId == current.ProfileId &&
        MeasurementProfileRules.GetComparisonDifferences(measured, current with { StartVolumeDb = measured.StartVolumeDb }).Count == 0;

    private static string Format(decimal? value) =>
        value is { } number ? number.ToString("+0.0;-0.0;0.0", System.Globalization.CultureInfo.GetCultureInfo("de-DE")) : "–";
}
