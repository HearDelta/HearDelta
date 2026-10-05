using Microsoft.Data.Sqlite;
using HearDelta.App.Services;
using HearDelta.Core;

namespace HearDelta.App.Tests;

public sealed class MeasurementSessionRepositoryTests
{
    [Fact]
    public void SessionRoundTripPreservesSnapshotsOrderAndRawAnswers()
    {
        var testDirectory = Path.Combine(Path.GetTempPath(), $"heardelta-tests-{Guid.NewGuid():N}");
        var databasePath = Path.Combine(testDirectory, "sessions.db");

        try
        {
            var repository = new MeasurementSessionRepository(TestDatabase.Initialize(databasePath));
            var session = CreateCompletedSession();
            var personId = CreatePerson(databasePath);

            repository.Save(personId, session);
            var loaded = new MeasurementSessionRepository(TestDatabase.Initialize(databasePath)).Load(session.Id);

            Assert.NotNull(loaded);
            Assert.Equal(MeasurementProtocol.CurrentVersion, loaded.ProtocolVersion);
            Assert.Equal(TestedEar.Left, loaded.Ear);
            Assert.Equal("Signia", loaded.HearingAid.Manufacturer);
            Assert.Equal("endpoint-topping", loaded.Hardware.EndpointId);
            Assert.NotNull(loaded.MaterialIdentity);
            Assert.Equal(
                StimulusMaterialIdentityFactory.Create(
                    "test-catalog", "1.0.0", new string('a', 64), new string('a', 64)).FingerprintSha256,
                loaded.MaterialIdentity!.FingerprintSha256);
            Assert.Equal(HearingAidCondition.WithoutHearingAid, loaded.Blocks[0].Condition);
            Assert.Equal("mono-list-a", loaded.Blocks[0].StimulusListId);
            Assert.Equal("  sieben ", loaded.Blocks[0].RawResponses[0].EnteredText);
            Assert.NotNull(loaded.Blocks[0].RawResponses[0].Presentation);
            Assert.Equal("endpoint-topping", loaded.Blocks[0].RawResponses[0].Presentation!.EndpointId);
            Assert.Equal(-60m, loaded.Blocks[0].RawResponses[0].Presentation!.RenderMetadata.DigitalAttenuationDb);
            Assert.Null(loaded.Blocks[1].RawResponses[0].EnteredText);
            Assert.Equal(session.CompletedAt, loaded.CompletedAt);
        }
        finally
        {
            if (Directory.Exists(testDirectory))
                Directory.Delete(testDirectory, recursive: true);
        }
    }

    [Fact]
    public void AbortedSessionRoundTripPreservesExplicitAbortMarker()
    {
        var testDirectory = Path.Combine(Path.GetTempPath(), $"heardelta-abort-tests-{Guid.NewGuid():N}");
        var databasePath = Path.Combine(testDirectory, "sessions.db");

        try
        {
            var repository = new MeasurementSessionRepository(TestDatabase.Initialize(databasePath));
            var session = CreateCompletedSession();
            var aborted = session with { AbortedAt = session.CompletedAt };
            var personId = CreatePerson(databasePath);

            repository.Save(personId, aborted);
            var loaded = new MeasurementSessionRepository(TestDatabase.Initialize(databasePath)).Load(aborted.Id);

            Assert.NotNull(loaded);
            Assert.Equal(aborted.CompletedAt, loaded.CompletedAt);
            Assert.Equal(aborted.AbortedAt, loaded.AbortedAt);
        }
        finally
        {
            if (Directory.Exists(testDirectory))
                Directory.Delete(testDirectory, recursive: true);
        }
    }

    [Fact]
    public void LoadForPersonReturnsOnlyDirectlyAssignedMeasurementsAndDeleteRemovesOnlyOne()
    {
        var testDirectory = Path.Combine(Path.GetTempPath(), $"heardelta-person-tests-{Guid.NewGuid():N}");
        var databasePath = Path.Combine(testDirectory, "sessions.db");

        try
        {
            var repository = new MeasurementSessionRepository(TestDatabase.Initialize(databasePath));
            var firstPersonId = CreatePerson(databasePath, "Erste Person");
            var secondPersonId = CreatePerson(databasePath, "Zweite Person");
            var first = CreateCompletedSession();
            var second = first with { Id = Guid.NewGuid() };

            repository.Save(firstPersonId, first);
            repository.Save(secondPersonId, second);

            Assert.Equal(first.Id, Assert.Single(repository.LoadForPerson(firstPersonId)).Id);
            Assert.Equal(second.Id, Assert.Single(repository.LoadForPerson(secondPersonId)).Id);

            repository.Delete(first.Id);

            Assert.Empty(repository.LoadForPerson(firstPersonId));
            Assert.Null(repository.Load(first.Id));
            Assert.Equal(second.Id, Assert.Single(repository.LoadForPerson(secondPersonId)).Id);
        }
        finally
        {
            if (Directory.Exists(testDirectory))
                Directory.Delete(testDirectory, recursive: true);
        }
    }

    private static Guid CreatePerson(string databasePath, string displayName = "Testperson")
    {
        var personId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        new PersonRepository(new DatabaseConnectionFactory(databasePath)).Save(
            new PersonProfile(personId, displayName, null, now, now));
        return personId;
    }

    private static PairedMeasurementSession CreateCompletedSession()
    {
        var startedAt = DateTimeOffset.Parse("2026-09-01T08:00:00Z");
        var session = PairedMeasurementSessionFactory.CreateRandomized(
            TestedEar.Left,
            new HearingAidSnapshot(
                Guid.Parse("3a80fa53-9658-4a71-8936-86d67f91e688"),
                "Signia",
                "Pure C&G BCT 2IX",
                "Signia links",
                TestedEar.Left,
                "Programm 1",
                "0"),
            SpeechMaterial.Monosyllables,
            ListeningEnvironment.Quiet,
            "mono-list-a",
            "mono-list-b",
            CreateHardware(),
            startedAt,
            randomizationSeed: 20,
            materialIdentity: StimulusMaterialIdentityFactory.Create(
                "test-catalog",
                "1.0.0",
                new string('a', 64),
                new string('a', 64)),
            contract: MeasurementSessionContracts.PhonemeContrastMeasurement);

        var firstBlock = session.Blocks[0] with
        {
            SetupConfirmation = new MeasurementSetupConfirmation(true, true, startedAt.AddSeconds(30)),
            RawResponses =
            [
                new RawMeasurementResponse(
                    1,
                    "mono-a-01",
                    "  sieben ",
                    startedAt.AddMinutes(1),
                    new StimulusPresentationRecord(
                        "test-catalog",
                        "1.0.0",
                        "mono-a-01",
                        new string('a', 64),
                        "endpoint-topping",
                        "Lautsprecher (TOPPING USB DAC)",
                        new StimulusRenderMetadata(
                            StimulusAudioRenderer.RendererVersion,
                            "none",
                            -6m,
                            -60m,
                            null,
                            42,
                            48000,
                            0.001f),
                        startedAt.AddSeconds(50),
                        startedAt.AddSeconds(51)))
            ]
        };
        var secondBlock = session.Blocks[1] with
        {
            SetupConfirmation = new MeasurementSetupConfirmation(true, true, startedAt.AddSeconds(30)),
            RawResponses =
            [
                new RawMeasurementResponse(
                    1, "mono-b-01", null, startedAt.AddMinutes(2),
                    new StimulusPresentationRecord(
                        "test-catalog", "1.0.0", "mono-b-01", new string('a', 64),
                        "endpoint-topping", "Lautsprecher (TOPPING USB DAC)",
                        new StimulusRenderMetadata(
                            StimulusAudioRenderer.RendererVersion, "none", -6m, -60m,
                            null, 43, 48000, 0.001f),
                        startedAt.AddSeconds(55), startedAt.AddSeconds(56)))
            ]
        };
        return session with
        {
            CompletedAt = startedAt.AddMinutes(3),
            Blocks = [firstBlock, secondBlock]
        };
    }

    private static MeasurementHardwareSnapshot CreateHardware() => new(
        Guid.Parse("84452115-0de9-489b-acd8-df0178db080d"),
        "Test-DAC + Testkopfhörer",
        "endpoint-topping",
        "Lautsprecher (TOPPING USB DAC)",
        true,
        48000,
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
        DateTimeOffset.Parse("2026-09-01T07:30:00Z"));
}
