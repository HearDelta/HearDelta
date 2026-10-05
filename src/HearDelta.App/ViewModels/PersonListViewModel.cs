using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HearDelta.App.Services;
using HearDelta.Core;

namespace HearDelta.App.ViewModels;

public partial class PersonListViewModel : ObservableObject
{
    private readonly PersonRepository repository;
    private readonly IUserConfirmationService? confirmation;
    private readonly Func<DateTimeOffset> now;
    private readonly List<PersonProfile> allPeople = [];
    private PersonProfile? selectionBeforeNew;

    public PersonListViewModel(
        PersonRepository repository,
        Func<DateTimeOffset>? now = null,
        IUserConfirmationService? confirmation = null)
    {
        this.repository = repository;
        this.confirmation = confirmation;
        this.now = now ?? (() => DateTimeOffset.UtcNow);
        Reload();
    }

    public event Action<PersonProfile?>? SelectionChanged;
    public ObservableCollection<PersonProfile> People { get; } = [];

    [ObservableProperty]
    private PersonProfile? selectedPerson;

    [ObservableProperty]
    private string searchText = string.Empty;

    [ObservableProperty]
    private string editDisplayName = string.Empty;

    [ObservableProperty]
    private DateTime? editDateOfBirth;

    [ObservableProperty]
    private string editOtherInformation = string.Empty;

    [ObservableProperty]
    private string statusMessage = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSummaryVisible))]
    private bool isEditorOpen;

    public bool IsSummaryVisible => !IsEditorOpen && SelectedPerson is not null;
    public bool IsEmptyHintVisible => !IsEditorOpen && SelectedPerson is null;

    public bool HasPeople => People.Count > 0;
    public bool IsEditingExisting => SelectedPerson is not null;
    public string EditorTitle => IsEditingExisting ? "Person bearbeiten" : "Neue Person";
    public string SaveButtonText => IsEditingExisting ? "Änderungen speichern" : "Person anlegen";

    // Hörgeräte der ausgewählten Person
    public ObservableCollection<HearingAidItem> HearingAids { get; } = [];
    public IReadOnlyList<SelectionOption<TestedEar>> EarOptions { get; } =
    [
        new(TestedEar.Left, "Linkes Ohr"),
        new(TestedEar.Right, "Rechtes Ohr")
    ];
    public bool HasHearingAids => HearingAids.Count > 0;
    public bool HasNoHearingAids => HearingAids.Count == 0;

    /// <summary>Wird ausgelöst, wenn Hörgeräte hinzugefügt, geändert oder gelöscht wurden.</summary>
    public event Action? HearingAidsChanged;

    [ObservableProperty]
    private string newAidManufacturer = string.Empty;

    [ObservableProperty]
    private string newAidModel = string.Empty;

    /// <summary>Optionale Bezeichnung; leer bedeutet „Hersteller Modell“.</summary>
    [ObservableProperty]
    private string newAidDisplayName = string.Empty;

    [ObservableProperty]
    private SelectionOption<TestedEar>? newAidEar;

    /// <summary>Das gerade bearbeitete Hörgerät; <c>null</c> heißt, das Formular legt ein neues an.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEditingHearingAid))]
    [NotifyPropertyChangedFor(nameof(HearingAidFormTitle))]
    [NotifyPropertyChangedFor(nameof(HearingAidSaveButtonText))]
    private HearingAidItem? editingHearingAid;

    public bool IsEditingHearingAid => EditingHearingAid is not null;
    public string HearingAidFormTitle => IsEditingHearingAid ? "Hörgerät bearbeiten" : "Hörgerät hinzufügen";
    public string HearingAidSaveButtonText => IsEditingHearingAid ? "Änderungen speichern" : "Hinzufügen";

    [RelayCommand]
    private void AddHearingAid()
    {
        if (SelectedPerson is null)
            return;
        if (NewAidEar is null)
        {
            StatusMessage = "Bitte das Ohr des Hörgeräts wählen.";
            return;
        }
        var manufacturer = NewAidManufacturer.Trim();
        var model = NewAidModel.Trim();
        var displayName = string.IsNullOrWhiteSpace(NewAidDisplayName)
            ? $"{manufacturer} {model}".Trim()
            : NewAidDisplayName.Trim();
        var existing = EditingHearingAid?.Aid;
        var aid = new PersonHearingAid(
            existing?.Id ?? Guid.NewGuid(), SelectedPerson.Id, manufacturer, model, displayName, NewAidEar.Value);
        var errors = PersonProfileRules.Validate(aid);
        if (errors.Count > 0)
        {
            StatusMessage = string.Join(" ", errors);
            return;
        }
        try
        {
            repository.SaveHearingAid(aid);
        }
        catch (Exception exception)
        {
            StatusMessage = $"Hörgerät konnte nicht gespeichert werden: {exception.Message}";
            return;
        }
        ResetHearingAidForm();
        LoadHearingAids();
        StatusMessage = existing is null
            ? $"Hörgerät „{aid.DisplayName}“ hinzugefügt."
            : $"Hörgerät „{aid.DisplayName}“ gespeichert. Bisherige Messungen behalten ihre gespeicherten Gerätedaten.";
        HearingAidsChanged?.Invoke();
    }

    [RelayCommand]
    private void EditHearingAid(HearingAidItem? item)
    {
        if (item is null)
            return;
        var aid = item.Aid;
        EditingHearingAid = item;
        NewAidManufacturer = aid.Manufacturer;
        NewAidModel = aid.Model;
        NewAidDisplayName = aid.DisplayName == $"{aid.Manufacturer} {aid.Model}".Trim() ? string.Empty : aid.DisplayName;
        NewAidEar = EarOptions.First(option => option.Value == aid.Ear);
        StatusMessage = $"„{aid.DisplayName}“ wird bearbeitet.";
    }

    [RelayCommand]
    private void CancelHearingAidEdit()
    {
        ResetHearingAidForm();
        StatusMessage = string.Empty;
    }

    [RelayCommand]
    private void ArchiveHearingAid(HearingAidItem? item)
    {
        if (item is null)
            return;
        if (confirmation is not null && !confirmation.Confirm(
                "Hörgerät löschen",
                $"Soll das Hörgerät „{item.Aid.DisplayName}“ aus der Liste der Person gelöscht werden?\n\n" +
                "Bisherige Messungen bleiben unverändert und behalten ihre gespeicherten Gerätedaten."))
            return;
        try
        {
            repository.SaveHearingAid(item.Aid with { ArchivedAt = now() });
        }
        catch (Exception exception)
        {
            StatusMessage = $"Hörgerät konnte nicht gelöscht werden: {exception.Message}";
            return;
        }
        if (EditingHearingAid?.Aid.Id == item.Aid.Id)
            ResetHearingAidForm();
        LoadHearingAids();
        StatusMessage = $"Hörgerät „{item.Aid.DisplayName}“ gelöscht. Bisherige Messungen bleiben unverändert.";
        HearingAidsChanged?.Invoke();
    }

    private void ResetHearingAidForm()
    {
        EditingHearingAid = null;
        NewAidManufacturer = string.Empty;
        NewAidModel = string.Empty;
        NewAidDisplayName = string.Empty;
    }

    private void LoadHearingAids()
    {
        HearingAids.Clear();
        if (SelectedPerson is not null)
            foreach (var aid in repository.LoadHearingAids(SelectedPerson.Id).OrderBy(aid => aid.Ear).ThenBy(aid => aid.DisplayName))
                HearingAids.Add(new HearingAidItem(aid));
        OnPropertyChanged(nameof(HasHearingAids));
        OnPropertyChanged(nameof(HasNoHearingAids));
    }

    partial void OnSelectedPersonChanged(PersonProfile? value)
    {
        ResetHearingAidForm();
        LoadHearingAids();
        EditDisplayName = value?.DisplayName ?? string.Empty;
        EditDateOfBirth = value?.DateOfBirth is { } date
            ? date.ToDateTime(TimeOnly.MinValue)
            : null;
        EditOtherInformation = value?.Notes ?? string.Empty;
        OnPropertyChanged(nameof(IsEditingExisting));
        OnPropertyChanged(nameof(EditorTitle));
        OnPropertyChanged(nameof(SaveButtonText));
        OnPropertyChanged(nameof(IsSummaryVisible));
        OnPropertyChanged(nameof(IsEmptyHintVisible));
        SelectionChanged?.Invoke(value);
    }
    partial void OnSearchTextChanged(string value) => ApplyFilter();

    [RelayCommand]
    public void Reload()
    {
        var selectedId = SelectedPerson?.Id;
        allPeople.Clear();
        allPeople.AddRange(repository.LoadAll());
        ApplyFilter();
        SelectedPerson = People.FirstOrDefault(person => person.Id == selectedId) ?? People.FirstOrDefault();
        StatusMessage = allPeople.Count == 0 ? "Noch keine Person angelegt." : $"{allPeople.Count} Person(en) geladen.";
    }

    [RelayCommand]
    private void SavePerson()
    {
        var timestamp = now();
        var existing = SelectedPerson;
        var person = new PersonProfile(
            existing?.Id ?? Guid.NewGuid(),
            EditDisplayName,
            string.IsNullOrWhiteSpace(EditOtherInformation) ? null : EditOtherInformation.Trim(),
            existing?.CreatedAt ?? timestamp,
            timestamp,
            existing?.ArchivedAt,
            EditDateOfBirth is { } date ? DateOnly.FromDateTime(date) : null);
        var errors = PersonProfileRules.Validate(person);
        if (errors.Count > 0)
        {
            StatusMessage = string.Join(" ", errors);
            return;
        }
        repository.Save(person with { DisplayName = person.DisplayName.Trim() });
        Reload();
        SelectedPerson = People.Single(candidate => candidate.Id == person.Id);
        selectionBeforeNew = null;
        IsEditorOpen = false;
        StatusMessage = existing is null
            ? $"Person „{person.DisplayName.Trim()}“ angelegt."
            : $"Änderungen für „{person.DisplayName.Trim()}“ gespeichert.";
    }

    [RelayCommand]
    private void NewPerson()
    {
        selectionBeforeNew = SelectedPerson ?? selectionBeforeNew;
        SelectedPerson = null;
        IsEditorOpen = true;
        StatusMessage = "Neue Person: Nur der Name ist erforderlich.";
    }

    [RelayCommand]
    private void EditPerson()
    {
        if (SelectedPerson is null)
            return;
        OnSelectedPersonChanged(SelectedPerson);
        IsEditorOpen = true;
        StatusMessage = string.Empty;
    }

    [RelayCommand]
    private void CancelEdit()
    {
        IsEditorOpen = false;
        if (SelectedPerson is null && selectionBeforeNew is { } previous)
            SelectedPerson = People.FirstOrDefault(person => person.Id == previous.Id);
        else
            OnSelectedPersonChanged(SelectedPerson);
        selectionBeforeNew = null;
        StatusMessage = string.Empty;
    }

    partial void OnIsEditorOpenChanged(bool value) => OnPropertyChanged(nameof(IsEmptyHintVisible));

    private void ApplyFilter()
    {
        var query = SearchText.Trim();
        People.Clear();
        foreach (var person in allPeople.Where(person =>
                     query.Length == 0 ||
                     person.DisplayName.Contains(query, StringComparison.CurrentCultureIgnoreCase) ||
                     person.Id.ToString("N").Contains(query, StringComparison.OrdinalIgnoreCase)))
            People.Add(person);
        OnPropertyChanged(nameof(HasPeople));
    }
}
