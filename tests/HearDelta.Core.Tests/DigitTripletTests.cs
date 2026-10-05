using HearDelta.Core;

namespace HearDelta.Core.Tests;

public sealed class DigitTripletTests
{
    [Fact]
    public void RepeatedDigitsAreAllowedInTheFrozenCandidateInventory()
    {
        var triplet = new DigitTripletDefinition("candidate-001", [4, 4, 0]);

        Assert.Empty(DigitTripletRules.Validate(triplet));
        Assert.True(DigitTripletProtocol.RepeatedDigitsAllowed);
        Assert.Equal(0.1m, DigitTripletProtocol.WholeTripletChanceLevelPercent);
    }

    [Fact]
    public void TripletRequiresExactlyThreeDecimalDigits()
    {
        var tooShort = new DigitTripletDefinition("candidate-001", [1, 2]);
        var outsideInventory = new DigitTripletDefinition("candidate-002", [1, 2, 10]);

        Assert.Contains(DigitTripletRules.Validate(tooShort), error => error.Contains("genau drei"));
        Assert.Contains(DigitTripletRules.Validate(outsideInventory), error => error.Contains("0 bis 9"));
    }

    [Fact]
    public void WholeTripletIsCorrectOnlyWhenEveryPositionMatches()
    {
        var target = new DigitTripletDefinition("candidate-001", [4, 4, 0]);

        var score = DigitTripletScoring.Score(target, [4, 7, 0]);

        Assert.Equal([true, false, true], score.DigitCorrectness);
        Assert.False(score.IsWholeTripletCorrect);
    }

    [Fact]
    public void IncompleteResponseIsNotCountedAsAWholeTripletSuccess()
    {
        var target = new DigitTripletDefinition("candidate-001", [4, 4, 0]);

        var score = DigitTripletScoring.Score(target, [4, 4]);

        Assert.Equal([false, false, false], score.DigitCorrectness);
        Assert.False(score.IsWholeTripletCorrect);
    }
}
