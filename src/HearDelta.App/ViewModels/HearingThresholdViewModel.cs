using System.Collections.ObjectModel;
using System.Security.Cryptography;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HearDelta.App.Services;
using HearDelta.Core;

namespace HearDelta.App.ViewModels;

public enum HearingThresholdStage
{
    Setup,
    ActiveTest,
    Results
}

public partial class HearingThresholdViewModel : ObservableObject
{
    private readonly IAudioEndpointService audioEndpoints;
    private readonly IThresholdTonePlaybackService playback;
    private readonly IHearingThresholdSessionRepository sessions;
    private readonly IMeasurementAnnotationRepository annotations;
    private readonly Func<DateTimeOffset> now;
    private readonly Func<int> createSeed;
    private TonePlaybackOperation? currentOperation;
    private Guid? personId;
    private string? personName;
    private readonly IReportPrinter? printer;
    private CancellationTokenSource? maskingPreviewStop;

    public ObservableCollection<MeasurementProfile> Profiles { get; } = [];
    public ObservableCollection<HearingThresholdResultRow> Results { get; } = [];
    public IReadOnlyList<SelectionOption<TestedEar>> EarOptions { get; } =
    [
        new(TestedEar.Left, Strings.Common_LeftEar),
        new(TestedEar.Right, Strings.Common_RightEar)
    ];
    public IReadOnlyList<SelectionOption<ThresholdToneOrder>> OrderOptions { get; } =
    [
        new(ThresholdToneOrder.Ascending, Strings.Threshold_OrderCenterOut),
        new(ThresholdToneOrder.Random, Strings.Threshold_OrderRandom)
    ];

    /// <summary>Name und Kommentar des nächsten Hörschwellentests.</summary>
    public MeasurementAnnotationDraft Annotation { get; } = new(HearingThresholdResultPresentation.DefaultName);

    public BadgeTone EarTone => BadgeTones.ForEar(SelectedEar.Value);
    public int StepNumber => Stage switch
    {
        HearingThresholdStage.Setup => 1,
        HearingThresholdStage.ActiveTest => 2,
        _ => 3
    };
    public bool HasNoProfiles => Profiles.Count == 0;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanStartTest))]
    [NotifyPropertyChangedFor(nameof(StartAttenuationText))]
    [NotifyPropertyChangedFor(nameof(MinimumStartLevelDbfs))]
    [NotifyPropertyChangedFor(nameof(MaximumStartLevelDbfs))]
    [NotifyPropertyChangedFor(nameof(MaximumAttenuationText))]
    [NotifyPropertyChangedFor(nameof(DefaultStartLevelText))]
    private MeasurementProfile? selectedProfile;

    /// <summary>Startpegel des 500-Hz-Tons in dBFS; Folgetöne beginnen nie leiser.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StartAttenuationText))]
    [NotifyPropertyChangedFor(nameof(StartLevelText))]
    [NotifyPropertyChangedFor(nameof(IsStartLevelRaised))]
    private double startLevelDbfs = (double)HearingThresholdProtocol.DefaultStartAttenuationDbfs;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EarTone))]
    [NotifyPropertyChangedFor(nameof(MaskedEarText))]
    private SelectionOption<TestedEar> selectedEar;

    [ObservableProperty]
    private SelectionOption<ThresholdToneOrder> selectedOrder;

    /// <summary>Das Gegenohr wird während jedes Tons mit Schmalbandrauschen vertäubt.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SetupConditionText))]
    [NotifyPropertyChangedFor(nameof(CanPreviewMasking))]
    private bool isMaskingEnabled;

    /// <summary>RMS-Pegel des Vertäubungsrauschens in dBFS (digitale Absenkung).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(MaskingLevelText))]
    [NotifyPropertyChangedFor(nameof(SetupConditionText))]
    private double maskingLevelDbfs = (double)ThresholdMaskingProtocol.DefaultLevelDbfs;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanPreviewMasking))]
    [NotifyPropertyChangedFor(nameof(MaskingPreviewButtonText))]
    private bool isMaskingPreviewPlaying;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSetup))]
    [NotifyPropertyChangedFor(nameof(IsActiveTest))]
    [NotifyPropertyChangedFor(nameof(IsResults))]
    [NotifyPropertyChangedFor(nameof(TestPhaseTitle))]
    [NotifyPropertyChangedFor(nameof(ProgressText))]
    [NotifyPropertyChangedFor(nameof(ProgressPercent))]
    [NotifyPropertyChangedFor(nameof(RampDescription))]
    [NotifyPropertyChangedFor(nameof(PresentationDisclosure))]
    [NotifyPropertyChangedFor(nameof(CanRepeatLastMeasurement))]
    [NotifyPropertyChangedFor(nameof(CanPause))]
    [NotifyPropertyChangedFor(nameof(StepNumber))]
    private HearingThresholdStage stage = HearingThresholdStage.Setup;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanStartTest))]
    private bool correctEarConfirmed;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanStartTest))]
    private bool headphoneFitConfirmed;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanStartTest))]
    [NotifyPropertyChangedFor(nameof(CanReportHeard))]
    [NotifyPropertyChangedFor(nameof(CanRepeatLastMeasurement))]
    [NotifyPropertyChangedFor(nameof(CanPause))]
    private bool isBusy;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanReportHeard))]
    [NotifyPropertyChangedFor(nameof(CanPause))]
    private bool isTonePlaying;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanReportHeard))]
    [NotifyPropertyChangedFor(nameof(CanRepeatLastMeasurement))]
    [NotifyPropertyChangedFor(nameof(CanPause))]
    [NotifyPropertyChangedFor(nameof(PauseButtonText))]
    private bool isPaused;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CurrentToneNumber))]
    [NotifyPropertyChangedFor(nameof(ProgressText))]
    [NotifyPropertyChangedFor(nameof(ProgressPercent))]
    private int currentToneIndex;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanRepeatLastMeasurement))]
    [NotifyPropertyChangedFor(nameof(SessionConditionText))]
    [NotifyPropertyChangedFor(nameof(SessionConditionTone))]
    [NotifyPropertyChangedFor(nameof(ResultMaximumAttenuationDbfs))]
    private HearingThresholdSession? currentSession;

    [ObservableProperty]
    private string statusMessage = Strings.Threshold_InitialStatus;

    public HearingThresholdViewModel(
        IEnumerable<MeasurementProfile> profiles,
        IAudioEndpointService audioEndpoints,
        IThresholdTonePlaybackService playback,
        IHearingThresholdSessionRepository sessions,
        Func<DateTimeOffset>? now = null,
        Func<int>? createSeed = null,
        IMeasurementAnnotationRepository? annotations = null,
        IReportPrinter? printer = null)
    {
        this.printer = printer;
        this.annotations = annotations ?? new InMemoryMeasurementAnnotationRepository();
        this.audioEndpoints = audioEndpoints;
        this.playback = playback;
        this.sessions = sessions;
        this.now = now ?? (() => DateTimeOffset.UtcNow);
        this.createSeed = createSeed ?? (() => RandomNumberGenerator.GetInt32(int.MaxValue));
        selectedEar = EarOptions[0];
        selectedOrder = OrderOptions[0];
        foreach (var profile in profiles)
            Profiles.Add(profile);
        SelectedProfile = Profiles.FirstOrDefault();
        StartLevelDbfs = (double)DefaultStartLevel;
    }

    public bool IsSetup => Stage == HearingThresholdStage.Setup;
    public bool IsActiveTest => Stage == HearingThresholdStage.ActiveTest;
    public bool IsResults => Stage == HearingThresholdStage.Results;
    public string MaskedEarText => SelectedEar.Value == TestedEar.Left
        ? Strings.Threshold_NoiseOnRight
        : Strings.Threshold_NoiseOnLeft;
    public string MaskingLevelText => string.Format(Strings.Threshold_MaskingLevelText, HearingThresholdResultPresentation.FormatDbfs(SelectedMaskingLevelDbfs));
    public double MinimumMaskingLevelDbfs => (double)ThresholdMaskingProtocol.MinimumLevelDbfs;
    public double MaximumMaskingLevelDbfs => (double)ThresholdMaskingProtocol.MaximumLevelDbfs;
    public bool CanPreviewMasking => IsMaskingEnabled && SelectedProfile is not null && !IsBusy && IsSetup;
    public string MaskingPreviewButtonText => IsMaskingPreviewPlaying ? Strings.Threshold_StopPreview : Strings.Threshold_PlayPreview;

    /// <summary>Bedingung des vorbereiteten Tests für die Anzeige.</summary>
    public string SetupConditionText => HearingThresholdResultPresentation.MaskingText(
        IsMaskingEnabled ? ThresholdMaskingProtocol.Create(SelectedMaskingLevelDbfs, 0) : null);

    /// <summary>Bedingung des laufenden bzw. angezeigten Tests.</summary>
    public string SessionConditionText => CurrentSession is { } session
        ? HearingThresholdResultPresentation.ConditionText(session)
        : SetupConditionText;

    public BadgeTone SessionConditionTone => CurrentSession is { } session
        ? HearingThresholdResultPresentation.ConditionTone(session)
        : BadgeTone.Neutral;

    private decimal SelectedMaskingLevelDbfs => Math.Clamp(
        decimal.Round((decimal)MaskingLevelDbfs, 0, MidpointRounding.AwayFromZero),
        ThresholdMaskingProtocol.MinimumLevelDbfs,
        ThresholdMaskingProtocol.MaximumLevelDbfs);

    public bool CanStartTest =>
        personId is not null &&
        SelectedProfile is not null &&
        CorrectEarConfirmed &&
        HeadphoneFitConfirmed &&
        !IsBusy &&
        !IsMaskingPreviewPlaying;
    public bool CanReportHeard => IsTonePlaying && !IsBusy && !IsPaused;
    public bool CanPause => IsActiveTest && (!IsBusy || IsTonePlaying || IsPaused);
    public string PauseButtonText => IsPaused ? Strings.Test_Resume : Strings.Test_Pause;
    public bool CanRepeatLastMeasurement =>
        Stage is HearingThresholdStage.ActiveTest or HearingThresholdStage.Results &&
        !WasAborted &&
        !IsBusy &&
        CurrentSession is { } session &&
        session.Observations.LastOrDefault()?.Heard == true;
    public bool WasAborted => CurrentSession?.AbortedAt is not null;
    public int CurrentToneNumber => CurrentToneIndex + 1;
    public string TestPhaseTitle => Strings.Shell_HearingThreshold;
    public string ProgressText => string.Format(Strings.Threshold_Progress, Math.Min(CurrentToneNumber, ToneCount), ToneCount);
    public double ProgressPercent => 100d * CurrentToneIndex / ToneCount;
    public string RampDescription =>
        string.Format(
            Strings.Threshold_RampDescription,
            HearingThresholdProtocol.SignalPattern.GroupPauseMilliseconds,
            HearingThresholdProtocol.SignalPattern.ToneMilliseconds,
            HearingThresholdProtocol.SignalPattern.SignalPauseMilliseconds,
            HearingThresholdProtocol.LevelStepDb,
            HearingThresholdProtocol.ConfirmationStartOffsetDb,
            HearingThresholdProtocol.ToneStartOffsetDb);
    public string MaskingDescription =>
        string.Format(Strings.Threshold_MaskingDescription, ThresholdMaskingProtocol.LeadInMilliseconds / 1000d);
    public string PresentationDisclosure =>
        Strings.Threshold_Disclosure;
    public int ToneCount => HearingThresholdToneCatalog.Create(ThresholdToneOrder.Ascending, 0).Count;
    public string StartAttenuationText => $"{SelectedStartLevelDbfs:0.##}";
    public string StartLevelText => $"{HearingThresholdResultPresentation.FormatDbfs(SelectedStartLevelDbfs)} dBFS";
    public double MinimumStartLevelDbfs => (double)LowestStartLevel;
    public double MaximumStartLevelDbfs => (double)HighestStartLevel;
    public string DefaultStartLevelText => string.Format(Strings.Threshold_DefaultStart, HearingThresholdResultPresentation.FormatDbfs(DefaultStartLevel));
    public bool IsStartLevelRaised => SelectedStartLevelDbfs != DefaultStartLevel;

    /// <summary>Obergrenze der Pegelrampe: die Pegelobergrenze des gewählten Messprofils.</summary>
    private decimal MaximumLevel => SelectedProfile?.MaximumVolumeDb ?? HearingThresholdProtocol.LegacyMaximumAttenuationDbfs;

    /// <summary>-80 dBFS oder der leisere Startpegel des Messprofils, mindestens 18 dB unter der Obergrenze.</summary>
    private decimal DefaultStartLevel => Math.Clamp(
        Math.Min(
            HearingThresholdProtocol.DefaultStartAttenuationDbfs,
            SelectedProfile?.StartVolumeDb ?? HearingThresholdProtocol.DefaultStartAttenuationDbfs),
        LowestStartLevel,
        HighestStartLevel);

    private decimal LowestStartLevel => HearingThresholdProtocol.LowestStartAttenuationFor(
        SelectedProfile?.StartVolumeDb ?? HearingThresholdProtocol.LowestStartAttenuationDbfs);

    private decimal HighestStartLevel => Math.Max(
        LowestStartLevel,
        HearingThresholdProtocol.HighestStartAttenuationFor(MaximumLevel));

    private decimal SelectedStartLevelDbfs => Math.Clamp(
        decimal.Round((decimal)StartLevelDbfs, 0, MidpointRounding.AwayFromZero),
        LowestStartLevel,
        HighestStartLevel);

    [RelayCommand]
    private void ResetStartLevel() => StartLevelDbfs = (double)DefaultStartLevel;
    public string MaximumAttenuationText => $"{MaximumLevel:0.##}";

    /// <summary>Obergrenze des angezeigten Ergebnisses für das Diagramm.</summary>
    public double ResultMaximumAttenuationDbfs => (double)(CurrentSession?.MaximumAttenuationDbfs ?? MaximumLevel);
    public string ResultsTitle => WasAborted
        ? Strings.Threshold_Aborted
        : Strings.Threshold_ResultTitle;
    public string ResultSummary => CurrentSession is null
        ? string.Empty
        : WasAborted
            ? string.Format(Strings.Threshold_HeardOfTested, CurrentSession.Observations.Count(observation => observation.Heard), CurrentSession.Observations.Count)
            : string.Format(Strings.Threshold_HeardOfAll, CurrentSession.Observations.Count(observation => observation.Heard), ToneCount);
    public string CenterResultText => CurrentSession is { } session &&
                                      session.Observations.FirstOrDefault() is { } center
        ? center.ThresholdAttenuationDbfs is { } threshold
            ? string.Format(Strings.Threshold_CenterValue, threshold)
            : string.Format(Strings.Threshold_CenterNotHeard, session.MaximumAttenuationDbfs)
        : Strings.Threshold_CenterNotTested;

    public void ReplaceProfiles(IEnumerable<MeasurementProfile> profiles)
    {
        var selectedId = SelectedProfile?.Id;
        Profiles.Clear();
        foreach (var profile in profiles)
            Profiles.Add(profile);
        SelectedProfile = Profiles.FirstOrDefault(profile => profile.Id == selectedId) ?? Profiles.FirstOrDefault();
        OnPropertyChanged(nameof(HasNoProfiles));
    }

    /// <summary>Schritt 1 des empfohlenen Ablaufs: Ohr wählen.</summary>
    public void ApplyPlanStep(TestedEar ear)
    {
        if (Stage != HearingThresholdStage.Setup)
            return;
        SelectedEar = EarOptions.First(option => option.Value == ear);
    }

    public void SetPerson(PersonProfile? person)
    {
        var personChanged = personId != person?.Id;
        personId = person?.Id;
        personName = person?.DisplayName;
        StatusMessage = person is null
            ? Strings.Threshold_NeedsPerson
            : string.Format(Strings.Threshold_Prepare, person.DisplayName);
        if (personChanged && Stage == HearingThresholdStage.Setup)
            ApplyLastSessionOfPerson();
        OnPropertyChanged(nameof(CanStartTest));
    }

    /// <summary>Übernimmt Ohr, Vertäubung, Reihenfolge und Messprofil des letzten Hörschwellentests.</summary>
    private void ApplyLastSessionOfPerson()
    {
        HearingThresholdSession? latest;
        try
        {
            latest = personId is { } id
                ? sessions.LoadForPerson(id).OrderByDescending(session => session.StartedAt).FirstOrDefault()
                : null;
        }
        catch
        {
            return;
        }
        if (latest is null)
            return;

        SelectedEar = EarOptions.First(option => option.Value == latest.Ear);
        SelectedOrder = OrderOptions.First(option => option.Value == latest.ToneOrder);
        IsMaskingEnabled = latest.Masking is not null;
        if (latest.Masking is { } masking)
            MaskingLevelDbfs = (double)masking.LevelDbfs;
        if (Profiles.FirstOrDefault(profile => profile.Id == latest.Hardware.ProfileId) is { } profile)
            SelectedProfile = profile;
        StartLevelDbfs = (double)latest.StartAttenuationDbfs;
    }

    partial void OnSelectedProfileChanged(MeasurementProfile? value)
    {
        ResetConfirmations();
        StartLevelDbfs = (double)SelectedStartLevelDbfs;
        OnPropertyChanged(nameof(IsStartLevelRaised));
        OnPropertyChanged(nameof(CanPreviewMasking));
    }

    partial void OnSelectedEarChanged(SelectionOption<TestedEar> value) => ResetConfirmations();

    partial void OnIsMaskingEnabledChanged(bool value)
    {
        Annotation.SetDefaultName(HearingThresholdResultPresentation.DefaultNameFor(masked: value));
        if (!value)
            maskingPreviewStop?.Cancel();
    }

    partial void OnIsBusyChanged(bool value) => OnPropertyChanged(nameof(CanPreviewMasking));

    partial void OnStageChanged(HearingThresholdStage value) => OnPropertyChanged(nameof(CanPreviewMasking));

    /// <summary>Spielt das Vertäubungsrauschen 3 s auf dem Gegenohr; ein zweiter Klick beendet die Hörprobe.</summary>
    [RelayCommand]
    private async Task PlayMaskingPreviewAsync()
    {
        if (IsMaskingPreviewPlaying)
        {
            maskingPreviewStop?.Cancel();
            return;
        }
        if (!CanPreviewMasking || SelectedProfile is null)
            return;

        using var stop = new CancellationTokenSource();
        maskingPreviewStop = stop;
        IsMaskingPreviewPlaying = true;
        OnPropertyChanged(nameof(CanStartTest));
        try
        {
            var maskedEar = SelectedEar.Value == TestedEar.Left ? TestedEar.Right : TestedEar.Left;
            await playback.PlayMaskingPreviewAsync(maskedEar, SelectedMaskingLevelDbfs, SelectedProfile.CreateSnapshot(), stop.Token);
            StatusMessage = string.Format(Strings.Threshold_PreviewEnded, MaskingLevelText);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            StatusMessage = string.Format(Strings.Threshold_PreviewBlocked, exception.Message);
        }
        finally
        {
            maskingPreviewStop = null;
            IsMaskingPreviewPlaying = false;
            OnPropertyChanged(nameof(CanStartTest));
        }
    }

    public void CancelActiveTest()
    {
        if (!IsActiveTest)
            return;

        if (currentOperation is { } operation)
        {
            Interlocked.Exchange(ref operation.ResponseClaimed, 1);
            operation.StopSignal.Cancel();
        }
        IsTonePlaying = false;
        IsBusy = false;
        IsPaused = false;
        currentOperation = null;

        if (CurrentSession is null)
            return;

        var abortedAt = now();
        var abortedSession = CurrentSession with
        {
            CompletedAt = abortedAt,
            AbortedAt = abortedAt
        };
        try
        {
            sessions.Save(personId!.Value, abortedSession);
            StatusMessage = Strings.Threshold_AbortedSaved;
        }
        catch (Exception exception)
        {
            StatusMessage = string.Format(Strings.Threshold_AbortSaveFailed, exception.Message);
        }
        CurrentSession = abortedSession;
        PopulateResults(abortedSession);
        Stage = HearingThresholdStage.Results;
        NotifyResultProperties();
    }

    [RelayCommand]
    private async Task StartTestAsync()
    {
        if (!CanStartTest || SelectedProfile is null)
        {
            StatusMessage = Strings.Threshold_ConfirmAll;
            return;
        }

        var profileErrors = MeasurementProfileRules.Validate(SelectedProfile);
        if (profileErrors.Count > 0)
        {
            StatusMessage = string.Join(" ", profileErrors);
            return;
        }

        IReadOnlyList<string> endpointErrors;
        try
        {
            endpointErrors = AudioEndpointBindingRules.Validate(
                SelectedProfile.CreateSnapshot(),
                audioEndpoints.GetActiveOutputs());
        }
        catch (Exception exception)
        {
            StatusMessage = string.Format(Strings.Test_OutputsCheckFailed, exception.Message);
            return;
        }
        if (endpointErrors.Count > 0)
        {
            StatusMessage = string.Join(" ", endpointErrors);
            return;
        }

        var annotationErrors = MeasurementAnnotationRules.Validate(Annotation.CreateAnnotation(Guid.NewGuid(), now()));
        if (annotationErrors.Count > 0)
        {
            StatusMessage = string.Join(" ", annotationErrors);
            return;
        }

        HearingThresholdSession session;
        try
        {
            session = HearingThresholdSessionFactory.Create(
                SelectedEar.Value,
                SelectedOrder.Value,
                SelectedProfile.CreateSnapshot(),
                now(),
                createSeed(),
                IsMaskingEnabled ? SelectedMaskingLevelDbfs : null,
                SelectedStartLevelDbfs);
        }
        catch (ArgumentOutOfRangeException exception)
        {
            StatusMessage = exception.Message;
            return;
        }
        var rangeErrors = HearingThresholdSessionRules.ValidatePlaybackRange(session.Hardware, session.Tones);
        if (rangeErrors.Count > 0)
        {
            StatusMessage = string.Join(" ", rangeErrors);
            return;
        }

        try
        {
            sessions.Save(personId!.Value, session);
        }
        catch (Exception exception)
        {
            StatusMessage = string.Format(Strings.Threshold_SessionCreateFailed, exception.Message);
            return;
        }
        var annotationWarning = string.Empty;
        try
        {
            annotations.Save(Annotation.CreateAnnotation(session.Id, now()));
        }
        catch (Exception exception)
        {
            annotationWarning = string.Format(Strings.Annotation_SaveFailedSuffix, exception.Message);
        }

        Results.Clear();
        CurrentSession = session;
        CurrentToneIndex = 0;
        Stage = HearingThresholdStage.ActiveTest;
        IsPaused = false;
        StatusMessage = Strings.Threshold_Starting + annotationWarning;
        await BeginSystematicToneAsync();
    }

    [RelayCommand]
    private async Task HeardAsync()
    {
        var operation = currentOperation;
        if (!CanReportHeard || operation is null ||
            Interlocked.CompareExchange(ref operation.ResponseClaimed, 1, 0) != 0)
            return;

        IsTonePlaying = false;
        IsBusy = true;
        operation.StopSignal.Cancel();
        try
        {
            var receipt = await operation.PlaybackTask;
            await CompleteOperationAsync(operation, receipt, heard: true);
        }
        catch (Exception exception)
        {
            HandlePlaybackFailure(operation, exception);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task RepeatLastMeasurementAsync()
    {
        if (!CanRepeatLastMeasurement || CurrentSession is null)
            return;

        IsBusy = true;
        var operation = currentOperation;
        if (operation is not null)
        {
            Interlocked.Exchange(ref operation.ResponseClaimed, 1);
            currentOperation = null;
            IsTonePlaying = false;
            operation.StopSignal.Cancel();
            try
            {
                await operation.PlaybackTask;
            }
            catch (OperationCanceledException)
            {
            }
            catch
            {
                // Die laufende Wiedergabe wird verworfen; maßgeblich ist der gespeicherte vorherige Wert.
            }
            finally
            {
                operation.StopSignal.Dispose();
            }
        }

        var sourceSession = CurrentSession;
        var lastObservation = sourceSession.Observations[^1];
        CurrentToneIndex = lastObservation.PresentationOrder - 1;
        var repeatedSession = sourceSession with
        {
            CompletedAt = null,
            AbortedAt = null,
            Observations = sourceSession.Observations.Take(sourceSession.Observations.Count - 1).ToArray()
        };

        try
        {
            sessions.Save(personId!.Value, repeatedSession);
        }
        catch (Exception exception)
        {
            IsBusy = false;
            StatusMessage = string.Format(Strings.Threshold_ResetFailed, exception.Message);
            return;
        }

        CurrentSession = repeatedSession;
        Results.Clear();
        IsBusy = false;
        Stage = HearingThresholdStage.ActiveTest;
        StatusMessage = Strings.Threshold_RepeatingLast;
        await BeginSystematicToneAsync();
    }

    [RelayCommand]
    private void AbortTest() => CancelActiveTest();

    [RelayCommand]
    private async Task TogglePauseAsync()
    {
        if (!CanPause)
            return;

        if (IsPaused)
        {
            IsPaused = false;
            StatusMessage = Strings.Threshold_Resumed;
            await BeginSystematicToneAsync();
            return;
        }

        IsPaused = true;
        IsBusy = true;
        var operation = currentOperation;
        currentOperation = null;
        IsTonePlaying = false;
        if (operation is not null)
        {
            Interlocked.Exchange(ref operation.ResponseClaimed, 1);
            operation.StopSignal.Cancel();
            try
            {
                await operation.PlaybackTask;
            }
            catch (OperationCanceledException)
            {
            }
            catch
            {
                // Eine beim Pausieren verworfene Wiedergabe wird nicht als Messwert übernommen.
            }
            finally
            {
                operation.StopSignal.Dispose();
            }
        }
        IsBusy = false;
        StatusMessage = Strings.Threshold_Paused;
    }

    /// <summary>Druckbarer Bericht des angezeigten Ergebnisses; <c>null</c> ohne Ergebnis.</summary>
    public PrintReport? CreateResultReport() => CurrentSession is { } session
        ? ResultReports.HearingThreshold(
            new HearingThresholdHistoryItem(session, annotation: Annotation.CreateAnnotation(session.Id, now())),
            Results.ToArray(),
            personName,
            now())
        : null;

    [RelayCommand]
    private void PrintResult()
    {
        if (CreateResultReport() is { } report)
            StatusMessage = ResultReports.Print(printer, report, StatusMessage);
    }

    [RelayCommand]
    private void NewTest()
    {
        Results.Clear();
        CurrentSession = null;
        CurrentToneIndex = 0;
        IsPaused = false;
        ResetConfirmations();
        Stage = HearingThresholdStage.Setup;
        StatusMessage = Strings.Threshold_PrepareNew;
    }

    /// <param name="firstDetection">
    /// Erster Durchgang mit Reaktion; gesetzt startet die Bestätigungswiederholung desselben Tons unterhalb dieses Pegels.
    /// </param>
    private Task BeginSystematicToneAsync(ThresholdTonePlaybackReceipt? firstDetection = null)
    {
        if (CurrentSession is null || CurrentToneIndex >= CurrentSession.Tones.Count || IsPaused)
            return Task.CompletedTask;

        currentOperation?.StopSignal.Dispose();
        var stopSignal = new CancellationTokenSource();
        IsTonePlaying = true;
        var tone = CurrentSession.Tones[CurrentToneIndex];
        var startAttenuation = firstDetection is null
            ? CalculateStartAttenuation(CurrentSession, tone)
            : HearingThresholdProtocol.CalculateConfirmationStartAttenuationDbfs(firstDetection.EndAttenuationDbfs);
        var request = new ThresholdTonePlaybackRequest(
            CurrentSession.Ear,
            tone.FrequencyHz,
            startAttenuation,
            CurrentSession.MaximumAttenuationDbfs,
            CurrentSession.LevelStepDb,
            CurrentSession.SignalPattern ?? HearingThresholdProtocol.SignalPattern,
            CurrentSession.Hardware.SampleRate,
            Masking: CurrentSession.Masking);
        var playbackTask = playback.PlayAsync(request, CurrentSession.Hardware, stopSignal.Token);
        var operation = new TonePlaybackOperation(tone, stopSignal, playbackTask, firstDetection);
        currentOperation = operation;
        StatusMessage = firstDetection is null
            ? Strings.Threshold_PressHint
            : Strings.Threshold_ConfirmHint;
        _ = ObserveNaturalCompletionAsync(operation);
        return Task.CompletedTask;
    }

    private async Task ObserveNaturalCompletionAsync(TonePlaybackOperation operation)
    {
        try
        {
            var receipt = await operation.PlaybackTask;
            if (!ReferenceEquals(currentOperation, operation) ||
                Interlocked.CompareExchange(ref operation.ResponseClaimed, 1, 0) != 0)
                return;
            IsTonePlaying = false;
            await CompleteOperationAsync(operation, receipt, heard: false);
        }
        catch (Exception exception)
        {
            if (ReferenceEquals(currentOperation, operation) &&
                Interlocked.CompareExchange(ref operation.ResponseClaimed, 1, 0) == 0)
                HandlePlaybackFailure(operation, exception);
        }
    }

    private Task CompleteOperationAsync(
        TonePlaybackOperation operation,
        ThresholdTonePlaybackReceipt receipt,
        bool heard) => CompleteSystematicToneAsync(operation, receipt, heard);

    private async Task CompleteSystematicToneAsync(
        TonePlaybackOperation operation,
        ThresholdTonePlaybackReceipt receipt,
        bool heard)
    {
        if (CurrentSession is null || !ReferenceEquals(currentOperation, operation))
            return;

        var tone = operation.Tone;
        var expectedTone = CurrentSession.Tones[CurrentToneIndex];
        if (tone.PresentationOrder != expectedTone.PresentationOrder)
            return;

        if (heard && operation.FirstDetection is null)
        {
            await BeginSystematicToneAsync(firstDetection: receipt);
            return;
        }

        var firstDetection = operation.FirstDetection;
        var observation = new HearingThresholdObservation(
            tone.PresentationOrder,
            tone.MidiNoteNumber,
            heard,
            heard ? receipt.EndAttenuationDbfs : null,
            now(),
            CreatePresentation(firstDetection ?? receipt),
            firstDetection is null ? null : CreatePresentation(receipt));
        var completesSession = CurrentToneIndex + 1 >= CurrentSession.Tones.Count;
        var updatedSession = CurrentSession with
        {
            CompletedAt = completesSession ? now() : null,
            Observations = CurrentSession.Observations.Append(observation).ToArray()
        };

        try
        {
            sessions.Save(personId!.Value, updatedSession);
        }
        catch (Exception exception)
        {
            StatusMessage = string.Format(Strings.Threshold_ValueSaveFailed, exception.Message);
            return;
        }

        CurrentSession = updatedSession;
        if (completesSession)
        {
            IsPaused = false;
            PopulateResults(updatedSession);
            Stage = HearingThresholdStage.Results;
            NotifyResultProperties();
            StatusMessage = Strings.Threshold_Completed;
            return;
        }

        CurrentToneIndex++;
        await BeginSystematicToneAsync();
    }

    private static ThresholdTonePresentationRecord CreatePresentation(ThresholdTonePlaybackReceipt receipt) => new(
        receipt.EndpointId,
        receipt.EndpointName,
        receipt.FrequencyHz,
        receipt.StartAttenuationDbfs,
        receipt.EndAttenuationDbfs,
        receipt.MaximumAttenuationDbfs,
        receipt.LevelStepDb,
        receipt.OutputSampleRate,
        receipt.StartedAt,
        receipt.CompletedAt,
        receipt.ReachedMaximum,
        receipt.SignalPattern,
        receipt.HeadphoneCorrectionDb,
        receipt.MaskingLevelDbfs);

    private static decimal CalculateStartAttenuation(
        HearingThresholdSession session,
        HearingThresholdTone tone)
    {
        var heardTones = session.Observations
            .Where(observation => observation.ThresholdAttenuationDbfs.HasValue)
            .Select(observation =>
            {
                var observedTone = session.Tones.Single(candidate =>
                    candidate.PresentationOrder == observation.PresentationOrder);
                return (
                    observedTone.FrequencyHz,
                    observation.ThresholdAttenuationDbfs!.Value);
            });
        return HearingThresholdProtocol.CalculateToneStartAttenuationDbfs(
            session.StartAttenuationDbfs,
            tone.FrequencyHz,
            heardTones);
    }

    private void PopulateResults(HearingThresholdSession session)
    {
        Results.Clear();
        foreach (var result in HearingThresholdResultPresentation.CreateRows(session))
            Results.Add(result);
    }

    private void NotifyResultProperties()
    {
        OnPropertyChanged(nameof(WasAborted));
        OnPropertyChanged(nameof(ResultsTitle));
        OnPropertyChanged(nameof(ResultSummary));
        OnPropertyChanged(nameof(CenterResultText));
    }

    private void HandlePlaybackFailure(TonePlaybackOperation operation, Exception exception)
    {
        if (!ReferenceEquals(currentOperation, operation))
            return;
        IsTonePlaying = false;
        IsBusy = false;
        StatusMessage = string.Format(Strings.Test_PlaybackBlocked, exception.Message);
    }

    private void ResetConfirmations()
    {
        CorrectEarConfirmed = false;
        HeadphoneFitConfirmed = false;
    }

    private sealed class TonePlaybackOperation(
        HearingThresholdTone tone,
        CancellationTokenSource stopSignal,
        Task<ThresholdTonePlaybackReceipt> playbackTask,
        ThresholdTonePlaybackReceipt? firstDetection)
    {
        public HearingThresholdTone Tone { get; } = tone;
        public ThresholdTonePlaybackReceipt? FirstDetection { get; } = firstDetection;
        public CancellationTokenSource StopSignal { get; } = stopSignal;
        public Task<ThresholdTonePlaybackReceipt> PlaybackTask { get; } = playbackTask;
        public int ResponseClaimed;
    }
}
