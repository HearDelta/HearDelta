using HearDelta.Core;

namespace HearDelta.App.ViewModels;

/// <summary>Bezeichnungen und Erklärungen der Schritte des empfohlenen Ablaufs.</summary>
public static class TestPlanTexts
{
    public static string Title(TestPlanStep step) => step switch
    {
        TestPlanStep.HearingThreshold => "Hörschwelle",
        TestPlanStep.NumbersQuiet => "Zahlen in Ruhe (adaptiv)",
        TestPlanStep.NumbersNoise => "Zahlen im Störgeräusch (adaptiv)",
        TestPlanStep.PhonemesQuiet => "Phonemkontraste in Ruhe",
        TestPlanStep.PhonemesNoise => "Phonemkontraste im Störgeräusch",
        _ => step.ToString()
    };

    public static string Description(TestPlanStep step) => step switch
    {
        TestPlanStep.HearingThreshold =>
            "Sinustöne ohne Hörgerät, optional mit Vertäubung des Gegenohrs. Liefert einen Startwert für Schritt 2, solange dort noch keine Messung vorliegt.",
        TestPlanStep.NumbersQuiet =>
            $"Ruheschwelle ohne und mit Hörgerät. Start {TestLevelRules.QuietAdaptiveStartAboveThresholdDb:0} dB über der (geschätzten) Ruheschwelle; Grundlage für alle folgenden Lautstärken.",
        TestPlanStep.NumbersNoise =>
            $"SNR-Schwelle ohne und mit Hörgerät. Sprache {TestLevelRules.NoiseSpeechAboveQuietThresholdDb:0} dB über der Ruheschwelle, Dauerrauschen.",
        TestPlanStep.PhonemesQuiet =>
            $"Prozent richtig bei {TestLevelRules.PhonemesQuietAboveThresholdDb:0} dB über der Ruheschwelle.",
        TestPlanStep.PhonemesNoise =>
            $"Prozent richtig: Sprache {TestLevelRules.NoiseSpeechAboveQuietThresholdDb:0} dB über der Ruheschwelle, SNR {TestLevelRules.PhonemesNoiseAboveSnrThresholdDb:0} dB über der SNR-Schwelle.",
        _ => string.Empty
    };
}
