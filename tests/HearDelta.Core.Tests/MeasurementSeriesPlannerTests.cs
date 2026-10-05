using HearDelta.Core;

namespace HearDelta.Core.Tests;

public sealed class MeasurementSeriesPlannerTests
{
    [Fact]
    public void SeriesBalancesConditionOrderAndRotatesDistinctLists()
    {
        var plan = MeasurementSeriesPlanner.Create(CreateCatalog(), SpeechMaterial.PhonemeContrasts, 6, 20260906);

        Assert.Equal(6, plan.Rounds.Count);
        Assert.Equal(3, plan.Rounds.Count(round => round.FirstCondition == HearingAidCondition.WithoutHearingAid));
        Assert.Equal(3, plan.Rounds.Count(round => round.FirstCondition == HearingAidCondition.WithHearingAid));
        Assert.All(plan.Rounds, round =>
        {
            Assert.NotEqual(round.FirstListId, round.SecondListId);
            Assert.Equal(round.FirstCondition, PairedMeasurementSessionFactory.GetFirstCondition(round.SessionSeed));
        });
    }

    private static StimulusCatalog CreateCatalog() => new(
        1, "test", "1", "de-DE", "Test", "CC0", false, 1,
        [
            new StimulusListDefinition("a", SpeechMaterial.PhonemeContrasts, [new StimulusDefinition("a1", "a", "a", "a.wav")]),
            new StimulusListDefinition("b", SpeechMaterial.PhonemeContrasts, [new StimulusDefinition("b1", "b", "b", "b.wav")]),
            new StimulusListDefinition("c", SpeechMaterial.PhonemeContrasts, [new StimulusDefinition("c1", "c", "c", "c.wav")])
        ]);
}
