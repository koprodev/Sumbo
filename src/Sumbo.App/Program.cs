using System;
using System.IO;
using System.Windows.Forms;
using Sumbo.Core;
using Sumbo.Native;

namespace Sumbo.App;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        // Applies HighDpiMode (PerMonitorV2) from the .csproj — source-generated.
        ApplicationConfiguration.Initialize();

        // Load settings + build the localization catalog first (FR-16), so even the startup DWM dialog is
        // localized. Settings are loaded once here and injected down (Program → context → manager) rather than
        // re-loaded, and the language JSON honours an optional %AppData%\Sumbo\lang override.
        string appDataDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Sumbo");
        var settingsService = new SettingsService(Path.Combine(appDataDir, "settings.json"));
        Settings settings = settingsService.Load();
        LocalizationCatalog localization = LocalizationCatalog.Load(
            settings.Language, Path.Combine(appDataDir, "lang"));

        // DWM composition is a hard prerequisite (§13). Guard at startup and exit rather than
        // launching an unusable window — the "실행 불가" notice must not be followed by the app
        // continuing to run (PEER 보완 — guard/표시 충돌 제거).
        if (!Dwm.IsCompositionEnabled())
        {
            MessageBox.Show(
                localization.Get(LocKeys.Dialog_DwmDisabled_Body),
                localization.Get(LocKeys.Dialog_DwmDisabled_Caption),
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
            return;
        }

        // FR-12 다중복제: a context (not a single form) owns the clone set + shared global hotkeys,
        // so the app outlives any individual clone window and exits when the last one closes.
        Application.Run(new SumboAppContext(settingsService, settings, localization, appDataDir));
    }
}
