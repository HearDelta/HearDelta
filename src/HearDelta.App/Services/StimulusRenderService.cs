using NAudio.Wave;
using HearDelta.Core;

namespace HearDelta.App.Services;

public sealed class StimulusRenderService
{
    private const double TrailingSilenceThresholdDbfs = -45d;
    private const double TrailingSilenceRetentionSeconds = 0.05d;

    public RenderedStimulusAudio Render(
        LoadedStimulusPack pack,
        string stimulusId,
        StimulusRenderRequest renderRequest)
    {
        ArgumentNullException.ThrowIfNull(pack);
        var source = ReadMonoSamples(pack.GetAudioPath(stimulusId), out var sourceSampleRate);
        if (string.Equals(pack.Catalog.Paradigm, CardinalNumberProtocol.Paradigm, StringComparison.Ordinal))
            source = TrimTrailingSilence(source, sourceSampleRate);
        var usesCardinalNoise = renderRequest.Environment == ListeningEnvironment.BackgroundNoise &&
            string.Equals(pack.Catalog.Paradigm, CardinalNumberProtocol.Paradigm, StringComparison.Ordinal);
        if (usesCardinalNoise && pack.CardinalNoiseProfile is null)
            throw new InvalidOperationException(
                Strings.Audio_NoiseProfileMissing);
        return StimulusAudioRenderer.Render(
            source,
            sourceSampleRate,
            renderRequest,
            usesCardinalNoise ? pack.CardinalNoiseProfile : null);
    }

    /// <summary>Rauschschleife des Dauerrauschens; Kardinalzahlen verwenden ihr materialgebundenes Rauschprofil.</summary>
    public ContinuousNoiseBed RenderContinuousNoise(LoadedStimulusPack pack, ContinuousNoiseRequest request) =>
        StimulusAudioRenderer.RenderContinuousNoise(request, GetCardinalNoiseProfile(pack));

    public RenderedStimulusAudio RenderSpeechOverContinuousNoise(
        LoadedStimulusPack pack,
        string stimulusId,
        SpeechOverNoiseRequest request)
    {
        ArgumentNullException.ThrowIfNull(pack);
        var source = ReadSource(pack, stimulusId, out var sourceSampleRate);
        return StimulusAudioRenderer.RenderSpeechOverContinuousNoise(source, sourceSampleRate, request, GetCardinalNoiseProfile(pack));
    }

    /// <summary>Mittlerer und kleinster Sprach-RMS aller Stimuli des Pakets, je Paket und Abtastrate einmal berechnet.</summary>
    public SpeechLevelStatistics GetSpeechLevelStatistics(LoadedStimulusPack pack, int outputSampleRate)
    {
        ArgumentNullException.ThrowIfNull(pack);
        var key = (pack.Catalog.Id, pack.Catalog.Version, outputSampleRate);
        lock (speechLevelCache)
        {
            if (speechLevelCache.TryGetValue(key, out var cached))
                return cached;
        }
        var useActiveRegion = IsCardinal(pack);
        var levels = pack.Catalog.Lists
            .SelectMany(list => list.Items)
            .Select(item => item.Id)
            .Distinct(StringComparer.Ordinal)
            .Select(id =>
            {
                var source = ReadSource(pack, id, out var sourceSampleRate);
                return StimulusAudioRenderer.MeasureSpeechRmsDbfs(source, sourceSampleRate, outputSampleRate, useActiveRegion);
            })
            .ToArray();
        var reference = decimal.Round(levels.Average(), 2, MidpointRounding.AwayFromZero);
        var statistics = new SpeechLevelStatistics(reference, reference - levels.Min());
        lock (speechLevelCache)
            speechLevelCache[key] = statistics;
        return statistics;
    }

    private readonly Dictionary<(string, string, int), SpeechLevelStatistics> speechLevelCache = [];

    private static bool IsCardinal(LoadedStimulusPack pack) =>
        string.Equals(pack.Catalog.Paradigm, CardinalNumberProtocol.Paradigm, StringComparison.Ordinal);

    private static CardinalSpeechShapedNoiseProfile? GetCardinalNoiseProfile(LoadedStimulusPack pack) =>
        !IsCardinal(pack)
            ? null
            : pack.CardinalNoiseProfile ?? throw new InvalidOperationException(
                Strings.Audio_NoiseProfileMissing);

    private static float[] ReadSource(LoadedStimulusPack pack, string stimulusId, out int sourceSampleRate)
    {
        var source = ReadMonoSamples(pack.GetAudioPath(stimulusId), out sourceSampleRate);
        return IsCardinal(pack) ? TrimTrailingSilence(source, sourceSampleRate) : source;
    }

    private static float[] TrimTrailingSilence(float[] samples, int sampleRate)
    {
        var threshold = Math.Pow(10d, TrailingSilenceThresholdDbfs / 20d);
        var lastAudibleSample = Array.FindLastIndex(samples, sample => Math.Abs(sample) >= threshold);
        if (lastAudibleSample < 0)
            return samples;

        var retainedTailSamples = (int)Math.Round(sampleRate * TrailingSilenceRetentionSeconds);
        var trimmedLength = Math.Min(samples.Length, lastAudibleSample + 1 + retainedTailSamples);
        return trimmedLength == samples.Length ? samples : samples[..trimmedLength];
    }

    private static float[] ReadMonoSamples(string audioPath, out int sampleRate)
    {
        using var reader = new WaveFileReader(audioPath);
        if (reader.WaveFormat.Channels != 1)
            throw new InvalidOperationException(Strings.Audio_NotMono);
        sampleRate = reader.WaveFormat.SampleRate;
        var provider = reader.ToSampleProvider();
        var samples = new List<float>();
        var buffer = new float[4096];
        int read;
        while ((read = provider.Read(buffer)) > 0)
            samples.AddRange(buffer.AsSpan(0, read).ToArray());
        return samples.ToArray();
    }
}
