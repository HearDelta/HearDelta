using System.Collections.Specialized;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HearDelta.App.Localization;
using HearDelta.App.Services;
using HearDelta.Core;

namespace HearDelta.App.ViewModels;

public enum ShellPage
{
    Persons,
    Overview,
    Measurement,
    Practice,
    HearingThreshold,
    History,
    Settings
}

/// <summary>Eintrag im Sprachmenü; die Sprachen selbst stehen immer in ihrer eigenen Sprache.</summary>
public sealed record LanguageOption(UiLanguagePreference Preference, string Label, bool IsSelected);

public partial class MainWindowViewModel : ObservableObject
{
    private readonly IUserConfirmationService? confirmation;
    private readonly UiLanguagePreference languagePreference;

    public MainWindowViewModel(
        MeasurementViewModel measurement,
        PracticeViewModel practice,
        HearingThresholdViewModel hearingThreshold,
        HistoryViewModel history,
        SettingsViewModel settings,
        PersonsViewModel persons,
        IUserConfirmationService? confirmation = null,
        UiLanguagePreference languagePreference = UiLanguagePreference.Automatic,
        CultureInfo? systemUiCulture = null)
    {
        Measurement = measurement;
        Practice = practice;
        HearingThreshold = hearingThreshold;
        History = history;
        Settings = settings;
        Persons = persons;
        this.confirmation = confirmation;
        this.languagePreference = languagePreference;
        var automatic = UiLanguage.Resolve(UiLanguagePreference.Automatic, systemUiCulture ?? CultureInfo.CurrentUICulture);
        LanguageOptions =
        [
            new(UiLanguagePreference.Automatic, string.Format(Strings.Shell_LanguageAutomatic, LanguageName(automatic)),
                languagePreference == UiLanguagePreference.Automatic),
            new(UiLanguagePreference.German, LanguageName(UiLanguage.German), languagePreference == UiLanguagePreference.German),
            new(UiLanguagePreference.English, LanguageName(UiLanguage.English), languagePreference == UiLanguagePreference.English)
        ];
        History.ResultRequested += ShowStoredMeasurementResult;
        Persons.Detail.HistoryRequested += () => NavigateToHistory(reload: false);
        Persons.Detail.PlanStepRequested += StartPlanStep;
        Persons.List.SelectionChanged += _ =>
        {
            ShowOverviewCommand.NotifyCanExecuteChanged();
            ShowMeasurementCommand.NotifyCanExecuteChanged();
            ShowPracticeCommand.NotifyCanExecuteChanged();
            ShowHearingThresholdCommand.NotifyCanExecuteChanged();
            ShowHistoryCommand.NotifyCanExecuteChanged();
            OnPropertyChanged(nameof(ContextText));
            OnPropertyChanged(nameof(ContextInitials));
            OnPropertyChanged(nameof(HasSelectedPersonValue));
        };
        Settings.Profiles.CollectionChanged += OnProfilesChanged;
        CurrentPage = HasSelectedPerson() ? ShellPage.Overview : ShellPage.Persons;
    }

    public MeasurementViewModel Measurement { get; }
    public PracticeViewModel Practice { get; }
    public HearingThresholdViewModel HearingThreshold { get; }
    public HistoryViewModel History { get; }
    public SettingsViewModel Settings { get; }
    public PersonsViewModel Persons { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsPersonsVisible))]
    [NotifyPropertyChangedFor(nameof(IsOverviewVisible))]
    [NotifyPropertyChangedFor(nameof(IsMeasurementVisible))]
    [NotifyPropertyChangedFor(nameof(IsPracticeVisible))]
    [NotifyPropertyChangedFor(nameof(IsHearingThresholdVisible))]
    [NotifyPropertyChangedFor(nameof(IsHistoryVisible))]
    [NotifyPropertyChangedFor(nameof(IsSettingsVisible))]
    private ShellPage currentPage;

    public bool IsPersonsVisible => CurrentPage == ShellPage.Persons;
    public bool IsOverviewVisible => CurrentPage == ShellPage.Overview;
    public bool IsMeasurementVisible => CurrentPage == ShellPage.Measurement;
    public bool IsPracticeVisible => CurrentPage == ShellPage.Practice;
    public bool IsHearingThresholdVisible => CurrentPage == ShellPage.HearingThreshold;
    public bool IsHistoryVisible => CurrentPage == ShellPage.History;
    public bool IsSettingsVisible => CurrentPage == ShellPage.Settings;

    public bool HasSelectedPersonValue => HasSelectedPerson();
    public string ContextText => Persons.List.SelectedPerson?.DisplayName ?? Strings.Shell_NoPerson;
    public string ContextInitials => PersonInitials.From(Persons.List.SelectedPerson?.DisplayName);
    public bool HasProfiles => Settings.Profiles.Count > 0;
    public string ProfileStatusText => Settings.Profiles.Count switch
    {
        0 => Strings.Shell_NoProfile,
        1 => Settings.Profiles[0].Name,
        _ => string.Format(Strings.Shell_ProfileCount, Settings.Profiles.Count)
    };

    public IReadOnlyList<LanguageOption> LanguageOptions { get; }

    /// <summary>Beschriftung der Sprachschaltfläche: die aktuell gewählte Option.</summary>
    public string LanguageText => LanguageOptions.Single(option => option.IsSelected).Label;

    /// <summary>
    /// Meldet eine neue Oberflächensprache; das Fenster baut sich darauf neu auf. Laufende Tests werden wie bei
    /// einem Seitenwechsel nach Rückfrage abgebrochen, offene Profiländerungen wie beim Schließen behandelt.
    /// </summary>
    public event Action<UiLanguagePreference>? LanguageChangeRequested;

    [RelayCommand]
    private void ChangeLanguage(UiLanguagePreference preference)
    {
        if (preference == languagePreference || !ConfirmLeavingRunningTest(ShellPage.Persons))
            return;
        if (!Settings.ConfirmLanguageChange())
        {
            ShowSettingsUnlessTestRunning();
            return;
        }
        CancelRunningTests(ShellPage.Persons);
        LanguageChangeRequested?.Invoke(preference);
    }

    private static string LanguageName(CultureInfo culture) =>
        culture.TwoLetterISOLanguageName == "de" ? "Deutsch" : "English (US)";

    [RelayCommand]
    private void ShowPersons()
    {
        if (!ConfirmLeavingRunningTest(ShellPage.Persons))
            return;
        CancelRunningTests(ShellPage.Persons);
        Persons.List.Reload();
        CurrentPage = ShellPage.Persons;
    }

    [RelayCommand(CanExecute = nameof(HasSelectedPerson))]
    private void ShowOverview()
    {
        if (!ConfirmLeavingRunningTest(ShellPage.Overview))
            return;
        CancelRunningTests(ShellPage.Overview);
        Persons.Detail.SetPerson(Persons.List.SelectedPerson);
        CurrentPage = ShellPage.Overview;
    }

    [RelayCommand(CanExecute = nameof(HasSelectedPerson))]
    private void ShowMeasurement()
    {
        if (!ConfirmLeavingRunningTest(ShellPage.Measurement))
            return;
        CancelRunningTests(ShellPage.Measurement);
        Measurement.ReplaceProfiles(Settings.Profiles);
        Measurement.SetPerson(Persons.List.SelectedPerson);
        CurrentPage = ShellPage.Measurement;
    }

    [RelayCommand(CanExecute = nameof(HasSelectedPerson))]
    private void ShowPractice()
    {
        if (!ConfirmLeavingRunningTest(ShellPage.Practice))
            return;
        CancelRunningTests(ShellPage.Practice);
        Practice.ReplaceProfiles(Settings.Profiles);
        Practice.SetPerson(Persons.List.SelectedPerson);
        CurrentPage = ShellPage.Practice;
    }

    [RelayCommand(CanExecute = nameof(HasSelectedPerson))]
    private void ShowHearingThreshold()
    {
        if (!ConfirmLeavingRunningTest(ShellPage.HearingThreshold))
            return;
        CancelRunningTests(ShellPage.HearingThreshold);
        HearingThreshold.ReplaceProfiles(Settings.Profiles);
        HearingThreshold.SetPerson(Persons.List.SelectedPerson);
        CurrentPage = ShellPage.HearingThreshold;
    }

    [RelayCommand(CanExecute = nameof(HasSelectedPerson))]
    private void ShowHistory() => NavigateToHistory(reload: true);

    [RelayCommand]
    private void OpenSettings()
    {
        if (!ConfirmLeavingRunningTest(ShellPage.Settings))
            return;
        CancelRunningTests(ShellPage.Settings);
        CurrentPage = ShellPage.Settings;
    }

    /// <summary>
    /// Zeigt das Messprofil, nachdem das Schließen wegen nicht gespeicherter Änderungen abgebrochen wurde. Ein
    /// laufender Test wird dafür weder abgebrochen noch mit einer weiteren Rückfrage unterbrochen.
    /// </summary>
    public void ShowSettingsUnlessTestRunning()
    {
        if (RunningTestLeftBy(ShellPage.Settings) is null)
            CurrentPage = ShellPage.Settings;
    }

    /// <summary>Öffnet den Test eines Ablaufschritts mit vorbelegter Einstellung; gestartet wird erst im Test selbst.</summary>
    private void StartPlanStep(TestPlanStep step, TestedEar ear)
    {
        if (step == TestPlanStep.HearingThreshold)
        {
            ShowHearingThreshold();
            if (CurrentPage == ShellPage.HearingThreshold)
                HearingThreshold.ApplyPlanStep(ear);
            return;
        }
        ShowMeasurement();
        if (CurrentPage == ShellPage.Measurement)
            Measurement.ApplyPlanStep(step, ear);
    }

    private void NavigateToHistory(bool reload)
    {
        if (!ConfirmLeavingRunningTest(ShellPage.History))
            return;
        CancelRunningTests(ShellPage.History);
        if (reload)
            History.SetPerson(Persons.List.SelectedPerson);
        CurrentPage = ShellPage.History;
    }

    private bool HasSelectedPerson() => Persons.List.SelectedPerson is not null;

    /// <summary>Name des laufenden Tests, der beim Wechsel auf <paramref name="target"/> abgebrochen würde.</summary>
    public string? RunningTestLeftBy(ShellPage target)
    {
        if (target != ShellPage.Measurement && (Measurement.IsPreparation || Measurement.IsActiveTest))
            return Strings.Shell_RunningWordTest;
        if (target != ShellPage.HearingThreshold && HearingThreshold.IsActiveTest)
            return Strings.Shell_RunningHearingThreshold;
        if (target != ShellPage.Practice && Practice.IsActive)
            return Strings.Shell_RunningPractice;
        return null;
    }

    private bool ConfirmLeavingRunningTest(ShellPage target)
    {
        if (RunningTestLeftBy(target) is not { } running || confirmation is null)
            return true;
        return confirmation.Confirm(
            Strings.Shell_CancelTestTitle,
            string.Format(Strings.Shell_CancelTestMessage, running));
    }

    private void CancelRunningTests(ShellPage target)
    {
        if (target != ShellPage.Measurement)
            Measurement.CancelActiveTest();
        if (target != ShellPage.HearingThreshold)
            HearingThreshold.CancelActiveTest();
        if (target != ShellPage.Practice)
            Practice.CancelActivePractice();
    }

    private void OnProfilesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        OnPropertyChanged(nameof(HasProfiles));
        OnPropertyChanged(nameof(ProfileStatusText));
    }

    private void ShowStoredMeasurementResult(HistorySessionItemViewModel item)
    {
        if (item.Result is null || !ConfirmLeavingRunningTest(ShellPage.History))
            return;

        CancelRunningTests(ShellPage.History);
        Measurement.ShowStoredResult(item.Session, item.Result);
        CurrentPage = ShellPage.Measurement;
    }
}
