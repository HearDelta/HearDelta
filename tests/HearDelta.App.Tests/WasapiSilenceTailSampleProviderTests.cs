using HearDelta.App.Services;

namespace HearDelta.App.Tests;

public sealed class WasapiSilenceTailSampleProviderTests
{
    [Fact]
    public void PadsFinalBufferAndContinuesWithSilence()
    {
        var provider = new WasapiSilenceTailSampleProvider([1, 2, 3, 4, 5, 6], 44100);
        var firstBuffer = new float[8];

        var firstRead = provider.Read(firstBuffer);

        Assert.Equal(8, firstRead);
        Assert.Equal([1, 2, 3, 4, 5, 6, 0, 0], firstBuffer);
        Assert.True(provider.SourceSamplesSubmitted.IsCompletedSuccessfully);

        var silenceBuffer = Enumerable.Repeat(1f, 8).ToArray();
        var silenceRead = provider.Read(silenceBuffer);

        Assert.Equal(8, silenceRead);
        Assert.All(silenceBuffer, sample => Assert.Equal(0, sample));
    }

    [Fact]
    public void SignalsCompletionOnlyAfterAllSourceSamplesWereSubmitted()
    {
        var provider = new WasapiSilenceTailSampleProvider(
            Enumerable.Range(1, 10).Select(value => (float)value).ToArray(),
            44100);
        var buffer = new float[8];

        provider.Read(buffer);

        Assert.False(provider.SourceSamplesSubmitted.IsCompleted);

        provider.Read(buffer);

        Assert.True(provider.SourceSamplesSubmitted.IsCompletedSuccessfully);
        Assert.Equal([9, 10, 0, 0, 0, 0, 0, 0], buffer);
    }
}
