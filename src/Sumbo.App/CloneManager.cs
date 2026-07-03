using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Versioning;
using System.Security;
using System.Windows.Forms;
using Sumbo.Core;

namespace Sumbo.App;

/// <summary>
/// The app-level service hub for the v2 single-window model (체크리스트v2.md 확정 아키텍처): owns the process-wide
/// <see cref="HotkeyHost"/>, the shared region/profile stores, the persisted <see cref="Settings"/> (FR-14, §7.1)
/// and the <see cref="LocalizationCatalog"/> (FR-16). <see cref="MainWindow"/> hosts the one embedded mirror
/// (<see cref="MirrorSurface"/>) and owns every hotkey's meaning — the manager just raises
/// <see cref="HotkeyRouted"/>/<see cref="SurfaceRequested"/> so tray/hotkey intent reaches the window without the
/// manager knowing any UI. The v1 clone-window fleet (FR-12 다중복제 — CloneForm) was removed in V2-E2; FR-12 재개
/// 시 이 허브에 세션 관리가 다시 얹힌다 (Q1 확정 — 단일 미러 전용, 복제 보류).
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class CloneManager : IDisposable
{
    private readonly RegionStore _regionStore;
    private readonly ProfileService _profiles;
    private readonly SettingsService _settingsService;
    private readonly LocalizationCatalog _localization;
    private readonly AutoStartRegistrar _autoStart;
    private readonly HotkeyHost _hotkeyHost;
    private Settings _settings;

    /// <summary>
    /// Raised when the user asks to quit the whole application (tray "종료"). The host closes the main window and
    /// exits the message loop (F1 보완 — 종료 의미의 단일 funnel).
    /// </summary>
    public event EventHandler? ExitRequested;

    /// <summary>
    /// Raised for every global hotkey press. <see cref="MainWindow"/> — which hosts the embedded mirror — owns the
    /// action's meaning (V2 단일 창: per-clone dispatch was removed with <c>CloneForm</c> in V2-E2).
    /// </summary>
    public event EventHandler<HotkeyAction>? HotkeyRouted;

    /// <summary>
    /// Raised for tray surface actions (표시/숨김 · 더블클릭 복원 · 설정). The manager stays UI-agnostic:
    /// <see cref="TrayHost"/> calls <see cref="RequestSurface"/>, <see cref="MainWindow"/> subscribes ([5차] F1).
    /// </summary>
    public event EventHandler<SurfaceRequest>? SurfaceRequested;

    /// <summary>Routes a tray surface action to the main window ([5차] F1).</summary>
    public void RequestSurface(SurfaceRequest request) => SurfaceRequested?.Invoke(this, request);

    /// <summary>
    /// Raised when the main window first retires to the tray on close (V2-E3 트레이 상주, Q1 채택) so the tray
    /// icon can show the one-time residency balloon. Mirror of <see cref="SurfaceRequested"/> in the opposite
    /// direction — the window decides <em>when</em>, <see cref="TrayHost"/> owns the <c>NotifyIcon</c> that shows it.
    /// </summary>
    public event EventHandler? TrayResidencyNoticeRequested;

    /// <summary>Asks the tray icon to show the one-time tray-residency balloon (V2-E3).</summary>
    public void RequestTrayResidencyNotice() => TrayResidencyNoticeRequested?.Invoke(this, EventArgs.Empty);

    /// <summary>
    /// The <see cref="SettingsService"/>, loaded <see cref="Settings"/> and <see cref="LocalizationCatalog"/> are
    /// injected (built once in <c>Program.Main</c> so the startup DWM dialog can already be localized), along with
    /// the <c>%AppData%\Sumbo</c> data directory used for the region/profile stores.
    /// </summary>
    public CloneManager(SettingsService settingsService, Settings settings, LocalizationCatalog localization, string appDataDir)
    {
        _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
        _localization = localization ?? throw new ArgumentNullException(nameof(localization));
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentException.ThrowIfNullOrEmpty(appDataDir);

        _regionStore = new RegionStore(Path.Combine(appDataDir, "regions.json"));
        _profiles = new ProfileService(Path.Combine(appDataDir, "profiles.json"));

        // FR-16 F1: pin _settings.Language to the catalog's normalized value, so an unsupported persisted value
        // ("ja") can't linger here (and get re-saved) while the UI has already fallen back to the default.
        _settings = settings with { Language = localization.Language };
        _autoStart = new AutoStartRegistrar();
        _autoStart.Reconcile(_settings.StartWithWindows); // self-heal the Run entry against the persisted setting (FR-14)

        _hotkeyHost = new HotkeyHost();
        _hotkeyHost.HotkeyPressed += OnHotkeyPressed;
    }

    /// <summary>Bindings that failed to register globally (chord conflict) — shown to the user once.</summary>
    public IReadOnlyList<HotkeyBinding> HotkeyFailures => _hotkeyHost.Failures;

    /// <summary>True when the click-through escape hotkey (Ctrl+Alt+C) is live, gating click-through.</summary>
    public bool IsClickThroughHotkeyLive => _hotkeyHost.IsRegistered(HotkeyAction.ClickThrough);

    /// <summary>Current global settings (FR-14, §7.1). The tray/settings-panel toggles mutate this via the setters below.</summary>
    public Settings Current => _settings;

    /// <summary>The shared UI string catalog (FR-16). The tray and settings panel read + subscribe to it.</summary>
    public LocalizationCatalog Localization => _localization;

    /// <summary>Shared saved-region store (V2-C — the region panel edits it through the main-window shell).</summary>
    internal RegionStore Regions => _regionStore;

    /// <summary>Shared profile store (V2-C — the profiles panel edits it through the main-window shell).</summary>
    internal ProfileService Profiles => _profiles;

    /// <summary>True when minimizing should hide to the tray instead of the taskbar (§7.1 — <see cref="MainWindow"/>
    /// 의 최소화 전이 분기가 소비, V2-E3 트레이 상주).</summary>
    public bool MinimizeToTray => _settings.MinimizeToTray;

    /// <summary>
    /// Toggles launch-at-startup (FR-14). The Registry write happens first; the setting is persisted only on
    /// success, so a policy-blocked Registry leaves settings (and the tray checkbox) unchanged (PEER F2).
    /// Returns true when the change took effect.
    /// </summary>
    public bool SetStartWithWindows(bool on)
    {
        try
        {
            _autoStart.Set(on);
        }
        catch (Exception ex) when (ex is InvalidOperationException or UnauthorizedAccessException or SecurityException or IOException)
        {
            MessageBox.Show(
                _localization.Format(LocKeys.Dialog_AutoStartFailed_Body, ex.Message),
                _localization.Get(LocKeys.Dialog_AutoStartFailed_Caption),
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            return false;
        }

        _settings = _settings with { StartWithWindows = on };
        _settingsService.Save(_settings);
        return true;
    }

    /// <summary>Toggles the minimize-to-tray preference (FR-14, §7.1) and persists it.</summary>
    public void SetMinimizeToTray(bool on)
    {
        _settings = _settings with { MinimizeToTray = on };
        _settingsService.Save(_settings);
    }

    /// <summary>
    /// Switches the UI language (FR-16 런타임 전환) and persists it (SSOT). Long-lived surfaces (tray, main window
    /// → panel fan-out) re-label themselves off <see cref="LocalizationCatalog.LanguageChanged"/>.
    /// </summary>
    public void SetLanguage(string language)
    {
        string next = LocalizationCatalog.Normalize(language);
        if (next == _settings.Language)
            return;

        _settings = _settings with { Language = next };
        _settingsService.Save(_settings);
        _localization.SetLanguage(next); // raises LanguageChanged → tray + main window re-label themselves
    }

    /// <summary>
    /// Requests full application exit (§6.3 "종료"). Raises <see cref="ExitRequested"/> so the host can close the
    /// main window and leave the message loop — the window, as the app's primary surface, owns the app lifetime.
    /// </summary>
    public void RequestExit() => ExitRequested?.Invoke(this, EventArgs.Empty);

    private void OnHotkeyPressed(object? sender, HotkeyAction action)
        // V2 단일 창 모델: every hotkey belongs to the main window's embedded mirror (per-clone dispatch removed in V2-E2).
        => HotkeyRouted?.Invoke(this, action);

    public void Dispose()
    {
        _hotkeyHost.HotkeyPressed -= OnHotkeyPressed;
        _hotkeyHost.Dispose();
    }
}

/// <summary>Main-window surface actions routed from the tray to the single window ([5차] F1):
/// 표시/숨김 toggles minimize/restore (same meaning as the ToggleVisible hotkey), 더블클릭 restores only,
/// OpenSettings restores + switches to the absorbed settings panel (V2-E1 — replaces the v1 settings window).</summary>
public enum SurfaceRequest
{
    ToggleVisible,
    Restore,
    OpenSettings,
}
