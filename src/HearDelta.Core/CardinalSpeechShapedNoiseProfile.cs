namespace HearDelta.Core;

public sealed record CardinalSpeechShapedNoiseProfile(
    string MaterialId,
    string CatalogSha256,
    string AudioIndexSha256,
    string ProfileSha256,
    IReadOnlyDictionary<int, IReadOnlyList<double>> FirCoefficientsBySampleRate);

public static class CardinalSpeechShapedNoiseProfileRules
{
    public static IReadOnlyList<string> Validate(
        CardinalSpeechShapedNoiseProfile profile,
        StimulusMaterialIdentity materialIdentity)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(materialIdentity);
        var errors = new List<string>();

        if (!string.Equals(profile.MaterialId, materialIdentity.CatalogId, StringComparison.Ordinal) ||
            !string.Equals(profile.CatalogSha256, materialIdentity.CatalogSha256, StringComparison.Ordinal) ||
            !string.Equals(profile.AudioIndexSha256, materialIdentity.AudioIndexSha256, StringComparison.Ordinal))
            errors.Add("Das Rauschprofil gehört nicht zur unveränderten Materialidentität des Stimuluspakets.");
        if (!CardinalSpeechShapedNoiseProtocol.SupportedMaterialIds.Contains(profile.MaterialId))
            errors.Add("Das Rauschprofil gehört nicht zu einem freigegebenen Kardinalzahlpaket.");
        if (!IsLowercaseSha256(profile.ProfileSha256))
            errors.Add("Das Rauschprofil benötigt einen kleingeschriebenen SHA-256-Wert.");
        else if (!CardinalSpeechShapedNoiseProtocol.ApprovedProfileSha256ByMaterialId.TryGetValue(
                     profile.MaterialId,
                     out var approvedProfileSha256) ||
                 !string.Equals(profile.ProfileSha256, approvedProfileSha256, StringComparison.Ordinal))
            errors.Add("Die SHA-256 des Rauschprofils stimmt nicht mit dem abgenommenen v1-Profil überein.");

        var filters = profile.FirCoefficientsBySampleRate;
        if (filters is null)
        {
            errors.Add("Das Rauschprofil enthält keine FIR-Filter.");
            return errors;
        }
        var rates = filters.Keys.Order().ToArray();
        if (!rates.SequenceEqual(CardinalSpeechShapedNoiseProtocol.SupportedOutputSampleRates))
            errors.Add("Das Rauschprofil muss FIR-Filter für 44,1 und 48 kHz enthalten.");
        foreach (var (sampleRate, coefficients) in filters)
        {
            if (coefficients is null || coefficients.Count != CardinalSpeechShapedNoiseProtocol.FirTapCount)
                errors.Add($"Der FIR-Filter für {sampleRate} Hz muss 4.097 Koeffizienten enthalten.");
            else if (coefficients.Any(value => !double.IsFinite(value)))
                errors.Add($"Der FIR-Filter für {sampleRate} Hz enthält ungültige Koeffizienten.");
        }

        return errors;
    }

    private static bool IsLowercaseSha256(string? value) =>
        value is { Length: 64 } &&
        value.All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f');
}
