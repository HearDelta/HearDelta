using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HearDelta.App.Services;
using HearDelta.Core;

namespace HearDelta.App.ViewModels;

/// <summary>Eine Zeile der Liste „Letzte Messungen“ – Wort- oder Hörschwellentest.</summary>
public sealed record RecentMeasurementItem(
    DateTimeOffset StartedAt,
    TestedEar Ear,
    string Title,
    string Detail,
    string DifferenceText,
    string StatusText,
    BadgeTone StatusTone,
    HistorySessionItemViewModel? WordTest,
    HearingThresholdHistoryItem? ThresholdTest)
{
    public string DateText => StartedAt.ToLocalTime().ToString("d", CultureInfo.CurrentCulture);
    public string EarLetter => BadgeTones.EarLetter(Ear);
    public BadgeTone EarTone => BadgeTones.ForEar(Ear);
    public bool HasDifference => DifferenceText is { Length: > 0 } and not "–";
}

/// <summary>Ein Schritt des empfohlenen Ablaufs in der Personenübersicht.</summary>
public sealed record TestPlanStepItem(
    TestPlanStep Step,
    string Title,
    string Description,
    string StatusText,
    bool IsDone,
    bool IsRecommended)
{
    public int Number => (int)Step;
    public string NumberText => IsDone ? "✓" : Number.ToString(CultureInfo.InvariantCulture);
    public string StartButtonText => IsDone ? Strings.Overview_Repeat : Strings.Overview_Start;
}

public partial class PersonDetailViewModel : ObservableObject
{
    public const int RecentLimit = 6;

    private readonly Func<Guid, IReadOnlyList<PersonHearingAid>> loadHearingAids;
    private readonly Func<Guid, TestedEar, IReadOnlyList<TestPlanStatus>> loadPlanStatus;

    public PersonDetailViewModel(
        HistoryViewModel history,
        Func<Guid, IReadOnlyList<PersonHearingAid>>? loadHearingAids = null,
        Func<Guid, TestedEar, IReadOnlyList<TestPlanStatus>>? loadPlanStatus = null)
    {
        History = history;
        this.loadHearingAids = loadHearingAids ?? (_ => []);
        this.loadPlanStatus = loadPlanStatus ?? ((_, _) => []);
        planEar = PlanEarOptions[0];
        History.Reloaded += RefreshRecent;
        History.Reloaded += RefreshPlan;
    }

    public IReadOnlyList<SelectionOption<TestedEar>> PlanEarOptions { get; } =
    [
        new(TestedEar.Left, Strings.Common_LeftEar),
        new(TestedEar.Right, Strings.Common_RightEar)
    ];

    [ObservableProperty]
    private SelectionOption<TestedEar> planEar;

    public ObservableCollection<TestPlanStepItem> PlanSteps { get; } = [];

    /// <summary>Ein Schritt des empfohlenen Ablaufs soll für das gewählte Ohr gestartet werden.</summary>
    public event Action<TestPlanStep, TestedEar>? PlanStepRequested;

    public string PlanHint => PlanSteps.FirstOrDefault(step => step.IsRecommended) is { } next
        ? string.Format(Strings.Overview_NextRecommended, next.Number)
        : Strings.Overview_AllDone;

    partial void OnPlanEarChanged(SelectionOption<TestedEar> value) => RefreshPlan();

    [RelayCommand]
    private void StartPlanStep(TestPlanStepItem? item)
    {
        if (item is not null && Person is not null)
            PlanStepRequested?.Invoke(item.Step, PlanEar.Value);
    }

    private void RefreshPlan()
    {
        PlanSteps.Clear();
        if (Person is not null)
        {
            IReadOnlyList<TestPlanStatus> statuses;
            try
            {
                statuses = loadPlanStatus(Person.Id, PlanEar.Value);
            }
            catch
            {
                statuses = [];
            }
            var recommendedAssigned = false;
            foreach (var step in Enum.GetValues<TestPlanStep>())
            {
                var status = statuses.FirstOrDefault(value => value.Step == step);
                var done = status?.LastCompletedAt is not null;
                var recommended = !done && !recommendedAssigned;
                recommendedAssigned |= recommended;
                PlanSteps.Add(new TestPlanStepItem(
                    step,
                    TestPlanTexts.Title(step),
                    TestPlanTexts.Description(step),
                    done
                        ? string.Format(Strings.Overview_LastDone, status!.LastCompletedAt!.Value.ToLocalTime(), status.ResultText)
                        : Strings.Overview_NotDone,
                    done,
                    recommended));
            }
        }
        OnPropertyChanged(nameof(PlanHint));
    }

    public ObservableCollection<HearingAidItem> HearingAids { get; } = [];
    public bool HasHearingAids => HearingAids.Count > 0;

    public HistoryViewModel History { get; }
    public ObservableCollection<RecentMeasurementItem> RecentMeasurements { get; } = [];

    /// <summary>Wird ausgelöst, wenn ein Hörschwellentest im Verlauf geöffnet werden soll.</summary>
    public event Action? HistoryRequested;

    [ObservableProperty]
    private PersonProfile? person;

    [ObservableProperty]
    private int measurementCount;

    public bool HasPerson => Person is not null;
    public bool HasRecentMeasurements => RecentMeasurements.Count > 0;
    public bool HasNoRecentMeasurements => HasPerson && RecentMeasurements.Count == 0;
    public string Heading => Person?.DisplayName ?? Strings.Overview_NoPerson;
    public string Initials => PersonInitials.From(Person?.DisplayName);

    public string Subtitle
    {
        get
        {
            if (Person is null)
                return Strings.Overview_NoPersonHint;
            var parts = new List<string>();
            if (Person.DateOfBirth is { } birth)
                parts.Add(string.Format(Strings.Overview_Born, birth));
            parts.Add(MeasurementCount switch
            {
                0 => Strings.Overview_NoMeasurements,
                1 => Strings.Overview_OneMeasurement,
                _ => string.Format(Strings.Overview_Measurements, MeasurementCount)
            });
            if (RecentMeasurements.FirstOrDefault() is { } latest)
                parts.Add(string.Format(Strings.Overview_LatestOn, latest.DateText));
            return string.Join(" · ", parts);
        }
    }

    public string? Notes => string.IsNullOrWhiteSpace(Person?.Notes) ? null : Person!.Notes;
    public bool HasNotes => Notes is not null;

    public void SetPerson(PersonProfile? person)
    {
        Person = person;
        OnPropertyChanged(nameof(HasPerson));
        OnPropertyChanged(nameof(Heading));
        OnPropertyChanged(nameof(Initials));
        OnPropertyChanged(nameof(Notes));
        OnPropertyChanged(nameof(HasNotes));
        RefreshHearingAids();
        History.SetPerson(person);
        if (RecentMeasurements.FirstOrDefault() is { } latest)
            PlanEar = PlanEarOptions.First(option => option.Value == latest.Ear);
        RefreshPlan();
    }

    public void RefreshHearingAids()
    {
        HearingAids.Clear();
        if (Person is not null)
            foreach (var aid in loadHearingAids(Person.Id).OrderBy(aid => aid.Ear).ThenBy(aid => aid.DisplayName))
                HearingAids.Add(new HearingAidItem(aid));
        OnPropertyChanged(nameof(HasHearingAids));
    }

    [RelayCommand]
    private void OpenRecent(RecentMeasurementItem? item)
    {
        if (item?.WordTest is { CanShowResult: true } wordTest)
        {
            History.ShowResultCommand.Execute(wordTest);
            return;
        }

        if (item?.ThresholdTest is { } thresholdTest)
        {
            History.SelectedTabIndex = 1;
            History.SelectedThresholdTest = History.HearingThresholdTests.FirstOrDefault(test => test.Id == thresholdTest.Id);
        }
        else
        {
            History.SelectedTabIndex = 0;
        }
        HistoryRequested?.Invoke();
    }

    private void RefreshRecent()
    {
        var words = History.AllSessions.Select(item => new RecentMeasurementItem(
            item.Session.StartedAt,
            item.Session.Ear,
            $"{item.Name} · {item.MaterialText} · {ShortEnvironment(item.Session.Environment)}",
            !item.HasScores ? Strings.Overview_NoAnswers : item.IsAdaptive ? item.ScoreText : string.Format(Strings.Overview_Correct, item.ScoreText),
            item.DifferenceText,
            item.StatusText,
            item.StatusTone,
            item,
            null));
        var thresholds = History.HearingThresholdTests.Select(item => new RecentMeasurementItem(
            item.Session.StartedAt,
            item.Ear,
            string.Format(Strings.Overview_ThresholdTitle, item.Name),
            item.ResultText,
            string.Empty,
            item.StatusText,
            item.StatusTone,
            null,
            item));
        var all = words.Concat(thresholds).OrderByDescending(item => item.StartedAt).ToList();

        MeasurementCount = all.Count;
        RecentMeasurements.Clear();
        foreach (var item in all.Take(RecentLimit))
            RecentMeasurements.Add(item);
        OnPropertyChanged(nameof(HasRecentMeasurements));
        OnPropertyChanged(nameof(HasNoRecentMeasurements));
        OnPropertyChanged(nameof(Subtitle));
    }

    private static string ShortEnvironment(ListeningEnvironment environment) =>
        environment == ListeningEnvironment.Quiet ? Strings.Common_Quiet : Strings.Common_Noise;
}

public static class PersonInitials
{
    public static string From(string? displayName)
    {
        var words = (displayName ?? string.Empty)
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return words.Length switch
        {
            0 => "?",
            1 => words[0][..Math.Min(2, words[0].Length)].ToUpperInvariant(),
            _ => $"{char.ToUpperInvariant(words[0][0])}{char.ToUpperInvariant(words[^1][0])}"
        };
    }
}
