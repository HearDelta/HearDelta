using HearDelta.Core;

namespace HearDelta.Core.Tests;

public sealed class MeasurementRunAndScoringTests
{
    [Fact]
    public void RunPlanIsStableAndUsesEveryListStimulusExactlyOnce()
    {
        var catalog = CreateCatalog();
        var session = CreateSession(catalog, randomizationSeed: 20260901);

        var first = MeasurementRunPlanner.Create(session, catalog);
        var second = MeasurementRunPlanner.Create(session, catalog);

        Assert.Equal(
            first.Blocks.SelectMany(block => block.Stimuli).Select(item => item.StimulusId),
            second.Blocks.SelectMany(block => block.Stimuli).Select(item => item.StimulusId));
        Assert.All(first.Blocks, block =>
        {
            Assert.Equal(3, block.Stimuli.Count);
            Assert.Equal([1, 2, 3], block.Stimuli.Select(item => item.PresentationOrder));
            Assert.Equal(3, block.Stimuli.Select(item => item.StimulusId).Distinct().Count());
        });
    }

    [Fact]
    public void ListSelectionIsStableAndAlwaysReturnsTwoDifferentLists()
    {
        var catalog = CreateCatalog();

        var first = MeasurementRunPlanner.SelectListIds(catalog, SpeechMaterial.PhonemeContrasts, 42);
        var second = MeasurementRunPlanner.SelectListIds(catalog, SpeechMaterial.PhonemeContrasts, 42);

        Assert.Equal(first, second);
        Assert.NotEqual(first.FirstListId, first.SecondListId);
    }

    [Fact]
    public void ScoreKeepsRawAnswersAndReportsConditionsSeparately()
    {
        var catalog = CreateCatalog();
        var session = CreateSession(catalog, randomizationSeed: 20);
        var plan = MeasurementRunPlanner.Create(session, catalog);
        var completedBlocks = new List<MeasurementBlock>();

        foreach (var plannedBlock in plan.Blocks)
        {
            var sourceBlock = session.Blocks.Single(block => block.Id == plannedBlock.BlockId);
            var responses = plannedBlock.Stimuli.Select((planned, index) =>
            {
                var canonical = catalog.Lists.SelectMany(list => list.Items)
                    .Single(item => item.Id == planned.StimulusId).CanonicalResponse;
                var entered = plannedBlock.Condition == HearingAidCondition.WithHearingAid || index == 0
                    ? $"  {canonical.ToUpperInvariant()}  "
                    : "andere Antwort";
                return new RawMeasurementResponse(
                    planned.PresentationOrder,
                    planned.StimulusId,
                    entered,
                    DateTimeOffset.Parse("2026-09-01T08:05:00Z").AddSeconds(index),
                    CreatePresentation(planned.StimulusId));
            }).ToArray();
            completedBlocks.Add(sourceBlock with
            {
                RawResponses = responses,
                SetupConfirmation = ConfirmedSetup()
            });
        }

        var completed = session with
        {
            CompletedAt = DateTimeOffset.Parse("2026-09-01T08:10:00Z"),
            Blocks = completedBlocks
        };
        var result = MeasurementScoring.Score(completed, catalog);

        Assert.Equal(1, result.WithoutHearingAid.CorrectResponses);
        Assert.Equal(3, result.WithoutHearingAid.TotalResponses);
        Assert.Equal(33.3m, result.WithoutHearingAid.PercentCorrect);
        Assert.Equal(3, result.WithHearingAid.CorrectResponses);
        Assert.Equal(100m, result.WithHearingAid.PercentCorrect);
        Assert.Equal(66.7m, result.DifferencePercentagePoints);
        Assert.Equal(20m, result.ChanceLevelPercent);
        Assert.StartsWith("  ", result.WithHearingAid.Responses[0].EnteredText);
    }

    [Fact]
    public void IncompleteSessionCannotBeScored()
    {
        var catalog = CreateCatalog();
        var session = CreateSession(catalog);

        var exception = Assert.Throws<InvalidOperationException>(() =>
            MeasurementScoring.Score(session, catalog));

        Assert.Contains("noch nicht abgeschlossene", exception.Message);
    }

    [Fact]
    public void AbortedSessionScoresOnlyRecordedResponses()
    {
        var catalog = CreateCatalog();
        var session = CreateSession(catalog, randomizationSeed: 20);
        var plan = MeasurementRunPlanner.Create(session, catalog);
        var planned = plan.Blocks[0].Stimuli[0];
        var canonical = catalog.Lists.SelectMany(list => list.Items)
            .Single(item => item.Id == planned.StimulusId).CanonicalResponse;
        var sourceBlock = session.Blocks.Single(block => block.Id == plan.Blocks[0].BlockId);
        var response = new RawMeasurementResponse(
            planned.PresentationOrder,
            planned.StimulusId,
            canonical,
            DateTimeOffset.Parse("2026-09-01T08:01:00Z"),
            CreatePresentation(planned.StimulusId));
        var abortedAt = DateTimeOffset.Parse("2026-09-01T08:02:00Z");
        var aborted = session with
        {
            CompletedAt = abortedAt,
            AbortedAt = abortedAt,
            Blocks = session.Blocks
                .Select(block => block.Id == sourceBlock.Id
                    ? block with { RawResponses = [response], SetupConfirmation = ConfirmedSetup() }
                    : block)
                .ToArray()
        };

        var result = MeasurementScoring.ScorePartial(aborted, catalog);

        Assert.Equal(1, result.WithoutHearingAid.CorrectResponses);
        Assert.Equal(1, result.WithoutHearingAid.TotalResponses);
        Assert.Equal(0, result.WithHearingAid.TotalResponses);
        Assert.Throws<InvalidOperationException>(() => MeasurementScoring.Score(aborted, catalog));
    }

    private static PairedMeasurementSession CreateSession(
        StimulusCatalog catalog,
        int randomizationSeed = 20)
    {
        var lists = MeasurementRunPlanner.SelectListIds(
            catalog,
            SpeechMaterial.PhonemeContrasts,
            randomizationSeed);
        return PairedMeasurementSessionFactory.CreateRandomized(
            TestedEar.Left,
            new HearingAidSnapshot(
                Guid.Parse("3a80fa53-9658-4a71-8936-86d67f91e688"),
                "Signia",
                "Pure C&G BCT 2IX",
                "Signia links",
                TestedEar.Left,
                "Programm 1",
                "0"),
            SpeechMaterial.PhonemeContrasts,
            ListeningEnvironment.Quiet,
            lists.FirstListId,
            lists.SecondListId,
            CreateHardware(),
            DateTimeOffset.Parse("2026-09-01T08:00:00Z"),
            randomizationSeed,
            StimulusMaterialIdentityFactory.Create(catalog.Id, catalog.Version, new string('a', 64), new string('b', 64)),
            MeasurementSessionContracts.PhonemeContrastMeasurement);
    }

    private static MeasurementSetupConfirmation ConfirmedSetup() =>
        new(true, true, DateTimeOffset.Parse("2026-09-01T08:00:30Z"));

    private static StimulusPresentationRecord CreatePresentation(string stimulusId)
    {
        var timestamp = DateTimeOffset.Parse("2026-09-01T08:00:45Z");
        return new StimulusPresentationRecord(
            "test-catalog", "1.0.0", stimulusId, new string('a', 64),
            "endpoint-topping", "Lautsprecher (TOPPING USB DAC)",
            new StimulusRenderMetadata(StimulusAudioRenderer.RendererVersion, "none", -6m, -60m, null, 1, 48_000, 0.1f),
            timestamp, timestamp.AddSeconds(1));
    }

    private static StimulusCatalog CreateCatalog()
    {
        static StimulusDefinition Item(string id, string word) =>
            new(id, word, word, $"audio/{id}.wav");
        static StimulusListDefinition List(string id, string prefix) =>
            new(id, SpeechMaterial.PhonemeContrasts,
            [
                Item($"{prefix}-1", $"{prefix}eins"),
                Item($"{prefix}-2", $"{prefix}zwei"),
                Item($"{prefix}-3", $"{prefix}drei")
            ]);
        return new StimulusCatalog(
            1,
            "test-catalog",
            "1.0.0",
            "de-DE",
            "Testkatalog",
            "CC0-1.0",
            false,
            3,
            [List("list-a", "a"), List("list-b", "b"), List("list-c", "c")],
            ChanceLevelPercent: 20m);
    }

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
