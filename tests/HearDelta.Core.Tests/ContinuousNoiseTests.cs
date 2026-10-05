using HearDelta.Core;

namespace HearDelta.Core.Tests;

public sealed class ContinuousNoiseTests
{
    [Fact]
    public void NoiseLoopHasRequestedLevelOnlyOnTestedEarAndNoSeam()
    {
        var bed = StimulusAudioRenderer.RenderContinuousNoise(new ContinuousNoiseRequest(TestedEar.Right, -40m, 7, 48_000));

        var right = Channel(bed.InterleavedStereoLoop, 1);
        Assert.All(Channel(bed.InterleavedStereoLoop, 0), sample => Assert.Equal(0d, sample));
        Assert.Equal(-40d, RmsDb(right, 0, right.Length), 1);
        // Übergang am Schleifenende: die letzten und ersten 100 ms haben denselben Pegel wie der Rest.
        var tail = RmsDb(right, right.Length - 4_800, right.Length);
        var head = RmsDb(right, 0, 4_800);
        Assert.InRange(tail, -42d, -38d);
        Assert.InRange(head, -42d, -38d);
        Assert.Equal(bed.Peak, right.Max(Math.Abs), 4);
    }

    [Fact]
    public void SpeechIsScaledToTheRequestedSnrAgainstTheNoiseLevel()
    {
        var bed = StimulusAudioRenderer.RenderContinuousNoise(new ContinuousNoiseRequest(TestedEar.Left, -40m, 7, 48_000));
        var source = CreateSource();
        var speechRms = StimulusAudioRenderer.MeasureSpeechRmsDbfs(source, 22_050, 48_000, useActiveSpeechRegion: false);

        var rendered = StimulusAudioRenderer.RenderSpeechOverContinuousNoise(
            source, 22_050, new SpeechOverNoiseRequest(TestedEar.Left, -5m, bed, -10m));

        Assert.Equal(-40m - 5m - speechRms, rendered.Metadata.DigitalAttenuationDb);
        Assert.Equal(-5m, rendered.Metadata.SignalToNoiseRatioDb);
        Assert.Equal(-40m, rendered.Metadata.ContinuousNoiseLevelDbfs);
        Assert.All(Channel(rendered.InterleavedStereoSamples, 1), sample => Assert.Equal(0d, sample));
    }

    [Fact]
    public void SpeechAboveTheLevelLimitIsBlocked()
    {
        var bed = StimulusAudioRenderer.RenderContinuousNoise(new ContinuousNoiseRequest(TestedEar.Left, -40m, 7, 48_000));

        Assert.Throws<InvalidOperationException>(() => StimulusAudioRenderer.RenderSpeechOverContinuousNoise(
            CreateSource(), 22_050, new SpeechOverNoiseRequest(TestedEar.Left, 30m, bed, -40m)));
    }

    [Fact]
    public void NoiseLevelFollowsSpeechLevelAtStartSnrAndLimitsMaximumSnr()
    {
        var statistics = new SpeechLevelStatistics(-20m, 4m);

        var settings = ContinuousNoiseProtocol.Create(-50m, 5m, statistics);

        Assert.Equal(-75m, settings.NoiseLevelDbfs);
        // Leisester Stimulus: -24 dBFS RMS; Sprache darf höchstens -30 dB Absenkung haben → SNR ≤ -30 - (-75) + (-24) = 21.
        Assert.Equal(21m, ContinuousNoiseProtocol.MaximumSignalToNoiseRatioDb(settings, statistics, -30m));
        Assert.Equal(21m, AdaptiveTrackProtocol.CreateSignalToNoiseTrack(5m, 21.4m).MaximumValueDb);
    }

    private static double[] Channel(float[] interleaved, int channel) =>
        Enumerable.Range(0, interleaved.Length / 2).Select(frame => (double)interleaved[(frame * 2) + channel]).ToArray();

    private static double RmsDb(double[] samples, int start, int end) =>
        20 * Math.Log10(Math.Sqrt(samples[start..end].Sum(sample => sample * sample) / (end - start)));

    private static float[] CreateSource() =>
        Enumerable.Range(0, 22_050 / 4)
            .Select(index => (float)(Math.Sin(2 * Math.PI * 700 * index / 22_050d) * 0.2))
            .ToArray();
}
