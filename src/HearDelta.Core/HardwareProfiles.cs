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
        if (string.IsNullOrWhiteSpace(profile.Name)) errors.Add("Das Messprofil benötigt einen Namen.");
        if (string.IsNullOrWhiteSpace(profile.EndpointId)) errors.Add("Ein erkannter Audioausgang muss ausgewählt werden.");
        if (profile.Channels < 2) errors.Add("Der Audioausgang muss mindestens zwei Kanäle bereitstellen.");
        if (profile.SampleRate <= 0) errors.Add("Die Abtastrate ist ungültig.");
        if (profile.BitsPerSample <= 0) errors.Add("Die Bittiefe ist ungültig.");
        if (string.IsNullOrWhiteSpace(profile.Headphone.Manufacturer)) errors.Add("Der Kopfhörerhersteller fehlt.");
        if (string.IsNullOrWhiteSpace(profile.Headphone.Model)) errors.Add("Das Kopfhörermodell fehlt.");
        if (profile.MaximumVolumeDb > 0) errors.Add("Die digitale Pegelobergrenze darf 0 dB nicht überschreiten.");
        if (profile.StartVolumeDb > profile.MaximumVolumeDb) errors.Add("Die Startlautstärke darf die Obergrenze nicht überschreiten.");
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
            differences.Add("Der gespeicherte WASAPI-Ausgang ist unterschiedlich.");
        if (left.ExclusiveMode != right.ExclusiveMode)
            differences.Add("Der WASAPI-Ausgabemodus ist unterschiedlich.");
        if (left.SampleRate != right.SampleRate ||
            left.BitsPerSample != right.BitsPerSample ||
            left.Channels != right.Channels)
            differences.Add("Das gespeicherte Audioformat ist unterschiedlich.");
        if (!SameText(left.HeadphoneManufacturer, right.HeadphoneManufacturer) ||
            !SameText(left.HeadphoneModel, right.HeadphoneModel) ||
            !SameText(left.HeadphoneDesign, right.HeadphoneDesign) ||
            left.HeadphoneImpedanceOhms != right.HeadphoneImpedanceOhms)
            differences.Add("Der Kopfhörer-Snapshot ist unterschiedlich.");
        if (!string.Equals(left.HeadphoneEqualization?.SourceSha256, right.HeadphoneEqualization?.SourceSha256, StringComparison.Ordinal))
            differences.Add("Die Kopfhörerentzerrung ist unterschiedlich.");
        if (!SameText(left.AmplifierOutput, right.AmplifierOutput))
            differences.Add("Der Verstärkerausgang ist unterschiedlich.");
        if (!SameText(left.Gain, right.Gain))
            differences.Add("Die Gain-Einstellung ist unterschiedlich.");
        if (left.StartVolumeDb != right.StartVolumeDb || left.MaximumVolumeDb != right.MaximumVolumeDb)
            differences.Add("Digitaler Messpegel oder Pegelobergrenze sind unterschiedlich.");
        if (left.IsCouplerCalibrated != right.IsCouplerCalibrated)
            differences.Add("Der dokumentierte Kalibrierstatus ist unterschiedlich.");
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
            return ["Der gespeicherte Audioausgang ist nicht verfügbar. Es wurde kein Ersatzgerät gewählt."];

        var errors = new List<string>();
        if (endpoint.Channels != hardware.Channels)
            errors.Add("Die Kanalzahl des gespeicherten Audioausgangs hat sich geändert.");
        if (endpoint.SampleRate != hardware.SampleRate)
            errors.Add("Die Abtastrate des gespeicherten Audioausgangs hat sich geändert.");
        if (endpoint.BitsPerSample != hardware.BitsPerSample)
            errors.Add("Die Bittiefe des gespeicherten Audioausgangs hat sich geändert.");
        return errors;
    }
}
