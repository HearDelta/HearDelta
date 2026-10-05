namespace HearDelta.Core;

public sealed record PhonemeConfusionCell(
    HearingAidCondition Condition,
    string ContrastGroupId,
    string ContrastPosition,
    string TargetResponse,
    string SelectedResponse,
    int Count);

public sealed record PhonemeConfusionSummary(
    HearingAidCondition Condition,
    int TotalResponses,
    int CorrectResponses,
    int NotUnderstoodResponses,
    IReadOnlyList<PhonemeConfusionCell> Cells);

public static class PhonemeConfusionAnalysis
{
    public const string NotUnderstood = "Nicht verstanden";
    public const string OtherResponse = "Andere Antwort";

    public static PhonemeConfusionSummary Analyze(MeasurementBlockResult block)
    {
        ArgumentNullException.ThrowIfNull(block);
        var cells = block.Responses
            .Where(response => !string.IsNullOrWhiteSpace(response.ContrastGroupId) &&
                               !string.IsNullOrWhiteSpace(response.ContrastPosition))
            .GroupBy(response => new
            {
                response.ContrastGroupId,
                response.ContrastPosition,
                response.CanonicalResponse,
                SelectedResponse = ClassifySelectedResponse(response)
            })
            .Select(group => new PhonemeConfusionCell(
                block.Condition,
                group.Key.ContrastGroupId!,
                group.Key.ContrastPosition!,
                group.Key.CanonicalResponse,
                group.Key.SelectedResponse,
                group.Count()))
            .OrderBy(cell => cell.ContrastPosition, StringComparer.Ordinal)
            .ThenBy(cell => cell.ContrastGroupId, StringComparer.Ordinal)
            .ThenBy(cell => cell.TargetResponse, StringComparer.OrdinalIgnoreCase)
            .ThenBy(cell => cell.SelectedResponse, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return new PhonemeConfusionSummary(
            block.Condition,
            block.TotalResponses,
            block.CorrectResponses,
            block.Responses.Count(response => response.EnteredText is null),
            cells);
    }

    private static string ClassifySelectedResponse(ScoredMeasurementResponse response) =>
        response.EnteredText is null ? NotUnderstood :
        string.Equals(response.EnteredText.Trim(), response.CanonicalResponse.Trim(), StringComparison.OrdinalIgnoreCase)
            ? response.CanonicalResponse
            : string.IsNullOrWhiteSpace(response.EnteredText) ? OtherResponse : response.EnteredText.Trim();
}
