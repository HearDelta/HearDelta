using HearDelta.App.Services;
using HearDelta.Core;

namespace HearDelta.App.Tests;

public sealed class ThresholdTonePlaybackServiceTests
{
    // Bei 1 kHz Abtastrate entspricht ein Frame genau einer Millisekunde.
    private const int MillisecondRate = 1_000;

    [Fact]
    public void CurrentPatternIsThreeShortTonesPauseThreeShortTonesTwicePerLevel()
    {
        var pattern = ThresholdSignalPattern.Current;

        Assert.Empty(pattern.Validate());
        Assert.Equal(12, pattern.GetTonesPerLevel());
        Assert.Equal(
            [0, 250, 500, 1_150, 1_400, 1_650, 2_550, 2_800, 3_050, 3_700, 3_950, 4_200],
            pattern.GetToneOnsetsMilliseconds());
        Assert.Equal(1_800, pattern.GetSignalDurationMilliseconds());
        Assert.Equal(5_100, pattern.GetLevelDurationMilliseconds());
    }

    [Fact]
    public void HighFrequencyToneUsesZeroEndpointsAndGradualCosineAttack()
    {
        var provider = new PulsedRisingSineSampleProvider(CreateRequest(TestedEar.Left, 10_000d, -50m, -49m, 48_000));
        var firstTone = new float[2 * 7_200];

        Assert.Equal(firstTone.Length, provider.Read(firstTone));
        Assert.Equal(0f, firstTone[0]);
        Assert.Equal(0f, firstTone[^2]);

        var firstFourMillisecondsPeak = firstTone.Take(2 * 192).Where((_, index) => index % 2 == 0).Max(Math.Abs);
        var steadyPeak = firstTone.Skip(2 * 1_500).Take(2 * 4_200).Where((_, index) => index % 2 == 0).Max(Math.Abs);
        Assert.True(firstFourMillisecondsPeak < steadyPeak * 0.11f);
    }

    [Theory]
    [InlineData(TestedEar.Left, 0)]
    [InlineData(TestedEar.Right, 1)]
    public void SignalFollowsThePatternOnOneChannelAndRisesOneStepPerLevel(TestedEar ear, int activeChannel)
    {
        var provider = new PulsedRisingSineSampleProvider(CreateRequest(ear, 100, -40m, -37m, MillisecondRate));
        // Zwei Stufen: 5.100 ms für die erste, danach bis zum Ende des zwölften Tons der zweiten (4.200 + 150 ms).
        var frames = new float[2 * 9_450];

        Assert.Equal(frames.Length, provider.Read(frames));
        Assert.True(provider.ReachedMaximum);
        Assert.True(provider.SourceSamplesSubmitted.IsCompletedSuccessfully);
        Assert.Equal(-37m, provider.CurrentAttenuationDbfs);

        var active = frames.Where((_, index) => index % 2 == activeChannel).ToArray();
        Assert.All(frames.Where((_, index) => index % 2 != activeChannel), sample => Assert.Equal(0f, sample));

        // Töne klingen, Lücken, Gruppenpause und Signalpause sind still.
        Assert.Contains(active.Skip(20).Take(110), sample => sample != 0f);
        Assert.All(active.Skip(150).Take(100), sample => Assert.Equal(0f, sample));
        Assert.All(active.Skip(650).Take(500), sample => Assert.Equal(0f, sample));
        Assert.All(active.Skip(1_800).Take(750), sample => Assert.Equal(0f, sample));
        Assert.Contains(active.Skip(4_220).Take(110), sample => sample != 0f);
        Assert.All(active.Skip(4_350).Take(750), sample => Assert.Equal(0f, sample));

        var firstLevelPeak = active.Take(5_100).Max(Math.Abs);
        var secondLevelPeak = active.Skip(5_100).Max(Math.Abs);
        Assert.Equal(Math.Pow(10, 3 / 20d), secondLevelPeak / firstLevelPeak, precision: 2);

        var silence = Enumerable.Repeat(1f, 20).ToArray();
        Assert.Equal(silence.Length, provider.Read(silence));
        Assert.All(silence, sample => Assert.Equal(0f, sample));
    }

    [Fact]
    public void ToneCallbackReportsTheLevelOfEveryRenderedTone()
    {
        var reportedLevels = new List<decimal>();
        var provider = new PulsedRisingSineSampleProvider(CreateRequest(TestedEar.Left, 500, -40m, -34m, MillisecondRate), toneStarted: reportedLevels.Add);

        provider.Read(new float[2 * 20_000]);

        Assert.Equal(
            Enumerable.Repeat(-40m, 12).Concat(Enumerable.Repeat(-37m, 12)).Concat(Enumerable.Repeat(-34m, 12)),
            reportedLevels);
    }

    [Fact]
    public void HeadphoneCorrectionAttenuatesTheWholeToneAndMayNeverBoost()
    {
        var plain = new float[2 * 7_200];
        var corrected = new float[2 * 7_200];
        new PulsedRisingSineSampleProvider(CreateRequest(TestedEar.Left, 1_000d, -30m, -29m, 48_000)).Read(plain);
        new PulsedRisingSineSampleProvider(
            CreateRequest(TestedEar.Left, 1_000d, -30m, -29m, 48_000) with { HeadphoneCorrectionDb = -6m }).Read(corrected);

        var ratioDb = 20 * Math.Log10(corrected.Max(Math.Abs) / plain.Max(Math.Abs));
        Assert.Equal(-6d, ratioDb, 0.01);
        Assert.Throws<ArgumentOutOfRangeException>(() => new PulsedRisingSineSampleProvider(
            CreateRequest(TestedEar.Left, 1_000d, -30m, -29m, 48_000) with { HeadphoneCorrectionDb = 0.5m }));
    }

    [Theory]
    [InlineData(TestedEar.Left, 0)]
    [InlineData(TestedEar.Right, 1)]
    public void MaskingPlaysNarrowbandNoiseOnTheOppositeChannelWithLeadIn(TestedEar ear, int toneChannel)
    {
        const int sampleRate = 48_000;
        var masking = ThresholdMaskingProtocol.Create(-50m, seed: 7);
        var request = CreateRequest(ear, 1_000d, -30m, -29m, sampleRate) with { Masking = masking };
        var loop = ThresholdMaskingProtocol.CreateNoiseLoop(1_000d, sampleRate, masking.Seed);
        var provider = new PulsedRisingSineSampleProvider(request, loop);
        var leadIn = sampleRate * masking.LeadInMilliseconds / 1_000;
        var toneEnd = leadIn + (sampleRate * (5_100 + 4_200 + 150) / 1_000);
        var total = toneEnd + (sampleRate * masking.FadeMilliseconds / 1_000);
        var frames = new float[2 * total];

        Assert.Equal(frames.Length, provider.Read(frames));
        Assert.True(provider.ReachedMaximum);
        var tone = frames.Where((_, index) => index % 2 == toneChannel).ToArray();
        var noise = frames.Where((_, index) => index % 2 != toneChannel).ToArray();

        // Während des Vorlaufs nur Rauschen; der erste Ton beginnt danach.
        Assert.All(tone.Take(leadIn), sample => Assert.Equal(0f, sample));
        Assert.Contains(tone.Skip(leadIn).Take(sampleRate / 10), sample => sample != 0f);
        // Rauschen blendet von Null ein und auf Null aus und hält dazwischen den RMS-Pegel.
        Assert.Equal(0f, noise[0]);
        Assert.True(Math.Abs(noise[^1]) < 1e-4f);
        var steady = noise.Skip(sampleRate / 10).Take(sampleRate * 2).ToArray();
        var rmsDb = 10 * Math.Log10(steady.Average(sample => (double)sample * sample));
        Assert.Equal(-50d, rmsDb, 0.3);
        Assert.True(noise.Max(Math.Abs) < Math.Pow(10, -6 / 20d));
    }

    [Fact]
    public void MaskingNoiseFollowsHeadphoneCorrectionAndRejectsPeaksAboveTheLimit()
    {
        var loop = ThresholdMaskingProtocol.CreateNoiseLoop(1_000d, 48_000, 1);

        Assert.Equal(
            ThresholdTonePlaybackService.MaskingAmplitude(-40m, 0d, loop, -6m) / 2d,
            ThresholdTonePlaybackService.MaskingAmplitude(-40m, -20 * Math.Log10(2), loop, -6m),
            precision: 9);
        Assert.Throws<ArgumentOutOfRangeException>(() => ThresholdTonePlaybackService.MaskingAmplitude(-40m, 1d, loop, -6m));
        Assert.Throws<InvalidOperationException>(() => ThresholdTonePlaybackService.MaskingAmplitude(-3m, 0d, loop, -6m));
    }

    [Fact]
    public void MaskingRequiresMatchingNoiseLoop()
    {
        var request = CreateRequest(TestedEar.Left, 1_000d, -30m, -29m, 48_000);

        Assert.Throws<ArgumentException>(() => new PulsedRisingSineSampleProvider(
            request with { Masking = ThresholdMaskingProtocol.Create(-40m, 1) }));
        Assert.Throws<ArgumentException>(() => new PulsedRisingSineSampleProvider(
            request, ThresholdMaskingProtocol.CreateNoiseLoop(1_000d, 48_000, 1)));
    }

    [Fact]
    public void MaskingPreviewPlaysOnlyOnTheMaskedEarAndFades()
    {
        const int sampleRate = 48_000;
        var loop = ThresholdMaskingProtocol.CreateNoiseLoop(500d, sampleRate, 0);
        var provider = new MaskingPreviewSampleProvider(TestedEar.Right, loop, 0.01d, sampleRate);
        var frames = new float[2 * sampleRate * MaskingPreviewSampleProvider.DurationMilliseconds / 1_000];

        provider.Read(frames);

        Assert.True(provider.SourceSamplesSubmitted.IsCompletedSuccessfully);
        Assert.All(frames.Where((_, index) => index % 2 == 0), sample => Assert.Equal(0f, sample));
        var right = frames.Where((_, index) => index % 2 == 1).ToArray();
        Assert.Equal(0f, right[0]);
        Assert.True(Math.Abs(right[^1]) < 1e-4f);
        Assert.Contains(right, sample => sample != 0f);
    }

    private static ThresholdTonePlaybackRequest CreateRequest(
        TestedEar ear,
        double frequencyHz,
        decimal startAttenuationDbfs,
        decimal maximumAttenuationDbfs,
        int sampleRate) => new(
            ear,
            frequencyHz,
            startAttenuationDbfs,
            maximumAttenuationDbfs,
            HearingThresholdProtocol.LevelStepDb,
            ThresholdSignalPattern.Current,
            sampleRate);
}
