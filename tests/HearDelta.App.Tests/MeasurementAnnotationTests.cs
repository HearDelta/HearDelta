using Microsoft.Data.Sqlite;
using HearDelta.App.Services;
using HearDelta.App.ViewModels;
using HearDelta.Core;

namespace HearDelta.App.Tests;

public sealed class MeasurementAnnotationTests : IDisposable
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-10-02T09:00:00Z");
    private readonly string directory = Path.Combine(Path.GetTempPath(), $"heardelta-annotations-{Guid.NewGuid():N}");

    [Fact]
    public void RepositoryStoresUpdatesAndDeletesAnnotations()
    {
        var repository = new MeasurementAnnotationRepository(TestDatabase.Initialize(Path.Combine(directory, "test.db")));
        var id = Guid.NewGuid();

        repository.Save(MeasurementAnnotationRules.Create(id, " Phonak links ", "  ", Now));
        var stored = repository.LoadAll()[id];
        Assert.Equal("Phonak links", stored.Name);
        Assert.Null(stored.Comment);

        repository.Save(MeasurementAnnotationRules.Create(id, "Programm 2", "Lautstärke +2", Now.AddMinutes(5)));
        stored = Assert.Single(repository.LoadAll()).Value;
        Assert.Equal("Programm 2", stored.Name);
        Assert.Equal("Lautstärke +2", stored.Comment);

        repository.Delete(id);
        Assert.Empty(repository.LoadAll());
    }

    [Fact]
    public void RepositoryRejectsEmptyName()
    {
        var repository = new MeasurementAnnotationRepository(TestDatabase.Initialize(Path.Combine(directory, "test.db")));

        Assert.Throws<ArgumentException>(() => repository.Save(MeasurementAnnotationRules.Create(Guid.NewGuid(), " ", null, Now)));
    }

    [Fact]
    public void CurrentDatabaseWithoutAnnotationTableIsExtendedWithoutBackup()
    {
        var connections = TestDatabase.Initialize(Path.Combine(directory, "test.db"));
        Execute(connections, """
            DROP TABLE measurement_annotations;
            INSERT INTO persons(id, display_name, notes, date_of_birth, created_at, updated_at, archived_at)
            VALUES('keep', 'Anna', NULL, NULL, '2026-10-01T00:00:00Z', '2026-10-01T00:00:00Z', NULL);
            """);

        var initializer = new DatabaseSchemaInitializer(connections);
        initializer.Initialize();

        Assert.Null(initializer.BackupPath);
        using var connection = connections.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT (SELECT COUNT(*) FROM persons WHERE id='keep') +
                   (SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='measurement_annotations');
            """;
        Assert.Equal(2L, Convert.ToInt64(command.ExecuteScalar()));
    }

    [Fact]
    public void DraftFollowsHearingAidUntilNameIsChangedByHand()
    {
        var draft = new MeasurementAnnotationDraft();
        draft.SetDefaultName("Phonak Audéo");
        Assert.Equal("Phonak Audéo", draft.Name);

        draft.Name = "Vergleich Programm 2";
        draft.SetDefaultName("Oticon Intent");
        Assert.Equal("Vergleich Programm 2", draft.Name);

        draft.Name = string.Empty;
        Assert.Equal("Oticon Intent", draft.CreateAnnotation(Guid.NewGuid(), Now).Name);
        draft.SetDefaultName("Signia Pure");
        Assert.Equal("Signia Pure", draft.Name);
    }

    [Fact]
    public void ThresholdHistoryUsesDefaultNameAndSavesEditedAnnotation()
    {
        var session = HearingThresholdSessionFactory.Create(
            TestedEar.Left,
            ThresholdToneOrder.Ascending,
            CreateHardware(),
            Now,
            42);
        var annotations = new InMemoryMeasurementAnnotationRepository();
        var editor = new FixedEditor(new MeasurementAnnotationInput("  ", " Hörtest am Morgen "));
        var history = new HistoryViewModel(
            new EmptyMeasurementRepository(),
            new FixedThresholdRepository(session),
            LoadPack(),
            new AlwaysConfirm(),
            () => Now,
            annotations: annotations,
            annotationEditor: editor);
        history.SetPerson(new PersonProfile(Guid.NewGuid(), "Anna", null, Now, Now));

        var item = Assert.Single(history.HearingThresholdTests);
        Assert.Equal(HearingThresholdResultPresentation.DefaultName, item.Name);
        Assert.False(item.HasComment);

        history.SelectedThresholdTest = item;
        history.EditSelectedThresholdAnnotationCommand.Execute(null);

        Assert.Equal(HearingThresholdResultPresentation.DefaultName, item.Name);
        Assert.Equal("Hörtest am Morgen", item.Comment);
        Assert.Equal("Hörtest am Morgen", annotations.LoadAll()[session.Id].Comment);
        Assert.Equal(HearingThresholdResultPresentation.DefaultName, editor.LastInput!.Name);
    }

    private static void Execute(DatabaseConnectionFactory connections, string sql)
    {
        using var connection = connections.Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private static LoadedStimulusPack LoadPack() =>
        new StimulusCatalogService().Load(StimulusCatalogService.GetBundledPackDirectory());

    private static MeasurementHardwareSnapshot CreateHardware() => new(
        Guid.Parse("4ad65c78-1b5d-4c3f-9c8e-3f1f0e6b7d21"),
        "Testprofil",
        "endpoint-test",
        "Testausgang",
        true,
        48_000,
        24,
        2,
        "Testhersteller",
        "Testmodell",
        "Ohrumschließend",
        300,
        "Vordere 3,5-mm-Klinke",
        "Low (+6 dB)",
        -60m,
        -30m,
        false,
        Now);

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(directory))
            Directory.Delete(directory, recursive: true);
    }

    private sealed class FixedEditor(MeasurementAnnotationInput result) : IMeasurementAnnotationEditor
    {
        public MeasurementAnnotationInput? LastInput { get; private set; }

        public MeasurementAnnotationInput? Edit(string title, MeasurementAnnotationInput current)
        {
            LastInput = current;
            return result;
        }
    }

    private sealed class EmptyMeasurementRepository : IMeasurementSessionRepository
    {
        public IReadOnlyList<PairedMeasurementSession> LoadAll() => [];
        public IReadOnlyList<PairedMeasurementSession> LoadForPerson(Guid personId) => [];
        public PairedMeasurementSession? Load(Guid id) => null;
        public void Save(Guid personId, PairedMeasurementSession session) => throw new NotSupportedException();
        public void Delete(Guid id) => throw new NotSupportedException();
    }

    private sealed class FixedThresholdRepository(params HearingThresholdSession[] sessions) : IHearingThresholdSessionRepository
    {
        public IReadOnlyList<HearingThresholdSession> LoadAll() => sessions;
        public IReadOnlyList<HearingThresholdSession> LoadForPerson(Guid personId) => sessions;
        public HearingThresholdSession? Load(Guid id) => sessions.SingleOrDefault(session => session.Id == id);
        public void Save(Guid personId, HearingThresholdSession session) => throw new NotSupportedException();
        public void Delete(Guid id) => throw new NotSupportedException();
    }

    private sealed class AlwaysConfirm : IUserConfirmationService
    {
        public bool Confirm(string title, string message) => true;
    }
}
