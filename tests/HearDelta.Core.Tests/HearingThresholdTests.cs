using HearDelta.Core;

namespace HearDelta.Core.Tests;

public sealed class HearingThresholdTests
{
    [Fact]
    public void FixedCatalogStartsAtCenterThenMovesUpAndDown()
    {
        var tones = HearingThresholdToneCatalog.Create(ThresholdToneOrder.Ascending, 123);
        double[] expected =
        [
            500d, 750d, 1_000d, 1_500d, 2_000d, 3_000d, 4_000d,
            6_000d, 8_000d, 10_000d, 375d, 250d, 125d, 62.5d
        ];

        Assert.Equal(14, tones.Count);
        Assert.Equal(expected, tones.Select(tone => tone.FrequencyHz));
        Assert.Equal("500 Hz", tones[0].DisplayLabel);
        Assert.Equal("62,5 Hz", tones[^1].DisplayLabel);
        Assert.Equal(Enumerable.Range(1, 14), tones.Select(tone => tone.PresentationOrder));
    }

    [Fact]
    public void RandomCatalogIsSeedStableAndContainsEveryToneOnce()
    {
        var ascending = HearingThresholdToneCatalog.Create(ThresholdToneOrder.Ascending, 0);
        var first = HearingThresholdToneCatalog.Create(ThresholdToneOrder.Random, 20260902);
        var second = HearingThresholdToneCatalog.Create(ThresholdToneOrder.Random, 20260902);

        Assert.Equal(
            first.Select(tone => tone.FrequencyHz),
            second.Select(tone => tone.FrequencyHz));
        Assert.Equal(500d, first[0].FrequencyHz);
        Assert.NotEqual(
            ascending.Select(tone => tone.FrequencyHz),
            first.Select(tone => tone.FrequencyHz));
        Assert.Equal(
            ascending.Select(tone => tone.FrequencyHz).Order(),
            first.Select(tone => tone.FrequencyHz).Order());
    }

    [Fact]
    public void SessionStoresHardwareTonePlanAndDigitalRamp()
    {
        var hardware = CreateHardware();
        var session = HearingThresholdSessionFactory.Create(
            TestedEar.Left,
            ThresholdToneOrder.Random,
            hardware,
            DateTimeOffset.Parse("2026-09-02T08:00:00Z"),
            randomizationSeed: 42);

        Assert.Equal(-80m, session.StartAttenuationDbfs);
        Assert.Equal(-30m, session.MaximumAttenuationDbfs);
        Assert.Equal(3m, session.LevelStepDb);
        Assert.Equal(-30m, session.Hardware.MaximumVolumeDb);
        Assert.Equal(ThresholdSignalPattern.Current, session.SignalPattern);
        Assert.Equal(hardware.EndpointId, session.Hardware.EndpointId);
        Assert.Empty(session.Observations);
        Assert.Empty(HearingThresholdSessionRules.Validate(session));
    }

    [Fact]
    public void RampRisesToTheProfileMaximumOnlyFromProtocolV12()
    {
        var startedAt = DateTimeOffset.Parse("2026-09-02T08:00:00Z");
        var session = HearingThresholdSessionFactory.Create(
            TestedEar.Left, ThresholdToneOrder.Ascending, CreateHardware() with { MaximumVolumeDb = 0m }, startedAt, randomizationSeed: 42);

        Assert.Equal(0m, session.MaximumAttenuationDbfs);
        Assert.Equal(0m, session.Hardware.MaximumVolumeDb);
        Assert.Empty(HearingThresholdSessionRules.Validate(session));
        Assert.Contains(
            HearingThresholdSessionRules.Validate(session with { MaximumAttenuationDbfs = -6m }),
            error => error.Contains("Obergrenze"));
        Assert.Contains(
            HearingThresholdSessionRules.Validate(session with { ProtocolVersion = 11 }),
            error => error.Contains("Obergrenze"));
        Assert.Throws<ArgumentOutOfRangeException>(() => HearingThresholdSessionFactory.Create(
            TestedEar.Left, ThresholdToneOrder.Ascending, CreateHardware() with { MaximumVolumeDb = 3m }, startedAt, randomizationSeed: 42));

        // Eine niedrige Profilgrenze verschiebt die Vorgabe so, dass mindestens sechs Stufen bleiben.
        var low = HearingThresholdSessionFactory.Create(
            TestedEar.Left, ThresholdToneOrder.Ascending, CreateHardware() with { MaximumVolumeDb = -70m, StartVolumeDb = -75m }, startedAt, randomizationSeed: 42);
        Assert.Equal(-88m, low.StartAttenuationDbfs);
        Assert.Equal(-70m, low.MaximumAttenuationDbfs);
    }

    [Fact]
    public void ChosenStartLevelIsStoredAndLimited()
    {
        var startedAt = DateTimeOffset.Parse("2026-09-02T08:00:00Z");
        var session = HearingThresholdSessionFactory.Create(
            TestedEar.Left, ThresholdToneOrder.Ascending, CreateHardware(), startedAt, randomizationSeed: 42,
            startAttenuationDbfs: -50m);

        Assert.Equal(-50m, session.StartAttenuationDbfs);
        Assert.Empty(HearingThresholdSessionRules.Validate(session));
        Assert.Equal(-50m, HearingThresholdProtocol.CalculateToneStartAttenuationDbfs(-50m, 1_000d, [(500d, -48m)]));
        Assert.Throws<ArgumentOutOfRangeException>(() => HearingThresholdSessionFactory.Create(
            TestedEar.Left, ThresholdToneOrder.Ascending, CreateHardware(), startedAt, randomizationSeed: 42,
            startAttenuationDbfs: -40m));
        Assert.Contains(
            HearingThresholdSessionRules.Validate(session with { StartAttenuationDbfs = -40m }),
            error => error.Contains("Startpegel"));
    }

    [Fact]
    public void CurrentSessionRejectsPreviousOnePointFiveDbLevelStep()
    {
        var session = HearingThresholdSessionFactory.Create(
            TestedEar.Left,
            ThresholdToneOrder.Ascending,
            CreateHardware(),
            DateTimeOffset.Parse("2026-09-02T08:00:00Z"),
            randomizationSeed: 42) with
        {
            LevelStepDb = 1.5m
        };

        Assert.Contains(
            HearingThresholdSessionRules.Validate(session),
            error => error.Contains("3-dB-Pegelschritt"));
    }

    [Fact]
    public void LegacyWithHearingAidRequiresMatchingImmutableDeviceSnapshot()
    {
        var session = HearingThresholdSessionFactory.Create(
            TestedEar.Left,
            ThresholdToneOrder.Ascending,
            LegacyHardware(),
            DateTimeOffset.Parse("2026-09-02T08:00:00Z"),
            randomizationSeed: 42) with
        {
            ProtocolVersion = 11,
            Condition = HearingAidCondition.WithHearingAid
        };

        Assert.Contains(
            HearingThresholdSessionRules.Validate(session),
            error => error.Contains("Hörgeräte-Snapshot"));
        Assert.Empty(HearingThresholdSessionRules.Validate(session with
        {
            HearingAid = new HearingAidSnapshot(Guid.NewGuid(), "Test", "Gerät", "Test Gerät", TestedEar.Left)
        }));
    }

    [Fact]
    public void CurrentProtocolMeasuresOnlyWithoutHearingAid()
    {
        var session = HearingThresholdSessionFactory.Create(
            TestedEar.Left,
            ThresholdToneOrder.Ascending,
            CreateHardware(),
            DateTimeOffset.Parse("2026-09-02T08:00:00Z"),
            randomizationSeed: 42);

        Assert.Equal(HearingAidCondition.WithoutHearingAid, session.Condition);
        Assert.Null(session.HearingAid);
        Assert.Contains(
            HearingThresholdSessionRules.Validate(session with
            {
                Condition = HearingAidCondition.WithHearingAid,
                HearingAid = new HearingAidSnapshot(Guid.NewGuid(), "Test", "Gerät", "Test Gerät", TestedEar.Left)
            }),
            error => error.Contains("nur ohne Hörgerät"));
    }

    [Fact]
    public void MaskingIsStoredForTheOppositeEarAndValidated()
    {
        var startedAt = DateTimeOffset.Parse("2026-09-02T08:00:00Z");
        var session = HearingThresholdSessionFactory.Create(
            TestedEar.Left,
            ThresholdToneOrder.Ascending,
            CreateHardware(),
            startedAt,
            randomizationSeed: 42,
            maskingLevelDbfs: -50m);

        Assert.Equal(ThresholdMaskingProtocol.Create(-50m, 42), session.Masking);
        Assert.Equal(TestedEar.Right, session.MaskedEar);
        Assert.Empty(HearingThresholdSessionRules.Validate(session));
        Assert.Contains(
            HearingThresholdSessionRules.Validate(session with { ProtocolVersion = 11 }),
            error => error.Contains("keine Vertäubung"));
        Assert.Contains(
            HearingThresholdSessionRules.Validate(session with { Masking = session.Masking! with { LevelDbfs = -10m } }),
            error => error.Contains("Vertäubungspegel"));
        Assert.Contains(
            HearingThresholdSessionRules.Validate(session with { Masking = session.Masking! with { Algorithm = "white-noise" } }),
            error => error.Contains("Schmalbandrauschen"));

        var tone = session.Tones[0];
        var presentation = new ThresholdTonePresentationRecord(
            session.Hardware.EndpointId, session.Hardware.EndpointName, tone.FrequencyHz, session.StartAttenuationDbfs,
            session.MaximumAttenuationDbfs, session.MaximumAttenuationDbfs, session.LevelStepDb, session.Hardware.SampleRate,
            startedAt, startedAt.AddSeconds(90), true, session.SignalPattern, MaskingLevelDbfs: -50m);
        var observation = new HearingThresholdObservation(
            tone.PresentationOrder, tone.MidiNoteNumber, false, null, startedAt.AddSeconds(90), presentation);

        Assert.Empty(HearingThresholdSessionRules.Validate(session with { Observations = [observation] }));
        Assert.Contains(
            HearingThresholdSessionRules.Validate(session with
            {
                Observations = [observation with { Presentation = presentation with { MaskingLevelDbfs = null } }]
            }),
            error => error.Contains("Vertäubung"));
        Assert.Contains(
            HearingThresholdSessionRules.Validate(session with { Masking = null, Observations = [observation] }),
            error => error.Contains("Vertäubung"));
    }

    [Fact]
    public void MaskingNoiseIsReproducibleThirdOctaveBandNoiseWithUnitRms()
    {
        var first = ThresholdMaskingProtocol.CreateNoiseLoop(1_000d, 48_000, 7);
        var second = ThresholdMaskingProtocol.CreateNoiseLoop(1_000d, 48_000, 7);
        var otherSeed = ThresholdMaskingProtocol.CreateNoiseLoop(1_000d, 48_000, 8);

        Assert.Equal(131_072, first.Length);
        Assert.Equal(first, second);
        Assert.NotEqual(first, otherSeed);
        Assert.Equal(1d, Math.Sqrt(first.Average(sample => (double)sample * sample)), 3);

        // Spektrallinien der periodischen Schleife: innerhalb der Terz Energie, außerhalb keine.
        double Magnitude(double frequencyHz)
        {
            var bin = Math.Round(frequencyHz * first.Length / 48_000d);
            double re = 0, im = 0;
            for (var index = 0; index < first.Length; index++)
            {
                var angle = 2 * Math.PI * bin * index / first.Length;
                re += first[index] * Math.Cos(angle);
                im -= first[index] * Math.Sin(angle);
            }
            return Math.Sqrt((re * re) + (im * im)) / first.Length;
        }
        var (lower, upper) = ThresholdMaskingProtocol.GetBand(1_000d);
        Assert.Equal(890.9d, lower, 1);
        Assert.Equal(1_122.5d, upper, 1);
        var inBand = Magnitude(1_000d);
        Assert.True(inBand > 0.005d);
        Assert.True(Magnitude(700d) < inBand * 1e-3);
        Assert.True(Magnitude(1_400d) < inBand * 1e-3);
        Assert.Throws<ArgumentOutOfRangeException>(() => ThresholdMaskingProtocol.CreateNoiseLoop(10_000d, 16_000, 1));
    }

    [Fact]
    public void SampleRateMustCoverHighestRequestedTone()
    {
        var hardware = CreateHardware() with { SampleRate = 16_000 };
        var tones = HearingThresholdToneCatalog.Create(ThresholdToneOrder.Ascending, 0);

        Assert.Contains(
            HearingThresholdSessionRules.ValidatePlaybackRange(hardware, tones),
            error => error.Contains("Abtastrate"));
    }

    [Fact]
    public void ToneStartUsesNearestHeardNeighborTowardCenter()
    {
        (double FrequencyHz, decimal ThresholdAttenuationDbfs)[] heardTones =
        [
            (500d, -72.5m),
            (750d, -68m),
            (2_000d, -60m),
            (375d, -75m)
        ];

        Assert.Equal(
            -74m,
            HearingThresholdProtocol.CalculateToneStartAttenuationDbfs(
                -100m,
                1_500d,
                heardTones));
        Assert.Equal(
            -81m,
            HearingThresholdProtocol.CalculateToneStartAttenuationDbfs(
                -100m,
                250d,
                heardTones));
        Assert.Equal(
            -100m,
            HearingThresholdProtocol.CalculateToneStartAttenuationDbfs(
                -100m,
                500d,
                heardTones));
        Assert.Equal(
            -100m,
            HearingThresholdProtocol.CalculateToneStartAttenuationDbfs(
                -100m,
                8_000d,
                []));
        Assert.Equal(
            -74m,
            HearingThresholdProtocol.CalculateToneStartAttenuationDbfs(
                -100m,
                1_500d,
                heardTones));
    }

    [Fact]
    public void CenterAndFollowingToneUseIndividuallyValidatedStartLevels()
    {
        var startedAt = DateTimeOffset.Parse("2026-09-02T08:00:00Z");
        var session = HearingThresholdSessionFactory.Create(
            TestedEar.Left,
            ThresholdToneOrder.Ascending,
            CreateHardware(),
            startedAt,
            randomizationSeed: 42);
        var centerTone = session.Tones[0];
        var centerPresentation = new ThresholdTonePresentationRecord(
            session.Hardware.EndpointId,
            session.Hardware.EndpointName,
            centerTone.FrequencyHz,
            session.StartAttenuationDbfs,
            -70m,
            session.MaximumAttenuationDbfs,
            session.LevelStepDb,
            session.Hardware.SampleRate,
            startedAt,
            startedAt.AddSeconds(7),
            false,
            session.SignalPattern);
        var centerConfirmation = centerPresentation with
        {
            StartAttenuationDbfs = -79m,
            EndAttenuationDbfs = -72.5m,
            StartedAt = startedAt.AddSeconds(7),
            CompletedAt = startedAt.AddSeconds(14)
        };
        var centerObservation = new HearingThresholdObservation(
            centerTone.PresentationOrder,
            centerTone.MidiNoteNumber,
            true,
            -72.5m,
            startedAt.AddSeconds(14),
            centerPresentation,
            centerConfirmation);
        var followingTone = session.Tones[1];
        var followingPresentation = centerPresentation with
        {
            FrequencyHz = followingTone.FrequencyHz,
            StartAttenuationDbfs = -78.5m,
            EndAttenuationDbfs = -70m,
            StartedAt = startedAt.AddSeconds(15),
            CompletedAt = startedAt.AddSeconds(22)
        };
        var followingObservation = new HearingThresholdObservation(
            followingTone.PresentationOrder,
            followingTone.MidiNoteNumber,
            true,
            -70m,
            startedAt.AddSeconds(29),
            followingPresentation,
            followingPresentation with
            {
                StartAttenuationDbfs = -79m,
                StartedAt = startedAt.AddSeconds(22),
                CompletedAt = startedAt.AddSeconds(29)
            });
        session = session with { Observations = [centerObservation, followingObservation] };

        Assert.Empty(HearingThresholdSessionRules.Validate(session));

        var invalidSession = session with
        {
            Observations =
            [
                centerObservation,
                followingObservation with
                {
                    Presentation = followingPresentation with { StartAttenuationDbfs = -80m }
                }
            ]
        };
        Assert.Contains(
            HearingThresholdSessionRules.Validate(invalidSession),
            error => error.Contains("Pegelrampe"));

        var unconfirmed = session with
        {
            Observations = [centerObservation with { Confirmation = null }]
        };
        Assert.Contains(
            HearingThresholdSessionRules.Validate(unconfirmed),
            error => error.Contains("Bestätigungswiederholung"));

        var wrongConfirmationStart = session with
        {
            Observations = [centerObservation with { Confirmation = centerConfirmation with { StartAttenuationDbfs = -76m } }]
        };
        Assert.Contains(
            HearingThresholdSessionRules.Validate(wrongConfirmationStart),
            error => error.Contains("Bestätigungsnachweis") && error.Contains("Pegelrampe"));
    }

    [Fact]
    public void UnconfirmedFirstReactionIsRecordedAsNotHeardWithBothPresentations()
    {
        var startedAt = DateTimeOffset.Parse("2026-09-02T08:00:00Z");
        var session = HearingThresholdSessionFactory.Create(
            TestedEar.Left,
            ThresholdToneOrder.Ascending,
            CreateHardware(),
            startedAt,
            randomizationSeed: 42);
        var tone = session.Tones[0];
        var first = new ThresholdTonePresentationRecord(
            session.Hardware.EndpointId,
            session.Hardware.EndpointName,
            tone.FrequencyHz,
            session.StartAttenuationDbfs,
            -45m,
            session.MaximumAttenuationDbfs,
            session.LevelStepDb,
            session.Hardware.SampleRate,
            startedAt,
            startedAt.AddSeconds(20),
            false,
            session.SignalPattern);
        var confirmation = first with
        {
            StartAttenuationDbfs = -54m,
            EndAttenuationDbfs = session.MaximumAttenuationDbfs,
            StartedAt = startedAt.AddSeconds(20),
            CompletedAt = startedAt.AddSeconds(90),
            ReachedMaximum = true
        };
        var observation = new HearingThresholdObservation(
            tone.PresentationOrder, tone.MidiNoteNumber, false, null, startedAt.AddSeconds(90), first, confirmation);

        Assert.Equal(-54m, HearingThresholdProtocol.CalculateConfirmationStartAttenuationDbfs(-45m));
        Assert.Empty(HearingThresholdSessionRules.Validate(session with { Observations = [observation] }));
        Assert.Contains(
            HearingThresholdSessionRules.Validate(session with
            {
                Observations = [observation with { Confirmation = confirmation with { ReachedMaximum = false } }]
            }),
            error => error.Contains("Pegelobergrenze"));
    }

    [Fact]
    public void NonCurrentProtocolAndToneCatalogAreRejected()
    {
        var session = HearingThresholdSessionFactory.Create(
            TestedEar.Left,
            ThresholdToneOrder.Ascending,
            CreateHardware(),
            DateTimeOffset.Parse("2026-09-02T08:00:00Z"),
            randomizationSeed: 42) with
        {
            ProtocolVersion = HearingThresholdProtocol.OldestReadableVersion - 1,
            ToneCatalogVersion = "obsolete"
        };

        var errors = HearingThresholdSessionRules.Validate(session);
        Assert.Contains(errors, error => error.Contains("Protokollversion"));
        Assert.Contains(errors, error => error.Contains("Tonkatalog"));
    }

    [Fact]
    public void V10SessionWithoutConfirmationStaysReadableAndIsFlaggedInComparisons()
    {
        var startedAt = DateTimeOffset.Parse("2026-09-02T08:00:00Z");
        var current = HearingThresholdSessionFactory.Create(
            TestedEar.Left,
            ThresholdToneOrder.Ascending,
            LegacyHardware(),
            startedAt,
            randomizationSeed: 42);
        var tone = current.Tones[0];
        var presentation = new ThresholdTonePresentationRecord(
            current.Hardware.EndpointId,
            current.Hardware.EndpointName,
            tone.FrequencyHz,
            current.StartAttenuationDbfs,
            -70m,
            current.MaximumAttenuationDbfs,
            current.LevelStepDb,
            current.Hardware.SampleRate,
            startedAt,
            startedAt.AddSeconds(7),
            false,
            current.SignalPattern);
        var heard = new HearingThresholdObservation(
            tone.PresentationOrder, tone.MidiNoteNumber, true, -70m, startedAt.AddSeconds(7), presentation);
        var v10 = current with { ProtocolVersion = 10, Observations = [heard] };

        Assert.Empty(HearingThresholdSessionRules.Validate(v10));
        Assert.Contains(
            HearingThresholdSessionRules.Validate(v10 with
            {
                Observations = [heard with { Confirmation = presentation with { StartAttenuationDbfs = -79m } }]
            }),
            error => error.Contains("v10 unbekannte Bestätigungswiederholung"));
        Assert.Contains(
            HearingThresholdComparisonRules.GetDifferences([v10, current]),
            difference => difference.Contains("Protokollversion"));
        Assert.Contains(
            HearingThresholdSessionRules.Validate(current with { ProtocolVersion = HearingThresholdProtocol.CurrentVersion + 1 }),
            error => error.Contains("Protokollversion"));
    }

    [Fact]
    public void LegacyV9SessionWithItsOwnSignalIsReadableButNotWithTheNewSignal()
    {
        var current = HearingThresholdSessionFactory.Create(
            TestedEar.Left,
            ThresholdToneOrder.Ascending,
            LegacyHardware(),
            DateTimeOffset.Parse("2026-09-02T08:00:00Z"),
            randomizationSeed: 42);
        var legacy = current with
        {
            ProtocolVersion = 9,
            SignalPattern = ThresholdSignalPattern.LegacyV9
        };

        Assert.Empty(HearingThresholdSessionRules.Validate(legacy));
        Assert.Contains(
            HearingThresholdSessionRules.Validate(legacy with { SignalPattern = ThresholdSignalPattern.Current }),
            error => error.Contains("Tonsignal"));
        Assert.Contains(
            HearingThresholdComparisonRules.GetDifferences([legacy, current]),
            difference => difference.Contains("Tonsignal"));
        Assert.Equal([0, 1_500], ThresholdSignalPattern.LegacyV9.GetToneOnsetsMilliseconds());
        Assert.Equal(3_000, ThresholdSignalPattern.LegacyV9.GetLevelDurationMilliseconds());
    }

    /// <summary>Messaufbau der Protokolle v9 bis v11 mit der damals festen Obergrenze von -6 dBFS.</summary>
    private static MeasurementHardwareSnapshot LegacyHardware() => CreateHardware() with { MaximumVolumeDb = -6m };

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
