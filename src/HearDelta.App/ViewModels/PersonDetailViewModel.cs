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
    private static readonly CultureInfo German = CultureInfo.GetCultureInfo("de-DE");

    public string DateText => StartedAt.ToLocalTime().ToString("dd.MM.yyyy", German);
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
    public string StartButtonText => IsDone ? "Wiederholen" : "Starten";
}

public partial class PersonDetailViewModel : ObservableObject
{
    public const int RecentLimit = 6;
    private static readonly CultureInfo German = CultureInfo.GetCultureInfo("de-DE");

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
        new(TestedEar.Left, "Linkes Ohr"),
        new(TestedEar.Right, "Rechtes Ohr")
    ];

    [ObservableProperty]
    private SelectionOption<TestedEar> planEar;

    public ObservableCollection<TestPlanStepItem> PlanSteps { get; } = [];

    /// <summary>Ein Schritt des empfohlenen Ablaufs soll für das gewählte Ohr gestartet werden.</summary>
    public event Action<TestPlanStep, TestedEar>? PlanStepRequested;

    public string PlanHint => PlanSteps.FirstOrDefault(step => step.IsRecommended) is { } next
        ? $"Als Nächstes empfohlen: Schritt {next.Number}. Jeder Schritt lässt sich jederzeit starten oder überspringen."
        : "Alle Schritte für dieses Ohr sind durchgeführt. Wiederholungen übernehmen jeweils die neuesten Vortestwerte.";

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
                        ? $"Zuletzt am {status!.LastCompletedAt!.Value.ToLocalTime().ToString("dd.MM.yyyy", German)}: {status.ResultText}"
                        : "Noch nicht durchgeführt",
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
    public string Heading => Person?.DisplayName ?? "Keine Person ausgewählt";
    public string Initials => PersonInitials.From(Person?.DisplayName);

    public string Subtitle
    {
        get
        {
            if (Person is null)
                return "Wähle oder lege eine Person an, um Tests zu starten.";
            var parts = new List<string>();
            if (Person.DateOfBirth is { } birth)
                parts.Add($"geboren {birth.ToString("dd.MM.yyyy", German)}");
            parts.Add(MeasurementCount switch
            {
                0 => "noch keine Messungen",
                1 => "1 Messung",
                _ => $"{MeasurementCount} Messungen"
            });
            if (RecentMeasurements.FirstOrDefault() is { } latest)
                parts.Add($"letzte am {latest.DateText}");
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
            !item.HasScores ? "Keine Antworten erfasst" : item.IsAdaptive ? item.ScoreText : $"{item.ScoreText} richtig",
            item.DifferenceText,
            item.StatusText,
            item.StatusTone,
            item,
            null));
        var thresholds = History.HearingThresholdTests.Select(item => new RecentMeasurementItem(
            item.Session.StartedAt,
            item.Ear,
            $"Hörschwelle · {item.Name}",
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
        environment == ListeningEnvironment.Quiet ? "Ruhe" : "Störgeräusch";
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
