using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HearDelta.App.Services;
using HearDelta.Core;

namespace HearDelta.App.ViewModels;

public partial class SettingsViewModel : ObservableObject
{
    private readonly IAudioEndpointService audio;
    private readonly ProfileRepository repository;
    private readonly IHeadphoneEqualizationCatalogService? equalizationCatalog;
    private readonly IUnsavedChangesPrompt? unsavedChangesPrompt;
    private ProfileFormState? newProfileBaseline;
    private bool isResettingProfileList;
    private HeadphoneEqualizationCatalog? loadedEqualizationCatalog;
    private const int MaximumEqualizationMatches = 100;

    /// <summary>
    /// Feste Bauformen. Freitext würde den Snapshot-Vergleich unterlaufen, der die Bauform als Text vergleicht.
    /// In-Ears und Earbuds fehlen bewusst: Sie lassen sich nicht zusammen mit einem Hörgerät tragen.
    /// </summary>
    public static IReadOnlyList<string> StandardDesigns { get; } =
        ["Ohrumschließend, offen", "Ohrumschließend, halboffen", "Ohrumschließend, geschlossen", "Ohraufliegend"];

    private const string OnEarDesign = "Ohraufliegend";

    /// <summary>Auswahl für die Bauform: „nicht angegeben“ (leer), die festen Bauformen und ein älterer Freitextwert.</summary>
    public ObservableCollection<string> DesignOptions { get; } = ["", .. StandardDesigns];

    public string DesignHint => Design switch
    {
        OnEarDesign => "Ohraufliegende Kopfhörer drücken auf ein Hinter-dem-Ohr-Hörgerät; Sitz und Rückkopplung sind kaum reproduzierbar. Für Messungen mit Hörgerät ohrumschließende Kopfhörer verwenden.",
        "" => "",
        var value when !StandardDesigns.Contains(value) => $"„{value}“ ist ein älterer Freitextwert. Bitte eine der festen Bauformen wählen.",
        _ => ""
    };

    public bool HasDesignHint => DesignHint.Length > 0;

    /// <summary>Eigenschaften, deren Änderung den Speicherzustand des Profils beeinflusst.</summary>
    private static readonly HashSet<string> SaveRelevantProperties =
    [
        nameof(SelectedProfile), nameof(SelectedEndpoint), nameof(ProfileName), nameof(Manufacturer), nameof(Model),
        nameof(Design), nameof(Impedance), nameof(AmplifierOutput), nameof(Gain), nameof(StartVolumeDb),
        nameof(MaximumVolumeDb), nameof(ExclusiveMode), nameof(Equalization)
    ];

    public ObservableCollection<AudioEndpointDescriptor> AudioEndpoints { get; } = [];
    public ObservableCollection<MeasurementProfile> Profiles { get; } = [];
    public ObservableCollection<HeadphoneEqualizationCatalogEntry> EqualizationMatches { get; } = [];

    /// <summary>Steuert den Hinweis in der leeren Profilliste.</summary>
    public bool HasNoProfiles => Profiles.Count == 0;

    [ObservableProperty] private AudioEndpointDescriptor? selectedEndpoint;
    [ObservableProperty] private MeasurementProfile? selectedProfile;
    [ObservableProperty] private string profileName = "Neues Messprofil";
    [ObservableProperty] private string manufacturer = "";
    [ObservableProperty] private string model = "";
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DesignHint), nameof(HasDesignHint))]
    private string design = "";
    [ObservableProperty] private string impedance = "";
    [ObservableProperty] private string amplifierOutput = "";
    [ObservableProperty] private string gain = "";
    [ObservableProperty] private decimal startVolumeDb = -60;
    [ObservableProperty] private decimal maximumVolumeDb = -30;
    [ObservableProperty] private bool exclusiveMode = true;
    [ObservableProperty] private string statusMessage = "Audioausgänge werden ermittelt …";
    [ObservableProperty] private bool isBusy;
    [ObservableProperty] private string equalizationSearchText = "";
    [ObservableProperty] private string equalizationSearchStatus = "";
    [ObservableProperty] private HeadphoneEqualizationCatalogEntry? selectedEqualizationMatch;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasEqualization), nameof(EqualizationDetails))]
    private HeadphoneEqualization? equalization;

    public bool IsEqualizationCatalogAvailable => equalizationCatalog is not null;
    public bool HasEqualization => Equalization is not null;

    /// <summary>Abtastrate, für die Vorabsenkung und Kurve berechnet werden: die des gewählten Ausgangs.</summary>
    public int EqualizationSampleRate => SelectedEndpoint?.SampleRate ?? 48_000;

    public string EqualizationDetails => Equalization is not { } value
        ? "Ohne Entzerrung: Die Signale werden unverändert ausgegeben."
        : string.Format(
            CultureInfo.GetCultureInfo("de-DE"),
            "{0} Filter · Vorabsenkung {1:0.0} dB bei {2:0.#} kHz (Datei: {3:0.0} dB)",
            value.Filters.Count,
            HeadphoneEqualizer.GetEffectivePreampDb(value, EqualizationSampleRate),
            EqualizationSampleRate / 1000d,
            value.PreampDb);

    /// <summary>
    /// Speichern würde etwas ändern: Das Profil ist neu oder weicht vom gespeicherten Stand ab. Verglichen wird so,
    /// wie <see cref="SaveProfile"/> speichert (getrimmte Texte, Ausgang samt Format, Entzerrung über die Prüfsumme).
    /// </summary>
    public bool HasUnsavedChanges => SelectedProfile is not { } saved || !MatchesSavedProfile(saved);

    /// <summary>
    /// Auswahl in der Profilliste. Ein Klick wird nur vorgemerkt: Rückfrage und Wechsel laufen erst, wenn die Liste
    /// ihre eigene Auswahländerung abgeschlossen hat. Sonst ignoriert sie das Zurückspringen bei Abbruch, und das
    /// Speichern sortiert die Liste mitten in ihrer Auswahl um.
    /// </summary>
    public MeasurementProfile? ProfileListSelection
    {
        get => isResettingProfileList ? null : SelectedProfile;
        set
        {
            if (isResettingProfileList)
                return;
            if (value is null || ReferenceEquals(value, SelectedProfile))
            {
                // Die Liste hebt die Auswahl beim Neueinsortieren nach dem Speichern kurz auf; das Profil bleibt.
                RunAfterListSelection(SyncProfileList);
                return;
            }
            RunAfterListSelection(() => SwitchToProfile(value));
        }
    }

    private void SwitchToProfile(MeasurementProfile requested)
    {
        if (!ReferenceEquals(requested, SelectedProfile) && Profiles.Contains(requested) && ConfirmLeavingCurrentProfile("wechseln", "beim aktuellen Profil bleiben"))
            SelectedProfile = requested;
        SyncProfileList();
    }

    /// <summary>
    /// Bringt die Listenauswahl auf <see cref="SelectedProfile"/>. Meldet die Quelle nach einem abgebrochenen Wechsel
    /// unverändert dasselbe Profil, übernimmt die WPF-Liste das nicht; daher zuerst kurz „keine Auswahl“.
    /// </summary>
    private void SyncProfileList()
    {
        isResettingProfileList = true;
        OnPropertyChanged(nameof(ProfileListSelection));
        isResettingProfileList = false;
        OnPropertyChanged(nameof(ProfileListSelection));
    }

    private static void RunAfterListSelection(Action action)
    {
        if (SynchronizationContext.Current is DispatcherSynchronizationContext)
            Dispatcher.CurrentDispatcher.BeginInvoke(action, DispatcherPriority.ContextIdle);
        else
            action();
    }

    public string SaveStateText => SelectedProfile is null
        ? "Neues Messprofil, noch nicht gespeichert"
        : HasUnsavedChanges
            ? "Nicht gespeicherte Änderungen"
            : "Alle Änderungen sind gespeichert";

    public SettingsViewModel(
        IAudioEndpointService audio,
        ProfileRepository repository,
        IHeadphoneEqualizationCatalogService? equalizationCatalog = null,
        IUnsavedChangesPrompt? unsavedChangesPrompt = null)
    {
        this.audio = audio;
        this.repository = repository;
        this.equalizationCatalog = equalizationCatalog;
        this.unsavedChangesPrompt = unsavedChangesPrompt;
        EqualizationSearchStatus = DefaultEqualizationSearchStatus;
        Profiles.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasNoProfiles));
        RefreshEndpoints();
        foreach (var profile in repository.LoadAll()) Profiles.Add(profile);
        if (Profiles.Count > 0) SelectedProfile = Profiles[0];
        else newProfileBaseline = CaptureFormState();
    }

    partial void OnDesignChanging(string value)
    {
        // Einen gespeicherten Freitextwert anzeigbar halten, statt ihn stillschweigend zu verwerfen.
        if (!string.IsNullOrEmpty(value) && !DesignOptions.Contains(value))
            DesignOptions.Add(value);
    }

    partial void OnDesignChanged(string value)
    {
        if (value is null)
            Design = "";
    }

    partial void OnSelectedProfileChanged(MeasurementProfile? value)
    {
        if (value is null) return;
        ProfileName = value.Name;
        Manufacturer = value.Headphone.Manufacturer;
        Model = value.Headphone.Model;
        Design = value.Headphone.Design;
        Impedance = value.Headphone.ImpedanceOhms?.ToString() ?? "";
        AmplifierOutput = value.AmplifierOutput;
        Gain = value.Gain;
        StartVolumeDb = value.StartVolumeDb;
        MaximumVolumeDb = value.MaximumVolumeDb;
        ExclusiveMode = value.ExclusiveMode;
        Equalization = value.Headphone.Equalization;
        SelectedEndpoint = AudioEndpoints.FirstOrDefault(endpoint => endpoint.Id == value.EndpointId);
        StatusMessage = SelectedEndpoint is null
            ? "Der gespeicherte Audioausgang fehlt. Bitte bewusst neu auswählen; es erfolgt kein automatischer Wechsel."
            : "Messprofil geladen. Der gespeicherte Audioausgang ist verfügbar.";
    }

    partial void OnSelectedEndpointChanged(AudioEndpointDescriptor? value)
    {
        OnPropertyChanged(nameof(EqualizationSampleRate));
        OnPropertyChanged(nameof(EqualizationDetails));
        if (value is not null)
        {
            AmplifierOutput = value.Name;
            StatusMessage = $"Audioausgang „{value.Name}“ ausgewählt ({value.Channels} Kanäle, {value.SampleRate} Hz, {value.BitsPerSample} Bit).";
        }
    }

    [RelayCommand]
    private void RefreshEndpoints()
    {
        var previousId = SelectedEndpoint?.Id;
        AudioEndpoints.Clear();
        foreach (var endpoint in audio.GetActiveOutputs()) AudioEndpoints.Add(endpoint);
        SelectedEndpoint = AudioEndpoints.FirstOrDefault(endpoint => endpoint.Id == previousId);
        StatusMessage = AudioEndpoints.Count == 0
            ? "Keine aktiven Audioausgänge gefunden."
            : $"{AudioEndpoints.Count} aktive Audioausgänge gefunden. Bitte den Messausgang bewusst auswählen.";
    }

    [RelayCommand]
    private void NewProfile()
    {
        if (!ConfirmLeavingCurrentProfile("neues Profil anlegen", "beim aktuellen Profil bleiben"))
            return;
        SelectedProfile = null;
        newProfileBaseline = CaptureFormState();
        StatusMessage = "Neues, noch nicht gespeichertes Messprofil.";
    }

    protected override void OnPropertyChanged(PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.PropertyName is null || !SaveRelevantProperties.Contains(e.PropertyName))
            return;
        OnPropertyChanged(nameof(HasUnsavedChanges));
        OnPropertyChanged(nameof(SaveStateText));
        SaveProfileCommand.NotifyCanExecuteChanged();
        if (e.PropertyName == nameof(SelectedProfile))
            OnPropertyChanged(nameof(ProfileListSelection));
    }

    /// <summary>Formularinhalt so, wie <see cref="SaveProfile"/> ihn speichern würde.</summary>
    private sealed record ProfileFormState(
        string Name,
        string? EndpointId,
        string? EndpointName,
        int SampleRate,
        int BitsPerSample,
        int Channels,
        bool ExclusiveMode,
        string Manufacturer,
        string Model,
        string Design,
        string Impedance,
        string AmplifierOutput,
        string Gain,
        decimal StartVolumeDb,
        decimal MaximumVolumeDb,
        string? EqualizationSha256);

    private static ProfileFormState FormStateOf(MeasurementProfile profile) => new(
        profile.Name,
        profile.EndpointId,
        profile.EndpointName,
        profile.SampleRate,
        profile.BitsPerSample,
        profile.Channels,
        profile.ExclusiveMode,
        profile.Headphone.Manufacturer,
        profile.Headphone.Model,
        profile.Headphone.Design,
        profile.Headphone.ImpedanceOhms?.ToString(CultureInfo.InvariantCulture) ?? "",
        profile.AmplifierOutput,
        profile.Gain,
        profile.StartVolumeDb,
        profile.MaximumVolumeDb,
        profile.Headphone.Equalization?.SourceSha256);

    /// <summary>
    /// Ohne ausgewählten Ausgang gibt es am Ausgang nichts zu speichern; dann gilt der Ausgang der Referenz.
    /// Das Speichern selbst verlangt weiterhin einen Ausgang.
    /// </summary>
    private ProfileFormState CaptureFormState(ProfileFormState? reference = null) => new(
        ProfileName.Trim(),
        SelectedEndpoint?.Id ?? reference?.EndpointId,
        SelectedEndpoint?.Name ?? reference?.EndpointName,
        SelectedEndpoint?.SampleRate ?? reference?.SampleRate ?? 0,
        SelectedEndpoint?.BitsPerSample ?? reference?.BitsPerSample ?? 0,
        SelectedEndpoint?.Channels ?? reference?.Channels ?? 0,
        ExclusiveMode,
        Manufacturer.Trim(),
        Model.Trim(),
        Design.Trim(),
        Impedance.Trim(),
        AmplifierOutput.Trim(),
        Gain.Trim(),
        StartVolumeDb,
        MaximumVolumeDb,
        Equalization?.SourceSha256);

    private bool MatchesSavedProfile(MeasurementProfile saved)
    {
        var savedState = FormStateOf(saved);
        return CaptureFormState(savedState) == savedState;
    }

    /// <summary>
    /// Würden beim Verlassen des Formulars Eingaben verloren gehen? Ein neues Profil zählt erst, wenn seit dem
    /// Anlegen etwas geändert wurde.
    /// </summary>
    private bool HasEditsToLose => SelectedProfile is { } saved
        ? !MatchesSavedProfile(saved)
        : newProfileBaseline is null || CaptureFormState(newProfileBaseline) != newProfileBaseline;

    /// <summary>
    /// Vor dem Schließen der App: Fragt bei nicht gespeicherten Änderungen nach. False bedeutet, dass die App offen
    /// bleiben soll (abgebrochen oder Speichern gescheitert).
    /// </summary>
    public bool ConfirmClose() => ConfirmLeavingCurrentProfile("Anwendung schließen", "Anwendung nicht schließen");

    private bool ConfirmLeavingCurrentProfile(string action, string cancelMeaning)
    {
        if (!HasEditsToLose || unsavedChangesPrompt is null)
            return true;
        var name = ProfileName.Trim().Length > 0 ? ProfileName.Trim() : "Neues Messprofil";
        var decision = unsavedChangesPrompt.Ask(
            "Nicht gespeicherte Änderungen",
            $"Das Messprofil „{name}“ enthält nicht gespeicherte Änderungen.\n\n" +
            $"Ja: Änderungen speichern und {action}\n" +
            $"Nein: Änderungen verwerfen und {action}\n" +
            $"Abbrechen: {cancelMeaning}");
        switch (decision)
        {
            case UnsavedChangesDecision.Save:
                SaveProfile();
                // Scheitert das Speichern (z. B. ohne Ausgang), bleibt das Profil offen; der Grund steht in der Statuszeile.
                return !HasEditsToLose;
            case UnsavedChangesDecision.Discard:
                return true;
            default:
                return false;
        }
    }


    [RelayCommand(CanExecute = nameof(HasUnsavedChanges))]
    private void SaveProfile()
    {
        if (SelectedEndpoint is null)
        {
            StatusMessage = "Speichern nicht möglich: Ein erkannter Audioausgang muss ausgewählt werden.";
            return;
        }
        int? impedanceOhms = int.TryParse(Impedance, out var parsed) ? parsed : null;
        var profile = new MeasurementProfile(
            SelectedProfile?.Id ?? Guid.NewGuid(), ProfileName.Trim(), SelectedEndpoint.Id, SelectedEndpoint.Name,
            ExclusiveMode, SelectedEndpoint.SampleRate, SelectedEndpoint.BitsPerSample, SelectedEndpoint.Channels,
            new HeadphoneProfile(SelectedProfile?.Headphone.Id ?? Guid.NewGuid(), Manufacturer.Trim(), Model.Trim(), Design.Trim(), impedanceOhms, Equalization),
            AmplifierOutput.Trim(), Gain.Trim(), StartVolumeDb, MaximumVolumeDb, false, DateTimeOffset.UtcNow);
        var errors = MeasurementProfileRules.Validate(profile);
        if (errors.Count > 0) { StatusMessage = string.Join(" ", errors); return; }
        repository.Save(profile);
        var existing = Profiles.FirstOrDefault(value => value.Id == profile.Id);
        if (existing is not null) Profiles.Remove(existing);
        Profiles.Insert(0, profile);
        SelectedProfile = profile;
        StatusMessage = "Messprofil lokal gespeichert.";
    }

    private string DefaultEqualizationSearchStatus => equalizationCatalog is null
        ? "Kein Entzerrungskatalog verfügbar."
        : "Modell eingeben, z. B. „HD 600“. Quelle: AutoEq.";

    partial void OnEqualizationSearchTextChanged(string value)
    {
        EqualizationMatches.Clear();
        var tokens = value.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (equalizationCatalog is null || tokens.Length == 0)
        {
            EqualizationSearchStatus = DefaultEqualizationSearchStatus;
            return;
        }

        try
        {
            loadedEqualizationCatalog ??= equalizationCatalog.Load();
        }
        catch (Exception exception)
        {
            EqualizationSearchStatus = $"Der Entzerrungskatalog konnte nicht geladen werden: {exception.Message}";
            return;
        }

        var matches = loadedEqualizationCatalog.Entries
            .Where(entry => tokens.All(token =>
                entry.Name.Contains(token, StringComparison.OrdinalIgnoreCase) ||
                entry.Source.Contains(token, StringComparison.OrdinalIgnoreCase) ||
                entry.Rig.Contains(token, StringComparison.OrdinalIgnoreCase)))
            .OrderBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        foreach (var match in matches.Take(MaximumEqualizationMatches))
            EqualizationMatches.Add(match);
        EqualizationSearchStatus = matches.Length switch
        {
            0 => "Kein Eintrag gefunden. Eine eigene AutoEq-Datei importieren oder ohne Entzerrung messen.",
            > MaximumEqualizationMatches => $"{matches.Length} Treffer, die ersten {MaximumEqualizationMatches} werden gezeigt.",
            _ => $"{matches.Length} Treffer."
        };
    }

    partial void OnSelectedEqualizationMatchChanged(HeadphoneEqualizationCatalogEntry? value)
    {
        if (value is null || equalizationCatalog is null || loadedEqualizationCatalog is null)
            return;
        try
        {
            Equalization = equalizationCatalog.CreateEqualization(loadedEqualizationCatalog, value);
        }
        catch (Exception exception)
        {
            StatusMessage = $"Die Entzerrung konnte nicht übernommen werden: {exception.Message}";
            return;
        }

        if (string.IsNullOrWhiteSpace(Manufacturer) && string.IsNullOrWhiteSpace(Model))
        {
            var separator = value.Name.IndexOf(' ');
            Manufacturer = separator > 0 ? value.Name[..separator] : value.Name;
            Model = separator > 0 ? value.Name[(separator + 1)..] : "";
        }
        StatusMessage = $"Entzerrung „{value.Name}“ ({value.MeasurementLabel}) übernommen. Wirksam nach dem Speichern des Messprofils.";
    }

    [RelayCommand]
    private void ClearEqualization()
    {
        Equalization = null;
        SelectedEqualizationMatch = null;
        StatusMessage = "Entzerrung entfernt. Wirksam nach dem Speichern des Messprofils.";
    }

    [RelayCommand]
    private void ImportEqualization()
    {
        if (equalizationCatalog?.PickImportFile() is not { } path)
            return;
        try
        {
            Equalization = equalizationCatalog.Import(path);
            SelectedEqualizationMatch = null;
            StatusMessage = $"Entzerrung aus „{System.IO.Path.GetFileName(path)}“ importiert. Wirksam nach dem Speichern des Messprofils.";
        }
        catch (Exception exception)
        {
            StatusMessage = $"Die Datei ist keine gültige AutoEq-Datei: {exception.Message}";
        }
    }

    [RelayCommand]
    private async Task TestChannelAsync(int channel)
    {
        if (SelectedEndpoint is null || IsBusy) return;
        IsBusy = true;
        try
        {
            var channelLabel = channel == 0 ? "Linker" : "Rechter";
            StatusMessage = $"{channelLabel} Kanal wird mit digitaler Absenkung {StartVolumeDb:0.##} dB geprüft …";
            await audio.PlayChannelTestAsync(
                SelectedEndpoint.Id,
                ExclusiveMode,
                channel,
                StartVolumeDb,
                MaximumVolumeDb);
            StatusMessage = "Kanalprüfung abgeschlossen.";
        }
        catch (Exception exception) { StatusMessage = exception.Message; }
        finally { IsBusy = false; }
    }
}
