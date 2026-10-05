using HearDelta.Core;

namespace HearDelta.Core.Tests;

public sealed class MeasurementProfileRulesTests
{
    [Fact]
    public void MissingStoredEndpointIsNotTreatedAsAvailable()
    {
        var profile = CreateProfile(endpointId: "endpoint-topping");
        var endpoints = new[] { new AudioEndpointDescriptor("windows-default", "Standardgerät", 2, 48000, 24) };

        Assert.False(MeasurementProfileRules.IsEndpointAvailable(profile, endpoints));
    }

    [Fact]
    public void SnapshotRemainsUnchangedWhenProfileIsEditedLater()
    {
        var profile = CreateProfile(endpointId: "endpoint-topping");
        var snapshot = profile.CreateSnapshot();
        var edited = profile with { Gain = "High (+19 dB)", UpdatedAt = profile.UpdatedAt.AddDays(1) };

        Assert.Equal("Low (+6 dB)", snapshot.Gain);
        Assert.Equal("High (+19 dB)", edited.Gain);
    }

    [Fact]
    public void DifferentHeadphonesAreNotDirectlyComparable()
    {
        var first = CreateProfile("endpoint-topping").CreateSnapshot();
        var second = CreateProfile("endpoint-topping") with
        {
            Headphone = new HeadphoneProfile(Guid.NewGuid(), "Beyerdynamic", "DT 770 Pro", "Geschlossen", 250)
        };

        Assert.False(MeasurementProfileRules.IsDirectlyComparable(first, second.CreateSnapshot()));
    }

    [Fact]
    public void StartVolumeAboveMaximumIsRejected()
    {
        var profile = CreateProfile("endpoint-topping") with { StartVolumeDb = -20m, MaximumVolumeDb = -30m };

        Assert.Contains(MeasurementProfileRules.Validate(profile), error => error.Contains("Obergrenze"));
    }

    [Fact]
    public void PositiveDigitalMaximumIsRejected()
    {
        var profile = CreateProfile("endpoint-topping") with { MaximumVolumeDb = 1m };

        Assert.Contains(MeasurementProfileRules.Validate(profile), error => error.Contains("0 dB"));
    }

    [Fact]
    public void ChangedEndpointFormatBlocksStoredHardwareSnapshot()
    {
        var hardware = CreateProfile("endpoint-topping").CreateSnapshot();
        var activeEndpoints = new[]
        {
            new AudioEndpointDescriptor("endpoint-topping", "Lautsprecher (TOPPING USB DAC)", 2, 44100, 24)
        };

        Assert.Contains(
            AudioEndpointBindingRules.Validate(hardware, activeEndpoints),
            error => error.Contains("Abtastrate"));
    }

    private static MeasurementProfile CreateProfile(string endpointId) => new(
        Guid.NewGuid(),
        "Test-DAC + Testkopfhörer",
        endpointId,
        "Lautsprecher (TOPPING USB DAC)",
        true,
        48000,
        24,
        2,
        new HeadphoneProfile(Guid.NewGuid(), "Testhersteller", "Testmodell", "Ohrumschließend", 300),
        "Vordere 3,5-mm-Klinke",
        "Low (+6 dB)",
        -60m,
        -30m,
        false,
        DateTimeOffset.UtcNow);
}
