using HearDelta.App.Services;
using HearDelta.App.ViewModels;
using HearDelta.Core;

namespace HearDelta.App.Tests;

public sealed class HearingThresholdViewModelTests
{
    [Fact]
    public async Task CompleteRunRecordsEachButtonPressAndProducesFrequencySortedResults()
    {
        var profile = CreateProfile();
        var playback = new ButtonControlledPlaybackService();
        var repository = new RecordingThresholdSessionRepository();
        var timestamp = DateTimeOffset.Parse("2026-09-02T08:00:00Z");
        var viewModel = new HearingThresholdViewModel(
            [profile],
            new FixedAudioEndpointService(CreateEndpoint()),
            playback,
            repository,
            now: () => timestamp = timestamp.AddSeconds(1),
            createSeed: () => 20260902)
        {
            CorrectEarConfirmed = true,
            HeadphoneFitConfirmed = true,
            SelectedOrder = new SelectionOption<ThresholdToneOrder>(ThresholdToneOrder.Random, "Zufällig")
        };

        viewModel.SetPerson(TestPerson);
        await viewModel.StartTestCommand.ExecuteAsync(null);
        Assert.Equal(HearingThresholdStage.ActiveTest, viewModel.Stage);
        Assert.Equal(HearingThresholdProtocol.CenterFrequencyHz, playback.Requests[0].FrequencyHz);
        Assert.Equal(-80m, playback.Requests[0].StartAttenuationDbfs);
        Assert.Equal(-30m, playback.Requests[0].MaximumAttenuationDbfs);
        Assert.Equal(HearingThresholdProtocol.LevelStepDb, playback.Requests[0].LevelStepDb);
        Assert.Equal(ThresholdSignalPattern.Current, playback.Requests[0].SignalPattern);

        await viewModel.HeardCommand.ExecuteAsync(null);

        Assert.Equal(HearingThresholdStage.ActiveTest, viewModel.Stage);
        Assert.Empty(viewModel.CurrentSession!.Observations);
        Assert.Equal(HearingThresholdProtocol.CenterFrequencyHz, playback.Requests[1].FrequencyHz);
        Assert.Equal(-81.5m, playback.Requests[1].StartAttenuationDbfs);
        Assert.True(viewModel.CanReportHeard);

        await viewModel.HeardCommand.ExecuteAsync(null);

        var center = viewModel.CurrentSession!.Observations[0];
        Assert.Equal(-72.5m, center.ThresholdAttenuationDbfs);
        Assert.Equal(-80m, center.Presentation.StartAttenuationDbfs);
        Assert.Equal(-81.5m, center.Confirmation!.StartAttenuationDbfs);
        Assert.Equal(-78.5m, playback.Requests[2].StartAttenuationDbfs);

        for (var index = 0; index < 26; index++)
        {
            Assert.True(viewModel.CanReportHeard);
            await viewModel.HeardCommand.ExecuteAsync(null);
        }

        Assert.Equal(HearingThresholdStage.Results, viewModel.Stage);
        Assert.Equal(28, playback.Requests.Count);
        Assert.Equal(15, repository.Saved.Count);
        Assert.Equal(14, viewModel.CurrentSession!.Observations.Count);
        Assert.All(viewModel.CurrentSession.Observations, observation => Assert.True(observation.Heard));
        Assert.All(viewModel.CurrentSession.Observations, observation => Assert.NotNull(observation.Confirmation));
        Assert.NotNull(viewModel.CurrentSession.CompletedAt);
        Assert.Equal(14, viewModel.Results.Count);
        Assert.Equal(62.5d, viewModel.Results[0].FrequencyHz);
        Assert.Equal("62,5 Hz", viewModel.Results[0].Frequency);
        Assert.Equal(10_000d, viewModel.Results[^1].FrequencyHz);
        Assert.Equal("10 kHz", viewModel.Results[^1].Frequency);
        Assert.All(viewModel.Results, row => Assert.True(row.Heard));
        Assert.All(viewModel.Results, row => Assert.NotNull(row.ThresholdAttenuationDbfs));
        Assert.All(viewModel.Results, row => Assert.Contains("dBFS", row.Threshold));
    }

    [Fact]
    public async Task MissingStoredEndpointBlocksTestBeforeSaveOrPlayback()
    {
        var playback = new ButtonControlledPlaybackService();
        var repository = new RecordingThresholdSessionRepository();
        var viewModel = new HearingThresholdViewModel(
            [CreateProfile()],
            new FixedAudioEndpointService(),
            playback,
            repository)
        {
            CorrectEarConfirmed = true,
            HeadphoneFitConfirmed = true
        };

        viewModel.SetPerson(TestPerson);
        await viewModel.StartTestCommand.ExecuteAsync(null);

        Assert.Equal(HearingThresholdStage.Setup, viewModel.Stage);
        Assert.Contains("kein Ersatzgerät", viewModel.StatusMessage);
        Assert.Empty(repository.Saved);
        Assert.Empty(playback.Requests);
    }

    [Fact]
    public async Task ReachingMaximumWithoutButtonPressRecordsNotHeardAndContinues()
    {
        var repository = new RecordingThresholdSessionRepository();
        var playback = new MaximumReachedPlaybackService();
        var viewModel = new HearingThresholdViewModel(
            [CreateProfile()],
            new FixedAudioEndpointService(CreateEndpoint()),
            playback,
            repository)
        {
            CorrectEarConfirmed = true,
            HeadphoneFitConfirmed = true
        };

        viewModel.SetPerson(TestPerson);
        await viewModel.StartTestCommand.ExecuteAsync(null);

        Assert.Equal(HearingThresholdStage.Results, viewModel.Stage);
        Assert.Equal(14, playback.Requests.Count);
        Assert.Equal(14, viewModel.CurrentSession!.Observations.Count);
        Assert.All(
            viewModel.CurrentSession.Observations,
            observation =>
            {
                Assert.False(observation.Heard);
                Assert.Null(observation.ThresholdAttenuationDbfs);
                Assert.True(observation.Presentation.ReachedMaximum);
            });
        Assert.All(
            viewModel.Results,
            row =>
            {
                Assert.False(row.Heard);
                Assert.Null(row.ThresholdAttenuationDbfs);
                Assert.Contains("nicht gehört", row.Threshold);
            });
    }

    [Fact]
    public async Task UnheardCenterFallsBackToSafeInitialStartAndContinuesWithoutInterruption()
    {
        var playback = new MaximumReachedPlaybackService();
        var viewModel = new HearingThresholdViewModel(
            [CreateProfile()],
            new FixedAudioEndpointService(CreateEndpoint()),
            playback,
            new RecordingThresholdSessionRepository())
        {
            CorrectEarConfirmed = true,
            HeadphoneFitConfirmed = true
        };

        viewModel.SetPerson(TestPerson);
        await viewModel.StartTestCommand.ExecuteAsync(null);

        Assert.Equal(HearingThresholdStage.Results, viewModel.Stage);
        Assert.Equal(14, playback.Requests.Count);
        Assert.All(playback.Requests, request => Assert.Equal(-80m, request.StartAttenuationDbfs));
        var session = Assert.IsType<HearingThresholdSession>(viewModel.CurrentSession);
        Assert.Equal(14, session.Observations.Count);
        Assert.All(session.Observations, observation => Assert.False(observation.Heard));
        Assert.DoesNotContain("nicht gehört", viewModel.StatusMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AbortKeepsCompletedThresholdObservationsAndMarksSession()
    {
        var playback = new ButtonControlledPlaybackService();
        var repository = new RecordingThresholdSessionRepository();
        var viewModel = new HearingThresholdViewModel(
            [CreateProfile()],
            new FixedAudioEndpointService(CreateEndpoint()),
            playback,
            repository)
        {
            CorrectEarConfirmed = true,
            HeadphoneFitConfirmed = true
        };

        viewModel.SetPerson(TestPerson);
        await viewModel.StartTestCommand.ExecuteAsync(null);
        for (var press = 0; press < 7; press++)
            await viewModel.HeardCommand.ExecuteAsync(null);

        viewModel.AbortTestCommand.Execute(null);

        Assert.Equal(HearingThresholdStage.Results, viewModel.Stage);
        Assert.True(viewModel.WasAborted);
        Assert.Equal(viewModel.CurrentSession!.CompletedAt, viewModel.CurrentSession.AbortedAt);
        Assert.Equal(3, viewModel.CurrentSession.Observations.Count);
        Assert.Equal(3, viewModel.Results.Count);
        Assert.Equal(viewModel.CurrentSession.AbortedAt, repository.Saved[^1].AbortedAt);
    }

    [Fact]
    public async Task PauseCancelsCurrentToneAndResumeRestartsItWithoutRecordingAValue()
    {
        var playback = new ButtonControlledPlaybackService();
        var viewModel = new HearingThresholdViewModel(
            [CreateProfile()],
            new FixedAudioEndpointService(CreateEndpoint()),
            playback,
            new RecordingThresholdSessionRepository())
        {
            CorrectEarConfirmed = true,
            HeadphoneFitConfirmed = true
        };

        viewModel.SetPerson(TestPerson);
        await viewModel.StartTestCommand.ExecuteAsync(null);
        await viewModel.TogglePauseCommand.ExecuteAsync(null);

        Assert.True(viewModel.IsPaused);
        Assert.False(viewModel.CanReportHeard);
        Assert.Empty(viewModel.CurrentSession!.Observations);
        Assert.Single(playback.Requests);

        await viewModel.TogglePauseCommand.ExecuteAsync(null);

        Assert.False(viewModel.IsPaused);
        Assert.True(viewModel.CanReportHeard);
        Assert.Equal(2, playback.Requests.Count);
        Assert.Equal(500d, playback.Requests[0].FrequencyHz);
        Assert.Equal(500d, playback.Requests[1].FrequencyHz);
        Assert.Equal(playback.Requests[0].StartAttenuationDbfs, playback.Requests[1].StartAttenuationDbfs);
    }

    [Fact]
    public async Task UnconfirmedReactionIsRecordedAsNotHeardAndDoesNotShiftFollowingStart()
    {
        var playback = new ConfirmationMissedPlaybackService();
        var viewModel = new HearingThresholdViewModel(
            [CreateProfile()],
            new FixedAudioEndpointService(CreateEndpoint()),
            playback,
            new RecordingThresholdSessionRepository())
        {
            CorrectEarConfirmed = true,
            HeadphoneFitConfirmed = true
        };

        viewModel.SetPerson(TestPerson);
        await viewModel.StartTestCommand.ExecuteAsync(null);
        await viewModel.HeardCommand.ExecuteAsync(null);

        var center = viewModel.CurrentSession!.Observations[0];
        Assert.False(center.Heard);
        Assert.Null(center.ThresholdAttenuationDbfs);
        Assert.Equal(-72.5m, center.Presentation.EndAttenuationDbfs);
        Assert.Equal(-81.5m, center.Confirmation!.StartAttenuationDbfs);
        Assert.True(center.Confirmation.ReachedMaximum);
        Assert.Equal(3, playback.Requests.Count);
        Assert.Equal(-80m, playback.Requests[2].StartAttenuationDbfs);
    }

    [Fact]
    public async Task PauseDuringConfirmationRestartsTheWholeFrequency()
    {
        var playback = new ButtonControlledPlaybackService();
        var viewModel = new HearingThresholdViewModel(
            [CreateProfile()],
            new FixedAudioEndpointService(CreateEndpoint()),
            playback,
            new RecordingThresholdSessionRepository())
        {
            CorrectEarConfirmed = true,
            HeadphoneFitConfirmed = true
        };

        viewModel.SetPerson(TestPerson);
        await viewModel.StartTestCommand.ExecuteAsync(null);
        await viewModel.HeardCommand.ExecuteAsync(null);
        await viewModel.TogglePauseCommand.ExecuteAsync(null);
        await viewModel.TogglePauseCommand.ExecuteAsync(null);

        Assert.Empty(viewModel.CurrentSession!.Observations);
        Assert.Equal(3, playback.Requests.Count);
        Assert.Equal(-81.5m, playback.Requests[1].StartAttenuationDbfs);
        Assert.Equal(-80m, playback.Requests[2].StartAttenuationDbfs);
    }

    [Fact]
    public async Task AbortDuringFirstCenterToneStopsPlaybackAndShowsEmptyResults()
    {
        var playback = new ButtonControlledPlaybackService();
        var viewModel = new HearingThresholdViewModel(
            [CreateProfile()],
            new FixedAudioEndpointService(CreateEndpoint()),
            playback,
            new RecordingThresholdSessionRepository())
        {
            CorrectEarConfirmed = true,
            HeadphoneFitConfirmed = true
        };

        viewModel.SetPerson(TestPerson);
        await viewModel.StartTestCommand.ExecuteAsync(null);
        viewModel.AbortTestCommand.Execute(null);
        await playback.Cancelled.Task;

        Assert.True(playback.CancellationObserved);
        Assert.Equal(HearingThresholdStage.Results, viewModel.Stage);
        Assert.True(viewModel.WasAborted);
        Assert.Empty(viewModel.Results);
    }

    [Fact]
    public async Task MaskingIsStoredWithSessionAndPlayedWithEveryPresentation()
    {
        var playback = new MaximumReachedPlaybackService();
        var repository = new RecordingThresholdSessionRepository();
        var viewModel = new HearingThresholdViewModel(
            [CreateProfile()],
            new FixedAudioEndpointService(CreateEndpoint()),
            playback,
            repository)
        {
            CorrectEarConfirmed = true,
            HeadphoneFitConfirmed = true,
            IsMaskingEnabled = true,
            MaskingLevelDbfs = -44.4d
        };

        viewModel.SetPerson(TestPerson);
        Assert.Equal(HearingThresholdResultPresentation.DefaultMaskedName, viewModel.Annotation.Name);
        await viewModel.StartTestCommand.ExecuteAsync(null);

        var session = Assert.IsType<HearingThresholdSession>(viewModel.CurrentSession);
        Assert.Equal(HearingThresholdProtocol.CurrentVersion, session.ProtocolVersion);
        Assert.Equal(-44m, session.Masking!.LevelDbfs);
        Assert.Equal(TestedEar.Right, session.MaskedEar);
        Assert.Equal(HearingAidCondition.WithoutHearingAid, session.Condition);
        Assert.Null(session.HearingAid);
        Assert.All(playback.Requests, request => Assert.Equal(session.Masking, request.Masking));
        Assert.All(session.Observations, observation => Assert.Equal(-44m, observation.Presentation.MaskingLevelDbfs));
        Assert.Empty(HearingThresholdSessionRules.Validate(session));
        Assert.Contains("vertäubt", viewModel.SessionConditionText);
    }

    [Fact]
    public async Task WithoutMaskingNoNoiseIsRequested()
    {
        var playback = new MaximumReachedPlaybackService();
        var viewModel = new HearingThresholdViewModel(
            [CreateProfile()],
            new FixedAudioEndpointService(CreateEndpoint()),
            playback,
            new RecordingThresholdSessionRepository())
        {
            CorrectEarConfirmed = true,
            HeadphoneFitConfirmed = true
        };

        viewModel.SetPerson(TestPerson);
        await viewModel.StartTestCommand.ExecuteAsync(null);

        Assert.Null(viewModel.CurrentSession!.Masking);
        Assert.All(playback.Requests, request => Assert.Null(request.Masking));
        Assert.All(viewModel.CurrentSession.Observations, observation => Assert.Null(observation.Presentation.MaskingLevelDbfs));
        Assert.Equal(HearingThresholdResultPresentation.DefaultName, viewModel.Annotation.Name);
    }

    [Fact]
    public async Task ChosenStartLevelIsUsedForTheCenterToneAndAsFloorForFollowingTones()
    {
        var playback = new MaximumReachedPlaybackService();
        var viewModel = new HearingThresholdViewModel(
            [CreateProfile()],
            new FixedAudioEndpointService(CreateEndpoint()),
            playback,
            new RecordingThresholdSessionRepository())
        {
            CorrectEarConfirmed = true,
            HeadphoneFitConfirmed = true
        };
        Assert.Equal(-80d, viewModel.StartLevelDbfs);
        Assert.Equal(-90d, viewModel.MinimumStartLevelDbfs);
        Assert.Equal(-48d, viewModel.MaximumStartLevelDbfs);
        Assert.False(viewModel.IsStartLevelRaised);

        viewModel.StartLevelDbfs = -50.4d;
        Assert.True(viewModel.IsStartLevelRaised);
        Assert.Equal("−50 dBFS", viewModel.StartLevelText);
        viewModel.SetPerson(TestPerson);
        await viewModel.StartTestCommand.ExecuteAsync(null);

        Assert.Equal(-50m, viewModel.CurrentSession!.StartAttenuationDbfs);
        Assert.All(playback.Requests, request => Assert.Equal(-50m, request.StartAttenuationDbfs));

        viewModel.NewTestCommand.Execute(null);
        viewModel.ResetStartLevelCommand.Execute(null);
        Assert.Equal(-80d, viewModel.StartLevelDbfs);
    }

    [Fact]
    public async Task MaskingPreviewPlaysOnTheOppositeEar()
    {
        var playback = new ButtonControlledPlaybackService();
        var viewModel = new HearingThresholdViewModel(
            [CreateProfile()],
            new FixedAudioEndpointService(CreateEndpoint()),
            playback,
            new RecordingThresholdSessionRepository())
        {
            SelectedEar = new SelectionOption<TestedEar>(TestedEar.Right, "Rechtes Ohr"),
            IsMaskingEnabled = true,
            MaskingLevelDbfs = -60d
        };

        await viewModel.PlayMaskingPreviewCommand.ExecuteAsync(null);

        Assert.Equal([(TestedEar.Left, -60m)], playback.Previews);
        Assert.Empty(playback.Requests);
        Assert.Equal("Rauschen auf dem linken Ohr", viewModel.MaskedEarText);
    }

    private static MeasurementProfile CreateProfile() => new(
        Guid.Parse("84452115-0de9-489b-acd8-df0178db080d"),
        "Test-DAC + Testkopfhörer",
        "endpoint-topping",
        "Lautsprecher (TOPPING USB DAC)",
        true,
        48_000,
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
        DateTimeOffset.Parse("2026-09-02T07:30:00Z"));

    private static PersonProfile TestPerson => new(
        Guid.Parse("174d7ccd-62a1-4d04-b630-b5db14da6901"), "Testperson", null,
        DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);

    private static AudioEndpointDescriptor CreateEndpoint() =>
        new("endpoint-topping", "Lautsprecher (TOPPING USB DAC)", 2, 48_000, 24);

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

    private sealed class ButtonControlledPlaybackService : IThresholdTonePlaybackService
    {
        public List<ThresholdTonePlaybackRequest> Requests { get; } = [];
        public bool CancellationObserved { get; private set; }
        public TaskCompletionSource Cancelled { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<ThresholdTonePlaybackReceipt> PlayAsync(
            ThresholdTonePlaybackRequest request,
            MeasurementHardwareSnapshot hardware,
            CancellationToken stopSignal)
        {
            Requests.Add(request);
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, stopSignal);
            }
            catch (OperationCanceledException) when (stopSignal.IsCancellationRequested)
            {
                CancellationObserved = true;
                Cancelled.TrySetResult();
            }

            var startedAt = DateTimeOffset.Parse("2026-09-02T08:00:00Z").AddSeconds(Requests.Count);
            return new ThresholdTonePlaybackReceipt(
                hardware.EndpointId,
                hardware.EndpointName,
                request.FrequencyHz,
                request.StartAttenuationDbfs,
                -72.5m,
                request.MaximumAttenuationDbfs,
                request.LevelStepDb,
                request.OutputSampleRate,
                startedAt,
                startedAt.AddSeconds(14),
                false,
                request.SignalPattern,
                MaskingLevelDbfs: request.Masking?.LevelDbfs);
        }

        public List<(TestedEar Ear, decimal LevelDbfs)> Previews { get; } = [];

        public Task PlayMaskingPreviewAsync(
            TestedEar maskedEar,
            decimal levelDbfs,
            MeasurementHardwareSnapshot hardware,
            CancellationToken stopSignal)
        {
            Previews.Add((maskedEar, levelDbfs));
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingThresholdSessionRepository : IHearingThresholdSessionRepository
    {
        public List<HearingThresholdSession> Saved { get; } = [];

        public IReadOnlyList<HearingThresholdSession> LoadAll() => Saved;

        public IReadOnlyList<HearingThresholdSession> LoadForPerson(Guid personId) => Saved;

        public HearingThresholdSession? Load(Guid id) => Saved.LastOrDefault(session => session.Id == id);

        public void Save(Guid personId, HearingThresholdSession session) => Saved.Add(session);

        public void Delete(Guid id) => Saved.RemoveAll(session => session.Id == id);
    }

    /// <summary>Erster Durchgang je Ton wartet auf den Knopfdruck; die Bestätigung läuft ohne Reaktion bis zur Obergrenze.</summary>
    private sealed class ConfirmationMissedPlaybackService : IThresholdTonePlaybackService
    {
        private readonly ButtonControlledPlaybackService firstRuns = new();
        private readonly MaximumReachedPlaybackService confirmations = new();
        public List<ThresholdTonePlaybackRequest> Requests { get; } = [];

        public Task<ThresholdTonePlaybackReceipt> PlayAsync(
            ThresholdTonePlaybackRequest request,
            MeasurementHardwareSnapshot hardware,
            CancellationToken stopSignal)
        {
            Requests.Add(request);
            return Requests.Count % 2 == 1
                ? firstRuns.PlayAsync(request, hardware, stopSignal)
                : confirmations.PlayAsync(request, hardware, stopSignal);
        }

        public Task PlayMaskingPreviewAsync(
            TestedEar maskedEar,
            decimal levelDbfs,
            MeasurementHardwareSnapshot hardware,
            CancellationToken stopSignal) => Task.CompletedTask;
    }

    private sealed class MaximumReachedPlaybackService : IThresholdTonePlaybackService
    {
        public List<ThresholdTonePlaybackRequest> Requests { get; } = [];

        public Task<ThresholdTonePlaybackReceipt> PlayAsync(
            ThresholdTonePlaybackRequest request,
            MeasurementHardwareSnapshot hardware,
            CancellationToken stopSignal)
        {
            Requests.Add(request);
            var startedAt = DateTimeOffset.Parse("2026-09-02T08:00:00Z");
            return Task.FromResult(new ThresholdTonePlaybackReceipt(
                hardware.EndpointId,
                hardware.EndpointName,
                request.FrequencyHz,
                request.StartAttenuationDbfs,
                request.MaximumAttenuationDbfs,
                request.MaximumAttenuationDbfs,
                request.LevelStepDb,
                request.OutputSampleRate,
                startedAt,
                startedAt.AddSeconds(35),
                true,
                request.SignalPattern,
                MaskingLevelDbfs: request.Masking?.LevelDbfs));
        }

        public Task PlayMaskingPreviewAsync(
            TestedEar maskedEar,
            decimal levelDbfs,
            MeasurementHardwareSnapshot hardware,
            CancellationToken stopSignal) => Task.CompletedTask;
    }
}
