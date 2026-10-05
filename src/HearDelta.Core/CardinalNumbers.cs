namespace HearDelta.Core;

/// <summary>
/// Contract for the personal, non-clinical German cardinal-number material.
/// It deliberately excludes leading-zero values: a spoken cardinal number
/// cannot distinguish them from its shorter written representation.
/// </summary>
public static class CardinalNumberProtocol
{
    public const string Paradigm = "open-set-cardinal-number";
    public const int MinimumValue = 100;
    public const int MaximumValue = 999;
    public const int ListCount = 36;
    public const int ItemsPerList = 25;
    public const decimal ChanceLevelPercent = 100m / 900m;

    public static bool IsCanonicalResponse(string? response) =>
        int.TryParse(response, out var value) &&
        value is >= MinimumValue and <= MaximumValue &&
        response!.Length == 3;
}
