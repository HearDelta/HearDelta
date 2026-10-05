using HearDelta.App.Services;
using HearDelta.Core;

namespace HearDelta.App.Tests;

public sealed class ChannelToneProviderTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void UsesRequestedChannelAndDigitalAttenuation(int activeChannel)
    {
        const int sampleRate = 48000;
        const decimal attenuationDb = -60m;
        var provider = new AudioEndpointService.ChannelToneProvider(sampleRate, activeChannel, attenuationDb);
        var samples = new float[128];

        var read = provider.Read(samples);

        Assert.Equal(samples.Length, read);
        var activeSamples = samples.Where((_, index) => index % 2 == activeChannel).ToArray();
        var mutedSamples = samples.Where((_, index) => index % 2 != activeChannel).ToArray();
        var expectedPeak = Math.Pow(
            10,
            (double)(StimulusAudioRenderer.SourcePeakNormalizationDbfs + attenuationDb) / 20);
        Assert.Equal(expectedPeak, activeSamples.Max(Math.Abs), 7);
        Assert.All(mutedSamples, sample => Assert.Equal(0, sample));
    }
}
