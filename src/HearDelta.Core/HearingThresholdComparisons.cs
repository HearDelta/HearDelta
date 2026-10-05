namespace HearDelta.Core;

/// <summary>Ergebnis eines Tests an einer Frequenz innerhalb eines Hörschwellenvergleichs.</summary>
public sealed record HearingThresholdComparisonValue(bool Tested, bool Heard, decimal? ThresholdAttenuationDbfs);

/// <summary>
/// Eine Frequenz im Vergleich zweier Hörschwellentests. <see cref="ImprovementDb"/> ist positiv, wenn der
/// zweite Test einen leiseren Ton gehört hat (erste minus zweite Schwelle); nur gesetzt, wenn beide gehört wurden.
/// </summary>
public sealed record HearingThresholdComparisonRow(
    double FrequencyHz,
    HearingThresholdComparisonValue First,
    HearingThresholdComparisonValue Second,
    decimal? ImprovementDb);

public static class HearingThresholdComparisonRules
{
    /// <summary>
    /// Nennt Unterschiede im Messaufbau, die einen direkten Vergleich einschränken. Das Ohr ist eine bewusst gewählte
    /// Vergleichsachse und wird nicht als Abweichung gemeldet. Mit Hörgerät gemessene Altbestände (v9–v11) werden
    /// gekennzeichnet, weil Hörgeräte Sinustöne unterdrücken.
    /// </summary>
    public static IReadOnlyList<string> GetDifferences(IReadOnlyList<HearingThresholdSession> sessions)
    {
        ArgumentNullException.ThrowIfNull(sessions);
        if (sessions.Count < 2)
            return [];

        var reference = sessions[0];
        var differences = new List<string>();
        if (sessions.Any(session => session.IsLegacyWithHearingAid))
            differences.Add("Mindestens ein älterer Test wurde mit Hörgerät gemessen; Hörgeräte unterdrücken Sinustöne.");
        foreach (var other in sessions.Skip(1))
        {
            differences.AddRange(MeasurementProfileRules.GetComparisonDifferences(reference.Hardware, other.Hardware));
            if (!string.Equals(reference.ToneCatalogVersion, other.ToneCatalogVersion, StringComparison.Ordinal))
                differences.Add("Der Tonkatalog ist unterschiedlich.");
            if (reference.MaximumAttenuationDbfs != other.MaximumAttenuationDbfs)
                differences.Add("Die Pegelobergrenze ist unterschiedlich.");
            if (reference.StartAttenuationDbfs != other.StartAttenuationDbfs)
                differences.Add("Der Startpegel ist unterschiedlich.");
            if (reference.SignalPattern != other.SignalPattern || reference.LevelStepDb != other.LevelStepDb)
                differences.Add("Tonsignal oder Pegelschritt sind unterschiedlich (Protokollversion).");
            if (reference.Masking?.LevelDbfs != other.Masking?.LevelDbfs ||
                reference.Masking?.Algorithm != other.Masking?.Algorithm)
                differences.Add("Die Vertäubung des Gegenohrs ist unterschiedlich.");
            if (reference.ProtocolVersion != other.ProtocolVersion)
                differences.Add("Die Protokollversion und damit das Messverfahren sind unterschiedlich.");
        }
        return differences.Distinct(StringComparer.Ordinal).ToArray();
    }

    /// <summary>Stellt zwei Tests Frequenz für Frequenz gegenüber, aufsteigend nach Frequenz.</summary>
    public static IReadOnlyList<HearingThresholdComparisonRow> CreateRows(HearingThresholdSession first, HearingThresholdSession second)
    {
        ArgumentNullException.ThrowIfNull(first);
        ArgumentNullException.ThrowIfNull(second);
        var firstValues = ValuesByFrequency(first);
        var secondValues = ValuesByFrequency(second);
        var notTested = new HearingThresholdComparisonValue(false, false, null);

        return firstValues.Keys.Union(secondValues.Keys)
            .Order()
            .Select(frequency =>
            {
                var a = firstValues.GetValueOrDefault(frequency, notTested);
                var b = secondValues.GetValueOrDefault(frequency, notTested);
                decimal? improvement = a.Heard && b.Heard && a.ThresholdAttenuationDbfs is { } left && b.ThresholdAttenuationDbfs is { } right
                    ? left - right
                    : null;
                return new HearingThresholdComparisonRow(frequency, a, b, improvement);
            })
            .ToArray();
    }

    private static Dictionary<double, HearingThresholdComparisonValue> ValuesByFrequency(HearingThresholdSession session)
    {
        var frequencies = session.Tones.ToDictionary(tone => tone.MidiNoteNumber, tone => tone.FrequencyHz);
        return session.Observations
            .Where(observation => frequencies.ContainsKey(observation.MidiNoteNumber))
            .GroupBy(observation => frequencies[observation.MidiNoteNumber])
            .ToDictionary(
                group => group.Key,
                group =>
                {
                    var latest = group.Last();
                    return new HearingThresholdComparisonValue(true, latest.Heard, latest.Heard ? latest.ThresholdAttenuationDbfs : null);
                });
    }
}
