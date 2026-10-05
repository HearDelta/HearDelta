using System.IO;
using System.Text.Json;
using HearDelta.Core;

namespace HearDelta.App.Services;

public sealed record HeadphoneEqualizationCatalogEntry(
    string Id,
    string Name,
    string Source,
    string Rig,
    string Form,
    bool Recommended,
    string Target,
    string Basis,
    string Sha256,
    string Text)
{
    public string MeasurementLabel => string.Equals(Rig, Form, StringComparison.Ordinal) ? $"{Source} · {Form}" : $"{Source} · {Rig}";
}

public sealed record HeadphoneEqualizationCatalog(
    string CatalogId,
    string CatalogVersion,
    string SourceRepository,
    string SourceCommit,
    string Target,
    IReadOnlyList<HeadphoneEqualizationCatalogEntry> Entries);

public interface IHeadphoneEqualizationCatalogService
{
    HeadphoneEqualizationCatalog Load();

    HeadphoneEqualization CreateEqualization(HeadphoneEqualizationCatalog catalog, HeadphoneEqualizationCatalogEntry entry);

    HeadphoneEqualization Import(string path);

    /// <summary>Fragt eine AutoEq-Datei ab; null bei Abbruch.</summary>
    string? PickImportFile();
}

/// <summary>
/// Lädt den gebündelten, mit AutoEq auf Diffusfeld berechneten Katalog
/// (<c>hardware/headphone-equalization/autoeq-*-diffuse-field</c>) und erzeugt daraus geprüfte
/// Kopfhörerentzerrungen. Jede Auswahl wird aus dem gespeicherten Text geparst und gegen die Katalogprüfsumme
/// geprüft; das Ziel stammt aus dem Eintrag, weil es vom Messaufbau abhängt.
/// </summary>
public sealed class HeadphoneEqualizationCatalogService(string? catalogPath = null) : IHeadphoneEqualizationCatalogService
{
    public const string BundledCatalogVersion = "7ae0f56";
    public const string BundledCatalogDirectory = $"autoeq-{BundledCatalogVersion}-diffuse-field";
    /// <summary>Grundlage ohne Rohmessung (crinacle): aus AutoEqs Harman-GraphicEQ zurückgerechnet.</summary>
    public const string ReconstructedBasis = "graphicEq";
    public const string ImportTarget = "Unbekannt (importierte Datei)";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private HeadphoneEqualizationCatalog? loaded;

    public static string GetBundledCatalogPath() => Path.Combine(
        AppContext.BaseDirectory, "hardware", "headphone-equalization", BundledCatalogDirectory, "catalog.json");

    public HeadphoneEqualizationCatalog Load()
    {
        if (loaded is not null)
            return loaded;
        using var stream = File.OpenRead(catalogPath ?? GetBundledCatalogPath());
        var catalog = JsonSerializer.Deserialize<HeadphoneEqualizationCatalog>(stream, JsonOptions)
            ?? throw new InvalidDataException("Der Kopfhörerentzerrungskatalog ist leer.");
        if (catalog.Entries is not { Count: > 0 })
            throw new InvalidDataException("Der Kopfhörerentzerrungskatalog enthält keine Einträge.");
        return loaded = catalog;
    }

    public HeadphoneEqualization CreateEqualization(HeadphoneEqualizationCatalog catalog, HeadphoneEqualizationCatalogEntry entry)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(entry);
        var equalization = HeadphoneEqualizer.Parse(
            entry.Text,
            entry.Basis == ReconstructedBasis
                ? $"AutoEq {catalog.CatalogVersion} · {entry.MeasurementLabel} · aus GraphicEQ rekonstruiert"
                : $"AutoEq {catalog.CatalogVersion} · {entry.MeasurementLabel}",
            entry.Id,
            entry.Name,
            entry.Target);
        if (!string.Equals(equalization.SourceSha256, entry.Sha256, StringComparison.Ordinal))
            throw new InvalidDataException($"Die Prüfsumme des Katalogeintrags „{entry.Id}“ stimmt nicht.");
        return equalization;
    }

    public HeadphoneEqualization Import(string path)
    {
        var text = File.ReadAllText(path);
        var name = Path.GetFileNameWithoutExtension(path);
        const string suffix = " ParametricEQ";
        var label = name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase) ? name[..^suffix.Length] : name;
        return HeadphoneEqualizer.Parse(text, "Import (AutoEq-Format)", Path.GetFileName(path), label, ImportTarget);
    }

    public string? PickImportFile()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "AutoEq-Datei „ParametricEQ.txt“ importieren",
            Filter = "AutoEq Parametric EQ (*.txt)|*.txt|Alle Dateien (*.*)|*.*"
        };
        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }
}
