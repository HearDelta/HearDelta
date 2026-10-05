namespace HearDelta.Core;

public enum MeasurementExposureScope
{
    Item,
    ContrastGroup,
    StimulusList
}

public sealed record MeasurementExposureCell(
    MeasurementExposureScope Scope,
    string Key,
    int PresentationCount);

public static class MeasurementExposureAnalysis
{
    public static IReadOnlyList<MeasurementExposureCell> Analyze(
        IEnumerable<PairedMeasurementSession> sessions,
        StimulusCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(sessions);
        ArgumentNullException.ThrowIfNull(catalog);

        var items = catalog.Lists
            .SelectMany(list => list.Items.Select(item => (ListId: list.Id, Item: item)))
            .ToDictionary(entry => entry.Item.Id, StringComparer.Ordinal);
        var presentations = sessions
            .Where(session => session.Material == SpeechMaterial.PhonemeContrasts)
            .SelectMany(session => session.Blocks)
            .SelectMany(block => block.RawResponses
                .Select(response => new { block.StimulusListId, response.StimulusId }))
            .Where(presentation => items.ContainsKey(presentation.StimulusId))
            .ToArray();

        var itemCells = presentations
            .GroupBy(presentation => presentation.StimulusId, StringComparer.Ordinal)
            .Select(group => new MeasurementExposureCell(MeasurementExposureScope.Item, group.Key, group.Count()));
        var groupCells = presentations
            .Select(presentation => items[presentation.StimulusId].Item.ContrastGroupId)
            .Where(groupId => !string.IsNullOrWhiteSpace(groupId))
            .GroupBy(groupId => groupId!, StringComparer.Ordinal)
            .Select(group => new MeasurementExposureCell(MeasurementExposureScope.ContrastGroup, group.Key, group.Count()));
        var listCells = presentations
            .GroupBy(presentation => presentation.StimulusListId, StringComparer.Ordinal)
            .Select(group => new MeasurementExposureCell(MeasurementExposureScope.StimulusList, group.Key, group.Count()));

        return itemCells.Concat(groupCells).Concat(listCells)
            .OrderBy(cell => cell.Scope)
            .ThenBy(cell => cell.Key, StringComparer.Ordinal)
            .ToArray();
    }
}
