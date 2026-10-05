using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using NAudio.Wave;
using HearDelta.Core;

namespace HearDelta.App.Services;

public sealed class LoadedStimulusPack
{
    private readonly IReadOnlyDictionary<string, StimulusDefinition> stimuliById;
    private readonly IReadOnlyDictionary<string, StimulusAudioAsset> audioByStimulusId;
    private readonly IReadOnlyDictionary<string, string> audioPathsByStimulusId;

    internal LoadedStimulusPack(
        StimulusCatalog catalog,
        StimulusAudioIndex audioIndex,
        StimulusMaterialIdentity materialIdentity,
        string rootDirectory,
        IReadOnlyDictionary<string, string> audioPathsByStimulusId,
        CardinalSpeechShapedNoiseProfile? cardinalNoiseProfile)
    {
        Catalog = catalog;
        AudioIndex = audioIndex;
        MaterialIdentity = materialIdentity;
        RootDirectory = rootDirectory;
        CardinalNoiseProfile = cardinalNoiseProfile;
        stimuliById = catalog.Lists
            .SelectMany(list => list.Items)
            .ToDictionary(item => item.Id, StringComparer.Ordinal);
        audioByStimulusId = audioIndex.Entries
            .ToDictionary(item => item.StimulusId, StringComparer.Ordinal);
        this.audioPathsByStimulusId = audioPathsByStimulusId;
    }

    public StimulusCatalog Catalog { get; }
    public StimulusAudioIndex AudioIndex { get; }
    public StimulusMaterialIdentity MaterialIdentity { get; }
    public string RootDirectory { get; }
    public CardinalSpeechShapedNoiseProfile? CardinalNoiseProfile { get; }

    public StimulusDefinition GetStimulus(string stimulusId) =>
        stimuliById.TryGetValue(stimulusId, out var stimulus)
            ? stimulus
            : throw new KeyNotFoundException($"Stimulus '{stimulusId}' ist nicht im geladenen Paket enthalten.");

    public StimulusAudioAsset GetAudioAsset(string stimulusId) =>
        audioByStimulusId.TryGetValue(stimulusId, out var asset)
            ? asset
            : throw new KeyNotFoundException($"Stimulus '{stimulusId}' hat keinen Audioindexeintrag.");

    public string GetAudioPath(string stimulusId) =>
        audioPathsByStimulusId.TryGetValue(stimulusId, out var path)
            ? path
            : throw new KeyNotFoundException($"Stimulus '{stimulusId}' hat keine geprüfte Audiodatei.");
}

public sealed class StimulusCatalogService
{
    private static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();

    public LoadedStimulusPack Load(string packDirectory)
    {
        if (string.IsNullOrWhiteSpace(packDirectory))
            throw new ArgumentException("Ein Stimuluspaket-Verzeichnis ist erforderlich.", nameof(packDirectory));

        var rootDirectory = Path.GetFullPath(packDirectory);
        var catalogPath = Path.Combine(rootDirectory, "catalog.json");
        var audioIndexPath = Path.Combine(rootDirectory, "audio-index.json");
        var catalogBytes = File.ReadAllBytes(catalogPath);
        var catalog = JsonSerializer.Deserialize<StimulusCatalog>(catalogBytes, JsonOptions)
            ?? throw new InvalidDataException("Der Stimuluskatalog ist leer oder unlesbar.");
        var audioIndexBytes = File.ReadAllBytes(audioIndexPath);
        var audioIndex = JsonSerializer.Deserialize<StimulusAudioIndex>(audioIndexBytes, JsonOptions)
            ?? throw new InvalidDataException("Der Stimulus-Audioindex ist leer oder unlesbar.");

        var errors = StimulusCatalogRules.Validate(catalog, audioIndex).ToList();
        var catalogSha256 = Convert.ToHexStringLower(SHA256.HashData(catalogBytes));
        if (!string.Equals(catalogSha256, audioIndex.CatalogSha256, StringComparison.OrdinalIgnoreCase))
            errors.Add("Die Prüfsumme des Stimuluskatalogs stimmt nicht mit dem Audioindex überein.");

        var verifiedPaths = new Dictionary<string, string>(StringComparer.Ordinal);
        var rootPrefix = rootDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) +
            Path.DirectorySeparatorChar;
        foreach (var entry in audioIndex.Entries ?? [])
        {
            var relativePath = entry.AudioFile.Replace('/', Path.DirectorySeparatorChar);
            var fullPath = Path.GetFullPath(Path.Combine(rootDirectory, relativePath));
            if (!fullPath.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase))
            {
                errors.Add($"Der Audiopfad für Stimulus '{entry.StimulusId}' verlässt das Stimuluspaket.");
                continue;
            }
            if (!File.Exists(fullPath))
            {
                errors.Add($"Die Audiodatei für Stimulus '{entry.StimulusId}' fehlt.");
                continue;
            }

            var fileInfo = new FileInfo(fullPath);
            if (fileInfo.Length != entry.ByteLength)
                errors.Add($"Die Dateilänge für Stimulus '{entry.StimulusId}' stimmt nicht.");
            var fileSha256 = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(fullPath)));
            if (!string.Equals(fileSha256, entry.Sha256, StringComparison.OrdinalIgnoreCase))
                errors.Add($"Die SHA-256 für Stimulus '{entry.StimulusId}' stimmt nicht.");

            using var wave = new WaveFileReader(fullPath);
            if (audioIndex.AudioFormat is not null &&
                (wave.WaveFormat.Encoding != WaveFormatEncoding.Pcm ||
                 wave.WaveFormat.SampleRate != audioIndex.AudioFormat.SampleRate ||
                 wave.WaveFormat.BitsPerSample != audioIndex.AudioFormat.BitsPerSample ||
                 wave.WaveFormat.Channels != audioIndex.AudioFormat.Channels))
                errors.Add($"Das WAV-Format für Stimulus '{entry.StimulusId}' widerspricht dem Audioindex.");
            verifiedPaths[entry.StimulusId] = fullPath;
        }

        if (errors.Count > 0)
            throw new InvalidDataException($"Das Stimuluspaket ist ungültig: {string.Join(" ", errors)}");
        var materialIdentity = StimulusMaterialIdentityFactory.Create(catalog, catalogBytes, audioIndexBytes);
        CardinalSpeechShapedNoiseProfile? cardinalNoiseProfile = null;
        if (CardinalSpeechShapedNoiseProtocol.SupportedMaterialIds.Contains(catalog.Id))
        {
            var profilePath = Path.Combine(
                Directory.GetParent(rootDirectory)?.FullName ?? rootDirectory,
                "cardinal-speech-shaped-noise-v1",
                catalog.Id,
                "profile.json");
            if (File.Exists(profilePath))
                cardinalNoiseProfile = CardinalSpeechShapedNoiseProfileLoader.Load(profilePath, materialIdentity);
        }
        return new LoadedStimulusPack(
            catalog,
            audioIndex,
            materialIdentity,
            rootDirectory,
            verifiedPaths,
            cardinalNoiseProfile);
    }

    public static string GetBundledPackDirectory() => Path.Combine(
        AppContext.BaseDirectory,
        "stimuli",
        "de-DE",
        "personal-phoneme-contrast-v1");

    private static JsonSerializerOptions CreateJsonOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }
}
