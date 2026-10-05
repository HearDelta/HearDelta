using HearDelta.App.Services;
using HearDelta.Core;

namespace HearDelta.App.Tests;

public sealed class MeasurementSeriesRepositoryTests
{
    [Fact]
    public void ActiveSeriesRoundTripAndCompletionArePersistent()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"heardelta-series-{Guid.NewGuid():N}");
        var databasePath = Path.Combine(directory, "sessions.db");

        try
        {
            var connections = TestDatabase.Initialize(databasePath);
            var personId = CreatePerson(connections, "Erste Person");
            var otherPersonId = CreatePerson(connections, "Zweite Person");
            var plan = MeasurementSeriesPlanner.Create(CreateCatalog(), SpeechMaterial.PhonemeContrasts, 6, 20260906);
            var repository = new MeasurementSeriesRepository(connections);
            repository.Save(personId, new MeasurementSeriesState(plan, 2));

            var loaded = new MeasurementSeriesRepository(connections).LoadActive(personId);
            Assert.NotNull(loaded);
            Assert.Equal(plan.Id, loaded.Plan.Id);
            Assert.Equal(2, loaded.CurrentRoundIndex);
            Assert.Equal(plan.Rounds, loaded.Plan.Rounds);
            Assert.Null(repository.LoadActive(otherPersonId));

            repository.Complete(plan.Id);
            Assert.Null(new MeasurementSeriesRepository(connections).LoadActive(personId));
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void SeriesForUnknownPersonIsRejectedByForeignKey()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"heardelta-series-fk-{Guid.NewGuid():N}");
        try
        {
            var repository = new MeasurementSeriesRepository(TestDatabase.Initialize(Path.Combine(directory, "sessions.db")));
            var plan = MeasurementSeriesPlanner.Create(CreateCatalog(), SpeechMaterial.PhonemeContrasts, 2, 1);

            Assert.Throws<Microsoft.Data.Sqlite.SqliteException>(() => repository.Save(Guid.NewGuid(), new MeasurementSeriesState(plan, 0)));
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
    }

    private static Guid CreatePerson(DatabaseConnectionFactory connections, string displayName)
    {
        var personId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        new PersonRepository(connections).Save(new PersonProfile(personId, displayName, null, now, now));
        return personId;
    }

    private static StimulusCatalog CreateCatalog() => new(
        1, "test", "1", "de-DE", "Test", "CC0", false, 1,
        [
            new StimulusListDefinition("a", SpeechMaterial.PhonemeContrasts, [new StimulusDefinition("a1", "a", "a", "a.wav")]),
            new StimulusListDefinition("b", SpeechMaterial.PhonemeContrasts, [new StimulusDefinition("b1", "b", "b", "b.wav")])
        ]);
}
