using CommunityToolkit.Mvvm.ComponentModel;
using HearDelta.Core;

namespace HearDelta.App.ViewModels;

/// <summary>
/// Name und Kommentar einer neuen Messung im Einrichten-Schritt. Der Name folgt dem gewählten Hörgerät,
/// bis er von Hand geändert wird; ein geleertes Namensfeld folgt wieder der Vorgabe.
/// </summary>
public partial class MeasurementAnnotationDraft : ObservableObject
{
    private string defaultName;
    private bool followsDefault = true;
    private bool applyingDefault;

    public MeasurementAnnotationDraft(string initialDefaultName = "")
    {
        defaultName = initialDefaultName;
        name = initialDefaultName;
    }

    [ObservableProperty]
    private string name;

    [ObservableProperty]
    private string comment = string.Empty;

    public string DefaultName => defaultName;

    public void SetDefaultName(string value)
    {
        defaultName = value;
        OnPropertyChanged(nameof(DefaultName));
        if (!followsDefault)
            return;
        applyingDefault = true;
        Name = value;
        applyingDefault = false;
    }

    partial void OnNameChanged(string value)
    {
        if (!applyingDefault)
            followsDefault = string.IsNullOrWhiteSpace(value) || value.Trim() == defaultName;
    }

    /// <summary>Erzeugt die zu speichernde Annotation; ein leerer Name fällt auf die Vorgabe zurück.</summary>
    public MeasurementAnnotation CreateAnnotation(Guid measurementId, DateTimeOffset at) =>
        MeasurementAnnotationRules.Create(
            measurementId,
            string.IsNullOrWhiteSpace(Name) ? defaultName : Name,
            Comment,
            at);
}
