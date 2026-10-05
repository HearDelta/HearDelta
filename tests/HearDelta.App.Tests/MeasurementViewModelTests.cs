using HearDelta.App.Services;
using HearDelta.App.ViewModels;
using HearDelta.Core;

namespace HearDelta.App.Tests;

public sealed class MeasurementViewModelTests
{
    [Fact]
    public async Task GuidedRunPersistsEveryAnswerAndProducesSeparateResults()
    {
        var pack = new StimulusCatalogService().Load(StimulusCatalogService.GetBundledPackDirectory());
        var profile = CreateProfile();
        var repository = new RecordingSessionRepository();
        var playback = new RecordingPlaybackService();
        var timestamp = DateTimeOffset.Parse("2026-09-01T14:30:00Z");
        var viewModel = new MeasurementViewModel(
            pack,
            [profile],
            new FixedAudioEndpointService(CreateEndpoint()),
            playback,
            repository,
            now: () => timestamp = timestamp.AddSeconds(1),
            createSeed: () => 20260901,
            loadHearingAids: TestHearingAids);

        viewModel.SetPerson(TestPerson);
        viewModel.StartMeasurementCommand.Execute(null);

        Assert.Equal(MeasurementStage.Preparation, viewModel.Stage);
        Assert.Single(repository.Saved);
        await CompletePreparationAsync(viewModel);
        await AnswerCurrentBlockAsync(viewModel);
        Assert.Equal(MeasurementStage.Preparation, viewModel.Stage);
        await CompletePreparationAsync(viewModel);
        await AnswerCurrentBlockAsync(viewModel);

        Assert.Equal(MeasurementStage.Results, viewModel.Stage);
        Assert.NotNull(viewModel.Result);
        Assert.True(viewModel.HasConfusionCells);
        Assert.Equal(50, viewModel.ConfusionCells.Sum(cell => cell.Count));
        Assert.Equal(50, playback.PlayedStimulusIds.Count);
        Assert.Equal(50, viewModel.CurrentSession!.Blocks.Sum(block => block.RawResponses.Count));
        Assert.All(
            viewModel.CurrentSession.Blocks.SelectMany(block => block.RawResponses),
            response =>
            {
                Assert.NotNull(response.Presentation);
                Assert.Equal(profile.EndpointId, response.Presentation!.EndpointId);
                Assert.Equal(response.StimulusId, response.Presentation.StimulusId);
            });
        Assert.Equal(53, repository.Saved.Count);
        Assert.NotNull(repository.Saved[^1].CompletedAt);
        Assert.All(viewModel.CurrentSession.Blocks, block =>
        {
            Assert.NotNull(block.SetupConfirmation);
            Assert.True(block.SetupConfirmation!.CorrectEarAndChannelConfirmed);
            Assert.True(block.SetupConfirmation.HeadphoneFitAndFeedbackChecked);
        });
        Assert.Equal(25, viewModel.Result.WithHearingAid.TotalResponses);
        Assert.Equal(25, viewModel.Result.WithoutHearingAid.TotalResponses);
    }

    [Fact]
    public void StartedMeasurementStoresNameDefaultingToHearingAidAndComment()
    {
        var pack = new StimulusCatalogService().Load(StimulusCatalogService.GetBundledPackDirectory());
        var repository = new RecordingSessionRepository();
        var annotations = new InMemoryMeasurementAnnotationRepository();
        var viewModel = new MeasurementViewModel(
            pack,
            [CreateProfile()],
            new FixedAudioEndpointService(CreateEndpoint()),
            new RecordingPlaybackService(),
            repository,
            createSeed: () => 20260901,
            loadHearingAids: TestHearingAids,
            annotations: annotations);

        viewModel.SetPerson(TestPerson);
        Assert.Equal(viewModel.HearingAidDisplayName, viewModel.Annotation.Name);
        viewModel.Annotation.Comment = " Programm Sprache im Lärm ";
        viewModel.StartMeasurementCommand.Execute(null);

        var annotation = annotations.LoadAll()[Assert.Single(repository.Saved).Id];
        Assert.Equal(viewModel.HearingAidDisplayName, annotation.Name);
        Assert.Equal("Programm Sprache im Lärm", annotation.Comment);
    }

    [Fact]
    public void MissingStoredEndpointBlocksSessionBeforeAnyPlaybackOrSave()
    {
        var pack = new StimulusCatalogService().Load(StimulusCatalogService.GetBundledPackDirectory());
        var repository = new RecordingSessionRepository();
        var playback = new RecordingPlaybackService();
        var viewModel = new MeasurementViewModel(
            pack,
            [CreateProfile()],
            new FixedAudioEndpointService(),
            playback,
            repository,
            createSeed: () => 20,
            loadHearingAids: TestHearingAids);

        viewModel.SetPerson(TestPerson);
        viewModel.StartMeasurementCommand.Execute(null);

        Assert.Equal(MeasurementStage.Setup, viewModel.Stage);
        Assert.Contains("kein Ersatzgerät", viewModel.StatusMessage);
        Assert.Empty(repository.Saved);
        Assert.Empty(playback.PlayedStimulusIds);
    }

    [Fact]
    public void PreparedSeriesUsesThePlannedSeedListsAndConditionOrder()
    {
        var pack = new StimulusCatalogService().Load(StimulusCatalogService.GetBundledPackDirectory());
        var repository = new RecordingSessionRepository();
        var viewModel = new MeasurementViewModel(
            pack,
            [CreateProfile()],
            new FixedAudioEndpointService(CreateEndpoint()),
            new RecordingPlaybackService(),
            repository,
            createSeed: () => 20260906,
            loadHearingAids: TestHearingAids);

        viewModel.SetPerson(TestPerson);
        viewModel.SeriesPairCount = 6;
        viewModel.PrepareSeriesCommand.Execute(null);
        viewModel.StartMeasurementCommand.Execute(null);

        var expected = MeasurementSeriesPlanner.Create(
            pack.Catalog,
            SpeechMaterial.PhonemeContrasts,
            6,
            20260906).Rounds[0];
        Assert.True(viewModel.HasPreparedSeries);
        Assert.Equal(MeasurementStage.Preparation, viewModel.Stage);
        Assert.Equal(expected.SessionSeed, viewModel.CurrentSession!.RandomizationSeed);
        Assert.Equal(expected.FirstCondition, viewModel.CurrentSession.Blocks[0].Condition);
        Assert.Equal(expected.FirstListId, viewModel.CurrentSession.Blocks[0].StimulusListId);
        Assert.Equal(expected.SecondListId, viewModel.CurrentSession.Blocks[1].StimulusListId);
    }

    [Fact]
    public void PreparedSeriesBelongsToThePersonItWasPlannedFor()
    {
        var pack = new StimulusCatalogService().Load(StimulusCatalogService.GetBundledPackDirectory());
        var viewModel = new MeasurementViewModel(
            pack,
            [CreateProfile()],
            new FixedAudioEndpointService(CreateEndpoint()),
            new RecordingPlaybackService(),
            new RecordingSessionRepository(),
            series: new InMemoryMeasurementSeriesRepository(),
            createSeed: () => 20260906,
            loadHearingAids: TestHearingAids);
        var otherPerson = TestPerson with { Id = Guid.NewGuid(), DisplayName = "Andere Person" };

        viewModel.PrepareSeriesCommand.Execute(null);
        Assert.False(viewModel.HasPreparedSeries);

        viewModel.SetPerson(TestPerson);
        viewModel.PrepareSeriesCommand.Execute(null);
        Assert.True(viewModel.HasPreparedSeries);

        viewModel.SetPerson(otherPerson);
        Assert.False(viewModel.HasPreparedSeries);

        viewModel.SetPerson(TestPerson);
        Assert.True(viewModel.HasPreparedSeries);
    }

    [Fact]
    public async Task AbortAfterAnswersMarksSessionAndShowsPartialResults()
    {
        var pack = new StimulusCatalogService().Load(StimulusCatalogService.GetBundledPackDirectory());
        var repository = new RecordingSessionRepository();
        var viewModel = new MeasurementViewModel(
            pack,
            [CreateProfile()],
            new FixedAudioEndpointService(CreateEndpoint()),
            new RecordingPlaybackService(),
            repository,
            createSeed: () => 20,
            loadHearingAids: TestHearingAids);

        viewModel.SetPerson(TestPerson);
        viewModel.StartMeasurementCommand.Execute(null);
        await CompletePreparationAsync(viewModel);
        for (var index = 0; index < 3; index++)
            await viewModel.SelectAnswerCommand.ExecuteAsync(viewModel.ResponseAlternatives[0]);

        viewModel.AbortMeasurementCommand.Execute(null);

        Assert.Equal(MeasurementStage.Results, viewModel.Stage);
        Assert.True(viewModel.WasAborted);
        Assert.Equal(viewModel.CurrentSession!.CompletedAt, viewModel.CurrentSession.AbortedAt);
        Assert.Equal(3, viewModel.Result!.WithoutHearingAid.TotalResponses);
        Assert.Equal(0, viewModel.Result.WithHearingAid.TotalResponses);
        Assert.Equal(viewModel.CurrentSession.AbortedAt, repository.Saved[^1].AbortedAt);
    }

    [Fact]
    public void AbortDuringBlockPreparationMarksSessionAndShowsEmptyPartialResult()
    {
        var pack = new StimulusCatalogService().Load(StimulusCatalogService.GetBundledPackDirectory());
        var repository = new RecordingSessionRepository();
        var viewModel = new MeasurementViewModel(
            pack,
            [CreateProfile()],
            new FixedAudioEndpointService(CreateEndpoint()),
            new RecordingPlaybackService(),
            repository,
            createSeed: () => 20,
            loadHearingAids: TestHearingAids);

        viewModel.SetPerson(TestPerson);
        viewModel.StartMeasurementCommand.Execute(null);
        viewModel.AbortMeasurementCommand.Execute(null);

        Assert.Equal(MeasurementStage.Results, viewModel.Stage);
        Assert.True(viewModel.WasAborted);
        Assert.Equal(0, viewModel.Result!.WithoutHearingAid.TotalResponses);
        Assert.Equal(0, viewModel.Result.WithHearingAid.TotalResponses);
        Assert.Equal(viewModel.CurrentSession!.AbortedAt, repository.Saved[^1].AbortedAt);
    }

    [Fact]
    public async Task AbortDuringPlaybackCancelsAudioAndShowsEmptyPartialResult()
    {
        var pack = new StimulusCatalogService().Load(StimulusCatalogService.GetBundledPackDirectory());
        var playback = new BlockingPlaybackService();
        var viewModel = new MeasurementViewModel(
            pack,
            [CreateProfile()],
            new FixedAudioEndpointService(CreateEndpoint()),
            playback,
            new RecordingSessionRepository(),
            createSeed: () => 20,
            loadHearingAids: TestHearingAids);

        viewModel.SetPerson(TestPerson);
        viewModel.StartMeasurementCommand.Execute(null);
        viewModel.CorrectEarConfirmed = true;
        viewModel.HeadphoneFitConfirmed = true;
        var startBlock = viewModel.StartBlockCommand.ExecuteAsync(null);
        await playback.Started.Task;

        viewModel.AbortMeasurementCommand.Execute(null);
        await startBlock;

        Assert.True(playback.WasCancelled);
        Assert.Equal(MeasurementStage.Results, viewModel.Stage);
        Assert.True(viewModel.WasAborted);
        Assert.Equal(0, viewModel.Result!.WithoutHearingAid.TotalResponses);
        Assert.Equal(0, viewModel.Result.WithHearingAid.TotalResponses);
    }

    [Fact]
    public async Task PauseCancelsCurrentWordAndResumeReplaysItWithoutRecordingAnAnswer()
    {
        var pack = new StimulusCatalogService().Load(StimulusCatalogService.GetBundledPackDirectory());
        var playback = new PauseThenCompletePlaybackService();
        var viewModel = new MeasurementViewModel(
            pack,
            [CreateProfile()],
            new FixedAudioEndpointService(CreateEndpoint()),
            playback,
            new RecordingSessionRepository(),
            createSeed: () => 20,
            loadHearingAids: TestHearingAids);

        viewModel.SetPerson(TestPerson);
        viewModel.StartMeasurementCommand.Execute(null);
        viewModel.CorrectEarConfirmed = true;
        viewModel.HeadphoneFitConfirmed = true;
        var startBlock = viewModel.StartBlockCommand.ExecuteAsync(null);
        await playback.FirstStarted.Task;

        await viewModel.TogglePauseCommand.ExecuteAsync(null);
        await startBlock;

        Assert.True(playback.FirstWasCancelled);
        Assert.True(viewModel.IsPaused);
        Assert.False(viewModel.CanAnswer);
        Assert.Empty(viewModel.ResponseAlternatives);
        Assert.Empty(viewModel.CurrentSession!.Blocks.SelectMany(block => block.RawResponses));

        await viewModel.TogglePauseCommand.ExecuteAsync(null);

        Assert.False(viewModel.IsPaused);
        Assert.True(viewModel.CanAnswer);
        Assert.Equal(2, playback.PlayedStimulusIds.Count);
        Assert.Equal(playback.PlayedStimulusIds[0], playback.PlayedStimulusIds[1]);
    }

    [Fact]
    public async Task NumericAnswerSubmissionAdvancesDirectlyToNextStimulus()
    {
        var catalogService = new StimulusCatalogService();
        var pack = catalogService.Load(StimulusCatalogService.GetBundledPackDirectory());
        var numberPack = catalogService.Load(Path.Combine(
            AppContext.BaseDirectory,
            "stimuli",
            "de-DE",
            "personal-cardinal-numbers-christoph-v1"));
        var playback = new RecordingPlaybackService();
        var viewModel = new MeasurementViewModel(
            pack,
            [CreateProfile()],
            new FixedAudioEndpointService(CreateEndpoint()),
            playback,
            new RecordingSessionRepository(),
            createSeed: () => 20,
            numberPacks: [numberPack],
            loadHearingAids: TestHearingAids);
        viewModel.SelectedMaterialOption = viewModel.MaterialOptions[1];

        viewModel.SetPerson(TestPerson);
        viewModel.StartMeasurementCommand.Execute(null);
        viewModel.CorrectEarConfirmed = true;
        viewModel.HeadphoneFitConfirmed = true;
        await viewModel.StartBlockCommand.ExecuteAsync(null);
        viewModel.NumericAnswer = "123";

        await viewModel.SubmitNumericAnswerCommand.ExecuteAsync(null);

        Assert.Equal(1, viewModel.CurrentStimulusIndex);
        Assert.Equal(2, playback.PlayedStimulusIds.Count);
        Assert.Equal("", viewModel.NumericAnswer);
        Assert.True(viewModel.CanAnswer);
        Assert.Equal(
            "123",
            Assert.Single(viewModel.CurrentSession!.Blocks[0].RawResponses).EnteredText);
    }

    [Fact]
    public async Task VolumeChangePlaysTestWordAndMeasurementUsesTheChosenLevel()
    {
        var pack = new StimulusCatalogService().Load(StimulusCatalogService.GetBundledPackDirectory());
        var playback = new RecordingPlaybackService();
        var repository = new RecordingSessionRepository();
        var viewModel = new MeasurementViewModel(
            pack,
            [CreateProfile()],
            new FixedAudioEndpointService(CreateEndpoint()),
            playback,
            repository,
            createSeed: () => 20260901,
            loadHearingAids: TestHearingAids,
            testWordDelay: TimeSpan.Zero);
        viewModel.SetPerson(TestPerson);
        Assert.Equal(-60d, viewModel.PresentationVolumeDb);
        Assert.Empty(playback.Requests);

        viewModel.SelectedEar = viewModel.EarOptions.Single(option => option.Value == TestedEar.Right);
        viewModel.PresentationVolumeDb = -48;
        await Task.Yield();

        var request = Assert.Single(playback.Requests);
        Assert.Equal(-48m, request.DigitalAttenuationDb);
        Assert.Equal(TestedEar.Right, request.Ear);
        Assert.Equal(ListeningEnvironment.Quiet, request.Environment);

        viewModel.PresentationVolumeDb = 0;
        await Task.Yield();
        Assert.Equal(-30d, viewModel.PresentationVolumeDb);

        viewModel.StartMeasurementCommand.Execute(null);
        Assert.Equal(-30m, Assert.Single(repository.Saved).Hardware.StartVolumeDb);
    }

    [Fact]
    public async Task NoiseRunsContinuouslyPerBlockAndStopsOnPauseAndBlockEnd()
    {
        var catalogService = new StimulusCatalogService();
        var pack = catalogService.Load(StimulusCatalogService.GetBundledPackDirectory());
        var numberPack = catalogService.Load(Path.Combine(
            AppContext.BaseDirectory, "stimuli", "de-DE", "personal-cardinal-numbers-christoph-v1"));
        var noise = new FakeContinuousNoiseService();
        var repository = new RecordingSessionRepository();
        var viewModel = new MeasurementViewModel(
            pack,
            [CreateProfile()],
            new FixedAudioEndpointService(CreateEndpoint()),
            new RecordingPlaybackService(),
            repository,
            createSeed: () => 20,
            numberPacks: [numberPack],
            loadHearingAids: TestHearingAids,
            continuousNoise: noise);
        viewModel.SelectedMaterialOption = viewModel.MaterialOptions[1];
        viewModel.SelectedEnvironment = viewModel.EnvironmentOptions.Single(option => option.Value == ListeningEnvironment.BackgroundNoise);
        viewModel.SignalToNoiseRatioDb = 0m;
        viewModel.SetPerson(TestPerson);

        viewModel.StartMeasurementCommand.Execute(null);
        var settings = viewModel.CurrentSession!.ContinuousNoise!;
        // Startlautstärke -60 dB, Referenz -20 dBFS, Start-SNR 0 dB.
        Assert.Equal(-80m, settings.NoiseLevelDbfs);
        Assert.Equal(-30m - -80m + -20m - 3m, viewModel.CurrentSession.AdaptiveTrack!.MaximumValueDb);

        viewModel.CorrectEarConfirmed = true;
        viewModel.HeadphoneFitConfirmed = true;
        await viewModel.StartBlockCommand.ExecuteAsync(null);
        Assert.Equal(1, noise.Started);
        await AnswerNumberAsync(viewModel, numberPack, noise);
        Assert.Equal(1, noise.Started);
        Assert.Equal(0m, noise.PlayedSnrs[0]);
        Assert.Equal(-6m, noise.PlayedSnrs[1]);

        await viewModel.TogglePauseCommand.ExecuteAsync(null);
        Assert.Equal(1, noise.Disposed);
        await viewModel.TogglePauseCommand.ExecuteAsync(null);
        Assert.Equal(2, noise.Started);

        while (viewModel.Stage == MeasurementStage.ActiveTest)
            await AnswerNumberAsync(viewModel, numberPack, noise);
        Assert.Equal(MeasurementStage.Preparation, viewModel.Stage);
        Assert.Equal(2, noise.Disposed);
        Assert.Empty(PairedMeasurementSessionRules.Validate(repository.Saved[^1]));
    }

    private static Task AnswerNumberAsync(MeasurementViewModel viewModel, LoadedStimulusPack numberPack, FakeContinuousNoiseService noise)
    {
        viewModel.NumericAnswer = numberPack.GetStimulus(noise.PlayedStimulusIds[^1]).CanonicalResponse;
        return viewModel.SubmitNumericAnswerCommand.ExecuteAsync(null);
    }

    [Theory]
    [InlineData(20260902, "ohne Hörgerät", "mit Hörgerät")]
    [InlineData(20260901, "mit Hörgerät", "ohne Hörgerät")]
    public async Task PreparationAnnouncesTheConditionOfEachBlockAfterEveryStart(int seed, string firstBlock, string secondBlock)
    {
        var pack = new StimulusCatalogService().Load(StimulusCatalogService.GetBundledPackDirectory());
        var viewModel = new MeasurementViewModel(
            pack,
            [CreateProfile()],
            new FixedAudioEndpointService(CreateEndpoint()),
            new RecordingPlaybackService(),
            new RecordingSessionRepository(),
            createSeed: () => seed,
            loadHearingAids: TestHearingAids);
        viewModel.SetPerson(TestPerson);
        var shownLabels = new List<string>();
        viewModel.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(MeasurementViewModel.CurrentConditionLabel))
                shownLabels.Add(viewModel.CurrentConditionLabel);
        };

        viewModel.StartMeasurementCommand.Execute(null);

        Assert.Equal(MeasurementStage.Preparation, viewModel.Stage);
        Assert.Equal(firstBlock, shownLabels[^1]);
        Assert.Contains(firstBlock == "mit Hörgerät" ? "einsetzen" : "kein Hörgerät", viewModel.PreparationInstruction);
        Assert.Equal(firstBlock, PairedMeasurementSessionFactory.GetFirstCondition(seed) == HearingAidCondition.WithHearingAid ? "mit Hörgerät" : "ohne Hörgerät");

        await CompletePreparationAsync(viewModel);
        await AnswerCurrentBlockAsync(viewModel);
        Assert.Equal(secondBlock, shownLabels[^1]);
    }

    [Fact]
    public void PlanStepPresetsTheTestAndFillsLevelsFromPretests()
    {
        var catalogService = new StimulusCatalogService();
        var pack = catalogService.Load(StimulusCatalogService.GetBundledPackDirectory());
        var numberPack = catalogService.Load(Path.Combine(
            AppContext.BaseDirectory, "stimuli", "de-DE", "personal-cardinal-numbers-christoph-v1"));
        var playback = new RecordingPlaybackService();
        var requestedEars = new List<TestedEar>();
        var viewModel = new MeasurementViewModel(
            pack,
            [CreateProfile()],
            new FixedAudioEndpointService(CreateEndpoint()),
            playback,
            new RecordingSessionRepository(),
            numberPacks: [numberPack],
            loadHearingAids: TestHearingAids,
            testWordDelay: TimeSpan.Zero,
            loadPretests: (_, ear, _) =>
            {
                requestedEars.Add(ear);
                return ear == TestedEar.Right
                    ? new PretestResults(QuietSpeechThresholdDb: -70m, NoiseSnrThresholdDb: -4m)
                    : PretestResults.None;
            });
        viewModel.SetPerson(TestPerson);
        Assert.Contains("Kein passender Vortest", viewModel.LevelSourceText);

        viewModel.ApplyPlanStep(TestPlanStep.NumbersNoise, TestedEar.Right);

        Assert.Equal(SpeechMaterial.Numbers, viewModel.SelectedMaterialOption.Material);
        Assert.True(viewModel.IsBackgroundNoise);
        Assert.True(viewModel.UsesAdaptiveLevel);
        Assert.Equal(TestedEar.Right, viewModel.SelectedEar.Value);
        // Ruheschwelle -70 dB + 25 dB.
        Assert.Equal(-45d, viewModel.PresentationVolumeDb);
        Assert.Equal(4m, viewModel.SignalToNoiseRatioDb);
        Assert.Contains("Ruheschwelle", viewModel.LevelSourceText);
        Assert.Contains("Schritt 3", viewModel.PlanStepText);
        Assert.Empty(playback.Requests);

        viewModel.PresentationVolumeDb = -50;
        Assert.StartsWith("Von Hand eingestellt", viewModel.LevelSourceText);

        viewModel.SelectedEnvironment = viewModel.EnvironmentOptions[0];
        Assert.False(viewModel.HasPlanStep);
        Assert.Equal(-58d, viewModel.PresentationVolumeDb);
    }

    [Fact]
    public async Task AdaptiveNumberTestInQuietTracksTheSimulatedThreshold()
    {
        var catalogService = new StimulusCatalogService();
        var pack = catalogService.Load(StimulusCatalogService.GetBundledPackDirectory());
        var numberPack = catalogService.Load(Path.Combine(
            AppContext.BaseDirectory, "stimuli", "de-DE", "personal-cardinal-numbers-christoph-v1"));
        var playback = new RecordingPlaybackService();
        var repository = new RecordingSessionRepository();
        var viewModel = new MeasurementViewModel(
            pack,
            [CreateProfile()],
            new FixedAudioEndpointService(CreateEndpoint()),
            playback,
            repository,
            createSeed: () => 20,
            numberPacks: [numberPack],
            loadHearingAids: TestHearingAids);
        viewModel.SelectedMaterialOption = viewModel.MaterialOptions[1];
        Assert.True(viewModel.UsesAdaptiveLevel);

        viewModel.SetPerson(TestPerson);
        viewModel.StartMeasurementCommand.Execute(null);
        Assert.NotNull(viewModel.CurrentSession!.AdaptiveTrack);
        for (var block = 0; block < 2; block++)
        {
            viewModel.CorrectEarConfirmed = true;
            viewModel.HeadphoneFitConfirmed = true;
            await viewModel.StartBlockCommand.ExecuteAsync(null);
            for (var index = 0; index < 25; index++)
            {
                // Simulierte Hörerin: versteht alles ab -71 dB, darunter nichts.
                var canonical = numberPack.GetStimulus(playback.PlayedStimulusIds[^1]).CanonicalResponse;
                var heard = playback.Requests[^1].DigitalAttenuationDb >= -71m;
                viewModel.NumericAnswer = heard ? canonical : canonical == "100" ? "101" : "100";
                await viewModel.SubmitNumericAnswerCommand.ExecuteAsync(null);
            }
        }

        Assert.Equal([-60m, -66m, -72m, -70m, -72m], playback.Requests.Take(5).Select(request => request.DigitalAttenuationDb));
        Assert.All(playback.Requests, request => Assert.InRange(request.DigitalAttenuationDb, -90m, -30m));
        Assert.Equal(MeasurementStage.Results, viewModel.Stage);
        Assert.True(viewModel.IsAdaptiveResult);
        var without = viewModel.Result!.WithoutHearingAid.Adaptive!;
        Assert.InRange(without.ThresholdDb!.Value, -72m, -70m);
        Assert.Equal(0m, viewModel.Result.ThresholdImprovementDb);
        Assert.Equal("gleich", viewModel.DifferenceText);
        Assert.Contains("dB", viewModel.WithoutResultText);
        Assert.Empty(PairedMeasurementSessionRules.Validate(repository.Saved[^1]));

        var pretests = new PretestResultsService(repository, new NoThresholdTests(), [numberPack]);
        var results = pretests.Load(TestPerson.Id, TestedEar.Left, repository.Saved[^1].Hardware with { StartVolumeDb = -40m });
        Assert.Equal(without.ThresholdDb, results.QuietSpeechThresholdDb);
        Assert.Null(pretests.Load(TestPerson.Id, TestedEar.Right, repository.Saved[^1].Hardware).QuietSpeechThresholdDb);
        var status = pretests.LoadStatus(TestPerson.Id, TestedEar.Left);
        Assert.NotNull(status.Single(value => value.Step == TestPlanStep.NumbersQuiet).LastCompletedAt);
        Assert.Null(status.Single(value => value.Step == TestPlanStep.NumbersNoise).LastCompletedAt);
    }

    private sealed class NoThresholdTests : IHearingThresholdSessionRepository
    {
        public IReadOnlyList<HearingThresholdSession> LoadAll() => [];
        public IReadOnlyList<HearingThresholdSession> LoadForPerson(Guid personId) => [];
        public HearingThresholdSession? Load(Guid id) => null;
        public void Save(Guid personId, HearingThresholdSession session) => throw new NotSupportedException();
        public void Delete(Guid id) => throw new NotSupportedException();
    }

    [Fact]
    public void CardinalMeasurementInNoiseStartsOnlyWithLoadedMaterialProfile()
    {
        var catalogService = new StimulusCatalogService();
        var pack = catalogService.Load(StimulusCatalogService.GetBundledPackDirectory());
        var numberPack = catalogService.Load(Path.Combine(
            AppContext.BaseDirectory,
            "stimuli",
            "de-DE",
            "personal-cardinal-numbers-katja-v1"));
        var viewModel = new MeasurementViewModel(
            pack,
            [CreateProfile()],
            new FixedAudioEndpointService(CreateEndpoint()),
            new RecordingPlaybackService(),
            new RecordingSessionRepository(),
            createSeed: () => 20,
            numberPacks: [numberPack],
            loadHearingAids: TestHearingAids);
        viewModel.SelectedMaterialOption = viewModel.MaterialOptions[1];
        viewModel.SelectedEnvironment = viewModel.EnvironmentOptions.Single(option =>
            option.Value == ListeningEnvironment.BackgroundNoise);

        viewModel.SetPerson(TestPerson);
        viewModel.StartMeasurementCommand.Execute(null);

        Assert.NotNull(numberPack.CardinalNoiseProfile);
        Assert.Equal(MeasurementStage.Preparation, viewModel.Stage);
        Assert.Equal(ListeningEnvironment.BackgroundNoise, viewModel.CurrentSession!.Environment);
    }

    [Fact]
    public void StoredResultUsesResultScreenAndCanStartANewMeasurement()
    {
        var pack = new StimulusCatalogService().Load(StimulusCatalogService.GetBundledPackDirectory());
        var profile = CreateProfile();
        var startedAt = DateTimeOffset.Parse("2026-09-01T14:30:00Z");
        var listIds = MeasurementRunPlanner.SelectListIds(pack.Catalog, SpeechMaterial.PhonemeContrasts, 20);
        var session = PairedMeasurementSessionFactory.CreateRandomized(
            TestedEar.Left,
            new HearingAidSnapshot(
                HearingAidIdentity.CreateStableId("Signia", "Pure C&G BCT 2IX", TestedEar.Left),
                "Signia",
                "Pure C&G BCT 2IX",
                "Signia links",
                TestedEar.Left,
                "Programm 1",
                "0"),
            SpeechMaterial.PhonemeContrasts,
            ListeningEnvironment.Quiet,
            listIds.FirstListId,
            listIds.SecondListId,
            profile.CreateSnapshot(),
            startedAt,
            20,
            pack.MaterialIdentity,
            MeasurementSessionContracts.PhonemeContrastMeasurement) with { CompletedAt = startedAt.AddMinutes(10) };
        var storedResult = new PairedMeasurementResult(
            new MeasurementBlockResult(HearingAidCondition.WithoutHearingAid, listIds.FirstListId, 12, 25, 48m, []),
            new MeasurementBlockResult(HearingAidCondition.WithHearingAid, listIds.SecondListId, 18, 25, 72m, []),
            24m,
            pack.Catalog.ChanceLevelPercent);
        var viewModel = new MeasurementViewModel(
            pack,
            [profile],
            new FixedAudioEndpointService(CreateEndpoint()),
            new RecordingPlaybackService(),
            new RecordingSessionRepository(),
            loadHearingAids: TestHearingAids);

        viewModel.ShowStoredResult(session, storedResult);

        Assert.Equal(MeasurementStage.Results, viewModel.Stage);
        Assert.True(viewModel.IsStoredResult);
        Assert.Same(session, viewModel.CurrentSession);
        Assert.Same(storedResult, viewModel.Result);
        Assert.Equal("Gespeichertes Messergebnis", viewModel.ResultsTitle);
        Assert.Contains("Signia links", viewModel.ResultsDescription);
        Assert.Equal("48 %", viewModel.WithoutResultText);
        Assert.Equal("72 %", viewModel.WithResultText);

        viewModel.NewMeasurementCommand.Execute(null);

        Assert.Equal(MeasurementStage.Setup, viewModel.Stage);
        Assert.False(viewModel.IsStoredResult);
    }

    private static async Task CompletePreparationAsync(MeasurementViewModel viewModel)
    {
        viewModel.CorrectEarConfirmed = true;
        viewModel.HeadphoneFitConfirmed = true;
        await viewModel.StartBlockCommand.ExecuteAsync(null);
        Assert.Equal(MeasurementStage.ActiveTest, viewModel.Stage);
        Assert.True(viewModel.CanAnswer);
        Assert.Equal(5, viewModel.ResponseAlternatives.Count);
    }

    [Fact]
    public void HearingAidChoicesFollowTheSelectedEarAndFillTheProtocolFields()
    {
        var pack = new StimulusCatalogService().Load(StimulusCatalogService.GetBundledPackDirectory());
        var viewModel = new MeasurementViewModel(
            pack,
            [CreateProfile()],
            new FixedAudioEndpointService(CreateEndpoint()),
            new RecordingPlaybackService(),
            new RecordingSessionRepository(),
            loadHearingAids: personId =>
            [
                new(Guid.NewGuid(), personId, "Oticon", "Intent 1", "Oticon Intent 1", TestedEar.Left),
                new(Guid.NewGuid(), personId, "Phonak", "Audéo Sphere", "Phonak Audéo Sphere", TestedEar.Right)
            ]);
        viewModel.SetPerson(TestPerson);

        Assert.Equal(["Oticon Intent 1", "Anderes Gerät eingeben"], viewModel.HearingAidChoices.Select(choice => choice.Label));
        Assert.Equal("Oticon", viewModel.HearingAidManufacturer);
        Assert.False(viewModel.IsManualHearingAid);

        viewModel.SelectedEar = viewModel.EarOptions.Single(option => option.Value == TestedEar.Right);

        Assert.Equal(["Phonak Audéo Sphere", "Anderes Gerät eingeben"], viewModel.HearingAidChoices.Select(choice => choice.Label));
        Assert.Equal("Phonak", viewModel.HearingAidManufacturer);
        Assert.Equal("Audéo Sphere", viewModel.HearingAidModel);
        Assert.Equal("Phonak Audéo Sphere", viewModel.HearingAidDisplayName);
        Assert.Equal(BadgeTone.EarRight, viewModel.EarTone);

        viewModel.SelectedHearingAidChoice = viewModel.HearingAidChoices[^1];
        Assert.True(viewModel.IsManualHearingAid);
    }

    [Fact]
    public void NewMeasurementStartsWithTheSettingsOfThePersonsLastMeasurement()
    {
        var pack = new StimulusCatalogService().Load(StimulusCatalogService.GetBundledPackDirectory());
        var repository = new RecordingSessionRepository();
        var first = new MeasurementViewModel(
            pack,
            [CreateProfile()],
            new FixedAudioEndpointService(CreateEndpoint()),
            new RecordingPlaybackService(),
            repository,
            createSeed: () => 20,
            loadHearingAids: TestHearingAids);
        first.SetPerson(TestPerson);
        first.SelectedEar = first.EarOptions.Single(option => option.Value == TestedEar.Right);
        first.SelectedEnvironment = first.EnvironmentOptions.Single(option => option.Value == ListeningEnvironment.BackgroundNoise);
        first.HearingAidProgramName = "Programm 2";
        first.StartMeasurementCommand.Execute(null);
        Assert.Single(repository.Saved);

        var second = new MeasurementViewModel(
            pack,
            [CreateProfile()],
            new FixedAudioEndpointService(CreateEndpoint()),
            new RecordingPlaybackService(),
            repository,
            loadHearingAids: TestHearingAids);
        second.SetPerson(TestPerson);

        Assert.Equal(TestedEar.Right, second.SelectedEar.Value);
        Assert.Equal(ListeningEnvironment.BackgroundNoise, second.SelectedEnvironment.Value);
        Assert.Equal("Programm 2", second.HearingAidProgramName);
        Assert.False(second.IsManualHearingAid);
    }

    [Fact]
    public void SeriesModePlansTheSeriesWhenTheMeasurementStarts()
    {
        var pack = new StimulusCatalogService().Load(StimulusCatalogService.GetBundledPackDirectory());
        var viewModel = new MeasurementViewModel(
            pack,
            [CreateProfile()],
            new FixedAudioEndpointService(CreateEndpoint()),
            new RecordingPlaybackService(),
            new RecordingSessionRepository(),
            createSeed: () => 20260906,
            series: new InMemoryMeasurementSeriesRepository(),
            loadHearingAids: TestHearingAids);
        viewModel.SetPerson(TestPerson);
        viewModel.SeriesPairCount = 4;

        viewModel.SelectedMode = viewModel.ModeOptions.Single(option => option.Value);
        Assert.Equal("Messreihe planen und starten", viewModel.StartButtonText);
        viewModel.StartMeasurementCommand.Execute(null);

        var expected = MeasurementSeriesPlanner.Create(pack.Catalog, SpeechMaterial.PhonemeContrasts, 4, 20260906).Rounds[0];
        Assert.True(viewModel.HasPreparedSeries);
        Assert.Equal(MeasurementStage.Preparation, viewModel.Stage);
        Assert.Equal(expected.SessionSeed, viewModel.CurrentSession!.RandomizationSeed);
        Assert.Equal(2, viewModel.StepNumber);
    }

    private static IReadOnlyList<PersonHearingAid> TestHearingAids(Guid personId) =>
    [
        new(Guid.Parse("5c1d0c43-4f53-4b4e-9f65-7bd2b9d1a001"), personId, "Signia", "Pure C&G BCT 2IX", "Signia Pure C&G BCT 2IX", TestedEar.Left),
        new(Guid.Parse("5c1d0c43-4f53-4b4e-9f65-7bd2b9d1a002"), personId, "Signia", "Pure C&G BCT 2IX", "Signia Pure C&G BCT 2IX", TestedEar.Right)
    ];

    private static PersonProfile TestPerson => new(
        Guid.Parse("174d7ccd-62a1-4d04-b630-b5db14da6901"), "Testperson", null,
        DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);

    private static async Task AnswerCurrentBlockAsync(MeasurementViewModel viewModel)
    {
        for (var index = 0; index < 25; index++)
        {
            var answer = viewModel.ResponseAlternatives[0];
            await viewModel.SelectAnswerCommand.ExecuteAsync(answer);
        }
    }

    private static MeasurementProfile CreateProfile() => new(
        Guid.Parse("84452115-0de9-489b-acd8-df0178db080d"),
        "Test-DAC + Testkopfhörer",
        "endpoint-topping",
        "Lautsprecher (TOPPING USB DAC)",
        true,
        48000,
        24,
        2,
        new HeadphoneProfile(
            Guid.Parse("2308157a-00c4-4f9a-81ae-03975ab5dcbe"),
            "Testhersteller",
            "Testmodell",
            "Ohrumschließend",
            300),
        "Vordere 3,5-mm-Klinke",
        "Low (+6 dB)",
        -60m,
        -30m,
        false,
        DateTimeOffset.Parse("2026-09-01T14:00:00Z"));

    private static AudioEndpointDescriptor CreateEndpoint() =>
        new("endpoint-topping", "Lautsprecher (TOPPING USB DAC)", 2, 48000, 24);

    private sealed class FixedAudioEndpointService(params AudioEndpointDescriptor[] endpoints) : IAudioEndpointService
    {
        public IReadOnlyList<AudioEndpointDescriptor> GetActiveOutputs() => endpoints;

        public Task PlayChannelTestAsync(
            string endpointId,
            bool exclusive,
            int channel,
            decimal digitalAttenuationDb,
            decimal maximumVolumeDb,
            CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class FakeContinuousNoiseService : IContinuousNoisePlaybackService
    {
        public int Started { get; private set; }
        public int Disposed { get; private set; }
        public List<string> PlayedStimulusIds { get; } = [];
        public List<decimal> PlayedSnrs { get; } = [];

        public SpeechLevelStatistics GetSpeechLevelStatistics(LoadedStimulusPack pack, int sampleRate) => new(-20m, 3m);

        public Task<IContinuousNoiseSession> StartAsync(
            LoadedStimulusPack pack,
            MeasurementHardwareSnapshot hardware,
            ContinuousNoiseSettings settings,
            TestedEar ear,
            int noiseSeed,
            CancellationToken cancellationToken = default)
        {
            Started++;
            return Task.FromResult<IContinuousNoiseSession>(new Session(this, pack, hardware, settings));
        }

        private sealed class Session(
            FakeContinuousNoiseService owner,
            LoadedStimulusPack pack,
            MeasurementHardwareSnapshot hardware,
            ContinuousNoiseSettings settings) : IContinuousNoiseSession
        {
            public Task<StimulusPlaybackReceipt> PlayAsync(string stimulusId, decimal signalToNoiseRatioDb, CancellationToken cancellationToken = default)
            {
                owner.PlayedStimulusIds.Add(stimulusId);
                owner.PlayedSnrs.Add(signalToNoiseRatioDb);
                var startedAt = DateTimeOffset.Parse("2026-10-02T10:00:00Z").AddSeconds(owner.PlayedStimulusIds.Count);
                return Task.FromResult(new StimulusPlaybackReceipt(
                    pack.Catalog.Id,
                    pack.Catalog.Version,
                    stimulusId,
                    pack.GetAudioAsset(stimulusId).Sha256,
                    hardware.EndpointId,
                    hardware.EndpointName,
                    new StimulusRenderMetadata(
                        StimulusAudioRenderer.RendererVersion,
                        StimulusAudioRenderer.CardinalNoiseAlgorithm,
                        StimulusAudioRenderer.SourcePeakNormalizationDbfs,
                        settings.NoiseLevelDbfs + signalToNoiseRatioDb + 20m,
                        signalToNoiseRatioDb,
                        1,
                        hardware.SampleRate,
                        0.1f,
                        pack.CardinalNoiseProfile!.MaterialId,
                        pack.CardinalNoiseProfile.ProfileSha256,
                        ContinuousNoiseLevelDbfs: settings.NoiseLevelDbfs),
                    startedAt,
                    startedAt.AddMilliseconds(500)));
            }

            public ValueTask DisposeAsync()
            {
                owner.Disposed++;
                return ValueTask.CompletedTask;
            }
        }
    }

    private sealed class RecordingPlaybackService : IStimulusPlaybackService
    {
        public List<string> PlayedStimulusIds { get; } = [];
        public List<StimulusRenderRequest> Requests { get; } = [];

        public Task<StimulusPlaybackReceipt> PlayAsync(
            LoadedStimulusPack pack,
            string stimulusId,
            MeasurementHardwareSnapshot hardware,
            StimulusRenderRequest renderRequest,
            CancellationToken cancellationToken = default)
        {
            PlayedStimulusIds.Add(stimulusId);
            Requests.Add(renderRequest);
            var startedAt = DateTimeOffset.Parse("2026-09-01T14:30:00Z").AddSeconds(PlayedStimulusIds.Count);
            return Task.FromResult(new StimulusPlaybackReceipt(
                pack.Catalog.Id,
                pack.Catalog.Version,
                stimulusId,
                pack.GetAudioAsset(stimulusId).Sha256,
                hardware.EndpointId,
                hardware.EndpointName,
                new StimulusRenderMetadata(
                    StimulusAudioRenderer.RendererVersion,
                    renderRequest.Environment == ListeningEnvironment.BackgroundNoise
                        ? StimulusAudioRenderer.NoiseAlgorithm
                        : "none",
                    StimulusAudioRenderer.SourcePeakNormalizationDbfs,
                    renderRequest.DigitalAttenuationDb,
                    renderRequest.SignalToNoiseRatioDb,
                    renderRequest.NoiseSeed,
                    renderRequest.OutputSampleRate,
                    0.01f),
                startedAt,
                startedAt.AddMilliseconds(500)));
        }
    }

    private sealed class BlockingPlaybackService : IStimulusPlaybackService
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool WasCancelled { get; private set; }

        public async Task<StimulusPlaybackReceipt> PlayAsync(
            LoadedStimulusPack pack,
            string stimulusId,
            MeasurementHardwareSnapshot hardware,
            StimulusRenderRequest renderRequest,
            CancellationToken cancellationToken = default)
        {
            Started.TrySetResult();
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                WasCancelled = true;
                throw;
            }

            throw new InvalidOperationException("Die blockierende Testwiedergabe darf nur per Abbruch enden.");
        }
    }

    private sealed class PauseThenCompletePlaybackService : IStimulusPlaybackService
    {
        public List<string> PlayedStimulusIds { get; } = [];
        public TaskCompletionSource FirstStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool FirstWasCancelled { get; private set; }

        public async Task<StimulusPlaybackReceipt> PlayAsync(
            LoadedStimulusPack pack,
            string stimulusId,
            MeasurementHardwareSnapshot hardware,
            StimulusRenderRequest renderRequest,
            CancellationToken cancellationToken = default)
        {
            PlayedStimulusIds.Add(stimulusId);
            if (PlayedStimulusIds.Count == 1)
            {
                FirstStarted.TrySetResult();
                try
                {
                    await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    FirstWasCancelled = true;
                    throw;
                }
            }

            var startedAt = DateTimeOffset.Parse("2026-09-01T14:30:00Z");
            return new StimulusPlaybackReceipt(
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
                    renderRequest.DigitalAttenuationDb,
                    null,
                    renderRequest.NoiseSeed,
                    renderRequest.OutputSampleRate,
                    0.01f),
                startedAt,
                startedAt.AddMilliseconds(500));
        }
    }

    private sealed class RecordingSessionRepository : IMeasurementSessionRepository
    {
        public List<PairedMeasurementSession> Saved { get; } = [];

        public IReadOnlyList<PairedMeasurementSession> LoadAll() => Saved;

        public IReadOnlyList<PairedMeasurementSession> LoadForPerson(Guid personId) => Saved;

        public PairedMeasurementSession? Load(Guid id) => Saved.LastOrDefault(session => session.Id == id);

        public void Save(Guid personId, PairedMeasurementSession session) => Saved.Add(session);

        public void Delete(Guid id) => Saved.RemoveAll(session => session.Id == id);
    }
}
