namespace HearDelta.Core;

public enum ThresholdToneOrder
{
    Ascending = 1,
    Random = 2
}

/// <summary>
/// Tonsignal einer Pegelstufe: <see cref="SignalsPerLevel"/>-mal
/// [<see cref="TonesPerGroup"/> kurze Töne · <see cref="GroupPauseMilliseconds"/> Pause · <see cref="TonesPerGroup"/> kurze Töne],
/// jeweils gefolgt von <see cref="SignalPauseMilliseconds"/> Stille. Jeder Ton beginnt und endet mit einer Kosinusblende.
/// </summary>
public sealed record ThresholdSignalPattern(
    int ToneMilliseconds,
    int ToneGapMilliseconds,
    int TonesPerGroup,
    int GroupPauseMilliseconds,
    int GroupsPerSignal,
    int SignalPauseMilliseconds,
    int SignalsPerLevel,
    int FadeMilliseconds)
{
    /// <summary>2 × [3 × 150 ms Ton mit 100 ms Lücke · 500 ms · 3 × 150 ms Ton], je 750 ms Pause; 25-ms-Blenden.</summary>
    public static ThresholdSignalPattern Current { get; } = new(150, 100, 3, 500, 2, 750, 2, 25);

    /// <summary>Signal des Protokolls v9: zwei 750-ms-Töne je Stufe mit je 750 ms Pause; 50-ms-Blenden.</summary>
    public static ThresholdSignalPattern LegacyV9 { get; } = new(750, 0, 1, 0, 1, 750, 2, 50);

    public int GetGroupDurationMilliseconds() => TonesPerGroup * ToneMilliseconds + (TonesPerGroup - 1) * ToneGapMilliseconds;

    public int GetSignalDurationMilliseconds() =>
        GroupsPerSignal * GetGroupDurationMilliseconds() + (GroupsPerSignal - 1) * GroupPauseMilliseconds;

    /// <summary>Dauer einer vollständigen Pegelstufe einschließlich der Pause nach dem letzten Signal.</summary>
    public int GetLevelDurationMilliseconds() => SignalsPerLevel * (GetSignalDurationMilliseconds() + SignalPauseMilliseconds);

    public int GetTonesPerLevel() => TonesPerGroup * GroupsPerSignal * SignalsPerLevel;

    /// <summary>Beginn jedes Tons innerhalb einer Pegelstufe in Millisekunden, aufsteigend.</summary>
    public IReadOnlyList<int> GetToneOnsetsMilliseconds()
    {
        var onsets = new List<int>(GetTonesPerLevel());
        for (var signal = 0; signal < SignalsPerLevel; signal++)
        {
            var signalStart = signal * (GetSignalDurationMilliseconds() + SignalPauseMilliseconds);
            for (var group = 0; group < GroupsPerSignal; group++)
            {
                var groupStart = signalStart + group * (GetGroupDurationMilliseconds() + GroupPauseMilliseconds);
                for (var tone = 0; tone < TonesPerGroup; tone++)
                    onsets.Add(groupStart + tone * (ToneMilliseconds + ToneGapMilliseconds));
            }
        }
        return onsets;
    }

    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();
        if (ToneMilliseconds <= 0 || TonesPerGroup <= 0 || GroupsPerSignal <= 0 || SignalsPerLevel <= 0)
            errors.Add("Tonlänge und Anzahl der Töne, Gruppen und Signale müssen positiv sein.");
        if (ToneGapMilliseconds < 0 || GroupPauseMilliseconds < 0 || SignalPauseMilliseconds < 0)
            errors.Add("Lücken und Pausen dürfen nicht negativ sein.");
        if (FadeMilliseconds <= 0 || FadeMilliseconds * 2 > ToneMilliseconds)
            errors.Add("Die Blende muss positiv sein und darf höchstens die halbe Tonlänge umfassen.");
        return errors;
    }
}

public static class HearingThresholdProtocol
{
    public const int CurrentVersion = 12;

    /// <summary>
    /// Kontrakt (2026-09-30): Hörschwellentests aller Protokollversionen ab v9 bleiben lesbar. Ältere Versionen werden
    /// angezeigt, verglichen und gelöscht, aber nie neu erzeugt oder verändert. Neue Tests verwenden immer
    /// <see cref="CurrentVersion"/>; jede künftige Version muss in <see cref="SignalPatternFor"/> ergänzt werden.
    /// </summary>
    public const int OldestReadableVersion = 9;

    /// <summary>Ab dieser Version wird jede Reaktion durch eine Wiederholung bestätigt; v9 und v10 kennen sie nicht.</summary>
    public const int FirstConfirmationVersion = 11;

    public static bool UsesConfirmation(int protocolVersion) => protocolVersion >= FirstConfirmationVersion;

    /// <summary>
    /// Ab dieser Version gibt es keine Hörgerätebedingung mehr (Hörgeräte unterdrücken Sinustöne); stattdessen kann das
    /// Gegenohr vertäubt werden. v9 bis v11 behalten ihre gespeicherte Hörgerätebedingung und kennen keine Vertäubung.
    /// </summary>
    public const int FirstMaskingVersion = 12;

    public static bool SupportsMasking(int protocolVersion) => protocolVersion >= FirstMaskingVersion;

    /// <summary>Das für eine lesbare Protokollversion verbindliche Tonsignal, sonst null.</summary>
    public static ThresholdSignalPattern? SignalPatternFor(int protocolVersion) => protocolVersion switch
    {
        9 => ThresholdSignalPattern.LegacyV9,
        10 or 11 or 12 => ThresholdSignalPattern.Current,
        _ => null
    };
    public const string ToneCatalogVersion = "centered-14-frequencies-v1";
    public const double CenterFrequencyHz = 500d;
    public const decimal DefaultStartAttenuationDbfs = -80m;

    /// <summary>Leisester wählbarer Startpegel, sofern das Messprofil keinen noch leiseren Startpegel vorgibt.</summary>
    public const decimal LowestStartAttenuationDbfs = -90m;

    /// <summary>
    /// Mindestabstand zwischen Startpegel und Obergrenze. Bei starker Schwerhörigkeit lässt sich der Test lauter
    /// beginnen, damit nicht viele unhörbare Stufen durchlaufen werden; bis zur Obergrenze bleiben mindestens sechs Stufen.
    /// </summary>
    public const decimal MinimumRampDb = 18m;

    /// <summary>Höchster wählbarer Startpegel für eine Obergrenze.</summary>
    public static decimal HighestStartAttenuationFor(decimal maximumAttenuationDbfs) =>
        maximumAttenuationDbfs - MinimumRampDb;

    /// <summary>
    /// Vorgabe-Startpegel: -80 dBFS oder der leisere Startpegel des Messprofils, höchstens aber
    /// <see cref="MinimumRampDb"/> unter der Obergrenze des Messprofils.
    /// </summary>
    public static decimal DefaultStartAttenuationFor(MeasurementHardwareSnapshot hardware) => Math.Min(
        Math.Min(DefaultStartAttenuationDbfs, hardware.StartVolumeDb),
        HighestStartAttenuationFor(hardware.MaximumVolumeDb));

    /// <summary>Leisester wählbarer Startpegel: -90 dBFS oder der leisere Startpegel des Messprofils.</summary>
    public static decimal LowestStartAttenuationFor(decimal profileStartVolumeDb) =>
        Math.Min(LowestStartAttenuationDbfs, profileStartVolumeDb);

    /// <summary>Feste Obergrenze der Protokolle v9 bis v11.</summary>
    public const decimal LegacyMaximumAttenuationDbfs = -6m;

    /// <summary>Ab dieser Version läuft jeder Ton bis zur Pegelobergrenze des Messprofils statt bis -6 dBFS.</summary>
    public const int FirstProfileMaximumVersion = 12;
    public const decimal LevelStepDb = 3m;
    public const decimal ToneStartOffsetDb = 6m;

    /// <summary>
    /// Nach der ersten Reaktion wird derselbe Ton zur Bestätigung erneut aufsteigend dargeboten, beginnend so viele dB
    /// unter dem zuerst erkannten Pegel. Maßgeblich ist der Wert dieser Wiederholung.
    /// </summary>
    public const decimal ConfirmationStartOffsetDb = 9m;

    public static decimal CalculateConfirmationStartAttenuationDbfs(decimal firstDetectionAttenuationDbfs) =>
        firstDetectionAttenuationDbfs - ConfirmationStartOffsetDb;
    public static ThresholdSignalPattern SignalPattern => ThresholdSignalPattern.Current;

    public static decimal CalculateToneStartAttenuationDbfs(
        decimal initialStartAttenuationDbfs,
        double frequencyHz,
        IEnumerable<(double FrequencyHz, decimal ThresholdAttenuationDbfs)> heardTones)
    {
        ArgumentNullException.ThrowIfNull(heardTones);
        if (Math.Abs(frequencyHz - CenterFrequencyHz) < 0.001d)
            return initialStartAttenuationDbfs;

        var candidates = heardTones.Where(candidate =>
            frequencyHz > CenterFrequencyHz
                ? candidate.FrequencyHz >= CenterFrequencyHz && candidate.FrequencyHz < frequencyHz
                : candidate.FrequencyHz <= CenterFrequencyHz && candidate.FrequencyHz > frequencyHz);
        var neighbor = frequencyHz > CenterFrequencyHz
            ? candidates.OrderByDescending(candidate => candidate.FrequencyHz).FirstOrDefault()
            : candidates.OrderBy(candidate => candidate.FrequencyHz).FirstOrDefault();

        return neighbor == default
            ? initialStartAttenuationDbfs
            : Math.Max(initialStartAttenuationDbfs, neighbor.ThresholdAttenuationDbfs - ToneStartOffsetDb);
    }
}

public sealed record HearingThresholdTone(
    int PresentationOrder,
    int MidiNoteNumber,
    string NoteName,
    int Octave,
    double FrequencyHz,
    string? DisplayLabel = null);

/// <summary>
/// Wiedergabenachweis eines Durchgangs. <see cref="HeadphoneCorrectionDb"/> ist die auf den Ton angewendete
/// Kopfhörerentzerrung (Vorabsenkung plus Filterverstärkung bei der Tonfrequenz, nie positiv); die Pegelangaben
/// bleiben die unentzerrten Nennpegel. Ohne Entzerrung ist der Wert null. <see cref="MaskingLevelDbfs"/> ist der
/// RMS-Pegel des während des Durchgangs auf dem Gegenohr gespielten Vertäubungsrauschens; dieselbe Entzerrung wirkt
/// auch auf das Rauschen. Ohne Vertäubung ist der Wert null.
/// </summary>
public sealed record ThresholdTonePresentationRecord(
    string EndpointId,
    string EndpointName,
    double FrequencyHz,
    decimal StartAttenuationDbfs,
    decimal EndAttenuationDbfs,
    decimal MaximumAttenuationDbfs,
    decimal LevelStepDb,
    int OutputSampleRate,
    DateTimeOffset StartedAt,
    DateTimeOffset CompletedAt,
    bool ReachedMaximum,
    ThresholdSignalPattern? SignalPattern = null,
    decimal? HeadphoneCorrectionDb = null,
    decimal? MaskingLevelDbfs = null);

/// <summary>
/// Ergebnis eines Tons. <see cref="Presentation"/> ist der erste aufsteigende Durchgang; wurde dort reagiert, enthält
/// <see cref="Confirmation"/> die Bestätigungswiederholung ab <see cref="HearingThresholdProtocol.ConfirmationStartOffsetDb"/>
/// unter der ersten Reaktion. <see cref="Heard"/> und <see cref="ThresholdAttenuationDbfs"/> beschreiben stets den
/// letzten Durchgang. Protokoll v9 kennt keine Bestätigungswiederholung.
/// </summary>
public sealed record HearingThresholdObservation(
    int PresentationOrder,
    int MidiNoteNumber,
    bool Heard,
    decimal? ThresholdAttenuationDbfs,
    DateTimeOffset RecordedAt,
    ThresholdTonePresentationRecord Presentation,
    ThresholdTonePresentationRecord? Confirmation = null);

/// <summary>
/// Ein Hörschwellentest. Ab Protokoll v12 wird immer ohne Hörgerät gemessen; <see cref="Masking"/> beschreibt die
/// optionale Vertäubung des Gegenohrs. <see cref="Condition"/> und <see cref="HearingAid"/> sind nur noch für gespeicherte
/// Tests der Protokolle v9 bis v11 belegt (Kontrakt) und in neuen Tests stets „ohne Hörgerät“ bzw. leer.
/// </summary>
public sealed record HearingThresholdSession(
    int ProtocolVersion,
    string ToneCatalogVersion,
    Guid Id,
    TestedEar Ear,
    ThresholdToneOrder ToneOrder,
    int RandomizationSeed,
    decimal StartAttenuationDbfs,
    decimal MaximumAttenuationDbfs,
    decimal LevelStepDb,
    MeasurementHardwareSnapshot Hardware,
    DateTimeOffset StartedAt,
    DateTimeOffset? CompletedAt,
    IReadOnlyList<HearingThresholdTone> Tones,
    IReadOnlyList<HearingThresholdObservation> Observations,
    DateTimeOffset? AbortedAt = null,
    ThresholdSignalPattern? SignalPattern = null,
    ThresholdMasking? Masking = null,
    HearingAidCondition Condition = HearingAidCondition.WithoutHearingAid,
    HearingAidSnapshot? HearingAid = null)
{
    /// <summary>Das vertäubte Gegenohr, sonst null.</summary>
    public TestedEar? MaskedEar => Masking is null ? null : Ear == TestedEar.Left ? TestedEar.Right : TestedEar.Left;

    /// <summary>Gespeicherter Altbestand (v9–v11), der mit Hörgerät gemessen wurde.</summary>
    public bool IsLegacyWithHearingAid => Condition == HearingAidCondition.WithHearingAid;
}

public static class HearingThresholdToneCatalog
{
    private static readonly (double FrequencyHz, string DisplayLabel)[] FixedFrequencies =
    [
        (62.5d, "62,5 Hz"),
        (125d, "125 Hz"),
        (250d, "250 Hz"),
        (375d, "375 Hz"),
        (500d, "500 Hz"),
        (750d, "750 Hz"),
        (1_000d, "1 kHz"),
        (1_500d, "1,5 kHz"),
        (2_000d, "2 kHz"),
        (3_000d, "3 kHz"),
        (4_000d, "4 kHz"),
        (6_000d, "6 kHz"),
        (8_000d, "8 kHz"),
        (10_000d, "10 kHz")
    ];

    public static IReadOnlyList<HearingThresholdTone> Create(
        ThresholdToneOrder order,
        int randomizationSeed)
    {
        if (!Enum.IsDefined(order))
            throw new ArgumentOutOfRangeException(nameof(order));

        var tonesByFrequency = FixedFrequencies
            .Select((frequency, index) => new HearingThresholdTone(
                0,
                index + 1,
                string.Empty,
                0,
                frequency.FrequencyHz,
                frequency.DisplayLabel))
            .ToArray();

        var center = tonesByFrequency.Single(tone =>
            Math.Abs(tone.FrequencyHz - HearingThresholdProtocol.CenterFrequencyHz) < 0.001d);
        HearingThresholdTone[] tones;
        if (order == ThresholdToneOrder.Random)
        {
            var remaining = tonesByFrequency.Where(tone => !ReferenceEquals(tone, center)).ToArray();
            StableShuffle(remaining, unchecked((uint)randomizationSeed ^ 0x48544852u));
            tones = [center, .. remaining];
        }
        else
        {
            tones =
            [
                center,
                .. tonesByFrequency.Where(tone => tone.FrequencyHz > HearingThresholdProtocol.CenterFrequencyHz),
                .. tonesByFrequency.Where(tone => tone.FrequencyHz < HearingThresholdProtocol.CenterFrequencyHz)
                    .OrderByDescending(tone => tone.FrequencyHz)
            ];
        }

        return tones
            .Select((tone, index) => tone with { PresentationOrder = index + 1 })
            .ToArray();
    }

    private static void StableShuffle<T>(IList<T> values, uint state)
    {
        for (var index = values.Count - 1; index > 0; index--)
        {
            state = Next(state);
            var target = (int)(state % (uint)(index + 1));
            (values[index], values[target]) = (values[target], values[index]);
        }
    }

    private static uint Next(uint state)
    {
        state ^= state << 13;
        state ^= state >> 17;
        state ^= state << 5;
        return state == 0 ? 0x6D2B79F5u : state;
    }
}

public static class HearingThresholdSessionFactory
{
    /// <param name="maskingLevelDbfs">RMS-Pegel des Vertäubungsrauschens auf dem Gegenohr; null ohne Vertäubung.</param>
    /// <param name="startAttenuationDbfs">
    /// Gewählter Startpegel des 500-Hz-Tons und Untergrenze aller Folgetöne; null für die Vorgabe
    /// (<see cref="HearingThresholdProtocol.DefaultStartAttenuationFor"/>).
    /// </param>
    public static HearingThresholdSession Create(
        TestedEar ear,
        ThresholdToneOrder toneOrder,
        MeasurementHardwareSnapshot hardware,
        DateTimeOffset startedAt,
        int randomizationSeed,
        decimal? maskingLevelDbfs = null,
        decimal? startAttenuationDbfs = null)
    {
        ArgumentNullException.ThrowIfNull(hardware);
        // Jeder Ton steigt bis zur Pegelobergrenze des Messprofils.
        var maximumAttenuation = hardware.MaximumVolumeDb;
        if (maximumAttenuation > 0)
            throw new ArgumentOutOfRangeException(nameof(hardware), CoreStrings.Threshold_MaximumAboveZero);
        var startAttenuation = startAttenuationDbfs ?? HearingThresholdProtocol.DefaultStartAttenuationFor(hardware);
        var highestStart = HearingThresholdProtocol.HighestStartAttenuationFor(maximumAttenuation);
        if (startAttenuation > highestStart)
            throw new ArgumentOutOfRangeException(
                nameof(startAttenuationDbfs),
                string.Format(CoreStrings.Threshold_StartTooHigh, highestStart, HearingThresholdProtocol.MinimumRampDb));

        return new HearingThresholdSession(
            HearingThresholdProtocol.CurrentVersion,
            HearingThresholdProtocol.ToneCatalogVersion,
            Guid.NewGuid(),
            ear,
            toneOrder,
            randomizationSeed,
            startAttenuation,
            maximumAttenuation,
            HearingThresholdProtocol.LevelStepDb,
            hardware,
            startedAt,
            null,
            HearingThresholdToneCatalog.Create(toneOrder, randomizationSeed),
            [],
            SignalPattern: HearingThresholdProtocol.SignalPattern,
            Masking: maskingLevelDbfs is { } level ? ThresholdMaskingProtocol.Create(level, randomizationSeed) : null);
    }
}

public static class HearingThresholdSessionRules
{
    private const double FrequencyToleranceHz = 0.001;

    public static IReadOnlyList<string> Validate(HearingThresholdSession session)
    {
        var errors = new List<string>();

        var expectedPattern = HearingThresholdProtocol.SignalPatternFor(session.ProtocolVersion);
        if (expectedPattern is null)
            errors.Add($"Die Hörschwellen-Protokollversion {session.ProtocolVersion} wird nicht unterstützt (lesbar: {HearingThresholdProtocol.OldestReadableVersion} bis {HearingThresholdProtocol.CurrentVersion}).");
        if (!string.Equals(session.ToneCatalogVersion, HearingThresholdProtocol.ToneCatalogVersion, StringComparison.Ordinal))
            errors.Add("Nur der aktuelle Hörschwellen-Tonkatalog wird unterstützt.");
        if (session.Id == Guid.Empty)
            errors.Add("Die Hörschwellensitzung benötigt eine ID.");
        if (!Enum.IsDefined(session.Ear))
            errors.Add("Das geprüfte Ohr ist ungültig.");
        if (!Enum.IsDefined(session.Condition))
            errors.Add("Die Hörgerätebedingung ist ungültig.");
        if (!Enum.IsDefined(session.ToneOrder))
            errors.Add("Die Tonreihenfolge ist ungültig.");
        if (session.StartedAt == default)
            errors.Add("Der Startzeitpunkt der Hörschwellensitzung fehlt.");
        if (session.CompletedAt is { } completedAt && completedAt < session.StartedAt)
            errors.Add("Der Abschlusszeitpunkt darf nicht vor dem Start liegen.");
        if (session.AbortedAt is { } abortedAt && abortedAt < session.StartedAt)
            errors.Add("Der Abbruchzeitpunkt darf nicht vor dem Start liegen.");
        if (session.AbortedAt is not null && session.CompletedAt != session.AbortedAt)
            errors.Add("Ein abgebrochener Hörschwellentest benötigt denselben Abbruch- und Abschlusszeitpunkt.");
        if (session.StartAttenuationDbfs >= session.MaximumAttenuationDbfs)
            errors.Add("Der Startpegel muss unter der digitalen Pegelobergrenze liegen.");
        else if (session.StartAttenuationDbfs > HearingThresholdProtocol.HighestStartAttenuationFor(session.MaximumAttenuationDbfs))
            errors.Add($"Der Startpegel muss mindestens {HearingThresholdProtocol.MinimumRampDb:0} dB unter der Pegelobergrenze liegen.");
        if (session.MaximumAttenuationDbfs > 0 || session.MaximumAttenuationDbfs > session.Hardware.MaximumVolumeDb)
            errors.Add("Die digitale Pegelobergrenze widerspricht dem Hardware-Snapshot.");
        if (session.LevelStepDb <= 0)
            errors.Add("Der Pegelschritt muss größer als 0 dB sein.");
        else if (session.LevelStepDb != HearingThresholdProtocol.LevelStepDb)
            errors.Add($"Der aktuelle Hörschwellentest muss den festgelegten {HearingThresholdProtocol.LevelStepDb:0}-dB-Pegelschritt verwenden.");
        var expectedMaximum = session.ProtocolVersion >= HearingThresholdProtocol.FirstProfileMaximumVersion
            ? session.Hardware?.MaximumVolumeDb
            : HearingThresholdProtocol.LegacyMaximumAttenuationDbfs;
        if (session.MaximumAttenuationDbfs != expectedMaximum ||
            (expectedPattern is not null && session.SignalPattern != expectedPattern))
            errors.Add("Der Hörschwellentest muss die festgelegte Obergrenze und das festgelegte Tonsignal verwenden.");

        ValidateHearingAid(session, errors);
        ValidateMasking(session, errors);
        ValidateHardware(session.Hardware, errors);
        ValidateTones(session, errors);
        ValidateObservations(session, errors);

        return errors;
    }

    public static IReadOnlyList<string> ValidatePlaybackRange(
        MeasurementHardwareSnapshot hardware,
        IEnumerable<HearingThresholdTone> tones)
    {
        ArgumentNullException.ThrowIfNull(hardware);
        ArgumentNullException.ThrowIfNull(tones);
        var errors = new List<string>();
        var highestFrequency = tones.Max(tone => tone.FrequencyHz);
        if (highestFrequency >= hardware.SampleRate / 2d)
            errors.Add(
                $"Die Abtastrate von {hardware.SampleRate} Hz reicht für den höchsten Ton " +
                $"({highestFrequency:0.#} Hz) nicht aus.");
        return errors;
    }

    private static void ValidateMasking(HearingThresholdSession session, ICollection<string> errors)
    {
        if (session.Masking is not { } masking)
            return;
        if (!HearingThresholdProtocol.SupportsMasking(session.ProtocolVersion))
            errors.Add($"Das Protokoll v{session.ProtocolVersion} kennt keine Vertäubung des Gegenohrs.");
        foreach (var error in ThresholdMaskingProtocol.Validate(masking))
            errors.Add(error);
    }

    private static void ValidateHearingAid(HearingThresholdSession session, ICollection<string> errors)
    {
        if (HearingThresholdProtocol.SupportsMasking(session.ProtocolVersion) &&
            (session.Condition != HearingAidCondition.WithoutHearingAid || session.HearingAid is not null))
        {
            errors.Add("Ab Protokoll v12 wird die Hörschwelle nur ohne Hörgerät gemessen.");
            return;
        }
        if (session.Condition == HearingAidCondition.WithoutHearingAid && session.HearingAid is not null)
            errors.Add("Eine Messung ohne Hörgerät darf keinen Hörgeräte-Snapshot enthalten.");
        if (session.Condition != HearingAidCondition.WithHearingAid)
            return;
        if (session.HearingAid is null)
        {
            errors.Add("Für eine Messung mit Hörgerät fehlt der unveränderliche Hörgeräte-Snapshot.");
            return;
        }
        if (session.HearingAid.DeviceId == Guid.Empty ||
            string.IsNullOrWhiteSpace(session.HearingAid.Manufacturer) ||
            string.IsNullOrWhiteSpace(session.HearingAid.Model) ||
            string.IsNullOrWhiteSpace(session.HearingAid.DisplayName))
            errors.Add("Der Hörgeräte-Snapshot ist unvollständig.");
        if (session.HearingAid.FittedEar != session.Ear)
            errors.Add("Hörgerät und geprüftes Ohr müssen dieselbe Seite haben.");
    }

    private static void ValidateHardware(MeasurementHardwareSnapshot hardware, ICollection<string> errors)
    {
        if (hardware is null)
        {
            errors.Add("Der unveränderliche Hardware-Snapshot fehlt.");
            return;
        }
        if (hardware.ProfileId == Guid.Empty || string.IsNullOrWhiteSpace(hardware.EndpointId))
            errors.Add("Der Hardware-Snapshot benötigt Messprofil und gespeicherten Audioausgang.");
        if (hardware.Channels < 2 || hardware.SampleRate <= 0 || hardware.BitsPerSample <= 0)
            errors.Add("Das gespeicherte Audioformat des Hardware-Snapshots ist ungültig.");
        if (string.IsNullOrWhiteSpace(hardware.HeadphoneManufacturer) ||
            string.IsNullOrWhiteSpace(hardware.HeadphoneModel))
            errors.Add("Der Hardware-Snapshot benötigt den verwendeten Kopfhörer.");
    }

    private static void ValidateTones(HearingThresholdSession session, ICollection<string> errors)
    {
        if (session.Tones is null)
        {
            errors.Add("Der unveränderliche Tonplan fehlt.");
            return;
        }

        if (!string.Equals(session.ToneCatalogVersion, HearingThresholdProtocol.ToneCatalogVersion, StringComparison.Ordinal))
            return;

        var expected = HearingThresholdToneCatalog.Create(session.ToneOrder, session.RandomizationSeed);
        if (session.Tones.Count != expected.Count ||
            session.Tones.Zip(expected).Any(pair =>
                pair.First.PresentationOrder != pair.Second.PresentationOrder ||
                pair.First.MidiNoteNumber != pair.Second.MidiNoteNumber ||
                !string.Equals(pair.First.NoteName, pair.Second.NoteName, StringComparison.Ordinal) ||
                pair.First.Octave != pair.Second.Octave ||
                !string.Equals(pair.First.DisplayLabel, pair.Second.DisplayLabel, StringComparison.Ordinal) ||
                Math.Abs(pair.First.FrequencyHz - pair.Second.FrequencyHz) > FrequencyToleranceHz))
            errors.Add("Der gespeicherte Tonplan passt nicht zum Frequenzkatalog, zur Reihenfolge und zum Seed.");
    }

    private static void ValidateObservations(HearingThresholdSession session, ICollection<string> errors)
    {
        if (session.Observations is null)
        {
            errors.Add("Die Hörschwellenbeobachtungen fehlen.");
            return;
        }
        if (session.Observations.Count > session.Tones.Count ||
            session.Observations.Select(value => value.PresentationOrder).Distinct().Count() != session.Observations.Count)
            errors.Add("Die Hörschwellenbeobachtungen benötigen eindeutige Tonpositionen.");
        var priorHeardTones = new List<(double FrequencyHz, decimal ThresholdAttenuationDbfs)>();

        for (var observationIndex = 0; observationIndex < session.Observations.Count; observationIndex++)
        {
            var observation = session.Observations[observationIndex];
            var tone = session.Tones.SingleOrDefault(value => value.PresentationOrder == observation.PresentationOrder);
            if (tone is null || tone.MidiNoteNumber != observation.MidiNoteNumber)
                errors.Add($"Beobachtung {observation.PresentationOrder} gehört nicht zum gespeicherten Tonplan.");
            if (observation.PresentationOrder != observationIndex + 1)
                errors.Add("Die Beobachtungen müssen der gespeicherten zentrierten Wiedergabereihenfolge folgen.");
            if (observation.RecordedAt == default || observation.Presentation is null)
            {
                errors.Add($"Beobachtung {observation.PresentationOrder} ist unvollständig.");
                continue;
            }
            if (observation.Heard != observation.ThresholdAttenuationDbfs.HasValue)
                errors.Add($"Beobachtung {observation.PresentationOrder} enthält einen widersprüchlichen Hörschwellenwert.");

            var expectedStart = tone is not null
                ? HearingThresholdProtocol.CalculateToneStartAttenuationDbfs(
                    session.StartAttenuationDbfs,
                    tone.FrequencyHz,
                    priorHeardTones)
                : session.StartAttenuationDbfs;

            var presentation = observation.Presentation;
            ValidatePresentation(
                session,
                presentation,
                tone?.FrequencyHz,
                expectedStart,
                $"Wiedergabenachweis {observation.PresentationOrder}",
                errors);

            var finalPresentation = presentation;
            var finalStart = expectedStart;
            if (!HearingThresholdProtocol.UsesConfirmation(session.ProtocolVersion))
            {
                if (observation.Confirmation is not null)
                    errors.Add($"Beobachtung {observation.PresentationOrder} enthält eine im Protokoll v{session.ProtocolVersion} unbekannte Bestätigungswiederholung.");
            }
            else if (observation.Confirmation is { } confirmation)
            {
                finalStart = HearingThresholdProtocol.CalculateConfirmationStartAttenuationDbfs(presentation.EndAttenuationDbfs);
                finalPresentation = confirmation;
                ValidatePresentation(
                    session,
                    confirmation,
                    tone?.FrequencyHz,
                    finalStart,
                    $"Bestätigungsnachweis {observation.PresentationOrder}",
                    errors);
                if (confirmation.StartedAt < presentation.CompletedAt)
                    errors.Add($"Die Bestätigungswiederholung {observation.PresentationOrder} beginnt vor dem Ende des ersten Durchgangs.");
            }
            else if (observation.Heard)
            {
                errors.Add($"Beobachtung {observation.PresentationOrder} ist gehört, aber ohne Bestätigungswiederholung.");
            }

            if (observation.ThresholdAttenuationDbfs is { } threshold &&
                (threshold < finalStart || threshold > session.MaximumAttenuationDbfs))
                errors.Add($"Beobachtung {observation.PresentationOrder} liegt außerhalb der digitalen Pegelgrenzen.");
            if (observation.ThresholdAttenuationDbfs is { } heardAt && heardAt != finalPresentation.EndAttenuationDbfs)
                errors.Add($"Hörschwellenwert {observation.PresentationOrder} passt nicht zum Wiedergabeende.");
            if (!observation.Heard &&
                (!finalPresentation.ReachedMaximum || finalPresentation.EndAttenuationDbfs != session.MaximumAttenuationDbfs))
                errors.Add($"Eine nicht gehörte Beobachtung {observation.PresentationOrder} muss die Pegelobergrenze erreicht haben.");
            if (tone is not null && observation.ThresholdAttenuationDbfs is { } heardThreshold)
                priorHeardTones.Add((tone.FrequencyHz, heardThreshold));
        }
    }

    private static void ValidatePresentation(
        HearingThresholdSession session,
        ThresholdTonePresentationRecord presentation,
        double? expectedFrequencyHz,
        decimal expectedStartAttenuationDbfs,
        string label,
        ICollection<string> errors)
    {
        if (!string.Equals(presentation.EndpointId, session.Hardware.EndpointId, StringComparison.Ordinal) ||
            presentation.OutputSampleRate != session.Hardware.SampleRate)
            errors.Add($"{label} widerspricht dem Hardware-Snapshot.");
        if (expectedFrequencyHz is { } frequency &&
            Math.Abs(presentation.FrequencyHz - frequency) > FrequencyToleranceHz)
            errors.Add($"{label} verwendet eine andere Frequenz.");
        if (presentation.StartAttenuationDbfs != expectedStartAttenuationDbfs ||
            presentation.MaximumAttenuationDbfs != session.MaximumAttenuationDbfs ||
            presentation.LevelStepDb != session.LevelStepDb ||
            presentation.EndAttenuationDbfs < expectedStartAttenuationDbfs ||
            presentation.EndAttenuationDbfs > session.MaximumAttenuationDbfs)
            errors.Add($"{label} widerspricht der gespeicherten Pegelrampe.");
        if (presentation.StartedAt == default || presentation.CompletedAt < presentation.StartedAt)
            errors.Add($"{label} enthält einen ungültigen Wiedergabezeitraum.");
        if (presentation.SignalPattern != session.SignalPattern)
            errors.Add($"{label} widerspricht dem gespeicherten Tonsignal.");
        if (presentation.MaskingLevelDbfs != session.Masking?.LevelDbfs)
            errors.Add($"{label} widerspricht der gespeicherten Vertäubung des Gegenohrs.");
    }
}
