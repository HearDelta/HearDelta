using HearDelta.Core;

namespace HearDelta.Core.Tests;

public sealed class TestLevelRulesTests
{
    [Fact]
    public void WithoutPretestsNothingIsRecommendedExceptAdaptiveStartSnr()
    {
        var quiet = TestLevelRules.Recommend(SpeechMaterial.Numbers, ListeningEnvironment.Quiet, true, PretestResults.None);
        Assert.Null(quiet.SpeechLevelDb);
        Assert.Contains("Kein passender Vortest", quiet.Source);

        var noise = TestLevelRules.Recommend(SpeechMaterial.Numbers, ListeningEnvironment.BackgroundNoise, true, PretestResults.None);
        Assert.Null(noise.SpeechLevelDb);
        Assert.Equal(0m, noise.SignalToNoiseRatioDb);
    }

    [Fact]
    public void ToneThresholdEstimatesTheQuietStartUntilASpeechThresholdExists()
    {
        var fromTone = TestLevelRules.Recommend(
            SpeechMaterial.Numbers, ListeningEnvironment.Quiet, true, new PretestResults(ToneAverageThresholdDbfs: -73m));
        Assert.Equal(-73m + 30m + 12m, fromTone.SpeechLevelDb);
        Assert.Contains("geschätzte Ruheschwelle", fromTone.Source);

        var measured = TestLevelRules.Recommend(
            SpeechMaterial.Numbers, ListeningEnvironment.Quiet, true,
            new PretestResults(ToneAverageThresholdDbfs: -73m, QuietSpeechThresholdDb: -43.4m));
        Assert.Equal(-31m, measured.SpeechLevelDb);
    }

    [Theory]
    [InlineData(SpeechMaterial.Numbers, true, -18, 5)]
    [InlineData(SpeechMaterial.Numbers, false, -18, -1)]
    [InlineData(SpeechMaterial.PhonemeContrasts, false, -18, 3)]
    public void NoiseTestsUseQuietThresholdPlus25AndTheSnrThreshold(SpeechMaterial material, bool adaptive, int speech, int snr)
    {
        var recommendation = TestLevelRules.Recommend(
            material, ListeningEnvironment.BackgroundNoise, adaptive,
            new PretestResults(QuietSpeechThresholdDb: -43m, NoiseSnrThresholdDb: -3m));

        Assert.Equal(speech, recommendation.SpeechLevelDb);
        Assert.Equal(snr, recommendation.SignalToNoiseRatioDb);
    }

    [Fact]
    public void PhonemesInQuietUseTenDecibelsAboveTheQuietThreshold() =>
        Assert.Equal(-33m, TestLevelRules.Recommend(
            SpeechMaterial.PhonemeContrasts, ListeningEnvironment.Quiet, false,
            new PretestResults(QuietSpeechThresholdDb: -43m)).SpeechLevelDb);

    [Fact]
    public void ToneAverageUsesHeardTonesBetween400HzAnd2500Hz()
    {
        Assert.Equal(-74m, TestLevelRules.ToneAverage([(250, -60m), (500, -73m), (1000, -75m), (2000, null), (4000, -40m)]));
        Assert.Null(TestLevelRules.ToneAverage([(500, -73m), (4000, -40m)]));
    }
}
