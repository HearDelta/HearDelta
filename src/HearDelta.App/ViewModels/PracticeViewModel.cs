using System.Collections.ObjectModel;
using System.Security.Cryptography;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HearDelta.App.Services;
using HearDelta.Core;

namespace HearDelta.App.ViewModels;

public enum PracticeStage
{
    Setup,
    Active,
    Results
}

public sealed record PracticeListOption(string Id, string Label, SpeechMaterial Material);

/// <summary>
/// A deliberately separate, feedback-enabled familiarisation flow. It never creates a paired measurement.
/// </summary>
public partial class PracticeViewModel : ObservableObject
{
    private readonly LoadedStimulusPack pack;
    private readonly IAudioEndpointService audioEndpoints;
    private readonly IStimulusPlaybackService playback;
    private readonly IPracticeSessionRepository sessions;
    private readonly Func<DateTimeOffset> now;
    private readonly Func<int> createSeed;
    private IReadOnlyList<StimulusDefinition> orderedStimuli = [];
    private StimulusPlaybackReceipt? currentPlayback;
    private CancellationTokenSource? playbackStopSignal;
    private Guid? personId;

    public ObservableCollection<MeasurementProfile> Profiles { get; } = [];
    public IReadOnlyList<SelectionOption<TestedEar>> EarOptions { get; } =
    [
        new(TestedEar.Left, "Linkes Ohr"),
        new(TestedEar.Right, "Rechtes Ohr")
    ];
    public IReadOnlyList<PracticeListOption> Lists { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PracticeVolumeMinimumDb))]
    [NotifyPropertyChangedFor(nameof(PracticeVolumeMaximumDb))]
    private MeasurementProfile? selectedProfile;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PracticeVolumeText))]
    private double practiceVolumeDb = -60;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EarTone))]
    private SelectionOption<TestedEar> selectedEar;

    [ObservableProperty]
    private PracticeListOption? selectedList;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanStartPractice))]
    private bool setupConfirmed;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSetup))]
    [NotifyPropertyChangedFor(nameof(IsActive))]
    [NotifyPropertyChangedFor(nameof(IsResults))]
    [NotifyPropertyChangedFor(nameof(StepNumber))]
    [NotifyPropertyChangedFor(nameof(CanContinuePractice))]
    [NotifyPropertyChangedFor(nameof(CanPause))]
    [NotifyPropertyChangedFor(nameof(CanReplayStimulus))]
    [NotifyCanExecuteChangedFor(nameof(ContinuePracticeCommand))]
    [NotifyCanExecuteChangedFor(nameof(ReplayStimulusCommand))]
    private PracticeStage stage = PracticeStage.Setup;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSubmitResponse))]
    [NotifyPropertyChangedFor(nameof(SubmitButtonText))]
    [NotifyCanExecuteChangedFor(nameof(SubmitResponseCommand))]
    private string enteredResponse = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSubmitResponse))]
    [NotifyPropertyChangedFor(nameof(CanContinuePractice))]
    [NotifyPropertyChangedFor(nameof(CanReplayStimulus))]
    [NotifyPropertyChangedFor(nameof(CurrentPrompt))]
    [NotifyCanExecuteChangedFor(nameof(SubmitResponseCommand))]
    [NotifyCanExecuteChangedFor(nameof(ContinuePracticeCommand))]
    [NotifyCanExecuteChangedFor(nameof(ReplayStimulusCommand))]
    private bool isAwaitingResponse;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSubmitResponse))]
    [NotifyPropertyChangedFor(nameof(CanContinuePractice))]
    [NotifyPropertyChangedFor(nameof(CanPause))]
    [NotifyPropertyChangedFor(nameof(CanReplayStimulus))]
    [NotifyPropertyChangedFor(nameof(CurrentPrompt))]
    [NotifyCanExecuteChangedFor(nameof(SubmitResponseCommand))]
    [NotifyCanExecuteChangedFor(nameof(ContinuePracticeCommand))]
    [NotifyCanExecuteChangedFor(nameof(ReplayStimulusCommand))]
    private bool isBusy;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CurrentPrompt))]
    private bool playbackFailed;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanPause))]
    [NotifyPropertyChangedFor(nameof(PauseButtonText))]
    [NotifyPropertyChangedFor(nameof(CanContinuePractice))]
    [NotifyPropertyChangedFor(nameof(CanReplayStimulus))]
    [NotifyCanExecuteChangedFor(nameof(ContinuePracticeCommand))]
    [NotifyCanExecuteChangedFor(nameof(ReplayStimulusCommand))]
    private bool isPaused;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasFeedback))]
    [NotifyPropertyChangedFor(nameof(CanContinuePractice))]
    [NotifyPropertyChangedFor(nameof(CanReplayStimulus))]
    [NotifyCanExecuteChangedFor(nameof(ContinuePracticeCommand))]
    [NotifyCanExecuteChangedFor(nameof(ReplayStimulusCommand))]
    [NotifyPropertyChangedFor(nameof(FeedbackTone))]
    private string feedbackTitle = "";

    [ObservableProperty]
    private string feedbackText = "";

    [ObservableProperty]
    private string statusMessage = "Übungsmaterial, Hörseite und Messprofil auswählen.";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ProgressText))]
    [NotifyPropertyChangedFor(nameof(ProgressPercent))]
    private int currentStimulusIndex;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ResultsTitle))]
    [NotifyPropertyChangedFor(nameof(ResultsDescription))]
    [NotifyPropertyChangedFor(nameof(CanContinuePractice))]
    [NotifyCanExecuteChangedFor(nameof(ContinuePracticeCommand))]
    private PracticeSession? currentSession;

    public PracticeViewModel(
        LoadedStimulusPack pack,
        IEnumerable<MeasurementProfile> profiles,
        IAudioEndpointService audioEndpoints,
        IStimulusPlaybackService playback,
        IPracticeSessionRepository sessions,
        Func<DateTimeOffset>? now = null,
        Func<int>? createSeed = null)
    {
        this.pack = pack;
        this.audioEndpoints = audioEndpoints;
        this.playback = playback;
        this.sessions = sessions;
        this.now = now ?? (() => DateTimeOffset.UtcNow);
        this.createSeed = createSeed ?? (() => RandomNumberGenerator.GetInt32(int.MaxValue));
        selectedEar = EarOptions[0];
        Lists = pack.Catalog.Lists
            .Select(list => new PracticeListOption(list.Id, $"{FormatMaterial(list.Material)} · {list.Id}", list.Material))
            .ToArray();
        SelectedList = Lists.FirstOrDefault();
        ReplaceProfiles(profiles);
    }

    public bool IsSetup => Stage == PracticeStage.Setup;
    public bool IsActive => Stage == PracticeStage.Active;
    public bool IsResults => Stage == PracticeStage.Results;
    public BadgeTone EarTone => BadgeTones.ForEar(SelectedEar.Value);
    public bool HasNoProfiles => Profiles.Count == 0;
    public BadgeTone FeedbackTone => FeedbackTitle == "Richtig" ? BadgeTone.Success : BadgeTone.Warning;
    public int StepNumber => Stage switch
    {
        PracticeStage.Setup => 1,
        PracticeStage.Active => 2,
        _ => 3
    };
    public bool CanStartPractice => personId is not null && SelectedProfile is not null && SelectedList is not null && SetupConfirmed;
    public bool CanSubmitResponse => IsAwaitingResponse && !IsBusy;
    public bool HasFeedback => !string.IsNullOrWhiteSpace(FeedbackTitle);
    public bool CanContinuePractice => HasFeedback && !IsAwaitingResponse && !IsBusy && !IsPaused &&
        CurrentSession?.CompletedAt is null && Stage == PracticeStage.Active;
    public bool CanPause => Stage == PracticeStage.Active && (IsPaused || !IsBusy || playbackStopSignal is not null);
    public bool CanReplayStimulus => Stage == PracticeStage.Active && !IsBusy && !IsPaused && !HasFeedback;
    public string PauseButtonText => IsPaused ? "Übung fortsetzen" : "Übung pausieren";
    public string SubmitButtonText => string.IsNullOrWhiteSpace(EnteredResponse) ? "Nicht verstanden" : "Antwort prüfen";
    public double PracticeVolumeMaximumDb => (double)(SelectedProfile?.MaximumVolumeDb ?? 0m);
    public double PracticeVolumeMinimumDb => Math.Min(-96, PracticeVolumeMaximumDb);
    public string PracticeVolumeText => $"{PracticeVolumeDb:0} dBFS";
    public string ProgressText => orderedStimuli.Count == 0 ? "0 von 0" : $"{Math.Min(CurrentStimulusIndex + 1, orderedStimuli.Count)} von {orderedStimuli.Count}";
    public double ProgressPercent
    {
        get => orderedStimuli.Count == 0 ? 0 : 100d * CurrentStimulusIndex / orderedStimuli.Count;
        set
        {
            if (orderedStimuli.Count == 0)
                return;
            var idx = Math.Clamp((int)Math.Round(value * orderedStimuli.Count / 100d), 0, Math.Max(0, orderedStimuli.Count - 1));
            CurrentStimulusIndex = idx;
        }
    }
    public string ResultsTitle => CurrentSession?.AbortedAt is null ? "Übung abgeschlossen" : "Übung abgebrochen";
    public string ResultsDescription => CurrentSession?.AbortedAt is null
        ? "Die Rückmeldungen und Rohantworten bleiben getrennt von allen Messwerten lokal gespeichert."
        : "Die bis zum Abbruch erfassten Übungsantworten bleiben getrennt von allen Messwerten lokal gespeichert.";
    public string CurrentPrompt => PlaybackFailed
        ? "Wiedergabe blockiert – bitte Statusmeldung prüfen"
        : IsAwaitingResponse
            ? "Was haben Sie gehört?"
            : "Stimulus wird abgespielt …";

    public void ReplaceProfiles(IEnumerable<MeasurementProfile> profiles)
    {
        var selectedId = SelectedProfile?.Id;
        Profiles.Clear();
        foreach (var profile in profiles)
            Profiles.Add(profile);
        SelectedProfile = Profiles.FirstOrDefault(profile => profile.Id == selectedId) ?? Profiles.FirstOrDefault();
        OnPropertyChanged(nameof(HasNoProfiles));
    }

    public void SetPerson(PersonProfile? person)
    {
        personId = person?.Id;
        StatusMessage = person is null
            ? "Vor einer Übung muss eine Person ausgewählt werden."
            : $"Übung für {person.DisplayName} vorbereiten.";
        OnPropertyChanged(nameof(CanStartPractice));
    }

    partial void OnSelectedProfileChanged(MeasurementProfile? value)
    {
        if (value is not null)
            PracticeVolumeDb = (double)value.StartVolumeDb;
    }

    partial void OnPracticeVolumeDbChanged(double value)
    {
        var clamped = Math.Clamp(value, PracticeVolumeMinimumDb, PracticeVolumeMaximumDb);
        if (Math.Abs(clamped - value) > double.Epsilon)
            PracticeVolumeDb = clamped;
    }

    [RelayCommand]
    private async Task StartPracticeAsync()
    {
        if (!CanStartPractice || SelectedProfile is null || SelectedList is null)
            return;

        var endpointAvailable = audioEndpoints.GetActiveOutputs().Any(endpoint =>
            string.Equals(endpoint.Id, SelectedProfile.EndpointId, StringComparison.Ordinal));
        if (!endpointAvailable)
        {
            StatusMessage = "Der im Messprofil gespeicherte Ausgang ist nicht verfügbar. Die Übung verwendet kein Ersatzgerät.";
            return;
        }

        var list = pack.Catalog.Lists.Single(list => string.Equals(list.Id, SelectedList.Id, StringComparison.Ordinal));
        var seed = createSeed();
        // A deterministic Fisher-Yates shuffle avoids retaining the catalog order in a practice session.
        var shuffled = list.Items.ToArray();
        var random = new Random(seed);
        for (var index = shuffled.Length - 1; index > 0; index--)
        {
            var swap = random.Next(index + 1);
            (shuffled[index], shuffled[swap]) = (shuffled[swap], shuffled[index]);
        }
        orderedStimuli = shuffled;
        CurrentSession = PracticeSessionFactory.Create(
            SelectedEar.Value,
            SelectedList.Material,
            SelectedList.Id,
            SelectedProfile.CreateSnapshot() with { StartVolumeDb = (decimal)PracticeVolumeDb },
            pack.MaterialIdentity,
            now(),
            seed);
        try
        {
            sessions.Save(personId!.Value, CurrentSession);
        }
        catch (Exception exception)
        {
            CurrentSession = null;
            StatusMessage = $"Übung konnte nicht vorbereitet werden: {exception.Message}";
            return;
        }

        CurrentStimulusIndex = 0;
        EnteredResponse = string.Empty;
        FeedbackTitle = string.Empty;
        FeedbackText = string.Empty;
        PlaybackFailed = false;
        IsPaused = false;
        Stage = PracticeStage.Active;
        StatusMessage = "Übung gestartet. Nach jeder Antwort erscheint eine Rückmeldung; sie geht nie in Messwerte ein.";
        await PlayCurrentStimulusAsync();
    }

    [RelayCommand(CanExecute = nameof(CanSubmitResponse))]
    private async Task SubmitResponseAsync()
    {
        if (CurrentSession is null || CurrentStimulusIndex >= orderedStimuli.Count || currentPlayback is null)
            return;

        IsBusy = true;
        IsAwaitingResponse = false;
        var stimulus = orderedStimuli[CurrentStimulusIndex];
        var enteredText = string.IsNullOrWhiteSpace(EnteredResponse)
            ? "Nicht verstanden"
            : EnteredResponse.Trim();
        var response = new PracticeResponse(
            CurrentStimulusIndex + 1,
            stimulus.Id,
            enteredText,
            now(),
            new StimulusPresentationRecord(
                currentPlayback.CatalogId,
                currentPlayback.CatalogVersion,
                currentPlayback.StimulusId,
                currentPlayback.AudioSha256,
                currentPlayback.EndpointId,
                currentPlayback.EndpointName,
                currentPlayback.RenderMetadata,
                currentPlayback.StartedAt,
                currentPlayback.CompletedAt));
        var completed = CurrentStimulusIndex + 1 >= orderedStimuli.Count;
        var updated = CurrentSession with
        {
            Responses = CurrentSession.Responses.Append(response).ToArray(),
            CompletedAt = completed ? now() : null
        };
        try
        {
            sessions.Save(personId!.Value, updated);
            CurrentSession = updated;
            var correct = string.Equals(response.EnteredText.Trim(), stimulus.CanonicalResponse.Trim(), StringComparison.OrdinalIgnoreCase);
            FeedbackTitle = correct ? "Richtig" : "Noch einmal merken";
            FeedbackText = correct
                ? $"Die Zielantwort war „{stimulus.CanonicalResponse}“."
                : $"Ihre Antwort: „{response.EnteredText}“ · Zielantwort: „{stimulus.CanonicalResponse}“.";
            currentPlayback = null;
            EnteredResponse = string.Empty;
            if (completed)
            {
                Stage = PracticeStage.Results;
                StatusMessage = "Übung vollständig und getrennt von Messungen lokal gespeichert.";
                return;
            }

            CurrentStimulusIndex++;
            StatusMessage = "Rückmeldung angezeigt. Mit „Nächster Stimulus“ fortsetzen.";
        }
        catch (Exception exception)
        {
            IsAwaitingResponse = true;
            StatusMessage = $"Übungsantwort konnte nicht gespeichert werden: {exception.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanReplayStimulus))]
    private Task ReplayStimulusAsync() => PlayCurrentStimulusAsync();

    [RelayCommand]
    private async Task TogglePauseAsync()
    {
        if (!CanPause)
            return;
        if (IsPaused)
        {
            IsPaused = false;
            FeedbackTitle = string.Empty;
            FeedbackText = string.Empty;
            StatusMessage = "Übung wird mit dem aktuellen Wort fortgesetzt.";
            await PlayCurrentStimulusAsync();
            return;
        }

        IsPaused = true;
        PlaybackFailed = false;
        CancelPlayback();
        StatusMessage = "Übung pausiert. Das aktuelle Wort wird beim Fortsetzen erneut abgespielt.";
    }

    [RelayCommand(CanExecute = nameof(CanContinuePractice))]
    private async Task ContinuePracticeAsync()
    {
        FeedbackTitle = string.Empty;
        FeedbackText = string.Empty;
        await PlayCurrentStimulusAsync();
    }

    [RelayCommand]
    private void AbortPractice() => CancelActivePractice();

    [RelayCommand]
    private void NewPractice()
    {
        CancelPlayback();
        CurrentSession = null;
        orderedStimuli = [];
        CurrentStimulusIndex = 0;
        EnteredResponse = string.Empty;
        FeedbackTitle = string.Empty;
        FeedbackText = string.Empty;
        PlaybackFailed = false;
        IsPaused = false;
        IsAwaitingResponse = false;
        Stage = PracticeStage.Setup;
        StatusMessage = "Neue Übung vorbereiten.";
    }

    public void CancelActivePractice()
    {
        if (Stage != PracticeStage.Active || CurrentSession is null)
            return;

        CancelPlayback();
        IsPaused = false;
        var abortedAt = now();
        var aborted = CurrentSession with { CompletedAt = abortedAt, AbortedAt = abortedAt };
        try
        {
            sessions.Save(personId!.Value, aborted);
            CurrentSession = aborted;
            StatusMessage = "Übung abgebrochen. Die bisherigen Antworten wurden getrennt lokal gespeichert.";
        }
        catch (Exception exception)
        {
            CurrentSession = aborted;
            StatusMessage = $"Übung abgebrochen, aber der Abbruchstatus konnte nicht gespeichert werden: {exception.Message}";
        }
        Stage = PracticeStage.Results;
    }

    private async Task PlayCurrentStimulusAsync()
    {
        if (CurrentSession is null || CurrentStimulusIndex >= orderedStimuli.Count || Stage != PracticeStage.Active || IsPaused)
            return;

        IsAwaitingResponse = false;
        PlaybackFailed = false;
        currentPlayback = null;
        playbackStopSignal?.Dispose();
        var stopSignal = new CancellationTokenSource();
        playbackStopSignal = stopSignal;
        IsBusy = true;
        try
        {
            var receipt = await playback.PlayAsync(
                pack,
                orderedStimuli[CurrentStimulusIndex].Id,
                CurrentSession.Hardware,
                new StimulusRenderRequest(
                    CurrentSession.Ear,
                    ListeningEnvironment.Quiet,
                    (decimal)PracticeVolumeDb,
                    CurrentSession.Hardware.MaximumVolumeDb,
                    null,
                    CurrentSession.RandomizationSeed ^ (CurrentStimulusIndex + 1),
                    CurrentSession.Hardware.SampleRate),
                stopSignal.Token);
            if (!ReferenceEquals(playbackStopSignal, stopSignal) || Stage != PracticeStage.Active)
                return;
            currentPlayback = receipt;
            IsAwaitingResponse = true;
            StatusMessage = "Bitte die gehörte Zielantwort eingeben. Danach folgt sofort die Rückmeldung.";
        }
        catch (OperationCanceledException) when (stopSignal.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            if (ReferenceEquals(playbackStopSignal, stopSignal) && Stage == PracticeStage.Active)
            {
                PlaybackFailed = true;
                StatusMessage = $"Wiedergabe blockiert: {exception.Message}";
            }
        }
        finally
        {
            if (ReferenceEquals(playbackStopSignal, stopSignal))
            {
                playbackStopSignal = null;
                IsBusy = false;
            }
            stopSignal.Dispose();
        }
    }

    private void CancelPlayback()
    {
        playbackStopSignal?.Cancel();
        playbackStopSignal = null;
        currentPlayback = null;
        IsAwaitingResponse = false;
        IsBusy = false;
    }

    private static string FormatMaterial(SpeechMaterial material) => material switch
    {
        SpeechMaterial.Monosyllables => "Einsilber",
        SpeechMaterial.Polysyllables => "Mehrsilber",
        SpeechMaterial.Numbers => "Zahlen",
        _ => material.ToString()
    };
}
