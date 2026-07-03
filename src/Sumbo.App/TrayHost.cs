using System;
using System.Drawing;
using System.Runtime.Versioning;
using System.Windows.Forms;
using Sumbo.App.Ui;
using Sumbo.Core;

namespace Sumbo.App;

/// <summary>
/// The system-tray presence (FR-14, §6.1 트레이 메뉴). Owns a single process-wide <see cref="NotifyIcon"/> so
/// the app stays reachable while the main window is hidden/minimized (V2-E3 트레이 상주 정책의 진입점). The icon
/// is present for the whole app lifetime — there is always a way back (double-click / 표시), so the hidden
/// window can never be stranded (PEER R3). Surface actions ride <see cref="CloneManager.RequestSurface"/>
/// (the manager stays UI-agnostic); exit and the setting toggles are delegated to <see cref="CloneManager"/>.
/// <para>
/// FR-16: menu captions + tooltip come from the shared <see cref="LocalizationCatalog"/>. As a long-lived
/// singleton the tray subscribes directly to <see cref="LocalizationCatalog.LanguageChanged"/> and re-labels
/// itself on a runtime switch (unsubscribing in <see cref="Dispose"/>).
/// </para>
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed class TrayHost : IDisposable
{
    private readonly CloneManager _manager;
    private readonly LocalizationCatalog _localization;
    private readonly NotifyIcon _icon;
    private readonly Icon _brandIcon;       // AppIcons.Load 소유권 — Dispose 에서 NotifyIcon 뒤에 해제 ([5차] 조건 3)
    private readonly ContextMenuStrip _menu;
    private readonly ToolStripMenuItem _toggleVisibleItem;
    private readonly ToolStripMenuItem _autoStartItem;
    private readonly ToolStripMenuItem _minimizeToTrayItem;
    private readonly ToolStripMenuItem _settingsItem;
    private readonly ToolStripMenuItem _exitItem;

    public TrayHost(CloneManager manager)
    {
        _manager = manager ?? throw new ArgumentNullException(nameof(manager));
        _localization = manager.Localization;

        _toggleVisibleItem = new ToolStripMenuItem(_localization.Get(LocKeys.Tray_ToggleVisible), null, (_, _) => ToggleVisible());
        _autoStartItem = new ToolStripMenuItem(_localization.Get(LocKeys.Tray_AutoStart), null, (_, _) => ToggleAutoStart());
        _minimizeToTrayItem = new ToolStripMenuItem(_localization.Get(LocKeys.Tray_MinimizeToTray), null, (_, _) => ToggleMinimizeToTray());
        _settingsItem = new ToolStripMenuItem(_localization.Get(LocKeys.Menu_Settings), null, (_, _) => _manager.RequestSurface(SurfaceRequest.OpenSettings));
        _exitItem = new ToolStripMenuItem(_localization.Get(LocKeys.Tray_Exit), null, (_, _) => _manager.RequestExit());

        _menu = new ContextMenuStrip();
        _menu.Items.Add(_toggleVisibleItem);
        _menu.Items.Add(new ToolStripSeparator());
        _menu.Items.Add(_autoStartItem);
        _menu.Items.Add(_minimizeToTrayItem);
        _menu.Items.Add(_settingsItem);
        _menu.Items.Add(new ToolStripSeparator());
        _menu.Items.Add(_exitItem);
        _menu.Opening += (_, _) => RefreshChecks();

        // 브랜드 아이콘 (배포 cycle, assets/sumbo.ico embedded) — 시스템 small icon 크기로 로드해 16px 프레임 선명도 확보.
        _brandIcon = AppIcons.Load(SystemInformation.SmallIconSize.Width);
        _icon = new NotifyIcon
        {
            Icon = _brandIcon,
            Text = _localization.Get(LocKeys.App_Title),
            Visible = true,
            ContextMenuStrip = _menu,
        };
        // V2 단일 창: "restore" means the main window ([5차] F1 — SurfaceRequest 단일 경로, V2-E2 분기 소멸).
        _icon.DoubleClick += (_, _) => _manager.RequestSurface(SurfaceRequest.Restore);

        _localization.LanguageChanged += OnLanguageChanged; // FR-16 runtime relabel (unsubscribed in Dispose)
        _manager.TrayResidencyNoticeRequested += OnTrayResidencyNotice; // V2-E3 close→트레이 1회 풍선 (Q1 채택)
    }

    // 표시/숨김 targets the main window, sharing the ToggleVisible hotkey's tray hide/restore meaning
    // (V2-E3 트레이 상주 — 숨김 = 트레이 잔류, 복원 = Visible+Normal).
    private void ToggleVisible() => _manager.RequestSurface(SurfaceRequest.ToggleVisible);

    // The window owns the once-per-session gate; this just renders the balloon on the always-alive icon.
    private void OnTrayResidencyNotice(object? sender, EventArgs e)
        => _icon.ShowBalloonTip(
            3000,
            _localization.Get(LocKeys.Tray_ResidentNotice_Title),
            _localization.Get(LocKeys.Tray_ResidentNotice_Body),
            ToolTipIcon.Info);

    private void ToggleAutoStart()
    {
        _manager.SetStartWithWindows(!_manager.Current.StartWithWindows);
        RefreshChecks(); // reflect the authoritative post-call state (reverts if the Registry write failed)
    }

    private void ToggleMinimizeToTray()
    {
        _manager.SetMinimizeToTray(!_manager.Current.MinimizeToTray);
        RefreshChecks();
    }

    private void RefreshChecks()
    {
        _autoStartItem.Checked = _manager.Current.StartWithWindows;
        _minimizeToTrayItem.Checked = _manager.Current.MinimizeToTray;
    }

    private void OnLanguageChanged(object? sender, EventArgs e) => ApplyStrings();

    private void ApplyStrings()
    {
        _toggleVisibleItem.Text = _localization.Get(LocKeys.Tray_ToggleVisible);
        _autoStartItem.Text = _localization.Get(LocKeys.Tray_AutoStart);
        _minimizeToTrayItem.Text = _localization.Get(LocKeys.Tray_MinimizeToTray);
        _settingsItem.Text = _localization.Get(LocKeys.Menu_Settings);
        _exitItem.Text = _localization.Get(LocKeys.Tray_Exit);
        _icon.Text = _localization.Get(LocKeys.App_Title);
    }

    public void Dispose()
    {
        _manager.TrayResidencyNoticeRequested -= OnTrayResidencyNotice;
        _localization.LanguageChanged -= OnLanguageChanged; // FR-16 — release the catalog subscription (T-11 누수 방지)
        _icon.Visible = false; // remove the tray icon immediately (else it lingers until the mouse hovers it)
        _icon.Dispose();
        _brandIcon.Dispose(); // NotifyIcon 이 참조를 놓은 뒤 원본 Icon 해제
        _menu.Dispose();
    }
}
