using System.Text.Json;
using System.Text.Json.Nodes;
using HearDelta.Core;

namespace HearDelta.App.Services;

/// <summary>
/// Kontrakt (2026-09-30): Gespeicherte Hörschwellentests des Protokolls v9 bleiben lesbar.
/// v9 speicherte statt <c>levelStepDb</c> und <c>signalPattern</c> die Felder <c>riseDbPerSecond</c>,
/// <c>toneDurationMilliseconds</c>, <c>pauseDurationMilliseconds</c> und <c>pulsesPerLevel</c> – in der Sitzung und
/// in jedem Wiedergabenachweis. Die Umwandlung geschieht nur im Speicher; die gespeicherte Nutzlast bleibt unverändert.
/// </summary>
internal static class HearingThresholdLegacyPayload
{
    private const int V9 = 9;

    public static string UpgradeV9(string payload, JsonSerializerOptions options)
    {
        if (JsonNode.Parse(payload) is not JsonObject root ||
            root["protocolVersion"]?.GetValue<int>() != V9 ||
            root.ContainsKey("signalPattern"))
            return payload;

        MapTiming(root, options);
        if (root["observations"] is JsonArray observations)
        {
            foreach (var observation in observations.OfType<JsonObject>())
            {
                if (observation["presentation"] is JsonObject presentation)
                    MapTiming(presentation, options);
            }
        }
        return root.ToJsonString();
    }

    private static void MapTiming(JsonObject node, JsonSerializerOptions options)
    {
        var riseDbPerSecond = node["riseDbPerSecond"]?.GetValue<decimal>() ?? 0m;
        var toneMilliseconds = node["toneDurationMilliseconds"]?.GetValue<int>() ?? 0;
        var pauseMilliseconds = node["pauseDurationMilliseconds"]?.GetValue<int>() ?? 0;
        var pulsesPerLevel = node["pulsesPerLevel"]?.GetValue<int>() ?? 0;

        // v9 berechnete den Pegelschritt aus Anstiegsrate × Tonlänge (4 dB/s × 750 ms = 3 dB).
        node["levelStepDb"] = riseDbPerSecond * toneMilliseconds / 1_000m;
        var pattern = toneMilliseconds == 750 && pauseMilliseconds == 750 && pulsesPerLevel == 2
            ? ThresholdSignalPattern.LegacyV9
            : new ThresholdSignalPattern(toneMilliseconds, 0, 1, 0, 1, pauseMilliseconds, pulsesPerLevel, 50);
        node["signalPattern"] = JsonSerializer.SerializeToNode(pattern, options);

        node.Remove("riseDbPerSecond");
        node.Remove("toneDurationMilliseconds");
        node.Remove("pauseDurationMilliseconds");
        node.Remove("pulsesPerLevel");
    }
}
