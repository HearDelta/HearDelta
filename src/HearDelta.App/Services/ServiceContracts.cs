using HearDelta.Core;

namespace HearDelta.App.Services;

public sealed record RepositoryReadError(string RecordId, string Message);

public interface IRepositoryReadDiagnostics
{
    IReadOnlyList<RepositoryReadError> LastReadErrors { get; }
}

public interface IAudioEndpointService
{
    IReadOnlyList<AudioEndpointDescriptor> GetActiveOutputs();

    Task PlayChannelTestAsync(
        string endpointId,
        bool exclusive,
        int channel,
        decimal digitalAttenuationDb,
        decimal maximumVolumeDb,
        CancellationToken cancellationToken = default);
}

public interface IStimulusPlaybackService
{
    Task<StimulusPlaybackReceipt> PlayAsync(
        LoadedStimulusPack pack,
        string stimulusId,
        MeasurementHardwareSnapshot hardware,
        StimulusRenderRequest renderRequest,
        CancellationToken cancellationToken = default);
}

/// <summary>Dauerrauschen eines Störgeräuschblocks mit eingemischten Sprachstimuli.</summary>
public interface IContinuousNoisePlaybackService
{
    SpeechLevelStatistics GetSpeechLevelStatistics(LoadedStimulusPack pack, int sampleRate);

    Task<IContinuousNoiseSession> StartAsync(
        LoadedStimulusPack pack,
        MeasurementHardwareSnapshot hardware,
        ContinuousNoiseSettings settings,
        TestedEar ear,
        int noiseSeed,
        CancellationToken cancellationToken = default);
}

/// <summary>Laufendes Dauerrauschen; Dispose blendet es aus und gibt den Endpunkt frei.</summary>
public interface IContinuousNoiseSession : IAsyncDisposable
{
    Task<StimulusPlaybackReceipt> PlayAsync(
        string stimulusId,
        decimal signalToNoiseRatioDb,
        CancellationToken cancellationToken = default);
}

public interface IThresholdTonePlaybackService
{
    Task<ThresholdTonePlaybackReceipt> PlayAsync(
        ThresholdTonePlaybackRequest request,
        MeasurementHardwareSnapshot hardware,
        CancellationToken stopSignal);

    Task PlayMaskingPreviewAsync(
        TestedEar maskedEar,
        decimal levelDbfs,
        MeasurementHardwareSnapshot hardware,
        CancellationToken stopSignal);
}

public interface IMeasurementSessionRepository
{
    IReadOnlyList<PairedMeasurementSession> LoadAll();
    IReadOnlyList<PairedMeasurementSession> LoadForPerson(Guid personId);

    PairedMeasurementSession? Load(Guid id);

    void Save(Guid personId, PairedMeasurementSession session);

    void Delete(Guid id);
}

public sealed record MeasurementSeriesState(
    MeasurementSeriesPlan Plan,
    int CurrentRoundIndex);

public interface IMeasurementSeriesRepository
{
    MeasurementSeriesState? LoadActive(Guid personId);

    void Save(Guid personId, MeasurementSeriesState state);

    void Complete(Guid id);

    void Delete(Guid id);
}

public interface IPracticeSessionRepository
{
    IReadOnlyList<PracticeSession> LoadAll();
    IReadOnlyList<PracticeSession> LoadForPerson(Guid personId);
    PracticeSession? Load(Guid id);
    void Save(Guid personId, PracticeSession session);
}

public interface IHearingThresholdSessionRepository
{
    IReadOnlyList<HearingThresholdSession> LoadAll();
    IReadOnlyList<HearingThresholdSession> LoadForPerson(Guid personId);

    HearingThresholdSession? Load(Guid id);

    void Save(Guid personId, HearingThresholdSession session);

    void Delete(Guid id);
}

/// <summary>Name und Kommentar je Messung, getrennt vom unveränderlichen Messprotokoll.</summary>
public interface IMeasurementAnnotationRepository
{
    IReadOnlyDictionary<Guid, MeasurementAnnotation> LoadAll();

    void Save(MeasurementAnnotation annotation);

    void Delete(Guid measurementId);
}

public sealed record MeasurementAnnotationInput(string Name, string Comment);

/// <summary>Lässt Name und Kommentar einer gespeicherten Messung bearbeiten; <c>null</c> heißt abgebrochen.</summary>
public interface IMeasurementAnnotationEditor
{
    MeasurementAnnotationInput? Edit(string title, MeasurementAnnotationInput current);
}

/// <summary>Druckt einen Bericht nach Auswahl des Druckers; <c>false</c> heißt abgebrochen.</summary>
public interface IReportPrinter
{
    bool Print(ViewModels.PrintReport report);
}

public interface IUserConfirmationService
{
    bool Confirm(string title, string message);
}

public enum UnsavedChangesDecision
{
    Save,
    Discard,
    Cancel
}

/// <summary>Fragt, was mit nicht gespeicherten Änderungen geschehen soll, bevor sie verloren gingen.</summary>
public interface IUnsavedChangesPrompt
{
    UnsavedChangesDecision Ask(string title, string message);
}
