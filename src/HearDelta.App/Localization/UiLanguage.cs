using System.Globalization;
using System.IO;
using System.Threading;

namespace HearDelta.App.Localization;

/// <summary>Gewählte Oberflächensprache; <see cref="Automatic"/> folgt der Windows-Anzeigesprache.</summary>
public enum UiLanguagePreference
{
    Automatic,
    German,
    English
}

public static class UiLanguage
{
    public static readonly CultureInfo German = CultureInfo.GetCultureInfo("de-DE");
    public static readonly CultureInfo English = CultureInfo.GetCultureInfo("en-US");

    /// <summary>
    /// Bestimmt die Kultur der Oberfläche. Automatisch wird Deutsch für eine deutsche Windows-Anzeigesprache und
    /// sonst Englisch (USA) gewählt.
    /// </summary>
    public static CultureInfo Resolve(UiLanguagePreference preference, CultureInfo systemUiCulture) => preference switch
    {
        UiLanguagePreference.German => German,
        UiLanguagePreference.English => English,
        _ => systemUiCulture.TwoLetterISOLanguageName == "de" ? German : English
    };

    /// <summary>Setzt Oberflächen- und Formatkultur für alle Threads des Prozesses.</summary>
    public static void Apply(CultureInfo culture)
    {
        CultureInfo.DefaultThreadCurrentCulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;
        Thread.CurrentThread.CurrentCulture = culture;
        Thread.CurrentThread.CurrentUICulture = culture;
    }
}

/// <summary>Speichert die Sprachwahl je Windows-Benutzer außerhalb der Messdatenbank.</summary>
public sealed class UiLanguageStore
{
    public UiLanguageStore(string? filePath = null)
    {
        FilePath = filePath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "HearDelta",
            "ui-language.txt");
    }

    public string FilePath { get; }

    public UiLanguagePreference Load()
    {
        try
        {
            return File.Exists(FilePath)
                && Enum.TryParse<UiLanguagePreference>(File.ReadAllText(FilePath).Trim(), ignoreCase: true, out var value)
                && Enum.IsDefined(value)
                    ? value
                    : UiLanguagePreference.Automatic;
        }
        catch (IOException)
        {
            return UiLanguagePreference.Automatic;
        }
        catch (UnauthorizedAccessException)
        {
            return UiLanguagePreference.Automatic;
        }
    }

    public void Save(UiLanguagePreference preference)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        File.WriteAllText(FilePath, preference.ToString());
    }
}
