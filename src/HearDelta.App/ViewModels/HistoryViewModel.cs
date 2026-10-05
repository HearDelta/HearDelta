using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HearDelta.App.Services;
using HearDelta.Core;

namespace HearDelta.App.ViewModels;

public enum HistoryCompletionFilter
{
    All,
    Completed,
    Aborted,
    InProgress
}

public sealed record HistoryEarFilterOption(string Label, TestedEar? Value);
public sealed record HistoryDeviceFilterOption(string Label, Guid? Value);
public sealed record HistoryPeriodFilterOption(string Label, int? Days);
public sealed record HistoryCompletionFilterOption(string Label, HistoryCompletionFilter Value);
public sealed record HistoryMaterialFilterOption(string Label, SpeechMaterial? Value);
public sealed record HistoryEnvironmentFilterOption(string Label, ListeningEnvironment? Value);

/// <summary>Verlaufseintrag mit nachträglich änderbarem Namen und Kommentar.</summary>
public abstract class AnnotatedHistoryItem : ObservableObject
{
    private string name = string.Empty;
    private string? comment;

    public abstract Guid Id { get; }
    /// <summary>Vorgabename aus dem Protokoll: das verwendete Hörgerät.</summary>
    public abstract string DefaultName { get; }

    public string Name
    {
        get => name;
        private set => SetProperty(ref name, value);
    }

    public string? Comment
    {
        get => comment;
        private set
        {
            if (SetProperty(ref comment, value))
                OnPropertyChanged(nameof(HasComment));
        }
    }

    public bool HasComment => !string.IsNullOrWhiteSpace(Comment);

    /// <summary>Ohne gespeicherte Annotation gilt das verwendete Hörgerät als Name.</summary>
    public void ApplyAnnotation(MeasurementAnnotation? annotation)
    {
        Name = annotation?.Name ?? DefaultName;
        Comment = annotation?.Comment;
    }
}

public sealed class HearingThresholdHistoryItem : AnnotatedHistoryItem
{
    private readonly Action<HearingThresholdHistoryItem>? comparisonChanged;
    private bool isComparisonSelected;

    public HearingThresholdHistoryItem(
        HearingThresholdSession session,
        Action<HearingThresholdHistoryItem>? comparisonChanged = null,
        MeasurementAnnotation? annotation = null)
    {
        Session = session;
        this.comparisonChanged = comparisonChanged;
        ApplyAnnotation(annotation);
    }

    public HearingThresholdSession Session { get; }

    /// <summary>Für den Kurvenvergleich angehakt.</summary>
    public bool IsComparisonSelected
    {
        get => isComparisonSelected;
        set
        {
            if (SetProperty(ref isComparisonSelected, value))
                comparisonChanged?.Invoke(this);
        }
    }

    public void SetComparisonSelectedSilently(bool value) => SetProperty(ref isComparisonSelected, value, nameof(IsComparisonSelected));

    public override Guid Id => Session.Id;
    public override string DefaultName => HearingThresholdResultPresentation.DefaultNameFor(Session);
    public TestedEar Ear => Session.Ear;
    public double MaximumAttenuationDbfs => (double)Session.MaximumAttenuationDbfs;
    public string StartedAtText => Session.StartedAt.ToLocalTime().ToString("dd.MM.yyyy · HH:mm", CultureInfo.GetCultureInfo("de-DE"));
    public string EarText => Session.Ear == TestedEar.Left ? "Linkes Ohr" : "Rechtes Ohr";
    public string ConditionText => HearingThresholdResultPresentation.ConditionText(Session);
    public string StatusText => Session.AbortedAt is not null
        ? "Abgebrochen"
        : Session.CompletedAt is not null
            ? "Abgeschlossen"
            : "Unvollständig";
    public string ResultText => $"{Session.Observations.Count(observation => observation.Heard)} von {Session.Observations.Count} geprüften Tönen gehört";
    public BadgeTone EarTone => BadgeTones.ForEar(Session.Ear);
    public string EarLetter => BadgeTones.EarLetter(Session.Ear);
    public BadgeTone ConditionTone => HearingThresholdResultPresentation.ConditionTone(Session);
    public BadgeTone StatusTone => BadgeTones.ForStatus(Session.CompletedAt, Session.AbortedAt);
    public string ProfileText => Session.Hardware.ProfileName;
}

/// <summary>Eine Zeile der Frequenztabelle im Vergleich zweier Hörschwellentests.</summary>
public sealed record ThresholdComparisonRowItem(string FrequencyText, string FirstText, string SecondText, string DifferenceText, BadgeTone DifferenceTone);

public sealed class HistorySessionItemViewModel : AnnotatedHistoryItem
{
    private static readonly CultureInfo German = CultureInfo.GetCultureInfo("de-DE");
    private readonly Action<HistorySessionItemViewModel> selectionChanged;
    private bool isSelected;

    public HistorySessionItemViewModel(
        PairedMeasurementSession session,
        PairedMeasurementResult? result,
        PairedMeasurementEstimate? estimate,
        string? scoringError,
        Action<HistorySessionItemViewModel> selectionChanged,
        MeasurementAnnotation? annotation = null)
    {
        Session = session;
        Result = result;
        Estimate = estimate;
        ScoringError = scoringError;
        this.selectionChanged = selectionChanged;
        ApplyAnnotation(annotation);
    }

    public PairedMeasurementSession Session { get; }
    public PairedMeasurementResult? Result { get; }
    public PairedMeasurementEstimate? Estimate { get; }
    public string? ScoringError { get; }
    public override Guid Id => Session.Id;
    public override string DefaultName => MeasurementAnnotationRules.DefaultName(Session.HearingAid);
    public Guid HearingAidId => Session.HearingAid.DeviceId;
    public string StartedAtText => Session.StartedAt.ToLocalTime().ToString("dd.MM.yyyy HH:mm", German);
    public string EarText => Session.Ear == TestedEar.Left ? "Links" : "Rechts";
    public string HearingAidText => Session.HearingAid.DisplayName;
    public string MaterialText => Session.Material switch
    {
        SpeechMaterial.Numbers => "Zahlen",
        SpeechMaterial.Monosyllables => "Einsilber",
        SpeechMaterial.Polysyllables => "Mehrsilber",
        SpeechMaterial.PhonemeContrasts => "Phonemkontraste",
        _ => Session.Material.ToString()
    };
    public string EnvironmentText => Session.Environment == ListeningEnvironment.Quiet
        ? "Ruhe"
        : FormatNoiseEnvironment();
    public string StatusText => Session.AbortedAt is not null
        ? "Abgebrochen"
        : Session.CompletedAt is null
            ? "Begonnen"
            : Result is null
                ? "Nicht auswertbar"
                : "Abgeschlossen";
    public BadgeTone EarTone => BadgeTones.ForEar(Session.Ear);
    public string EarLetter => BadgeTones.EarLetter(Session.Ear);
    public string MaterialEnvironmentText => Session.IsAdaptive
        ? $"{MaterialText} · {EnvironmentText} · adaptiv"
        : $"{MaterialText} · {EnvironmentText}";
    public bool IsAdaptive => Session.IsAdaptive;
    private decimal? ComparableDifference => IsAdaptive
        ? Result?.ThresholdImprovementDb
        : Result is null || Result.WithoutHearingAid.TotalResponses == 0 || Result.WithHearingAid.TotalResponses == 0
            ? null
            : Result.DifferencePercentagePoints;
    public BadgeTone DifferenceTone => ComparableDifference switch
    {
        > 0 => BadgeTone.WithAid,
        < 0 => BadgeTone.Danger,
        _ => BadgeTone.Neutral
    };
    public BadgeTone StatusTone => Session.AbortedAt is not null
        ? BadgeTone.Warning
        : Result is not null && Session.CompletedAt is not null
            ? BadgeTone.Success
            : BadgeTone.Neutral;
    public string HardwareText => Session.Hardware.ProfileName;
    public string ScoreText => Result is null
        ? "–"
        : IsAdaptive
            ? $"{AdaptiveResultText.Threshold(Result.WithoutHearingAid.Adaptive, Result.AdaptiveParameter)} → {AdaptiveResultText.Threshold(Result.WithHearingAid.Adaptive, Result.AdaptiveParameter)}"
            : $"{FormatBlockScore(Result.WithoutHearingAid)} → {FormatBlockScore(Result.WithHearingAid)}";
    public string DifferenceText => IsAdaptive
        ? AdaptiveResultText.ShortImprovement(Result?.ThresholdImprovementDb)
        : ComparableDifference is { } difference
            ? $"{FormatSigned(difference)} PP"
            : "–";
    public bool CanShowResult => Result is not null;
    public string ScoreDisplayText => HasScores ? ScoreText : "keine Antworten";
    public bool HasScores => Result is not null &&
        (Result.WithoutHearingAid.TotalResponses > 0 || Result.WithHearingAid.TotalResponses > 0);
    public string ResultAvailabilityText => CanShowResult
        ? "Gespeichertes Ergebnis anzeigen"
        : ScoringError ?? "Für diese Messung liegt noch kein Ergebnis vor.";
    public bool CanSelectForComparison => Session.AbortedAt is null && Result is not null && (Estimate is not null || IsAdaptive);
    public string ComparisonAvailabilityText => CanSelectForComparison
        ? "Für den Vergleich auswählbar"
        : Session.AbortedAt is not null
            ? "Abgebrochene Messungen können nicht direkt verglichen werden."
            : ScoringError ?? "Die Messung ist noch nicht abgeschlossen.";
    public string WithoutEstimateText => IsAdaptive
        ? AdaptiveResultText.ThresholdWithSpread(Result?.WithoutHearingAid.Adaptive, Result?.AdaptiveParameter)
        : FormatEstimate(Estimate?.WithoutHearingAid, "%");
    public string WithEstimateText => IsAdaptive
        ? AdaptiveResultText.ThresholdWithSpread(Result?.WithHearingAid.Adaptive, Result?.AdaptiveParameter)
        : FormatEstimate(Estimate?.WithHearingAid, "%");
    public string DifferenceEstimateText => IsAdaptive
        ? AdaptiveResultText.Improvement(Result?.ThresholdImprovementDb)
        : FormatEstimate(Estimate?.DifferencePercentagePoints, "PP", signed: true);
    public string DifferenceCaption => IsAdaptive ? "Gewinn mit Hörgerät (Schwelle ohne minus mit)" : "Differenz mit minus ohne";
    public string ComparisonSubtitle => $"{StartedAtText} · {EarText} · {EnvironmentText}";

    public bool IsSelected
    {
        get => isSelected;
        set
        {
            if (!SetProperty(ref isSelected, value))
                return;
            selectionChanged(this);
        }
    }

    public void SetSelectedSilently(bool value) => SetProperty(ref isSelected, value, nameof(IsSelected));

    private string FormatNoiseEnvironment()
    {
        var values = Session.Blocks.SelectMany(block => block.RawResponses)
            .Select(response => response.Presentation.RenderMetadata.SignalToNoiseRatioDb)
            .Distinct()
            .ToArray();
        return values.Length == 1 && values[0] is { } snr
            ? $"Störgeräusch ({FormatSigned(snr)} dB SNR)"
            : "Störgeräusch";
    }

    private static string FormatEstimate(PercentageEstimate? estimate, string unit, bool signed = false)
    {
        if (estimate is null)
            return "–";
        var point = signed ? FormatSigned(estimate.EstimatePercent) : FormatNumber(estimate.EstimatePercent);
        var lower = signed ? FormatSigned(estimate.Lower95Percent) : FormatNumber(estimate.Lower95Percent);
        var upper = signed ? FormatSigned(estimate.Upper95Percent) : FormatNumber(estimate.Upper95Percent);
        return $"{point} {unit}  ·  95 %-Intervall {lower} bis {upper} {unit}";
    }

    private static string FormatNumber(decimal value) => value.ToString("0.#", German);

    private static string FormatSigned(decimal value) => value.ToString("+0.#;-0.#;0", German);

    private static string FormatBlockScore(MeasurementBlockResult result) => result.TotalResponses == 0
        ? "–"
        : $"{FormatNumber(result.PercentCorrect)} %";
}

public partial class HistoryViewModel : ObservableObject
{
    private readonly IMeasurementSessionRepository repository;
    private readonly IHearingThresholdSessionRepository hearingThresholdRepository;
    private readonly IUserConfirmationService confirmation;
    private readonly IMeasurementAnnotationRepository annotations;
    private readonly IMeasurementAnnotationEditor? annotationEditor;
    private readonly IReadOnlyDictionary<string, LoadedStimulusPack> packsByCatalogId;
    private readonly Func<DateTimeOffset> now;
    private readonly List<HistorySessionItemViewModel> allSessions = [];
    private Guid? personId;
    private string? personName;
    private readonly IReportPrinter? printer;
    private HistoryEarFilterOption selectedEarFilter;
    private HistoryDeviceFilterOption selectedDeviceFilter;
    private HistoryPeriodFilterOption selectedPeriodFilter;
    private HistoryCompletionFilterOption selectedCompletionFilter;
    private HistoryMaterialFilterOption selectedMaterialFilter;
    private HistoryEnvironmentFilterOption selectedEnvironmentFilter;
    private string searchText = string.Empty;

    public event Action<HistorySessionItemViewModel>? ResultRequested;

    public HistoryViewModel(
        IMeasurementSessionRepository repository,
        IHearingThresholdSessionRepository hearingThresholdRepository,
        LoadedStimulusPack pack,
        IUserConfirmationService confirmation,
        Func<DateTimeOffset>? now = null,
        IEnumerable<LoadedStimulusPack>? additionalPacks = null,
        IMeasurementAnnotationRepository? annotations = null,
        IMeasurementAnnotationEditor? annotationEditor = null,
        IReportPrinter? printer = null)
    {
        this.printer = printer;
        this.repository = repository;
        this.annotations = annotations ?? new InMemoryMeasurementAnnotationRepository();
        this.annotationEditor = annotationEditor;
        this.hearingThresholdRepository = hearingThresholdRepository;
        packsByCatalogId = new[] { pack }.Concat(additionalPacks ?? [])
            .ToDictionary(value => value.Catalog.Id, StringComparer.Ordinal);
        this.confirmation = confirmation;
        this.now = now ?? (() => DateTimeOffset.Now);
        selectedEarFilter = EarFilters[0];
        selectedDeviceFilter = DeviceFilters[0];
        selectedPeriodFilter = PeriodFilters[0];
        selectedCompletionFilter = CompletionFilters[0];
        selectedMaterialFilter = MaterialFilters[0];
        selectedEnvironmentFilter = EnvironmentFilters[0];
        Reload();
        RefreshHearingThresholdHistory();
    }

    public ObservableCollection<HistorySessionItemViewModel> Sessions { get; } = [];
    public ObservableCollection<HearingThresholdHistoryItem> HearingThresholdTests { get; } = [];
    public ObservableCollection<HearingThresholdResultRow> SelectedThresholdResults { get; } = [];
    public ObservableCollection<ThresholdChartSeries> ThresholdComparisonSeries { get; } = [];
    public ObservableCollection<ThresholdComparisonRowItem> ThresholdComparisonRows { get; } = [];

    public const int MaximumThresholdComparisonCount = 4;
    private static readonly System.Windows.Media.Color[] ComparisonColors =
    [
        System.Windows.Media.Color.FromRgb(0x5B, 0x47, 0xC8),
        System.Windows.Media.Color.FromRgb(0xB8, 0x5C, 0x00),
        System.Windows.Media.Color.FromRgb(0xA2, 0x3B, 0x8C),
        System.Windows.Media.Color.FromRgb(0x4F, 0x5D, 0x73)
    ];

    public int ThresholdComparisonCount => HearingThresholdTests.Count(item => item.IsComparisonSelected);
    public bool HasThresholdComparison => ThresholdComparisonCount >= 2;
    public bool HasThresholdComparisonSelection => ThresholdComparisonCount > 0;
    public bool IsThresholdSingleView => !HasThresholdComparison && HasThresholdSelection;
    public bool HasThresholdComparisonTable => ThresholdComparisonRows.Count > 0;
    public string ThresholdComparisonFirstLabel { get; private set; } = string.Empty;
    public string ThresholdComparisonSecondLabel { get; private set; } = string.Empty;
    public string ThresholdComparisonWarningText { get; private set; } = string.Empty;
    public bool HasThresholdComparisonWarning => HasThresholdComparison && ThresholdComparisonWarningText.Length > 0;
    public double ThresholdComparisonMaximumDbfs { get; private set; } = -6d;
    public string ThresholdComparisonHint => ThresholdComparisonCount switch
    {
        0 => $"Zum Vergleichen bis zu {MaximumThresholdComparisonCount} Tests anhaken.",
        1 => "Noch mindestens einen weiteren Test anhaken.",
        _ => $"{ThresholdComparisonCount} Tests im Vergleich"
    };

    [RelayCommand]
    private void ClearThresholdComparison()
    {
        foreach (var item in HearingThresholdTests)
            item.SetComparisonSelectedSilently(false);
        UpdateThresholdComparison();
    }

    private void OnThresholdComparisonChanged(HearingThresholdHistoryItem item)
    {
        if (item.IsComparisonSelected && ThresholdComparisonCount > MaximumThresholdComparisonCount)
        {
            item.SetComparisonSelectedSilently(false);
            ThresholdStatusMessage = $"Es können höchstens {MaximumThresholdComparisonCount} Hörschwellentests gleichzeitig verglichen werden.";
        }
        UpdateThresholdComparison();
    }

    private void UpdateThresholdComparison()
    {
        var selected = HearingThresholdTests.Where(item => item.IsComparisonSelected)
            .OrderBy(item => item.Session.StartedAt)
            .ToArray();
        ThresholdComparisonSeries.Clear();
        ThresholdComparisonRows.Clear();
        ThresholdComparisonWarningText = string.Empty;
        ThresholdComparisonFirstLabel = string.Empty;
        ThresholdComparisonSecondLabel = string.Empty;

        if (selected.Length >= 2)
        {
            for (var index = 0; index < selected.Length; index++)
                ThresholdComparisonSeries.Add(new ThresholdChartSeries(
                    ComparisonLabel(index, selected[index]),
                    selected[index].Ear,
                    ComparisonColors[index % ComparisonColors.Length],
                    HearingThresholdResultPresentation.CreateRows(selected[index].Session)));
            ThresholdComparisonMaximumDbfs = selected.Max(item => item.MaximumAttenuationDbfs);
            var differences = HearingThresholdComparisonRules.GetDifferences(selected.Select(item => item.Session).ToArray());
            ThresholdComparisonWarningText = string.Join(" ", differences);

            if (selected.Length == 2)
            {
                ThresholdComparisonFirstLabel = ComparisonLabel(0, selected[0]);
                ThresholdComparisonSecondLabel = ComparisonLabel(1, selected[1]);
                foreach (var row in HearingThresholdComparisonRules.CreateRows(selected[0].Session, selected[1].Session))
                    ThresholdComparisonRows.Add(new ThresholdComparisonRowItem(
                        FormatFrequency(row.FrequencyHz),
                        FormatThresholdValue(row.First),
                        FormatThresholdValue(row.Second),
                        row.ImprovementDb switch
                        {
                            null => "–",
                            > 0 => $"{row.ImprovementDb:0.#} dB besser",
                            < 0 => $"{-row.ImprovementDb:0.#} dB schlechter",
                            _ => "gleich"
                        },
                        row.ImprovementDb switch
                        {
                            > 0 => BadgeTone.Success,
                            < 0 => BadgeTone.Danger,
                            _ => BadgeTone.Neutral
                        }));
            }
        }

        OnPropertyChanged(nameof(ThresholdComparisonCount));
        OnPropertyChanged(nameof(HasThresholdComparison));
        OnPropertyChanged(nameof(HasThresholdComparisonSelection));
        OnPropertyChanged(nameof(IsThresholdSingleView));
        OnPropertyChanged(nameof(HasThresholdComparisonTable));
        OnPropertyChanged(nameof(ThresholdComparisonFirstLabel));
        OnPropertyChanged(nameof(ThresholdComparisonSecondLabel));
        OnPropertyChanged(nameof(ThresholdComparisonWarningText));
        OnPropertyChanged(nameof(HasThresholdComparisonWarning));
        OnPropertyChanged(nameof(ThresholdComparisonMaximumDbfs));
        OnPropertyChanged(nameof(ThresholdComparisonHint));
    }

    /// <summary>Druckbarer Bericht des ausgewählten Hörschwellentests; <c>null</c> ohne Auswahl.</summary>
    public PrintReport? CreateSelectedThresholdReport() => SelectedThresholdTest is { } item
        ? ResultReports.HearingThreshold(item, SelectedThresholdResults.ToArray(), personName, now())
        : null;

    /// <summary>Druckbarer Bericht des Kurvenvergleichs; <c>null</c> mit weniger als zwei Tests.</summary>
    public PrintReport? CreateThresholdComparisonReport() => HasThresholdComparison
        ? ResultReports.ThresholdComparison(
            HearingThresholdTests.Where(item => item.IsComparisonSelected).OrderBy(item => item.Session.StartedAt).ToArray(),
            ThresholdComparisonSeries.ToArray(),
            ThresholdComparisonMaximumDbfs,
            ThresholdComparisonWarningText,
            ThresholdComparisonRows.ToArray(),
            personName,
            now())
        : null;

    /// <summary>Druckbarer Bericht der Gegenüberstellung zweier Worttests; <c>null</c> ohne Vergleich.</summary>
    public PrintReport? CreateWordComparisonReport() => FirstComparison is { } first && SecondComparison is { } second
        ? ResultReports.WordComparison(first, second, IsDirectlyComparable, ComparisonAssessmentText, personName, now())
        : null;

    [RelayCommand]
    private void PrintSelectedThreshold()
    {
        if (CreateSelectedThresholdReport() is { } report)
            ThresholdStatusMessage = ResultReports.Print(printer, report, ThresholdStatusMessage);
    }

    [RelayCommand]
    private void PrintThresholdComparison()
    {
        if (CreateThresholdComparisonReport() is { } report)
            ThresholdStatusMessage = ResultReports.Print(printer, report, ThresholdStatusMessage);
    }

    [RelayCommand]
    private void PrintWordComparison()
    {
        if (CreateWordComparisonReport() is { } report)
            WordStatusMessage = ResultReports.Print(printer, report, WordStatusMessage);
    }

    private static string ComparisonLabel(int index, HearingThresholdHistoryItem item) =>
        $"{index + 1} · {item.Session.StartedAt.ToLocalTime():dd.MM. HH:mm} · {(item.Ear == TestedEar.Left ? "links" : "rechts")} · {(item.Session.IsLegacyWithHearingAid ? "mit HG" : item.Session.Masking is null ? "ohne Vertäubung" : "vertäubt")}";

    private static string FormatFrequency(double frequencyHz) => frequencyHz < 1_000d
        ? $"{frequencyHz.ToString("0.##", CultureInfo.GetCultureInfo("de-DE"))} Hz"
        : $"{(frequencyHz / 1_000d).ToString("0.##", CultureInfo.GetCultureInfo("de-DE"))} kHz";

    private static string FormatThresholdValue(HearingThresholdComparisonValue value) =>
        !value.Tested ? "nicht geprüft"
        : value.Heard && value.ThresholdAttenuationDbfs is { } level ? $"{level.ToString("0.#", CultureInfo.GetCultureInfo("de-DE"))} dBFS"
        : "nicht gehört";
    public IReadOnlyList<HistoryEarFilterOption> EarFilters { get; } =
    [
        new("Alle Ohren", null),
        new("Links", TestedEar.Left),
        new("Rechts", TestedEar.Right)
    ];
    public ObservableCollection<HistoryDeviceFilterOption> DeviceFilters { get; } = [new("Alle Hörgeräte", null)];
    public IReadOnlyList<HistoryPeriodFilterOption> PeriodFilters { get; } =
    [
        new("Gesamter Zeitraum", null),
        new("Letzte 30 Tage", 30),
        new("Letzte 90 Tage", 90),
        new("Letztes Jahr", 365)
    ];
    public IReadOnlyList<HistoryCompletionFilterOption> CompletionFilters { get; } =
    [
        new("Alle Status", HistoryCompletionFilter.All),
        new("Abgeschlossen", HistoryCompletionFilter.Completed),
        new("Abgebrochen", HistoryCompletionFilter.Aborted),
        new("Begonnen", HistoryCompletionFilter.InProgress)
    ];
    public IReadOnlyList<HistoryMaterialFilterOption> MaterialFilters { get; } =
    [
        new("Alle Materialien", null),
        new("Phonemkontraste", SpeechMaterial.PhonemeContrasts),
        new("Zahlen", SpeechMaterial.Numbers),
        new("Einsilber", SpeechMaterial.Monosyllables),
        new("Mehrsilber", SpeechMaterial.Polysyllables)
    ];
    public IReadOnlyList<HistoryEnvironmentFilterOption> EnvironmentFilters { get; } =
    [
        new("Alle Umgebungen", null),
        new("Ruhe", ListeningEnvironment.Quiet),
        new("Störgeräusch", ListeningEnvironment.BackgroundNoise)
    ];

    public HistoryEarFilterOption SelectedEarFilter
    {
        get => selectedEarFilter;
        set { if (SetProperty(ref selectedEarFilter, value)) ApplyFilters(); }
    }

    public HistoryDeviceFilterOption SelectedDeviceFilter
    {
        get => selectedDeviceFilter;
        set { if (SetProperty(ref selectedDeviceFilter, value)) ApplyFilters(); }
    }

    public HistoryPeriodFilterOption SelectedPeriodFilter
    {
        get => selectedPeriodFilter;
        set { if (SetProperty(ref selectedPeriodFilter, value)) ApplyFilters(); }
    }

    public HistoryCompletionFilterOption SelectedCompletionFilter
    {
        get => selectedCompletionFilter;
        set { if (SetProperty(ref selectedCompletionFilter, value)) ApplyFilters(); }
    }

    public HistoryMaterialFilterOption SelectedMaterialFilter
    {
        get => selectedMaterialFilter;
        set { if (SetProperty(ref selectedMaterialFilter, value)) ApplyFilters(); }
    }

    public HistoryEnvironmentFilterOption SelectedEnvironmentFilter
    {
        get => selectedEnvironmentFilter;
        set { if (SetProperty(ref selectedEnvironmentFilter, value)) ApplyFilters(); }
    }

    public string SearchText
    {
        get => searchText;
        set { if (SetProperty(ref searchText, value)) ApplyFilters(); }
    }

    /// <summary>Statusmeldung des Worttest-Reiters.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusMessage))]
    private string wordStatusMessage = string.Empty;

    /// <summary>Statusmeldung des Hörschwellen-Reiters.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusMessage))]
    private string thresholdStatusMessage = string.Empty;

    /// <summary>Statusmeldung des gerade sichtbaren Reiters.</summary>
    public string StatusMessage => SelectedTabIndex == 1 ? ThresholdStatusMessage : WordStatusMessage;

    /// <summary>0 = Worttests, 1 = Hörschwellentests.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusMessage))]
    private int selectedTabIndex;

    /// <summary>Alle geladenen Wortmessungen der Person, unabhängig von den Filtern.</summary>
    public IReadOnlyList<HistorySessionItemViewModel> AllSessions => allSessions;

    /// <summary>Wird nach jedem vollständigen Neuladen ausgelöst.</summary>
    public event Action? Reloaded;

    [ObservableProperty]
    private HistorySessionItemViewModel? firstComparison;

    [ObservableProperty]
    private HistorySessionItemViewModel? secondComparison;

    [ObservableProperty]
    private string comparisonAssessmentText = string.Empty;

    [ObservableProperty]
    private bool isDirectlyComparable;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasThresholdSelection))]
    [NotifyPropertyChangedFor(nameof(SelectedThresholdTitle))]
    [NotifyPropertyChangedFor(nameof(SelectedThresholdConditionText))]
    [NotifyPropertyChangedFor(nameof(SelectedThresholdHardwareText))]
    [NotifyPropertyChangedFor(nameof(SelectedThresholdProtocolText))]
    private HearingThresholdHistoryItem? selectedThresholdTest;

    public int TotalSessionCount => allSessions.Count;
    public int FilteredSessionCount => Sessions.Count;
    public bool HasSessions => TotalSessionCount > 0;
    public bool HasFilteredSessions => FilteredSessionCount > 0;
    public bool HasComparison => FirstComparison is not null && SecondComparison is not null;
    public bool HasComparisonWarning => HasComparison && !IsDirectlyComparable;
    public bool HasDirectComparison => HasComparison && IsDirectlyComparable;
    public string ResultCountText => $"{FilteredSessionCount} von {TotalSessionCount} Messungen";
    public string SelectionText => $"{allSessions.Count(session => session.IsSelected)} von 2 Messungen für den Vergleich ausgewählt";
    public int SelectedCount => allSessions.Count(session => session.IsSelected);
    public bool HasSelection => SelectedCount > 0;
    public string SelectionHintText => SelectedCount switch
    {
        0 => "Zum Vergleichen zwei abgeschlossene Messungen anhaken.",
        1 => "Noch eine zweite abgeschlossene Messung anhaken.",
        _ => "Vergleich der beiden Messungen:"
    };

    /// <summary>Anzahl der aktiven Filter außerhalb der sichtbaren Schnellfilter (Ohr, Material).</summary>
    public int ActiveMoreFilterCount =>
        (SelectedDeviceFilter.Value is null ? 0 : 1) +
        (SelectedPeriodFilter.Days is null ? 0 : 1) +
        (SelectedCompletionFilter.Value == HistoryCompletionFilter.All ? 0 : 1) +
        (SelectedEnvironmentFilter.Value is null ? 0 : 1) +
        (string.IsNullOrWhiteSpace(SearchText) ? 0 : 1);
    public string MoreFiltersHeader => ActiveMoreFilterCount == 0 ? "Weitere Filter" : $"Weitere Filter ({ActiveMoreFilterCount} aktiv)";
    public bool HasAnyFilter => ActiveMoreFilterCount > 0 || SelectedEarFilter.Value is not null || SelectedMaterialFilter.Value is not null;

    [RelayCommand]
    private void ResetFilters()
    {
        selectedEarFilter = EarFilters[0];
        selectedDeviceFilter = DeviceFilters[0];
        selectedPeriodFilter = PeriodFilters[0];
        selectedCompletionFilter = CompletionFilters[0];
        selectedMaterialFilter = MaterialFilters[0];
        selectedEnvironmentFilter = EnvironmentFilters[0];
        searchText = string.Empty;
        OnPropertyChanged(nameof(SelectedEarFilter));
        OnPropertyChanged(nameof(SelectedDeviceFilter));
        OnPropertyChanged(nameof(SelectedPeriodFilter));
        OnPropertyChanged(nameof(SelectedCompletionFilter));
        OnPropertyChanged(nameof(SelectedMaterialFilter));
        OnPropertyChanged(nameof(SelectedEnvironmentFilter));
        OnPropertyChanged(nameof(SearchText));
        ApplyFilters();
    }
    public bool HasHearingThresholdTests => HearingThresholdTests.Count > 0;
    public bool HasNoHearingThresholdTests => !HasHearingThresholdTests;
    public bool HasThresholdSelection => SelectedThresholdTest is not null;
    public BadgeTone SelectedThresholdConditionTone => SelectedThresholdTest?.ConditionTone ?? BadgeTone.Neutral;
    public string SelectedThresholdTitle => SelectedThresholdTest is null
        ? string.Empty
        : $"Hörschwellentest vom {SelectedThresholdTest.StartedAtText}";
    public string SelectedThresholdConditionText => SelectedThresholdTest is { } test
        ? test.Session.IsLegacyWithHearingAid
            ? $"{test.ConditionText}: {test.Session.HearingAid?.DisplayName}"
            : test.ConditionText
        : string.Empty;
    public string SelectedThresholdHardwareText => SelectedThresholdTest is null
        ? string.Empty
        : $"{SelectedThresholdTest.Session.Hardware.ProfileName} · {SelectedThresholdTest.Session.Hardware.EndpointName} · {SelectedThresholdTest.Session.Hardware.HeadphoneManufacturer} {SelectedThresholdTest.Session.Hardware.HeadphoneModel}";
    public string SelectedThresholdProtocolText => SelectedThresholdTest is null
        ? string.Empty
        : $"Protokoll v{SelectedThresholdTest.Session.ProtocolVersion} · {FormatThresholdOrder(SelectedThresholdTest.Session)} · Pegelobergrenze {SelectedThresholdTest.Session.MaximumAttenuationDbfs:0.##} dBFS";

    [RelayCommand]
    public void ReloadAll()
    {
        Reload();
        RefreshHearingThresholdHistory();
        Reloaded?.Invoke();
    }

    public void SetPerson(PersonProfile? person)
    {
        personId = person?.Id;
        personName = person?.DisplayName;
        ReloadAll();
    }

    [RelayCommand]
    public void Reload()
    {
        var selectedIds = allSessions.Where(session => session.IsSelected).Select(session => session.Id).Take(2).ToHashSet();
        try
        {
            allSessions.Clear();
            var sessions = personId is null ? [] : repository.LoadForPerson(personId.Value);
            var storedAnnotations = LoadAnnotations();
            foreach (var session in sessions.OrderByDescending(session => session.StartedAt))
            {
                PairedMeasurementResult? result = null;
                PairedMeasurementEstimate? estimate = null;
                string? scoringError = null;
                if (session.CompletedAt is not null)
                {
                    try
                    {
                        var sessionPack = ResolvePack(session);
                        result = session.AbortedAt is not null
                            ? MeasurementScoring.ScorePartial(session, sessionPack.Catalog)
                            : MeasurementScoring.Score(session, sessionPack.Catalog);
                        if (session.AbortedAt is null && !session.IsAdaptive)
                            estimate = MeasurementUncertainty.Estimate(result);
                    }
                    catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or KeyNotFoundException)
                    {
                        scoringError = $"Nicht mit dem aktiven Stimuluspaket auswertbar: {exception.Message}";
                    }
                }

                var item = new HistorySessionItemViewModel(
                    session, result, estimate, scoringError, HandleSelectionChanged, storedAnnotations.GetValueOrDefault(session.Id));
                item.SetSelectedSilently(selectedIds.Contains(item.Id) && item.CanSelectForComparison);
                allSessions.Add(item);
            }
            RebuildDeviceFilters();
            ApplyFilters();
            UpdateComparison();
            if (repository is IRepositoryReadDiagnostics { LastReadErrors.Count: > 0 } diagnostics)
                WordStatusMessage = $"{diagnostics.LastReadErrors.Count} gespeicherte Wortmessung(en) sind nicht lesbar; die übrigen bleiben verfügbar. ID: {diagnostics.LastReadErrors[0].RecordId}.";
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or InvalidOperationException)
        {
            allSessions.Clear();
            Sessions.Clear();
            WordStatusMessage = $"Messverlauf konnte nicht geladen werden: {exception.Message}";
            NotifySummaryChanged();
        }
    }

    private LoadedStimulusPack ResolvePack(PairedMeasurementSession session)
    {
        var catalogId = session.MaterialIdentity.CatalogId;
        if (string.IsNullOrWhiteSpace(catalogId) || !packsByCatalogId.TryGetValue(catalogId, out var pack))
            throw new InvalidOperationException("Das zugehörige Stimuluspaket ist nicht installiert.");
        return pack;
    }

    [RelayCommand]
    private void ClearComparison()
    {
        foreach (var session in allSessions.Where(session => session.IsSelected))
            session.SetSelectedSilently(false);
        UpdateComparison();
    }

    [RelayCommand]
    private void ShowResult(HistorySessionItemViewModel? item)
    {
        if (item?.Result is null)
        {
            WordStatusMessage = item?.ResultAvailabilityText ?? "Keine Messung ausgewählt.";
            return;
        }

        ResultRequested?.Invoke(item);
        WordStatusMessage = $"Gespeichertes Ergebnis vom {item.StartedAtText} geöffnet.";
    }

    [RelayCommand]
    private void RefreshHearingThresholdHistory()
    {
        var selectedId = SelectedThresholdTest?.Id;
        IReadOnlyList<HearingThresholdSession> loaded;
        try
        {
            loaded = personId is null ? [] : hearingThresholdRepository.LoadForPerson(personId.Value);
        }
        catch (Exception exception)
        {
            ThresholdStatusMessage = $"Hörschwellenverlauf konnte nicht geladen werden: {exception.Message}";
            return;
        }

        var storedAnnotations = LoadAnnotations();
        HearingThresholdTests.Clear();
        foreach (var session in loaded.OrderByDescending(session => session.StartedAt))
            HearingThresholdTests.Add(new HearingThresholdHistoryItem(
                session, OnThresholdComparisonChanged, storedAnnotations.GetValueOrDefault(session.Id)));

        SelectedThresholdTest = HearingThresholdTests.FirstOrDefault(item => item.Id == selectedId)
            ?? HearingThresholdTests.FirstOrDefault();
        NotifyThresholdCollectionState();
        UpdateThresholdComparison();
        ThresholdStatusMessage = HearingThresholdTests.Count == 0
            ? "Noch keine Hörschwellentests gespeichert."
            : $"{HearingThresholdTests.Count} Hörschwellentest{(HearingThresholdTests.Count == 1 ? string.Empty : "s")} geladen.";
        if (hearingThresholdRepository is IRepositoryReadDiagnostics { LastReadErrors.Count: > 0 } diagnostics)
            ThresholdStatusMessage = $"{diagnostics.LastReadErrors.Count} gespeicherte Hörschwellenmessung(en) sind nicht lesbar; die übrigen bleiben verfügbar. ID: {diagnostics.LastReadErrors[0].RecordId}.";
    }

    partial void OnSelectedThresholdTestChanged(HearingThresholdHistoryItem? value)
    {
        SelectedThresholdResults.Clear();
        if (value is not null)
        {
            foreach (var result in HearingThresholdResultPresentation.CreateRows(value.Session))
                SelectedThresholdResults.Add(result);
        }
        DeleteSelectedThresholdTestCommand.NotifyCanExecuteChanged();
        EditSelectedThresholdAnnotationCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(IsThresholdSingleView));
    }

    private bool CanDeleteSelectedThresholdTest() => SelectedThresholdTest is not null;

    [RelayCommand(CanExecute = nameof(CanDeleteSelectedThresholdTest))]
    private void DeleteSelectedThresholdTest()
    {
        var item = SelectedThresholdTest;
        if (item is null || !confirmation.Confirm(
                "Hörschwellentest löschen",
                $"Soll der Hörschwellentest vom {item.StartedAtText} dauerhaft gelöscht werden?"))
            return;

        try
        {
            hearingThresholdRepository.Delete(item.Id);
            annotations.Delete(item.Id);
            var index = HearingThresholdTests.IndexOf(item);
            HearingThresholdTests.Remove(item);
            SelectedThresholdTest = HearingThresholdTests.Count == 0
                ? null
                : HearingThresholdTests[Math.Min(index, HearingThresholdTests.Count - 1)];
            NotifyThresholdCollectionState();
            ThresholdStatusMessage = "Hörschwellentest gelöscht.";
        }
        catch (Exception exception)
        {
            ThresholdStatusMessage = $"Hörschwellentest konnte nicht gelöscht werden: {exception.Message}";
        }
    }

    [RelayCommand]
    private void DeleteWordTest(HistorySessionItemViewModel? item)
    {
        if (item is null || !confirmation.Confirm(
                "Worttest löschen",
                $"Soll der Worttest „{item.Name}“ vom {item.StartedAtText} ({item.EarText}, {item.MaterialEnvironmentText}) " +
                "dauerhaft gelöscht werden?\n\nRohantworten, Ergebnis, Name und Kommentar dieser Messung werden entfernt; " +
                "andere Messungen bleiben unberührt."))
            return;

        try
        {
            repository.Delete(item.Id);
            annotations.Delete(item.Id);
        }
        catch (Exception exception)
        {
            WordStatusMessage = $"Worttest konnte nicht gelöscht werden: {exception.Message}";
            return;
        }
        ReloadAll();
        WordStatusMessage = $"Worttest „{item.Name}“ vom {item.StartedAtText} gelöscht.";
    }

    private IReadOnlyDictionary<Guid, MeasurementAnnotation> LoadAnnotations()
    {
        try
        {
            return annotations.LoadAll();
        }
        catch (Exception)
        {
            // Ohne Namen und Kommentare bleibt der Verlauf mit den Vorgabenamen nutzbar.
            return new Dictionary<Guid, MeasurementAnnotation>();
        }
    }

    [RelayCommand]
    private void EditWordTestAnnotation(HistorySessionItemViewModel? item)
    {
        if (item is not null)
            WordStatusMessage = EditAnnotation(item, $"Worttest vom {item.StartedAtText}", WordStatusMessage);
    }

    private bool CanEditSelectedThresholdAnnotation() => SelectedThresholdTest is not null;

    [RelayCommand(CanExecute = nameof(CanEditSelectedThresholdAnnotation))]
    private void EditSelectedThresholdAnnotation()
    {
        if (SelectedThresholdTest is { } item)
            ThresholdStatusMessage = EditAnnotation(item, $"Hörschwellentest vom {item.StartedAtText}", ThresholdStatusMessage);
    }

    /// <summary>Lässt Name und Kommentar bearbeiten, speichert sie und liefert die neue Statusmeldung.</summary>
    private string EditAnnotation(AnnotatedHistoryItem item, string title, string currentStatus)
    {
        if (annotationEditor?.Edit(title, new MeasurementAnnotationInput(item.Name, item.Comment ?? string.Empty)) is not { } input)
            return currentStatus;
        var annotation = MeasurementAnnotationRules.Create(
            item.Id, string.IsNullOrWhiteSpace(input.Name) ? item.DefaultName : input.Name, input.Comment, now());
        var errors = MeasurementAnnotationRules.Validate(annotation);
        if (errors.Count > 0)
            return string.Join(" ", errors);
        try
        {
            annotations.Save(annotation);
        }
        catch (Exception exception)
        {
            return $"Name und Kommentar konnten nicht gespeichert werden: {exception.Message}";
        }
        item.ApplyAnnotation(annotation);
        if (item is HearingThresholdHistoryItem)
            UpdateThresholdComparison();
        else
            ApplyFilters();
        Reloaded?.Invoke();
        return $"Name und Kommentar von „{annotation.Name}“ gespeichert.";
    }

    private void RebuildDeviceFilters()
    {
        var selectedId = SelectedDeviceFilter.Value;
        DeviceFilters.Clear();
        DeviceFilters.Add(new HistoryDeviceFilterOption("Alle Hörgeräte", null));
        foreach (var device in allSessions
            .GroupBy(session => session.HearingAidId)
            .Select(group => new HistoryDeviceFilterOption(group.First().HearingAidText, group.Key))
            .OrderBy(option => option.Label, StringComparer.CurrentCultureIgnoreCase))
            DeviceFilters.Add(device);
        selectedDeviceFilter = DeviceFilters.FirstOrDefault(option => option.Value == selectedId) ?? DeviceFilters[0];
        OnPropertyChanged(nameof(SelectedDeviceFilter));
    }

    private void ApplyFilters()
    {
        if (selectedEarFilter is null || selectedDeviceFilter is null || selectedPeriodFilter is null ||
            selectedCompletionFilter is null || selectedMaterialFilter is null || selectedEnvironmentFilter is null)
            return;

        IEnumerable<HistorySessionItemViewModel> query = allSessions;
        if (SelectedEarFilter.Value is { } ear)
            query = query.Where(item => item.Session.Ear == ear);
        if (SelectedDeviceFilter.Value is { } deviceId)
            query = query.Where(item => item.HearingAidId == deviceId);
        if (SelectedPeriodFilter.Days is { } days)
        {
            var cutoff = now().AddDays(-days);
            query = query.Where(item => item.Session.StartedAt >= cutoff);
        }
        query = SelectedCompletionFilter.Value switch
        {
            HistoryCompletionFilter.Completed => query.Where(item => item.Session.CompletedAt is not null && item.Session.AbortedAt is null),
            HistoryCompletionFilter.Aborted => query.Where(item => item.Session.AbortedAt is not null),
            HistoryCompletionFilter.InProgress => query.Where(item => item.Session.CompletedAt is null),
            _ => query
        };
        if (SelectedMaterialFilter.Value is { } material)
            query = query.Where(item => item.Session.Material == material);
        if (SelectedEnvironmentFilter.Value is { } environment)
            query = query.Where(item => item.Session.Environment == environment);
        if (!string.IsNullOrWhiteSpace(SearchText))
        {
            var term = SearchText.Trim();
            query = query.Where(item =>
                item.Name.Contains(term, StringComparison.CurrentCultureIgnoreCase) ||
                (item.Comment?.Contains(term, StringComparison.CurrentCultureIgnoreCase) ?? false) ||
                item.HearingAidText.Contains(term, StringComparison.CurrentCultureIgnoreCase) ||
                item.HardwareText.Contains(term, StringComparison.CurrentCultureIgnoreCase) ||
                item.MaterialText.Contains(term, StringComparison.CurrentCultureIgnoreCase));
        }

        Sessions.Clear();
        foreach (var item in query.OrderByDescending(item => item.Session.StartedAt))
            Sessions.Add(item);
        WordStatusMessage = allSessions.Count == 0
            ? "Noch keine Messsitzung gespeichert."
            : $"{ResultCountText} angezeigt. Begonnene Messungen bleiben im Verlauf sichtbar.";
        NotifySummaryChanged();
    }

    private void HandleSelectionChanged(HistorySessionItemViewModel item)
    {
        if (item.IsSelected && !item.CanSelectForComparison)
        {
            item.SetSelectedSilently(false);
            WordStatusMessage = item.ComparisonAvailabilityText;
            return;
        }
        if (item.IsSelected && allSessions.Count(session => session.IsSelected) > 2)
        {
            item.SetSelectedSilently(false);
            WordStatusMessage = "Für eine klare Gegenüberstellung können genau zwei abgeschlossene Messungen ausgewählt werden.";
            return;
        }
        UpdateComparison();
    }

    private void UpdateComparison()
    {
        var selected = allSessions.Where(session => session.IsSelected)
            .OrderBy(session => session.Session.StartedAt)
            .ToArray();
        FirstComparison = selected.ElementAtOrDefault(0);
        SecondComparison = selected.ElementAtOrDefault(1);
        if (FirstComparison is not null && SecondComparison is not null)
        {
            var assessment = MeasurementComparisonRules.Assess(FirstComparison.Session, SecondComparison.Session);
            IsDirectlyComparable = assessment.IsDirectlyComparable;
            ComparisonAssessmentText = assessment.IsDirectlyComparable
                ? "Gleiche Hörseite, Messbedingungen, Katalogversion und kompatible Hardware-Snapshots. Die Werte dürfen direkt deskriptiv gegenübergestellt werden."
                : string.Join(" ", assessment.Differences);
        }
        else
        {
            IsDirectlyComparable = false;
            ComparisonAssessmentText = string.Empty;
        }
        OnPropertyChanged(nameof(HasComparison));
        OnPropertyChanged(nameof(HasComparisonWarning));
        OnPropertyChanged(nameof(HasDirectComparison));
        OnPropertyChanged(nameof(SelectionText));
        OnPropertyChanged(nameof(SelectedCount));
        OnPropertyChanged(nameof(HasSelection));
        OnPropertyChanged(nameof(SelectionHintText));
    }

    private void NotifySummaryChanged()
    {
        OnPropertyChanged(nameof(TotalSessionCount));
        OnPropertyChanged(nameof(FilteredSessionCount));
        OnPropertyChanged(nameof(HasSessions));
        OnPropertyChanged(nameof(HasFilteredSessions));
        OnPropertyChanged(nameof(ResultCountText));
        OnPropertyChanged(nameof(SelectionText));
        OnPropertyChanged(nameof(SelectedCount));
        OnPropertyChanged(nameof(HasSelection));
        OnPropertyChanged(nameof(SelectionHintText));
        OnPropertyChanged(nameof(ActiveMoreFilterCount));
        OnPropertyChanged(nameof(MoreFiltersHeader));
        OnPropertyChanged(nameof(HasAnyFilter));
    }

    private void NotifyThresholdCollectionState()
    {
        OnPropertyChanged(nameof(HasHearingThresholdTests));
        OnPropertyChanged(nameof(HasNoHearingThresholdTests));
    }

    private static string FormatThresholdOrder(HearingThresholdSession session) => session.ToneOrder switch
    {
        ThresholdToneOrder.Random => "zufällige Reihenfolge nach 500 Hz",
        _ => "vom 500-Hz-Zentrum nach außen"
    };
}
