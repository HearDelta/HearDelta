using HearDelta.Core;

namespace HearDelta.Core.Tests;

public sealed class MeasurementComparisonTests
{
    [Fact]
    public void WilsonAndNewcombeIntervalsExposeSmallSampleUncertainty()
    {
        var result = new PairedMeasurementResult(
            new MeasurementBlockResult(HearingAidCondition.WithoutHearingAid, "a", 14, 25, 56m, []),
            new MeasurementBlockResult(HearingAidCondition.WithHearingAid, "b", 20, 25, 80m, []),
            24m,
            20m);

        var estimate = MeasurementUncertainty.Estimate(result);

        Assert.Equal(56m, estimate.WithoutHearingAid.EstimatePercent);
        Assert.Equal(80m, estimate.WithHearingAid.EstimatePercent);
        Assert.Equal(24m, estimate.DifferencePercentagePoints.EstimatePercent);
        Assert.True(estimate.WithoutHearingAid.Lower95Percent < 56m);
        Assert.True(estimate.WithoutHearingAid.Upper95Percent > 56m);
        Assert.True(estimate.DifferencePercentagePoints.Lower95Percent < 24m);
        Assert.True(estimate.DifferencePercentagePoints.Upper95Percent > 24m);
    }

    [Fact]
    public void IdenticalMeasurementConditionsAreDirectlyComparable()
    {
        var first = CreateSession(Guid.NewGuid(), "Gerät A", CreateHardware());
        var second = CreateSession(Guid.NewGuid(), "Gerät B", CreateHardware());

        var assessment = MeasurementComparisonRules.Assess(first, second);

        Assert.True(assessment.IsDirectlyComparable);
        Assert.Empty(assessment.Differences);
    }

    [Fact]
    public void HardwareAndMeasurementDeviationsAreReported()
    {
        var first = CreateSession(Guid.NewGuid(), "Gerät A", CreateHardware());
        var changedHardware = CreateHardware() with
        {
            Gain = "High (+19 dB)",
            StartVolumeDb = -50m
        };
        var second = CreateSession(Guid.NewGuid(), "Gerät B", changedHardware) with
        {
            Ear = TestedEar.Right,
            HearingAid = new HearingAidSnapshot(Guid.NewGuid(), "Test", "B", "Gerät B", TestedEar.Right)
        };

        var assessment = MeasurementComparisonRules.Assess(first, second);

        Assert.False(assessment.IsDirectlyComparable);
        Assert.Contains(assessment.Differences, difference => difference.Contains("Ohr"));
        Assert.Contains(assessment.Differences, difference => difference.Contains("Gain"));
        Assert.Contains(assessment.Differences, difference => difference.Contains("Messpegel"));
    }

    [Fact]
    public void ChangedAudioMaterialWithSameCatalogVersionIsNotDirectlyComparable()
    {
        var first = CreateSession(Guid.NewGuid(), "Gerät A", CreateHardware());
        var second = CreateSession(Guid.NewGuid(), "Gerät B", CreateHardware()) with
        {
            MaterialIdentity = CreateMaterialIdentity('b')
        };

        var assessment = MeasurementComparisonRules.Assess(first, second);

        Assert.False(assessment.IsDirectlyComparable);
        Assert.Contains(assessment.Differences, difference => difference.Contains("Fingerabdruck"));
    }

    [Fact]
    public void AdaptiveLevelsVaryWithoutBreakingComparabilityButFixedVersusAdaptiveIsReported()
    {
        var track = AdaptiveTrackProtocol.CreateSpeechLevelTrack(-60m, -30m);
        var first = WithLevel(CreateSession(Guid.NewGuid(), "Gerät A", CreateHardware()), -60m) with { AdaptiveTrack = track };
        var second = WithLevel(CreateSession(Guid.NewGuid(), "Gerät B", CreateHardware()), -66m) with { AdaptiveTrack = track };

        Assert.True(MeasurementComparisonRules.Assess(first, second).IsDirectlyComparable);

        var fixedLevel = CreateSession(Guid.NewGuid(), "Gerät C", CreateHardware());
        var assessment = MeasurementComparisonRules.Assess(first, fixedLevel);
        Assert.False(assessment.IsDirectlyComparable);
        Assert.Contains(assessment.Differences, difference => difference.Contains("adaptiv"));
    }

    [Fact]
    public void StartVolumeOnlyMattersForFixedLevelsNotForAdaptiveSpeechLevel()
    {
        var louder = CreateHardware() with { StartVolumeDb = -50m };
        var first = CreateSession(Guid.NewGuid(), "Gerät A", CreateHardware()) with
        {
            AdaptiveTrack = AdaptiveTrackProtocol.CreateSpeechLevelTrack(CreateHardware().StartVolumeDb, CreateHardware().MaximumVolumeDb)
        };
        var second = CreateSession(Guid.NewGuid(), "Gerät B", louder) with
        {
            AdaptiveTrack = AdaptiveTrackProtocol.CreateSpeechLevelTrack(louder.StartVolumeDb, louder.MaximumVolumeDb)
        };

        Assert.True(MeasurementComparisonRules.Assess(first, WithLevel(second, CreateHardware().StartVolumeDb)).IsDirectlyComparable);
        Assert.False(MeasurementComparisonRules.Assess(
            CreateSession(Guid.NewGuid(), "Gerät A", CreateHardware()),
            CreateSession(Guid.NewGuid(), "Gerät B", louder)).IsDirectlyComparable);
    }

    private static PairedMeasurementSession WithLevel(PairedMeasurementSession session, decimal level) => session with
    {
        Blocks = session.Blocks.Select(block => block with
        {
            RawResponses = block.RawResponses.Select(response => response with
            {
                Presentation = response.Presentation with
                {
                    RenderMetadata = response.Presentation.RenderMetadata with { DigitalAttenuationDb = level }
                }
            }).ToArray()
        }).ToArray()
    };

    private static PairedMeasurementSession CreateSession(
        Guid hearingAidId,
        string displayName,
        MeasurementHardwareSnapshot hardware)
    {
        var startedAt = DateTimeOffset.Parse("2026-09-01T08:00:00Z");
        var session = PairedMeasurementSessionFactory.CreateRandomized(
            TestedEar.Left,
            new HearingAidSnapshot(hearingAidId, "Test", displayName, displayName, TestedEar.Left),
            SpeechMaterial.PhonemeContrasts,
            ListeningEnvironment.Quiet,
            "list-a",
            "list-b",
            hardware,
            startedAt,
            20,
            CreateMaterialIdentity('a'),
            MeasurementSessionContracts.PhonemeContrastMeasurement);
        return session with
        {
            CompletedAt = startedAt.AddMinutes(10),
            Blocks = session.Blocks.Select(block => block with
            {
                RawResponses =
                [
                    new RawMeasurementResponse(
                        1,
                        $"stimulus-{block.PresentationOrder}",
                        "Test",
                        startedAt.AddMinutes(block.PresentationOrder),
                        CreatePresentation($"stimulus-{block.PresentationOrder}", hardware))
                ]
            }).ToArray()
        };
    }

    private static StimulusPresentationRecord CreatePresentation(
        string stimulusId,
        MeasurementHardwareSnapshot hardware) => new(
            "catalog",
            "1.0.0",
            stimulusId,
            new string('a', 64),
            hardware.EndpointId,
            hardware.EndpointName,
            new StimulusRenderMetadata(
                StimulusAudioRenderer.RendererVersion,
                "none",
                -6m,
                hardware.StartVolumeDb,
                null,
                42,
                hardware.SampleRate,
                0.01f),
            DateTimeOffset.Parse("2026-09-01T08:01:00Z"),
            DateTimeOffset.Parse("2026-09-01T08:01:01Z"));

    private static StimulusMaterialIdentity CreateMaterialIdentity(char value) =>
        StimulusMaterialIdentityFactory.Create(
        "catalog",
        "1.0.0",
        new string(value, 64),
        new string(value, 64));

    private static MeasurementHardwareSnapshot CreateHardware() => new(
        Guid.Parse("84452115-0de9-489b-acd8-df0178db080d"),
        "Test-DAC + Testkopfhörer",
        "endpoint-topping",
        "Lautsprecher (TOPPING USB DAC)",
        true,
        48000,
        24,
        2,
        "Testhersteller",
        "Testmodell",
        "Ohrumschließend",
        300,
        "Vordere 3,5-mm-Klinke",
        "Low (+6 dB)",
        -60m,
        -30m,
        false,
        DateTimeOffset.Parse("2026-09-01T07:30:00Z"));
}
