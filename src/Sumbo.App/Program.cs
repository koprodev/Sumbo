using System;
using System.IO;
using System.Threading;
using System.Windows.Forms;
using Sumbo.Core;
using Sumbo.Native;

namespace Sumbo.App;

internal static class Program
{
    // Single-instance coordination (per user session — matches the global-hotkey scope). The mutex guards
    // the "one process" invariant; the registered message tells the running instance to surface itself.
    private const string InstanceMutexName = "Local\\Sumbo.SingleInstance.6f1d2a34";
    internal const string ShowInstanceMessage = "Sumbo.ShowExistingInstance.6f1d2a34";

    [STAThread]
    private static void Main()
    {
        // One process only: a second launch signals the running instance to surface (it may be tray-resident)
        // and exits — this also prevents the second instance's global-hotkey registration from colliding with
        // the first (the "another app took Ctrl+Alt+…" notice was really a second Sumbo).
        var mutex = new Mutex(initiallyOwned: true, InstanceMutexName, out bool createdNew);
        if (!createdNew)
        {
            // This freshly launched process holds the foreground right (the user just started it) — hand it
            // over so the running instance's Activate() actually brings its window to the front instead of
            // being foreground-locked into a taskbar flash.
            User32.AllowSetForegroundWindow(User32.ASFW_ANY);
            User32.PostMessage(User32.HWND_BROADCAST, User32.RegisterWindowMessage(ShowInstanceMessage),
                IntPtr.Zero, IntPtr.Zero);
            return;
        }

        try
        {
            RunApp();
        }
        finally
        {
            GC.KeepAlive(mutex); // hold the mutex for the whole app lifetime
            mutex.Dispose();
        }
    }

    private static void RunApp()
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
