using System.Text.Json;
using HearDelta.Core;

namespace HearDelta.Core.Tests;

public sealed class HeadphoneEqualizationTests
{
    // AutoEq 7ae0f56, crinacle/GRAS 43AG-7 over-ear/Sennheiser HD 600.
    private const string Hd600Text = """
        Preamp: -6.3 dB
        Filter 1: ON LSC Fc 105 Hz Gain 6.3 dB Q 0.70
        Filter 2: ON PK Fc 10000 Hz Gain 4.4 dB Q 0.75
        Filter 3: ON PK Fc 157 Hz Gain -2.1 dB Q 0.77
        Filter 4: ON PK Fc 1298 Hz Gain -1.8 dB Q 1.90
        Filter 5: ON PK Fc 3326 Hz Gain -2.1 dB Q 3.51
        Filter 6: ON HSC Fc 10000 Hz Gain -3.5 dB Q 0.70
        Filter 7: ON PK Fc 4394 Hz Gain 1.4 dB Q 6.00
        Filter 8: ON PK Fc 57 Hz Gain 0.7 dB Q 4.58
        Filter 9: ON PK Fc 5637 Hz Gain -1.7 dB Q 5.98
        Filter 10: ON PK Fc 6413 Hz Gain 1.7 dB Q 5.98

        """;

    [Fact]
    public void AutoEqParametricFileIsParsedCompletely()
    {
        var equalization = CreateHd600();

        Assert.Equal(-6.3, equalization.PreampDb);
        Assert.Equal(10, equalization.Filters.Count);
        Assert.Equal(new ParametricEqFilter(ParametricEqFilterType.LowShelf, 105, 6.3, 0.7), equalization.Filters[0]);
        Assert.Equal(ParametricEqFilterType.HighShelf, equalization.Filters[5].Type);
        Assert.Equal(HeadphoneEqualizer.ComputeSha256(Hd600Text), equalization.SourceSha256);
        Assert.Empty(HeadphoneEqualizer.Validate(equalization, 48_000));
    }

    [Fact]
    public void DisabledFiltersAreSkippedAndInvalidLinesAreRejected()
    {
        var equalization = HeadphoneEqualizer.Parse(
            "Preamp: -1 dB\r\nFilter 1: OFF PK Fc 1000 Hz Gain 3 dB Q 1\r\nFilter 2: ON PK Fc 2000 Hz Gain 1 dB Q 1\r\n",
            "Test", "test", "Test", "Test");

        Assert.Single(equalization.Filters);
        Assert.Throws<FormatException>(() => HeadphoneEqualizer.Parse("Filter 1: ON PK Fc 1000 Hz Gain 3 dB Q 1", "T", "t", "T", "T"));
        Assert.Throws<FormatException>(() => HeadphoneEqualizer.Parse("Preamp: -3 dB\nFilter 1: ON XX Fc 1000 Hz Gain 3 dB Q 1", "T", "t", "T", "T"));
        Assert.Throws<FormatException>(() => HeadphoneEqualizer.Parse("Preamp: -3 dB\nFilter 1: ON PK Fc 1000 Hz Gain 3 dB Q 90", "T", "t", "T", "T"));
    }

    [Fact]
    public void TamperedSourceTextFailsValidation()
    {
        var tampered = CreateHd600() with { SourceText = Hd600Text.Replace("6.3 dB Q", "9.3 dB Q", StringComparison.Ordinal) };

        Assert.Contains(HeadphoneEqualizer.Validate(tampered, 48_000), error => error.Contains("Prüfsumme"));
    }

    [Theory]
    [InlineData(ParametricEqFilterType.Peaking, 1000, 1000, 6)]
    [InlineData(ParametricEqFilterType.LowShelf, 200, 20, 6)]
    [InlineData(ParametricEqFilterType.HighShelf, 2000, 20000, 6)]
    [InlineData(ParametricEqFilterType.Peaking, 1000, 50, 0)]
    public void FilterResponseMatchesCookbookDefinition(
        ParametricEqFilterType type,
        double cornerHz,
        double probeHz,
        double expectedDb)
    {
        var equalization = new HeadphoneEqualization(
            "Test", "test", "Test", "Test", "", "", 0, [new ParametricEqFilter(type, cornerHz, 6, 0.7)]);

        Assert.Equal(expectedDb, HeadphoneEqualizer.GetFilterResponseDb(equalization, probeHz, 96_000), 0.1);
    }

    [Theory]
    [InlineData(44_100)]
    [InlineData(48_000)]
    public void EffectivePreampPreventsAnyBoost(int sampleRate)
    {
        var equalization = CreateHd600();
        var preamp = HeadphoneEqualizer.GetEffectivePreampDb(equalization, sampleRate);

        Assert.True(preamp <= equalization.PreampDb);
        foreach (var frequency in HearingThresholdToneCatalog.Create(ThresholdToneOrder.Ascending, 1).Select(tone => tone.FrequencyHz))
            Assert.True(HeadphoneEqualizer.GetToneCorrectionDb(equalization, frequency, sampleRate) <= 0);
        for (var frequency = 20d; frequency < 19_800; frequency *= 1.01)
            Assert.True(preamp + HeadphoneEqualizer.GetFilterResponseDb(equalization, frequency, sampleRate) <= 1e-9);
    }

    [Theory]
    [InlineData(62.5)]
    [InlineData(500)]
    [InlineData(4000)]
    [InlineData(10000)]
    public void ToneCorrectionEqualsSteadyStateOfFilteredSine(double frequencyHz)
    {
        const int sampleRate = 48_000;
        var equalization = CreateHd600();
        var samples = Enumerable.Range(0, sampleRate * 2)
            .Select(index => Math.Sin(2 * Math.PI * frequencyHz * index / sampleRate))
            .ToArray();

        HeadphoneEqualizer.Apply(equalization, samples, sampleRate);

        var steadyState = samples.AsSpan(sampleRate).ToArray();
        var measuredDb = 20 * Math.Log10(Math.Sqrt(steadyState.Average(value => value * value)) * Math.Sqrt(2));
        Assert.Equal(HeadphoneEqualizer.GetToneCorrectionDb(equalization, frequencyHz, sampleRate), measuredDb, 0.02);
    }

    [Fact]
    public void DifferentEqualizationMakesSnapshotsIncomparable()
    {
        var plain = CreateProfile(null).CreateSnapshot();
        var equalized = CreateProfile(CreateHd600()).CreateSnapshot();

        Assert.Contains(
            MeasurementProfileRules.GetComparisonDifferences(plain, equalized),
            difference => difference.Contains("Kopfhörerentzerrung"));
        Assert.True(MeasurementProfileRules.IsDirectlyComparable(equalized, CreateProfile(CreateHd600()).CreateSnapshot()));
    }

    [Fact]
    public void SnapshotJsonRoundTripKeepsEqualizationAndOldPayloadsStayReadable()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var snapshot = CreateProfile(CreateHd600()).CreateSnapshot();

        var json = JsonSerializer.Serialize(snapshot, options);
        var restored = JsonSerializer.Deserialize<MeasurementHardwareSnapshot>(json, options)!;
        var withoutEqualization = JsonSerializer.Deserialize<MeasurementHardwareSnapshot>(
            JsonSerializer.Serialize(snapshot with { HeadphoneEqualization = null }, options).Replace(",\"headphoneEqualization\":null", "", StringComparison.Ordinal),
            options)!;

        Assert.Contains("\"lowShelf\"", json, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(snapshot.HeadphoneEqualization!.SourceSha256, restored.HeadphoneEqualization!.SourceSha256);
        Assert.Equal(snapshot.HeadphoneEqualization.Filters, restored.HeadphoneEqualization.Filters);
        Assert.Empty(HeadphoneEqualizer.Validate(restored.HeadphoneEqualization, 48_000));
        Assert.Null(withoutEqualization.HeadphoneEqualization);
    }

    [Fact]
    public void RendererAppliesEqualizationAndDocumentsIt()
    {
        var source = Enumerable.Range(0, 22050 / 4)
            .Select(index => (float)(Math.Sin(2 * Math.PI * 700 * index / 22050d) * 0.2))
            .ToArray();
        var request = new StimulusRenderRequest(TestedEar.Left, ListeningEnvironment.Quiet, -40m, -30m, null, 42, 48_000);

        var plain = StimulusAudioRenderer.Render(source, 22050, request);
        var equalized = StimulusAudioRenderer.Render(source, 22050, request with { HeadphoneEqualization = CreateHd600() });

        Assert.Null(plain.Metadata.HeadphoneEqualizationSha256);
        Assert.Equal(CreateHd600().SourceSha256, equalized.Metadata.HeadphoneEqualizationSha256);
        Assert.True(equalized.Metadata.HeadphoneEqualizationPreampDb <= -6.3m);
        Assert.True(equalized.Metadata.OutputPeak < plain.Metadata.OutputPeak);
        Assert.Equal(0f, equalized.InterleavedStereoSamples[0]);
        Assert.Equal(0f, equalized.InterleavedStereoSamples[^2]);
    }

    private static HeadphoneEqualization CreateHd600() => HeadphoneEqualizer.Parse(
        Hd600Text,
        "AutoEq 7ae0f56",
        "crinacle/GRAS 43AG-7 over-ear/Sennheiser HD 600",
        "Sennheiser HD 600",
        "Harman");

    private static MeasurementProfile CreateProfile(HeadphoneEqualization? equalization) => new(
        Guid.Parse("7f0c3c8e-2a55-4d0f-9d1a-0a6f7f1b9e11"),
        "Test",
        "endpoint",
        "Ausgang",
        true,
        48000,
        24,
        2,
        new HeadphoneProfile(Guid.Parse("a5d6e0f2-4b1c-4e8f-8c37-3b2f1c0d9e22"), "Sennheiser", "HD 600", "Offen", 300, equalization),
        "Klinke",
        "Low",
        -60m,
        -30m,
        false,
        DateTimeOffset.Parse("2026-09-30T12:00:00Z"));
}
