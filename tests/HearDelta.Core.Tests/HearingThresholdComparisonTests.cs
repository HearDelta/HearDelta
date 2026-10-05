using HearDelta.Core;

namespace HearDelta.Core.Tests;

public sealed class HearingThresholdComparisonTests
{
    private static readonly DateTimeOffset StartedAt = DateTimeOffset.Parse("2026-09-30T08:00:00Z");

    [Fact]
    public void RowsCompareEachFrequencyAndReportTheImprovementOfTheSecondTest()
    {
        var without = CreateSession(null, CreateHardware(), (0, -40m), (1, null), (2, -30m));
        var with = CreateSession(null, CreateHardware(), (0, -55m), (1, -20m), (3, -35m));

        var rows = HearingThresholdComparisonRules.CreateRows(without, with);

        Assert.Equal(rows.Select(row => row.FrequencyHz).Order(), rows.Select(row => row.FrequencyHz));
        var first = rows.Single(row => row.FrequencyHz == without.Tones[0].FrequencyHz);
        Assert.Equal(15m, first.ImprovementDb);

        var notHeardBefore = rows.Single(row => row.FrequencyHz == without.Tones[1].FrequencyHz);
        Assert.True(notHeardBefore.First.Tested);
        Assert.False(notHeardBefore.First.Heard);
        Assert.Equal(-20m, notHeardBefore.Second.ThresholdAttenuationDbfs);
        Assert.Null(notHeardBefore.ImprovementDb);

        var onlyFirst = rows.Single(row => row.FrequencyHz == without.Tones[2].FrequencyHz);
        Assert.False(onlyFirst.Second.Tested);
        Assert.Null(onlyFirst.ImprovementDb);

        Assert.Equal(4, rows.Count);
    }

    [Fact]
    public void DifferencesIgnoreEarButReportAnotherSetup()
    {
        var reference = CreateSession(null, CreateHardware(), (0, -40m));
        var sameSetup = CreateSession(null, CreateHardware(), (0, -50m));
        Assert.Empty(HearingThresholdComparisonRules.GetDifferences([reference, sameSetup]));

        var otherHeadphones = CreateSession(null, CreateHardware() with { HeadphoneModel = "Anderes Modell" }, (0, -50m));
        Assert.NotEmpty(HearingThresholdComparisonRules.GetDifferences([reference, otherHeadphones]));
        Assert.Empty(HearingThresholdComparisonRules.GetDifferences([reference]));
    }

    [Fact]
    public void DifferentMaskingIsReported()
    {
        var unmasked = CreateSession(null, CreateHardware(), (0, -40m));
        var masked = CreateSession(-50m, CreateHardware(), (0, -38m));
        var maskedLouder = CreateSession(-40m, CreateHardware(), (0, -38m));

        Assert.Contains(HearingThresholdComparisonRules.GetDifferences([unmasked, masked]), text => text.Contains("Vertäubung"));
        Assert.Contains(HearingThresholdComparisonRules.GetDifferences([masked, maskedLouder]), text => text.Contains("Vertäubung"));
        Assert.Empty(HearingThresholdComparisonRules.GetDifferences([masked, CreateSession(-50m, CreateHardware(), (0, -41m))]));
    }

    [Fact]
    public void LegacySessionWithHearingAidIsFlagged()
    {
        var current = CreateSession(null, CreateHardware() with { MaximumVolumeDb = -6m });
        var legacy = current with
        {
            ProtocolVersion = 11,
            Condition = HearingAidCondition.WithHearingAid,
            HearingAid = new HearingAidSnapshot(Guid.NewGuid(), "Test", "Gerät", "Test Gerät", TestedEar.Right)
        };

        Assert.Empty(HearingThresholdSessionRules.Validate(legacy));
        Assert.Contains(HearingThresholdComparisonRules.GetDifferences([current, legacy]), text => text.Contains("mit Hörgerät"));
    }

    private static HearingThresholdSession CreateSession(
        decimal? maskingLevelDbfs,
        MeasurementHardwareSnapshot hardware,
        params (int ToneIndex, decimal? Threshold)[] results)
    {
        var session = HearingThresholdSessionFactory.Create(
            TestedEar.Right, ThresholdToneOrder.Ascending, hardware, StartedAt, randomizationSeed: 1, maskingLevelDbfs);
        var observations = results.Select(result =>
        {
            var tone = session.Tones[result.ToneIndex];
            var presentation = new ThresholdTonePresentationRecord(
                hardware.EndpointId, hardware.EndpointName, tone.FrequencyHz, session.StartAttenuationDbfs,
                result.Threshold ?? session.MaximumAttenuationDbfs, session.MaximumAttenuationDbfs, session.LevelStepDb,
                hardware.SampleRate, StartedAt, StartedAt.AddSeconds(10), false,
                session.SignalPattern, MaskingLevelDbfs: session.Masking?.LevelDbfs);
            return new HearingThresholdObservation(tone.PresentationOrder, tone.MidiNoteNumber, result.Threshold is not null,
                result.Threshold, StartedAt.AddSeconds(10), presentation);
        }).ToArray();
        return session with { Observations = observations };
    }

    private static MeasurementHardwareSnapshot CreateHardware() => new(
        Guid.Parse("84452115-0de9-489b-acd8-df0178db080d"),
        "Test-DAC + Testkopfhörer",
        "endpoint-topping",
        "Lautsprecher (TOPPING USB DAC)",
        true,
        48_000,
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
        DateTimeOffset.Parse("2026-09-02T07:30:00Z"));
}
