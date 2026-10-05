using NAudio.CoreAudioApi;
using NAudio.Wave;
using HearDelta.Core;

namespace HearDelta.App.Services;

public sealed class AudioEndpointService : IAudioEndpointService
{
    public IReadOnlyList<AudioEndpointDescriptor> GetActiveOutputs()
    {
        using var enumerator = new MMDeviceEnumerator();
        return enumerator
            .EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active)
            .Select(device =>
            {
                using var client = device.CreateAudioClient();
                var format = client.MixFormat;
                return new AudioEndpointDescriptor(device.ID, device.FriendlyName, format.Channels, format.SampleRate, format.BitsPerSample);
            })
            .OrderBy(endpoint => endpoint.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
    }

    public async Task PlayChannelTestAsync(
        string endpointId,
        bool exclusive,
        int channel,
        decimal digitalAttenuationDb,
        decimal maximumVolumeDb,
        CancellationToken cancellationToken = default)
    {
        if (channel is < 0 or > 1)
            throw new ArgumentOutOfRangeException(nameof(channel), Strings.Audio_ChannelTestSides);
        if (maximumVolumeDb is > 0 or < -96)
            throw new ArgumentOutOfRangeException(nameof(maximumVolumeDb), Strings.Audio_MaximumRange);
        if (digitalAttenuationDb < -96 || digitalAttenuationDb > maximumVolumeDb)
            throw new ArgumentOutOfRangeException(nameof(digitalAttenuationDb), Strings.Audio_AttenuationAboveLimit);

        using var enumerator = new MMDeviceEnumerator();
        using var device = enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active)
            .SingleOrDefault(candidate => string.Equals(candidate.ID, endpointId, StringComparison.Ordinal))
            ?? throw new InvalidOperationException(Strings.Audio_EndpointUnavailable);

        using var client = device.CreateAudioClient();
        var provider = new ChannelToneProvider(client.MixFormat.SampleRate, channel, digitalAttenuationDb);
        var builder = new WasapiPlayerBuilder()
            .WithDevice(device)
            .WithLatency(100)
            .WithEventSync()
            .WithRawMode();
        builder = exclusive ? builder.WithExclusiveMode() : builder.WithSharedMode();
        using var output = builder.Build();
        output.Init(provider.ToWaveProvider());

        var stopped = new TaskCompletionSource<StoppedEventArgs>(TaskCreationOptions.RunContinuationsAsynchronously);
        output.PlaybackStopped += (_, eventArgs) => stopped.TrySetResult(eventArgs);
        using var cancellationRegistration = cancellationToken.Register(output.Stop);
        output.Play();
        await Task.Delay(TimeSpan.FromMilliseconds(550), cancellationToken);
        output.Stop();
        var stoppedEvent = await stopped.Task.WaitAsync(cancellationToken);
        if (stoppedEvent.Exception is not null)
            throw new InvalidOperationException(Strings.Audio_ChannelTestFailed, stoppedEvent.Exception);
    }

    internal sealed class ChannelToneProvider : ISampleProvider
    {
        private readonly int sampleRate;
        private readonly int channel;
        private readonly double peakAmplitude;
        private double phase;

        public ChannelToneProvider(int sampleRate, int channel, decimal digitalAttenuationDb)
        {
            this.sampleRate = sampleRate;
            this.channel = channel;
            peakAmplitude = Math.Pow(
                10,
                (double)(StimulusAudioRenderer.SourcePeakNormalizationDbfs + digitalAttenuationDb) / 20);
            WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(sampleRate, 2);
        }

        public WaveFormat WaveFormat { get; }

        public int Read(Span<float> buffer)
        {
            var frames = buffer.Length / 2;
            for (var frame = 0; frame < frames; frame++)
            {
                var sample = (float)(Math.Sin(phase) * peakAmplitude);
                phase += 2 * Math.PI * 750 / sampleRate;
                buffer[frame * 2] = channel == 0 ? sample : 0;
                buffer[frame * 2 + 1] = channel == 1 ? sample : 0;
            }
            return frames * 2;
        }
    }
}
