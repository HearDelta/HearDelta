using System.Numerics;

namespace HearDelta.Core;

/// <summary>
/// Vertäubung des Gegenohrs im Hörschwellentest (ab Protokoll v12): Während jedes Durchgangs läuft auf dem nicht
/// geprüften Ohr Schmalbandrauschen um die Prüffrequenz mit festem digitalem RMS-Pegel <see cref="LevelDbfs"/>.
/// <see cref="Seed"/> macht das Rauschen reproduzierbar.
/// </summary>
public sealed record ThresholdMasking(
    string Algorithm,
    decimal LevelDbfs,
    int LeadInMilliseconds,
    int FadeMilliseconds,
    int Seed);

public static class ThresholdMaskingProtocol
{
    /// <summary>
    /// Terzbreites Schmalbandrauschen (Bandgrenzen Mittenfrequenz · 2^±1/6) als periodische Schleife aus gleich starken
    /// Spektrallinien mit zufälliger Phase, normiert auf den RMS-Pegel.
    /// </summary>
    public const string Algorithm = "narrowband-third-octave-multisine-v1";

    public const double BandwidthOctaves = 1d / 3d;
    public const decimal MinimumLevelDbfs = -90m;
    public const decimal MaximumLevelDbfs = -20m;
    public const decimal DefaultLevelDbfs = -50m;

    /// <summary>Das Rauschen läuft so lange allein, bevor der erste Ton der Pegelrampe beginnt.</summary>
    public const int LeadInMilliseconds = 1_000;

    /// <summary>Ein- und Ausblendung des Rauschens am Anfang und Ende jedes Durchgangs.</summary>
    public const int FadeMilliseconds = 50;

    /// <summary>Mindestlänge der Rauschschleife; tatsächlich die nächste Zweierpotenz an Abtastwerten.</summary>
    public const int MinimumLoopMilliseconds = 2_000;

    public static ThresholdMasking Create(decimal levelDbfs, int seed) =>
        new(Algorithm, levelDbfs, LeadInMilliseconds, FadeMilliseconds, seed);

    public static IReadOnlyList<string> Validate(ThresholdMasking masking)
    {
        ArgumentNullException.ThrowIfNull(masking);
        var errors = new List<string>();
        if (!string.Equals(masking.Algorithm, Algorithm, StringComparison.Ordinal) ||
            masking.LeadInMilliseconds != LeadInMilliseconds ||
            masking.FadeMilliseconds != FadeMilliseconds)
            errors.Add("Die Vertäubung muss das festgelegte Schmalbandrauschen mit Vorlauf und Blenden verwenden.");
        if (masking.LevelDbfs < MinimumLevelDbfs || masking.LevelDbfs > MaximumLevelDbfs)
            errors.Add($"Der Vertäubungspegel muss zwischen {MinimumLevelDbfs:0} und {MaximumLevelDbfs:0} dBFS liegen.");
        return errors;
    }

    public static (double LowerHz, double UpperHz) GetBand(double centerFrequencyHz)
    {
        var factor = Math.Pow(2d, BandwidthOctaves / 2d);
        return (centerFrequencyHz / factor, centerFrequencyHz * factor);
    }

    /// <summary>
    /// Erzeugt eine nahtlos wiederholbare Rauschschleife mit RMS 1 (0 dBFS RMS) für die Prüffrequenz. Gleiche Frequenz,
    /// Abtastrate und Seed ergeben bitgleich dasselbe Rauschen.
    /// </summary>
    public static float[] CreateNoiseLoop(double centerFrequencyHz, int sampleRate, int seed)
    {
        if (sampleRate <= 0)
            throw new ArgumentOutOfRangeException(nameof(sampleRate));
        var (lowerHz, upperHz) = GetBand(centerFrequencyHz);
        if (lowerHz <= 0 || upperHz >= sampleRate / 2d)
            throw new ArgumentOutOfRangeException(nameof(centerFrequencyHz), "Das Vertäubungsband liegt außerhalb des ausgebbaren Bereichs.");

        var length = 1;
        while (length < (long)sampleRate * MinimumLoopMilliseconds / 1_000)
            length <<= 1;
        var spectrum = new Complex[length];
        var firstBin = Math.Max(1, (int)Math.Ceiling(lowerHz * length / sampleRate));
        var lastBin = Math.Min(length / 2 - 1, (int)Math.Floor(upperHz * length / sampleRate));
        if (lastBin < firstBin)
            throw new ArgumentOutOfRangeException(nameof(centerFrequencyHz), "Das Vertäubungsband enthält keine Spektrallinie.");

        var state = unchecked((uint)seed ^ 0x4D41534Bu ^ (uint)Math.Round(centerFrequencyHz * 10d));
        for (var bin = firstBin; bin <= lastBin; bin++)
        {
            state = Next(state);
            var phase = 2d * Math.PI * (state / 4_294_967_296d);
            spectrum[bin] = Complex.FromPolarCoordinates(1d, phase);
            spectrum[length - bin] = Complex.Conjugate(spectrum[bin]);
        }
        StimulusAudioRenderer.FourierTransform(spectrum, inverse: true);

        var sumOfSquares = 0d;
        for (var index = 0; index < length; index++)
            sumOfSquares += spectrum[index].Real * spectrum[index].Real;
        var scale = 1d / Math.Sqrt(sumOfSquares / length);
        var loop = new float[length];
        for (var index = 0; index < length; index++)
            loop[index] = (float)(spectrum[index].Real * scale);
        return loop;
    }

    private static uint Next(uint state)
    {
        state ^= state << 13;
        state ^= state >> 17;
        state ^= state << 5;
        return state == 0 ? 0x6D2B79F5u : state;
    }
}
