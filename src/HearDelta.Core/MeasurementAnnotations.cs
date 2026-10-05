namespace HearDelta.Core;

/// <summary>
/// Nachträglich änderbarer Name und Kommentar einer Messung (Worttest oder Hörschwellentest).
/// Liegt bewusst außerhalb des unveränderlichen Messprotokolls.
/// </summary>
public sealed record MeasurementAnnotation(
    Guid MeasurementId,
    string Name,
    string? Comment,
    DateTimeOffset UpdatedAt);

public static class MeasurementAnnotationRules
{
    public const int MaximumNameLength = 120;
    public const int MaximumCommentLength = 4_000;
    public const string WithoutHearingAidName = "Ohne Hörgerät";

    /// <summary>Vorgabename einer Messung: das verwendete Hörgerät, sonst „Ohne Hörgerät“.</summary>
    public static string DefaultName(string? hearingAidDisplayName) =>
        string.IsNullOrWhiteSpace(hearingAidDisplayName) ? WithoutHearingAidName : hearingAidDisplayName.Trim();

    public static string DefaultName(HearingAidSnapshot? hearingAid) => DefaultName(hearingAid?.DisplayName);

    /// <summary>Kürzt Name und Kommentar; ein leerer Kommentar wird zu <c>null</c>.</summary>
    public static MeasurementAnnotation Create(Guid measurementId, string? name, string? comment, DateTimeOffset updatedAt) => new(
        measurementId,
        name?.Trim() ?? string.Empty,
        string.IsNullOrWhiteSpace(comment) ? null : comment.Trim(),
        updatedAt);

    public static IReadOnlyList<string> Validate(MeasurementAnnotation annotation)
    {
        ArgumentNullException.ThrowIfNull(annotation);
        var errors = new List<string>();
        if (annotation.MeasurementId == Guid.Empty)
            errors.Add("Name und Kommentar benötigen die ID der Messung.");
        var name = annotation.Name?.Trim() ?? string.Empty;
        if (name.Length is < 1 or > MaximumNameLength)
            errors.Add(string.Format(CoreStrings.Annotation_NameLength, MaximumNameLength));
        if (annotation.Comment?.Length > MaximumCommentLength)
            errors.Add(CoreStrings.Annotation_CommentLength);
        if (annotation.UpdatedAt == default)
            errors.Add("Der Änderungszeitpunkt von Name und Kommentar fehlt.");
        return errors;
    }
}
