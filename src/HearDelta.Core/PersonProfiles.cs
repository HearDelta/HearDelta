namespace HearDelta.Core;

public sealed record PersonProfile(
    Guid Id,
    string DisplayName,
    string? Notes,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? ArchivedAt = null,
    DateOnly? DateOfBirth = null);

public sealed record PersonTestPreferences(
    Guid PersonId,
    Guid? PreferredHardwareProfileId,
    Guid? DefaultTemplateId);

public sealed record PersonHearingAid(
    Guid Id,
    Guid PersonId,
    string Manufacturer,
    string Model,
    string DisplayName,
    TestedEar Ear,
    DateTimeOffset? ArchivedAt = null)
{
    public HearingAidSnapshot CreateSnapshot(string? programName, string? volumeState) => new(
        Id,
        Manufacturer.Trim(),
        Model.Trim(),
        DisplayName.Trim(),
        Ear,
        NormalizeOptional(programName),
        NormalizeOptional(volumeState));

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

public static class PersonProfileRules
{
    public static IReadOnlyList<string> Validate(PersonProfile person)
    {
        ArgumentNullException.ThrowIfNull(person);
        var errors = new List<string>();
        var displayName = person.DisplayName?.Trim() ?? string.Empty;
        if (person.Id == Guid.Empty)
            errors.Add("Die Person benötigt eine ID.");
        if (displayName.Length is < 1 or > 100)
            errors.Add("Der Anzeigename muss nach dem Kürzen 1 bis 100 Zeichen enthalten.");
        if (person.Notes?.Length > 2_000)
            errors.Add("Die optionale Notiz darf höchstens 2.000 Zeichen enthalten.");
        if (person.CreatedAt == default || person.UpdatedAt < person.CreatedAt)
            errors.Add("Erstellungs- und Änderungszeitpunkt der Person sind ungültig.");
        if (person.ArchivedAt is { } archivedAt && archivedAt < person.CreatedAt)
            errors.Add("Der Archivierungszeitpunkt darf nicht vor der Erstellung liegen.");
        return errors;
    }

    public static IReadOnlyList<string> Validate(PersonHearingAid hearingAid)
    {
        ArgumentNullException.ThrowIfNull(hearingAid);
        var errors = new List<string>();
        if (hearingAid.Id == Guid.Empty || hearingAid.PersonId == Guid.Empty)
            errors.Add("Das Hörgerät benötigt eine Geräte- und Personen-ID.");
        if (string.IsNullOrWhiteSpace(hearingAid.Manufacturer) ||
            string.IsNullOrWhiteSpace(hearingAid.Model) ||
            string.IsNullOrWhiteSpace(hearingAid.DisplayName))
            errors.Add("Hersteller, Modell und Anzeigename des Hörgeräts sind erforderlich.");
        if (!Enum.IsDefined(hearingAid.Ear))
            errors.Add("Das Ohr des Hörgeräts ist ungültig.");
        return errors;
    }
}
