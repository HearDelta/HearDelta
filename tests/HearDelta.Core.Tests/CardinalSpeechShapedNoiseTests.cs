using HearDelta.Core;

namespace HearDelta.Core.Tests;

public sealed class CardinalSpeechShapedNoiseTests
{
    private const string CatalogHash = "2f243101f65bf7e19688e31bcba3b849662119b2071a06ccc90bf73c11431670";
    private const string AudioIndexHash = "6a87b81d31da60f21ad2a8db21c50297518d3d0cbcc78916e70e8c4d55dc44bc";

    [Fact]
    public void CurrentContractBindsOneCompleteCardinalMaterial()
    {
        var contract = CardinalSpeechShapedNoiseProtocol.CreateContract(
            "de-DE-personal-cardinal-numbers-christoph-v1",
            CatalogHash,
            AudioIndexHash);

        Assert.Empty(CardinalSpeechShapedNoiseProtocol.Validate(contract));
        Assert.Equal(900, contract.StimulusCount);
        Assert.Equal([44100, 48000], contract.SupportedOutputSampleRates);
        Assert.Equal("first-to-last-active-frame-v1", contract.SpeechRmsWindow);
        Assert.Equal("speech-active-aligned-v1", contract.NoiseRmsWindow);
    }

    [Fact]
    public void ContractCannotBeReusedForUnrelatedOrIncompleteMaterial()
    {
        var contract = CardinalSpeechShapedNoiseProtocol.CreateContract(
            "de-DE-personal-cardinal-numbers-christoph-v1",
            CatalogHash,
            AudioIndexHash) with
        {
            MaterialId = "de-DE-personal-phoneme-contrast-v1",
            StimulusCount = 899
        };

        var errors = CardinalSpeechShapedNoiseProtocol.Validate(contract);

        Assert.Contains(errors, error => error.Contains("Kardinalzahlpaket"));
        Assert.Contains(errors, error => error.Contains("allen 900"));
    }

    [Theory]
    [InlineData("ABC3b81d31da60f21ad2a8db21c50297518d3d0cbcc78916e70e8c4d55dc44bc")]
    [InlineData("too-short")]
    public void MaterialBindingRequiresCanonicalSha256(string invalidHash)
    {
        var contract = CardinalSpeechShapedNoiseProtocol.CreateContract(
            "de-DE-personal-cardinal-numbers-katja-v1",
            CatalogHash,
            invalidHash);

        Assert.Contains(
            CardinalSpeechShapedNoiseProtocol.Validate(contract),
            error => error.Contains("SHA-256"));
    }

    [Fact]
    public void FrozenAnalysisAndRmsRulesCannotChangeSilently()
    {
        var contract = CardinalSpeechShapedNoiseProtocol.CreateContract(
            "de-DE-personal-cardinal-numbers-katja-v1",
            CatalogHash,
            AudioIndexHash) with
        {
            AnalysisFrameMilliseconds = 20
        };

        Assert.Contains(
            CardinalSpeechShapedNoiseProtocol.Validate(contract),
            error => error.Contains("eingefrorenen Vertrag"));
    }

    [Fact]
    public void EquivalentDeserializedSampleRateListRemainsValid()
    {
        var contract = CardinalSpeechShapedNoiseProtocol.CreateContract(
            "de-DE-personal-cardinal-numbers-katja-v1",
            CatalogHash,
            AudioIndexHash) with
        {
            SupportedOutputSampleRates = new[] { 44100, 48000 }
        };

        Assert.Empty(CardinalSpeechShapedNoiseProtocol.Validate(contract));
    }
}
