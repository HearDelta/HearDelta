using HearDelta.Core;

namespace HearDelta.Core.Tests;

public sealed class MeasurementExposureAnalysisTests
{
    [Fact]
    public void CountsEveryCurrentResponseByItemGroupAndList()
    {
        var catalog = new StimulusCatalog(
            2, "test", "1", "de-DE", "Test", "CC0", false, 1,
            [
                new StimulusListDefinition("list-a", SpeechMaterial.PhonemeContrasts,
                    [new StimulusDefinition("a1", "a", "a", "a.wav", "onset", "Anfang")]),
                new StimulusListDefinition("list-b", SpeechMaterial.PhonemeContrasts,
                    [new StimulusDefinition("b1", "b", "b", "b.wav", "onset", "Anfang")])
            ]);
        var session = CreateSession() with
        {
            Blocks =
            [
                new MeasurementBlock(Guid.NewGuid(), 1, HearingAidCondition.WithoutHearingAid, "list-a", [Response("a1")]),
                new MeasurementBlock(Guid.NewGuid(), 2, HearingAidCondition.WithHearingAid, "list-b", [Response("b1"), Response("a1") with { PresentationOrder = 2 }])
            ]
        };

        var cells = MeasurementExposureAnalysis.Analyze([session], catalog);

        Assert.Contains(cells, cell => cell is { Scope: MeasurementExposureScope.Item, Key: "a1", PresentationCount: 2 });
        Assert.Contains(cells, cell => cell is { Scope: MeasurementExposureScope.Item, Key: "b1", PresentationCount: 1 });
        Assert.Contains(cells, cell => cell is { Scope: MeasurementExposureScope.ContrastGroup, Key: "onset", PresentationCount: 3 });
        Assert.Contains(cells, cell => cell is { Scope: MeasurementExposureScope.StimulusList, Key: "list-a", PresentationCount: 1 });
        Assert.Contains(cells, cell => cell is { Scope: MeasurementExposureScope.StimulusList, Key: "list-b", PresentationCount: 2 });
    }

    private static PairedMeasurementSession CreateSession() => PairedMeasurementSessionFactory.CreateRandomized(
        TestedEar.Left,
        new HearingAidSnapshot(Guid.NewGuid(), "Test", "Gerät", "Testgerät", TestedEar.Left, "Programm 1", "0"),
        SpeechMaterial.PhonemeContrasts,
        ListeningEnvironment.Quiet,
        "list-a",
        "list-b",
        new MeasurementHardwareSnapshot(Guid.NewGuid(), "Profil", "endpoint", "Ausgang", true, 48000, 24, 2, "Kopfhörer", "Modell", "offen", 300, "Ausgang", "Low", -60m, -30m, false, DateTimeOffset.UtcNow),
        DateTimeOffset.UtcNow,
        2,
        StimulusMaterialIdentityFactory.Create("test", "1", new string('a', 64), new string('b', 64)),
        MeasurementSessionContracts.PhonemeContrastMeasurement);

    private static RawMeasurementResponse Response(string stimulusId)
    {
        var timestamp = DateTimeOffset.UtcNow;
        return new RawMeasurementResponse(1, stimulusId, stimulusId, timestamp,
            new StimulusPresentationRecord("test", "1", stimulusId, new string('a', 64), "endpoint", "Ausgang",
                new StimulusRenderMetadata("test", "none", -6m, -60m, null, 1, 48000, 0.1f), timestamp, timestamp));
    }
}
