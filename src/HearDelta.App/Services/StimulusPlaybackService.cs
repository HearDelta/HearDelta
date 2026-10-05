using NAudio.CoreAudioApi;
using NAudio.Wave;
using HearDelta.Core;

namespace HearDelta.App.Services;

public sealed record StimulusPlaybackReceipt(
    string CatalogId,
    string CatalogVersion,
    string StimulusId,
    string AudioSha256,
    string EndpointId,
    string EndpointName,
    StimulusRenderMetadata RenderMetadata,
    DateTimeOffset StartedAt,
    DateTimeOffset CompletedAt);

public sealed class StimulusPlaybackService : IStimulusPlaybackService
{
    private const int PlaybackLatencyMilliseconds = 100;
    private const int PlaybackDrainMarginMilliseconds = 50;
    private readonly StimulusRenderService renderService;

    public StimulusPlaybackService(StimulusRenderService? renderService = null)
    {
        this.renderService = renderService ?? new StimulusRenderService();
    }

    public async Task<StimulusPlaybackReceipt> PlayAsync(
        LoadedStimulusPack pack,
        string stimulusId,
        MeasurementHardwareSnapshot hardware,
        StimulusRenderRequest renderRequest,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(pack);
        ArgumentNullException.ThrowIfNull(hardware);
        cancellationToken.ThrowIfCancellationRequested();

        if (renderRequest.OutputSampleRate != hardware.SampleRate)
            throw new InvalidOperationException("Die Render-Abtastrate stimmt nicht mit dem Hardware-Snapshot überein.");
        if (renderRequest.MaximumVolumeDb != hardware.MaximumVolumeDb)
            throw new InvalidOperationException("Die Render-Pegelgrenze stimmt nicht mit dem Hardware-Snapshot überein.");

        // Die Kopfhörerentzerrung stammt ausschließlich aus dem Hardware-Snapshot der Messung.
        renderRequest = renderRequest with { HeadphoneEqualization = hardware.HeadphoneEqualization };
        var rendered = renderService.Render(pack, stimulusId, renderRequest);

        using var enumerator = new MMDeviceEnumerator();
        using var device = enumerator
            .EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active)
            .SingleOrDefault(candidate => string.Equals(candidate.ID, hardware.EndpointId, StringComparison.Ordinal))
            ?? throw new InvalidOperationException(
                "Der gespeicherte Audioausgang ist nicht verfügbar. Es wurde kein Ersatzgerät gewählt.");
        using var audioClient = device.CreateAudioClient();
        var endpoint = new AudioEndpointDescriptor(
            device.ID,
            device.FriendlyName,
            audioClient.MixFormat.Channels,
            audioClient.MixFormat.SampleRate,
            audioClient.MixFormat.BitsPerSample);
        var endpointErrors = AudioEndpointBindingRules.Validate(hardware, [endpoint]);
        if (endpointErrors.Count > 0)
            throw new InvalidOperationException(string.Join(" ", endpointErrors));

        var provider = new WasapiSilenceTailSampleProvider(
            rendered.InterleavedStereoSamples,
            rendered.SampleRate);
        var builder = new WasapiPlayerBuilder()
            .WithDevice(device)
            .WithLatency(PlaybackLatencyMilliseconds)
            .WithEventSync()
            .WithRawMode();
        builder = hardware.ExclusiveMode ? builder.WithExclusiveMode() : builder.WithSharedMode();
        using var output = builder.Build();
        output.Init(provider.ToWaveProvider());

        var stopped = new TaskCompletionSource<StoppedEventArgs>(TaskCreationOptions.RunContinuationsAsynchronously);
        output.PlaybackStopped += (_, eventArgs) => stopped.TrySetResult(eventArgs);
        var startedAt = DateTimeOffset.UtcNow;
        using var cancellationRegistration = cancellationToken.Register(output.Stop);
        output.Play();
        var sourceOrStop = await Task.WhenAny(provider.SourceSamplesSubmitted, stopped.Task)
            .WaitAsync(cancellationToken);
        if (sourceOrStop == stopped.Task)
        {
            var prematureStop = await stopped.Task.WaitAsync(cancellationToken);
            if (prematureStop.Exception is not null)
                throw new InvalidOperationException("Die WASAPI-Wiedergabe ist fehlgeschlagen.", prematureStop.Exception);
            throw new InvalidOperationException("Die WASAPI-Wiedergabe wurde vor dem Ende des Stimulus beendet.");
        }

        await Task.Delay(
            TimeSpan.FromMilliseconds(output.LatencyMilliseconds + PlaybackDrainMarginMilliseconds),
            cancellationToken);
        output.Stop();
        var stoppedEvent = await stopped.Task.WaitAsync(cancellationToken);
        if (stoppedEvent.Exception is not null)
            throw new InvalidOperationException("Die WASAPI-Wiedergabe ist fehlgeschlagen.", stoppedEvent.Exception);

        var asset = pack.GetAudioAsset(stimulusId);
        return new StimulusPlaybackReceipt(
            pack.Catalog.Id,
            pack.Catalog.Version,
            stimulusId,
            asset.Sha256,
            endpoint.Id,
            endpoint.Name,
            rendered.Metadata,
            startedAt,
            DateTimeOffset.UtcNow);
    }

}

internal sealed class WasapiSilenceTailSampleProvider : ISampleProvider
{
    private readonly float[] samples;
    private readonly TaskCompletionSource sourceSamplesSubmitted =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int position;

    public WasapiSilenceTailSampleProvider(float[] samples, int sampleRate)
    {
        ArgumentNullException.ThrowIfNull(samples);
        this.samples = samples;
        WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(sampleRate, 2);
        if (samples.Length == 0)
            sourceSamplesSubmitted.TrySetResult();
    }

    public WaveFormat WaveFormat { get; }
    public Task SourceSamplesSubmitted => sourceSamplesSubmitted.Task;

    public int Read(Span<float> buffer)
    {
        buffer.Clear();
        if (position < samples.Length)
        {
            var count = Math.Min(buffer.Length, samples.Length - position);
            samples.AsSpan(position, count).CopyTo(buffer);
            position += count;
            if (position >= samples.Length)
                sourceSamplesSubmitted.TrySetResult();
        }

        // NAudio 3.0.1 releases a zero-frame buffer when a finite provider returns 0.
        // Some exclusive WASAPI drivers reject that with AUDCLNT_E_BUFFER_SIZE_ERROR.
        // Keep supplying silence until StimulusPlaybackService stops after the drain interval.
        return buffer.Length;
    }
}
