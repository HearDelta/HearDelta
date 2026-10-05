namespace HearDelta.Core;

/// <summary>
/// Dauerrauschen eines Störgeräuschblocks: Das Rauschen läuft mit festem RMS-Pegel ohne Unterbrechung, die Sprache
/// wird je Stimulus so eingemischt, dass ihr RMS (bei Kardinalzahlen im aktiven Bereich) den gewünschten SNR ergibt.
/// <see cref="ReferenceSpeechRmsDbfs"/> ist der mittlere Sprach-RMS des Pakets nach Spitzennormalisierung; der
/// Rauschpegel entspricht der eingestellten Lautstärke beim Start-SNR.
/// </summary>
public sealed record ContinuousNoiseSettings(
    int Version,
    decimal NoiseLevelDbfs,
    decimal ReferenceSpeechRmsDbfs);

/// <summary>
/// Sprachpegel-Statistik eines Pakets bei einer Ausgabe-Abtastrate: mittlerer Sprach-RMS nach Spitzennormalisierung und
/// der größte Abstand, um den ein einzelner Stimulus darunter liegt (er braucht bei gleichem SNR die meiste Verstärkung).
/// </summary>
public sealed record SpeechLevelStatistics(
    decimal ReferenceRmsDbfs,
    decimal MaximumBelowReferenceDb);

public sealed record ContinuousNoiseRequest(
    TestedEar Ear,
    decimal NoiseLevelDbfs,
    int NoiseSeed,
    int OutputSampleRate,
    HeadphoneEqualization? HeadphoneEqualization = null);

/// <summary>Nahtlos wiederholbare Rauschschleife, nur auf dem geprüften Kanal.</summary>
public sealed record ContinuousNoiseBed(
    int SampleRate,
    float[] InterleavedStereoLoop,
    string NoiseAlgorithm,
    decimal NoiseLevelDbfs,
    int NoiseSeed,
    float Peak);

public sealed record SpeechOverNoiseRequest(
    TestedEar Ear,
    decimal SignalToNoiseRatioDb,
    ContinuousNoiseBed Bed,
    decimal MaximumVolumeDb,
    HeadphoneEqualization? HeadphoneEqualization = null);

public static class ContinuousNoiseProtocol
{
    public const int CurrentVersion = 1;
    public const double LoopSeconds = 20;
    public const double CrossfadeSeconds = 0.5;

    /// <summary>Rauschpegel so, dass ein Stimulus mit Referenz-RMS bei <paramref name="speechLevelDb"/> den Start-SNR hat.</summary>
    public static ContinuousNoiseSettings Create(decimal speechLevelDb, decimal startSignalToNoiseRatioDb, SpeechLevelStatistics statistics) => new(
        CurrentVersion,
        speechLevelDb + statistics.ReferenceRmsDbfs - startSignalToNoiseRatioDb,
        statistics.ReferenceRmsDbfs);

    /// <summary>
    /// Höchster SNR, bei dem auch der leiseste Stimulus des Pakets die Pegelobergrenze nicht überschreitet.
    /// </summary>
    public static decimal MaximumSignalToNoiseRatioDb(
        ContinuousNoiseSettings settings,
        SpeechLevelStatistics statistics,
        decimal maximumVolumeDb) =>
        maximumVolumeDb - settings.NoiseLevelDbfs + settings.ReferenceSpeechRmsDbfs - statistics.MaximumBelowReferenceDb;
}
