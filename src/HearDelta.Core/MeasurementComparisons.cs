namespace HearDelta.Core;

public sealed record PercentageEstimate(
    decimal EstimatePercent,
    decimal Lower95Percent,
    decimal Upper95Percent);

public sealed record PairedMeasurementEstimate(
    PercentageEstimate WithoutHearingAid,
    PercentageEstimate WithHearingAid,
    PercentageEstimate DifferencePercentagePoints);

public static class MeasurementUncertainty
{
    private const double Z95 = 1.959963984540054;
    private sealed record ProportionInterval(double Estimate, double Lower, double Upper);

    public static PairedMeasurementEstimate Estimate(PairedMeasurementResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        var without = Wilson(result.WithoutHearingAid.CorrectResponses, result.WithoutHearingAid.TotalResponses);
        var with = Wilson(result.WithHearingAid.CorrectResponses, result.WithHearingAid.TotalResponses);
        var difference = NewcombeDifference(with, without);
        return new PairedMeasurementEstimate(
            ToPercent(without),
            ToPercent(with),
            ToPercent(difference));
    }

    private static ProportionInterval Wilson(int successes, int total)
    {
        ValidateCount(successes, total);
        var proportion = successes / (double)total;
        var zSquared = Z95 * Z95;
        var denominator = 1 + zSquared / total;
        var center = (proportion + zSquared / (2 * total)) / denominator;
        var margin = Z95 * Math.Sqrt(
            proportion * (1 - proportion) / total + zSquared / (4d * total * total)) / denominator;
        return new ProportionInterval(proportion, Math.Max(0, center - margin), Math.Min(1, center + margin));
    }

    private static ProportionInterval NewcombeDifference(
        ProportionInterval first,
        ProportionInterval second)
    {
        var difference = first.Estimate - second.Estimate;
        var lower = difference - Math.Sqrt(
            Math.Pow(first.Estimate - first.Lower, 2) + Math.Pow(second.Upper - second.Estimate, 2));
        var upper = difference + Math.Sqrt(
            Math.Pow(first.Upper - first.Estimate, 2) + Math.Pow(second.Estimate - second.Lower, 2));
        return new ProportionInterval(difference, Math.Max(-1, lower), Math.Min(1, upper));
    }

    private static PercentageEstimate ToPercent(ProportionInterval interval) => new(
        RoundPercent(interval.Estimate),
        RoundPercent(interval.Lower),
        RoundPercent(interval.Upper));

    private static decimal RoundPercent(double value) =>
        Math.Round((decimal)value * 100m, 1, MidpointRounding.AwayFromZero);

    private static void ValidateCount(int successes, int total)
    {
        if (total <= 0 || successes < 0 || successes > total)
            throw new ArgumentOutOfRangeException(nameof(successes), "Für das Unsicherheitsintervall werden gültige Binomialzählungen benötigt.");
    }
}

public sealed record MeasurementComparisonAssessment(
    bool IsDirectlyComparable,
    IReadOnlyList<string> Differences);

public static class MeasurementComparisonRules
{
    public static MeasurementComparisonAssessment Assess(
        PairedMeasurementSession first,
        PairedMeasurementSession second)
    {
        ArgumentNullException.ThrowIfNull(first);
        ArgumentNullException.ThrowIfNull(second);
        var differences = new List<string>();

        if (first.CompletedAt is null || second.CompletedAt is null)
            differences.Add(CoreStrings.Compare_OnlyCompleted);
        if (first.Ear != second.Ear)
            differences.Add(CoreStrings.Compare_Ear);
        if (first.Material != second.Material)
            differences.Add(CoreStrings.Compare_Material);
        if (first.Environment != second.Environment)
            differences.Add(CoreStrings.Compare_Environment);

        // Beim adaptiven Sprachpegel in Ruhe ist die Startlautstärke nur der Ausgangspunkt der Suche und kein Messpegel.
        var adaptiveSpeechLevel = first.AdaptiveTrack?.Parameter == AdaptiveTrackParameter.SpeechLevel &&
            second.AdaptiveTrack?.Parameter == AdaptiveTrackParameter.SpeechLevel;
        var firstTrack = adaptiveSpeechLevel ? first.AdaptiveTrack! with { StartValueDb = 0m } : first.AdaptiveTrack;
        var secondTrack = adaptiveSpeechLevel ? second.AdaptiveTrack! with { StartValueDb = 0m } : second.AdaptiveTrack;
        if (firstTrack != secondTrack)
            differences.Add(first.IsAdaptive != second.IsAdaptive
                ? CoreStrings.Compare_FixedVsAdaptive
                : CoreStrings.Compare_AdaptiveRule);

        if (first.ContinuousNoise != second.ContinuousNoise)
            differences.Add((first.ContinuousNoise is null) != (second.ContinuousNoise is null)
                ? CoreStrings.Compare_ContinuousVsStimulusNoise
                : CoreStrings.Compare_ContinuousNoiseLevel);

        CompareMaterialIdentity(first.MaterialIdentity, second.MaterialIdentity, differences);
        differences.AddRange(MeasurementProfileRules.GetComparisonDifferences(
            first.Hardware,
            adaptiveSpeechLevel ? second.Hardware with { StartVolumeDb = first.Hardware.StartVolumeDb } : second.Hardware));
        ComparePresentationConditions(first, second, differences);
        return new MeasurementComparisonAssessment(differences.Count == 0, differences);
    }

    private static void CompareMaterialIdentity(
        StimulusMaterialIdentity first,
        StimulusMaterialIdentity second,
        ICollection<string> differences)
    {
        if (first is null || second is null)
        {
            differences.Add(CoreStrings.Compare_MaterialIdentityMissing);
            return;
        }

        if (!string.Equals(first.FingerprintSha256, second.FingerprintSha256, StringComparison.OrdinalIgnoreCase))
            differences.Add(CoreStrings.Compare_PackFingerprint);
    }

    private static void ComparePresentationConditions(
        PairedMeasurementSession first,
        PairedMeasurementSession second,
        ICollection<string> differences)
    {
        var firstPresentations = first.Blocks.SelectMany(block => block.RawResponses)
            .Select(response => response.Presentation)
            .Where(presentation => presentation is not null)
            .Cast<StimulusPresentationRecord>()
            .ToArray();
        var secondPresentations = second.Blocks.SelectMany(block => block.RawResponses)
            .Select(response => response.Presentation)
            .Where(presentation => presentation is not null)
            .Cast<StimulusPresentationRecord>()
            .ToArray();

        if (firstPresentations.Length == 0 || secondPresentations.Length == 0)
        {
            differences.Add(CoreStrings.Compare_RecordMissing);
            return;
        }

        var firstCatalogs = firstPresentations
            .Select(presentation => (presentation.CatalogId, presentation.CatalogVersion))
            .Distinct()
            .ToArray();
        var secondCatalogs = secondPresentations
            .Select(presentation => (presentation.CatalogId, presentation.CatalogVersion))
            .Distinct()
            .ToArray();
        if (firstCatalogs.Length != 1 || secondCatalogs.Length != 1 || firstCatalogs[0] != secondCatalogs[0])
            differences.Add(CoreStrings.Compare_PackOrCatalog);

        // Die adaptiv veränderte Größe variiert innerhalb der Messung; verglichen wird dann nur die feste Größe.
        var snrIsAdaptive = first.AdaptiveTrack?.Parameter == AdaptiveTrackParameter.SignalToNoiseRatio &&
            second.AdaptiveTrack?.Parameter == AdaptiveTrackParameter.SignalToNoiseRatio;
        // Beim Dauerrauschen folgt der Sprachpegel je Stimulus aus Rauschpegel, SNR und Sprach-RMS.
        var levelIsAdaptive = (first.AdaptiveTrack?.Parameter == AdaptiveTrackParameter.SpeechLevel &&
            second.AdaptiveTrack?.Parameter == AdaptiveTrackParameter.SpeechLevel) ||
            (first.ContinuousNoise is not null && second.ContinuousNoise is not null);

        var firstSnr = firstPresentations.Select(presentation => snrIsAdaptive ? null : presentation.RenderMetadata.SignalToNoiseRatioDb)
            .Distinct()
            .ToArray();
        var secondSnr = secondPresentations.Select(presentation => snrIsAdaptive ? null : presentation.RenderMetadata.SignalToNoiseRatioDb)
            .Distinct()
            .ToArray();
        if (firstSnr.Length != 1 || secondSnr.Length != 1 || firstSnr[0] != secondSnr[0])
            differences.Add(CoreStrings.Compare_Snr);

        var firstRenderConditions = firstPresentations.Select(presentation => new
            {
                presentation.RenderMetadata.RendererVersion,
                presentation.RenderMetadata.NoiseAlgorithm,
                presentation.RenderMetadata.SourcePeakNormalizationDbfs,
                DigitalAttenuationDb = levelIsAdaptive ? 0m : presentation.RenderMetadata.DigitalAttenuationDb,
                presentation.RenderMetadata.OutputSampleRate,
                presentation.RenderMetadata.HeadphoneEqualizationSha256
            })
            .Distinct()
            .ToArray();
        var secondRenderConditions = secondPresentations.Select(presentation => new
            {
                presentation.RenderMetadata.RendererVersion,
                presentation.RenderMetadata.NoiseAlgorithm,
                presentation.RenderMetadata.SourcePeakNormalizationDbfs,
                DigitalAttenuationDb = levelIsAdaptive ? 0m : presentation.RenderMetadata.DigitalAttenuationDb,
                presentation.RenderMetadata.OutputSampleRate,
                presentation.RenderMetadata.HeadphoneEqualizationSha256
            })
            .Distinct()
            .ToArray();
        if (firstRenderConditions.Length != 1 || secondRenderConditions.Length != 1 ||
            !firstRenderConditions[0].Equals(secondRenderConditions[0]))
            differences.Add(CoreStrings.Compare_Render);
    }
}
