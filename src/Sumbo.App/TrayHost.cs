using System;
using System.Drawing;
using System.Runtime.Versioning;
using System.Windows.Forms;
using Sumbo.App.Ui;
using Sumbo.Core;

namespace Sumbo.App;

/// <summary>
/// The system-tray presence. Owns a single process-wide <see cref="NotifyIcon"/> so the app stays reachable
/// while the main window is hidden/minimized. The icon is present for the whole app lifetime — there is
/// always a way back (double-click / show), so the hidden window can never be stranded. Surface actions ride
/// <see cref="CloneManager.RequestSurface"/> (the manager stays UI-agnostic); exit and the setting toggles
/// are delegated to <see cref="CloneManager"/>.
/// <para>
/// Menu captions + tooltip come from the shared <see cref="LocalizationCatalog"/>. As a long-lived
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
    private readonly Icon _brandIcon;       // owned here; disposed after the NotifyIcon that references it
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

        // Load the brand icon at the system small-icon size so the 16px frame renders sharp in the tray.
        _brandIcon = AppIcons.Load(SystemInformation.SmallIconSize.Width);
        _icon = new NotifyIcon
        {
            Icon = _brandIcon,
            Text = _localization.Get(LocKeys.App_Title),
            Visible = true,
            ContextMenuStrip = _menu,
        };
        _icon.DoubleClick += (_, _) => _manager.RequestSurface(SurfaceRequest.Restore);

        _localization.LanguageChanged += OnLanguageChanged; // runtime relabel (unsubscribed in Dispose)
        _manager.TrayResidencyNoticeRequested += OnTrayResidencyNotice; // one-time close-to-tray balloon
    }

    // Same meaning as the ToggleVisible hotkey: hide leaves the window resident in the tray, restore = Visible+Normal.
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
        _localization.LanguageChanged -= OnLanguageChanged; // the catalog outlives the tray — unsubscribe to avoid a leak
        _icon.Visible = false; // remove the tray icon immediately (else it lingers until the mouse hovers it)
        _icon.Dispose();
        _brandIcon.Dispose(); // only after the NotifyIcon has released its reference to the Icon
        _menu.Dispose();
    }
}
