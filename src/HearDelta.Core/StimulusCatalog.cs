using System.Security.Cryptography;
using System.Text;

namespace HearDelta.Core;

public static class StimulusCatalogProtocol
{
    public const int MinimumSchemaVersion = 1;
    public const int CurrentSchemaVersion = 2;
}

public sealed record StimulusCatalog(
    int SchemaVersion,
    string Id,
    string Version,
    string Language,
    string Title,
    string License,
    bool ClinicallyValidated,
    int ItemsPerList,
    IReadOnlyList<StimulusListDefinition> Lists,
    string? Paradigm = null,
    int? ResponseAlternativeCount = null,
    decimal? ChanceLevelPercent = null);

public sealed record StimulusListDefinition(
    string Id,
    SpeechMaterial Material,
    IReadOnlyList<StimulusDefinition> Items);

public sealed record StimulusDefinition(
    string Id,
    string SpokenText,
    string CanonicalResponse,
    string AudioFile,
    string? ContrastGroupId = null,
    string? ContrastPosition = null,
    string? TargetIpa = null,
    string? TargetPhoneme = null,
    string? CueCategory = null,
    int? TargetAlternativeIndex = null,
    IReadOnlyList<StimulusResponseAlternative>? ResponseAlternatives = null);

public sealed record StimulusResponseAlternative(
    int Index,
    string Text,
    string Ipa,
    string Phoneme);

public sealed record StimulusAudioIndex(
    int SchemaVersion,
    string CatalogId,
    string CatalogVersion,
    string CatalogSha256,
    StimulusAudioFormat AudioFormat,
    StimulusGeneratorDescriptor Generator,
    IReadOnlyList<StimulusAudioAsset> Entries);

public sealed record StimulusAudioFormat(
    string Encoding,
    int SampleRate,
    int BitsPerSample,
    int Channels);

public sealed record StimulusGeneratorDescriptor(
    string Name,
    string Version,
    string ExecutableSha256,
    string ModelRepository,
    string ModelRevision,
    string ModelName,
    string ModelSha256,
    string ConfigSha256,
    string ModelLicense,
    string DatasetLicense,
    bool SyntheticVoice,
    decimal NoiseScale,
    decimal PhonemeWidthNoise,
    decimal LengthScale,
    decimal SentenceSilenceSeconds,
    string? Voice = null,
    bool AiGeneratedVoice = false,
    string? AiGeneratedVoiceDisclosure = null,
    string? Instructions = null,
    decimal? Speed = null,
    string? ResponseFormat = null,
    string? SourceAudioFormat = null,
    string? Normalization = null,
    string? GeneratedAt = null);

public sealed record StimulusAudioAsset(
    string StimulusId,
    string AudioFile,
    string Sha256,
    long ByteLength,
    string? SourceAudioFile = null,
    string? SourceSha256 = null,
    long? SourceByteLength = null);

/// <summary>
/// Immutable identity of the complete stimulus material used for a session.
/// The two source-file hashes deliberately make a changed voice or audio index
/// visible even when a catalog keeps its public ID and version.
/// </summary>
public sealed record StimulusMaterialIdentity(
    string CatalogId,
    string CatalogVersion,
    string CatalogSha256,
    string AudioIndexSha256,
    string FingerprintSha256);

public static class StimulusMaterialIdentityFactory
{
    public static StimulusMaterialIdentity Create(
        StimulusCatalog catalog,
        ReadOnlySpan<byte> catalogBytes,
        ReadOnlySpan<byte> audioIndexBytes)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        var catalogSha256 = Convert.ToHexStringLower(SHA256.HashData(catalogBytes));
        var audioIndexSha256 = Convert.ToHexStringLower(SHA256.HashData(audioIndexBytes));
        return Create(catalog.Id, catalog.Version, catalogSha256, audioIndexSha256);
    }

    public static StimulusMaterialIdentity Create(
        string catalogId,
        string catalogVersion,
        string catalogSha256,
        string audioIndexSha256)
    {
        var identityBytes = Encoding.UTF8.GetBytes(string.Join('\n',
            catalogId,
            catalogVersion,
            catalogSha256.ToLowerInvariant(),
            audioIndexSha256.ToLowerInvariant()));
        return new StimulusMaterialIdentity(
            catalogId,
            catalogVersion,
            catalogSha256,
            audioIndexSha256,
            Convert.ToHexStringLower(SHA256.HashData(identityBytes)));
    }
}

public static class StimulusCatalogRules
{
    public static IReadOnlyList<string> Validate(StimulusCatalog catalog, StimulusAudioIndex audioIndex)
    {
        var errors = new List<string>();

        if (catalog.SchemaVersion < StimulusCatalogProtocol.MinimumSchemaVersion ||
            catalog.SchemaVersion > StimulusCatalogProtocol.CurrentSchemaVersion)
            errors.Add($"Die Katalogversion {catalog.SchemaVersion} wird nicht unterstützt.");
        if (audioIndex.SchemaVersion < StimulusCatalogProtocol.MinimumSchemaVersion ||
            audioIndex.SchemaVersion > StimulusCatalogProtocol.CurrentSchemaVersion)
            errors.Add($"Die Audioindexversion {audioIndex.SchemaVersion} wird nicht unterstützt.");
        if (catalog.SchemaVersion != audioIndex.SchemaVersion)
            errors.Add("Stimuluskatalog und Audioindex verwenden unterschiedliche Schemaversionen.");
        if (string.IsNullOrWhiteSpace(catalog.Id) || string.IsNullOrWhiteSpace(catalog.Version))
            errors.Add("Der Stimuluskatalog benötigt ID und Version.");
        if (!string.Equals(catalog.Id, audioIndex.CatalogId, StringComparison.Ordinal) ||
            !string.Equals(catalog.Version, audioIndex.CatalogVersion, StringComparison.Ordinal))
            errors.Add("Stimuluskatalog und Audioindex gehören nicht zur selben Version.");
        if (string.IsNullOrWhiteSpace(catalog.Language) || string.IsNullOrWhiteSpace(catalog.License))
            errors.Add("Sprache und Lizenz des Stimuluskatalogs müssen dokumentiert sein.");
        if (catalog.ItemsPerList <= 0)
            errors.Add("Die Listenlänge muss positiv sein.");
        if (catalog.Lists is null || catalog.Lists.Count == 0)
        {
            errors.Add("Der Stimuluskatalog enthält keine Listen.");
            return errors;
        }

        ValidateLists(catalog, errors);
        if (catalog.SchemaVersion == 2)
            ValidatePhonemeContrastCatalog(catalog, errors);
        if (string.Equals(catalog.Paradigm, CardinalNumberProtocol.Paradigm, StringComparison.Ordinal))
            ValidateCardinalNumberCatalog(catalog, errors);
        ValidateAudioIndex(catalog, audioIndex, errors);
        return errors;
    }

    private static void ValidateCardinalNumberCatalog(
        StimulusCatalog catalog,
        ICollection<string> errors)
    {
        if (catalog.ItemsPerList != CardinalNumberProtocol.ItemsPerList ||
            catalog.Lists.Count != CardinalNumberProtocol.ListCount ||
            catalog.Lists.Any(list => list.Material != SpeechMaterial.Numbers))
            errors.Add("Das Kardinalzahlmaterial benötigt 36 Zahlenlisten mit je 25 Werten.");
        if (catalog.ResponseAlternativeCount is not null ||
            catalog.ChanceLevelPercent is null ||
            Math.Abs(catalog.ChanceLevelPercent.Value - CardinalNumberProtocol.ChanceLevelPercent) > 0.000001m)
            errors.Add("Das Kardinalzahlmaterial benötigt eine freie Antwort und die Zufallstrefferquote 1/900.");

        var allItems = catalog.Lists.SelectMany(list => list.Items).ToArray();
        if (allItems.Any(item => !CardinalNumberProtocol.IsCanonicalResponse(item.CanonicalResponse) ||
                                 !string.Equals(item.Id, $"cardinal-{item.CanonicalResponse}", StringComparison.Ordinal) ||
                                 (item.ResponseAlternatives?.Count ?? 0) != 0))
            errors.Add("Jeder Kardinalzahl-Stimulus benötigt eine eindeutige freie dreistellige Zielzahl.");
        var values = allItems.Select(item => item.CanonicalResponse).Order(StringComparer.Ordinal).ToArray();
        var expected = Enumerable.Range(CardinalNumberProtocol.MinimumValue,
                CardinalNumberProtocol.MaximumValue - CardinalNumberProtocol.MinimumValue + 1)
            .Select(value => value.ToString())
            .Order(StringComparer.Ordinal)
            .ToArray();
        if (!values.SequenceEqual(expected, StringComparer.Ordinal))
            errors.Add("Das Kardinalzahlmaterial muss jeden Wert von 100 bis 999 genau einmal enthalten.");
    }

    private static void ValidateLists(StimulusCatalog catalog, ICollection<string> errors)
    {
        if (catalog.Lists.Any(list => string.IsNullOrWhiteSpace(list.Id)) ||
            catalog.Lists.Select(list => list.Id).Distinct(StringComparer.Ordinal).Count() != catalog.Lists.Count)
            errors.Add("Alle Stimuluslisten benötigen unterschiedliche IDs.");

        foreach (var material in catalog.Lists.Select(list => list.Material).Distinct())
        {
            if (catalog.Lists.Count(list => list.Material == material) < 2)
                errors.Add($"Für {material} werden mindestens zwei getrennte Listen benötigt.");
        }

        var allItems = new List<StimulusDefinition>();
        foreach (var list in catalog.Lists)
        {
            if (!Enum.IsDefined(list.Material))
                errors.Add($"Liste '{list.Id}' verwendet eine unbekannte Materialart.");
            if (list.Items is null || list.Items.Count != catalog.ItemsPerList)
            {
                errors.Add($"Liste '{list.Id}' muss genau {catalog.ItemsPerList} Stimuli enthalten.");
                continue;
            }

            foreach (var item in list.Items)
            {
                allItems.Add(item);
                if (string.IsNullOrWhiteSpace(item.Id) || string.IsNullOrWhiteSpace(item.SpokenText) ||
                    string.IsNullOrWhiteSpace(item.CanonicalResponse))
                    errors.Add($"Liste '{list.Id}' enthält einen unvollständigen Stimulus.");
                if (!IsSafeRelativeWavePath(item.AudioFile))
                    errors.Add($"Stimulus '{item.Id}' hat keinen sicheren relativen WAV-Pfad.");
            }
        }

        if (allItems.Select(item => item.Id).Distinct(StringComparer.Ordinal).Count() != allItems.Count)
            errors.Add("Stimulus-IDs müssen katalogweit eindeutig sein.");
        if (allItems.Select(item => item.AudioFile).Distinct(StringComparer.OrdinalIgnoreCase).Count() != allItems.Count)
            errors.Add("Jeder Stimulus benötigt eine eigene Audiodatei.");
    }

    private static void ValidatePhonemeContrastCatalog(
        StimulusCatalog catalog,
        ICollection<string> errors)
    {
        if (!string.Equals(catalog.Paradigm, "closed-set-phoneme-contrast", StringComparison.Ordinal))
            errors.Add("Schema v2 benötigt das Paradigma 'closed-set-phoneme-contrast'.");
        if (catalog.ResponseAlternativeCount != 5)
            errors.Add("Das Phonemkontrastparadigma benötigt genau fünf Antwortalternativen.");
        if (catalog.ChanceLevelPercent != 20m)
            errors.Add("Die dokumentierte Zufallstrefferquote muss bei fünf Alternativen 20 Prozent betragen.");
        if (catalog.Lists.Any(list => list.Material != SpeechMaterial.PhonemeContrasts))
            errors.Add("Schema-v2-Phonemkontrastlisten müssen als PhonemeContrasts gekennzeichnet sein.");
        if (catalog.Lists.Count != 5)
            errors.Add("Eine vollständige Fünferrotation benötigt genau fünf Listen.");

        var expectedGroups = catalog.Lists[0].Items
            .Select(item => item.ContrastGroupId)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Cast<string>()
            .Order(StringComparer.Ordinal)
            .ToArray();
        foreach (var list in catalog.Lists)
        {
            var actualGroups = list.Items
                .Select(item => item.ContrastGroupId)
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Cast<string>()
                .Order(StringComparer.Ordinal)
                .ToArray();
            if (!actualGroups.SequenceEqual(expectedGroups, StringComparer.Ordinal))
                errors.Add($"Liste '{list.Id}' enthält nicht dieselbe Kontrastgruppenmenge wie die Referenzliste.");
        }

        var allItems = catalog.Lists.SelectMany(list => list.Items).ToArray();
        foreach (var item in allItems)
        {
            if (string.IsNullOrWhiteSpace(item.ContrastGroupId) ||
                item.ContrastPosition is not ("initial" or "vowel" or "final") ||
                string.IsNullOrWhiteSpace(item.TargetIpa) ||
                string.IsNullOrWhiteSpace(item.TargetPhoneme) ||
                item.CueCategory is not ("broad" or "low-mid" or "mid" or "high"))
            {
                errors.Add($"Stimulus '{item.Id}' hat unvollständige Phonemkontrastmetadaten.");
                continue;
            }

            var alternatives = item.ResponseAlternatives ?? [];
            if (alternatives.Count != 5 ||
                !alternatives.Select(alternative => alternative.Index).Order().SequenceEqual([1, 2, 3, 4, 5]) ||
                alternatives.Select(alternative => alternative.Text).Distinct(StringComparer.OrdinalIgnoreCase).Count() != 5 ||
                alternatives.Select(alternative => alternative.Ipa).Distinct(StringComparer.Ordinal).Count() != 5 ||
                alternatives.Any(alternative => string.IsNullOrWhiteSpace(alternative.Text) ||
                    string.IsNullOrWhiteSpace(alternative.Ipa) ||
                    string.IsNullOrWhiteSpace(alternative.Phoneme)))
            {
                errors.Add($"Stimulus '{item.Id}' benötigt fünf eindeutige, vollständig beschriebene Antwortalternativen.");
                continue;
            }

            var target = alternatives.SingleOrDefault(alternative => alternative.Index == item.TargetAlternativeIndex);
            if (target is null ||
                !string.Equals(target.Text, item.SpokenText, StringComparison.Ordinal) ||
                !string.Equals(target.Text, item.CanonicalResponse, StringComparison.Ordinal) ||
                !string.Equals(target.Ipa, item.TargetIpa, StringComparison.Ordinal) ||
                !string.Equals(target.Phoneme, item.TargetPhoneme, StringComparison.Ordinal))
                errors.Add($"Stimulus '{item.Id}' passt nicht zu seiner markierten Zielalternative.");
        }

        foreach (var group in allItems.GroupBy(item => item.ContrastGroupId, StringComparer.Ordinal))
        {
            if (string.IsNullOrWhiteSpace(group.Key) || group.Count() != 5)
            {
                errors.Add("Jede Kontrastgruppe muss genau einmal je Liste vorkommen.");
                continue;
            }

            var targetRotation = group.Select(item => item.TargetAlternativeIndex).Order().ToArray();
            if (!targetRotation.SequenceEqual<int?>([1, 2, 3, 4, 5]))
                errors.Add($"Kontrastgruppe '{group.Key}' enthält keine vollständige Zielrotation.");

            var alternativeSignatures = group.Select(item => string.Join('|',
                (item.ResponseAlternatives ?? []).OrderBy(alternative => alternative.Index)
                    .Select(alternative => $"{alternative.Index}:{alternative.Text}:{alternative.Ipa}:{alternative.Phoneme}")))
                .Distinct(StringComparer.Ordinal)
                .Count();
            if (alternativeSignatures != 1)
                errors.Add($"Kontrastgruppe '{group.Key}' verwendet zwischen den Listen unterschiedliche Antwortmengen.");
        }
    }

    private static void ValidateAudioIndex(
        StimulusCatalog catalog,
        StimulusAudioIndex audioIndex,
        ICollection<string> errors)
    {
        if (!IsSha256(audioIndex.CatalogSha256))
            errors.Add("Der Audioindex benötigt die SHA-256 des Katalogs.");
        if (audioIndex.AudioFormat is null ||
            !string.Equals(audioIndex.AudioFormat.Encoding, "PCM_SIGNED", StringComparison.Ordinal) ||
            audioIndex.AudioFormat.SampleRate <= 0 ||
            audioIndex.AudioFormat.BitsPerSample != 16 ||
            audioIndex.AudioFormat.Channels != 1)
            errors.Add("Das Audiopaket muss aus vorzeichenbehafteten 16-Bit-Mono-PCM-Dateien bestehen.");
        if (audioIndex.Generator is null || string.IsNullOrWhiteSpace(audioIndex.Generator.Name) ||
            string.IsNullOrWhiteSpace(audioIndex.Generator.ModelRevision) || !audioIndex.Generator.SyntheticVoice)
            errors.Add("Generator, Modellrevision und synthetische Herkunft müssen dokumentiert sein.");
        if (audioIndex.Generator is { AiGeneratedVoice: true } generator &&
            (string.IsNullOrWhiteSpace(generator.Voice) || string.IsNullOrWhiteSpace(generator.AiGeneratedVoiceDisclosure)))
            errors.Add("Eine KI-generierte Stimme benötigt Stimmenname und sichtbare Herkunftskennzeichnung.");
        if (audioIndex.Entries is null)
        {
            errors.Add("Der Audioindex enthält keine Einträge.");
            return;
        }

        var catalogItems = catalog.Lists.SelectMany(list => list.Items).ToArray();
        if (audioIndex.Entries.Count != catalogItems.Length)
            errors.Add("Katalog und Audioindex enthalten unterschiedlich viele Stimuli.");
        if (audioIndex.Entries.Select(entry => entry.StimulusId).Distinct(StringComparer.Ordinal).Count() != audioIndex.Entries.Count)
            errors.Add("Der Audioindex enthält doppelte Stimulus-IDs.");

        var entriesById = audioIndex.Entries
            .GroupBy(entry => entry.StimulusId, StringComparer.Ordinal)
            .Where(group => group.Count() == 1)
            .ToDictionary(group => group.Key, group => group.Single(), StringComparer.Ordinal);
        foreach (var item in catalogItems)
        {
            if (!entriesById.TryGetValue(item.Id, out var entry))
            {
                errors.Add($"Für Stimulus '{item.Id}' fehlt der Audioindexeintrag.");
                continue;
            }
            if (!string.Equals(item.AudioFile, entry.AudioFile, StringComparison.OrdinalIgnoreCase))
                errors.Add($"Der Audiopfad für Stimulus '{item.Id}' widerspricht dem Katalog.");
            if (!IsSha256(entry.Sha256) || entry.ByteLength <= 44)
                errors.Add($"Stimulus '{item.Id}' hat keine gültige Dateiprüfsumme oder Länge.");
            if (entry.SourceAudioFile is not null &&
                (!IsSafeRelativeWavePath(entry.SourceAudioFile) ||
                 !IsSha256(entry.SourceSha256 ?? string.Empty) ||
                 entry.SourceByteLength is null or <= 44))
                errors.Add($"Stimulus '{item.Id}' hat unvollständige Roh-Audionachweise.");
        }
    }

    private static bool IsSafeRelativeWavePath(string path) =>
        !string.IsNullOrWhiteSpace(path) &&
        !Path.IsPathRooted(path) &&
        !path.Split('/', '\\').Any(segment => segment == "..") &&
        string.Equals(Path.GetExtension(path), ".wav", StringComparison.OrdinalIgnoreCase);

    private static bool IsSha256(string value) =>
        value is { Length: 64 } && value.All(character => Uri.IsHexDigit(character));
}
