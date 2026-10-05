using System.IO;
using HearDelta.App.Services;
using HearDelta.Core;

namespace HearDelta.App.Tests;

public sealed class HeadphoneEqualizationCatalogServiceTests
{
    [Fact]
    public void BundledCatalogEntriesAreAllValidAndMatchTheirChecksums()
    {
        var service = new HeadphoneEqualizationCatalogService();

        var catalog = service.Load();

        Assert.Equal("autoeq-diffuse-field-parametric-eq", catalog.CatalogId);
        Assert.Equal(HeadphoneEqualizationCatalogService.BundledCatalogVersion, catalog.CatalogVersion);
        Assert.Equal(1378, catalog.Entries.Count);
        Assert.Equal(catalog.Entries.Count, catalog.Entries.Select(entry => entry.Name).Distinct().Count());
        Assert.All(catalog.Entries, entry => Assert.Equal("over-ear", entry.Form));
        Assert.All(catalog.Entries, entry => Assert.StartsWith("Diffusfeld", entry.Target, StringComparison.Ordinal));
        Assert.All(catalog.Entries, entry => Assert.Equal(
            entry.Source == "crinacle" ? HeadphoneEqualizationCatalogService.ReconstructedBasis : "measurement",
            entry.Basis));
        foreach (var entry in catalog.Entries)
        {
            var equalization = service.CreateEqualization(catalog, entry);
            Assert.Empty(HeadphoneEqualizer.Validate(equalization, 44_100));
        }
    }

    [Fact]
    public void SennheiserHd600KeepsOnlyTheRecommendedMeasurement()
    {
        var service = new HeadphoneEqualizationCatalogService();
        var catalog = service.Load();

        var entry = catalog.Entries.Single(entry => entry.Name == "Sennheiser HD 600");
        var equalization = service.CreateEqualization(catalog, entry);

        Assert.Equal("oratory1990/over-ear/Sennheiser HD 600", entry.Id);
        Assert.True(entry.Recommended);
        Assert.Equal(-6.8, equalization.PreampDb);
        Assert.Equal("AutoEq 7ae0f56 · oratory1990 · over-ear", equalization.Source);
        Assert.Equal("Diffusfeld GRAS KEMAR (AutoEq), ohne Bassanhebung", equalization.Target);
    }

    [Theory]
    [InlineData("Rtings", "HMS II.3 over-ear", "HMS-II.3-Anpassung")]
    [InlineData("Rtings", "Bruel & Kjaer 5128 over-ear", "B&K 5128")]
    [InlineData("crinacle", "EARS + 711 over-ear", "EARS-+-711-Anpassung")]
    [InlineData("crinacle", "GRAS 43AG-7 over-ear", "GRAS KEMAR (AutoEq)")]
    public void DiffuseFieldTargetFollowsTheMeasurementRig(string source, string rig, string expectedTargetPart)
    {
        var service = new HeadphoneEqualizationCatalogService();
        var catalog = service.Load();

        var entry = catalog.Entries.First(entry => entry.Source == source && entry.Rig == rig);
        var equalization = service.CreateEqualization(catalog, entry);

        Assert.Contains(expectedTargetPart, equalization.Target, StringComparison.Ordinal);
        Assert.Equal(source == "crinacle", equalization.Source.EndsWith("aus GraphicEQ rekonstruiert", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("Sennheiser HD 600", true)]
    [InlineData("Sennheiser HD 600 (2020)", false)]
    [InlineData("Beyerdynamic DT 770 Pro (80 Ohm)", true)]
    [InlineData("Beyerdynamic DT 770 Pro (250 Ohm)", true)]
    [InlineData("Beyerdynamic DT 770 Pro (32 ohm Limited Edition, pleather earpads)", false)]
    [InlineData("Bose QuietComfort SE (passive)", true)]
    [InlineData("Bose QuietComfort SE (ANC on)", false)]
    [InlineData("Sony WH-1000XM4", false)]
    [InlineData("Apple AirPods Max", false)]
    [InlineData("Sennheiser PXC 550 Wireless (wired, power on)", false)]
    [InlineData("64 Audio U12t (m15 Apex module)", false)]
    public void CatalogKeepsOnePassiveOverEarEntryPerModel(string name, bool expected)
    {
        var catalog = new HeadphoneEqualizationCatalogService().Load();

        Assert.Equal(expected, catalog.Entries.Any(entry => entry.Name == name));
    }

    [Fact]
    public void TamperedCatalogEntryIsRejected()
    {
        var service = new HeadphoneEqualizationCatalogService();
        var catalog = service.Load();
        var entry = catalog.Entries[0];

        Assert.Throws<InvalidDataException>(() => service.CreateEqualization(catalog, entry with { Sha256 = new string('0', 64) }));
    }

    [Fact]
    public void ImportedAutoEqFileUsesItsFileNameAsLabel()
    {
        var path = Path.Combine(Path.GetTempPath(), $"eq-{Guid.NewGuid():N}", "Thomann t.bone HD 815 ParametricEQ.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        try
        {
            File.WriteAllText(path, "Preamp: -3.0 dB\nFilter 1: ON PK Fc 3000 Hz Gain 3.0 dB Q 1.41\n");

            var equalization = new HeadphoneEqualizationCatalogService().Import(path);

            Assert.Equal("Thomann t.bone HD 815", equalization.Label);
            Assert.Equal(HeadphoneEqualizationCatalogService.ImportTarget, equalization.Target);
            Assert.Single(equalization.Filters);
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(path)!, recursive: true);
        }
    }
}
