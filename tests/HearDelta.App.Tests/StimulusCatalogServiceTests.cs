using System.IO;
using NAudio.Wave;
using HearDelta.App.Services;
using HearDelta.Core;

namespace HearDelta.App.Tests;

public sealed class StimulusCatalogServiceTests
{
    [Theory]
    [InlineData("personal-cardinal-numbers-christoph-v1", "Deutsch Männlich")]
    [InlineData("personal-cardinal-numbers-katja-v1", "Deutsch Weiblich")]
    public void BundledCardinalNumberPackLoadsWithAllValues(string directoryName, string voice)
    {
        var root = Path.Combine(AppContext.BaseDirectory, "stimuli", "de-DE", directoryName);

        var pack = new StimulusCatalogService().Load(root);

        Assert.Equal(CardinalNumberProtocol.Paradigm, pack.Catalog.Paradigm);
        Assert.Equal(SpeechMaterial.Numbers, Assert.Single(pack.Catalog.Lists.Select(list => list.Material).Distinct()));
        Assert.Equal(36, pack.Catalog.Lists.Count);
        Assert.Equal(900, pack.Catalog.Lists.SelectMany(list => list.Items).Count());
        Assert.Equal(voice, pack.AudioIndex.Generator.Voice);
        Assert.Equal(900, pack.AudioIndex.Entries.Count);
        Assert.NotNull(pack.CardinalNoiseProfile);
        Assert.Equal(pack.MaterialIdentity.CatalogId, pack.CardinalNoiseProfile.MaterialId);
        Assert.Equal(pack.MaterialIdentity.CatalogSha256, pack.CardinalNoiseProfile.CatalogSha256);
        Assert.Equal(pack.MaterialIdentity.AudioIndexSha256, pack.CardinalNoiseProfile.AudioIndexSha256);
        Assert.Equal([44100, 48000], pack.CardinalNoiseProfile.FirCoefficientsBySampleRate.Keys.Order());
    }

    [Fact]
    public void CardinalNoiseProfileCannotBeReusedForTheOtherVoiceMaterial()
    {
        var catalogService = new StimulusCatalogService();
        var christoph = catalogService.Load(Path.Combine(
            AppContext.BaseDirectory, "stimuli", "de-DE", "personal-cardinal-numbers-christoph-v1"));
        var katjaProfilePath = Path.Combine(
            AppContext.BaseDirectory,
            "stimuli",
            "de-DE",
            "cardinal-speech-shaped-noise-v1",
            "de-DE-personal-cardinal-numbers-katja-v1",
            "profile.json");

        var exception = Assert.Throws<InvalidDataException>(() =>
            CardinalSpeechShapedNoiseProfileLoader.Load(katjaProfilePath, christoph.MaterialIdentity));

        Assert.Contains("Materialidentität", exception.Message);
    }

    [Fact]
    public void BundledCardinalNoiseRendersOfflineWithoutOpeningAudioDevice()
    {
        var pack = new StimulusCatalogService().Load(Path.Combine(
            AppContext.BaseDirectory, "stimuli", "de-DE", "personal-cardinal-numbers-christoph-v1"));
        var request = new StimulusRenderRequest(
            TestedEar.Right,
            ListeningEnvironment.BackgroundNoise,
            -60m,
            -30m,
            5m,
            20260919,
            48000);

        var rendered = new StimulusRenderService().Render(pack, "cardinal-123", request);

        Assert.Equal(StimulusAudioRenderer.CardinalNoiseAlgorithm, rendered.Metadata.NoiseAlgorithm);
        Assert.Equal(pack.Catalog.Id, rendered.Metadata.NoiseProfileMaterialId);
        Assert.Equal(pack.CardinalNoiseProfile!.ProfileSha256, rendered.Metadata.NoiseProfileSha256);
        Assert.InRange(rendered.Metadata.OutputPeak, 0, 1);
        Assert.All(
            Enumerable.Range(0, rendered.InterleavedStereoSamples.Length / 2),
            frame => Assert.Equal(0, rendered.InterleavedStereoSamples[frame * 2]));
    }

    [Fact]
    public void CardinalRenderingRemovesLongTrailingSilenceBeforeAnswerRelease()
    {
        var pack = new StimulusCatalogService().Load(Path.Combine(
            AppContext.BaseDirectory, "stimuli", "de-DE", "personal-cardinal-numbers-christoph-v1"));
        var stimulusId = "cardinal-123";
        using var source = new WaveFileReader(pack.GetAudioPath(stimulusId));
        var sourceDuration = source.TotalTime.TotalSeconds;
        var request = new StimulusRenderRequest(
            TestedEar.Left,
            ListeningEnvironment.Quiet,
            -60m,
            -30m,
            null,
            1,
            48000);

        var rendered = new StimulusRenderService().Render(pack, stimulusId, request);
        var renderedDuration = rendered.InterleavedStereoSamples.Length / 2d / rendered.SampleRate;

        Assert.True(
            renderedDuration < sourceDuration - 0.5d,
            $"Die gerenderte Zahl dauert noch {renderedDuration:0.###} s bei {sourceDuration:0.###} s Quelldauer.");
    }

    [Fact]
    public void MissingCardinalNoiseProfileBlocksInsteadOfFallingBackToWhiteNoise()
    {
        var loaded = new StimulusCatalogService().Load(Path.Combine(
            AppContext.BaseDirectory, "stimuli", "de-DE", "personal-cardinal-numbers-christoph-v1"));
        var stimulusId = "cardinal-123";
        var withoutProfile = new LoadedStimulusPack(
            loaded.Catalog,
            loaded.AudioIndex,
            loaded.MaterialIdentity,
            loaded.RootDirectory,
            new Dictionary<string, string> { [stimulusId] = loaded.GetAudioPath(stimulusId) },
            cardinalNoiseProfile: null);
        var request = new StimulusRenderRequest(
            TestedEar.Left,
            ListeningEnvironment.BackgroundNoise,
            -60m,
            -30m,
            5m,
            1,
            48000);

        var exception = Assert.Throws<InvalidOperationException>(() =>
            new StimulusRenderService().Render(withoutProfile, stimulusId, request));

        Assert.Contains("Rauschprofil fehlt", exception.Message);
    }

    [Fact]
    public void BundledPackPassesCatalogHashFileHashAndWaveFormatChecks()
    {
        var pack = new StimulusCatalogService().Load(StimulusCatalogService.GetBundledPackDirectory());

        Assert.Equal(2, pack.Catalog.SchemaVersion);
        Assert.Equal("de-DE-personal-phoneme-contrast-v1", pack.Catalog.Id);
        Assert.Equal("1.0.0", pack.Catalog.Version);
        Assert.False(pack.Catalog.ClinicallyValidated);
        Assert.Equal("closed-set-phoneme-contrast", pack.Catalog.Paradigm);
        Assert.Equal(5, pack.Catalog.ResponseAlternativeCount);
        Assert.Equal(20m, pack.Catalog.ChanceLevelPercent);
        Assert.Equal(5, pack.Catalog.Lists.Count);
        Assert.All(pack.Catalog.Lists, list =>
        {
            Assert.Equal(SpeechMaterial.PhonemeContrasts, list.Material);
            Assert.Equal(25, list.Items.Count);
            Assert.Equal(5, list.Items.Count(item => item.TargetAlternativeIndex == 1));
            Assert.Equal(5, list.Items.Count(item => item.TargetAlternativeIndex == 2));
            Assert.Equal(5, list.Items.Count(item => item.TargetAlternativeIndex == 3));
            Assert.Equal(5, list.Items.Count(item => item.TargetAlternativeIndex == 4));
            Assert.Equal(5, list.Items.Count(item => item.TargetAlternativeIndex == 5));
        });
        Assert.Equal(125, pack.Catalog.Lists.Sum(list => list.Items.Count));
        Assert.Equal(25, pack.Catalog.Lists.SelectMany(list => list.Items)
            .Select(item => item.ContrastGroupId).Distinct().Count());
        Assert.Equal(125, pack.AudioIndex.Entries.Count);
        Assert.Equal("Azure AI Speech", pack.AudioIndex.Generator.Name);
        Assert.Equal("service-managed-current-2026-09-03", pack.AudioIndex.Generator.ModelRevision);
        Assert.Equal("ralf", pack.AudioIndex.Generator.Voice);
        Assert.True(pack.AudioIndex.Generator.AiGeneratedVoice);
        Assert.False(string.IsNullOrWhiteSpace(pack.AudioIndex.Generator.AiGeneratedVoiceDisclosure));
        Assert.Equal(pack.Catalog.Id, pack.MaterialIdentity.CatalogId);
        Assert.Equal(pack.Catalog.Version, pack.MaterialIdentity.CatalogVersion);
        Assert.Matches("^[0-9a-f]{64}$", pack.MaterialIdentity.FingerprintSha256);
    }

    [Fact]
    public void BundledLegacyPiperPackRemainsValidAndSeparate()
    {
        var legacyDirectory = Path.Combine(
            AppContext.BaseDirectory,
            "stimuli",
            "de-DE",
            "personal-relative-v1");

        var pack = new StimulusCatalogService().Load(legacyDirectory);

        Assert.Equal(1, pack.Catalog.SchemaVersion);
        Assert.Equal("de-DE-personal-relative-v1", pack.Catalog.Id);
        Assert.Equal(240, pack.AudioIndex.Entries.Count);
        Assert.Equal("Piper", pack.AudioIndex.Generator.Name);
    }

    [Fact]
    public void ChangedWaveFileIsRejectedBySha256()
    {
        var sourceDirectory = StimulusCatalogService.GetBundledPackDirectory();
        var testDirectory = Path.Combine(Path.GetTempPath(), $"heardelta-stimuli-{Guid.NewGuid():N}");

        try
        {
            CopyDirectory(sourceDirectory, testDirectory);
            var changedPath = Path.Combine(testDirectory, "audio", "initial-01-1.wav");
            using (var stream = new FileStream(changedPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            {
                stream.Position = stream.Length - 1;
                var original = stream.ReadByte();
                stream.Position = stream.Length - 1;
                stream.WriteByte((byte)(original ^ 0x01));
            }

            var exception = Assert.Throws<InvalidDataException>(() =>
                new StimulusCatalogService().Load(testDirectory));
            Assert.Contains("SHA-256", exception.Message);
        }
        finally
        {
            if (Directory.Exists(testDirectory))
                Directory.Delete(testDirectory, recursive: true);
        }
    }

    [Fact]
    public void BundledSpeechAndNoiseRenderDeterministicallyWithoutOpeningAudioDevice()
    {
        var pack = new StimulusCatalogService().Load(StimulusCatalogService.GetBundledPackDirectory());
        var request = new StimulusRenderRequest(
            TestedEar.Left,
            ListeningEnvironment.BackgroundNoise,
            -40m,
            -30m,
            5m,
            20260901,
            48000);
        var service = new StimulusRenderService();

        var first = service.Render(pack, "initial-01-1", request);
        var second = service.Render(pack, "initial-01-1", request);

        Assert.Equal(first.InterleavedStereoSamples, second.InterleavedStereoSamples);
        Assert.Equal(48000, first.SampleRate);
        Assert.Equal(StimulusAudioRenderer.NoiseAlgorithm, first.Metadata.NoiseAlgorithm);
        Assert.All(
            Enumerable.Range(0, first.InterleavedStereoSamples.Length / 2),
            frame => Assert.Equal(0, first.InterleavedStereoSamples[(frame * 2) + 1]));
    }

    [Fact]
    public void ReportedFourthStimulusRendersAtSavedSessionFormatWithoutOpeningAudioDevice()
    {
        var pack = new StimulusCatalogService().Load(StimulusCatalogService.GetBundledPackDirectory());
        var request = new StimulusRenderRequest(
            TestedEar.Left,
            ListeningEnvironment.Quiet,
            -60m,
            -30m,
            null,
            unchecked((480409328 * 397) ^ (1 * 101) ^ 4),
            44100);

        var rendered = new StimulusRenderService().Render(pack, "vowel-07-4", request);

        Assert.Equal(44100, rendered.SampleRate);
        Assert.Equal("none", rendered.Metadata.NoiseAlgorithm);
        Assert.NotEmpty(rendered.InterleavedStereoSamples);
    }

    [Fact]
    public void AllBundledStimuliRenderAtSavedSessionFormatWithoutOpeningAudioDevice()
    {
        var pack = new StimulusCatalogService().Load(StimulusCatalogService.GetBundledPackDirectory());
        var request = new StimulusRenderRequest(
            TestedEar.Left,
            ListeningEnvironment.Quiet,
            -60m,
            -30m,
            null,
            1,
            44100);
        var service = new StimulusRenderService();

        foreach (var stimulusId in pack.Catalog.Lists.SelectMany(list => list.Items).Select(item => item.Id))
        {
            var rendered = service.Render(pack, stimulusId, request);
            Assert.Equal(44100, rendered.SampleRate);
            Assert.NotEmpty(rendered.InterleavedStereoSamples);
        }
    }

    [Fact]
    public async Task MissingStoredEndpointBlocksPlaybackWithoutFallback()
    {
        var pack = new StimulusCatalogService().Load(StimulusCatalogService.GetBundledPackDirectory());
        var hardware = new MeasurementHardwareSnapshot(
            Guid.NewGuid(),
            "Nicht vorhandener Testendpunkt",
            "__heardelta_missing_endpoint__",
            "Nicht vorhanden",
            true,
            48000,
            24,
            2,
            "Testhersteller",
            "Testmodell",
            "Ohrumschließend",
            300,
            "Vordere 3,5-mm-Klinke",
            "Low (+6 dB)",
            -60m,
            -30m,
            false,
            DateTimeOffset.UtcNow);
        var request = new StimulusRenderRequest(
            TestedEar.Left,
            ListeningEnvironment.Quiet,
            -60m,
            -30m,
            null,
            42,
            48000);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new StimulusPlaybackService().PlayAsync(pack, "initial-01-1", hardware, request));

        Assert.Contains("kein Ersatzgerät", exception.Message);
    }

    private static void CopyDirectory(string sourceDirectory, string destinationDirectory)
    {
        Directory.CreateDirectory(destinationDirectory);
        foreach (var sourcePath in Directory.EnumerateFiles(sourceDirectory, "*", SearchOption.AllDirectories))
        {
            var relativePath = Path.GetRelativePath(sourceDirectory, sourcePath);
            var destinationPath = Path.Combine(destinationDirectory, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
            File.Copy(sourcePath, destinationPath);
        }
    }
}
