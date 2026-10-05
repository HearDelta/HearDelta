using HearDelta.Core;

namespace HearDelta.App.ViewModels;

/// <summary>Bezeichnungen und Erklärungen der Schritte des empfohlenen Ablaufs.</summary>
public static class TestPlanTexts
{
    public static string Title(TestPlanStep step) => step switch
    {
        TestPlanStep.HearingThreshold => Strings.Plan_HearingThreshold,
        TestPlanStep.NumbersQuiet => Strings.Plan_NumbersQuiet,
        TestPlanStep.NumbersNoise => Strings.Plan_NumbersNoise,
        TestPlanStep.PhonemesQuiet => Strings.Plan_PhonemesQuiet,
        TestPlanStep.PhonemesNoise => Strings.Plan_PhonemesNoise,
        _ => step.ToString()
    };

    public static string Description(TestPlanStep step) => step switch
    {
        TestPlanStep.HearingThreshold => Strings.Plan_HearingThresholdDescription,
        TestPlanStep.NumbersQuiet =>
            string.Format(Strings.Plan_NumbersQuietDescription, TestLevelRules.QuietAdaptiveStartAboveThresholdDb),
        TestPlanStep.NumbersNoise =>
            string.Format(Strings.Plan_NumbersNoiseDescription, TestLevelRules.NoiseSpeechAboveQuietThresholdDb),
        TestPlanStep.PhonemesQuiet =>
            string.Format(Strings.Plan_PhonemesQuietDescription, TestLevelRules.PhonemesQuietAboveThresholdDb),
        TestPlanStep.PhonemesNoise =>
            string.Format(Strings.Plan_PhonemesNoiseDescription, TestLevelRules.NoiseSpeechAboveQuietThresholdDb,
                TestLevelRules.PhonemesNoiseAboveSnrThresholdDb),
        _ => string.Empty
    };
}
