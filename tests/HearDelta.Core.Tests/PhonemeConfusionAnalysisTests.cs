using HearDelta.Core;

namespace HearDelta.Core.Tests;

public sealed class PhonemeConfusionAnalysisTests
{
    [Fact]
    public void AnalysisKeepsTargetChoiceNotUnderstoodAndDenominatorsSeparate()
    {
        var block = new MeasurementBlockResult(
            HearingAidCondition.WithoutHearingAid,
            "list-a",
            2,
            4,
            25m,
            [
                Response(1, "Ross", "Ross", true, "vowel-04", "vowel"),
                Response(2, "Ross", "Ruß", false, "vowel-04", "vowel"),
                Response(3, "Ross", null, false, "vowel-04", "vowel"),
                Response(4, "Bett", "Bett", true, null, null)
            ]);

        var summary = PhonemeConfusionAnalysis.Analyze(block);

        Assert.Equal(4, summary.TotalResponses);
        Assert.Equal(2, summary.CorrectResponses);
        Assert.Equal(1, summary.NotUnderstoodResponses);
        Assert.Equal(3, summary.Cells.Sum(cell => cell.Count));
        Assert.Contains(summary.Cells, cell => cell.TargetResponse == "Ross" && cell.SelectedResponse == "Ross" && cell.Count == 1);
        Assert.Contains(summary.Cells, cell => cell.TargetResponse == "Ross" && cell.SelectedResponse == "Ruß" && cell.Count == 1);
        Assert.Contains(summary.Cells, cell => cell.SelectedResponse == PhonemeConfusionAnalysis.NotUnderstood && cell.Count == 1);
    }

    private static ScoredMeasurementResponse Response(
        int order, string target, string? selected, bool correct, string? group, string? position) => new(
        order, $"stimulus-{order}", target, selected, correct, group, position, "broad");
}
