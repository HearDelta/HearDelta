using System.Collections;
using System.Globalization;
using System.IO;
using System.Resources;
using System.Text.RegularExpressions;
using HearDelta.App.Localization;
using HearDelta.Core.Localization;

namespace HearDelta.App.Tests;

public sealed partial class LocalizationTests
{
    [Theory]
    [InlineData("de-DE", "de-DE")]
    [InlineData("de-AT", "de-DE")]
    [InlineData("de-CH", "de-DE")]
    [InlineData("en-US", "en-US")]
    [InlineData("en-GB", "en-US")]
    [InlineData("fr-FR", "en-US")]
    public void AutomaticFollowsWindowsDisplayLanguage(string system, string expected) =>
        Assert.Equal(expected, UiLanguage.Resolve(UiLanguagePreference.Automatic, CultureInfo.GetCultureInfo(system)).Name);

    [Theory]
    [InlineData(UiLanguagePreference.German, "en-US", "de-DE")]
    [InlineData(UiLanguagePreference.English, "de-DE", "en-US")]
    public void ManualChoiceOverridesWindowsDisplayLanguage(UiLanguagePreference preference, string system, string expected) =>
        Assert.Equal(expected, UiLanguage.Resolve(preference, CultureInfo.GetCultureInfo(system)).Name);

    [Fact]
    public void StoreRoundTripsChoiceAndFallsBackToAutomatic()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"heardelta-language-{Guid.NewGuid():N}");
        try
        {
            var store = new UiLanguageStore(Path.Combine(directory, "ui-language.txt"));
            Assert.Equal(UiLanguagePreference.Automatic, store.Load());

            store.Save(UiLanguagePreference.English);
            Assert.Equal(UiLanguagePreference.English, store.Load());

            File.WriteAllText(store.FilePath, "Klingonisch");
            Assert.Equal(UiLanguagePreference.Automatic, store.Load());
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
    }

    public static TheoryData<string> ResourceSets => ["App", "Core"];

    [Theory]
    [MemberData(nameof(ResourceSets))]
    public void EnglishResourcesAreCompleteAndKeepPlaceholders(string set)
    {
        var manager = set == "App" ? Strings.ResourceManager : CoreStrings.ResourceManager;
        var neutral = Entries(manager, CultureInfo.InvariantCulture);
        var english = Entries(manager, CultureInfo.GetCultureInfo("en"));

        Assert.NotEmpty(neutral);
        Assert.Empty(neutral.Keys.Except(english.Keys));
        Assert.Empty(english.Keys.Except(neutral.Keys));
        foreach (var (key, german) in neutral)
        {
            Assert.False(string.IsNullOrWhiteSpace(english[key]), key);
            Assert.True(Placeholders(german).SetEquals(Placeholders(english[key])), key);
        }
    }

    [Fact]
    public void StringsFollowTheCurrentUiCulture()
    {
        Assert.Equal("Übersicht", Strings.ResourceManager.GetString(nameof(Strings.Shell_Overview), UiLanguage.German));
        Assert.Equal("Overview", Strings.ResourceManager.GetString(nameof(Strings.Shell_Overview), UiLanguage.English));
        Assert.Equal(
            "The tested ear differs.",
            CoreStrings.ResourceManager.GetString(nameof(CoreStrings.Compare_Ear), UiLanguage.English));
    }

    private static Dictionary<string, string> Entries(ResourceManager manager, CultureInfo culture)
    {
        var resourceSet = manager.GetResourceSet(culture, createIfNotExists: true, tryParents: false)
            ?? throw new InvalidOperationException($"Keine Ressourcen für „{culture.Name}“.");
        return resourceSet.Cast<DictionaryEntry>().ToDictionary(entry => (string)entry.Key, entry => (string)entry.Value!);
    }

    private static HashSet<string> Placeholders(string text) =>
        PlaceholderPattern().Matches(text).Select(match => match.Groups[1].Value).ToHashSet();

    [GeneratedRegex(@"\{(\d+)")]
    private static partial Regex PlaceholderPattern();
}
