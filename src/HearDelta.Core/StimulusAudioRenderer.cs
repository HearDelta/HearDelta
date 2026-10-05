namespace HearDelta.Core;

public sealed record StimulusRenderRequest(
    TestedEar Ear,
    ListeningEnvironment Environment,
    decimal DigitalAttenuationDb,
    decimal MaximumVolumeDb,
    decimal? SignalToNoiseRatioDb,
    int NoiseSeed,
    int OutputSampleRate,
    HeadphoneEqualization? HeadphoneEqualization = null);

public sealed record StimulusRenderMetadata(
    string RendererVersion,
    string NoiseAlgorithm,
    decimal SourcePeakNormalizationDbfs,
    decimal DigitalAttenuationDb,
    decimal? SignalToNoiseRatioDb,
    int NoiseSeed,
    int OutputSampleRate,
    float OutputPeak,
    string? NoiseProfileMaterialId = null,
    string? NoiseProfileSha256 = null,
    string? HeadphoneEqualizationSha256 = null,
    decimal? HeadphoneEqualizationPreampDb = null,
    decimal? ContinuousNoiseLevelDbfs = null);

public sealed record RenderedStimulusAudio(
    int SampleRate,
    float[] InterleavedStereoSamples,
    StimulusRenderMetadata Metadata);

public static class StimulusAudioRenderer
{
    public const string RendererVersion = "2.1.0";
    public const string NoiseAlgorithm = "band-limited-white-v1";
    public const string CardinalNoiseAlgorithm = "cardinal-speech-shaped-noise-v1";
    public const decimal SourcePeakNormalizationDbfs = -6m;
    private const decimal MinimumDigitalAttenuationDb = -96m;
    private const double PaddingSeconds = 0.2;
    private const double FadeSeconds = 0.02;

    public static IReadOnlyList<string> Validate(
        IReadOnlyList<float> monoSource,
        int sourceSampleRate,
        StimulusRenderRequest request)
    {
        var errors = new List<string>();
        if (monoSource is null || monoSource.Count == 0)
            errors.Add("Der Mono-Stimulus ist leer.");
        else if (monoSource.Any(sample => !float.IsFinite(sample)))
            errors.Add("Der Mono-Stimulus enthält ungültige Samples.");
        else if (monoSource.All(sample => Math.Abs(sample) < float.Epsilon))
            errors.Add("Der Mono-Stimulus enthält kein Signal.");
        if (sourceSampleRate <= 0 || request.OutputSampleRate <= 0)
            errors.Add("Quell- und Zielabtastrate müssen positiv sein.");
        if (!Enum.IsDefined(request.Ear))
            errors.Add("Das Wiedergabeohr ist ungültig.");
        if (!Enum.IsDefined(request.Environment))
            errors.Add("Die Hörumgebung ist ungültig.");
        if (request.MaximumVolumeDb is > 0 or < MinimumDigitalAttenuationDb)
            errors.Add("Die digitale Pegelobergrenze muss zwischen -96 dB und 0 dB liegen.");
        if (request.DigitalAttenuationDb < MinimumDigitalAttenuationDb ||
            request.DigitalAttenuationDb > request.MaximumVolumeDb)
            errors.Add("Die digitale Absenkung überschreitet die gespeicherte Pegelobergrenze.");

        if (request.Environment == ListeningEnvironment.Quiet && request.SignalToNoiseRatioDb is not null)
            errors.Add("In Ruhe darf kein Signal-Rausch-Abstand gesetzt sein.");
        if (request.Environment == ListeningEnvironment.BackgroundNoise &&
            request.SignalToNoiseRatioDb is not (>= -20m and <= 30m))
            errors.Add("Mit Störgeräusch wird ein Signal-Rausch-Abstand zwischen -20 dB und +30 dB benötigt.");
        if (request.HeadphoneEqualization is { } equalization && request.OutputSampleRate > 0)
            errors.AddRange(HeadphoneEqualizer.Validate(equalization, request.OutputSampleRate));
        return errors;
    }

    public static RenderedStimulusAudio Render(
        IReadOnlyList<float> monoSource,
        int sourceSampleRate,
        StimulusRenderRequest request,
        CardinalSpeechShapedNoiseProfile? cardinalNoiseProfile = null)
    {
        var errors = Validate(monoSource, sourceSampleRate, request);
        if (errors.Count > 0)
            throw new ArgumentException(string.Join(" ", errors), nameof(request));

        var resampled = ResampleLinear(monoSource, sourceSampleRate, request.OutputSampleRate);
        NormalizePeak(resampled, DbToLinear(SourcePeakNormalizationDbfs));

        var paddingSeconds = cardinalNoiseProfile is null
            ? PaddingSeconds
            : (double)CardinalSpeechShapedNoiseProtocol.PreRollSeconds;
        var paddingFrames = (int)Math.Round(request.OutputSampleRate * paddingSeconds);
        var monoOutput = new double[checked(resampled.Length + (paddingFrames * 2))];
        var speechGain = DbToLinear(request.DigitalAttenuationDb);
        for (var index = 0; index < resampled.Length; index++)
            monoOutput[index + paddingFrames] = resampled[index] * speechGain;

        if (request.Environment == ListeningEnvironment.BackgroundNoise)
        {
            var noise = cardinalNoiseProfile is null
                ? CreateBandLimitedNoise(monoOutput.Length, request.OutputSampleRate, request.NoiseSeed)
                : CreateCardinalSpeechShapedNoise(
                    monoOutput.Length,
                    request.OutputSampleRate,
                    request.NoiseSeed,
                    cardinalNoiseProfile);
            var (activeStart, activeEnd) = cardinalNoiseProfile is null
                ? (0, resampled.Length)
                : FindActiveSpeechRegion(resampled, request.OutputSampleRate);
            var speechRmsBeforeAttenuation = RootMeanSquare(resampled, activeStart, activeEnd);
            var noiseRegionStart = paddingFrames + activeStart;
            var noiseRms = RootMeanSquare(noise, noiseRegionStart, paddingFrames + activeEnd);
            var targetNoiseRms = speechRmsBeforeAttenuation /
                DbToLinear(request.SignalToNoiseRatioDb!.Value);
            Scale(noise, targetNoiseRms / noiseRms * speechGain);
            for (var index = 0; index < monoOutput.Length; index++)
                monoOutput[index] += noise[index];
        }

        // Die Kopfhörerentzerrung formt Sprache und Rauschen gemeinsam nach der Mischung; der SNR bezieht sich
        // weiterhin auf die unentzerrten Anteile. Die Vorabsenkung verhindert jede Anhebung über den Digitalpegel.
        if (request.HeadphoneEqualization is { } equalization)
            HeadphoneEqualizer.Apply(equalization, monoOutput, request.OutputSampleRate);

        ApplyEdgeFade(monoOutput, request.OutputSampleRate, useCardinalProfileEnvelope: cardinalNoiseProfile is not null);
        var outputPeak = monoOutput.Max(sample => Math.Abs(sample));
        if (!double.IsFinite(outputPeak) || outputPeak > 1.0)
            throw new InvalidOperationException("Die Mischung würde den digitalen Vollpegel überschreiten und wurde blockiert.");

        var stereo = new float[checked(monoOutput.Length * 2)];
        var channelOffset = request.Ear == TestedEar.Left ? 0 : 1;
        for (var frame = 0; frame < monoOutput.Length; frame++)
            stereo[(frame * 2) + channelOffset] = (float)monoOutput[frame];

        return new RenderedStimulusAudio(
            request.OutputSampleRate,
            stereo,
            new StimulusRenderMetadata(
                RendererVersion,
                request.Environment == ListeningEnvironment.BackgroundNoise
                    ? cardinalNoiseProfile is null ? NoiseAlgorithm : CardinalNoiseAlgorithm
                    : "none",
                SourcePeakNormalizationDbfs,
                request.DigitalAttenuationDb,
                request.SignalToNoiseRatioDb,
                request.NoiseSeed,
                request.OutputSampleRate,
                (float)outputPeak,
                cardinalNoiseProfile?.MaterialId,
                cardinalNoiseProfile?.ProfileSha256,
                request.HeadphoneEqualization?.SourceSha256,
                request.HeadphoneEqualization is { } appliedEqualization
                    ? decimal.Round(
                        (decimal)HeadphoneEqualizer.GetEffectivePreampDb(appliedEqualization, request.OutputSampleRate),
                        2,
                        MidpointRounding.AwayFromZero)
                    : null));
    }

    /// <summary>
    /// Sprach-RMS in dBFS nach Resampling und Spitzennormalisierung, bei Kardinalzahlen im aktiven Bereich, sonst über
    /// den ganzen Stimulus – dieselbe Größe, auf die sich der SNR bezieht.
    /// </summary>
    public static decimal MeasureSpeechRmsDbfs(
        IReadOnlyList<float> monoSource,
        int sourceSampleRate,
        int outputSampleRate,
        bool useActiveSpeechRegion)
    {
        var resampled = ResampleLinear(monoSource, sourceSampleRate, outputSampleRate);
        NormalizePeak(resampled, DbToLinear(SourcePeakNormalizationDbfs));
        var (start, end) = useActiveSpeechRegion ? FindActiveSpeechRegion(resampled, outputSampleRate) : (0, resampled.Length);
        return LinearToDb(RootMeanSquare(resampled, start, end));
    }

    /// <summary>
    /// Erzeugt eine nahtlos wiederholbare Rauschschleife mit festem RMS-Pegel. Das Ende wird mit gleichbleibender Leistung
    /// in den Anfang übergeblendet; eine Kopfhörerentzerrung wird im eingeschwungenen Zustand angewendet.
    /// </summary>
    public static ContinuousNoiseBed RenderContinuousNoise(
        ContinuousNoiseRequest request,
        CardinalSpeechShapedNoiseProfile? cardinalNoiseProfile = null)
    {
        if (!Enum.IsDefined(request.Ear))
            throw new ArgumentException("Das Wiedergabeohr ist ungültig.", nameof(request));
        if (request.OutputSampleRate <= 0)
            throw new ArgumentException("Die Zielabtastrate muss positiv sein.", nameof(request));
        if (request.HeadphoneEqualization is { } checkedEqualization)
        {
            var equalizationErrors = HeadphoneEqualizer.Validate(checkedEqualization, request.OutputSampleRate);
            if (equalizationErrors.Count > 0)
                throw new ArgumentException(string.Join(" ", equalizationErrors), nameof(request));
        }

        var loopFrames = (int)Math.Round(request.OutputSampleRate * ContinuousNoiseProtocol.LoopSeconds);
        var crossfadeFrames = (int)Math.Round(request.OutputSampleRate * ContinuousNoiseProtocol.CrossfadeSeconds);
        var raw = cardinalNoiseProfile is null
            ? CreateBandLimitedNoise(loopFrames + crossfadeFrames, request.OutputSampleRate, request.NoiseSeed)
            : CreateCardinalSpeechShapedNoise(loopFrames + crossfadeFrames, request.OutputSampleRate, request.NoiseSeed, cardinalNoiseProfile);
        var loop = new double[loopFrames];
        for (var index = 0; index < loopFrames; index++)
        {
            if (index < crossfadeFrames)
            {
                // Gleichleistungsblende zwischen unkorrelierten Rauschabschnitten.
                var phase = Math.PI / 2 * index / crossfadeFrames;
                loop[index] = (raw[index] * Math.Sin(phase)) + (raw[loopFrames + index] * Math.Cos(phase));
            }
            else
            {
                loop[index] = raw[index];
            }
        }
        Scale(loop, DbToLinear(request.NoiseLevelDbfs) / RootMeanSquare(loop, 0, loop.Length));

        if (request.HeadphoneEqualization is { } equalization)
        {
            // Zwei Durchläufe: Der zweite beginnt mit dem Filterzustand am Schleifenende und bleibt dadurch nahtlos.
            var twice = new double[loopFrames * 2];
            loop.CopyTo(twice, 0);
            loop.CopyTo(twice, loopFrames);
            HeadphoneEqualizer.Apply(equalization, twice, request.OutputSampleRate);
            Array.Copy(twice, loopFrames, loop, 0, loopFrames);
        }

        var peak = loop.Max(sample => Math.Abs(sample));
        if (!double.IsFinite(peak) || peak > 1.0)
            throw new InvalidOperationException("Das Dauerrauschen würde den digitalen Vollpegel überschreiten und wurde blockiert.");

        var stereo = new float[checked(loopFrames * 2)];
        var channelOffset = request.Ear == TestedEar.Left ? 0 : 1;
        for (var frame = 0; frame < loopFrames; frame++)
            stereo[(frame * 2) + channelOffset] = (float)loop[frame];
        return new ContinuousNoiseBed(
            request.OutputSampleRate,
            stereo,
            cardinalNoiseProfile is null ? NoiseAlgorithm : CardinalNoiseAlgorithm,
            request.NoiseLevelDbfs,
            request.NoiseSeed,
            (float)peak);
    }

    /// <summary>
    /// Rendert nur die Sprache für die Mischung mit dem Dauerrauschen. Der digitale Sprachpegel ergibt sich aus Rauschpegel,
    /// SNR und dem Sprach-RMS dieses Stimulus; er darf die Pegelobergrenze nicht überschreiten.
    /// </summary>
    public static RenderedStimulusAudio RenderSpeechOverContinuousNoise(
        IReadOnlyList<float> monoSource,
        int sourceSampleRate,
        SpeechOverNoiseRequest request,
        CardinalSpeechShapedNoiseProfile? cardinalNoiseProfile = null)
    {
        var bed = request.Bed;
        if (monoSource is null || monoSource.Count == 0 || monoSource.All(sample => Math.Abs(sample) < float.Epsilon))
            throw new ArgumentException("Der Mono-Stimulus ist leer.", nameof(monoSource));
        if (request.SignalToNoiseRatioDb is not (>= -20m and <= 30m))
            throw new ArgumentException("Der Signal-Rausch-Abstand muss zwischen -20 dB und +30 dB liegen.", nameof(request));

        var resampled = ResampleLinear(monoSource, sourceSampleRate, bed.SampleRate);
        NormalizePeak(resampled, DbToLinear(SourcePeakNormalizationDbfs));
        var (activeStart, activeEnd) = cardinalNoiseProfile is null
            ? (0, resampled.Length)
            : FindActiveSpeechRegion(resampled, bed.SampleRate);
        var speechRmsDbfs = LinearToDb(RootMeanSquare(resampled, activeStart, activeEnd));
        var attenuationDb = decimal.Round(bed.NoiseLevelDbfs + request.SignalToNoiseRatioDb - speechRmsDbfs, 2, MidpointRounding.AwayFromZero);
        if (attenuationDb > request.MaximumVolumeDb || attenuationDb < MinimumDigitalAttenuationDb)
            throw new InvalidOperationException(
                $"Für {request.SignalToNoiseRatioDb:0.#} dB SNR wäre ein Sprachpegel von {attenuationDb:0.#} dB nötig; die Pegelobergrenze ist {request.MaximumVolumeDb:0.#} dB.");

        var paddingFrames = (int)Math.Round(bed.SampleRate * PaddingSeconds);
        var monoOutput = new double[checked(resampled.Length + (paddingFrames * 2))];
        var speechGain = DbToLinear(attenuationDb);
        for (var index = 0; index < resampled.Length; index++)
            monoOutput[index + paddingFrames] = resampled[index] * speechGain;
        if (request.HeadphoneEqualization is { } equalization)
            HeadphoneEqualizer.Apply(equalization, monoOutput, bed.SampleRate);
        ApplyEdgeFade(monoOutput, bed.SampleRate, useCardinalProfileEnvelope: false);

        var outputPeak = monoOutput.Max(sample => Math.Abs(sample));
        if (!double.IsFinite(outputPeak) || outputPeak + bed.Peak > 1.0)
            throw new InvalidOperationException("Sprache und Dauerrauschen würden zusammen den digitalen Vollpegel überschreiten; die Darbietung wurde blockiert.");

        var stereo = new float[checked(monoOutput.Length * 2)];
        var channelOffset = request.Ear == TestedEar.Left ? 0 : 1;
        for (var frame = 0; frame < monoOutput.Length; frame++)
            stereo[(frame * 2) + channelOffset] = (float)monoOutput[frame];

        return new RenderedStimulusAudio(
            bed.SampleRate,
            stereo,
            new StimulusRenderMetadata(
                RendererVersion,
                bed.NoiseAlgorithm,
                SourcePeakNormalizationDbfs,
                attenuationDb,
                request.SignalToNoiseRatioDb,
                bed.NoiseSeed,
                bed.SampleRate,
                (float)(outputPeak + bed.Peak),
                cardinalNoiseProfile?.MaterialId,
                cardinalNoiseProfile?.ProfileSha256,
                request.HeadphoneEqualization?.SourceSha256,
                request.HeadphoneEqualization is { } appliedEqualization
                    ? decimal.Round(
                        (decimal)HeadphoneEqualizer.GetEffectivePreampDb(appliedEqualization, bed.SampleRate),
                        2,
                        MidpointRounding.AwayFromZero)
                    : null,
                bed.NoiseLevelDbfs));
    }

    private static decimal LinearToDb(double value) =>
        decimal.Round((decimal)(20 * Math.Log10(value)), 2, MidpointRounding.AwayFromZero);

    private static double[] ResampleLinear(IReadOnlyList<float> source, int sourceRate, int outputRate)
    {
        if (sourceRate == outputRate)
            return source.Select(sample => (double)sample).ToArray();

        var outputLength = Math.Max(1, checked((int)Math.Round(source.Count * (double)outputRate / sourceRate)));
        var result = new double[outputLength];
        for (var outputIndex = 0; outputIndex < outputLength; outputIndex++)
        {
            var sourcePosition = outputIndex * (double)sourceRate / outputRate;
            var leftIndex = Math.Min((int)sourcePosition, source.Count - 1);
            var rightIndex = Math.Min(leftIndex + 1, source.Count - 1);
            var fraction = sourcePosition - leftIndex;
            result[outputIndex] = source[leftIndex] + ((source[rightIndex] - source[leftIndex]) * fraction);
        }
        return result;
    }

    private static void NormalizePeak(double[] samples, double targetPeak)
    {
        var peak = samples.Max(sample => Math.Abs(sample));
        var factor = targetPeak / peak;
        for (var index = 0; index < samples.Length; index++)
            samples[index] *= factor;
    }

    private static double[] CreateBandLimitedNoise(int length, int sampleRate, int seed)
    {
        var result = new double[length];
        var random = new XorShift32(seed);
        var lowPassFactor = 1 - Math.Exp(-2 * Math.PI * 4000 / sampleRate);
        var highPassRc = 1 / (2 * Math.PI * 120);
        var highPassFactor = highPassRc / (highPassRc + (1d / sampleRate));
        double lowPass = 0;
        double previousLowPass = 0;
        double highPass = 0;

        for (var index = 0; index < length; index++)
        {
            var white = random.NextLegacySignedUnit();
            lowPass += lowPassFactor * (white - lowPass);
            highPass = highPassFactor * (highPass + lowPass - previousLowPass);
            previousLowPass = lowPass;
            result[index] = highPass;
        }
        return result;
    }

    private static double[] CreateCardinalSpeechShapedNoise(
        int length,
        int sampleRate,
        int seed,
        CardinalSpeechShapedNoiseProfile profile)
    {
        if (!profile.FirCoefficientsBySampleRate.TryGetValue(sampleRate, out var coefficients))
            throw new InvalidOperationException($"Das Rauschprofil enthält keinen FIR-Filter für {sampleRate} Hz.");
        return ConvolveSame(CreateWhiteNoise(length, seed), coefficients);
    }

    private static double[] CreateWhiteNoise(int length, int seed)
    {
        var result = new double[length];
        var random = new XorShift32(seed);
        for (var index = 0; index < result.Length; index++)
            result[index] = random.NextProtocolSignedUnit();
        return result;
    }

    private static (int Start, int End) FindActiveSpeechRegion(double[] samples, int sampleRate)
    {
        var frameLength = Math.Max(1, (int)Math.Round(sampleRate * CardinalSpeechShapedNoiseProtocol.AnalysisFrameMilliseconds / 1000d));
        var hopLength = Math.Max(1, (int)Math.Round(sampleRate * CardinalSpeechShapedNoiseProtocol.AnalysisHopMilliseconds / 1000d));
        var frameCount = Math.Max(1, 1 + (int)Math.Ceiling((samples.Length - frameLength) / (double)hopLength));
        var frameRms = new double[frameCount];
        for (var frame = 0; frame < frameCount; frame++)
        {
            var start = frame * hopLength;
            var sumSquares = 0d;
            for (var offset = 0; offset < frameLength; offset++)
            {
                var value = start + offset < samples.Length ? samples[start + offset] : 0d;
                sumSquares += value * value;
            }
            frameRms[frame] = Math.Sqrt(sumSquares / frameLength);
        }

        var threshold = Math.Max(
            DbToLinear(CardinalSpeechShapedNoiseProtocol.ActiveFrameAbsoluteThresholdDbfs),
            frameRms.Max() * DbToLinear(CardinalSpeechShapedNoiseProtocol.ActiveFrameRelativeThresholdDb));
        var first = Array.FindIndex(frameRms, value => value >= threshold);
        var last = Array.FindLastIndex(frameRms, value => value >= threshold);
        if (first < 0 || last < first)
            throw new InvalidOperationException("Der Stimulus enthält kein gültiges aktives Sprachfenster.");
        return (first * hopLength, Math.Min(samples.Length, (last * hopLength) + frameLength));
    }

    private static double[] ConvolveSame(double[] samples, IReadOnlyList<double> impulse)
    {
        const int blockSize = 16384;
        var fftSize = 1;
        while (fftSize < blockSize + impulse.Count - 1)
            fftSize <<= 1;

        var response = new System.Numerics.Complex[fftSize];
        for (var index = 0; index < impulse.Count; index++)
            response[index] = impulse[index];
        FourierTransform(response, inverse: false);

        var full = new double[samples.Length + impulse.Count - 1];
        for (var start = 0; start < samples.Length; start += blockSize)
        {
            var block = new System.Numerics.Complex[fftSize];
            var count = Math.Min(blockSize, samples.Length - start);
            for (var index = 0; index < count; index++)
                block[index] = samples[start + index];
            FourierTransform(block, inverse: false);
            for (var index = 0; index < fftSize; index++)
                block[index] *= response[index];
            FourierTransform(block, inverse: true);
            var used = Math.Min(fftSize, full.Length - start);
            for (var index = 0; index < used; index++)
                full[start + index] += block[index].Real;
        }

        var delay = (impulse.Count - 1) / 2;
        return full.AsSpan(delay, samples.Length).ToArray();
    }

    internal static void FourierTransform(System.Numerics.Complex[] values, bool inverse)
    {
        for (int index = 1, reversed = 0; index < values.Length; index++)
        {
            var bit = values.Length >> 1;
            for (; (reversed & bit) != 0; bit >>= 1)
                reversed ^= bit;
            reversed ^= bit;
            if (index < reversed)
                (values[index], values[reversed]) = (values[reversed], values[index]);
        }

        for (var length = 2; length <= values.Length; length <<= 1)
        {
            var angle = (inverse ? 2 : -2) * Math.PI / length;
            var step = new System.Numerics.Complex(Math.Cos(angle), Math.Sin(angle));
            for (var start = 0; start < values.Length; start += length)
            {
                var factor = System.Numerics.Complex.One;
                for (var offset = 0; offset < length / 2; offset++)
                {
                    var even = values[start + offset];
                    var odd = values[start + offset + (length / 2)] * factor;
                    values[start + offset] = even + odd;
                    values[start + offset + (length / 2)] = even - odd;
                    factor *= step;
                }
            }
        }

        if (inverse)
        {
            for (var index = 0; index < values.Length; index++)
                values[index] /= values.Length;
        }
    }

    private static void Scale(double[] samples, double factor)
    {
        for (var index = 0; index < samples.Length; index++)
            samples[index] *= factor;
    }

    private static double RootMeanSquare(IEnumerable<double> samples)
    {
        var values = samples as double[] ?? samples.ToArray();
        return Math.Sqrt(values.Sum(sample => sample * sample) / values.Length);
    }

    private static double RootMeanSquare(double[] samples, int start, int end)
    {
        var sumSquares = 0d;
        for (var index = start; index < end; index++)
            sumSquares += samples[index] * samples[index];
        return Math.Sqrt(sumSquares / (end - start));
    }

    private static void ApplyEdgeFade(double[] samples, int sampleRate, bool useCardinalProfileEnvelope)
    {
        var fadeFrames = Math.Min(samples.Length / 2, (int)Math.Round(sampleRate * FadeSeconds));
        var denominator = useCardinalProfileEnvelope ? Math.Max(1, fadeFrames - 1) : fadeFrames;
        for (var index = 0; index < fadeFrames; index++)
        {
            var gain = 0.5 - (0.5 * Math.Cos(Math.PI * index / denominator));
            samples[index] *= gain;
            samples[^(index + 1)] *= gain;
        }
    }

    private static double DbToLinear(decimal decibels) => Math.Pow(10, (double)decibels / 20);

    private sealed class XorShift32(int seed)
    {
        private uint state = seed == 0 ? 0x9E3779B9u : unchecked((uint)seed);

        private uint NextUInt32()
        {
            state ^= state << 13;
            state ^= state >> 17;
            state ^= state << 5;
            return state;
        }

        public double NextLegacySignedUnit() => ((NextUInt32() / (double)uint.MaxValue) * 2) - 1;

        public double NextProtocolSignedUnit() => (NextUInt32() * (2d / 4294967296d)) - 1;
    }
}
