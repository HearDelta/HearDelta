using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.IO;
using HearDelta.App.Localization;
using HearDelta.App.Services;
using HearDelta.App.ViewModels;
using HearDelta.App.Views;

namespace HearDelta.App;

public partial class MainWindow : Window
{
    private bool replacedForLanguageChange;

    /// <param name="restoredPersonId">Nach einem Sprachwechsel wieder auszuwählende Person.</param>
    /// <param name="restoredPage">Nach einem Sprachwechsel wieder anzuzeigende Seite.</param>
    public MainWindow(Guid? restoredPersonId = null, ShellPage? restoredPage = null)
    {
        // Bindungen formatieren sonst unabhängig von der Oberflächensprache immer nach en-US.
        Language = XmlLanguage.GetLanguage(CultureInfo.CurrentCulture.IetfLanguageTag);
        InitializeComponent();
        var database = new DatabaseConnectionFactory();
        var schemaInitializer = new DatabaseSchemaInitializer(database);
        schemaInitializer.Initialize();
        if (restoredPage is null && schemaInitializer.BackupPath is { } backupPath)
        {
            Loaded += (_, _) => MessageBox.Show(
                this,
                DatabaseSchemaInitializer.BuildBackupNotice(database.DatabasePath, backupPath),
                Strings.Shell_DatabaseBackupTitle,
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
            if (replacedForLanguageChange || settings.ConfirmClose())
                return;
            args.Cancel = true;
            // Abgebrochen oder Speichern gescheitert: das Messprofil mit seinem Status zeigen.
            (DataContext as MainWindowViewModel)?.ShowSettingsUnlessTestRunning();
        };
        var viewModel = new MainWindowViewModel(
            measurement,
            practice,
            hearingThreshold,
            history,
            settings,
            persons,
            new MessageBoxConfirmationService(),
            App.LanguageStore.Load(),
            App.SystemUiCulture);
        viewModel.LanguageChangeRequested += SwitchLanguage;
        Restore(viewModel, restoredPersonId, restoredPage);
        DataContext = viewModel;
    }

    private static void Restore(MainWindowViewModel viewModel, Guid? personId, ShellPage? page)
    {
        if (personId is { } id && viewModel.Persons.List.People.FirstOrDefault(person => person.Id == id) is { } person)
            viewModel.Persons.List.SelectedPerson = person;
        switch (page)
        {
            case ShellPage.Persons:
                viewModel.ShowPersonsCommand.Execute(null);
                break;
            case ShellPage.Settings:
                viewModel.OpenSettingsCommand.Execute(null);
                break;
            case ShellPage.History when viewModel.ShowHistoryCommand.CanExecute(null):
                viewModel.ShowHistoryCommand.Execute(null);
                break;
            case not null when viewModel.ShowOverviewCommand.CanExecute(null):
                viewModel.ShowOverviewCommand.Execute(null);
                break;
        }
    }

    /// <summary>
    /// Speichert die Sprachwahl und ersetzt dieses Fenster durch ein in der neuen Sprache aufgebautes. Person und
    /// Seite bleiben erhalten; ein Test wurde vorher bereits nach Rückfrage abgebrochen.
    /// </summary>
    private void SwitchLanguage(UiLanguagePreference preference)
    {
        try
        {
            App.LanguageStore.Save(preference);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(this, string.Format(Strings.Shell_LanguageSaveFailed, exception.Message),
                Strings.Shell_LanguageTooltip, MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        UiLanguage.Apply(UiLanguage.Resolve(preference, App.SystemUiCulture));
        var viewModel = (MainWindowViewModel)DataContext;
        var next = new MainWindow(viewModel.Persons.List.SelectedPerson?.Id, viewModel.CurrentPage)
        {
            WindowStartupLocation = WindowStartupLocation.Manual,
            Left = RestoreBounds.Left,
            Top = RestoreBounds.Top,
            Width = RestoreBounds.Width,
            Height = RestoreBounds.Height
        };
        next.Show();
        next.WindowState = WindowState;
        Application.Current.MainWindow = next;
        replacedForLanguageChange = true;
        Close();
    }

    private void OnLanguageButtonClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button { ContextMenu: { } menu } button)
        {
            menu.PlacementTarget = button;
            menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Top;
            menu.IsOpen = true;
        }
    }
}
