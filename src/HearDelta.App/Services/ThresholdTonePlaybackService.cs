using System.Diagnostics;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using HearDelta.Core;

namespace HearDelta.App.Services;

public sealed record ThresholdTonePlaybackRequest(
    TestedEar Ear,
    double FrequencyHz,
    decimal StartAttenuationDbfs,
    decimal MaximumAttenuationDbfs,
    decimal LevelStepDb,
    ThresholdSignalPattern SignalPattern,
    int OutputSampleRate,
    decimal HeadphoneCorrectionDb = 0m,
    ThresholdMasking? Masking = null);

public sealed record ThresholdTonePlaybackReceipt(
    string EndpointId,
    string EndpointName,
    double FrequencyHz,
    decimal StartAttenuationDbfs,
    decimal EndAttenuationDbfs,
    decimal MaximumAttenuationDbfs,
    decimal LevelStepDb,
    int OutputSampleRate,
    DateTimeOffset StartedAt,
    DateTimeOffset CompletedAt,
    bool ReachedMaximum,
    ThresholdSignalPattern SignalPattern,
    decimal? HeadphoneCorrectionDb = null,
    decimal? MaskingLevelDbfs = null);

public sealed class ThresholdTonePlaybackService : IThresholdTonePlaybackService
{
    private const int PlaybackLatencyMilliseconds = 100;
    private const int PlaybackDrainMarginMilliseconds = 50;

    public async Task<ThresholdTonePlaybackReceipt> PlayAsync(
        ThresholdTonePlaybackRequest request,
        MeasurementHardwareSnapshot hardware,
        CancellationToken stopSignal)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(hardware);
        stopSignal.ThrowIfCancellationRequested();
        ValidateRequest(request, hardware);
        decimal? headphoneCorrectionDb = hardware.HeadphoneEqualization is { } equalization
            ? decimal.Round(
                (decimal)HeadphoneEqualizer.GetToneCorrectionDb(equalization, request.FrequencyHz, request.OutputSampleRate),
                2,
                MidpointRounding.AwayFromZero)
            : null;
        request = request with { HeadphoneCorrectionDb = headphoneCorrectionDb ?? 0m };

        var noiseLoop = request.Masking is { } masking
            ? ThresholdMaskingProtocol.CreateNoiseLoop(request.FrequencyHz, request.OutputSampleRate, masking.Seed)
            : null;
        var provider = new PulsedRisingSineSampleProvider(
            request,
            noiseLoop,
            attenuationDbfs => Debug.WriteLine(
                $"[{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss.fff zzz}] Hörschwellenton: " +
                $"{request.FrequencyHz:0.##} Hz, Pegel {attenuationDbfs:0.##} dBFS."));
        var (endpoint, startedAt) = await PlayToEndAsync(provider, hardware, stopSignal);
        if (!provider.ReachedMaximum && !stopSignal.IsCancellationRequested)
            throw new InvalidOperationException(Strings.Audio_ToneStoppedEarly);

        return new ThresholdTonePlaybackReceipt(
            endpoint.Id,
            endpoint.Name,
            request.FrequencyHz,
            request.StartAttenuationDbfs,
            provider.CurrentAttenuationDbfs,
            request.MaximumAttenuationDbfs,
            request.LevelStepDb,
            request.OutputSampleRate,
            startedAt,
            DateTimeOffset.UtcNow,
            provider.ReachedMaximum,
            request.SignalPattern,
            headphoneCorrectionDb,
            request.Masking?.LevelDbfs);
    }

    /// <summary>Hörprobe des Vertäubungsrauschens um 500 Hz auf dem Gegenohr, ohne Ton und ohne Protokolleintrag.</summary>
    public async Task PlayMaskingPreviewAsync(
        TestedEar maskedEar,
        decimal levelDbfs,
        MeasurementHardwareSnapshot hardware,
        CancellationToken stopSignal)
    {
        ArgumentNullException.ThrowIfNull(hardware);
        stopSignal.ThrowIfCancellationRequested();
        if (!Enum.IsDefined(maskedEar))
            throw new ArgumentOutOfRangeException(nameof(maskedEar), "Das vertäubte Ohr ist ungültig.");
        var masking = ThresholdMaskingProtocol.Create(levelDbfs, seed: 0);
        if (ThresholdMaskingProtocol.Validate(masking) is { Count: > 0 } errors)
            throw new ArgumentOutOfRangeException(nameof(levelDbfs), string.Join(" ", errors));
        var frequencyHz = HearingThresholdProtocol.CenterFrequencyHz;
        var correctionDb = hardware.HeadphoneEqualization is { } equalization
            ? HeadphoneEqualizer.GetToneCorrectionDb(equalization, frequencyHz, hardware.SampleRate)
            : 0d;
        var noiseLoop = ThresholdMaskingProtocol.CreateNoiseLoop(frequencyHz, hardware.SampleRate, masking.Seed);
        var provider = new MaskingPreviewSampleProvider(
            maskedEar,
            noiseLoop,
            MaskingAmplitude(levelDbfs, correctionDb, noiseLoop, hardware.MaximumVolumeDb),
            hardware.SampleRate);
        await PlayToEndAsync(provider, hardware, stopSignal);
    }

    /// <summary>
    /// Lineare Amplitude des Vertäubungsrauschens aus RMS-Pegel und Kopfhörerkorrektur. Wie der Ton darf das Rauschen
    /// die Pegelobergrenze des Messprofils (Spitzenwert) nicht überschreiten; sonst wird die Wiedergabe blockiert.
    /// </summary>
    internal static double MaskingAmplitude(decimal levelDbfs, double correctionDb, float[] noiseLoop, decimal maximumAttenuationDbfs)
    {
        if (correctionDb > 0)
            throw new ArgumentOutOfRangeException(nameof(correctionDb), "Die Kopfhörerkorrektur darf das Rauschen nicht anheben.");
        var amplitude = Math.Pow(10d, ((double)levelDbfs + correctionDb) / 20d);
        var peak = noiseLoop.Max(value => Math.Abs(value)) * amplitude;
        if (peak > Math.Pow(10d, (double)maximumAttenuationDbfs / 20d))
            throw new InvalidOperationException(Strings.Audio_MaskingAboveLimit);
        return amplitude;
    }

    private static async Task<(AudioEndpointDescriptor Endpoint, DateTimeOffset StartedAt)> PlayToEndAsync(
        IFiniteSampleProvider provider,
        MeasurementHardwareSnapshot hardware,
        CancellationToken stopSignal)
    {
        using var enumerator = new MMDeviceEnumerator();
        using var device = enumerator
            .EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active)
            .SingleOrDefault(candidate => string.Equals(candidate.ID, hardware.EndpointId, StringComparison.Ordinal))
            ?? throw new InvalidOperationException(
                Strings.Audio_EndpointUnavailable);
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
        using var stopRegistration = stopSignal.Register(output.Stop);
        output.Play();
        var sourceOrStop = await Task.WhenAny(provider.SourceSamplesSubmitted, stopped.Task);
        if (sourceOrStop == provider.SourceSamplesSubmitted)
        {
            var drainOrStop = await Task.WhenAny(
                Task.Delay(TimeSpan.FromMilliseconds(
                    output.LatencyMilliseconds + PlaybackDrainMarginMilliseconds)),
                stopped.Task);
            if (drainOrStop != stopped.Task)
                output.Stop();
        }

        var stoppedEvent = await stopped.Task;
        if (stoppedEvent.Exception is not null)
            throw new InvalidOperationException(Strings.Audio_ToneFailed, stoppedEvent.Exception);
        return (endpoint, startedAt);
    }

    private static void ValidateRequest(
        ThresholdTonePlaybackRequest request,
        MeasurementHardwareSnapshot hardware)
    {
        if (!Enum.IsDefined(request.Ear))
            throw new ArgumentOutOfRangeException(nameof(request), "Das geprüfte Ohr ist ungültig.");
        if (request.FrequencyHz < 20 || request.FrequencyHz > 20_000 ||
            request.FrequencyHz >= request.OutputSampleRate / 2d)
            throw new ArgumentOutOfRangeException(nameof(request), "Die Tonfrequenz liegt außerhalb des ausgebbaren Bereichs.");
        if (request.StartAttenuationDbfs >= request.MaximumAttenuationDbfs)
            throw new ArgumentException("Der Startpegel muss unter der digitalen Pegelobergrenze liegen.", nameof(request));
        if (request.MaximumAttenuationDbfs > hardware.MaximumVolumeDb || request.MaximumAttenuationDbfs > 0)
            throw new ArgumentException("Die Tonrampe überschreitet die Pegelobergrenze des Hardware-Snapshots.", nameof(request));
        if (request.LevelStepDb <= 0)
            throw new ArgumentOutOfRangeException(nameof(request), "Der Pegelschritt muss positiv sein.");
        if (request.SignalPattern.Validate() is { Count: > 0 } patternErrors)
            throw new ArgumentOutOfRangeException(nameof(request), string.Join(" ", patternErrors));
        if (request.OutputSampleRate != hardware.SampleRate)
            throw new ArgumentException("Die Ton-Abtastrate stimmt nicht mit dem Hardware-Snapshot überein.", nameof(request));
        if (hardware.HeadphoneEqualization is { } equalization &&
            HeadphoneEqualizer.Validate(equalization, request.OutputSampleRate) is { Count: > 0 } equalizationErrors)
            throw new ArgumentException(string.Join(" ", equalizationErrors), nameof(request));
        if (request.Masking is { } masking &&
            ThresholdMaskingProtocol.Validate(masking) is { Count: > 0 } maskingErrors)
            throw new ArgumentException(string.Join(" ", maskingErrors), nameof(request));
    }

}

internal interface IFiniteSampleProvider : ISampleProvider
{
    /// <summary>Erfüllt, sobald alle Quellabtastwerte an WASAPI übergeben sind.</summary>
    Task SourceSamplesSubmitted { get; }
}

/// <summary>
/// Erzeugt das Tonsignal des Hörschwellentests: je Pegelstufe das Muster aus <see cref="ThresholdSignalPattern"/>,
/// danach steigt der Pegel um den Pegelschritt bis zur Obergrenze. Nur der Kanal des geprüften Ohrs erhält den Ton.
/// Mit Vertäubung läuft auf dem anderen Kanal Schmalbandrauschen mit festem Pegel: eingeblendet, zunächst
/// <see cref="ThresholdMasking.LeadInMilliseconds"/> allein und nach dem letzten Ton ausgeblendet.
/// Eine Kopfhörerentzerrung wirkt als feste, nie positive Pegelkorrektur auf Ton und Rauschen.
/// </summary>
internal sealed class PulsedRisingSineSampleProvider : IFiniteSampleProvider
{
    private readonly int activeChannel;
    private readonly double frequencyHz;
    private readonly double startAttenuationDbfs;
    private readonly double maximumAttenuationDbfs;
    private readonly double levelStepDb;
    private readonly double correctionGain;
    private readonly long toneFrames;
    private readonly long[] toneOnsetFrames;
    private readonly long levelFrames;
    private readonly long leadInFrames;
    private readonly long toneEndFrame;
    private readonly long totalFrames;
    private readonly int fadeFrames;
    private readonly float[]? noiseLoop;
    private readonly double noiseAmplitude;
    private readonly long noiseFadeFrames;
    private readonly Action<decimal>? toneStarted;
    private readonly TaskCompletionSource sourceSamplesSubmitted =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private long renderedFrames;
    private double phase;
    private double currentAttenuationDbfs;

    public PulsedRisingSineSampleProvider(
        ThresholdTonePlaybackRequest request,
        float[]? noiseLoop = null,
        Action<decimal>? toneStarted = null)
    {
        if ((noiseLoop is null) != (request.Masking is null))
            throw new ArgumentException("Rauschschleife und Vertäubung gehören zusammen.", nameof(noiseLoop));
        var pattern = request.SignalPattern;
        long Frames(int milliseconds) =>
            (long)Math.Round(request.OutputSampleRate * milliseconds / 1_000d, MidpointRounding.AwayFromZero);

        activeChannel = request.Ear == TestedEar.Left ? 0 : 1;
        frequencyHz = request.FrequencyHz;
        startAttenuationDbfs = (double)request.StartAttenuationDbfs;
        maximumAttenuationDbfs = (double)request.MaximumAttenuationDbfs;
        levelStepDb = (double)request.LevelStepDb;
        if (request.HeadphoneCorrectionDb > 0)
            throw new ArgumentOutOfRangeException(nameof(request), "Die Kopfhörerkorrektur darf den Ton nicht anheben.");
        correctionGain = Math.Pow(10d, (double)request.HeadphoneCorrectionDb / 20d);
        toneFrames = Math.Max(1, Frames(pattern.ToneMilliseconds));
        toneOnsetFrames = pattern.GetToneOnsetsMilliseconds().Select(Frames).ToArray();
        levelFrames = Frames(pattern.GetLevelDurationMilliseconds());
        var levelCount = Math.Max(
            1,
            (long)Math.Ceiling((maximumAttenuationDbfs - startAttenuationDbfs) / levelStepDb) + 1);
        if (request.Masking is { } masking)
        {
            this.noiseLoop = noiseLoop;
            noiseAmplitude = ThresholdTonePlaybackService.MaskingAmplitude(
                masking.LevelDbfs, (double)request.HeadphoneCorrectionDb, noiseLoop!, request.MaximumAttenuationDbfs);
            leadInFrames = Frames(masking.LeadInMilliseconds);
            noiseFadeFrames = Math.Max(2, Frames(masking.FadeMilliseconds));
        }
        // Die Töne enden mit dem letzten Ton der obersten Stufe, ohne abschließende Pause; ein Vertäubungsrauschen
        // wird danach noch ausgeblendet.
        toneEndFrame = leadInFrames + ((levelCount - 1) * levelFrames) + toneOnsetFrames[^1] + toneFrames;
        totalFrames = toneEndFrame + (this.noiseLoop is null ? 0 : noiseFadeFrames);
        fadeFrames = (int)Math.Max(2, Math.Min(Frames(pattern.FadeMilliseconds), toneFrames / 2));
        currentAttenuationDbfs = startAttenuationDbfs;
        this.toneStarted = toneStarted;
        WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(request.OutputSampleRate, 2);
    }

    public WaveFormat WaveFormat { get; }
    public Task SourceSamplesSubmitted => sourceSamplesSubmitted.Task;

    public decimal CurrentAttenuationDbfs =>
        decimal.Round((decimal)Volatile.Read(ref currentAttenuationDbfs), 2, MidpointRounding.AwayFromZero);

    public bool ReachedMaximum => Interlocked.Read(ref renderedFrames) >= toneEndFrame;

    public int Read(Span<float> buffer)
    {
        buffer.Clear();
        var availableFrames = totalFrames - renderedFrames;
        if (availableFrames <= 0)
        {
            Volatile.Write(ref currentAttenuationDbfs, maximumAttenuationDbfs);
            sourceSamplesSubmitted.TrySetResult();
            return buffer.Length;
        }

        var requestedFrames = buffer.Length / 2;
        var framesToWrite = (int)Math.Min(requestedFrames, availableFrames);
        var phaseIncrement = 2d * Math.PI * frequencyHz / WaveFormat.SampleRate;
        for (var frame = 0; frame < framesToWrite; frame++)
        {
            var absoluteFrame = renderedFrames + frame;
            var toneFrame = absoluteFrame - leadInFrames;
            var sampleOffset = frame * 2;
            var sample = 0f;
            if (toneFrame >= 0 && absoluteFrame < toneEndFrame &&
                TryGetFrameInTone(toneFrame % levelFrames, out var frameInTone))
            {
                var level = toneFrame / levelFrames;
                var attenuationDbfs = Math.Min(maximumAttenuationDbfs, startAttenuationDbfs + (level * levelStepDb));
                if (frameInTone == 0)
                {
                    phase = 0;
                    toneStarted?.Invoke(decimal.Round((decimal)attenuationDbfs, 2, MidpointRounding.AwayFromZero));
                }
                var attackProgress = Math.Min(1d, frameInTone / (fadeFrames - 1d));
                var releaseProgress = Math.Min(1d, (toneFrames - frameInTone - 1d) / (fadeFrames - 1d));
                var fadeProgress = Math.Min(attackProgress, releaseProgress);
                var envelope = 0.5d - (0.5d * Math.Cos(Math.PI * fadeProgress));
                var amplitude = Math.Pow(10d, attenuationDbfs / 20d) * correctionGain;
                sample = (float)(Math.Sin(phase) * amplitude * envelope);
                phase += phaseIncrement;
                if (phase >= 2d * Math.PI)
                    phase -= 2d * Math.PI;
                Volatile.Write(ref currentAttenuationDbfs, attenuationDbfs);
            }
            var noise = 0f;
            if (noiseLoop is not null)
            {
                var fadeIn = Math.Min(1d, absoluteFrame / (noiseFadeFrames - 1d));
                var fadeOut = Math.Min(1d, (totalFrames - absoluteFrame - 1d) / (noiseFadeFrames - 1d));
                var noiseEnvelope = 0.5d - (0.5d * Math.Cos(Math.PI * Math.Min(fadeIn, fadeOut)));
                noise = (float)(noiseLoop[absoluteFrame % noiseLoop.Length] * noiseAmplitude * noiseEnvelope);
            }
            buffer[sampleOffset] = activeChannel == 0 ? sample : noise;
            buffer[sampleOffset + 1] = activeChannel == 1 ? sample : noise;
        }

        renderedFrames += framesToWrite;
        if (renderedFrames >= totalFrames)
        {
            Volatile.Write(ref currentAttenuationDbfs, maximumAttenuationDbfs);
            sourceSamplesSubmitted.TrySetResult();
        }

        // NAudio 3.0.1 releases a zero-frame buffer when a finite provider returns 0.
        // Some exclusive WASAPI drivers reject that with AUDCLNT_E_BUFFER_SIZE_ERROR.
        // Keep supplying silence until ThresholdTonePlaybackService stops after the drain interval.
        if (sourceSamplesSubmitted.Task.IsCompletedSuccessfully)
            return buffer.Length;
        return framesToWrite * 2;
    }

    private bool TryGetFrameInTone(long frameInLevel, out long frameInTone)
    {
        foreach (var onset in toneOnsetFrames)
        {
            if (frameInLevel < onset)
                break;
            if (frameInLevel < onset + toneFrames)
            {
                frameInTone = frameInLevel - onset;
                return true;
            }
        }
        frameInTone = 0;
        return false;
    }
}

/// <summary>Hörprobe des Vertäubungsrauschens: 3 s auf dem Gegenohr mit Ein- und Ausblendung.</summary>
internal sealed class MaskingPreviewSampleProvider : IFiniteSampleProvider
{
    public const int DurationMilliseconds = 3_000;
    private readonly int maskedChannel;
    private readonly float[] noiseLoop;
    private readonly double amplitude;
    private readonly long totalFrames;
    private readonly long fadeFrames;
    private readonly TaskCompletionSource sourceSamplesSubmitted =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private long renderedFrames;

    public MaskingPreviewSampleProvider(TestedEar maskedEar, float[] noiseLoop, double amplitude, int sampleRate)
    {
        maskedChannel = maskedEar == TestedEar.Left ? 0 : 1;
        this.noiseLoop = noiseLoop;
        this.amplitude = amplitude;
        totalFrames = (long)sampleRate * DurationMilliseconds / 1_000;
        fadeFrames = Math.Max(2, (long)sampleRate * ThresholdMaskingProtocol.FadeMilliseconds / 1_000);
        WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(sampleRate, 2);
    }

    public WaveFormat WaveFormat { get; }
    public Task SourceSamplesSubmitted => sourceSamplesSubmitted.Task;

    public int Read(Span<float> buffer)
    {
        buffer.Clear();
        var framesToWrite = (int)Math.Min(buffer.Length / 2, Math.Max(0, totalFrames - renderedFrames));
        for (var frame = 0; frame < framesToWrite; frame++)
        {
            var absoluteFrame = renderedFrames + frame;
            var fade = Math.Min(
                Math.Min(1d, absoluteFrame / (fadeFrames - 1d)),
                Math.Min(1d, (totalFrames - absoluteFrame - 1d) / (fadeFrames - 1d)));
            var envelope = 0.5d - (0.5d * Math.Cos(Math.PI * fade));
            buffer[(frame * 2) + maskedChannel] = (float)(noiseLoop[absoluteFrame % noiseLoop.Length] * amplitude * envelope);
        }
        renderedFrames += framesToWrite;
        if (renderedFrames >= totalFrames)
            sourceSamplesSubmitted.TrySetResult();
        // Wie beim Tonsignal Stille nachliefern, bis die Wiedergabe nach dem Ausklingen gestoppt wird.
        return buffer.Length;
    }
}
