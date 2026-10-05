namespace HearDelta.Core;

public sealed record ListeningSetting(
    Guid Id,
    Guid PersonId,
    int Revision,
    string Name,
    TestedEar Ear,
    HearingAidCondition Condition,
    Guid? HearingAidId,
    string? ProgramName,
    string? VolumeState,
    string? WearingNotes,
    DateTimeOffset? ArchivedAt = null)
{
    public ListeningSettingSnapshot CreateSnapshot(PersonHearingAid? hearingAid)
    {
        var errors = ListeningSettingRules.Validate(this, hearingAid);
        if (errors.Count > 0)
            throw new ArgumentException(string.Join(" ", errors), nameof(hearingAid));

        return new ListeningSettingSnapshot(
            Id,
            PersonId,
            Revision,
            Name.Trim(),
            Ear,
            Condition,
            hearingAid?.CreateSnapshot(ProgramName, VolumeState),
            NormalizeOptional(ProgramName),
            NormalizeOptional(VolumeState),
            NormalizeOptional(WearingNotes));
    }

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

public sealed record ListeningSettingSnapshot(
    Guid SettingId,
    Guid PersonId,
    int Revision,
    string Name,
    TestedEar Ear,
    HearingAidCondition Condition,
    HearingAidSnapshot? HearingAid,
    string? ProgramName,
    string? VolumeState,
    string? WearingNotes);

public static class ListeningSettingRules
{
    public static IReadOnlyList<string> Validate(ListeningSetting setting, PersonHearingAid? hearingAid)
    {
        ArgumentNullException.ThrowIfNull(setting);
        var errors = new List<string>();
        if (setting.Id == Guid.Empty || setting.PersonId == Guid.Empty)
            errors.Add("Das Setting benötigt eine Setting- und Personen-ID.");
        if (setting.Revision <= 0)
            errors.Add("Die Setting-Revision muss positiv sein.");
        if (string.IsNullOrWhiteSpace(setting.Name))
            errors.Add("Das Setting benötigt einen Namen.");
        if (!Enum.IsDefined(setting.Ear) || !Enum.IsDefined(setting.Condition))
            errors.Add("Ohr oder Hörgerätebedingung des Settings ist ungültig.");

        if (setting.Condition == HearingAidCondition.WithoutHearingAid)
        {
            if (setting.HearingAidId is not null || hearingAid is not null ||
                !string.IsNullOrWhiteSpace(setting.ProgramName) ||
                !string.IsNullOrWhiteSpace(setting.VolumeState))
                errors.Add("Ein Setting ohne Hörgerät darf kein Gerät, Programm oder Lautstärkezustand enthalten.");
        }
        else if (setting.Condition == HearingAidCondition.WithHearingAid)
        {
            if (setting.HearingAidId is null || hearingAid is null)
                errors.Add("Ein Setting mit Hörgerät benötigt das zugeordnete Gerät.");
            else
            {
                if (setting.HearingAidId != hearingAid.Id)
                    errors.Add("Das angegebene Hörgerät passt nicht zur Geräte-ID des Settings.");
                if (setting.PersonId != hearingAid.PersonId)
                    errors.Add("Setting und Hörgerät müssen derselben Person gehören.");
                if (setting.Ear != hearingAid.Ear)
                    errors.Add("Setting und Hörgerät müssen dasselbe Ohr verwenden.");
                errors.AddRange(PersonProfileRules.Validate(hearingAid));
            }
        }

        return errors;
    }

    public static IReadOnlyList<string> ValidateSnapshot(ListeningSettingSnapshot snapshot, Guid personId)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var errors = new List<string>();
        if (snapshot.PersonId != personId)
            errors.Add("Der Setting-Snapshot gehört zu einer anderen Person.");
        if (snapshot.SettingId == Guid.Empty || snapshot.Revision <= 0 || string.IsNullOrWhiteSpace(snapshot.Name))
            errors.Add("Der Setting-Snapshot ist unvollständig.");
        if (snapshot.Condition == HearingAidCondition.WithoutHearingAid &&
            (snapshot.HearingAid is not null || snapshot.ProgramName is not null || snapshot.VolumeState is not null))
            errors.Add("Ein Snapshot ohne Hörgerät darf keine Gerätedaten enthalten.");
        if (snapshot.Condition == HearingAidCondition.WithHearingAid && snapshot.HearingAid is null)
            errors.Add("Ein Snapshot mit Hörgerät benötigt unveränderliche Gerätedaten.");
        if (snapshot.HearingAid is { } aid && aid.FittedEar != snapshot.Ear)
            errors.Add("Hörgerät und Setting-Snapshot müssen dasselbe Ohr verwenden.");
        return errors;
    }
}
