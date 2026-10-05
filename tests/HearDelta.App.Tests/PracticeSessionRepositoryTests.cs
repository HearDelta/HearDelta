using HearDelta.App.Services;
using HearDelta.Core;

namespace HearDelta.App.Tests;

public sealed class PracticeSessionRepositoryTests
{
    [Fact]
    public void RoundTripPreservesFreeTextAndAbortState()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"heardelta-practice-{Guid.NewGuid():N}");
        try
        {
            var repository = new PracticeSessionRepository(TestDatabase.Initialize(Path.Combine(directory, "sessions.db")));
            var databasePath = Path.Combine(directory, "sessions.db");
            var personId = Guid.NewGuid();
            var start = DateTimeOffset.UtcNow;
            new PersonRepository(new DatabaseConnectionFactory(databasePath)).Save(
                new PersonProfile(personId, "Testperson", null, start, start));
            var session = PracticeSessionFactory.Create(TestedEar.Left, SpeechMaterial.Monosyllables, "de-mono-a-v1",
                new MeasurementHardwareSnapshot(Guid.NewGuid(), "Profil", "endpoint", "Ausgang", true, 48000, 24, 2, "Kopfhörer", "Modell", "offen", 300, "Ausgang", "Low", -60m, -30m, false, start),
                StimulusMaterialIdentityFactory.Create("practice", "1", new string('a', 64), new string('b', 64)), start, 42) with
                { CompletedAt = start.AddMinutes(1), AbortedAt = start.AddMinutes(1), Responses = [new PracticeResponse(1, "mono-a-01", " Haus ", start)] };
            repository.Save(personId, session);
            var loaded = repository.Load(session.Id);
            Assert.NotNull(loaded); Assert.Equal(" Haus ", loaded.Responses[0].EnteredText); Assert.Equal(session.AbortedAt, loaded.AbortedAt);
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }
}
