using System.Globalization;
using System.Windows;
using HearDelta.App.Localization;

namespace HearDelta.App;

public partial class App : Application
{
    /// <summary>Windows-Anzeigesprache beim Start; Grundlage der automatischen Sprachwahl.</summary>
    internal static CultureInfo SystemUiCulture { get; } = CultureInfo.CurrentUICulture;

    internal static UiLanguageStore LanguageStore { get; } = new();

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        UiLanguage.Apply(UiLanguage.Resolve(LanguageStore.Load(), SystemUiCulture));
        MainWindow = new MainWindow();
        MainWindow.Show();
    }
}
