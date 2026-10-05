using System.Collections.ObjectModel;
using System.Globalization;
using System.Security.Cryptography;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HearDelta.App.Services;
using HearDelta.Core;

namespace HearDelta.App.ViewModels;

public enum MeasurementStage
{
    Setup,
    Preparation,
    ActiveTest,
    Results
}

public sealed record SelectionOption<T>(T Value, string Label);

public sealed record ResponseAlternativeOption(int Index, string Text);
public sealed record MeasurementMaterialOption(string Label, SpeechMaterial Material, LoadedStimulusPack Pack, bool FreeNumericResponse);

/// <summary>
/// Zeitliche Abstände im aktiven Worttest: Pause zwischen Antwort und nächstem Stimulus sowie Vorlauf des Dauerrauschens
/// vor dem ersten Stimulus eines Blocks.
/// </summary>
public sealed record MeasurementTiming(TimeSpan InterStimulusPause, TimeSpan NoiseLeadIn)
{
    public static readonly MeasurementTiming Default = new(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1.5));

    /// <summary>Ohne Wartezeiten, für Tests.</summary>
    public static readonly MeasurementTiming Immediate = new(TimeSpan.Zero, TimeSpan.Zero);
}

/// <summary>Auswahl eines gespeicherten Hörgeräts; <c>Aid = null</c> bedeutet freie Eingabe.</summary>
public sealed record HearingAidChoice(PersonHearingAid? Aid, string Label);

public partial class MeasurementViewModel : ObservableObject
{
    private LoadedStimulusPack pack;
    private readonly IAudioEndpointService audioEndpoints;
    private readonly IStimulusPlaybackService playback;
    private readonly IMeasurementSessionRepository sessions;
    private readonly IMeasurementSeriesRepository series;
    private readonly IMeasurementAnnotationRepository annotations;
    private readonly Func<DateTimeOffset> now;
    private readonly Func<int> createSeed;
    private readonly TimeSpan testWordDelay;
    private readonly IContinuousNoisePlaybackService? continuousNoise;
    private readonly MeasurementTiming timing;
    private IContinuousNoiseSession? noiseBed;
    private readonly Func<Guid, TestedEar, MeasurementHardwareSnapshot, PretestResults>? loadPretests;
    private string recommendationSource = string.Empty;
    private bool applyingPlanStep;
    private CancellationTokenSource? testWordStopSignal;
    private bool suppressTestWord;
    private MeasurementRunPlan? runPlan;
    private MeasurementSeriesPlan? measurementSeriesPlan;
    private int currentSeriesRoundIndex;
    private StimulusPlaybackReceipt? currentPlayback;
    private CancellationTokenSource? currentPlaybackStopSignal;
    private Guid? personId;
    private string? personName;
    private readonly IReportPrinter? printer;
    private readonly Func<Guid, IReadOnlyList<PersonHearingAid>> loadHearingAids;
    private IReadOnlyList<PersonHearingAid> personHearingAids = [];

    public ObservableCollection<MeasurementProfile> Profiles { get; } = [];
    public ObservableCollection<ResponseAlternativeOption> ResponseAlternatives { get; } = [];
    public ObservableCollection<PhonemeConfusionCell> ConfusionCells { get; } = [];
    public ObservableCollection<MeasurementExposureCell> ExposureCells { get; } = [];
    public IReadOnlyList<MeasurementMaterialOption> MaterialOptions { get; }
    public IReadOnlyList<SelectionOption<TestedEar>> EarOptions { get; } =
    [
        new(TestedEar.Left, Strings.Common_LeftEar),
        new(TestedEar.Right, Strings.Common_RightEar)
    ];
    public IReadOnlyList<SelectionOption<ListeningEnvironment>> EnvironmentOptions { get; } =
    [
        new(ListeningEnvironment.Quiet, Strings.Common_Quiet),
        new(ListeningEnvironment.BackgroundNoise, Strings.Common_Noise)
    ];
    public IReadOnlyList<SelectionOption<bool>> LevelModeOptions { get; } =
    [
        new(true, Strings.Measure_Adaptive),
        new(false, Strings.Measure_FixedLevel)
    ];
    public IReadOnlyList<SelectionOption<bool>> ModeOptions { get; } =
    [
        new(false, Strings.Measure_Single),
        new(true, Strings.Measure_Series)
    ];
    public ObservableCollection<HearingAidChoice> HearingAidChoices { get; } = [];

    /// <summary>Name und Kommentar der nächsten Messung.</summary>
    public MeasurementAnnotationDraft Annotation { get; } = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsManualHearingAid))]
    private HearingAidChoice? selectedHearingAidChoice;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSeriesMode))]
    [NotifyPropertyChangedFor(nameof(StartButtonText))]
    private SelectionOption<bool> selectedMode;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(UsesAdaptiveLevel))]
    [NotifyPropertyChangedFor(nameof(SignalToNoiseLabel))]
    [NotifyPropertyChangedFor(nameof(LevelModeDescription))]
    [NotifyPropertyChangedFor(nameof(PresentationVolumeLabel))]
    private SelectionOption<bool> selectedLevelMode;

    /// <summary>Das adaptive Verfahren gibt es vorerst nur für den Zahlentest.</summary>
    public bool IsAdaptiveAvailable => SelectedMaterialOption.Material == SpeechMaterial.Numbers;
    public bool UsesAdaptiveLevel => IsAdaptiveAvailable && SelectedLevelMode.Value;
    public string SignalToNoiseLabel => UsesAdaptiveLevel ? Strings.Measure_StartSnr : Strings.Measure_Snr;
    public string LevelModeDescription => !UsesAdaptiveLevel
        ? Strings.Measure_FixedLevelDescription
        : IsBackgroundNoise
            ? Strings.Measure_AdaptiveNoiseDescription
            : Strings.Measure_AdaptiveQuietDescription;

    public bool IsManualHearingAid => SelectedHearingAidChoice?.Aid is null;
    public bool IsSeriesMode => SelectedMode.Value;
    public string StartButtonText => IsSeriesMode && measurementSeriesPlan is null ? Strings.Measure_PlanSeries : Strings.Measure_Prepare;
    public BadgeTone EarTone => BadgeTones.ForEar(SelectedEar.Value);
    public string EarLabel => SelectedEar.Label;
    public BadgeTone CurrentConditionTone => BadgeTones.ForCondition(CurrentBlock?.Condition ?? HearingAidCondition.WithoutHearingAid);
    public int StepNumber => Stage switch
    {
        MeasurementStage.Setup => 1,
        MeasurementStage.Results => 3,
        _ => 2
    };

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PresentationVolumeMinimumDb))]
    [NotifyPropertyChangedFor(nameof(PresentationVolumeMaximumDb))]
    private MeasurementProfile? selectedProfile;

    /// <summary>
    /// Wiedergabepegel des Worttests (digitale Absenkung). Fester Pegel bzw. Startwert des adaptiven Verfahrens in Ruhe,
    /// Sprachpegel im Störgeräusch. Wird als <c>StartVolumeDb</c> im Hardware-Snapshot der Messung gespeichert.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PresentationVolumeText))]
    private double presentationVolumeDb = -60;

    /// <summary>Herkunft der vorbelegten Lautstärke und des SNR.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasLevelSource))]
    private string levelSourceText = string.Empty;

    public bool HasLevelSource => LevelSourceText.Length > 0;

    /// <summary>Gewählter Schritt des empfohlenen Ablaufs, solange seine Einstellung nicht von Hand geändert wurde.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPlanStep))]
    private string planStepText = string.Empty;

    public bool HasPlanStep => PlanStepText.Length > 0;

    public double PresentationVolumeMaximumDb => (double)(SelectedProfile?.MaximumVolumeDb ?? 0m);
    public double PresentationVolumeMinimumDb => Math.Min((double)AdaptiveTrackProtocol.MinimumSpeechLevelDb, PresentationVolumeMaximumDb);
    public string PresentationVolumeText => string.Format(Strings.Measure_VolumeValue, PresentationVolumeDb);
    public string PresentationVolumeLabel => UsesAdaptiveLevel && !IsBackgroundNoise
        ? Strings.Measure_StartVolumeLabel
        : Strings.Measure_SpeechVolumeLabel;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CurrentMaterialLabel))]
    [NotifyPropertyChangedFor(nameof(UsesFreeNumericResponse))]
    [NotifyPropertyChangedFor(nameof(AiVoiceDisclosure))]
    [NotifyPropertyChangedFor(nameof(ChanceLevelText))]
    private MeasurementMaterialOption selectedMaterialOption;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EarTone))]
    [NotifyPropertyChangedFor(nameof(EarLabel))]
    private SelectionOption<TestedEar> selectedEar;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsBackgroundNoise))]
    [NotifyPropertyChangedFor(nameof(LevelModeDescription))]
    [NotifyPropertyChangedFor(nameof(PresentationVolumeLabel))]
    private SelectionOption<ListeningEnvironment> selectedEnvironment;

    [ObservableProperty]
    private string hearingAidManufacturer = string.Empty;

    [ObservableProperty]
    private string hearingAidModel = string.Empty;

    [ObservableProperty]
    private string hearingAidDisplayName = string.Empty;

    [ObservableProperty]
    private string hearingAidProgramName = Strings.Measure_NotDocumented;

    [ObservableProperty]
    private string hearingAidVolumeState = Strings.Measure_NotDocumented;

    [ObservableProperty]
    private decimal signalToNoiseRatioDb = 5m;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SeriesPlanSummary))]
    private int seriesPairCount = 6;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSetup))]
    [NotifyPropertyChangedFor(nameof(IsPreparation))]
    [NotifyPropertyChangedFor(nameof(IsActiveTest))]
    [NotifyPropertyChangedFor(nameof(IsResults))]
    [NotifyPropertyChangedFor(nameof(CanPause))]
    [NotifyPropertyChangedFor(nameof(StepNumber))]
    private MeasurementStage stage = MeasurementStage.Setup;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanStartBlock))]
    private bool correctEarConfirmed;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanStartBlock))]
    private bool headphoneFitConfirmed;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanAnswer))]
    [NotifyPropertyChangedFor(nameof(CanSubmitNumericAnswer))]
    [NotifyPropertyChangedFor(nameof(CanPause))]
    private bool isBusy;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanAnswer))]
    [NotifyPropertyChangedFor(nameof(CanSubmitNumericAnswer))]
    private bool isAwaitingAnswer;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSubmitNumericAnswer))]
    private string numericAnswer = string.Empty;

    [ObservableProperty]
    private bool playbackFailed;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanAnswer))]
    [NotifyPropertyChangedFor(nameof(CanSubmitNumericAnswer))]
    [NotifyPropertyChangedFor(nameof(CanPause))]
    [NotifyPropertyChangedFor(nameof(PauseButtonText))]
    private bool isPaused;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CurrentBlockNumber))]
    [NotifyPropertyChangedFor(nameof(CurrentConditionLabel))]
    [NotifyPropertyChangedFor(nameof(PreparationTitle))]
    [NotifyPropertyChangedFor(nameof(BlockTitle))]
    [NotifyPropertyChangedFor(nameof(PreparationInstruction))]
    [NotifyPropertyChangedFor(nameof(CurrentConditionTone))]
    [NotifyPropertyChangedFor(nameof(ProgressText))]
    [NotifyPropertyChangedFor(nameof(ProgressPercent))]
    private int currentBlockIndex;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ProgressText))]
    [NotifyPropertyChangedFor(nameof(ProgressPercent))]
    private int currentStimulusIndex;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanAdvanceSeries))]
    [NotifyPropertyChangedFor(nameof(AdvanceSeriesButtonText))]
    private PairedMeasurementSession? currentSession;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(WithoutResultText))]
    [NotifyPropertyChangedFor(nameof(WithResultText))]
    [NotifyPropertyChangedFor(nameof(DifferenceText))]
    [NotifyPropertyChangedFor(nameof(WithoutDetailText))]
    [NotifyPropertyChangedFor(nameof(WithDetailText))]
    private PairedMeasurementResult? result;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ResultsTitle))]
    [NotifyPropertyChangedFor(nameof(ResultsDescription))]
    [NotifyPropertyChangedFor(nameof(IsLiveResult))]
    private bool isStoredResult;

    public bool IsLiveResult => !IsStoredResult;

    [ObservableProperty]
    private string statusMessage = Strings.Measure_InitialStatus;

    public MeasurementViewModel(
        LoadedStimulusPack pack,
        IEnumerable<MeasurementProfile> profiles,
        IAudioEndpointService audioEndpoints,
        IStimulusPlaybackService playback,
        IMeasurementSessionRepository sessions,
        Func<DateTimeOffset>? now = null,
        Func<int>? createSeed = null,
        IMeasurementSeriesRepository? series = null,
        IEnumerable<LoadedStimulusPack>? numberPacks = null,
        Func<Guid, IReadOnlyList<PersonHearingAid>>? loadHearingAids = null,
        IMeasurementAnnotationRepository? annotations = null,
        TimeSpan? testWordDelay = null,
        IContinuousNoisePlaybackService? continuousNoise = null,
        MeasurementTiming? timing = null,
        Func<Guid, TestedEar, MeasurementHardwareSnapshot, PretestResults>? loadPretests = null,
        IReportPrinter? printer = null)
    {
        this.printer = printer;
        this.loadPretests = loadPretests;
        this.continuousNoise = continuousNoise;
        this.timing = timing ?? MeasurementTiming.Immediate;
        this.testWordDelay = testWordDelay ?? TimeSpan.FromMilliseconds(350);
        this.loadHearingAids = loadHearingAids ?? (_ => []);
        this.annotations = annotations ?? new InMemoryMeasurementAnnotationRepository();
        this.pack = pack;
        this.audioEndpoints = audioEndpoints;
        this.playback = playback;
        this.sessions = sessions;
        this.series = series ?? new InMemoryMeasurementSeriesRepository();
        this.now = now ?? (() => DateTimeOffset.UtcNow);
        this.createSeed = createSeed ?? (() => RandomNumberGenerator.GetInt32(int.MaxValue));
        MaterialOptions = [
            new MeasurementMaterialOption(Strings.Measure_PhonemeMaterial, SpeechMaterial.PhonemeContrasts, pack, false),
            .. (numberPacks ?? []).Select(numberPack => new MeasurementMaterialOption(
                string.Format(Strings.Measure_NumberMaterial, numberPack.AudioIndex.Generator.Voice), SpeechMaterial.Numbers, numberPack, true))
        ];
        selectedMaterialOption = MaterialOptions[0];
        selectedEar = EarOptions[0];
        selectedEnvironment = EnvironmentOptions[0];
        selectedMode = ModeOptions[0];
        selectedLevelMode = LevelModeOptions[0];
        foreach (var profile in profiles)
            Profiles.Add(profile);
        SelectedProfile = Profiles.FirstOrDefault();
        RefreshExposure();
    }

    public bool IsSetup => Stage == MeasurementStage.Setup;
    public bool IsPreparation => Stage == MeasurementStage.Preparation;
    public bool IsActiveTest => Stage == MeasurementStage.ActiveTest;
    public bool IsResults => Stage == MeasurementStage.Results;
    public bool WasAborted => CurrentSession?.AbortedAt is not null;
    public bool IsBackgroundNoise => SelectedEnvironment.Value == ListeningEnvironment.BackgroundNoise;
    public bool CanStartBlock => CorrectEarConfirmed && HeadphoneFitConfirmed && !IsBusy;
    public bool CanAnswer => IsAwaitingAnswer && !IsBusy && !IsPaused;
    public bool CanSubmitNumericAnswer => CanAnswer && UsesFreeNumericResponse && CardinalNumberProtocol.IsCanonicalResponse(NumericAnswer);
    public bool CanPause => IsActiveTest && (IsPaused || !IsBusy || currentPlaybackStopSignal is not null);
    public string PauseButtonText => IsPaused ? Strings.Test_Resume : Strings.Test_Pause;
    public int CurrentBlockNumber => CurrentBlockIndex + 1;
    public string CurrentConditionLabel => CurrentBlock?.Condition == HearingAidCondition.WithHearingAid
        ? Strings.Condition_WithAidLower
        : Strings.Condition_WithoutAidLower;
    public string PreparationTitle => string.Format(Strings.Measure_PreparationTitle, CurrentBlockNumber, CurrentConditionLabel);
    public string BlockTitle => string.Format(Strings.Measure_BlockTitle, CurrentBlockNumber);
    public string PreparationInstruction => (CurrentBlock?.Condition == HearingAidCondition.WithHearingAid, IsLeftEar) switch
    {
        (true, true) => Strings.Measure_InsertAidLeft,
        (true, false) => Strings.Measure_InsertAidRight,
        (false, true) => Strings.Measure_NoAidLeft,
        (false, false) => Strings.Measure_NoAidRight
    };
    public string ProgressText => runPlan is null
        ? string.Format(Strings.Measure_Progress, 0, pack.Catalog.ItemsPerList * 2)
        : string.Format(
            Strings.Measure_Progress,
            Math.Min(runPlan.Blocks.Sum(block => block.Stimuli.Count), (CurrentBlockIndex * pack.Catalog.ItemsPerList) + CurrentStimulusIndex + 1),
            runPlan.Blocks.Sum(block => block.Stimuli.Count));
    public double ProgressPercent => runPlan is null
        ? 0
        : 100d * ((CurrentBlockIndex * pack.Catalog.ItemsPerList) + CurrentStimulusIndex) /
          runPlan.Blocks.Sum(block => block.Stimuli.Count);
    public string AiVoiceDisclosure => pack.AudioIndex.Generator.AiGeneratedVoiceDisclosure ??
        Strings.Measure_AiVoice;
    public string ResultsTitle => IsStoredResult
        ? WasAborted ? Strings.Measure_StoredPartial : Strings.Measure_StoredResult
        : WasAborted ? Strings.Measure_Aborted : Strings.Measure_ResultTitle;
    public string ResultsDescription => IsStoredResult && CurrentSession is { } session
        ? string.Format(Strings.Measure_StoredDescription, session.StartedAt.ToLocalTime(), FormatEar(session.Ear), session.HearingAid.DisplayName)
        : WasAborted
            ? Strings.Measure_PartialDescription
            : Strings.Measure_ResultDescription;
    public bool IsAdaptiveResult => Result?.AdaptiveParameter is not null;
    public string WithoutResultText => IsAdaptiveResult
        ? AdaptiveResultText.Threshold(Result!.WithoutHearingAid.Adaptive, Result.AdaptiveParameter)
        : Result?.WithoutHearingAid.TotalResponses > 0
            ? string.Format(Strings.Measure_Percent, Result.WithoutHearingAid.PercentCorrect)
            : "–";
    public string WithResultText => IsAdaptiveResult
        ? AdaptiveResultText.Threshold(Result!.WithHearingAid.Adaptive, Result.AdaptiveParameter)
        : Result?.WithHearingAid.TotalResponses > 0
            ? string.Format(Strings.Measure_Percent, Result.WithHearingAid.PercentCorrect)
            : "–";
    public string DifferenceText => IsAdaptiveResult
        ? AdaptiveResultText.Improvement(Result!.ThresholdImprovementDb)
        : Result is null ||
          Result.WithoutHearingAid.TotalResponses == 0 ||
          Result.WithHearingAid.TotalResponses == 0
            ? "–"
            : string.Format(Strings.Measure_PercentagePoints, Result.DifferencePercentagePoints);
    public string DifferenceLabel => IsAdaptiveResult
        ? Strings.Measure_GainLabel
        : Strings.Measure_DifferenceLabel;
    public string ResultExplanationText => IsAdaptiveResult ? AdaptiveResultText.Explanation : ChanceLevelText;
    public string WithoutDetailText => Result is null
        ? string.Empty
        : IsAdaptiveResult
            ? AdaptiveResultText.Detail(Result.WithoutHearingAid, Result.AdaptiveParameter)
            : Result.WithoutHearingAid.TotalResponses == 0
                ? Strings.Measure_NoAnswerYet
                : string.Format(Strings.Measure_CorrectOf, Result.WithoutHearingAid.CorrectResponses, Result.WithoutHearingAid.TotalResponses);
    public string WithDetailText => Result is null
        ? string.Empty
        : IsAdaptiveResult
            ? AdaptiveResultText.Detail(Result.WithHearingAid, Result.AdaptiveParameter)
            : Result.WithHearingAid.TotalResponses == 0
                ? Strings.Measure_NoAnswerYet
                : string.Format(Strings.Measure_CorrectOf, Result.WithHearingAid.CorrectResponses, Result.WithHearingAid.TotalResponses);
    public string CurrentMaterialLabel => SelectedMaterialOption.Label;
    public bool UsesFreeNumericResponse => SelectedMaterialOption.FreeNumericResponse;
    public string ChanceLevelText => pack.Catalog.ChanceLevelPercent is { } chance
        ? UsesFreeNumericResponse
            ? string.Format(Strings.Measure_ChanceNumbers, chance)
            : string.Format(Strings.Measure_ChanceFive, chance)
        : string.Empty;
    public bool HasConfusionCells => ConfusionCells.Count > 0;
    public bool HasPreparedSeries => measurementSeriesPlan is not null;
    public bool CanAdvanceSeries => measurementSeriesPlan is not null &&
        CurrentSession is { CompletedAt: not null, AbortedAt: null };
    public string AdvanceSeriesButtonText => measurementSeriesPlan is not null &&
        currentSeriesRoundIndex + 1 >= measurementSeriesPlan.Rounds.Count
        ? Strings.Measure_FinishSeries
        : Strings.Measure_NextPair;
    public string SeriesPlanSummary => measurementSeriesPlan is null
        ? Strings.Measure_SeriesOptional
        : string.Format(
            Strings.Measure_SeriesPrepared,
            currentSeriesRoundIndex + 1,
            measurementSeriesPlan.Rounds.Count,
            FormatCondition(measurementSeriesPlan.Rounds[currentSeriesRoundIndex].FirstCondition));

    public void ReplaceProfiles(IEnumerable<MeasurementProfile> profiles)
    {
        var selectedId = SelectedProfile?.Id;
        Profiles.Clear();
        foreach (var profile in profiles)
            Profiles.Add(profile);
        SelectedProfile = Profiles.FirstOrDefault(profile => profile.Id == selectedId) ?? Profiles.FirstOrDefault();
        OnPropertyChanged(nameof(HasNoProfiles));
    }

    public bool HasNoProfiles => Profiles.Count == 0;

    public void SetPerson(PersonProfile? person)
    {
        var personChanged = personId != person?.Id;
        personId = person?.Id;
        personName = person?.DisplayName;
        StatusMessage = person is null
            ? Strings.Measure_NeedsPerson
            : string.Format(Strings.Measure_PrepareFor, person.DisplayName);
        if (personChanged)
        {
            measurementSeriesPlan = null;
            currentSeriesRoundIndex = 0;
            SelectedMode = ModeOptions[0];
            personHearingAids = LoadPersonHearingAids();
            if (Stage == MeasurementStage.Setup)
                ApplyLastSessionOfPerson();
            RebuildHearingAidChoices(keepFields: true);
            RestoreActiveSeries();
            NotifySeriesProperties();
        }
        if (measurementSeriesPlan is null)
            ApplyRecommendedLevels();
        RefreshExposure();
    }

    partial void OnSelectedEarChanged(SelectionOption<TestedEar> value)
    {
        RebuildHearingAidChoices(keepFields: false);
        ApplyRecommendedLevels();
    }

    partial void OnSelectedProfileChanged(MeasurementProfile? value)
    {
        if (value is not null)
            SetPresentationVolumeSilently((double)value.StartVolumeDb);
        PrewarmSpeechLevelStatistics();
        ApplyRecommendedLevels();
    }

    partial void OnSelectedEnvironmentChanged(SelectionOption<ListeningEnvironment> value)
    {
        PrewarmSpeechLevelStatistics();
        OnTestConfigurationChanged();
    }

    partial void OnSelectedLevelModeChanged(SelectionOption<bool> value) => OnTestConfigurationChanged();

    private void OnTestConfigurationChanged()
    {
        if (!applyingPlanStep)
            PlanStepText = string.Empty;
        ApplyRecommendedLevels();
    }

    /// <summary>
    /// Übernimmt einen Schritt des empfohlenen Ablaufs: Material, Umgebung, Pegelverfahren und Ohr; Lautstärke und SNR
    /// folgen aus den Vortests. Alles bleibt danach von Hand änderbar.
    /// </summary>
    public void ApplyPlanStep(TestPlanStep step, TestedEar ear)
    {
        if (Stage != MeasurementStage.Setup || step == TestPlanStep.HearingThreshold)
            return;
        var (material, environment, adaptive) = TestLevelRules.Configuration(step);
        applyingPlanStep = true;
        try
        {
            if (SelectedMaterialOption.Material != material &&
                MaterialOptions.FirstOrDefault(option => option.Material == material) is { } option)
                SelectedMaterialOption = option;
            SelectedEnvironment = EnvironmentOptions.First(value => value.Value == environment);
            SelectedLevelMode = LevelModeOptions.First(value => value.Value == adaptive);
            SelectedEar = EarOptions.First(value => value.Value == ear);
            if (measurementSeriesPlan is null)
                SelectedMode = ModeOptions[0];
            PlanStepText = string.Format(
                ear == TestedEar.Left ? Strings.Measure_PlanStepLeft : Strings.Measure_PlanStepRight, (int)step, TestPlanTexts.Title(step));
        }
        finally
        {
            applyingPlanStep = false;
        }
        ApplyRecommendedLevels();
    }

    /// <summary>Belegt Lautstärke und SNR aus den Vortests des gewählten Ohrs mit kompatiblem Messaufbau vor.</summary>
    private void ApplyRecommendedLevels()
    {
        if (Stage != MeasurementStage.Setup || loadPretests is null || personId is not { } id || SelectedProfile is null)
            return;
        PretestResults pretests;
        try
        {
            pretests = loadPretests(id, SelectedEar.Value, SelectedProfile.CreateSnapshot());
        }
        catch
        {
            pretests = PretestResults.None;
        }
        var recommendation = TestLevelRules.Recommend(
            SelectedMaterialOption.Material,
            SelectedEnvironment.Value,
            UsesAdaptiveLevel,
            pretests);
        var source = recommendation.Source;
        if (recommendation.SpeechLevelDb is { } level)
        {
            var capped = Math.Min(level, SelectedProfile.MaximumVolumeDb);
            SetPresentationVolumeSilently((double)capped);
            if (capped < level)
                source += string.Format(Strings.Measure_CappedToLimit, SelectedProfile.MaximumVolumeDb);
        }
        if (IsBackgroundNoise && recommendation.SignalToNoiseRatioDb is { } snr)
            SignalToNoiseRatioDb = Math.Clamp(snr, AdaptiveTrackProtocol.MinimumSignalToNoiseRatioDb, AdaptiveTrackProtocol.MaximumSignalToNoiseRatioDb);
        recommendationSource = source;
        LevelSourceText = source;
    }

    /// <summary>Berechnet die Sprachpegel-Statistik für das Dauerrauschen vorab, damit der Messstart nicht wartet.</summary>
    private void PrewarmSpeechLevelStatistics()
    {
        if (continuousNoise is null || !IsBackgroundNoise || SelectedProfile is null)
            return;
        var service = continuousNoise;
        var statisticsPack = pack;
        var sampleRate = SelectedProfile.SampleRate;
        _ = Task.Run(() =>
        {
            try
            {
                service.GetSpeechLevelStatistics(statisticsPack, sampleRate);
            }
            catch
            {
                // Fehler werden beim Messstart sichtbar gemeldet.
            }
        });
    }

    partial void OnPresentationVolumeDbChanged(double value)
    {
        var clamped = Math.Clamp(Math.Round(value), PresentationVolumeMinimumDb, PresentationVolumeMaximumDb);
        if (clamped != value)
        {
            PresentationVolumeDb = clamped;
            return;
        }
        if (!suppressTestWord && Stage == MeasurementStage.Setup)
        {
            if (recommendationSource.Length > 0)
                LevelSourceText = string.Format(Strings.Measure_ManualLevel, recommendationSource);
            _ = PlayTestWordAsync();
        }
    }

    private void SetPresentationVolumeSilently(double value)
    {
        suppressTestWord = true;
        try
        {
            PresentationVolumeDb = value;
        }
        finally
        {
            suppressTestWord = false;
        }
    }

    [RelayCommand]
    private Task PlayTestWord() => PlayTestWordAsync(immediately: true);

    /// <summary>
    /// Spielt ein festes Testwort des gewählten Materials mit der eingestellten Lautstärke auf dem geprüften Ohr, im
    /// Störgeräuschtest mit kurzem Rauschen beim (Start-)SNR. Schnelle Änderungen am Regler werden zusammengefasst; eine laufende Wiedergabe wird abgebrochen.
    /// Testwörter zählen nicht als Messdarbietung.
    /// </summary>
    private async Task PlayTestWordAsync(bool immediately = false)
    {
        testWordStopSignal?.Cancel();
        var stopSignal = new CancellationTokenSource();
        testWordStopSignal = stopSignal;
        try
        {
            if (!immediately && testWordDelay > TimeSpan.Zero)
                await Task.Delay(testWordDelay, stopSignal.Token);
            if (SelectedProfile is null)
            {
                StatusMessage = Strings.Measure_ChooseProfileForTestWord;
                return;
            }
            var hardware = SelectedProfile.CreateSnapshot() with { StartVolumeDb = (decimal)PresentationVolumeDb };
            var endpointErrors = AudioEndpointBindingRules.Validate(hardware, audioEndpoints.GetActiveOutputs());
            if (endpointErrors.Count > 0)
            {
                StatusMessage = string.Join(" ", endpointErrors);
                return;
            }
            var testPack = SelectedMaterialOption.Pack;
            var stimulusId = testPack.Catalog.Lists
                .Where(list => list.Material == SelectedMaterialOption.Material)
                .OrderBy(list => list.Id, StringComparer.Ordinal)
                .First().Items[0].Id;
            await playback.PlayAsync(
                testPack,
                stimulusId,
                hardware,
                new StimulusRenderRequest(
                    SelectedEar.Value,
                    SelectedEnvironment.Value,
                    hardware.StartVolumeDb,
                    hardware.MaximumVolumeDb,
                    IsBackgroundNoise ? SignalToNoiseRatioDb : null,
                    0,
                    hardware.SampleRate),
                stopSignal.Token);
            StatusMessage = string.Format(IsLeftEar ? Strings.Measure_TestWordPlayedLeft : Strings.Measure_TestWordPlayedRight, PresentationVolumeText);
        }
        catch (OperationCanceledException) when (stopSignal.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            StatusMessage = string.Format(Strings.Measure_TestWordFailed, exception.Message);
        }
        finally
        {
            if (ReferenceEquals(testWordStopSignal, stopSignal))
                testWordStopSignal = null;
            stopSignal.Dispose();
        }
    }

    partial void OnSelectedHearingAidChoiceChanged(HearingAidChoice? value)
    {
        if (value?.Aid is not { } aid)
            return;
        HearingAidManufacturer = aid.Manufacturer;
        HearingAidModel = aid.Model;
        HearingAidDisplayName = aid.DisplayName;
    }

    partial void OnSelectedModeChanged(SelectionOption<bool> value) => NotifySeriesProperties();

    partial void OnHearingAidDisplayNameChanged(string value) => Annotation.SetDefaultName(value?.Trim() ?? string.Empty);

    private IReadOnlyList<PersonHearingAid> LoadPersonHearingAids()
    {
        try
        {
            return personId is { } id ? loadHearingAids(id) : [];
        }
        catch
        {
            return [];
        }
    }

    /// <summary>
    /// Füllt die Auswahl mit den Hörgeräten der Person für das gewählte Ohr. Mit <paramref name="keepFields"/>
    /// bleiben die Eingabefelder erhalten und die passende Auswahl wird gesucht.
    /// </summary>
    private void RebuildHearingAidChoices(bool keepFields)
    {
        HearingAidChoices.Clear();
        foreach (var aid in personHearingAids.Where(aid => aid.Ear == SelectedEar.Value))
            HearingAidChoices.Add(new HearingAidChoice(aid, aid.DisplayName));
        HearingAidChoices.Add(new HearingAidChoice(null, Strings.Measure_OtherDevice));

        if (keepFields)
        {
            SelectedHearingAidChoice = HearingAidChoices.FirstOrDefault(choice => choice.Aid is { } aid &&
                    string.Equals(aid.Manufacturer.Trim(), HearingAidManufacturer.Trim(), StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(aid.Model.Trim(), HearingAidModel.Trim(), StringComparison.OrdinalIgnoreCase))
                ?? (string.IsNullOrWhiteSpace(HearingAidManufacturer) && HearingAidChoices[0].Aid is not null
                    ? HearingAidChoices[0]
                    : HearingAidChoices[^1]);
            return;
        }

        SelectedHearingAidChoice = HearingAidChoices[0];
        if (SelectedHearingAidChoice.Aid is null)
        {
            HearingAidManufacturer = string.Empty;
            HearingAidModel = string.Empty;
            HearingAidDisplayName = string.Empty;
        }
    }

    /// <summary>Übernimmt Ohr, Material, Umgebung, Messprofil und Hörgerät der letzten Messung der Person.</summary>
    private void ApplyLastSessionOfPerson()
    {
        PairedMeasurementSession? latest;
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

        if (MaterialOptions.FirstOrDefault(option => option.Material == latest.Material &&
                option.Pack.Catalog.Id == latest.MaterialIdentity?.CatalogId) is { } material)
            SelectedMaterialOption = material;
        SelectedEar = EarOptions.First(option => option.Value == latest.Ear);
        SelectedEnvironment = EnvironmentOptions.First(option => option.Value == latest.Environment);
        if (latest.Material == SpeechMaterial.Numbers)
            SelectedLevelMode = LevelModeOptions.First(option => option.Value == latest.IsAdaptive);
        if (Profiles.FirstOrDefault(profile => profile.Id == latest.Hardware.ProfileId) is { } profile)
        {
            SelectedProfile = profile;
            SetPresentationVolumeSilently((double)latest.Hardware.StartVolumeDb);
        }
        if (latest.Blocks.SelectMany(block => block.RawResponses)
                .Select(response => response.Presentation.RenderMetadata.SignalToNoiseRatioDb)
                .FirstOrDefault(value => value is not null) is { } snr)
            SignalToNoiseRatioDb = snr;
        HearingAidManufacturer = latest.HearingAid.Manufacturer;
        HearingAidModel = latest.HearingAid.Model;
        HearingAidDisplayName = latest.HearingAid.DisplayName;
        if (!string.IsNullOrWhiteSpace(latest.HearingAid.ProgramName))
            HearingAidProgramName = latest.HearingAid.ProgramName;
        if (!string.IsNullOrWhiteSpace(latest.HearingAid.VolumeState))
            HearingAidVolumeState = latest.HearingAid.VolumeState;
    }

    partial void OnSelectedMaterialOptionChanged(MeasurementMaterialOption value)
    {
        OnPropertyChanged(nameof(IsAdaptiveAvailable));
        OnPropertyChanged(nameof(UsesAdaptiveLevel));
        OnPropertyChanged(nameof(SignalToNoiseLabel));
        OnPropertyChanged(nameof(LevelModeDescription));
        OnPropertyChanged(nameof(PresentationVolumeLabel));
        if (value is not null && Stage == MeasurementStage.Setup)
        {
            pack = value.Pack;
            PrewarmSpeechLevelStatistics();
            OnTestConfigurationChanged();
        }
        if (value is null || Stage != MeasurementStage.Setup)
            return;
        pack = value.Pack;
        measurementSeriesPlan = null;
        currentSeriesRoundIndex = 0;
        NumericAnswer = string.Empty;
        RefreshExposure();
        NotifySeriesProperties();
    }

    public void ShowStoredResult(PairedMeasurementSession session, PairedMeasurementResult storedResult)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(storedResult);

        var storedMaterial = MaterialOptions.FirstOrDefault(option =>
            string.Equals(option.Pack.Catalog.Id, session.MaterialIdentity.CatalogId, StringComparison.Ordinal));
        if (storedMaterial is not null)
        {
            pack = storedMaterial.Pack;
            SelectedMaterialOption = storedMaterial;
        }

        currentPlaybackStopSignal?.Cancel();
        currentPlaybackStopSignal = null;
        currentPlayback = null;
        SetRunPlan(null);
        ResponseAlternatives.Clear();
        IsBusy = false;
        IsAwaitingAnswer = false;
        IsPaused = false;
        PlaybackFailed = false;
        CurrentSession = session;
        Result = storedResult;
        IsStoredResult = true;
        Stage = MeasurementStage.Results;
        NotifyResultProperties();
        StatusMessage = string.Format(Strings.Measure_StoredResultStatus, session.StartedAt.ToLocalTime());
    }

    private PlannedMeasurementBlock? CurrentBlock => runPlan is not null && CurrentBlockIndex < runPlan.Blocks.Count
        ? runPlan.Blocks[CurrentBlockIndex]
        : null;
    private PlannedStimulus? CurrentStimulus => CurrentBlock is not null && CurrentStimulusIndex < CurrentBlock.Stimuli.Count
        ? CurrentBlock.Stimuli[CurrentStimulusIndex]
        : null;
    private bool IsLeftEar => SelectedEar.Value == TestedEar.Left;

    [RelayCommand]
    private void StartMeasurement()
    {
        if (personId is null)
        {
            StatusMessage = Strings.Measure_NeedsPersonBefore;
            return;
        }
        if (SelectedProfile is null)
        {
            StatusMessage = Strings.Measure_NeedsProfile;
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
        if (string.IsNullOrWhiteSpace(HearingAidManufacturer) ||
            string.IsNullOrWhiteSpace(HearingAidModel) ||
            string.IsNullOrWhiteSpace(HearingAidDisplayName) ||
            string.IsNullOrWhiteSpace(HearingAidProgramName) ||
            string.IsNullOrWhiteSpace(HearingAidVolumeState))
        {
            StatusMessage = Strings.Measure_NeedsAid;
            return;
        }
        if (SelectedMaterialOption.Material == SpeechMaterial.Numbers &&
            IsBackgroundNoise &&
            pack.CardinalNoiseProfile is null)
        {
            StatusMessage = Strings.Measure_NoiseProfileMissing;
            return;
        }
        if (SelectedMaterialOption.Material == SpeechMaterial.Numbers &&
            IsBackgroundNoise &&
            !pack.CardinalNoiseProfile!.FirCoefficientsBySampleRate.ContainsKey(SelectedProfile.SampleRate))
        {
            StatusMessage = Strings.Measure_NoiseProfileSampleRate;
            return;
        }
        if (IsBackgroundNoise && SignalToNoiseRatioDb is < -20m or > 30m)
        {
            StatusMessage = Strings.Measure_SnrRange;
            return;
        }

        var annotationErrors = MeasurementAnnotationRules.Validate(Annotation.CreateAnnotation(Guid.NewGuid(), now()));
        if (annotationErrors.Count > 0)
        {
            StatusMessage = string.Join(" ", annotationErrors);
            return;
        }
        testWordStopSignal?.Cancel();
        var hardware = SelectedProfile.CreateSnapshot() with { StartVolumeDb = (decimal)PresentationVolumeDb };
        (ContinuousNoiseSettings Settings, decimal MaximumSignalToNoiseRatioDb)? noiseSettings = null;
        if (IsBackgroundNoise && continuousNoise is not null)
        {
            try
            {
                var statistics = continuousNoise.GetSpeechLevelStatistics(pack, hardware.SampleRate);
                var settings = ContinuousNoiseProtocol.Create(hardware.StartVolumeDb, SignalToNoiseRatioDb, statistics);
                var maximumSnr = ContinuousNoiseProtocol.MaximumSignalToNoiseRatioDb(settings, statistics, hardware.MaximumVolumeDb);
                if (maximumSnr < SignalToNoiseRatioDb)
                {
                    StatusMessage = string.Format(Strings.Measure_VolumeTooHigh, SignalToNoiseRatioDb - maximumSnr);
                    return;
                }
                noiseSettings = (settings, maximumSnr);
            }
            catch (Exception exception)
            {
                StatusMessage = string.Format(Strings.Measure_NoiseFailed, exception.Message);
                return;
            }
        }

        if (IsSeriesMode && measurementSeriesPlan is null)
        {
            PrepareSeries();
            if (measurementSeriesPlan is null)
                return;
        }
        var plannedRound = IsSeriesMode ? GetCurrentSeriesRound() : null;
        var seed = plannedRound?.SessionSeed ?? createSeed();
        var listIds = plannedRound is null
            ? MeasurementRunPlanner.SelectListIds(pack.Catalog, SelectedMaterialOption.Material, seed)
            : (plannedRound.FirstListId, plannedRound.SecondListId);
        var hearingAid = new HearingAidSnapshot(
            HearingAidIdentity.CreateStableId(HearingAidManufacturer, HearingAidModel, SelectedEar.Value),
            HearingAidManufacturer.Trim(),
            HearingAidModel.Trim(),
            HearingAidDisplayName.Trim(),
            SelectedEar.Value,
            HearingAidProgramName.Trim(),
            HearingAidVolumeState.Trim());
        var session = PairedMeasurementSessionFactory.CreateRandomized(
            SelectedEar.Value,
            hearingAid,
            SelectedMaterialOption.Material,
            SelectedEnvironment.Value,
            listIds.FirstListId,
            listIds.SecondListId,
            hardware,
            now(),
            seed,
            pack.MaterialIdentity,
            UsesAdaptiveLevel
                ? MeasurementSessionContracts.AdaptiveCardinalNumberMeasurement
                : SelectedMaterialOption.Material == SpeechMaterial.Numbers
                    ? MeasurementSessionContracts.CardinalNumberMeasurement
                    : MeasurementSessionContracts.PhonemeContrastMeasurement,
            !UsesAdaptiveLevel
                ? null
                : IsBackgroundNoise
                    ? AdaptiveTrackProtocol.CreateSignalToNoiseTrack(SignalToNoiseRatioDb, noiseSettings?.MaximumSignalToNoiseRatioDb)
                    : AdaptiveTrackProtocol.CreateSpeechLevelTrack(hardware.StartVolumeDb, hardware.MaximumVolumeDb),
            noiseSettings?.Settings);
        var sessionErrors = PairedMeasurementSessionRules.Validate(session);
        if (sessionErrors.Count > 0)
        {
            StatusMessage = string.Join(" ", sessionErrors);
            return;
        }
        var plan = MeasurementRunPlanner.Create(session, pack.Catalog);
        try
        {
            sessions.Save(personId.Value, session);
        }
        catch (Exception exception)
        {
            StatusMessage = string.Format(Strings.Measure_CreateFailed, exception.Message);
            return;
        }
        var annotationWarning = SaveAnnotation(session.Id);
        CurrentSession = session;
        IsStoredResult = false;
        SetRunPlan(plan);
        CurrentBlockIndex = 0;
        CurrentStimulusIndex = 0;
        Result = null;
        ResetPreparationChecks();
        Stage = MeasurementStage.Preparation;
        StatusMessage = plannedRound is null
            ? Strings.Measure_Created
            : string.Format(Strings.Measure_SeriesPairCreated, plannedRound.PairNumber, measurementSeriesPlan!.Rounds.Count);
        StatusMessage += annotationWarning;
    }

    /// <summary>Speichert Name und Kommentar; liefert bei einem Fehler einen Hinweis für die Statuszeile.</summary>
    private string SaveAnnotation(Guid sessionId)
    {
        try
        {
            annotations.Save(Annotation.CreateAnnotation(sessionId, now()));
            return string.Empty;
        }
        catch (Exception exception)
        {
            return string.Format(Strings.Annotation_SaveFailedSuffix, exception.Message);
        }
    }

    [RelayCommand]
    private async Task StartBlockAsync()
    {
        if (!CanStartBlock || CurrentSession is null)
        {
            StatusMessage = Strings.Measure_ConfirmAll;
            return;
        }
        IReadOnlyList<string> endpointErrors;
        try
        {
            endpointErrors = AudioEndpointBindingRules.Validate(
                CurrentSession.Hardware,
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

        var sourceBlock = CurrentSession.Blocks.Single(block => block.PresentationOrder == CurrentBlockNumber);
        var updatedBlock = sourceBlock with
        {
            SetupConfirmation = new MeasurementSetupConfirmation(
                CorrectEarConfirmed,
                HeadphoneFitConfirmed,
                now())
        };
        var updatedSession = CurrentSession with
        {
            Blocks = CurrentSession.Blocks
                .Select(block => block.Id == updatedBlock.Id ? updatedBlock : block)
                .ToArray()
        };
        try
        {
            sessions.Save(personId!.Value, updatedSession);
        }
        catch (Exception exception)
        {
            StatusMessage = string.Format(Strings.Measure_SetupSaveFailed, exception.Message);
            return;
        }
        CurrentSession = updatedSession;

        Stage = MeasurementStage.ActiveTest;
        IsPaused = false;
        await PlayCurrentStimulusAsync();
    }

    [RelayCommand]
    private Task RetryPlaybackAsync() => PlaybackFailed
        ? PlayCurrentStimulusAsync()
        : Task.CompletedTask;

    [RelayCommand]
    private Task SelectAnswerAsync(ResponseAlternativeOption option) =>
        RecordAnswerAsync(option.Text);

    [RelayCommand]
    private async Task SubmitNumericAnswerAsync()
    {
        if (!CanSubmitNumericAnswer)
        {
            StatusMessage = Strings.Measure_EnterValidNumber;
            return;
        }
        var response = NumericAnswer;
        NumericAnswer = string.Empty;
        await RecordAnswerAsync(response);
    }

    [RelayCommand]
    private Task NotUnderstoodAsync() => RecordAnswerAsync(null);

    [RelayCommand]
    private void AbortMeasurement() => CancelActiveTest();

    [RelayCommand]
    private async Task TogglePauseAsync()
    {
        if (!IsActiveTest || IsBusy && currentPlaybackStopSignal is null)
            return;

        if (IsPaused)
        {
            IsPaused = false;
            StatusMessage = Strings.Measure_Resumed;
            await PlayCurrentStimulusAsync();
            return;
        }

        IsPaused = true;
        IsAwaitingAnswer = false;
        PlaybackFailed = false;
        ResponseAlternatives.Clear();
        NumericAnswer = string.Empty;
        NumericAnswer = string.Empty;
        currentPlayback = null;
        var stopSignal = currentPlaybackStopSignal;
        currentPlaybackStopSignal = null;
        OnPropertyChanged(nameof(CanPause));
        stopSignal?.Cancel();
        IsBusy = false;
        StatusMessage = Strings.Measure_Paused;
    }

    public void CancelActiveTest()
    {
        if (Stage is not (MeasurementStage.Preparation or MeasurementStage.ActiveTest) || CurrentSession is null)
            return;

        currentPlaybackStopSignal?.Cancel();
        currentPlaybackStopSignal = null;
        currentPlayback = null;
        IsBusy = false;
        IsAwaitingAnswer = false;
        IsPaused = false;
        PlaybackFailed = false;
        ResponseAlternatives.Clear();
        NumericAnswer = string.Empty;

        var abortedAt = now();
        var abortedSession = CurrentSession with
        {
            CompletedAt = abortedAt,
            AbortedAt = abortedAt
        };
        try
        {
            sessions.Save(personId!.Value, abortedSession);
            StatusMessage = Strings.Measure_AbortedSaved;
        }
        catch (Exception exception)
        {
            StatusMessage = string.Format(Strings.Measure_AbortSaveFailed, exception.Message);
        }

        CurrentSession = abortedSession;
        Result = MeasurementScoring.ScorePartial(abortedSession, pack.Catalog);
        Stage = MeasurementStage.Results;
        NotifyResultProperties();
    }

    /// <summary>Druckbarer Bericht des angezeigten Ergebnisses; <c>null</c> ohne Ergebnis.</summary>
    public PrintReport? CreateResultReport()
    {
        if (CurrentSession is not { } session || Result is null)
            return null;
        MeasurementAnnotation? stored = null;
        try
        {
            stored = annotations.LoadAll().GetValueOrDefault(session.Id);
        }
        catch
        {
            // Ohne gespeicherte Annotation gilt der Name aus dem Einrichten-Schritt.
        }
        var item = new HistorySessionItemViewModel(
            session, Result, null, null, _ => { },
            stored ?? (IsStoredResult ? null : Annotation.CreateAnnotation(session.Id, now())));
        return ResultReports.WordResult(
            ResultsTitle,
            item,
            new WordResultSummary(WithoutResultText, WithoutDetailText, WithResultText, WithDetailText, DifferenceLabel, DifferenceText, ResultExplanationText),
            ConfusionCells.ToArray(),
            personName,
            now());
    }

    [RelayCommand]
    private void PrintResult()
    {
        if (CreateResultReport() is { } report)
            StatusMessage = ResultReports.Print(printer, report, StatusMessage);
    }

    [RelayCommand]
    private void NewMeasurement()
    {
        SetRunPlan(null);
        currentPlayback = null;
        currentPlaybackStopSignal?.Cancel();
        currentPlaybackStopSignal = null;
        CurrentSession = null;
        Result = null;
        IsStoredResult = false;
        ResponseAlternatives.Clear();
        NumericAnswer = string.Empty;
        CurrentBlockIndex = 0;
        CurrentStimulusIndex = 0;
        IsAwaitingAnswer = false;
        IsPaused = false;
        PlaybackFailed = false;
        Stage = MeasurementStage.Setup;
        StatusMessage = measurementSeriesPlan is null
            ? Strings.Measure_PrepareNew
            : string.Format(Strings.Measure_PreparePair, currentSeriesRoundIndex + 1, measurementSeriesPlan.Rounds.Count);
        NotifySeriesProperties();
        // Innerhalb einer Messreihe bleiben Lautstärke und SNR unverändert, damit die Paare vergleichbar bleiben.
        if (measurementSeriesPlan is null)
            ApplyRecommendedLevels();
    }

    [RelayCommand]
    private void PrepareSeries()
    {
        if (personId is null)
        {
            StatusMessage = Strings.Measure_SeriesNeedsPerson;
            return;
        }

        if (SeriesPairCount is < 2 or > 12)
        {
            StatusMessage = Strings.Measure_SeriesPairRange;
            return;
        }

        try
        {
            measurementSeriesPlan = MeasurementSeriesPlanner.Create(
                pack.Catalog,
                SelectedMaterialOption.Material,
                SeriesPairCount,
                createSeed());
            currentSeriesRoundIndex = 0;
            series.Save(personId.Value, new MeasurementSeriesState(measurementSeriesPlan, currentSeriesRoundIndex));
            SelectedMode = ModeOptions[1];
            StatusMessage = SeriesPlanSummary;
            NotifySeriesProperties();
        }
        catch (Exception exception)
        {
            StatusMessage = string.Format(Strings.Measure_SeriesPlanFailed, exception.Message);
        }
    }

    [RelayCommand]
    private void AdvanceSeries()
    {
        if (!CanAdvanceSeries || measurementSeriesPlan is null)
            return;

        currentSeriesRoundIndex++;
        if (currentSeriesRoundIndex >= measurementSeriesPlan.Rounds.Count)
        {
            series.Complete(measurementSeriesPlan.Id);
            measurementSeriesPlan = null;
            currentSeriesRoundIndex = 0;
            SelectedMode = ModeOptions[0];
            NewMeasurement();
            StatusMessage = Strings.Measure_SeriesFinished;
            return;
        }

        series.Save(personId!.Value, new MeasurementSeriesState(measurementSeriesPlan, currentSeriesRoundIndex));
        NewMeasurement();
    }

    [RelayCommand]
    private void ClearSeries()
    {
        if (measurementSeriesPlan is not null)
            series.Delete(measurementSeriesPlan.Id);
        measurementSeriesPlan = null;
        currentSeriesRoundIndex = 0;
        SelectedMode = ModeOptions[0];
        StatusMessage = Strings.Measure_SeriesDiscarded;
        NotifySeriesProperties();
    }

    private MeasurementSeriesRound? GetCurrentSeriesRound() => measurementSeriesPlan is not null &&
        currentSeriesRoundIndex < measurementSeriesPlan.Rounds.Count
            ? measurementSeriesPlan.Rounds[currentSeriesRoundIndex]
            : null;

    private static string FormatCondition(HearingAidCondition condition) => condition == HearingAidCondition.WithHearingAid
        ? Strings.Condition_WithAidLower
        : Strings.Condition_WithoutAidLower;

    private void NotifySeriesProperties()
    {
        OnPropertyChanged(nameof(HasPreparedSeries));
        OnPropertyChanged(nameof(CanAdvanceSeries));
        OnPropertyChanged(nameof(AdvanceSeriesButtonText));
        OnPropertyChanged(nameof(SeriesPlanSummary));
        OnPropertyChanged(nameof(StartButtonText));
    }

    private void RefreshExposure()
    {
        try
        {
            ExposureCells.Clear();
            if (personId is null)
                return;
            foreach (var cell in MeasurementExposureAnalysis.Analyze(sessions.LoadForPerson(personId.Value), pack.Catalog))
                ExposureCells.Add(cell);
        }
        catch
        {
            // Exposition ist eine abgeleitete Ansicht; sie darf keine Messung blockieren.
        }
    }

    private void RestoreActiveSeries()
    {
        if (personId is null)
            return;

        try
        {
            var persisted = series.LoadActive(personId.Value);
            if (persisted is null)
                return;

            if (SelectedMaterialOption.Material != persisted.Plan.Material &&
                MaterialOptions.FirstOrDefault(option => option.Material == persisted.Plan.Material) is { } material)
                SelectedMaterialOption = material;
            measurementSeriesPlan = persisted.Plan;
            currentSeriesRoundIndex = persisted.CurrentRoundIndex;
            SelectedMode = ModeOptions[1];
            StatusMessage = string.Format(Strings.Measure_SeriesRestored, SeriesPlanSummary);
            NotifySeriesProperties();
        }
        catch (Exception exception)
        {
            StatusMessage = string.Format(Strings.Measure_SeriesLoadFailed, exception.Message);
        }
    }

    private async Task PlayCurrentStimulusAsync()
    {
        if (CurrentSession is null || CurrentStimulus is null || IsPaused)
            return;

        IsBusy = true;
        IsAwaitingAnswer = false;
        PlaybackFailed = false;
        currentPlayback = null;
        currentPlaybackStopSignal?.Dispose();
        var playbackStopSignal = new CancellationTokenSource();
        currentPlaybackStopSignal = playbackStopSignal;
        OnPropertyChanged(nameof(CanPause));
        ResponseAlternatives.Clear();
        StatusMessage = Strings.Measure_Playing;
        try
        {
            var (speechLevelDb, signalToNoiseRatioDb) = GetPresentationValues(CurrentSession);

            var renderRequest = new StimulusRenderRequest(
                CurrentSession.Ear,
                CurrentSession.Environment,
                speechLevelDb,
                CurrentSession.Hardware.MaximumVolumeDb,
                signalToNoiseRatioDb,
                CreateNoiseSeed(CurrentSession.RandomizationSeed, CurrentBlockNumber, CurrentStimulus.PresentationOrder),
                CurrentSession.Hardware.SampleRate);
            var completedPlayback = CurrentSession.ContinuousNoise is not null && continuousNoise is not null
                ? await PlayOverContinuousNoiseAsync(CurrentStimulus.StimulusId, signalToNoiseRatioDb!.Value, playbackStopSignal.Token)
                : await playback.PlayAsync(
                    pack,
                    CurrentStimulus.StimulusId,
                    CurrentSession.Hardware,
                    renderRequest,
                    playbackStopSignal.Token);
            if (!ReferenceEquals(currentPlaybackStopSignal, playbackStopSignal) ||
                Stage != MeasurementStage.ActiveTest)
                return;
            currentPlayback = completedPlayback;
            var stimulus = pack.GetStimulus(CurrentStimulus.StimulusId);
            foreach (var alternative in (stimulus.ResponseAlternatives ?? []).OrderBy(value => value.Index))
                ResponseAlternatives.Add(new ResponseAlternativeOption(alternative.Index, alternative.Text));
            IsAwaitingAnswer = true;
            StatusMessage = Strings.Measure_ChooseOne;
        }
        catch (OperationCanceledException) when (playbackStopSignal.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            if (ReferenceEquals(currentPlaybackStopSignal, playbackStopSignal) &&
                Stage == MeasurementStage.ActiveTest)
            {
                PlaybackFailed = true;
                StatusMessage = string.Format(Strings.Test_PlaybackBlocked, exception.Message);
            }
        }
        finally
        {
            if (ReferenceEquals(currentPlaybackStopSignal, playbackStopSignal))
            {
                currentPlaybackStopSignal = null;
                OnPropertyChanged(nameof(CanPause));
                IsBusy = false;
            }
            playbackStopSignal.Dispose();
        }
    }

    private async Task RecordAnswerAsync(string? enteredText)
    {
        if (!CanAnswer || CurrentSession is null || CurrentBlock is null ||
            CurrentStimulus is null || currentPlayback is null)
            return;

        IsBusy = true;
        IsAwaitingAnswer = false;
        try
        {
            var response = new RawMeasurementResponse(
                CurrentStimulus.PresentationOrder,
                CurrentStimulus.StimulusId,
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
            var sourceBlock = CurrentSession.Blocks.Single(block => block.Id == CurrentBlock.BlockId);
            var updatedBlock = sourceBlock with
            {
                RawResponses = sourceBlock.RawResponses.Append(response).ToArray()
            };
            var updatedSession = CurrentSession with
            {
                Blocks = CurrentSession.Blocks
                    .Select(block => block.Id == updatedBlock.Id ? updatedBlock : block)
                    .ToArray()
            };

            var completesSession = CurrentBlockIndex + 1 >= runPlan!.Blocks.Count &&
                CurrentStimulusIndex + 1 >= CurrentBlock.Stimuli.Count;
            if (completesSession)
                updatedSession = updatedSession with { CompletedAt = now() };
            sessions.Save(personId!.Value, updatedSession);
            CurrentSession = updatedSession;
            RefreshExposure();

            if (CurrentStimulusIndex + 1 < CurrentBlock.Stimuli.Count)
            {
                CurrentStimulusIndex++;
                IsBusy = false;
                if (timing.InterStimulusPause > TimeSpan.Zero)
                {
                    var stimulusIndex = CurrentStimulusIndex;
                    StatusMessage = Strings.Measure_AnswerSaved;
                    await Task.Delay(timing.InterStimulusPause);
                    // Während der Pause kann pausiert, fortgesetzt oder abgebrochen worden sein.
                    if (Stage != MeasurementStage.ActiveTest || IsPaused || IsBusy || IsAwaitingAnswer ||
                        CurrentStimulusIndex != stimulusIndex)
                        return;
                }
                await PlayCurrentStimulusAsync();
                return;
            }

            if (CurrentBlockIndex + 1 < runPlan!.Blocks.Count)
            {
                CurrentBlockIndex++;
                CurrentStimulusIndex = 0;
                currentPlayback = null;
                ResponseAlternatives.Clear();
                ResetPreparationChecks();
                Stage = MeasurementStage.Preparation;
                StatusMessage = Strings.Measure_FirstBlockSaved;
                return;
            }

            Result = MeasurementScoring.Score(CurrentSession, pack.Catalog);
            currentPlayback = null;
            ResponseAlternatives.Clear();
            Stage = MeasurementStage.Results;
            NotifyResultProperties();
            StatusMessage = Strings.Measure_Completed;
        }
        catch (Exception exception)
        {
            IsAwaitingAnswer = true;
            StatusMessage = string.Format(Strings.Measure_AnswerSaveFailed, exception.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void ResetPreparationChecks()
    {
        CorrectEarConfirmed = false;
        HeadphoneFitConfirmed = false;
    }

    private void NotifyResultProperties()
    {
        ConfusionCells.Clear();
        if (Result is not null)
        {
            foreach (var cell in PhonemeConfusionAnalysis.Analyze(Result.WithoutHearingAid).Cells)
                ConfusionCells.Add(cell with { SelectedResponse = ConfusionResponseLabel(cell.SelectedResponse) });
            foreach (var cell in PhonemeConfusionAnalysis.Analyze(Result.WithHearingAid).Cells)
                ConfusionCells.Add(cell with { SelectedResponse = ConfusionResponseLabel(cell.SelectedResponse) });
        }
        OnPropertyChanged(nameof(HasConfusionCells));
        OnPropertyChanged(nameof(WasAborted));
        OnPropertyChanged(nameof(ResultsTitle));
        OnPropertyChanged(nameof(ResultsDescription));
        OnPropertyChanged(nameof(WithoutResultText));
        OnPropertyChanged(nameof(WithResultText));
        OnPropertyChanged(nameof(DifferenceText));
        OnPropertyChanged(nameof(WithoutDetailText));
        OnPropertyChanged(nameof(WithDetailText));
        OnPropertyChanged(nameof(IsAdaptiveResult));
        OnPropertyChanged(nameof(DifferenceLabel));
        OnPropertyChanged(nameof(ResultExplanationText));
    }

    /// <summary>
    /// Sprachpegel und SNR des nächsten Stimulus. Im adaptiven Verfahren folgt der veränderliche Wert aus der Regel
    /// der Sitzung und den bisherigen Antworten des laufenden Blocks.
    /// </summary>
    private (decimal SpeechLevelDb, decimal? SignalToNoiseRatioDb) GetPresentationValues(PairedMeasurementSession session)
    {
        var speechLevel = session.Hardware.StartVolumeDb;
        decimal? signalToNoise = session.Environment == ListeningEnvironment.BackgroundNoise ? SignalToNoiseRatioDb : null;
        if (session.AdaptiveTrack is not { } track || CurrentBlock is null)
            return (speechLevel, signalToNoise);

        var block = session.Blocks.Single(value => value.Id == CurrentBlock.BlockId);
        var next = AdaptiveTrackRules.NextValue(
            track,
            MeasurementScoring.CreateAdaptiveTrials(track, block, id => pack.GetStimulus(id).CanonicalResponse));
        return track.Parameter == AdaptiveTrackParameter.SpeechLevel
            ? (next, signalToNoise)
            : (speechLevel, next);
    }

    /// <summary>
    /// Spielt den Stimulus über das Dauerrauschen des Blocks. Läuft noch kein Rauschen (Blockbeginn, nach Pause oder
    /// Wiedergabefehler), wird es gestartet und vor dem Stimulus für den Vorlauf allein abgespielt.
    /// </summary>
    private async Task<StimulusPlaybackReceipt> PlayOverContinuousNoiseAsync(
        string stimulusId,
        decimal signalToNoiseRatioDb,
        CancellationToken cancellationToken)
    {
        var session = CurrentSession!;
        if (noiseBed is null)
        {
            StatusMessage = Strings.Measure_NoiseStarting;
            var started = await continuousNoise!.StartAsync(
                pack,
                session.Hardware,
                session.ContinuousNoise!,
                session.Ear,
                CreateNoiseSeed(session.RandomizationSeed, CurrentBlockNumber, 0),
                cancellationToken);
            if (cancellationToken.IsCancellationRequested || Stage != MeasurementStage.ActiveTest || IsPaused)
            {
                await started.DisposeAsync();
                cancellationToken.ThrowIfCancellationRequested();
                throw new OperationCanceledException(cancellationToken);
            }
            noiseBed = started;
            if (timing.NoiseLeadIn > TimeSpan.Zero)
                await Task.Delay(timing.NoiseLeadIn, cancellationToken);
            StatusMessage = Strings.Measure_Playing;
        }
        return await noiseBed.PlayAsync(stimulusId, signalToNoiseRatioDb, cancellationToken);
    }

    /// <summary>Blendet das Dauerrauschen aus, sobald der aktive Block verlassen oder pausiert wird.</summary>
    private void StopNoiseBed()
    {
        var bed = noiseBed;
        noiseBed = null;
        if (bed is not null)
            _ = bed.DisposeAsync().AsTask();
    }

    partial void OnStageChanged(MeasurementStage value)
    {
        if (value != MeasurementStage.ActiveTest)
            StopNoiseBed();
    }

    partial void OnIsPausedChanged(bool value)
    {
        if (value)
            StopNoiseBed();
    }

    /// <summary>
    /// Setzt den Laufplan und meldet alle davon abhängigen Anzeigen neu. Ohne diese Meldung zeigte Block 1 die
    /// Hörgerätebedingung der vorherigen Anzeige, weil der Blockindex beim Start unverändert 0 bleibt.
    /// </summary>
    private void SetRunPlan(MeasurementRunPlan? plan)
    {
        runPlan = plan;
        OnPropertyChanged(nameof(CurrentConditionLabel));
        OnPropertyChanged(nameof(CurrentConditionTone));
        OnPropertyChanged(nameof(PreparationTitle));
        OnPropertyChanged(nameof(PreparationInstruction));
        OnPropertyChanged(nameof(BlockTitle));
        OnPropertyChanged(nameof(ProgressText));
        OnPropertyChanged(nameof(ProgressPercent));
    }

    /// <summary>
    /// Anzeigetext einer Auswahl in der Verwechslungstabelle. Gewählte Stimuluswörter bleiben in der Sprache des
    /// Stimuluspakets; nur die Sammelkategorien werden übersetzt.
    /// </summary>
    internal static string ConfusionResponseLabel(string selectedResponse) => selectedResponse switch
    {
        PhonemeConfusionAnalysis.NotUnderstood => Strings.Measure_NotUnderstood,
        PhonemeConfusionAnalysis.OtherResponse => Strings.Confusion_OtherResponse,
        _ => selectedResponse
    };

    private static int CreateNoiseSeed(int sessionSeed, int blockNumber, int presentationOrder) =>
        unchecked((sessionSeed * 397) ^ (blockNumber * 101) ^ presentationOrder);

    private static string FormatEar(TestedEar ear) => ear == TestedEar.Left ? Strings.Common_LeftEarLower : Strings.Common_RightEarLower;
}
