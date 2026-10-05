using System.Security.Cryptography;

namespace HearDelta.Core;

/// <summary>Separate free-text exercise; deliberately not a paired measurement.</summary>
public sealed record PracticeResponse(
    int PresentationOrder,
    string StimulusId,
    string EnteredText,
    DateTimeOffset RecordedAt,
    StimulusPresentationRecord? Presentation = null);

public sealed record PracticeSession(
    int Version,
    Guid Id,
    TestedEar Ear,
    SpeechMaterial Material,
    string StimulusListId,
    int RandomizationSeed,
    MeasurementHardwareSnapshot Hardware,
    StimulusMaterialIdentity MaterialIdentity,
    DateTimeOffset StartedAt,
    DateTimeOffset? CompletedAt,
    DateTimeOffset? AbortedAt,
    IReadOnlyList<PracticeResponse> Responses);

public static class PracticeSessionFactory
{
    public static PracticeSession Create(
        TestedEar ear,
        SpeechMaterial material,
        string stimulusListId,
        MeasurementHardwareSnapshot hardware,
        StimulusMaterialIdentity materialIdentity,
        DateTimeOffset startedAt,
        int? randomizationSeed = null) => new(
            1, Guid.NewGuid(), ear, material, stimulusListId,
            randomizationSeed ?? RandomNumberGenerator.GetInt32(int.MaxValue), hardware,
            materialIdentity, startedAt, null, null, []);
}

public static class PracticeSessionRules
{
    public static IReadOnlyList<string> Validate(PracticeSession session)
    {
        var errors = new List<string>();
        if (session.Version != 1 || session.Id == Guid.Empty || session.StartedAt == default)
            errors.Add("Das Übungsprotokoll ist unvollständig oder wird nicht unterstützt.");
        if (!Enum.IsDefined(session.Ear) || !Enum.IsDefined(session.Material) || string.IsNullOrWhiteSpace(session.StimulusListId))
            errors.Add("Übungsohr, Material oder Stimulusliste fehlen.");
        if (session.MaterialIdentity is null || string.IsNullOrWhiteSpace(session.MaterialIdentity.FingerprintSha256))
            errors.Add("Die Übung benötigt eine Materialidentität.");
        if (session.AbortedAt is not null && session.CompletedAt != session.AbortedAt)
            errors.Add("Eine abgebrochene Übung benötigt denselben Abbruch- und Abschlusszeitpunkt.");
        return errors;
    }
}
