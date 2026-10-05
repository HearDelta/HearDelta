using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace HearDelta.Core;

[JsonConverter(typeof(JsonStringEnumConverter<ParametricEqFilterType>))]
public enum ParametricEqFilterType
{
    Peaking,
    LowShelf,
    HighShelf
}

public sealed record ParametricEqFilter(
    ParametricEqFilterType Type,
    double FrequencyHz,
    double GainDb,
    double Q);

/// <summary>
/// Kopfhörerentzerrung als Teil des Hardware-Snapshots. <see cref="SourceText"/> ist die unveränderte AutoEq-Datei
/// (<c>ParametricEQ.txt</c>); <see cref="SourceSha256"/> identifiziert die Entzerrung beim Vergleich.
/// </summary>
public sealed record HeadphoneEqualization(
    string Source,
    string SourceId,
    string Label,
    string Target,
    string SourceText,
    string SourceSha256,
    double PreampDb,
    IReadOnlyList<ParametricEqFilter> Filters);

/// <summary>
/// Parametrischer Equalizer im Format von AutoEq/Equalizer APO. Die Biquad-Koeffizienten folgen dem
/// Audio-EQ-Cookbook (RBJ) mit Güte Q, wie AutoEq sie bei der Optimierung verwendet.
/// </summary>
public static partial class HeadphoneEqualizer
{
    public const int MaximumFilterCount = 20;
    private const double MinimumFrequencyHz = 10;
    private const double MaximumAbsoluteGainDb = 30;
    private const double MinimumQ = 0.05;
    private const double MaximumQ = 20;
    private const double ResponseGridStepsPerOctave = 96;

    [GeneratedRegex(@"^Preamp:\s*(-?\d+(?:\.\d+)?)\s*dB$", RegexOptions.CultureInvariant)]
    private static partial Regex PreampLine();

    [GeneratedRegex(
        @"^Filter\s+\d+:\s+(ON|OFF)\s+(PK|LSC|HSC)\s+Fc\s+(\d+(?:\.\d+)?)\s*Hz\s+Gain\s+(-?\d+(?:\.\d+)?)\s*dB\s+Q\s+(\d+(?:\.\d+)?)$",
        RegexOptions.CultureInvariant)]
    private static partial Regex FilterLine();

    /// <summary>Liest eine AutoEq-Datei <c>ParametricEQ.txt</c>. Abgeschaltete Filter (OFF) werden übergangen.</summary>
    public static HeadphoneEqualization Parse(
        string text,
        string source,
        string sourceId,
        string label,
        string target)
    {
        ArgumentNullException.ThrowIfNull(text);
        double? preamp = null;
        var filters = new List<ParametricEqFilter>();
        var lineNumber = 0;
        foreach (var rawLine in text.Split('\n'))
        {
            lineNumber++;
            var line = rawLine.Trim();
            if (line.Length == 0)
                continue;
            if (PreampLine().Match(line) is { Success: true } preampMatch)
            {
                if (preamp is not null)
                    throw new FormatException(string.Format(CoreStrings.Equalization_DuplicatePreamp, lineNumber));
                preamp = ParseNumber(preampMatch.Groups[1].Value);
                continue;
            }
            if (FilterLine().Match(line) is not { Success: true } filterMatch)
                throw new FormatException(string.Format(CoreStrings.Equalization_InvalidLine, lineNumber, line));
            if (filterMatch.Groups[1].Value == "OFF")
                continue;
            filters.Add(new ParametricEqFilter(
                filterMatch.Groups[2].Value switch
                {
                    "PK" => ParametricEqFilterType.Peaking,
                    "LSC" => ParametricEqFilterType.LowShelf,
                    _ => ParametricEqFilterType.HighShelf
                },
                ParseNumber(filterMatch.Groups[3].Value),
                ParseNumber(filterMatch.Groups[4].Value),
                ParseNumber(filterMatch.Groups[5].Value)));
        }

        if (preamp is null)
            throw new FormatException(CoreStrings.Equalization_PreampMissing);
        var equalization = new HeadphoneEqualization(
            source,
            sourceId,
            label,
            target,
            text,
            ComputeSha256(text),
            preamp.Value,
            filters);
        var errors = Validate(equalization, sampleRate: null);
        if (errors.Count > 0)
            throw new FormatException(string.Join(" ", errors));
        return equalization;
    }

    public static string ComputeSha256(string text) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));

    /// <summary>Prüft Filterbereiche, Prüfsumme und optional die Darstellbarkeit bei der Ausgabeabtastrate.</summary>
    public static IReadOnlyList<string> Validate(HeadphoneEqualization equalization, int? sampleRate)
    {
        ArgumentNullException.ThrowIfNull(equalization);
        var errors = new List<string>();
        if (!string.Equals(ComputeSha256(equalization.SourceText), equalization.SourceSha256, StringComparison.Ordinal))
            errors.Add(CoreStrings.Equalization_Checksum);
        if (equalization.Filters.Count is 0 or > MaximumFilterCount)
            errors.Add(string.Format(CoreStrings.Equalization_FilterCount, MaximumFilterCount));
        if (!double.IsFinite(equalization.PreampDb) || Math.Abs(equalization.PreampDb) > MaximumAbsoluteGainDb)
            errors.Add(CoreStrings.Equalization_PreampRange);
        var nyquistLimit = sampleRate is { } rate ? rate * 0.49 : 20_000;
        foreach (var filter in equalization.Filters)
        {
            if (!Enum.IsDefined(filter.Type))
                errors.Add(CoreStrings.Equalization_FilterType);
            if (!double.IsFinite(filter.FrequencyHz) || filter.FrequencyHz < MinimumFrequencyHz || filter.FrequencyHz > nyquistLimit)
                errors.Add(string.Format(CoreStrings.Equalization_FilterFrequency, filter.FrequencyHz.ToString(CultureInfo.InvariantCulture)));
            if (!double.IsFinite(filter.GainDb) || Math.Abs(filter.GainDb) > MaximumAbsoluteGainDb)
                errors.Add(CoreStrings.Equalization_FilterGain);
            if (!double.IsFinite(filter.Q) || filter.Q < MinimumQ || filter.Q > MaximumQ)
                errors.Add(CoreStrings.Equalization_FilterQ);
        }
        return errors.Distinct(StringComparer.Ordinal).ToArray();
    }

    /// <summary>Betragsfrequenzgang der Filterkette ohne Preamp in dB bei der gegebenen Abtastrate.</summary>
    public static double GetFilterResponseDb(HeadphoneEqualization equalization, double frequencyHz, int sampleRate)
    {
        ArgumentNullException.ThrowIfNull(equalization);
        var omega = 2 * Math.PI * frequencyHz / sampleRate;
        var z1 = System.Numerics.Complex.FromPolarCoordinates(1, -omega);
        var z2 = z1 * z1;
        var total = 0d;
        foreach (var filter in equalization.Filters)
        {
            var c = GetCoefficients(filter, sampleRate);
            var numerator = c.B0 + (c.B1 * z1) + (c.B2 * z2);
            var denominator = 1 + (c.A1 * z1) + (c.A2 * z2);
            total += 20 * Math.Log10((numerator / denominator).Magnitude);
        }
        return total;
    }

    /// <summary>
    /// Tatsächlich angewendete Vorabsenkung: die AutoEq-Angabe, höchstens aber so hoch, dass die Filterkette
    /// zwischen 20 Hz und der Hörgrenze beziehungsweise 0,45 × Abtastrate nirgends über 0 dB verstärkt.
    /// </summary>
    public static double GetEffectivePreampDb(HeadphoneEqualization equalization, int sampleRate)
    {
        ArgumentNullException.ThrowIfNull(equalization);
        var upper = Math.Min(20_000d, sampleRate * 0.45);
        var maximum = double.NegativeInfinity;
        var steps = (int)Math.Ceiling(Math.Log2(upper / 20d) * ResponseGridStepsPerOctave);
        for (var step = 0; step <= steps; step++)
            maximum = Math.Max(maximum, GetFilterResponseDb(equalization, Math.Min(upper, 20d * Math.Pow(2, step / ResponseGridStepsPerOctave)), sampleRate));
        foreach (var filter in equalization.Filters.Where(filter => filter.FrequencyHz <= upper))
            maximum = Math.Max(maximum, GetFilterResponseDb(equalization, filter.FrequencyHz, sampleRate));
        return Math.Min(equalization.PreampDb, -Math.Max(0, maximum));
    }

    /// <summary>
    /// Gesamtkorrektur eines stationären Sinustons: Vorabsenkung plus Filterverstärkung bei der Tonfrequenz.
    /// Entspricht exakt der Wirkung von <see cref="Apply"/> auf den eingeschwungenen Ton und ist nie positiv.
    /// </summary>
    public static double GetToneCorrectionDb(HeadphoneEqualization equalization, double frequencyHz, int sampleRate) =>
        Math.Min(0, GetEffectivePreampDb(equalization, sampleRate) + GetFilterResponseDb(equalization, frequencyHz, sampleRate));

    /// <summary>Wendet Vorabsenkung und Filterkette in doppelter Genauigkeit auf ein Monosignal an.</summary>
    public static void Apply(HeadphoneEqualization equalization, double[] samples, int sampleRate)
    {
        ArgumentNullException.ThrowIfNull(equalization);
        ArgumentNullException.ThrowIfNull(samples);
        var preampGain = Math.Pow(10, GetEffectivePreampDb(equalization, sampleRate) / 20);
        for (var index = 0; index < samples.Length; index++)
            samples[index] *= preampGain;

        foreach (var filter in equalization.Filters)
        {
            var c = GetCoefficients(filter, sampleRate);
            double state1 = 0, state2 = 0;
            for (var index = 0; index < samples.Length; index++)
            {
                // Transponierte Direktform II.
                var input = samples[index];
                var output = (c.B0 * input) + state1;
                state1 = (c.B1 * input) - (c.A1 * output) + state2;
                state2 = (c.B2 * input) - (c.A2 * output);
                samples[index] = output;
            }
        }
    }

    internal static (double B0, double B1, double B2, double A1, double A2) GetCoefficients(
        ParametricEqFilter filter,
        int sampleRate)
    {
        var a = Math.Pow(10, filter.GainDb / 40);
        var omega = 2 * Math.PI * filter.FrequencyHz / sampleRate;
        var cos = Math.Cos(omega);
        var alpha = Math.Sin(omega) / (2 * filter.Q);
        var sqrtAAlpha = 2 * Math.Sqrt(a) * alpha;
        double b0, b1, b2, a0, a1, a2;
        switch (filter.Type)
        {
            case ParametricEqFilterType.Peaking:
                b0 = 1 + (alpha * a);
                b1 = -2 * cos;
                b2 = 1 - (alpha * a);
                a0 = 1 + (alpha / a);
                a1 = -2 * cos;
                a2 = 1 - (alpha / a);
                break;
            case ParametricEqFilterType.LowShelf:
                b0 = a * ((a + 1) - ((a - 1) * cos) + sqrtAAlpha);
                b1 = 2 * a * ((a - 1) - ((a + 1) * cos));
                b2 = a * ((a + 1) - ((a - 1) * cos) - sqrtAAlpha);
                a0 = (a + 1) + ((a - 1) * cos) + sqrtAAlpha;
                a1 = -2 * ((a - 1) + ((a + 1) * cos));
                a2 = (a + 1) + ((a - 1) * cos) - sqrtAAlpha;
                break;
            case ParametricEqFilterType.HighShelf:
                b0 = a * ((a + 1) + ((a - 1) * cos) + sqrtAAlpha);
                b1 = -2 * a * ((a - 1) + ((a + 1) * cos));
                b2 = a * ((a + 1) + ((a - 1) * cos) - sqrtAAlpha);
                a0 = (a + 1) - ((a - 1) * cos) + sqrtAAlpha;
                a1 = 2 * ((a - 1) - ((a + 1) * cos));
                a2 = (a + 1) - ((a - 1) * cos) - sqrtAAlpha;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(filter), "Unbekannter Filtertyp.");
        }
        return (b0 / a0, b1 / a0, b2 / a0, a1 / a0, a2 / a0);
    }

    private static double ParseNumber(string value) => double.Parse(value, NumberStyles.Float, CultureInfo.InvariantCulture);
}
