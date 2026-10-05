using System.Windows;
using System.IO;
using HearDelta.App.Services;
using HearDelta.App.ViewModels;
using HearDelta.App.Views;

namespace HearDelta.App;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        var database = new DatabaseConnectionFactory();
        var schemaInitializer = new DatabaseSchemaInitializer(database);
        schemaInitializer.Initialize();
        if (schemaInitializer.BackupPath is { } backupPath)
        {
            Loaded += (_, _) => MessageBox.Show(
                this,
                DatabaseSchemaInitializer.BuildBackupNotice(database.DatabasePath, backupPath),
                "Datenbank gesichert",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        var audioEndpoints = new AudioEndpointService();
        var profileRepository = new ProfileRepository(database);
        var settings = new SettingsViewModel(
            audioEndpoints,
            profileRepository,
            new HeadphoneEqualizationCatalogService(),
            new MessageBoxConfirmationService());
        var catalogService = new StimulusCatalogService();
        var pack = catalogService.Load(StimulusCatalogService.GetBundledPackDirectory());
        var numberPacks = new[]
        {
            catalogService.Load(Path.Combine(AppContext.BaseDirectory, "stimuli", "de-DE", "personal-cardinal-numbers-christoph-v1")),
            catalogService.Load(Path.Combine(AppContext.BaseDirectory, "stimuli", "de-DE", "personal-cardinal-numbers-katja-v1"))
        };
        var practicePack = catalogService.Load(
            Path.Combine(AppContext.BaseDirectory, "stimuli", "de-DE", "personal-relative-v1"));
        var measurementSessions = new MeasurementSessionRepository(database);
        var measurementSeries = new MeasurementSeriesRepository(database);
        var practiceSessions = new PracticeSessionRepository(database);
        var hearingThresholdSessions = new HearingThresholdSessionRepository(database);
        var personRepository = new PersonRepository(database);
        var pretests = new PretestResultsService(measurementSessions, hearingThresholdSessions, numberPacks.Prepend(pack));
        var measurementAnnotations = new MeasurementAnnotationRepository(database);
        var reportPrinter = new FlowDocumentReportPrinter();
        var measurement = new MeasurementViewModel(
            pack,
            settings.Profiles,
            audioEndpoints,
            new StimulusPlaybackService(),
            measurementSessions,
            series: measurementSeries,
            numberPacks: numberPacks,
            loadHearingAids: personId => personRepository.LoadHearingAids(personId),
            annotations: measurementAnnotations,
            continuousNoise: new ContinuousNoisePlaybackService(),
            timing: MeasurementTiming.Default,
            loadPretests: pretests.Load,
            printer: reportPrinter);
        var practice = new PracticeViewModel(
            practicePack,
            settings.Profiles,
            audioEndpoints,
            new StimulusPlaybackService(),
            practiceSessions);
        var hearingThreshold = new HearingThresholdViewModel(
            settings.Profiles,
            audioEndpoints,
            new ThresholdTonePlaybackService(),
            hearingThresholdSessions,
            annotations: measurementAnnotations,
            printer: reportPrinter);
        var history = new HistoryViewModel(
            measurementSessions,
            hearingThresholdSessions,
            pack,
            new MessageBoxConfirmationService(),
            additionalPacks: numberPacks,
            annotations: measurementAnnotations,
            annotationEditor: new DialogMeasurementAnnotationEditor(),
            printer: reportPrinter);
        var personList = new PersonListViewModel(personRepository, confirmation: new MessageBoxConfirmationService());
        var personDetail = new PersonDetailViewModel(
            history,
            personId => personRepository.LoadHearingAids(personId),
            pretests.LoadStatus);
        var persons = new PersonsViewModel(personList, personDetail);
        Closing += (_, args) =>
        {
            if (settings.ConfirmClose())
                return;
            args.Cancel = true;
            // Abgebrochen oder Speichern gescheitert: das Messprofil mit seinem Status zeigen.
            (DataContext as MainWindowViewModel)?.ShowSettingsUnlessTestRunning();
        };
        DataContext = new MainWindowViewModel(measurement, practice, hearingThreshold, history, settings, persons, new MessageBoxConfirmationService());
    }
}
