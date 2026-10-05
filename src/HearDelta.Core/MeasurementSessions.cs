using System.Security.Cryptography;

namespace HearDelta.Core;

public enum TestedEar
{
    Left = 1,
    Right = 2
}

public enum HearingAidCondition
{
    WithoutHearingAid = 1,
    WithHearingAid = 2
}

public enum SpeechMaterial
{
    Numbers = 1,
    Monosyllables = 2,
    Polysyllables = 3,
    PhonemeContrasts = 4
}

public enum ListeningEnvironment
{
    Quiet = 1,
    BackgroundNoise = 2
}

public static class MeasurementProtocol
{
    public const int CurrentVersion = 1;
}

public enum MeasurementTrainingStatus
{
    Measurement = 1,
    Practice = 2
}

public sealed record MeasurementSessionContract(
    int Version,
    string ModuleId,
    string ResponseFormat,
    string ScoringRule,
    MeasurementTrainingStatus TrainingStatus);

public static class MeasurementSessionContracts
{
    public static readonly MeasurementSessionContract PhonemeContrastMeasurement = new(
        Version: 1,
        ModuleId: "phoneme-contrast",
        ResponseFormat: "closed-set-five-alternatives-or-not-understood",
        ScoringRule: "exact-canonical-response",
        TrainingStatus: MeasurementTrainingStatus.Measurement);

    public static readonly MeasurementSessionContract CardinalNumberMeasurement = new(
        Version: 1,
        ModuleId: "cardinal-number",
        ResponseFormat: "open-set-three-digit-number-or-not-understood",
        ScoringRule: "exact-three-digit-integer-response",
        TrainingStatus: MeasurementTrainingStatus.Measurement);

    public static readonly MeasurementSessionContract AdaptiveCardinalNumberMeasurement = new(
        Version: 1,
        ModuleId: "cardinal-number-adaptive",
        ResponseFormat: "open-set-three-digit-number-or-not-understood",
        ScoringRule: "adaptive-one-up-one-down-50-percent-threshold",
        TrainingStatus: MeasurementTrainingStatus.Measurement);
}

public sealed record HearingAidSnapshot(
    Guid DeviceId,
    string Manufacturer,
    string Model,
    string DisplayName,
    TestedEar FittedEar,
    string? ProgramName = null,
    string? VolumeState = null);

public static class HearingAidIdentity
{
    public static Guid CreateStableId(string manufacturer, string model, TestedEar fittedEar)
    {
        if (string.IsNullOrWhiteSpace(manufacturer) || string.IsNullOrWhiteSpace(model))
            throw new ArgumentException("Hersteller und Modell werden für die Hörgeräte-ID benötigt.");
        if (!Enum.IsDefined(fittedEar))
            throw new ArgumentOutOfRangeException(nameof(fittedEar));

        var identity = $"{manufacturer.Trim().ToUpperInvariant()}\n{model.Trim().ToUpperInvariant()}\n{(int)fittedEar}";
        var hash = SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(identity));
        Span<byte> bytes = stackalloc byte[16];
        hash.AsSpan(0, 16).CopyTo(bytes);
        bytes[7] = (byte)((bytes[7] & 0x0F) | 0x50);
        bytes[8] = (byte)((bytes[8] & 0x3F) | 0x80);
        return new Guid(bytes);
    }
}

public sealed record RawMeasurementResponse(
    int PresentationOrder,
    string StimulusId,
    string? EnteredText,
    DateTimeOffset RecordedAt,
    StimulusPresentationRecord Presentation);

public sealed record StimulusPresentationRecord(
    string CatalogId,
    string CatalogVersion,
    string StimulusId,
    string AudioSha256,
    string EndpointId,
    string EndpointName,
    StimulusRenderMetadata RenderMetadata,
    DateTimeOffset StartedAt,
    DateTimeOffset CompletedAt);

public sealed record MeasurementBlock(
    Guid Id,
    int PresentationOrder,
    HearingAidCondition Condition,
    string StimulusListId,
    IReadOnlyList<RawMeasurementResponse> RawResponses,
    MeasurementSetupConfirmation? SetupConfirmation = null);

public sealed record MeasurementSetupConfirmation(
    bool CorrectEarAndChannelConfirmed,
    bool HeadphoneFitAndFeedbackChecked,
    DateTimeOffset ConfirmedAt);

public sealed record PairedMeasurementSession(
    int ProtocolVersion,
    Guid Id,
    TestedEar Ear,
    HearingAidSnapshot HearingAid,
    SpeechMaterial Material,
    ListeningEnvironment Environment,
    int RandomizationSeed,
    MeasurementHardwareSnapshot Hardware,
    DateTimeOffset StartedAt,
    DateTimeOffset? CompletedAt,
    IReadOnlyList<MeasurementBlock> Blocks,
    StimulusMaterialIdentity MaterialIdentity,
    MeasurementSessionContract Contract,
    DateTimeOffset? AbortedAt = null,
    AdaptiveTrackSettings? AdaptiveTrack = null,
    ContinuousNoiseSettings? ContinuousNoise = null)
{
    [System.Text.Json.Serialization.JsonIgnore]
    public bool IsAdaptive => AdaptiveTrack is not null;
}

public static class PairedMeasurementSessionFactory
{
    public static PairedMeasurementSession CreateRandomized(
        TestedEar ear,
        HearingAidSnapshot hearingAid,
        SpeechMaterial material,
        ListeningEnvironment environment,
        string firstPresentedListId,
        string secondPresentedListId,
        MeasurementHardwareSnapshot hardware,
        DateTimeOffset startedAt,
        int randomizationSeed,
        StimulusMaterialIdentity materialIdentity,
        MeasurementSessionContract contract,
        AdaptiveTrackSettings? adaptiveTrack = null,
        ContinuousNoiseSettings? continuousNoise = null)
    {
        var seed = randomizationSeed;
        var firstCondition = GetFirstCondition(seed);
        var secondCondition = firstCondition == HearingAidCondition.WithoutHearingAid
            ? HearingAidCondition.WithHearingAid
            : HearingAidCondition.WithoutHearingAid;

        return new PairedMeasurementSession(
            MeasurementProtocol.CurrentVersion,
            Guid.NewGuid(),
            ear,
            hearingAid,
            material,
            environment,
            seed,
            hardware,
            startedAt,
            null,
            [
                new MeasurementBlock(Guid.NewGuid(), 1, firstCondition, firstPresentedListId, []),
                new MeasurementBlock(Guid.NewGuid(), 2, secondCondition, secondPresentedListId, [])
            ],
            materialIdentity,
            contract,
            AdaptiveTrack: adaptiveTrack,
            ContinuousNoise: continuousNoise);
    }

    public static HearingAidCondition GetFirstCondition(int randomizationSeed) =>
        (randomizationSeed & 1) == 0
            ? HearingAidCondition.WithoutHearingAid
            : HearingAidCondition.WithHearingAid;
}

public static class PairedMeasurementSessionRules
{
    public static IReadOnlyList<string> Validate(PairedMeasurementSession session)
    {
        var errors = new List<string>();

        if (session.ProtocolVersion != MeasurementProtocol.CurrentVersion)
            errors.Add($"Die Protokollversion {session.ProtocolVersion} wird nicht unterstützt.");
        if (session.Id == Guid.Empty)
            errors.Add("Die Messsitzung benötigt eine ID.");
        if (!Enum.IsDefined(typeof(TestedEar), session.Ear))
            errors.Add("Das geprüfte Ohr ist ungültig.");
        if (!Enum.IsDefined(typeof(SpeechMaterial), session.Material))
            errors.Add("Das Sprachmaterial ist ungültig.");
        if (!Enum.IsDefined(typeof(ListeningEnvironment), session.Environment))
            errors.Add("Die Hörumgebung ist ungültig.");
        if (session.StartedAt == default)
            errors.Add("Der Startzeitpunkt der Messsitzung fehlt.");
        if (session.CompletedAt is { } completedAt && completedAt < session.StartedAt)
            errors.Add("Der Abschlusszeitpunkt darf nicht vor dem Start liegen.");
        if (session.AbortedAt is { } abortedAt && abortedAt < session.StartedAt)
            errors.Add("Der Abbruchzeitpunkt darf nicht vor dem Start liegen.");
        if (session.AbortedAt is not null && session.CompletedAt != session.AbortedAt)
            errors.Add("Eine abgebrochene Messsitzung benötigt denselben Abbruch- und Abschlusszeitpunkt.");

        ValidateHearingAid(session, errors);
        ValidateHardware(session.Hardware, errors);
        ValidateMaterialIdentity(session.MaterialIdentity, errors);
        ValidateContract(session.Contract, errors);
        ValidateAdaptiveTrack(session, errors);
        if (session.ContinuousNoise is { } continuousNoise)
        {
            if (session.Environment != ListeningEnvironment.BackgroundNoise)
                errors.Add("Dauerrauschen ist nur im Störgeräuschtest zulässig.");
            if (continuousNoise.Version != ContinuousNoiseProtocol.CurrentVersion)
                errors.Add($"Die Version {continuousNoise.Version} des Dauerrauschens wird nicht unterstützt.");
        }
        ValidateBlocks(session, errors);

        return errors;
    }

    private static void ValidateHearingAid(PairedMeasurementSession session, ICollection<string> errors)
    {
        if (session.HearingAid is null)
        {
            errors.Add("Der unveränderliche Hörgeräte-Snapshot fehlt.");
            return;
        }

        if (session.HearingAid.DeviceId == Guid.Empty)
            errors.Add("Der Hörgeräte-Snapshot benötigt eine Geräte-ID.");
        if (string.IsNullOrWhiteSpace(session.HearingAid.Manufacturer))
            errors.Add("Der Hörgerätehersteller fehlt.");
        if (string.IsNullOrWhiteSpace(session.HearingAid.Model))
            errors.Add("Das Hörgerätemodell fehlt.");
        if (string.IsNullOrWhiteSpace(session.HearingAid.DisplayName))
            errors.Add("Die Hörgerätebezeichnung fehlt.");
        if (session.HearingAid.FittedEar != session.Ear)
            errors.Add("Hörgerät und geprüftes Ohr müssen dieselbe Seite haben.");
        if (string.IsNullOrWhiteSpace(session.HearingAid.ProgramName) ||
            string.IsNullOrWhiteSpace(session.HearingAid.VolumeState))
            errors.Add("Der Hörgeräte-Snapshot benötigt Programm und Lautstärkezustand.");
    }

    private static void ValidateHardware(MeasurementHardwareSnapshot hardware, ICollection<string> errors)
    {
        if (hardware is null)
        {
            errors.Add("Der unveränderliche Hardware-Snapshot fehlt.");
            return;
        }

        if (hardware.ProfileId == Guid.Empty)
            errors.Add("Der Hardware-Snapshot benötigt eine Messprofil-ID.");
        if (string.IsNullOrWhiteSpace(hardware.EndpointId))
            errors.Add("Der Hardware-Snapshot benötigt den gespeicherten Audioausgang.");
        if (string.IsNullOrWhiteSpace(hardware.HeadphoneManufacturer) || string.IsNullOrWhiteSpace(hardware.HeadphoneModel))
            errors.Add("Der Hardware-Snapshot benötigt den verwendeten Kopfhörer.");
    }

    private static void ValidateContract(MeasurementSessionContract contract, ICollection<string> errors)
    {
        if (contract is null)
        {
            errors.Add("Der versionierte Messvertrag fehlt.");
            return;
        }
        if (contract.Version != 1 ||
            string.IsNullOrWhiteSpace(contract.ModuleId) ||
            string.IsNullOrWhiteSpace(contract.ResponseFormat) ||
            string.IsNullOrWhiteSpace(contract.ScoringRule) ||
            !Enum.IsDefined(contract.TrainingStatus))
            errors.Add("Der versionierte Messvertrag ist unvollständig oder wird nicht unterstützt.");
    }

    private static void ValidateAdaptiveTrack(PairedMeasurementSession session, ICollection<string> errors)
    {
        var expectsAdaptive = session.Contract?.ModuleId == MeasurementSessionContracts.AdaptiveCardinalNumberMeasurement.ModuleId;
        if (session.AdaptiveTrack is null)
        {
            if (expectsAdaptive)
                errors.Add("Der adaptive Messvertrag benötigt die gespeicherte Regel des adaptiven Verfahrens.");
            return;
        }
        if (!expectsAdaptive)
            errors.Add("Ein adaptives Verfahren ist nur mit dem adaptiven Messvertrag zulässig.");
        if (session.Material != SpeechMaterial.Numbers)
            errors.Add("Das adaptive Verfahren ist nur für den Zahlentest vorgesehen.");
        foreach (var error in AdaptiveTrackRules.Validate(session.AdaptiveTrack, session.Environment))
            errors.Add(error);
        if (session.AdaptiveTrack.Parameter == AdaptiveTrackParameter.SpeechLevel &&
            session.Hardware is { } hardware && session.AdaptiveTrack.MaximumValueDb > hardware.MaximumVolumeDb)
            errors.Add("Der adaptive Sprachpegel darf die Pegelobergrenze des Hardware-Snapshots nicht überschreiten.");
    }

    private static void ValidateMaterialIdentity(
        StimulusMaterialIdentity materialIdentity,
        ICollection<string> errors)
    {
        if (materialIdentity is null)
        {
            errors.Add("Die gespeicherte Materialidentität fehlt.");
            return;
        }

        if (string.IsNullOrWhiteSpace(materialIdentity.CatalogId) ||
            string.IsNullOrWhiteSpace(materialIdentity.CatalogVersion) ||
            !IsSha256(materialIdentity.CatalogSha256) ||
            !IsSha256(materialIdentity.AudioIndexSha256) ||
            !IsSha256(materialIdentity.FingerprintSha256))
        {
            errors.Add("Die gespeicherte Materialidentität ist unvollständig.");
            return;
        }

        var expected = StimulusMaterialIdentityFactory.Create(
            materialIdentity.CatalogId,
            materialIdentity.CatalogVersion,
            materialIdentity.CatalogSha256,
            materialIdentity.AudioIndexSha256);
        if (!string.Equals(expected.FingerprintSha256, materialIdentity.FingerprintSha256, StringComparison.OrdinalIgnoreCase))
            errors.Add("Der Fingerabdruck der gespeicherten Materialidentität ist ungültig.");
    }

    private static void ValidateBlocks(PairedMeasurementSession session, ICollection<string> errors)
    {
        if (session.Blocks is null || session.Blocks.Count != 2)
        {
            errors.Add("Eine gepaarte Messsitzung benötigt genau zwei Messblöcke.");
            return;
        }

        if (session.Blocks.Select(block => block.Id).Any(id => id == Guid.Empty) ||
            session.Blocks.Select(block => block.Id).Distinct().Count() != 2)
            errors.Add("Die beiden Messblöcke benötigen unterschiedliche IDs.");

        var orders = session.Blocks.Select(block => block.PresentationOrder).Order().ToArray();
        if (!orders.SequenceEqual(new[] { 1, 2 }))
            errors.Add("Die Messblöcke müssen die Reihenfolge 1 und 2 abbilden.");

        var conditions = session.Blocks.Select(block => block.Condition).ToArray();
        if (conditions.Count(condition => condition == HearingAidCondition.WithoutHearingAid) != 1 ||
            conditions.Count(condition => condition == HearingAidCondition.WithHearingAid) != 1)
            errors.Add("Die Paarung muss genau einen Block ohne und einen Block mit Hörgerät enthalten.");

        var firstBlock = session.Blocks.FirstOrDefault(block => block.PresentationOrder == 1);
        if (firstBlock is not null &&
            firstBlock.Condition != PairedMeasurementSessionFactory.GetFirstCondition(session.RandomizationSeed))
            errors.Add("Die gespeicherte Reihenfolge passt nicht zum Randomisierungs-Seed.");

        if (session.Blocks.Any(block => string.IsNullOrWhiteSpace(block.StimulusListId)))
            errors.Add("Jeder Messblock benötigt eine Listen-ID.");
        else if (session.Blocks.Select(block => block.StimulusListId).Distinct(StringComparer.OrdinalIgnoreCase).Count() != 2)
            errors.Add("Die gepaarten Messblöcke müssen unterschiedliche Listen verwenden.");

        foreach (var block in session.Blocks)
        {
            if (block.RawResponses.Count > 0 &&
                (block.SetupConfirmation is null ||
                 !block.SetupConfirmation.CorrectEarAndChannelConfirmed ||
                 !block.SetupConfirmation.HeadphoneFitAndFeedbackChecked ||
                 block.SetupConfirmation.ConfirmedAt == default))
                errors.Add($"Messblock {block.PresentationOrder} enthält Antworten ohne vollständige Aufbaubestätigung.");
            ValidateResponses(session, block, errors);
        }
    }

    private static void ValidateResponses(
        PairedMeasurementSession session,
        MeasurementBlock block,
        ICollection<string> errors)
    {
        if (block.RawResponses is null)
        {
            errors.Add($"Die Rohantworten für Messblock {block.PresentationOrder} fehlen.");
            return;
        }

        if (block.RawResponses.Any(response => response.PresentationOrder <= 0) ||
            block.RawResponses.Select(response => response.PresentationOrder).Distinct().Count() != block.RawResponses.Count)
            errors.Add($"Die Rohantworten in Messblock {block.PresentationOrder} benötigen eindeutige positive Reihenfolgenummern.");
        if (block.RawResponses.Any(response => string.IsNullOrWhiteSpace(response.StimulusId)))
            errors.Add($"Jede Rohantwort in Messblock {block.PresentationOrder} benötigt eine Stimulus-ID.");
        if (block.RawResponses.Any(response => response.RecordedAt == default))
            errors.Add($"Jede Rohantwort in Messblock {block.PresentationOrder} benötigt einen Erfassungszeitpunkt.");

        foreach (var response in block.RawResponses)
        {
            var presentation = response.Presentation;
            if (presentation is null)
            {
                errors.Add($"Der Wiedergabenachweis für Antwort {response.PresentationOrder} fehlt.");
                continue;
            }
            if (!string.Equals(response.StimulusId, presentation.StimulusId, StringComparison.Ordinal))
                errors.Add($"Der Wiedergabenachweis für Antwort {response.PresentationOrder} gehört zu einem anderen Stimulus.");
            if (string.IsNullOrWhiteSpace(presentation.CatalogId) ||
                string.IsNullOrWhiteSpace(presentation.CatalogVersion) ||
                !IsSha256(presentation.AudioSha256))
                errors.Add($"Der Wiedergabenachweis für Antwort {response.PresentationOrder} ist unvollständig.");
            if (!string.Equals(presentation.EndpointId, session.Hardware.EndpointId, StringComparison.Ordinal))
                errors.Add($"Der Wiedergabenachweis für Antwort {response.PresentationOrder} verwendet nicht den gespeicherten Audioausgang.");
            if (presentation.RenderMetadata is null ||
                presentation.RenderMetadata.OutputSampleRate != session.Hardware.SampleRate ||
                presentation.RenderMetadata.DigitalAttenuationDb > session.Hardware.MaximumVolumeDb)
                errors.Add($"Der Rendernachweis für Antwort {response.PresentationOrder} widerspricht dem Hardware-Snapshot.");
            if (session.Material == SpeechMaterial.Numbers &&
                session.Environment == ListeningEnvironment.BackgroundNoise &&
                presentation.RenderMetadata is { } renderMetadata &&
                (session.MaterialIdentity is null ||
                 renderMetadata.NoiseAlgorithm != StimulusAudioRenderer.CardinalNoiseAlgorithm ||
                 !string.Equals(renderMetadata.NoiseProfileMaterialId, session.MaterialIdentity.CatalogId, StringComparison.Ordinal) ||
                 !CardinalSpeechShapedNoiseProtocol.ApprovedProfileSha256ByMaterialId.TryGetValue(
                     session.MaterialIdentity.CatalogId,
                     out var approvedProfileSha256) ||
                 !string.Equals(renderMetadata.NoiseProfileSha256, approvedProfileSha256, StringComparison.Ordinal)))
                errors.Add($"Der Rendernachweis für Antwort {response.PresentationOrder} enthält nicht das materialgebundene Kardinalzahl-Rauschprofil.");
            if (presentation.RenderMetadata is { } noiseMetadata &&
                noiseMetadata.ContinuousNoiseLevelDbfs != session.ContinuousNoise?.NoiseLevelDbfs)
                errors.Add($"Der Rauschpegel für Antwort {response.PresentationOrder} weicht vom Dauerrauschen der Sitzung ab.");
            if (presentation.StartedAt == default || presentation.CompletedAt < presentation.StartedAt)
                errors.Add($"Der Wiedergabezeitraum für Antwort {response.PresentationOrder} ist ungültig.");
        }
    }

    private static bool IsSha256(string value) =>
        value is { Length: 64 } && value.All(Uri.IsHexDigit);
}

public sealed record PlannedStimulus(int PresentationOrder, string StimulusId);

public sealed record PlannedMeasurementBlock(
    Guid BlockId,
    int PresentationOrder,
    HearingAidCondition Condition,
    string StimulusListId,
    IReadOnlyList<PlannedStimulus> Stimuli);

public sealed record MeasurementRunPlan(IReadOnlyList<PlannedMeasurementBlock> Blocks);

public static class MeasurementRunPlanner
{
    public static (string FirstListId, string SecondListId) SelectListIds(
        StimulusCatalog catalog,
        SpeechMaterial material,
        int randomizationSeed)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        var listIds = catalog.Lists
            .Where(list => list.Material == material)
            .Select(list => list.Id)
            .Order(StringComparer.Ordinal)
            .ToArray();
        if (listIds.Length < 2)
            throw new InvalidOperationException("Für die gepaarte Messung werden mindestens zwei getrennte Stimuluslisten benötigt.");

        StableShuffle(listIds, unchecked((uint)randomizationSeed ^ 0xA511E9B3u));
        return (listIds[0], listIds[1]);
    }

    public static MeasurementRunPlan Create(PairedMeasurementSession session, StimulusCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(catalog);
        var sessionErrors = PairedMeasurementSessionRules.Validate(session);
        if (sessionErrors.Count > 0)
            throw new ArgumentException(string.Join(" ", sessionErrors), nameof(session));

        var listsById = catalog.Lists.ToDictionary(list => list.Id, StringComparer.Ordinal);
        var blocks = new List<PlannedMeasurementBlock>(session.Blocks.Count);
        foreach (var block in session.Blocks.OrderBy(block => block.PresentationOrder))
        {
            if (!listsById.TryGetValue(block.StimulusListId, out var list))
                throw new InvalidOperationException($"Stimulusliste '{block.StimulusListId}' ist im aktiven Katalog nicht vorhanden.");
            if (list.Material != session.Material)
                throw new InvalidOperationException($"Stimulusliste '{block.StimulusListId}' gehört nicht zum gewählten Sprachmaterial.");

            var stimulusIds = list.Items.Select(item => item.Id).ToArray();
            var shuffleSeed = unchecked((uint)session.RandomizationSeed ^
                ((uint)block.PresentationOrder * 0x9E3779B9u));
            StableShuffle(stimulusIds, shuffleSeed);
            blocks.Add(new PlannedMeasurementBlock(
                block.Id,
                block.PresentationOrder,
                block.Condition,
                block.StimulusListId,
                stimulusIds.Select((id, index) => new PlannedStimulus(index + 1, id)).ToArray()));
        }
        return new MeasurementRunPlan(blocks);
    }

    private static void StableShuffle<T>(T[] values, uint seed)
    {
        var random = new StableRandom(seed);
        for (var index = values.Length - 1; index > 0; index--)
        {
            var selected = (int)(random.NextUInt32() % (uint)(index + 1));
            (values[index], values[selected]) = (values[selected], values[index]);
        }
    }

    private sealed class StableRandom(uint seed)
    {
        private uint state = seed == 0 ? 0x6D2B79F5u : seed;

        public uint NextUInt32()
        {
            state ^= state << 13;
            state ^= state >> 17;
            state ^= state << 5;
            return state;
        }
    }
}

public sealed record ScoredMeasurementResponse(
    int PresentationOrder,
    string StimulusId,
    string CanonicalResponse,
    string? EnteredText,
    bool IsCorrect,
    string? ContrastGroupId,
    string? ContrastPosition,
    string? CueCategory);

public sealed record MeasurementBlockResult(
    HearingAidCondition Condition,
    string StimulusListId,
    int CorrectResponses,
    int TotalResponses,
    decimal PercentCorrect,
    IReadOnlyList<ScoredMeasurementResponse> Responses,
    AdaptiveTrackResult? Adaptive = null);

public sealed record PairedMeasurementResult(
    MeasurementBlockResult WithoutHearingAid,
    MeasurementBlockResult WithHearingAid,
    decimal DifferencePercentagePoints,
    decimal? ChanceLevelPercent,
    AdaptiveTrackParameter? AdaptiveParameter = null)
{
    /// <summary>Schwelle ohne minus Schwelle mit Hörgerät; positiv heißt, mit Hörgerät genügt ein leiserer Pegel bzw. ungünstigerer SNR.</summary>
    public decimal? ThresholdImprovementDb =>
        WithoutHearingAid.Adaptive?.ThresholdDb is { } without && WithHearingAid.Adaptive?.ThresholdDb is { } with
            ? without - with
            : null;
}

public static class MeasurementScoring
{
    public static PairedMeasurementResult Score(PairedMeasurementSession session, StimulusCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(catalog);
        if (session.AbortedAt is not null)
            throw new InvalidOperationException("Eine abgebrochene Messsitzung kann nur als Teilergebnis bewertet werden.");
        if (session.CompletedAt is null)
            throw new InvalidOperationException("Eine noch nicht abgeschlossene Messsitzung kann nicht bewertet werden.");

        return ScoreCore(session, catalog, requireCompleteBlocks: true);
    }

    public static PairedMeasurementResult ScorePartial(
        PairedMeasurementSession session,
        StimulusCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(catalog);
        if (session.AbortedAt is null)
            throw new InvalidOperationException("Nur eine als abgebrochen markierte Messsitzung kann als Teilergebnis bewertet werden.");

        return ScoreCore(session, catalog, requireCompleteBlocks: false);
    }

    private static PairedMeasurementResult ScoreCore(
        PairedMeasurementSession session,
        StimulusCatalog catalog,
        bool requireCompleteBlocks)
    {

        var plan = MeasurementRunPlanner.Create(session, catalog);
        var stimuliById = catalog.Lists
            .SelectMany(list => list.Items)
            .ToDictionary(item => item.Id, StringComparer.Ordinal);
        var results = new List<MeasurementBlockResult>(2);

        foreach (var plannedBlock in plan.Blocks)
        {
            var block = session.Blocks.Single(value => value.Id == plannedBlock.BlockId);
            if (requireCompleteBlocks && block.RawResponses.Count != plannedBlock.Stimuli.Count)
                throw new InvalidOperationException(
                    $"Messblock {block.PresentationOrder} enthält {block.RawResponses.Count} statt {plannedBlock.Stimuli.Count} Antworten.");

            var plannedByOrder = plannedBlock.Stimuli.ToDictionary(stimulus => stimulus.PresentationOrder);
            var scored = new List<ScoredMeasurementResponse>(block.RawResponses.Count);
            foreach (var response in block.RawResponses.OrderBy(value => value.PresentationOrder))
            {
                if (!plannedByOrder.TryGetValue(response.PresentationOrder, out var planned) ||
                    !string.Equals(response.StimulusId, planned.StimulusId, StringComparison.Ordinal))
                    throw new InvalidOperationException(
                        $"Die gespeicherte Antwortfolge in Messblock {block.PresentationOrder} passt nicht zum randomisierten Laufplan.");

                var stimulus = stimuliById[planned.StimulusId];
                scored.Add(new ScoredMeasurementResponse(
                    response.PresentationOrder,
                    response.StimulusId,
                    stimulus.CanonicalResponse,
                    response.EnteredText,
                    AreEquivalent(response.EnteredText, stimulus.CanonicalResponse),
                    stimulus.ContrastGroupId,
                    stimulus.ContrastPosition,
                    stimulus.CueCategory));
            }

            var correct = scored.Count(response => response.IsCorrect);
            results.Add(new MeasurementBlockResult(
                block.Condition,
                block.StimulusListId,
                correct,
                scored.Count,
                Percent(correct, scored.Count),
                scored,
                session.AdaptiveTrack is { } track ? EvaluateAdaptiveBlock(session, track, block, scored) : null));
        }

        var without = results.Single(result => result.Condition == HearingAidCondition.WithoutHearingAid);
        var with = results.Single(result => result.Condition == HearingAidCondition.WithHearingAid);
        return new PairedMeasurementResult(
            without,
            with,
            with.PercentCorrect - without.PercentCorrect,
            catalog.ChanceLevelPercent,
            session.AdaptiveTrack?.Parameter);
    }

    /// <summary>Verlauf eines adaptiven Blocks aus gespeicherten Werten und Antworten.</summary>
    public static IReadOnlyList<AdaptiveTrackTrial> CreateAdaptiveTrials(
        AdaptiveTrackSettings track,
        MeasurementBlock block,
        Func<string, string> canonicalResponseById) =>
        block.RawResponses.OrderBy(response => response.PresentationOrder)
            .Select(response => new AdaptiveTrackTrial(
                AdaptiveTrackRules.GetPresentedValue(track, response.Presentation.RenderMetadata)
                    ?? throw new InvalidOperationException($"Für Antwort {response.PresentationOrder} fehlt der adaptiv dargebotene Wert."),
                IsCorrectResponse(response.EnteredText, canonicalResponseById(response.StimulusId))))
            .ToArray();

    private static AdaptiveTrackResult EvaluateAdaptiveBlock(
        PairedMeasurementSession session,
        AdaptiveTrackSettings track,
        MeasurementBlock block,
        IReadOnlyList<ScoredMeasurementResponse> scored)
    {
        var canonicalById = scored.ToDictionary(response => response.StimulusId, response => response.CanonicalResponse, StringComparer.Ordinal);
        var trials = CreateAdaptiveTrials(track, block, id => canonicalById[id]);
        if (!AdaptiveTrackRules.IsConsistent(track, trials))
            throw new InvalidOperationException(
                $"Die gespeicherten Werte in Messblock {block.PresentationOrder} folgen nicht der adaptiven Regel der Sitzung {session.Id}.");
        return AdaptiveTrackRules.Evaluate(track, trials);
    }

    public static bool IsCorrectResponse(string? enteredText, string canonicalResponse) => AreEquivalent(enteredText, canonicalResponse);

    private static bool AreEquivalent(string? enteredText, string canonicalResponse) =>
        string.Equals(enteredText?.Trim(), canonicalResponse.Trim(), StringComparison.OrdinalIgnoreCase);

    private static decimal Percent(int correct, int total) =>
        total == 0 ? 0 : Math.Round(correct * 100m / total, 1, MidpointRounding.AwayFromZero);
}
