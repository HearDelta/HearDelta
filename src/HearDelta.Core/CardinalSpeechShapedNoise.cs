using System.Collections.Frozen;

namespace HearDelta.Core;

/// <summary>
/// Frozen design contract for a future stationary masker derived from one
/// cardinal-number package. This contract does not generate samples and does
/// not enable playback.
/// </summary>
public static class CardinalSpeechShapedNoiseProtocol
{
    public const string ProtocolId = "cardinal-speech-shaped-noise";
    public const int ProtocolVersion = 1;
    public const string SpectrumAlgorithm = "equal-item-welch-power-v1";
    public const string SpectrumSmoothing = "third-octave-log-power-v1";
    public const string FilterDesignAlgorithm = "frequency-sampling-blackman-v1";
    public const string GeneratorAlgorithm = "xorshift32-fir-v1";
    public const string SpeechRmsWindow = "first-to-last-active-frame-v1";
    public const string NoiseRmsWindow = "speech-active-aligned-v1";
    public const int RequiredStimulusCount = 900;
    public const int AnalysisSampleRate = 22050;
    public const int AnalysisFrameMilliseconds = 25;
    public const int AnalysisHopMilliseconds = 10;
    public const int AnalysisFftSize = 2048;
    public const decimal ActiveFrameRelativeThresholdDb = -40m;
    public const decimal ActiveFrameAbsoluteThresholdDbfs = -70m;
    public const int FirTapCount = 4097;
    public const decimal PreRollSeconds = 0.2m;
    public const decimal PostRollSeconds = 0.2m;
    public const decimal EdgeFadeSeconds = 0.02m;
    public const int SpectrumLowerFrequencyHz = 125;
    public const int SpectrumUpperFrequencyHz = 8000;
    public const int ValidationNoiseSeconds = 60;
    public const decimal ThirdOctaveValidationToleranceDb = 1m;
    public const decimal SignalToNoiseValidationToleranceDb = 0.1m;

    public static IReadOnlyList<int> SupportedOutputSampleRates { get; } =
        Array.AsReadOnly([44100, 48000]);

    public static IReadOnlySet<string> SupportedMaterialIds { get; } = new[]
    {
        "de-DE-personal-cardinal-numbers-christoph-v1",
        "de-DE-personal-cardinal-numbers-katja-v1"
    }.ToFrozenSet(StringComparer.Ordinal);

    public static IReadOnlyDictionary<string, string> ApprovedProfileSha256ByMaterialId { get; } =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["de-DE-personal-cardinal-numbers-christoph-v1"] = "40a88ad7891c705bd2f87f4712100c063da72dc74430d0b693933d167a8c7d44",
            ["de-DE-personal-cardinal-numbers-katja-v1"] = "01fa0cda34b60dc253edf6f40ed43beb1ef180cada3116d81ccf2e6876943c27"
        }.ToFrozenDictionary(StringComparer.Ordinal);

    public static CardinalSpeechShapedNoiseContract CreateContract(
        string materialId,
        string catalogSha256,
        string audioIndexSha256) => new(
            ProtocolId,
            ProtocolVersion,
            materialId,
            RequiredStimulusCount,
            catalogSha256,
            audioIndexSha256,
            SpectrumAlgorithm,
            SpectrumSmoothing,
            FilterDesignAlgorithm,
            GeneratorAlgorithm,
            AnalysisSampleRate,
            AnalysisFrameMilliseconds,
            AnalysisHopMilliseconds,
            AnalysisFftSize,
            ActiveFrameRelativeThresholdDb,
            ActiveFrameAbsoluteThresholdDbfs,
            SpeechRmsWindow,
            NoiseRmsWindow,
            FirTapCount,
            PreRollSeconds,
            PostRollSeconds,
            EdgeFadeSeconds,
            SpectrumLowerFrequencyHz,
            SpectrumUpperFrequencyHz,
            ValidationNoiseSeconds,
            ThirdOctaveValidationToleranceDb,
            SignalToNoiseValidationToleranceDb,
            SupportedOutputSampleRates);

    public static IReadOnlyList<string> Validate(CardinalSpeechShapedNoiseContract contract)
    {
        ArgumentNullException.ThrowIfNull(contract);
        var errors = new List<string>();

        if (contract.ProtocolId != ProtocolId || contract.ProtocolVersion != ProtocolVersion)
            errors.Add("Der Vertrag verwendet nicht die aktuelle Rauschprotokollversion.");
        if (!SupportedMaterialIds.Contains(contract.MaterialId))
            errors.Add("Sprachangepasstes Rauschen ist nur an ein freigegebenes Kardinalzahlpaket bindbar.");
        if (contract.StimulusCount != RequiredStimulusCount)
            errors.Add("Das Rauschspektrum muss aus allen 900 Kardinalzahlstimuli abgeleitet werden.");
        if (!IsLowercaseSha256(contract.CatalogSha256) || !IsLowercaseSha256(contract.AudioIndexSha256))
            errors.Add("Katalog und Audioindex benötigen jeweils einen kleingeschriebenen SHA-256-Wert.");
        if (contract.CatalogSha256 == contract.AudioIndexSha256)
            errors.Add("Katalog- und Audioindex-Hash dürfen nicht identisch sein.");

        if (!HasFrozenParameters(contract))
            errors.Add("Die Analyse-, RMS-, Filter- und Ausgabeparameter weichen vom eingefrorenen Vertrag ab.");

        return errors;
    }

    private static bool HasFrozenParameters(CardinalSpeechShapedNoiseContract contract) =>
        contract.SpectrumAlgorithm == SpectrumAlgorithm &&
        contract.SpectrumSmoothing == SpectrumSmoothing &&
        contract.FilterDesignAlgorithm == FilterDesignAlgorithm &&
        contract.GeneratorAlgorithm == GeneratorAlgorithm &&
        contract.AnalysisSampleRate == AnalysisSampleRate &&
        contract.AnalysisFrameMilliseconds == AnalysisFrameMilliseconds &&
        contract.AnalysisHopMilliseconds == AnalysisHopMilliseconds &&
        contract.AnalysisFftSize == AnalysisFftSize &&
        contract.ActiveFrameRelativeThresholdDb == ActiveFrameRelativeThresholdDb &&
        contract.ActiveFrameAbsoluteThresholdDbfs == ActiveFrameAbsoluteThresholdDbfs &&
        contract.SpeechRmsWindow == SpeechRmsWindow &&
        contract.NoiseRmsWindow == NoiseRmsWindow &&
        contract.FirTapCount == FirTapCount &&
        contract.PreRollSeconds == PreRollSeconds &&
        contract.PostRollSeconds == PostRollSeconds &&
        contract.EdgeFadeSeconds == EdgeFadeSeconds &&
        contract.SpectrumLowerFrequencyHz == SpectrumLowerFrequencyHz &&
        contract.SpectrumUpperFrequencyHz == SpectrumUpperFrequencyHz &&
        contract.ValidationNoiseSeconds == ValidationNoiseSeconds &&
        contract.ThirdOctaveValidationToleranceDb == ThirdOctaveValidationToleranceDb &&
        contract.SignalToNoiseValidationToleranceDb == SignalToNoiseValidationToleranceDb &&
        contract.SupportedOutputSampleRates is not null &&
        contract.SupportedOutputSampleRates.SequenceEqual(SupportedOutputSampleRates);

    private static bool IsLowercaseSha256(string? value) =>
        value is { Length: 64 } &&
        value.All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f');
}

public sealed record CardinalSpeechShapedNoiseContract(
    string ProtocolId,
    int ProtocolVersion,
    string MaterialId,
    int StimulusCount,
    string CatalogSha256,
    string AudioIndexSha256,
    string SpectrumAlgorithm,
    string SpectrumSmoothing,
    string FilterDesignAlgorithm,
    string GeneratorAlgorithm,
    int AnalysisSampleRate,
    int AnalysisFrameMilliseconds,
    int AnalysisHopMilliseconds,
    int AnalysisFftSize,
    decimal ActiveFrameRelativeThresholdDb,
    decimal ActiveFrameAbsoluteThresholdDbfs,
    string SpeechRmsWindow,
    string NoiseRmsWindow,
    int FirTapCount,
    decimal PreRollSeconds,
    decimal PostRollSeconds,
    decimal EdgeFadeSeconds,
    int SpectrumLowerFrequencyHz,
    int SpectrumUpperFrequencyHz,
    int ValidationNoiseSeconds,
    decimal ThirdOctaveValidationToleranceDb,
    decimal SignalToNoiseValidationToleranceDb,
    IReadOnlyList<int> SupportedOutputSampleRates);
