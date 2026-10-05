using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Data.Sqlite;
using HearDelta.App.Services;
using HearDelta.App.ViewModels;
using HearDelta.Core;

namespace HearDelta.App.Tests;

public sealed class HearingThresholdSessionRepositoryTests
{
    [Fact]
    public void SessionRoundTripPreservesTonePlanObservationAndHardwareSnapshot()
    {
        var testDirectory = Path.Combine(Path.GetTempPath(), $"heardelta-threshold-tests-{Guid.NewGuid():N}");
        var databasePath = Path.Combine(testDirectory, "sessions.db");

        try
        {
            var repository = new HearingThresholdSessionRepository(TestDatabase.Initialize(databasePath));
            var session = CreateSessionWithObservation();
            var personId = CreatePerson(databasePath);

            repository.Save(personId, session);
            var loaded = new HearingThresholdSessionRepository(TestDatabase.Initialize(databasePath)).Load(session.Id);

            Assert.NotNull(loaded);
            Assert.Equal(ThresholdToneOrder.Random, loaded.ToneOrder);
            Assert.Equal(14, loaded.Tones.Count);
            Assert.Equal(500d, loaded.Tones[0].FrequencyHz);
            Assert.Single(loaded.Observations);
            Assert.Equal(-72.5m, loaded.Observations[0].ThresholdAttenuationDbfs);
            Assert.Equal("endpoint-topping", loaded.Observations[0].Presentation.EndpointId);
            Assert.Equal(-70m, loaded.Observations[0].Presentation.EndAttenuationDbfs);
            Assert.Equal(-79m, loaded.Observations[0].Confirmation!.StartAttenuationDbfs);
            Assert.Equal(-72.5m, loaded.Observations[0].Confirmation!.EndAttenuationDbfs);
            Assert.Equal("Testmodell", loaded.Hardware.HeadphoneModel);
            Assert.Equal(-30m, loaded.MaximumAttenuationDbfs);
            Assert.Equal(ThresholdSignalPattern.Current, loaded.SignalPattern);
            Assert.Equal(ThresholdSignalPattern.Current, loaded.Observations[0].Presentation.SignalPattern);
            Assert.Equal(loaded.CompletedAt, loaded.AbortedAt);
        }
        finally
        {
            if (Directory.Exists(testDirectory))
                Directory.Delete(testDirectory, recursive: true);
        }
    }

    [Fact]
    public void DeleteRemovesOnlySelectedMeasurement()
    {
        var testDirectory = Path.Combine(Path.GetTempPath(), $"heardelta-threshold-delete-{Guid.NewGuid():N}");
        var databasePath = Path.Combine(testDirectory, "sessions.db");

        try
        {
            var repository = new HearingThresholdSessionRepository(TestDatabase.Initialize(databasePath));
            var personId = CreatePerson(databasePath);
            var selected = CreateSessionWithObservation();
            var remaining = selected with
            {
                Id = Guid.NewGuid(),
                StartedAt = selected.StartedAt.AddMinutes(1),
                CompletedAt = selected.CompletedAt?.AddMinutes(1),
                AbortedAt = selected.AbortedAt?.AddMinutes(1)
            };
            repository.Save(personId, selected);
            repository.Save(personId, remaining);

            repository.Delete(selected.Id);

            Assert.Null(repository.Load(selected.Id));
            Assert.Equal(remaining.Id, Assert.Single(repository.LoadAll()).Id);
        }
        finally
        {
            if (Directory.Exists(testDirectory))
                Directory.Delete(testDirectory, recursive: true);
        }
    }

    [Fact]
    public void LegacyV9PayloadIsReadableAndStaysUnchangedInTheDatabase()
    {
        var testDirectory = Path.Combine(Path.GetTempPath(), $"heardelta-threshold-v9-{Guid.NewGuid():N}");
        var databasePath = Path.Combine(testDirectory, "sessions.db");

        try
        {
            var connections = TestDatabase.Initialize(databasePath);
            var repository = new HearingThresholdSessionRepository(connections);
            var personId = CreatePerson(databasePath);
            var current = CreateSessionWithObservation(LegacyHardware());
            var legacy = current with
            {
                ProtocolVersion = 9,
                SignalPattern = ThresholdSignalPattern.LegacyV9,
                Observations = current.Observations
                    .Select(observation => observation with
                    {
                        ThresholdAttenuationDbfs = observation.Presentation.EndAttenuationDbfs,
                        Presentation = observation.Presentation with { SignalPattern = ThresholdSignalPattern.LegacyV9 },
                        Confirmation = null
                    })
                    .ToArray()
            };
            var v9Payload = ToV9Payload(legacy);
            Assert.DoesNotContain("signalPattern", v9Payload);
            InsertRaw(connections, personId, legacy, v9Payload);

            var loaded = repository.Load(legacy.Id);

            Assert.NotNull(loaded);
            Assert.Equal(9, loaded.ProtocolVersion);
            Assert.Equal(3m, loaded.LevelStepDb);
            Assert.Equal(ThresholdSignalPattern.LegacyV9, loaded.SignalPattern);
            Assert.Equal(ThresholdSignalPattern.LegacyV9, loaded.Observations[0].Presentation.SignalPattern);
            Assert.Single(repository.LoadForPerson(personId));
            Assert.Empty(repository.LastReadErrors);

            using var connection = connections.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT payload_json FROM hearing_threshold_sessions WHERE id = $id";
            command.Parameters.AddWithValue("$id", legacy.Id.ToString("D"));
            Assert.Equal(v9Payload, command.ExecuteScalar() as string);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(testDirectory))
                Directory.Delete(testDirectory, recursive: true);
        }
    }

    [Fact]
    public void LegacyV11PayloadWithHearingAidStaysReadableAndMarked()
    {
        var testDirectory = Path.Combine(Path.GetTempPath(), $"heardelta-threshold-v11-{Guid.NewGuid():N}");
        var databasePath = Path.Combine(testDirectory, "sessions.db");

        try
        {
            var connections = TestDatabase.Initialize(databasePath);
            var repository = new HearingThresholdSessionRepository(connections);
            var personId = CreatePerson(databasePath);
            var legacy = CreateSessionWithObservation(LegacyHardware()) with
            {
                ProtocolVersion = 11,
                Condition = HearingAidCondition.WithHearingAid,
                HearingAid = new HearingAidSnapshot(Guid.NewGuid(), "Phonak", "Audéo", "Phonak Audéo links", TestedEar.Left)
            };
            // v11 kannte weder die Vertäubung noch den Rauschpegel im Wiedergabenachweis.
            var root = JsonSerializer.SerializeToNode(legacy, new JsonSerializerOptions(JsonSerializerDefaults.Web))!.AsObject();
            root.Remove("masking");
            foreach (var observation in root["observations"]!.AsArray())
            {
                observation!["presentation"]!.AsObject().Remove("maskingLevelDbfs");
                observation["confirmation"]!.AsObject().Remove("maskingLevelDbfs");
            }
            InsertRaw(connections, personId, legacy, root.ToJsonString());

            var loaded = repository.Load(legacy.Id);

            Assert.NotNull(loaded);
            Assert.True(loaded.IsLegacyWithHearingAid);
            Assert.Null(loaded.Masking);
            Assert.Equal("Phonak Audéo links", loaded.HearingAid!.DisplayName);
            var item = new HearingThresholdHistoryItem(loaded);
            Assert.Contains("Mit Hörgerät", item.ConditionText);
            Assert.Equal("Phonak Audéo links", item.DefaultName);
            Assert.Equal(BadgeTone.Warning, item.ConditionTone);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(testDirectory))
                Directory.Delete(testDirectory, recursive: true);
        }
    }

    [Fact]
    public void MaskedSessionRoundTripKeepsMaskingAndPerPresentationLevel()
    {
        var testDirectory = Path.Combine(Path.GetTempPath(), $"heardelta-threshold-masked-{Guid.NewGuid():N}");
        var databasePath = Path.Combine(testDirectory, "sessions.db");

        try
        {
            var repository = new HearingThresholdSessionRepository(TestDatabase.Initialize(databasePath));
            var unmasked = CreateSessionWithObservation();
            var masking = ThresholdMaskingProtocol.Create(-48m, unmasked.RandomizationSeed);
            var session = unmasked with
            {
                Masking = masking,
                Observations = unmasked.Observations
                    .Select(observation => observation with
                    {
                        Presentation = observation.Presentation with { MaskingLevelDbfs = -48m },
                        Confirmation = observation.Confirmation! with { MaskingLevelDbfs = -48m }
                    })
                    .ToArray()
            };
            var personId = CreatePerson(databasePath);

            repository.Save(personId, session);
            var loaded = repository.Load(session.Id);

            Assert.NotNull(loaded);
            Assert.Equal(masking, loaded.Masking);
            Assert.Equal(TestedEar.Right, loaded.MaskedEar);
            Assert.Equal(-48m, loaded.Observations[0].Presentation.MaskingLevelDbfs);
            Assert.Equal(-48m, loaded.Observations[0].Confirmation!.MaskingLevelDbfs);
            Assert.Equal("Gegenohr vertäubt · −48 dBFS", new HearingThresholdHistoryItem(loaded).ConditionText);
            Assert.Throws<ArgumentException>(() => repository.Save(personId, session with { Masking = null }));
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(testDirectory))
                Directory.Delete(testDirectory, recursive: true);
        }
    }

    /// <summary>Bildet das JSON-Format des Protokolls v9 nach (alte Zeitfelder statt Tonsignal).</summary>
    private static string ToV9Payload(HearingThresholdSession session)
    {
        var root = JsonSerializer.SerializeToNode(session, new JsonSerializerOptions(JsonSerializerDefaults.Web))!.AsObject();
        static void Downgrade(JsonObject node)
        {
            node.Remove("levelStepDb");
            node.Remove("signalPattern");
            node["riseDbPerSecond"] = 4m;
            node["toneDurationMilliseconds"] = 750;
            node["pauseDurationMilliseconds"] = 750;
            node["pulsesPerLevel"] = 2;
        }
        Downgrade(root);
        foreach (var observation in root["observations"]!.AsArray())
        {
            observation!.AsObject().Remove("confirmation");
            Downgrade(observation["presentation"]!.AsObject());
        }
        return root.ToJsonString();
    }

    private static void InsertRaw(DatabaseConnectionFactory connections, Guid personId, HearingThresholdSession session, string payload)
    {
        using var connection = connections.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO hearing_threshold_sessions (id, person_id, protocol_version, started_at, completed_at, aborted_at,
                tested_ear, hearing_aid_condition, hearing_aid_id, payload_json, updated_at)
            VALUES ($id, $personId, 9, $startedAt, $completedAt, $abortedAt, $ear, $condition, NULL, $payload, $startedAt);
            """;
        command.Parameters.AddWithValue("$id", session.Id.ToString("D"));
        command.Parameters.AddWithValue("$personId", personId.ToString("D"));
        command.Parameters.AddWithValue("$startedAt", session.StartedAt.ToString("O"));
        command.Parameters.AddWithValue("$completedAt", session.CompletedAt?.ToString("O") ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("$abortedAt", session.AbortedAt?.ToString("O") ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("$ear", (int)session.Ear);
        command.Parameters.AddWithValue("$condition", (int)session.Condition);
        command.Parameters.AddWithValue("$payload", payload);
        command.ExecuteNonQuery();
    }

    private static Guid CreatePerson(string databasePath)
    {
        var personId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        new PersonRepository(new DatabaseConnectionFactory(databasePath)).Save(
            new PersonProfile(personId, "Testperson", null, now, now));
        return personId;
    }

    /// <summary>Messaufbau der Protokolle v9 bis v11 mit der damals festen Obergrenze von -6 dBFS.</summary>
    private static MeasurementHardwareSnapshot LegacyHardware() => CreateHardware() with { MaximumVolumeDb = -6m };

    private static HearingThresholdSession CreateSessionWithObservation(MeasurementHardwareSnapshot? hardware = null)
    {
        var startedAt = DateTimeOffset.Parse("2026-09-02T08:00:00Z");
        var session = HearingThresholdSessionFactory.Create(
            TestedEar.Left,
            ThresholdToneOrder.Random,
            hardware ?? CreateHardware(),
            startedAt,
            randomizationSeed: 42);
        var tone = session.Tones[0];
        var presentation = new ThresholdTonePresentationRecord(
            session.Hardware.EndpointId,
            session.Hardware.EndpointName,
            tone.FrequencyHz,
            session.StartAttenuationDbfs,
            -70m,
            session.MaximumAttenuationDbfs,
            session.LevelStepDb,
            session.Hardware.SampleRate,
            startedAt.AddSeconds(1),
            startedAt.AddSeconds(8),
            false,
            session.SignalPattern);
        var observation = new HearingThresholdObservation(
            tone.PresentationOrder,
            tone.MidiNoteNumber,
            true,
            -72.5m,
            startedAt.AddSeconds(15),
            presentation,
            presentation with
            {
                StartAttenuationDbfs = -79m,
                EndAttenuationDbfs = -72.5m,
                StartedAt = startedAt.AddSeconds(8),
                CompletedAt = startedAt.AddSeconds(15)
            });
        return session with
        {
            Observations = [observation],
            CompletedAt = startedAt.AddSeconds(20),
            AbortedAt = startedAt.AddSeconds(20)
        };
    }

    private static MeasurementHardwareSnapshot CreateHardware() => new(
        Guid.Parse("84452115-0de9-489b-acd8-df0178db080d"),
        "Test-DAC + Testkopfhörer",
        "endpoint-topping",
        "Lautsprecher (TOPPING USB DAC)",
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
        DateTimeOffset.Parse("2026-09-02T07:30:00Z"));
}
