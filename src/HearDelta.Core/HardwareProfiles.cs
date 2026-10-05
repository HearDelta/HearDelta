namespace HearDelta.Core;

public sealed record AudioEndpointDescriptor(
    string Id,
    string Name,
    int Channels,
    int SampleRate,
    int BitsPerSample);

public sealed record HeadphoneProfile(
    Guid Id,
    string Manufacturer,
    string Model,
    string Design,
    int? ImpedanceOhms,
    HeadphoneEqualization? Equalization = null);

public sealed record MeasurementProfile(
    Guid Id,
    string Name,
    string EndpointId,
    string EndpointName,
    bool ExclusiveMode,
    int SampleRate,
    int BitsPerSample,
    int Channels,
    HeadphoneProfile Headphone,
    string AmplifierOutput,
    string Gain,
    decimal StartVolumeDb,
    decimal MaximumVolumeDb,
    bool IsCouplerCalibrated,
    DateTimeOffset UpdatedAt)
{
    public MeasurementHardwareSnapshot CreateSnapshot() => new(
        Id,
        Name,
        EndpointId,
        EndpointName,
        ExclusiveMode,
        SampleRate,
        BitsPerSample,
        Channels,
        Headphone.Manufacturer,
        Headphone.Model,
        Headphone.Design,
        Headphone.ImpedanceOhms,
        AmplifierOutput,
        Gain,
        StartVolumeDb,
        MaximumVolumeDb,
        IsCouplerCalibrated,
        UpdatedAt,
        Headphone.Equalization);
}

public sealed record MeasurementHardwareSnapshot(
    Guid ProfileId,
    string ProfileName,
    string EndpointId,
    string EndpointName,
    bool ExclusiveMode,
    int SampleRate,
    int BitsPerSample,
    int Channels,
    string HeadphoneManufacturer,
    string HeadphoneModel,
    string HeadphoneDesign,
    int? HeadphoneImpedanceOhms,
    string AmplifierOutput,
    string Gain,
    decimal StartVolumeDb,
    decimal MaximumVolumeDb,
    bool IsCouplerCalibrated,
    DateTimeOffset ProfileUpdatedAt,
    HeadphoneEqualization? HeadphoneEqualization = null);

public static class MeasurementProfileRules
{
    public static IReadOnlyList<string> Validate(MeasurementProfile profile)
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(profile.Name)) errors.Add(CoreStrings.Profile_NameRequired);
        if (string.IsNullOrWhiteSpace(profile.EndpointId)) errors.Add(CoreStrings.Profile_OutputRequired);
        if (profile.Channels < 2) errors.Add(CoreStrings.Profile_TwoChannels);
        if (profile.SampleRate <= 0) errors.Add(CoreStrings.Profile_SampleRateInvalid);
        if (profile.BitsPerSample <= 0) errors.Add(CoreStrings.Profile_BitDepthInvalid);
        if (string.IsNullOrWhiteSpace(profile.Headphone.Manufacturer)) errors.Add(CoreStrings.Profile_HeadphoneManufacturerMissing);
        if (string.IsNullOrWhiteSpace(profile.Headphone.Model)) errors.Add(CoreStrings.Profile_HeadphoneModelMissing);
        if (profile.MaximumVolumeDb > 0) errors.Add(CoreStrings.Profile_MaximumAboveZero);
        if (profile.StartVolumeDb > profile.MaximumVolumeDb) errors.Add(CoreStrings.Profile_StartAboveMaximum);
        if (profile.Headphone.Equalization is { } equalization)
            errors.AddRange(HeadphoneEqualizer.Validate(equalization, profile.SampleRate));
        return errors;
    }

    public static bool IsEndpointAvailable(MeasurementProfile profile, IEnumerable<AudioEndpointDescriptor> endpoints) =>
        endpoints.Any(endpoint => string.Equals(endpoint.Id, profile.EndpointId, StringComparison.Ordinal));

    public static bool IsDirectlyComparable(MeasurementHardwareSnapshot left, MeasurementHardwareSnapshot right) =>
        GetComparisonDifferences(left, right).Count == 0;

    public static IReadOnlyList<string> GetComparisonDifferences(
        MeasurementHardwareSnapshot left,
        MeasurementHardwareSnapshot right)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);

        var differences = new List<string>();
        if (!string.Equals(left.EndpointId, right.EndpointId, StringComparison.Ordinal))
            differences.Add(CoreStrings.Profile_DiffEndpoint);
        if (left.ExclusiveMode != right.ExclusiveMode)
            differences.Add(CoreStrings.Profile_DiffMode);
        if (left.SampleRate != right.SampleRate ||
            left.BitsPerSample != right.BitsPerSample ||
            left.Channels != right.Channels)
            differences.Add(CoreStrings.Profile_DiffFormat);
        if (!SameText(left.HeadphoneManufacturer, right.HeadphoneManufacturer) ||
            !SameText(left.HeadphoneModel, right.HeadphoneModel) ||
            !SameText(left.HeadphoneDesign, right.HeadphoneDesign) ||
            left.HeadphoneImpedanceOhms != right.HeadphoneImpedanceOhms)
            differences.Add(CoreStrings.Profile_DiffHeadphone);
        if (!string.Equals(left.HeadphoneEqualization?.SourceSha256, right.HeadphoneEqualization?.SourceSha256, StringComparison.Ordinal))
            differences.Add(CoreStrings.Profile_DiffEqualization);
        if (!SameText(left.AmplifierOutput, right.AmplifierOutput))
            differences.Add(CoreStrings.Profile_DiffAmplifier);
        if (!SameText(left.Gain, right.Gain))
            differences.Add(CoreStrings.Profile_DiffGain);
        if (left.StartVolumeDb != right.StartVolumeDb || left.MaximumVolumeDb != right.MaximumVolumeDb)
            differences.Add(CoreStrings.Profile_DiffLevel);
        if (left.IsCouplerCalibrated != right.IsCouplerCalibrated)
            differences.Add(CoreStrings.Profile_DiffCalibration);
        return differences;
    }

    private static bool SameText(string left, string right) =>
        string.Equals(left.Trim(), right.Trim(), StringComparison.OrdinalIgnoreCase);
}

public static class AudioEndpointBindingRules
{
    public static IReadOnlyList<string> Validate(
        MeasurementHardwareSnapshot hardware,
        IEnumerable<AudioEndpointDescriptor> activeEndpoints)
    {
        var endpoint = activeEndpoints.SingleOrDefault(candidate =>
            string.Equals(candidate.Id, hardware.EndpointId, StringComparison.Ordinal));
        if (endpoint is null)
            return [CoreStrings.Profile_EndpointUnavailable];

        var errors = new List<string>();
        if (endpoint.Channels != hardware.Channels)
            errors.Add(CoreStrings.Profile_ChannelsChanged);
        if (endpoint.SampleRate != hardware.SampleRate)
            errors.Add(CoreStrings.Profile_SampleRateChanged);
        if (endpoint.BitsPerSample != hardware.BitsPerSample)
            errors.Add(CoreStrings.Profile_BitDepthChanged);
        return errors;
    }
}
