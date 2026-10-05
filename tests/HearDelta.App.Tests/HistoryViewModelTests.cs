using HearDelta.App.Services;
using HearDelta.App.ViewModels;
using HearDelta.Core;

namespace HearDelta.App.Tests;

public sealed class HistoryViewModelTests
{
    [Fact]
    public void AllSessionsRemainVisibleAndCanBeCombinedAcrossFilters()
    {
        var pack = LoadPack();
        var now = DateTimeOffset.Parse("2026-09-02T12:00:00Z");
        var completedLeft = CreateSession(pack, "Alpha links", TestedEar.Left, now.AddDays(-2), CreateHardware(), 12, 18);
        var completedRight = CreateSession(pack, "Beta rechts", TestedEar.Right, now.AddDays(-40), CreateHardware(), 14, 19);
        var inProgress = CreateSession(pack, "Gamma links", TestedEar.Left, now.AddHours(-2), CreateHardware(), null, null);
        var viewModel = new HistoryViewModel(
            new FixedSessionRepository(completedLeft, completedRight, inProgress),
            new EmptyThresholdRepository(),
            pack,
            new ConfirmationService(),
            () => now);
        viewModel.SetPerson(TestPerson);

        Assert.Equal(3, viewModel.TotalSessionCount);
        Assert.Equal(3, viewModel.FilteredSessionCount);
        Assert.Contains(viewModel.Sessions, item => item.StatusText == "Begonnen" && !item.CanSelectForComparison);

        viewModel.SelectedEarFilter = viewModel.EarFilters.Single(option => option.Value == TestedEar.Right);
        Assert.Single(viewModel.Sessions);
        Assert.Equal("Beta rechts", viewModel.Sessions[0].HearingAidText);

        viewModel.SelectedEarFilter = viewModel.EarFilters[0];
        viewModel.SelectedPeriodFilter = viewModel.PeriodFilters.Single(option => option.Days == 30);
        viewModel.SelectedCompletionFilter = viewModel.CompletionFilters.Single(option => option.Value == HistoryCompletionFilter.Completed);
        Assert.Single(viewModel.Sessions);
        Assert.Equal("Alpha links", viewModel.Sessions[0].HearingAidText);

        viewModel.SelectedCompletionFilter = viewModel.CompletionFilters[0];
        viewModel.SearchText = "Gamma";
        Assert.Single(viewModel.Sessions);
        Assert.Equal("Begonnen", viewModel.Sessions[0].StatusText);
    }

    [Fact]
    public void WordTestIsDeletedOnlyAfterConfirmationTogetherWithItsAnnotation()
    {
        var pack = LoadPack();
        var startedAt = DateTimeOffset.Parse("2026-09-01T08:00:00Z");
        var keep = CreateSession(pack, "Alpha links", TestedEar.Left, startedAt, CreateHardware(), 12, 18);
        var remove = CreateSession(pack, "Beta links", TestedEar.Left, startedAt.AddHours(1), CreateHardware(), 14, 20);
        var annotations = new InMemoryMeasurementAnnotationRepository();
        annotations.Save(MeasurementAnnotationRules.Create(remove.Id, "Beta", "Kommentar", startedAt));
        var confirmation = new FixedConfirmation(false);
        var viewModel = new HistoryViewModel(
            new FixedSessionRepository(keep, remove),
            new EmptyThresholdRepository(),
            pack,
            confirmation,
            annotations: annotations);
        viewModel.SetPerson(TestPerson);
        var reloads = 0;
        viewModel.Reloaded += () => reloads++;

        viewModel.DeleteWordTestCommand.Execute(viewModel.Sessions.Single(item => item.Id == remove.Id));
        Assert.Equal(2, viewModel.TotalSessionCount);

        confirmation.Result = true;
        viewModel.DeleteWordTestCommand.Execute(viewModel.Sessions.Single(item => item.Id == remove.Id));

        Assert.Equal(keep.Id, Assert.Single(viewModel.Sessions).Id);
        Assert.Empty(annotations.LoadAll());
        Assert.Equal(1, reloads);
        Assert.Contains("gelöscht", viewModel.WordStatusMessage);
    }

    [Fact]
    public void TwoCompatibleSessionsExposeIntervalsAndDirectComparison()
    {
        var pack = LoadPack();
        var startedAt = DateTimeOffset.Parse("2026-09-01T08:00:00Z");
        var first = CreateSession(pack, "Alpha links", TestedEar.Left, startedAt, CreateHardware(), 12, 18);
        var second = CreateSession(pack, "Beta links", TestedEar.Left, startedAt.AddHours(1), CreateHardware(), 14, 20);
        var viewModel = new HistoryViewModel(
            new FixedSessionRepository(first, second),
            new EmptyThresholdRepository(),
            pack,
            new ConfirmationService());
        viewModel.SetPerson(TestPerson);

        viewModel.Sessions[0].IsSelected = true;
        viewModel.Sessions[1].IsSelected = true;

        Assert.True(viewModel.HasComparison);
        Assert.True(viewModel.IsDirectlyComparable);
        Assert.Contains("95 %-Intervall", viewModel.FirstComparison!.WithEstimateText);
        Assert.Contains("direkt", viewModel.ComparisonAssessmentText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void WordComparisonReportListsBothMeasurementsSideBySide()
    {
        var pack = LoadPack();
        var startedAt = DateTimeOffset.Parse("2026-09-01T08:00:00Z");
        var first = CreateSession(pack, "Alpha links", TestedEar.Left, startedAt, CreateHardware(), 12, 18);
        var second = CreateSession(pack, "Beta links", TestedEar.Left, startedAt.AddHours(1), CreateHardware(), 14, 20);
        var viewModel = new HistoryViewModel(
            new FixedSessionRepository(first, second),
            new EmptyThresholdRepository(),
            pack,
            new ConfirmationService(),
            () => startedAt.AddDays(1));
        viewModel.SetPerson(TestPerson);
        Assert.Null(viewModel.CreateWordComparisonReport());

        viewModel.Sessions[0].IsSelected = true;
        viewModel.Sessions[1].IsSelected = true;
        var report = viewModel.CreateWordComparisonReport();

        Assert.NotNull(report);
        var table = Assert.Single(report.Blocks.OfType<PrintTable>());
        Assert.Equal(["", "Messung 1", "Messung 2"], table.Headers);
        var hearingAids = table.Rows.Single(row => row[0] == "Hörgerät");
        Assert.Equal(["Hörgerät", "Alpha links", "Beta links"], hearingAids);
        Assert.Contains("95 %-Intervall", table.Rows.Single(row => row[0] == "Mit Hörgerät")[1]);
        Assert.Contains(report.Blocks.OfType<PrintParagraph>(), paragraph => paragraph.Text.Contains("keine dB SPL"));
    }

    [Fact]
    public void ProfileDeviationKeepsBothResultsButRaisesWarning()
    {
        var pack = LoadPack();
        var startedAt = DateTimeOffset.Parse("2026-09-01T08:00:00Z");
        var first = CreateSession(pack, "Alpha links", TestedEar.Left, startedAt, CreateHardware(), 12, 18);
        var changedHardware = CreateHardware() with { Gain = "High (+19 dB)" };
        var second = CreateSession(pack, "Beta links", TestedEar.Left, startedAt.AddHours(1), changedHardware, 14, 20);
        var viewModel = new HistoryViewModel(
            new FixedSessionRepository(first, second),
            new EmptyThresholdRepository(),
            pack,
            new ConfirmationService());
        viewModel.SetPerson(TestPerson);

        viewModel.Sessions[0].IsSelected = true;
        viewModel.Sessions[1].IsSelected = true;

        Assert.True(viewModel.HasComparison);
        Assert.True(viewModel.HasComparisonWarning);
        Assert.False(viewModel.IsDirectlyComparable);
        Assert.Contains("Gain", viewModel.ComparisonAssessmentText);
        Assert.NotEqual("–", viewModel.FirstComparison!.DifferenceEstimateText);
        Assert.NotEqual("–", viewModel.SecondComparison!.DifferenceEstimateText);
    }

    [Fact]
    public void AbortedSessionCanReopenItsPartialResultButCannotBeCompared()
    {
        var pack = LoadPack();
        var startedAt = DateTimeOffset.Parse("2026-09-01T08:00:00Z");
        var unfinished = CreateSession(pack, "Alpha links", TestedEar.Left, startedAt, CreateHardware(), null, null);
        var aborted = unfinished with
        {
            CompletedAt = startedAt.AddMinutes(3),
            AbortedAt = startedAt.AddMinutes(3)
        };
        var viewModel = new HistoryViewModel(
            new FixedSessionRepository(aborted),
            new EmptyThresholdRepository(),
            pack,
            new ConfirmationService());
        viewModel.SetPerson(TestPerson);
        HistorySessionItemViewModel? requested = null;
        viewModel.ResultRequested += item => requested = item;

        var historyItem = Assert.Single(viewModel.Sessions);
        Assert.Equal("Abgebrochen", historyItem.StatusText);
        Assert.True(historyItem.CanShowResult);
        Assert.False(historyItem.CanSelectForComparison);
        Assert.Equal("– → –", historyItem.ScoreText);

        viewModel.SelectedCompletionFilter = viewModel.CompletionFilters.Single(
            option => option.Value == HistoryCompletionFilter.Aborted);
        Assert.Same(historyItem, Assert.Single(viewModel.Sessions));

        viewModel.ShowResultCommand.Execute(historyItem);

        Assert.Same(historyItem, requested);
        Assert.NotNull(historyItem.Result);
        Assert.Equal(0, historyItem.Result.WithoutHearingAid.TotalResponses);
        Assert.Contains("geöffnet", viewModel.StatusMessage);
    }

    [Fact]
    public void FilterSummaryResetAndSelectionHintsFollowTheState()
    {
        var pack = LoadPack();
        var now = DateTimeOffset.Parse("2026-09-02T12:00:00Z");
        var first = CreateSession(pack, "Alpha links", TestedEar.Left, now.AddDays(-2), CreateHardware(), 12, 18);
        var second = CreateSession(pack, "Beta rechts", TestedEar.Right, now.AddDays(-40), CreateHardware(), 14, 11);
        var viewModel = new HistoryViewModel(
            new FixedSessionRepository(first, second),
            new EmptyThresholdRepository(),
            pack,
            new ConfirmationService(),
            () => now);
        viewModel.SetPerson(TestPerson);

        Assert.False(viewModel.HasAnyFilter);
        Assert.Equal("Weitere Filter", viewModel.MoreFiltersHeader);
        Assert.Equal(BadgeTone.WithAid, viewModel.Sessions.Single(item => item.HearingAidText == "Alpha links").DifferenceTone);
        Assert.Equal(BadgeTone.Danger, viewModel.Sessions.Single(item => item.HearingAidText == "Beta rechts").DifferenceTone);

        viewModel.SelectedEarFilter = viewModel.EarFilters.Single(option => option.Value == TestedEar.Left);
        viewModel.SelectedPeriodFilter = viewModel.PeriodFilters.Single(option => option.Days == 30);
        Assert.True(viewModel.HasAnyFilter);
        Assert.Equal(1, viewModel.ActiveMoreFilterCount);
        Assert.Equal("Weitere Filter (1 aktiv)", viewModel.MoreFiltersHeader);

        viewModel.ResetFiltersCommand.Execute(null);
        Assert.False(viewModel.HasAnyFilter);
        Assert.Equal(2, viewModel.FilteredSessionCount);
        Assert.Null(viewModel.SelectedEarFilter.Value);

        Assert.StartsWith("Zum Vergleichen", viewModel.SelectionHintText);
        viewModel.Sessions[0].IsSelected = true;
        Assert.StartsWith("Noch eine", viewModel.SelectionHintText);
        viewModel.Sessions[1].IsSelected = true;
        Assert.True(viewModel.HasComparison);
        Assert.Equal(2, viewModel.SelectedCount);
    }

    private static LoadedStimulusPack LoadPack() =>
        new StimulusCatalogService().Load(StimulusCatalogService.GetBundledPackDirectory());

    private static PairedMeasurementSession CreateSession(
        LoadedStimulusPack pack,
        string deviceName,
        TestedEar ear,
        DateTimeOffset startedAt,
        MeasurementHardwareSnapshot hardware,
        int? correctWithout,
        int? correctWith)
    {
        var lists = MeasurementRunPlanner.SelectListIds(pack.Catalog, SpeechMaterial.PhonemeContrasts, 20);
        var session = PairedMeasurementSessionFactory.CreateRandomized(
            ear,
            new HearingAidSnapshot(
                HearingAidIdentity.CreateStableId("Test", deviceName, ear),
                "Test",
                deviceName,
                deviceName,
                ear,
                "Programm 1",
                "0"),
            SpeechMaterial.PhonemeContrasts,
            ListeningEnvironment.Quiet,
            lists.FirstListId,
            lists.SecondListId,
            hardware,
            startedAt,
            20,
            pack.MaterialIdentity,
            MeasurementSessionContracts.PhonemeContrastMeasurement);
        if (correctWithout is null || correctWith is null)
            return session;

        var plan = MeasurementRunPlanner.Create(session, pack.Catalog);
        var stimuli = pack.Catalog.Lists.SelectMany(list => list.Items)
            .ToDictionary(item => item.Id, StringComparer.Ordinal);
        var completedBlocks = plan.Blocks.Select(plannedBlock =>
        {
            var source = session.Blocks.Single(block => block.Id == plannedBlock.BlockId);
            var correct = plannedBlock.Condition == HearingAidCondition.WithoutHearingAid
                ? correctWithout.Value
                : correctWith.Value;
            var responses = plannedBlock.Stimuli.Select((planned, index) =>
            {
                var stimulus = stimuli[planned.StimulusId];
                var entered = index < correct ? stimulus.CanonicalResponse : "falsche Antwort";
                return new RawMeasurementResponse(
                    planned.PresentationOrder,
                    planned.StimulusId,
                    entered,
                    startedAt.AddMinutes(plannedBlock.PresentationOrder).AddSeconds(index),
                    CreatePresentation(pack, planned.StimulusId, hardware, startedAt.AddSeconds(index)));
            }).ToArray();
            return source with
            {
                RawResponses = responses,
                SetupConfirmation = new MeasurementSetupConfirmation(true, true, startedAt.AddSeconds(30))
            };
        }).ToArray();
        return session with
        {
            CompletedAt = startedAt.AddMinutes(10),
            Blocks = completedBlocks
        };
    }

    private static StimulusPresentationRecord CreatePresentation(
        LoadedStimulusPack pack,
        string stimulusId,
        MeasurementHardwareSnapshot hardware,
        DateTimeOffset startedAt) => new(
            pack.Catalog.Id,
            pack.Catalog.Version,
            stimulusId,
            pack.GetAudioAsset(stimulusId).Sha256,
            hardware.EndpointId,
            hardware.EndpointName,
            new StimulusRenderMetadata(
                StimulusAudioRenderer.RendererVersion,
                "none",
                StimulusAudioRenderer.SourcePeakNormalizationDbfs,
                hardware.StartVolumeDb,
                null,
                42,
                hardware.SampleRate,
                0.01f),
            startedAt,
            startedAt.AddMilliseconds(500));

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

    private static PersonProfile TestPerson => new(
        Guid.Parse("174d7ccd-62a1-4d04-b630-b5db14da6901"), "Testperson", null,
        DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);

    private sealed class FixedSessionRepository(params PairedMeasurementSession[] initial) : IMeasurementSessionRepository
    {
        private readonly List<PairedMeasurementSession> sessions = [.. initial];

        public IReadOnlyList<PairedMeasurementSession> LoadAll() => sessions.ToArray();
        public IReadOnlyList<PairedMeasurementSession> LoadForPerson(Guid personId) => sessions.ToArray();
        public PairedMeasurementSession? Load(Guid id) => sessions.SingleOrDefault(session => session.Id == id);
        public void Save(Guid personId, PairedMeasurementSession session) => throw new NotSupportedException();
        public void Delete(Guid id) => sessions.RemoveAll(session => session.Id == id);
    }

    private sealed class FixedConfirmation(bool result) : IUserConfirmationService
    {
        public bool Result { get; set; } = result;
        public bool Confirm(string title, string message) => Result;
    }

    private sealed class EmptyThresholdRepository : IHearingThresholdSessionRepository
    {
        public IReadOnlyList<HearingThresholdSession> LoadAll() => [];
        public IReadOnlyList<HearingThresholdSession> LoadForPerson(Guid personId) => [];
        public HearingThresholdSession? Load(Guid id) => null;
        public void Save(Guid personId, HearingThresholdSession session) => throw new NotSupportedException();
        public void Delete(Guid id) => throw new NotSupportedException();
    }

    private sealed class ConfirmationService : IUserConfirmationService
    {
        public bool Confirm(string title, string message) => true;
    }
}
