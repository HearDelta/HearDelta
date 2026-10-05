using HearDelta.App.Services;
using HearDelta.App.ViewModels;
using HearDelta.Core;

namespace HearDelta.App.Tests;

public sealed class HearingThresholdHistoryViewModelTests
{
    [Fact]
    public void RefreshShowsNewestTestAndItsFrequencySortedResults()
    {
        var older = CreateSession(DateTimeOffset.Parse("2026-09-03T08:00:00Z"), TestedEar.Left, aborted: true);
        var newer = CreateSession(DateTimeOffset.Parse("2026-09-03T09:00:00Z"), TestedEar.Right, aborted: false);
        var repository = new InMemoryThresholdRepository([older, newer]);
        var viewModel = CreateViewModel(repository, new ConfirmationService(true));
        viewModel.SetPerson(TestPerson);

        viewModel.RefreshHearingThresholdHistoryCommand.Execute(null);

        Assert.Equal([newer.Id, older.Id], viewModel.HearingThresholdTests.Select(item => item.Id));
        Assert.Equal(newer.Id, viewModel.SelectedThresholdTest?.Id);
        Assert.Equal(TestedEar.Right, viewModel.SelectedThresholdTest?.Ear);
        Assert.Equal(newer.Observations.Count, viewModel.SelectedThresholdResults.Count);
        Assert.True(viewModel.SelectedThresholdResults.Select(row => row.FrequencyHz).SequenceEqual(
            viewModel.SelectedThresholdResults.Select(row => row.FrequencyHz).OrderBy(value => value)));
        Assert.Equal("Abgeschlossen", viewModel.SelectedThresholdTest?.StatusText);
        Assert.True(viewModel.HasHearingThresholdTests);
        Assert.False(viewModel.HasNoHearingThresholdTests);
    }

    [Fact]
    public void DeleteRequiresConfirmationAndKeepsRemainingTestSelected()
    {
        var first = CreateSession(DateTimeOffset.Parse("2026-09-03T09:00:00Z"), TestedEar.Left, aborted: false);
        var second = CreateSession(DateTimeOffset.Parse("2026-09-03T08:00:00Z"), TestedEar.Right, aborted: true);
        var repository = new InMemoryThresholdRepository([first, second]);
        var confirmation = new ConfirmationService(false);
        var viewModel = CreateViewModel(repository, confirmation);
        viewModel.SetPerson(TestPerson);

        viewModel.DeleteSelectedThresholdTestCommand.Execute(null);
        Assert.Equal(2, repository.Sessions.Count);

        confirmation.Result = true;
        viewModel.DeleteSelectedThresholdTestCommand.Execute(null);

        Assert.DoesNotContain(repository.Sessions, session => session.Id == first.Id);
        Assert.Equal(second.Id, viewModel.SelectedThresholdTest?.Id);
        Assert.Equal("Abgebrochen", viewModel.SelectedThresholdTest?.StatusText);
        Assert.Equal("Hörschwellentest gelöscht.", viewModel.ThresholdStatusMessage);
    }

    [Fact]
    public void OverviewListsNewestMeasurementsWithEarAndStatusTones()
    {
        var older = CreateSession(DateTimeOffset.Parse("2026-09-03T08:00:00Z"), TestedEar.Left, aborted: true);
        var newer = CreateSession(DateTimeOffset.Parse("2026-09-03T09:00:00Z"), TestedEar.Right, aborted: false);
        var history = CreateViewModel(new InMemoryThresholdRepository([older, newer]), new ConfirmationService(true));
        var overview = new PersonDetailViewModel(history);

        overview.SetPerson(TestPerson);

        Assert.Equal([newer.Id, older.Id], overview.RecentMeasurements.Select(item => item.ThresholdTest!.Id));
        Assert.Equal([BadgeTone.EarRight, BadgeTone.EarLeft], overview.RecentMeasurements.Select(item => item.EarTone));
        Assert.Equal([BadgeTone.Success, BadgeTone.Warning], overview.RecentMeasurements.Select(item => item.StatusTone));
        Assert.Equal(["R", "L"], overview.RecentMeasurements.Select(item => item.EarLetter));
        Assert.Equal(2, overview.MeasurementCount);
        Assert.Contains("2 Messungen", overview.Subtitle);
        Assert.True(overview.HasRecentMeasurements);
        Assert.False(overview.HasNoRecentMeasurements);
    }

    [Fact]
    public void OpeningRecentThresholdTestSelectsItInHistory()
    {
        var older = CreateSession(DateTimeOffset.Parse("2026-09-03T08:00:00Z"), TestedEar.Left, aborted: true);
        var newer = CreateSession(DateTimeOffset.Parse("2026-09-03T09:00:00Z"), TestedEar.Right, aborted: false);
        var history = CreateViewModel(new InMemoryThresholdRepository([older, newer]), new ConfirmationService(true));
        var overview = new PersonDetailViewModel(history);
        var historyRequested = false;
        overview.HistoryRequested += () => historyRequested = true;
        overview.SetPerson(TestPerson);

        overview.OpenRecentCommand.Execute(overview.RecentMeasurements[1]);

        Assert.True(historyRequested);
        Assert.Equal(1, history.SelectedTabIndex);
        Assert.Equal(older.Id, history.SelectedThresholdTest?.Id);
    }

    [Fact]
    public void OverviewWithoutMeasurementsShowsEmptyState()
    {
        var overview = new PersonDetailViewModel(CreateViewModel(new InMemoryThresholdRepository([]), new ConfirmationService(true)));

        overview.SetPerson(TestPerson);

        Assert.Empty(overview.RecentMeasurements);
        Assert.True(overview.HasNoRecentMeasurements);
        Assert.Contains("noch keine Messungen", overview.Subtitle);
    }

    [Fact]
    public void TickedThresholdTestsAreComparedAsCurvesAndTwoAsFrequencyTable()
    {
        var sessions = Enumerable.Range(0, 5)
            .Select(index => CreateSession(DateTimeOffset.Parse("2026-09-03T08:00:00Z").AddHours(index), index % 2 == 0 ? TestedEar.Right : TestedEar.Left, aborted: false))
            .ToArray();
        var viewModel = CreateViewModel(new InMemoryThresholdRepository(sessions), new ConfirmationService(true));
        viewModel.SetPerson(TestPerson);

        Assert.False(viewModel.HasThresholdComparison);
        Assert.True(viewModel.IsThresholdSingleView);

        viewModel.HearingThresholdTests[0].IsComparisonSelected = true;
        Assert.False(viewModel.HasThresholdComparison);
        Assert.StartsWith("Noch mindestens", viewModel.ThresholdComparisonHint);

        viewModel.HearingThresholdTests[1].IsComparisonSelected = true;
        Assert.True(viewModel.HasThresholdComparison);
        Assert.False(viewModel.IsThresholdSingleView);
        Assert.Equal(2, viewModel.ThresholdComparisonSeries.Count);
        Assert.StartsWith("1 · ", viewModel.ThresholdComparisonSeries[0].Label);
        Assert.True(viewModel.HasThresholdComparisonTable);
        Assert.NotEmpty(viewModel.ThresholdComparisonRows);
        Assert.False(viewModel.HasThresholdComparisonWarning);

        viewModel.HearingThresholdTests[2].IsComparisonSelected = true;
        viewModel.HearingThresholdTests[3].IsComparisonSelected = true;
        Assert.Equal(4, viewModel.ThresholdComparisonSeries.Count);
        Assert.False(viewModel.HasThresholdComparisonTable);

        viewModel.HearingThresholdTests[4].IsComparisonSelected = true;
        Assert.False(viewModel.HearingThresholdTests[4].IsComparisonSelected);
        Assert.Equal(4, viewModel.ThresholdComparisonCount);
        Assert.Contains("höchstens 4", viewModel.ThresholdStatusMessage);

        viewModel.ClearThresholdComparisonCommand.Execute(null);
        Assert.Equal(0, viewModel.ThresholdComparisonCount);
        Assert.Empty(viewModel.ThresholdComparisonSeries);
        Assert.True(viewModel.IsThresholdSingleView);
    }

    [Fact]
    public void StatusLineFollowsTheVisibleTab()
    {
        var session = CreateSession(DateTimeOffset.Parse("2026-09-03T09:00:00Z"), TestedEar.Right, aborted: false);
        var viewModel = CreateViewModel(new InMemoryThresholdRepository([session]), new ConfirmationService(true));
        viewModel.SetPerson(TestPerson);
        var changed = new List<string?>();
        viewModel.PropertyChanged += (_, args) => changed.Add(args.PropertyName);

        Assert.Equal(0, viewModel.SelectedTabIndex);
        Assert.Equal("Noch keine Messsitzung gespeichert.", viewModel.StatusMessage);

        viewModel.SelectedTabIndex = 1;

        Assert.Equal("1 Hörschwellentest geladen.", viewModel.StatusMessage);
        Assert.Contains(nameof(HistoryViewModel.StatusMessage), changed);
    }

    [Fact]
    public void SelectedTestAndCurveComparisonArePrintedWithChartsAndTables()
    {
        var sessions = Enumerable.Range(0, 2)
            .Select(index => CreateSession(DateTimeOffset.Parse("2026-09-03T08:00:00Z").AddHours(index), TestedEar.Right, aborted: false))
            .ToArray();
        var printer = new RecordingPrinter();
        var viewModel = CreateViewModel(new InMemoryThresholdRepository(sessions), new ConfirmationService(true), printer);
        viewModel.SetPerson(TestPerson);

        viewModel.PrintSelectedThresholdCommand.Execute(null);

        var single = Assert.Single(printer.Reports);
        Assert.Equal(viewModel.SelectedThresholdTest!.Name, single.Title);
        Assert.Contains(single.Blocks.OfType<PrintFacts>().SelectMany(facts => facts.Items), fact => fact is { Label: "Person", Value: "Testperson" });
        var chart = Assert.Single(single.Blocks.OfType<PrintThresholdChart>());
        Assert.Equal(TestedEar.Right, chart.Ear);
        Assert.Equal(2, chart.Rows.Count);
        Assert.Equal(2, single.Blocks.OfType<PrintTable>().Single().Rows.Count);
        Assert.Contains("an den Drucker übergeben", viewModel.ThresholdStatusMessage);

        viewModel.HearingThresholdTests[0].IsComparisonSelected = true;
        viewModel.HearingThresholdTests[1].IsComparisonSelected = true;
        viewModel.PrintThresholdComparisonCommand.Execute(null);

        var comparison = printer.Reports[1];
        Assert.Equal("Vergleich der Hörschwellen", comparison.Title);
        Assert.Equal(2, comparison.Blocks.OfType<PrintThresholdChart>().Single().Series.Count);
        Assert.Contains(comparison.Blocks.OfType<PrintTable>(), table => table.Headers.Contains("Test 2 gegenüber Test 1"));
    }

    [Fact]
    public void CancelledPrintKeepsStatusAndFailedPrintIsReported()
    {
        var session = CreateSession(DateTimeOffset.Parse("2026-09-03T09:00:00Z"), TestedEar.Left, aborted: false);
        var printer = new RecordingPrinter { Result = false };
        var viewModel = CreateViewModel(new InMemoryThresholdRepository([session]), new ConfirmationService(true), printer);
        viewModel.SetPerson(TestPerson);
        var status = viewModel.ThresholdStatusMessage;

        viewModel.PrintSelectedThresholdCommand.Execute(null);
        Assert.Equal(status, viewModel.ThresholdStatusMessage);

        printer.Failure = new InvalidOperationException("Drucker offline");
        viewModel.PrintSelectedThresholdCommand.Execute(null);
        Assert.Equal("Drucken fehlgeschlagen: Drucker offline", viewModel.ThresholdStatusMessage);
    }

    private static HistoryViewModel CreateViewModel(
        IHearingThresholdSessionRepository repository,
        IUserConfirmationService confirmation,
        IReportPrinter? printer = null) => new(
            new EmptyMeasurementRepository(),
            repository,
            new StimulusCatalogService().Load(StimulusCatalogService.GetBundledPackDirectory()),
            confirmation,
            printer: printer);

    private static HearingThresholdSession CreateSession(
        DateTimeOffset startedAt,
        TestedEar ear,
        bool aborted)
    {
        var session = HearingThresholdSessionFactory.Create(
            ear,
            ThresholdToneOrder.Ascending,
            CreateHardware(),
            startedAt,
            randomizationSeed: 12);
        var observations = session.Tones.Take(2).Reverse().Select((tone, index) =>
            new HearingThresholdObservation(
                tone.PresentationOrder,
                tone.MidiNoteNumber,
                true,
                -70m + index,
                startedAt.AddSeconds(index + 1),
                new ThresholdTonePresentationRecord(
                    session.Hardware.EndpointId,
                    session.Hardware.EndpointName,
                    tone.FrequencyHz,
                    session.StartAttenuationDbfs,
                    -70m + index,
                    session.MaximumAttenuationDbfs,
                    session.LevelStepDb,
                    session.Hardware.SampleRate,
                    startedAt,
                    startedAt.AddSeconds(index + 1),
                    false,
                    session.SignalPattern)))
            .ToArray();
        var completedAt = startedAt.AddMinutes(2);
        return session with
        {
            Observations = observations,
            CompletedAt = completedAt,
            AbortedAt = aborted ? completedAt : null
        };
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
        -100m,
        -6m,
        false,
        DateTimeOffset.Parse("2026-09-03T07:30:00Z"));

    private static PersonProfile TestPerson => new(
        Guid.Parse("174d7ccd-62a1-4d04-b630-b5db14da6901"), "Testperson", null,
        DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);

    private sealed class EmptyMeasurementRepository : IMeasurementSessionRepository
    {
        public IReadOnlyList<PairedMeasurementSession> LoadAll() => [];
        public IReadOnlyList<PairedMeasurementSession> LoadForPerson(Guid personId) => [];
        public PairedMeasurementSession? Load(Guid id) => null;
        public void Save(Guid personId, PairedMeasurementSession session) => throw new NotSupportedException();
        public void Delete(Guid id) => throw new NotSupportedException();
    }

    private sealed class InMemoryThresholdRepository(IEnumerable<HearingThresholdSession> sessions)
        : IHearingThresholdSessionRepository
    {
        public List<HearingThresholdSession> Sessions { get; } = [.. sessions];
        public IReadOnlyList<HearingThresholdSession> LoadAll() => Sessions;
        public IReadOnlyList<HearingThresholdSession> LoadForPerson(Guid personId) => Sessions;
        public HearingThresholdSession? Load(Guid id) => Sessions.SingleOrDefault(session => session.Id == id);
        public void Save(Guid personId, HearingThresholdSession session)
        {
            Sessions.RemoveAll(existing => existing.Id == session.Id);
            Sessions.Add(session);
        }
        public void Delete(Guid id) => Sessions.RemoveAll(session => session.Id == id);
    }

    private sealed class RecordingPrinter : IReportPrinter
    {
        public List<PrintReport> Reports { get; } = [];
        public bool Result { get; set; } = true;
        public Exception? Failure { get; set; }

        public bool Print(PrintReport report)
        {
            if (Failure is not null)
                throw Failure;
            Reports.Add(report);
            return Result;
        }
    }

    private sealed class ConfirmationService(bool result) : IUserConfirmationService
    {
        public bool Result { get; set; } = result;
        public bool Confirm(string title, string message) => Result;
    }
}
