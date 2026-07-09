using System;
using System.Linq;
using System.Runtime.Versioning;
using System.Windows.Forms;
using Sumbo.Core;

namespace Sumbo.App;

/// <summary>
/// Application lifetime. Owns the <see cref="CloneManager"/> service hub (and through it the shared hotkey host),
/// the <see cref="MainWindow"/> (the app's primary surface — it hosts the embedded mirror itself) and the
/// <see cref="TrayHost"/>. A user close GESTURE retires the window to the tray (the window cancels it), so the
/// message loop ends only on a real exit — tray Exit → <see cref="CloneManager.ExitRequested"/> →
/// <see cref="MainWindow.CloseForExit"/> → <c>FormClosed</c> → <see cref="ExitApp"/> (one funnel), a raw external
/// <c>WM_CLOSE</c> (e.g. taskkill), or an OS-initiated close (shutdown/logoff) — the window never cancels those.
/// The DWM-composition prerequisite is guarded earlier in <c>Program.Main</c>.
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

        // A user close hides the window to the tray (the window cancels the close itself), so FormClosed
        // only fires on a real exit: tray Exit → RequestExit → CloseForExit (bypasses the cancel) → FormClosed → ExitApp.
        _main = new MainWindow(_manager, localization);
        _main.FormClosed += (_, _) => ExitApp();
        _manager.ExitRequested += (_, _) => _main.CloseForExit(); // real quit — bypasses the close=tray policy
        _main.Show();

        _tray = new TrayHost(_manager);

        if (settings.CheckUpdateOnStart)
            _ = NotifyOnNewerReleaseAsync(localization);

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

    /// <summary>Startup update check (opt-out via settings): probes the latest GitHub release once after launch
    /// settles and offers the download page when it is strictly newer. A Yes/No dialog rather than a tray
    /// balloon — balloons render as toasts, which the system can suppress entirely (ToastEnabled=0).
    /// Best-effort — failures stay silent.</summary>
    private async System.Threading.Tasks.Task NotifyOnNewerReleaseAsync(LocalizationCatalog localization)
    {
        await System.Threading.Tasks.Task.Delay(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
        (string Tag, string Url)? latest = await UpdateChecker.FetchLatestAsync().ConfigureAwait(false);
        if (latest is null || !UpdateCheck.IsNewer(latest.Value.Tag, typeof(SumboAppContext).Assembly.GetName().Version))
            return;

        try
        {
            _main.BeginInvoke(() =>
            {
                if (_exiting)
                    return;
                DialogResult choice = MessageBox.Show(
                    localization.Format(LocKeys.Update_Dialog_Body, latest.Value.Tag),
                    localization.Get(LocKeys.Update_Dialog_Caption),
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Information);
                if (choice == DialogResult.Yes)
                {
                    try
                    {
                        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(latest.Value.Url) { UseShellExecute = true });
                    }
                    catch (Exception)
                    {
                        // no browser association / cancelled shell prompt — nothing actionable
                    }
                }
            });
        }
        catch (InvalidOperationException)
        {
            // window torn down between the check and the marshal — exiting anyway
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
            _tray.Dispose();     // remove the tray icon before tearing down the manager it delegates to
            _manager.Dispose();
        }
        base.Dispose(disposing);
    }
}
