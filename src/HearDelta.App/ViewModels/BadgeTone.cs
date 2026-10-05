using HearDelta.Core;

namespace HearDelta.App.ViewModels;

/// <summary>Bedeutungsfarbe einer Marke; die Farbwerte stehen zentral in App.xaml.</summary>
public enum BadgeTone
{
    Neutral,
    EarLeft,
    EarRight,
    WithAid,
    WithoutAid,
    Success,
    Warning,
    Danger
}

public static class BadgeTones
{
    public static BadgeTone ForEar(TestedEar ear) => ear == TestedEar.Left ? BadgeTone.EarLeft : BadgeTone.EarRight;

    public static string EarLetter(TestedEar ear) => ear == TestedEar.Left ? "L" : "R";

    public static BadgeTone ForCondition(HearingAidCondition condition) =>
        condition == HearingAidCondition.WithHearingAid ? BadgeTone.WithAid : BadgeTone.WithoutAid;

    public static BadgeTone ForStatus(DateTimeOffset? completedAt, DateTimeOffset? abortedAt) =>
        abortedAt is not null ? BadgeTone.Warning
        : completedAt is not null ? BadgeTone.Success
        : BadgeTone.Neutral;
}

/// <summary>Hörgerät einer Person mit Anzeigehilfen.</summary>
public sealed record HearingAidItem(PersonHearingAid Aid)
{
    public BadgeTone EarTone => BadgeTones.ForEar(Aid.Ear);
    public string EarLetter => BadgeTones.EarLetter(Aid.Ear);
    public string EarText => Aid.Ear == TestedEar.Left ? Strings.Common_Left : Strings.Common_Right;
    public string Label => $"{EarText} · {Aid.DisplayName}";
}
