using HearDelta.Core;

namespace HearDelta.Core.Tests;

public sealed class StimulusCatalogRulesTests
{
    [Fact]
    public void SchemaOneRemainsSupportedWithoutEverySpeechMaterial()
    {
        var (catalog, audioIndex) = CreateSchemaOnePack();

        Assert.Empty(StimulusCatalogRules.Validate(catalog, audioIndex));
    }

    [Fact]
    public void SchemaTwoAcceptsCompleteFiveListTargetRotation()
    {
        var (catalog, audioIndex) = CreateSchemaTwoPack();

        Assert.Empty(StimulusCatalogRules.Validate(catalog, audioIndex));
    }

    [Fact]
    public void SchemaTwoRejectsRepeatedTargetWithinContrastGroup()
    {
        var (catalog, audioIndex) = CreateSchemaTwoPack();
        var changedLists = catalog.Lists
            .Select((list, index) => index == 4
                ? list with
                {
                    Items =
                    [
                        list.Items[0] with
                        {
                            SpokenText = "Band",
                            CanonicalResponse = "Band",
                            TargetIpa = "/bant/",
                            TargetPhoneme = "b",
                            TargetAlternativeIndex = 1
                        }
                    ]
                }
                : list)
            .ToArray();

        var errors = StimulusCatalogRules.Validate(catalog with { Lists = changedLists }, audioIndex);

        Assert.Contains(errors, error => error.Contains("vollständige Zielrotation"));
    }

    [Fact]
    public void AiGeneratedVoiceNeedsVoiceNameAndDisclosure()
    {
        var (catalog, audioIndex) = CreateSchemaTwoPack();
        var invalidGenerator = audioIndex.Generator with
        {
            Voice = null,
            AiGeneratedVoice = true,
            AiGeneratedVoiceDisclosure = null
        };

        var errors = StimulusCatalogRules.Validate(catalog, audioIndex with { Generator = invalidGenerator });

        Assert.Contains(errors, error => error.Contains("KI-generierte Stimme"));
    }

    [Fact]
    public void CardinalNumberCatalogAcceptsEveryThreeDigitValueExactlyOnce()
    {
        var (catalog, index) = CreateCardinalNumberPack();

        Assert.Empty(StimulusCatalogRules.Validate(catalog, index));
    }

    [Fact]
    public void CardinalNumberCatalogRejectsMissingNumber()
    {
        var (catalog, index) = CreateCardinalNumberPack();
        var changed = catalog with
        {
            Lists = catalog.Lists.Select((list, listIndex) => listIndex == 0
                ? list with { Items = list.Items.Select((item, itemIndex) => itemIndex == 0
                    ? item with { CanonicalResponse = "999" }
                    : item).ToArray() }
                : list).ToArray()
        };

        var errors = StimulusCatalogRules.Validate(changed, index);

        Assert.Contains(errors, error => error.Contains("100 bis 999"));
    }

    private static (StimulusCatalog Catalog, StimulusAudioIndex AudioIndex) CreateSchemaOnePack()
    {
        var lists = new[] { "a", "b" }
            .Select(listId => new StimulusListDefinition(
                listId,
                SpeechMaterial.Monosyllables,
                [new StimulusDefinition($"word-{listId}", "Wort", "Wort", $"audio/word-{listId}.wav")]))
            .ToArray();
        var catalog = new StimulusCatalog(
            1,
            "schema-one",
            "1.0.0",
            "de-DE",
            "Schema one",
            "CC0-1.0",
            false,
            1,
            lists);
        var index = new StimulusAudioIndex(
            1,
            catalog.Id,
            catalog.Version,
            new string('a', 64),
            new StimulusAudioFormat("PCM_SIGNED", 22050, 16, 1),
            CreateGenerator(),
            lists.SelectMany(list => list.Items)
                .Select(item => new StimulusAudioAsset(item.Id, item.AudioFile, new string('b', 64), 100))
                .ToArray());
        return (catalog, index);
    }

    private static (StimulusCatalog Catalog, StimulusAudioIndex AudioIndex) CreateSchemaTwoPack()
    {
        StimulusResponseAlternative[] alternatives =
        [
            new(1, "Band", "/bant/", "b"),
            new(2, "Hand", "/hant/", "h"),
            new(3, "Land", "/lant/", "l"),
            new(4, "Rand", "/ʁant/", "ʁ"),
            new(5, "Sand", "/zant/", "z")
        ];
        var lists = Enumerable.Range(1, 5)
            .Select(targetIndex =>
            {
                var target = alternatives[targetIndex - 1];
                return new StimulusListDefinition(
                    $"contrast-{targetIndex}",
                    SpeechMaterial.PhonemeContrasts,
                    [
                        new StimulusDefinition(
                            $"initial-01-{targetIndex}",
                            target.Text,
                            target.Text,
                            $"audio/initial-01-{targetIndex}.wav",
                            "initial-01",
                            "initial",
                            target.Ipa,
                            target.Phoneme,
                            "broad",
                            targetIndex,
                            alternatives)
                    ]);
            })
            .ToArray();
        var catalog = new StimulusCatalog(
            2,
            "schema-two",
            "1.0.0",
            "de-DE",
            "Schema two",
            "CC0-1.0",
            false,
            1,
            lists,
            "closed-set-phoneme-contrast",
            5,
            20m);
        var index = new StimulusAudioIndex(
            2,
            catalog.Id,
            catalog.Version,
            new string('a', 64),
            new StimulusAudioFormat("PCM_SIGNED", 22050, 16, 1),
            CreateGenerator() with
            {
                Voice = "cedar",
                AiGeneratedVoice = true,
                AiGeneratedVoiceDisclosure = "KI-generierte Stimme"
            },
            lists.SelectMany(list => list.Items)
                .Select(item => new StimulusAudioAsset(item.Id, item.AudioFile, new string('b', 64), 100))
                .ToArray());
        return (catalog, index);
    }

    private static (StimulusCatalog Catalog, StimulusAudioIndex AudioIndex) CreateCardinalNumberPack()
    {
        var lists = Enumerable.Range(0, CardinalNumberProtocol.ListCount)
            .Select(listIndex => new StimulusListDefinition(
                $"numbers-{listIndex + 1:00}",
                SpeechMaterial.Numbers,
                Enumerable.Range(0, CardinalNumberProtocol.ItemsPerList)
                    .Select(itemIndex =>
                    {
                        var value = CardinalNumberProtocol.MinimumValue + listIndex +
                            (CardinalNumberProtocol.ListCount * itemIndex);
                        return new StimulusDefinition(
                            $"cardinal-{value}", value.ToString(), value.ToString(), $"audio/cardinal-{value}.wav");
                    }).ToArray()))
            .ToArray();
        var catalog = new StimulusCatalog(
            1, "numbers", "1.0.0", "de-DE", "Numbers", "test", false,
            CardinalNumberProtocol.ItemsPerList, lists, CardinalNumberProtocol.Paradigm,
            null, CardinalNumberProtocol.ChanceLevelPercent);
        var index = new StimulusAudioIndex(
            1, catalog.Id, catalog.Version, new string('a', 64),
            new StimulusAudioFormat("PCM_SIGNED", 22050, 16, 1),
            CreateGenerator() with { Voice = "Christoph", AiGeneratedVoice = true, AiGeneratedVoiceDisclosure = "KI-generiert" },
            lists.SelectMany(list => list.Items)
                .Select(item => new StimulusAudioAsset(item.Id, item.AudioFile, new string('b', 64), 100))
                .ToArray());
        return (catalog, index);
    }

    private static StimulusGeneratorDescriptor CreateGenerator() => new(
        "Test generator",
        "1",
        new string('c', 64),
        "test",
        "revision",
        "model",
        new string('d', 64),
        new string('e', 64),
        "test",
        "test",
        true,
        0,
        0,
        1,
        0);
}
