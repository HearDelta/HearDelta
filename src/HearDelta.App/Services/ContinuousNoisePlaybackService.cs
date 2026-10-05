using NAudio.CoreAudioApi;
using NAudio.Wave;
using HearDelta.Core;

namespace HearDelta.App.Services;

/// <summary>
/// Spielt das Dauerrauschen eines Störgeräuschblocks über den exakt gespeicherten WASAPI-Endpunkt und mischt die
/// Sprachstimuli ein, ohne den Ausgabestrom zu unterbrechen.
/// </summary>
public sealed class ContinuousNoisePlaybackService : IContinuousNoisePlaybackService
{
    private const int PlaybackLatencyMilliseconds = 100;
    private readonly StimulusRenderService renderService;

    public ContinuousNoisePlaybackService(StimulusRenderService? renderService = null)
    {
        this.renderService = renderService ?? new StimulusRenderService();
    }

    public SpeechLevelStatistics GetSpeechLevelStatistics(LoadedStimulusPack pack, int sampleRate) =>
        renderService.GetSpeechLevelStatistics(pack, sampleRate);

    public async Task<IContinuousNoiseSession> StartAsync(
        LoadedStimulusPack pack,
        MeasurementHardwareSnapshot hardware,
        ContinuousNoiseSettings settings,
        TestedEar ear,
        int noiseSeed,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(pack);
        ArgumentNullException.ThrowIfNull(hardware);
        ArgumentNullException.ThrowIfNull(settings);
        var bed = await Task.Run(
            () => renderService.RenderContinuousNoise(pack, new ContinuousNoiseRequest(
                ear,
                settings.NoiseLevelDbfs,
                noiseSeed,
                hardware.SampleRate,
                hardware.HeadphoneEqualization)),
            cancellationToken);

        var enumerator = new MMDeviceEnumerator();
        MMDevice? device = null;
        try
        {
            device = enumerator
                .EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active)
                .SingleOrDefault(candidate => string.Equals(candidate.ID, hardware.EndpointId, StringComparison.Ordinal))
                ?? throw new InvalidOperationException(
                    Strings.Audio_EndpointUnavailable);
            AudioEndpointDescriptor endpoint;
            using (var audioClient = device.CreateAudioClient())
            {
                endpoint = new AudioEndpointDescriptor(
                    device.ID,
                    device.FriendlyName,
                    audioClient.MixFormat.Channels,
                    audioClient.MixFormat.SampleRate,
                    audioClient.MixFormat.BitsPerSample);
            }
            var endpointErrors = AudioEndpointBindingRules.Validate(hardware, [endpoint]);
            if (endpointErrors.Count > 0)
                throw new InvalidOperationException(string.Join(" ", endpointErrors));

            var provider = new ContinuousNoiseSampleProvider(bed);
            var builder = new WasapiPlayerBuilder()
                .WithDevice(device)
                .WithLatency(PlaybackLatencyMilliseconds)
                .WithEventSync()
                .WithRawMode();
            builder = hardware.ExclusiveMode ? builder.WithExclusiveMode() : builder.WithSharedMode();
            var output = builder.Build();
            output.Init(provider.ToWaveProvider());
            var session = new Session(renderService, pack, hardware, ear, bed, endpoint, enumerator, device, output, provider);
            output.Play();
            return session;
        }
        catch
        {
            device?.Dispose();
            enumerator.Dispose();
            throw;
        }
    }

    private sealed class Session(
        StimulusRenderService renderService,
        LoadedStimulusPack pack,
        MeasurementHardwareSnapshot hardware,
        TestedEar ear,
        ContinuousNoiseBed bed,
        AudioEndpointDescriptor endpoint,
        MMDeviceEnumerator enumerator,
        MMDevice device,
        IWavePlayer output,
        ContinuousNoiseSampleProvider provider) : IContinuousNoiseSession
    {
        private int disposed;

        public async Task<StimulusPlaybackReceipt> PlayAsync(
            string stimulusId,
            decimal signalToNoiseRatioDb,
            CancellationToken cancellationToken = default)
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);
            var rendered = await Task.Run(
                () => renderService.RenderSpeechOverContinuousNoise(pack, stimulusId, new SpeechOverNoiseRequest(
                    ear,
                    signalToNoiseRatioDb,
                    bed,
                    hardware.MaximumVolumeDb,
                    hardware.HeadphoneEqualization)),
                cancellationToken);
            var startedAt = DateTimeOffset.UtcNow;
            var submitted = provider.Enqueue(rendered.InterleavedStereoSamples);
            using (cancellationToken.Register(provider.CancelSpeech))
                await submitted.WaitAsync(cancellationToken);
            await Task.Delay(PlaybackLatencyMilliseconds + 50, cancellationToken);
            return new StimulusPlaybackReceipt(
                pack.Catalog.Id,
                pack.Catalog.Version,
                stimulusId,
                pack.GetAudioAsset(stimulusId).Sha256,
                endpoint.Id,
                endpoint.Name,
                rendered.Metadata,
                startedAt,
                DateTimeOffset.UtcNow);
        }

        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref disposed, 1) != 0)
                return;
            try
            {
                await provider.FadeOutAsync().WaitAsync(TimeSpan.FromSeconds(2));
                await Task.Delay(PlaybackLatencyMilliseconds + 50);
            }
            catch (TimeoutException)
            {
            }
            finally
            {
                output.Stop();
                output.Dispose();
                device.Dispose();
                enumerator.Dispose();
            }
        }
    }
}

/// <summary>Endlose Rauschschleife mit Ein- und Ausblendung und eingemischten Sprachpuffern.</summary>
internal sealed class ContinuousNoiseSampleProvider : ISampleProvider
{
    private const double FadeInSeconds = 0.3;
    private const double FadeOutSeconds = 0.15;
    private readonly object gate = new();
    private readonly float[] loop;
    private readonly int fadeInFrames;
    private readonly int fadeOutFrames;
    private readonly TaskCompletionSource fadedOut = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int loopPosition;
    private long framesPlayed;
    private int fadeOutPosition = -1;
    private float[]? speech;
    private int speechPosition;
    private TaskCompletionSource? speechSubmitted;

    public ContinuousNoiseSampleProvider(ContinuousNoiseBed bed)
    {
        loop = bed.InterleavedStereoLoop;
        WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(bed.SampleRate, 2);
        fadeInFrames = Math.Max(1, (int)Math.Round(bed.SampleRate * FadeInSeconds));
        fadeOutFrames = Math.Max(1, (int)Math.Round(bed.SampleRate * FadeOutSeconds));
    }

    public WaveFormat WaveFormat { get; }

    public Task Enqueue(float[] interleavedSpeech)
    {
        lock (gate)
        {
            speechSubmitted?.TrySetCanceled();
            speech = interleavedSpeech;
            speechPosition = 0;
            speechSubmitted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            return speechSubmitted.Task;
        }
    }

    public void CancelSpeech()
    {
        lock (gate)
        {
            speech = null;
            speechSubmitted?.TrySetCanceled();
            speechSubmitted = null;
        }
    }

    public Task FadeOutAsync()
    {
        lock (gate)
        {
            if (fadeOutPosition < 0)
                fadeOutPosition = 0;
        }
        return fadedOut.Task;
    }

    public int Read(Span<float> buffer)
    {
        lock (gate)
        {
            for (var index = 0; index + 1 < buffer.Length; index += 2)
            {
                double gain = 1;
                if (framesPlayed < fadeInFrames)
                    gain = 0.5 - (0.5 * Math.Cos(Math.PI * framesPlayed / fadeInFrames));
                if (fadeOutPosition >= 0)
                {
                    gain *= fadeOutPosition >= fadeOutFrames
                        ? 0
                        : 0.5 + (0.5 * Math.Cos(Math.PI * fadeOutPosition / fadeOutFrames));
                    fadeOutPosition++;
                }

                var left = loop[loopPosition] * gain;
                var right = loop[loopPosition + 1] * gain;
                loopPosition = (loopPosition + 2) % loop.Length;
                framesPlayed++;

                if (speech is not null && fadeOutPosition < 0)
                {
                    left += speech[speechPosition];
                    right += speech[speechPosition + 1];
                    speechPosition += 2;
                    if (speechPosition >= speech.Length)
                    {
                        speech = null;
                        speechSubmitted?.TrySetResult();
                        speechSubmitted = null;
                    }
                }

                buffer[index] = (float)left;
                buffer[index + 1] = (float)right;
            }

            if (fadeOutPosition >= fadeOutFrames)
            {
                speechSubmitted?.TrySetCanceled();
                fadedOut.TrySetResult();
            }
        }
        // Wie beim Einzelstimulus: nie 0 Frames zurückgeben, damit exklusive WASAPI-Treiber nicht abbrechen.
        return buffer.Length;
    }
}
