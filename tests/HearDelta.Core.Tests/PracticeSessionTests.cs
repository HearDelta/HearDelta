using HearDelta.Core;

namespace HearDelta.Core.Tests;

public sealed class PracticeSessionTests
{
    [Fact]
    public void SeparatePracticeSessionIsNotAPairedMeasurement()
    {
        var session = PracticeSessionFactory.Create(TestedEar.Left, SpeechMaterial.Monosyllables, "de-mono-a-v1",
            new MeasurementHardwareSnapshot(Guid.NewGuid(), "Profil", "endpoint", "Ausgang", true, 48000, 24, 2, "Kopfhörer", "Modell", "offen", 300, "Ausgang", "Low", -60m, -30m, false, DateTimeOffset.UtcNow),
            StimulusMaterialIdentityFactory.Create("practice", "1", new string('a', 64), new string('b', 64)), DateTimeOffset.UtcNow, 42);

        Assert.Empty(PracticeSessionRules.Validate(session));
        Assert.Equal("de-mono-a-v1", session.StimulusListId);
        Assert.Empty(session.Responses);
    }
}
