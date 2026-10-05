using HearDelta.App.Services;
using HearDelta.App.ViewModels;
using HearDelta.Core;

namespace HearDelta.App.Tests;

public sealed class PracticeViewModelTests
{
    [Fact]
    public async Task PracticePersistsFeedbackEnabledFreeTextSeparatelyFromMeasurements()
    {
        var pack = new StimulusCatalogService().Load(Path.Combine(
            AppContext.BaseDirectory, "stimuli", "de-DE", "personal-relative-v1"));
        var repository = new RecordingPracticeRepository();
        var viewModel = new PracticeViewModel(
            pack,
            [CreateProfile()],
            new FixedAudioEndpointService(CreateEndpoint()),
            new RecordingPlaybackService(),
            repository,
            createSeed: () => 42);
        viewModel.SetPerson(TestPerson);
        viewModel.SetupConfirmed = true;

        await viewModel.StartPracticeCommand.ExecuteAsync(null);

        Assert.Equal(PracticeStage.Active, viewModel.Stage);
        Assert.True(viewModel.IsAwaitingResponse);
        Assert.True(viewModel.CanPause);
        Assert.Single(repository.Saved);
        Assert.True(viewModel.CanSubmitResponse);
        Assert.Equal("Nicht verstanden", viewModel.SubmitButtonText);
        viewModel.EnteredResponse = "falsch";
        Assert.True(viewModel.CanSubmitResponse);
        Assert.Equal("Antwort prüfen", viewModel.SubmitButtonText);
        Assert.True(viewModel.SubmitResponseCommand.CanExecute(null));
        await viewModel.SubmitResponseCommand.ExecuteAsync(null);

        var saved = Assert.Single(repository.Saved[^1].Responses);
        Assert.Equal("falsch", saved.EnteredText);
        Assert.NotNull(saved.Presentation);
        Assert.True(viewModel.HasFeedback);
        Assert.Equal("Noch einmal merken", viewModel.FeedbackTitle);
        Assert.True(viewModel.CanContinuePractice);
        Assert.True(viewModel.ContinuePracticeCommand.CanExecute(null));
        Assert.DoesNotContain("Mess", viewModel.StatusMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task EmptyPracticeAnswerRecordsNotUnderstoodAndAllowsContinuation()
    {
        var pack = new StimulusCatalogService().Load(Path.Combine(
            AppContext.BaseDirectory, "stimuli", "de-DE", "personal-relative-v1"));
        var repository = new RecordingPracticeRepository();
        var viewModel = new PracticeViewModel(
            pack,
            [CreateProfile()],
            new FixedAudioEndpointService(CreateEndpoint()),
            new RecordingPlaybackService(),
            repository,
            createSeed: () => 42);
        viewModel.SetPerson(TestPerson);
        viewModel.SetupConfirmed = true;

        await viewModel.StartPracticeCommand.ExecuteAsync(null);
        await viewModel.SubmitResponseCommand.ExecuteAsync(null);

        Assert.Equal("Nicht verstanden", Assert.Single(repository.Saved[^1].Responses).EnteredText);
        Assert.True(viewModel.CanContinuePractice);
        Assert.True(viewModel.ContinuePracticeCommand.CanExecute(null));
    }

    [Fact]
    public async Task PracticeVolumeIsClampedAndAppliedFromTheNextPlayback()
    {
        var pack = new StimulusCatalogService().Load(Path.Combine(
            AppContext.BaseDirectory, "stimuli", "de-DE", "personal-relative-v1"));
        var playback = new RecordingPlaybackService();
        var viewModel = new PracticeViewModel(
            pack,
            [CreateProfile()],
            new FixedAudioEndpointService(CreateEndpoint()),
            playback,
            new RecordingPracticeRepository(),
            createSeed: () => 42);
        viewModel.SetPerson(TestPerson);

        Assert.Equal(-60, viewModel.PracticeVolumeDb);
        viewModel.PracticeVolumeDb = -10;
        Assert.Equal(-30, viewModel.PracticeVolumeDb);
        viewModel.PracticeVolumeDb = -120;
        Assert.Equal(-96, viewModel.PracticeVolumeDb);

        viewModel.PracticeVolumeDb = -60;
        viewModel.SetPerson(TestPerson);
        viewModel.SetupConfirmed = true;
        await viewModel.StartPracticeCommand.ExecuteAsync(null);
        Assert.Equal(-60m, Assert.Single(playback.RenderRequests).DigitalAttenuationDb);

        viewModel.EnteredResponse = "Antwort";
        await viewModel.SubmitResponseCommand.ExecuteAsync(null);
        viewModel.PracticeVolumeDb = -45;
        await viewModel.ContinuePracticeCommand.ExecuteAsync(null);

        Assert.Equal(-45m, playback.RenderRequests[1].DigitalAttenuationDb);
        Assert.All(playback.RenderRequests, request => Assert.True(request.DigitalAttenuationDb <= request.MaximumVolumeDb));
    }

    [Fact]
    public async Task MissingPracticeEndpointBlocksBeforeSavingOrPlayback()
    {
        var pack = new StimulusCatalogService().Load(Path.Combine(
            AppContext.BaseDirectory, "stimuli", "de-DE", "personal-relative-v1"));
        var repository = new RecordingPracticeRepository();
        var playback = new RecordingPlaybackService();
        var viewModel = new PracticeViewModel(
            pack,
            [CreateProfile()],
            new FixedAudioEndpointService(),
            playback,
            repository);
        viewModel.SetPerson(TestPerson);
        viewModel.SetupConfirmed = true;

        await viewModel.StartPracticeCommand.ExecuteAsync(null);

        Assert.Equal(PracticeStage.Setup, viewModel.Stage);
        Assert.Contains("kein Ersatzgerät", viewModel.StatusMessage);
        Assert.Empty(repository.Saved);
        Assert.Empty(playback.PlayedStimulusIds);
    }

    private static MeasurementProfile CreateProfile() => new(
        Guid.Parse("84452115-0de9-489b-acd8-df0178db080d"), "Testprofil", "endpoint-topping", "TOPPING USB DAC", true,
        48000, 24, 2,
        new HeadphoneProfile(Guid.Parse("2308157a-00c4-4f9a-81ae-03975ab5dcbe"), "Testhersteller", "Testmodell", "Ohrumschließend", 300),
        "Ausgang", "Low", -60m, -30m, false, DateTimeOffset.UtcNow);

    private static AudioEndpointDescriptor CreateEndpoint() => new("endpoint-topping", "TOPPING USB DAC", 2, 48000, 24);

    private static PersonProfile TestPerson => new(
        Guid.Parse("174d7ccd-62a1-4d04-b630-b5db14da6901"), "Testperson", null,
        DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);

    private sealed class FixedAudioEndpointService(params AudioEndpointDescriptor[] endpoints) : IAudioEndpointService
    {
        public IReadOnlyList<AudioEndpointDescriptor> GetActiveOutputs() => endpoints;
        public Task PlayChannelTestAsync(string endpointId, bool exclusive, int channel, decimal digitalAttenuationDb, decimal maximumVolumeDb, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class RecordingPracticeRepository : IPracticeSessionRepository
    {
        public List<PracticeSession> Saved { get; } = [];
        public IReadOnlyList<PracticeSession> LoadAll() => Saved;
        public IReadOnlyList<PracticeSession> LoadForPerson(Guid personId) => Saved;
        public PracticeSession? Load(Guid id) => Saved.LastOrDefault(session => session.Id == id);
        public void Save(Guid personId, PracticeSession session) => Saved.Add(session);
    }

    private sealed class RecordingPlaybackService : IStimulusPlaybackService
    {
        public List<string> PlayedStimulusIds { get; } = [];
        public List<StimulusRenderRequest> RenderRequests { get; } = [];
        public Task<StimulusPlaybackReceipt> PlayAsync(LoadedStimulusPack pack, string stimulusId, MeasurementHardwareSnapshot hardware, StimulusRenderRequest renderRequest, CancellationToken cancellationToken = default)
        {
            PlayedStimulusIds.Add(stimulusId);
            RenderRequests.Add(renderRequest);
            var startedAt = DateTimeOffset.Parse("2026-09-06T12:00:00Z");
            return Task.FromResult(new StimulusPlaybackReceipt(
                pack.Catalog.Id, pack.Catalog.Version, stimulusId, pack.GetAudioAsset(stimulusId).Sha256,
                hardware.EndpointId, hardware.EndpointName,
                new StimulusRenderMetadata("test", "none", -1m, -30m, null, 1, 48000, 0.01f),
                startedAt, startedAt.AddMilliseconds(500)));
        }
    }
}
