using System;
using System.Linq;
using System.Runtime.Versioning;
using System.Windows.Forms;
using Sumbo.Core;

namespace Sumbo.App;

/// <summary>
/// Application lifetime for the v2 single-window app. Owns the <see cref="CloneManager"/> service hub (and through
/// it the shared hotkey host), the <see cref="MainWindow"/> (the app's primary surface — it hosts the embedded
/// mirror itself) and the <see cref="TrayHost"/>. Opens the window at startup. V2-E3 트레이 상주: a user close
/// GESTURE retires the window to the tray (the window cancels it), so the message loop ends only on a real exit —
/// tray "종료" → <see cref="CloneManager.ExitRequested"/> → <see cref="MainWindow.CloseForExit"/> → <c>FormClosed</c>
/// → <see cref="ExitApp"/> (one funnel, F1/F2 보완), a raw external <c>WM_CLOSE</c> (taskkill·스크립트), or an
/// OS-initiated close (shutdown/logoff) — the window never cancels those. The DWM-composition prerequisite is
/// guarded earlier in <c>Program.Main</c>.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class SumboAppContext : ApplicationContext
{
    private readonly CloneManager _manager;
    private readonly MainWindow _main;
    private readonly TrayHost _tray;
    private bool _exiting;

    public SumboAppContext(SettingsService settingsService, Settings settings, LocalizationCatalog localization, string appDataDir)
    {
        _manager = new CloneManager(settingsService, settings, localization, appDataDir);

        // The main window is the app's primary surface. A user close hides it to the tray (V2-E3 — the window
        // cancels the close itself), so FormClosed only fires on a real exit. That exit still has one path
        // (F2 보완): tray "종료" → RequestExit → CloseForExit (cancel 우회) → FormClosed → ExitApp.
        _main = new MainWindow(_manager, localization);
        _main.FormClosed += (_, _) => ExitApp();
        _manager.ExitRequested += (_, _) => _main.CloseForExit(); // real quit — bypasses the close=tray policy
        _main.Show();

        _tray = new TrayHost(_manager); // FR-14 system-tray presence + auto-start / minimize-to-tray toggles

        if (_manager.HotkeyFailures.Count > 0)
        {
            string keys = string.Join(", ", _manager.HotkeyFailures.Select(f => f.Display));
            MessageBox.Show(
                _main,
                localization.Format(LocKeys.Dialog_HotkeyConflict_Body, keys),
                localization.Get(LocKeys.Dialog_HotkeyConflict_Caption),
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }
    }

    /// <summary>Leaves the message loop. Idempotent (both the window close and an explicit quit route here).</summary>
    private void ExitApp()
    {
        if (_exiting)
            return;
        _exiting = true;
        ExitThread();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _tray.Dispose();     // remove the tray icon before tearing down the manager (PEER Dispose order)
            _manager.Dispose();
        }
        base.Dispose(disposing);
    }
}
