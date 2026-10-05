using System.Globalization;
using System.Runtime.CompilerServices;

namespace HearDelta.App.Tests;

/// <summary>
/// Die Erwartungen der Tests sind deutsch formuliert; ohne feste Kultur hingen sie von der Windows-Sprache des
/// Testrechners ab.
/// </summary>
internal static class TestCulture
{
    [ModuleInitializer]
    internal static void UseGerman()
    {
        var german = CultureInfo.GetCultureInfo("de-DE");
        CultureInfo.DefaultThreadCurrentCulture = german;
        CultureInfo.DefaultThreadCurrentUICulture = german;
        CultureInfo.CurrentCulture = german;
        CultureInfo.CurrentUICulture = german;
    }
}
