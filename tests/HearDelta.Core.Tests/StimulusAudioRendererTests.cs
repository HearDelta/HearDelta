using HearDelta.Core;

namespace HearDelta.Core.Tests;

public sealed class StimulusAudioRendererTests
{
    [Fact]
    public void BackgroundNoiseIsBitExactForSameSeed()
    {
        var request = CreateRequest(ListeningEnvironment.BackgroundNoise, signalToNoiseRatioDb: 5m, noiseSeed: 12345);

        var first = StimulusAudioRenderer.Render(CreateSource(), 22050, request);
        var second = StimulusAudioRenderer.Render(CreateSource(), 22050, request);

        Assert.Equal(first.InterleavedStereoSamples, second.InterleavedStereoSamples);
        Assert.Equal(StimulusAudioRenderer.NoiseAlgorithm, first.Metadata.NoiseAlgorithm);
    }

    [Fact]
    public void DifferentNoiseSeedChangesRenderedSignal()
    {
        var first = StimulusAudioRenderer.Render(
            CreateSource(),
            22050,
            CreateRequest(ListeningEnvironment.BackgroundNoise, signalToNoiseRatioDb: 5m, noiseSeed: 11));
        var second = StimulusAudioRenderer.Render(
            CreateSource(),
            22050,
            CreateRequest(ListeningEnvironment.BackgroundNoise, signalToNoiseRatioDb: 5m, noiseSeed: 12));

        Assert.NotEqual(first.InterleavedStereoSamples, second.InterleavedStereoSamples);
    }

    [Theory]
    [InlineData(TestedEar.Left, 0, 1)]
    [InlineData(TestedEar.Right, 1, 0)]
    public void MonoStimulusIsRoutedOnlyToTestedEar(TestedEar ear, int signalChannel, int silentChannel)
    {
        var rendered = StimulusAudioRenderer.Render(
            CreateSource(),
            22050,
            CreateRequest(ListeningEnvironment.Quiet, ear: ear));

        Assert.Contains(
            Enumerable.Range(0, rendered.InterleavedStereoSamples.Length / 2),
            frame => rendered.InterleavedStereoSamples[(frame * 2) + signalChannel] != 0);
        Assert.All(
            Enumerable.Range(0, rendered.InterleavedStereoSamples.Length / 2),
            frame => Assert.Equal(0, rendered.InterleavedStereoSamples[(frame * 2) + silentChannel]));
    }

    [Fact]
    public void LevelAboveStoredMaximumIsRejected()
    {
        var request = CreateRequest(ListeningEnvironment.Quiet) with
        {
            DigitalAttenuationDb = -20m,
            MaximumVolumeDb = -30m
        };

        Assert.Contains(
            StimulusAudioRenderer.Validate(CreateSource(), 22050, request),
            error => error.Contains("Pegelobergrenze"));
    }

    [Fact]
    public void QuietPresentationRejectsSignalToNoiseRatio()
    {
        var request = CreateRequest(ListeningEnvironment.Quiet) with { SignalToNoiseRatioDb = 10m };

        Assert.Contains(
            StimulusAudioRenderer.Validate(CreateSource(), 22050, request),
            error => error.Contains("In Ruhe"));
    }

    [Fact]
    public void SafeProfileProducesFiniteSignalBelowDigitalFullScale()
    {
        var rendered = StimulusAudioRenderer.Render(
            CreateSource(),
            22050,
            CreateRequest(ListeningEnvironment.BackgroundNoise, signalToNoiseRatioDb: -10m));

        Assert.All(rendered.InterleavedStereoSamples, sample => Assert.True(float.IsFinite(sample)));
        Assert.InRange(rendered.Metadata.OutputPeak, 0, 1);
    }

    [Theory]
    [InlineData(44100)]
    [InlineData(48000)]
    public void CardinalProfileKeepsSeedSnrRollsAndChannelSeparationDeterministic(int sampleRate)
    {
        var sourceCount = sampleRate / 10;
        var rollFrames = sampleRate / 5;
        var source = CreateSource(sampleRate, sourceCount);
        var request = CreateRequest(
            ListeningEnvironment.BackgroundNoise,
            signalToNoiseRatioDb: 5m,
            noiseSeed: 20260919) with
        {
            DigitalAttenuationDb = -40m,
            OutputSampleRate = sampleRate
        };
        var profile = CreateIdentityCardinalProfile();

        var first = StimulusAudioRenderer.Render(source, sampleRate, request, profile);
        var second = StimulusAudioRenderer.Render(source, sampleRate, request, profile);
        var quiet = StimulusAudioRenderer.Render(
            source,
            sampleRate,
            request with { Environment = ListeningEnvironment.Quiet, SignalToNoiseRatioDb = null });

        Assert.Equal(first.InterleavedStereoSamples, second.InterleavedStereoSamples);
        Assert.Equal(sourceCount + (2 * rollFrames), first.InterleavedStereoSamples.Length / 2);
        Assert.Equal(StimulusAudioRenderer.CardinalNoiseAlgorithm, first.Metadata.NoiseAlgorithm);
        Assert.Equal(profile.MaterialId, first.Metadata.NoiseProfileMaterialId);
        Assert.Equal(profile.ProfileSha256, first.Metadata.NoiseProfileSha256);
        Assert.Equal(0, first.InterleavedStereoSamples[0]);
        Assert.Equal(0, first.InterleavedStereoSamples[^2]);
        Assert.All(
            Enumerable.Range(0, first.InterleavedStereoSamples.Length / 2),
            frame => Assert.Equal(0, first.InterleavedStereoSamples[(frame * 2) + 1]));

        var speech = Enumerable.Range(rollFrames, sourceCount)
            .Select(frame => (double)quiet.InterleavedStereoSamples[frame * 2])
            .ToArray();
        var noise = Enumerable.Range(rollFrames, sourceCount)
            .Select(frame => (double)first.InterleavedStereoSamples[frame * 2] - quiet.InterleavedStereoSamples[frame * 2])
            .ToArray();
        var measuredSnr = 20 * Math.Log10(Rms(speech) / Rms(noise));
        Assert.InRange(measuredSnr, 4.999, 5.001);
    }

    [Fact]
    public void CardinalProfileBlocksFullScaleViolationWithoutNormalizingMix()
    {
        var request = CreateRequest(
            ListeningEnvironment.BackgroundNoise,
            signalToNoiseRatioDb: -20m,
            noiseSeed: 17) with
        {
            DigitalAttenuationDb = 0m,
            MaximumVolumeDb = 0m
        };

        var exception = Assert.Throws<InvalidOperationException>(() => StimulusAudioRenderer.Render(
            CreateSource(sampleRate: 48000, sampleCount: 4800),
            48000,
            request,
            CreateIdentityCardinalProfile()));

        Assert.Contains("Vollpegel", exception.Message);
    }

    private static StimulusRenderRequest CreateRequest(
        ListeningEnvironment environment,
        TestedEar ear = TestedEar.Left,
        decimal? signalToNoiseRatioDb = null,
        int noiseSeed = 42) => new(
            ear,
            environment,
            -40m,
            -30m,
            signalToNoiseRatioDb,
            noiseSeed,
            48000);

    private static float[] CreateSource(int sampleRate = 22050, int? sampleCount = null)
    {
        var length = sampleCount ?? sampleRate / 4;
        return Enumerable.Range(0, length)
            .Select(index => (float)(Math.Sin(2 * Math.PI * 700 * index / sampleRate) * 0.2))
            .ToArray();
    }

    private static CardinalSpeechShapedNoiseProfile CreateIdentityCardinalProfile()
    {
        var coefficients = new double[CardinalSpeechShapedNoiseProtocol.FirTapCount];
        coefficients[(coefficients.Length - 1) / 2] = 1;
        return new CardinalSpeechShapedNoiseProfile(
            "de-DE-personal-cardinal-numbers-christoph-v1",
            new string('1', 64),
            new string('2', 64),
            CardinalSpeechShapedNoiseProtocol.ApprovedProfileSha256ByMaterialId[
                "de-DE-personal-cardinal-numbers-christoph-v1"],
            new Dictionary<int, IReadOnlyList<double>>
            {
                [44100] = coefficients,
                [48000] = coefficients
            });
    }

    private static double Rms(IEnumerable<double> values)
    {
        var samples = values.ToArray();
        return Math.Sqrt(samples.Sum(value => value * value) / samples.Length);
    }
}
