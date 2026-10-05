namespace HearDelta.Core;

public sealed record MeasurementSeriesRound(
    int PairNumber,
    int SessionSeed,
    HearingAidCondition FirstCondition,
    string FirstListId,
    string SecondListId);

public sealed record MeasurementSeriesPlan(
    int Version,
    Guid Id,
    int RandomizationSeed,
    SpeechMaterial Material,
    IReadOnlyList<MeasurementSeriesRound> Rounds);

public static class MeasurementSeriesPlanner
{
    public static MeasurementSeriesPlan Create(
        StimulusCatalog catalog,
        SpeechMaterial material,
        int pairCount,
        int randomizationSeed)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        if (pairCount <= 0)
            throw new ArgumentOutOfRangeException(nameof(pairCount));

        var listIds = catalog.Lists.Where(list => list.Material == material)
            .Select(list => list.Id).Order(StringComparer.Ordinal).ToArray();
        if (listIds.Length < 2)
            throw new InvalidOperationException(CoreStrings.Series_TwoLists);

        var rounds = Enumerable.Range(0, pairCount).Select(index =>
        {
            var firstCondition = index % 2 == 0
                ? HearingAidCondition.WithoutHearingAid
                : HearingAidCondition.WithHearingAid;
            var seed = unchecked((randomizationSeed * 1103515245) + (index * 12345));
            seed = (seed & ~1) | (firstCondition == HearingAidCondition.WithHearingAid ? 1 : 0);
            return new MeasurementSeriesRound(
                index + 1,
                seed,
                firstCondition,
                listIds[index % listIds.Length],
                listIds[(index + 1) % listIds.Length]);
        }).ToArray();
        return new MeasurementSeriesPlan(1, Guid.NewGuid(), randomizationSeed, material, rounds);
    }
}
