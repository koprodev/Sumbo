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

        // Load settings and build the localization catalog before anything else so even the startup DWM
        // dialog is localized. Both are created once here and injected downward; the language JSON honours
        // an optional %AppData%\Sumbo\lang override.
        string appDataDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Sumbo");
        var settingsService = new SettingsService(Path.Combine(appDataDir, "settings.json"));
        Settings settings = settingsService.Load();
        LocalizationCatalog localization = LocalizationCatalog.Load(
            settings.Language, Path.Combine(appDataDir, "lang"));

        // DWM composition is a hard startup prerequisite: guard here and exit instead of launching an
        // unusable window.
        if (!Dwm.IsCompositionEnabled())
        {
            MessageBox.Show(
                localization.Get(LocKeys.Dialog_DwmDisabled_Body),
                localization.Get(LocKeys.Dialog_DwmDisabled_Caption),
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
            return;
        }

        // An ApplicationContext (not a form) owns the app lifetime: the main window can retire to the
        // tray on close, so the message loop must end only on a real exit.
        Application.Run(new SumboAppContext(settingsService, settings, localization, appDataDir));
    }
}
