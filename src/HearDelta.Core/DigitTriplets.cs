namespace HearDelta.Core;

/// <summary>
/// Immutable protocol rules for the experimental digit-triplet module.
/// This describes digital material only; it does not claim equivalence to a
/// clinical digit-triplet test or define an acoustic level.
/// </summary>
public static class DigitTripletProtocol
{
    public const string ModuleId = "digit-triplet";
    public const int DigitCount = 3;
    public const int MinimumDigit = 0;
    public const int MaximumDigit = 9;
    public const decimal WholeTripletChanceLevelPercent = 0.1m;
    public const bool RepeatedDigitsAllowed = true;
}

public sealed record DigitTripletDefinition(string Id, IReadOnlyList<int> Digits);

public sealed record DigitTripletScore(
    IReadOnlyList<bool> DigitCorrectness,
    bool IsWholeTripletCorrect);

public static class DigitTripletRules
{
    public static IReadOnlyList<string> Validate(DigitTripletDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(definition.Id))
            errors.Add("Ein Zifferntripel benötigt eine ID.");
        if (definition.Digits is null || definition.Digits.Count != DigitTripletProtocol.DigitCount)
        {
            errors.Add("Ein Zifferntripel besteht aus genau drei Ziffern.");
            return errors;
        }

        if (definition.Digits.Any(digit => digit is < DigitTripletProtocol.MinimumDigit or > DigitTripletProtocol.MaximumDigit))
            errors.Add("Ein Zifferntripel darf nur Ziffern von 0 bis 9 enthalten.");
        if (!DigitTripletProtocol.RepeatedDigitsAllowed && definition.Digits.Distinct().Count() != definition.Digits.Count)
            errors.Add("Ein Zifferntripel darf keine Wiederholung enthalten.");
        return errors;
    }
}

public static class DigitTripletScoring
{
    /// <summary>
    /// Scores a response positionally. The experimental SNR module uses only
    /// the whole-triplet result as its success event; per-digit results are
    /// retained for later descriptive analysis.
    /// </summary>
    public static DigitTripletScore Score(
        DigitTripletDefinition target,
        IReadOnlyList<int>? enteredDigits)
    {
        ArgumentNullException.ThrowIfNull(target);
        var errors = DigitTripletRules.Validate(target);
        if (errors.Count > 0)
            throw new ArgumentException(string.Join(" ", errors), nameof(target));

        var correctness = Enumerable.Range(0, DigitTripletProtocol.DigitCount)
            .Select(index => enteredDigits is not null &&
                             enteredDigits.Count == DigitTripletProtocol.DigitCount &&
                             enteredDigits[index] == target.Digits[index])
            .ToArray();
        return new DigitTripletScore(correctness, correctness.All(correct => correct));
    }
}
