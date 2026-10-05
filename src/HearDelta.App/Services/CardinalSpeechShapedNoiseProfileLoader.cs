using System.Buffers.Binary;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using HearDelta.Core;

namespace HearDelta.App.Services;

internal static class CardinalSpeechShapedNoiseProfileLoader
{
    public static CardinalSpeechShapedNoiseProfile Load(
        string profilePath,
        StimulusMaterialIdentity materialIdentity)
    {
        var bytes = File.ReadAllBytes(profilePath);
        using var document = JsonDocument.Parse(bytes);
        var root = document.RootElement;
        var errors = new List<string>();

        Require(root.GetProperty("schemaVersion").GetInt32() == 1, "Die Rauschprofil-Schemaversion ist ungültig.", errors);
        Require(root.GetProperty("protocolId").GetString() == CardinalSpeechShapedNoiseProtocol.ProtocolId &&
            root.GetProperty("protocolVersion").GetInt32() == CardinalSpeechShapedNoiseProtocol.ProtocolVersion,
            "Das Rauschprofil verwendet nicht cardinal-speech-shaped-noise v1.", errors);

        var material = root.GetProperty("material");
        var materialId = material.GetProperty("id").GetString() ?? string.Empty;
        var catalogSha256 = material.GetProperty("catalogSha256").GetString() ?? string.Empty;
        var audioIndexSha256 = material.GetProperty("audioIndexSha256").GetString() ?? string.Empty;
        Require(material.GetProperty("stimulusCount").GetInt32() == CardinalSpeechShapedNoiseProtocol.RequiredStimulusCount,
            "Das Rauschprofil wurde nicht aus allen 900 Stimuli abgeleitet.", errors);

        var analysis = root.GetProperty("analysis");
        Require(analysis.GetProperty("algorithm").GetString() == CardinalSpeechShapedNoiseProtocol.SpectrumAlgorithm &&
            analysis.GetProperty("smoothing").GetString() == CardinalSpeechShapedNoiseProtocol.SpectrumSmoothing &&
            analysis.GetProperty("sampleRate").GetInt32() == CardinalSpeechShapedNoiseProtocol.AnalysisSampleRate &&
            analysis.GetProperty("frameMilliseconds").GetInt32() == CardinalSpeechShapedNoiseProtocol.AnalysisFrameMilliseconds &&
            analysis.GetProperty("hopMilliseconds").GetInt32() == CardinalSpeechShapedNoiseProtocol.AnalysisHopMilliseconds &&
            analysis.GetProperty("fftSize").GetInt32() == CardinalSpeechShapedNoiseProtocol.AnalysisFftSize &&
            analysis.GetProperty("activeFrameRelativeThresholdDb").GetDecimal() == CardinalSpeechShapedNoiseProtocol.ActiveFrameRelativeThresholdDb &&
            analysis.GetProperty("activeFrameAbsoluteThresholdDbfs").GetDecimal() == CardinalSpeechShapedNoiseProtocol.ActiveFrameAbsoluteThresholdDbfs &&
            analysis.GetProperty("speechRmsWindow").GetString() == CardinalSpeechShapedNoiseProtocol.SpeechRmsWindow,
            "Die Analyse- oder RMS-Regeln des Rauschprofils weichen vom v1-Vertrag ab.", errors);

        var generator = root.GetProperty("generator");
        Require(generator.GetProperty("algorithm").GetString() == CardinalSpeechShapedNoiseProtocol.GeneratorAlgorithm &&
            generator.GetProperty("zeroSeedReplacementHex").GetString() == "9e3779b9" &&
            generator.GetProperty("preRollSeconds").GetDecimal() == CardinalSpeechShapedNoiseProtocol.PreRollSeconds &&
            generator.GetProperty("postRollSeconds").GetDecimal() == CardinalSpeechShapedNoiseProtocol.PostRollSeconds &&
            generator.GetProperty("edgeFadeSeconds").GetDecimal() == CardinalSpeechShapedNoiseProtocol.EdgeFadeSeconds,
            "Seed, Vor-/Nachlauf oder Blende des Rauschprofils weichen vom v1-Vertrag ab.", errors);

        var filterDesign = root.GetProperty("filterDesign");
        var targetBand = filterDesign.GetProperty("targetBandHz").EnumerateArray().Select(value => value.GetInt32()).ToArray();
        Require(filterDesign.GetProperty("algorithm").GetString() == CardinalSpeechShapedNoiseProtocol.FilterDesignAlgorithm &&
            filterDesign.GetProperty("tapCount").GetInt32() == CardinalSpeechShapedNoiseProtocol.FirTapCount &&
            targetBand.SequenceEqual([CardinalSpeechShapedNoiseProtocol.SpectrumLowerFrequencyHz, CardinalSpeechShapedNoiseProtocol.SpectrumUpperFrequencyHz]),
            "Filterdesign oder Zielband des Rauschprofils weichen vom v1-Vertrag ab.", errors);

        var filters = new Dictionary<int, IReadOnlyList<double>>();
        foreach (var filter in filterDesign.GetProperty("filters").EnumerateArray())
        {
            var sampleRate = filter.GetProperty("sampleRate").GetInt32();
            var coefficients = filter.GetProperty("coefficients").EnumerateArray().Select(value => value.GetDouble()).ToArray();
            Require(filter.GetProperty("tapCount").GetInt32() == CardinalSpeechShapedNoiseProtocol.FirTapCount &&
                filter.GetProperty("groupDelaySamples").GetInt32() == (CardinalSpeechShapedNoiseProtocol.FirTapCount - 1) / 2,
                $"Tap-Zahl oder Gruppenlaufzeit des FIR-Filters für {sampleRate} Hz ist ungültig.", errors);
            var expectedHash = filter.GetProperty("coefficientSha256").GetString();
            var coefficientBytes = new byte[checked(coefficients.Length * sizeof(double))];
            for (var index = 0; index < coefficients.Length; index++)
                BinaryPrimitives.WriteInt64LittleEndian(
                    coefficientBytes.AsSpan(index * sizeof(double), sizeof(double)),
                    BitConverter.DoubleToInt64Bits(coefficients[index]));
            var actualHash = Convert.ToHexStringLower(SHA256.HashData(coefficientBytes));
            Require(string.Equals(actualHash, expectedHash, StringComparison.Ordinal),
                $"Die Koeffizienten-SHA-256 für {sampleRate} Hz stimmt nicht.", errors);
            if (!filters.TryAdd(sampleRate, coefficients))
                errors.Add($"Das Rauschprofil enthält den FIR-Filter für {sampleRate} Hz mehrfach.");
        }

        var profile = new CardinalSpeechShapedNoiseProfile(
            materialId,
            catalogSha256,
            audioIndexSha256,
            Convert.ToHexStringLower(SHA256.HashData(bytes)),
            filters);
        errors.AddRange(CardinalSpeechShapedNoiseProfileRules.Validate(profile, materialIdentity));
        if (errors.Count > 0)
            throw new InvalidDataException($"Das Rauschprofil ist ungültig: {string.Join(" ", errors)}");
        return profile;
    }

    private static void Require(bool condition, string message, ICollection<string> errors)
    {
        if (!condition)
            errors.Add(message);
    }
}
