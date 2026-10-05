using HearDelta.Core;

namespace HearDelta.Core.Tests;

public sealed class PairedMeasurementSessionRulesTests
{
    [Fact]
    public void RandomizationSeedCreatesBothPossiblePairedOrders()
    {
        var even = CreateSession(randomizationSeed: 20);
        var odd = CreateSession(randomizationSeed: 21);

        Assert.Equal(HearingAidCondition.WithoutHearingAid, even.Blocks[0].Condition);
        Assert.Equal(HearingAidCondition.WithHearingAid, even.Blocks[1].Condition);
        Assert.Equal(HearingAidCondition.WithHearingAid, odd.Blocks[0].Condition);
        Assert.Equal(HearingAidCondition.WithoutHearingAid, odd.Blocks[1].Condition);
        Assert.Empty(PairedMeasurementSessionRules.Validate(even));
        Assert.Empty(PairedMeasurementSessionRules.Validate(odd));
    }

    [Fact]
    public void PairMustContainDifferentStimulusLists()
    {
        var session = PairedMeasurementSessionFactory.CreateRandomized(
            TestedEar.Left,
            CreateHearingAid(),
            SpeechMaterial.Monosyllables,
            ListeningEnvironment.Quiet,
            "mono-list-a",
            "MONO-LIST-A",
            CreateHardware(),
            DateTimeOffset.Parse("2026-09-01T08:00:00Z"),
            20,
            CreateMaterialIdentity(),
            MeasurementSessionContracts.PhonemeContrastMeasurement);

        Assert.Contains(
            PairedMeasurementSessionRules.Validate(session),
            error => error.Contains("unterschiedliche Listen"));
    }

    [Fact]
    public void HearingAidMustFitTestedEar()
    {
        var session = CreateSession() with { Ear = TestedEar.Right };

        Assert.Contains(
            PairedMeasurementSessionRules.Validate(session),
            error => error.Contains("dieselbe Seite"));
    }

    [Fact]
    public void StoredOrderMustMatchRandomizationSeed()
    {
        var session = CreateSession(randomizationSeed: 20);
        var changedFirstBlock = session.Blocks[0] with { Condition = HearingAidCondition.WithHearingAid };
        var changedSecondBlock = session.Blocks[1] with { Condition = HearingAidCondition.WithoutHearingAid };
        var changed = session with { Blocks = [changedFirstBlock, changedSecondBlock] };

        Assert.Contains(
            PairedMeasurementSessionRules.Validate(changed),
            error => error.Contains("Randomisierungs-Seed"));
    }

    [Fact]
    public void RawResponsesNeedUniquePresentationOrder()
    {
        var session = CreateSession();
        var capturedAt = DateTimeOffset.Parse("2026-09-01T08:03:00Z");
        var changedFirstBlock = session.Blocks[0] with
        {
            RawResponses =
            [
                new RawMeasurementResponse(1, "stimulus-1", "eins", capturedAt, null!),
                new RawMeasurementResponse(1, "stimulus-2", null, capturedAt.AddSeconds(4), null!)
            ]
        };
        var changed = session with { Blocks = [changedFirstBlock, session.Blocks[1]] };

        Assert.Contains(
            PairedMeasurementSessionRules.Validate(changed),
            error => error.Contains("eindeutige positive Reihenfolgenummern"));
    }

    [Fact]
    public void UnsupportedProtocolVersionIsRejected()
    {
        var session = CreateSession() with { ProtocolVersion = MeasurementProtocol.CurrentVersion + 1 };

        Assert.Contains(
            PairedMeasurementSessionRules.Validate(session),
            error => error.Contains("Protokollversion"));
    }

    [Fact]
    public void CurrentContractRequiresDocumentedHearingAidProgramAndVolume()
    {
        var session = CreateSession() with
        {
            HearingAid = CreateHearingAid() with { ProgramName = null, VolumeState = null },
            Contract = MeasurementSessionContracts.PhonemeContrastMeasurement
        };

        Assert.Contains(
            PairedMeasurementSessionRules.Validate(session),
            error => error.Contains("Programm und Lautstärkezustand"));
    }

    [Fact]
    public void MissingCurrentContractAndMaterialIdentityAreRejected()
    {
        var session = CreateSession() with { Contract = null!, MaterialIdentity = null! };

        var errors = PairedMeasurementSessionRules.Validate(session);
        Assert.Contains(errors, error => error.Contains("Messvertrag fehlt"));
        Assert.Contains(errors, error => error.Contains("Materialidentität fehlt"));
    }

    [Fact]
    public void CardinalNoiseResponseRequiresTheMaterialBoundApprovedProfile()
    {
        const string materialId = "de-DE-personal-cardinal-numbers-christoph-v1";
        var catalogHash = new string('a', 64);
        var audioIndexHash = new string('b', 64);
        var identity = StimulusMaterialIdentityFactory.Create(materialId, "1.0.0", catalogHash, audioIndexHash);
        var session = PairedMeasurementSessionFactory.CreateRandomized(
            TestedEar.Left,
            CreateHearingAid(),
            SpeechMaterial.Numbers,
            ListeningEnvironment.BackgroundNoise,
            "cardinal-list-01",
            "cardinal-list-02",
            CreateHardware(),
            DateTimeOffset.Parse("2026-09-19T08:00:00Z"),
            20,
            identity,
            MeasurementSessionContracts.CardinalNumberMeasurement);
        var capturedAt = DateTimeOffset.Parse("2026-09-19T08:01:00Z");
        var wrongPresentation = new StimulusPresentationRecord(
            materialId,
            "1.0.0",
            "cardinal-123",
            new string('c', 64),
            session.Hardware.EndpointId,
            session.Hardware.EndpointName,
            new StimulusRenderMetadata(
                StimulusAudioRenderer.RendererVersion,
                StimulusAudioRenderer.NoiseAlgorithm,
                StimulusAudioRenderer.SourcePeakNormalizationDbfs,
                -60m,
                5m,
                42,
                48000,
                0.1f),
            capturedAt,
            capturedAt.AddSeconds(1));
        var firstBlock = session.Blocks[0] with
        {
            RawResponses = [new RawMeasurementResponse(1, "cardinal-123", "123", capturedAt, wrongPresentation)]
        };

        var errors = PairedMeasurementSessionRules.Validate(session with
        {
            Blocks = [firstBlock, session.Blocks[1]]
        });

        Assert.Contains(errors, error => error.Contains("materialgebundene Kardinalzahl-Rauschprofil"));
    }

    private static PairedMeasurementSession CreateSession(int randomizationSeed = 20) =>
        PairedMeasurementSessionFactory.CreateRandomized(
            TestedEar.Left,
            CreateHearingAid(),
            SpeechMaterial.Monosyllables,
            ListeningEnvironment.Quiet,
            "mono-list-a",
            "mono-list-b",
            CreateHardware(),
            DateTimeOffset.Parse("2026-09-01T08:00:00Z"),
            randomizationSeed,
            CreateMaterialIdentity(),
            MeasurementSessionContracts.PhonemeContrastMeasurement);

    private static HearingAidSnapshot CreateHearingAid() => new(
        Guid.Parse("3a80fa53-9658-4a71-8936-86d67f91e688"),
        "Signia",
        "Pure C&G BCT 2IX",
        "Signia links",
        TestedEar.Left,
        "Programm 1",
        "0");

    private static StimulusMaterialIdentity CreateMaterialIdentity() =>
        StimulusMaterialIdentityFactory.Create("catalog", "1", new string('a', 64), new string('b', 64));

    private static MeasurementHardwareSnapshot CreateHardware() => new(
        Guid.Parse("84452115-0de9-489b-acd8-df0178db080d"),
        "Test-DAC + Testkopfhörer",
        "endpoint-topping",
        "Lautsprecher (TOPPING USB DAC)",
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
        DateTimeOffset.Parse("2026-09-01T07:30:00Z"));
}
