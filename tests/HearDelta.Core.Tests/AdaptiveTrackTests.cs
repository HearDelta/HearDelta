using HearDelta.Core;

namespace HearDelta.Core.Tests;

public sealed class AdaptiveTrackTests
{
    private static readonly AdaptiveTrackSettings Level = AdaptiveTrackProtocol.CreateSpeechLevelTrack(-40m, -20m);

    [Fact]
    public void LargeStepsUntilFirstErrorThenSmallSteps()
    {
        var trials = new List<AdaptiveTrackTrial>();
        Assert.Equal(-40m, AdaptiveTrackRules.NextValue(Level, trials));
        trials.Add(new(-40m, true));
        Assert.Equal(-46m, AdaptiveTrackRules.NextValue(Level, trials));
        trials.Add(new(-46m, false));
        Assert.Equal(-44m, AdaptiveTrackRules.NextValue(Level, trials));
        trials.Add(new(-44m, true));
        Assert.Equal(-46m, AdaptiveTrackRules.NextValue(Level, trials));
        Assert.True(AdaptiveTrackRules.IsConsistent(Level, trials));
        Assert.False(AdaptiveTrackRules.IsConsistent(Level, [new(-40m, true), new(-44m, true)]));
    }

    [Fact]
    public void ValueStaysWithinTheMaximum()
    {
        var trials = new List<AdaptiveTrackTrial> { new(-40m, false), new(-38m, false) };
        for (var index = 0; index < 12; index++)
            trials.Add(new(AdaptiveTrackRules.NextValue(Level, trials), false));

        Assert.Equal(-20m, AdaptiveTrackRules.NextValue(Level, trials));
        var result = AdaptiveTrackRules.Evaluate(Level, trials);
        Assert.True(result.ReachedMaximum);
        Assert.NotNull(result.Note);
    }

    [Fact]
    public void ThresholdIsMeanFromFirstErrorIncludingNextValue()
    {
        var trials = new List<AdaptiveTrackTrial>();
        // Hört genau ab -50 dB: Verlauf pendelt zwischen -50 und -52.
        for (var index = 0; index < 20; index++)
        {
            var value = AdaptiveTrackRules.NextValue(Level, trials);
            trials.Add(new(value, value >= -50m));
        }

        var result = AdaptiveTrackRules.Evaluate(Level, trials);

        // 10 × -52 und 9 × -50 einschließlich des Folgewerts.
        Assert.Equal(-51.1m, result.ThresholdDb);
        Assert.False(result.ReachedMaximum);
        Assert.True(result.AveragedTrialCount >= AdaptiveTrackProtocol.MinimumAveragedTrials);
    }

    [Fact]
    public void NoThresholdWithoutErrorOrWithTooFewTrials()
    {
        Assert.Null(AdaptiveTrackRules.Evaluate(Level, [new(-40m, true), new(-46m, true)]).ThresholdDb);
        Assert.Null(AdaptiveTrackRules.Evaluate(Level, [new(-40m, false), new(-38m, true)]).ThresholdDb);
    }

    [Fact]
    public void ParameterMustMatchEnvironment()
    {
        Assert.Empty(AdaptiveTrackRules.Validate(Level, ListeningEnvironment.Quiet));
        Assert.NotEmpty(AdaptiveTrackRules.Validate(Level, ListeningEnvironment.BackgroundNoise));
        Assert.Empty(AdaptiveTrackRules.Validate(AdaptiveTrackProtocol.CreateSignalToNoiseTrack(5m), ListeningEnvironment.BackgroundNoise));
        Assert.NotEmpty(AdaptiveTrackRules.Validate(AdaptiveTrackProtocol.CreateSignalToNoiseTrack(40m), ListeningEnvironment.BackgroundNoise));
    }
}
