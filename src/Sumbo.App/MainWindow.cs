using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Windows.Forms;
using Sumbo.App.Ui;
using Sumbo.App.Ui.Panels;
using Sumbo.Core;
using Sumbo.Native;

namespace Sumbo.App;

/// <summary>
/// Single-window shell: the main window itself IS the mirror output — the whole center is a child-free
/// form-surface canvas hosting the DWM thumbnail (<see cref="MirrorSurface"/>), with a right icon rail whose items
/// switch the side panel between <see cref="PanelView"/>s. The shell owns the mirror and all window-control state
/// (size preset / anchor / click forwarding / click-through / lock / always-on-top) and wires panel events to
/// them — panels are pure views. Overlay (UI-hidden) mode collapses the chrome to a mirror-only canvas;
/// click-through forces it, with Ctrl+Alt+C / tray restore / ESC as the exits.
/// <para>
/// DWM constraints: the thumbnail destination must be this top-level window's handle, and the thumbnail composites
/// OVER child controls — so no child control may intersect <see cref="_mirrorRect"/>. The side panel and rail
/// never overlap the canvas; expanding/collapsing the panel re-fits the mirror via <see cref="DoLayout"/>.
/// </para>
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class MainWindow : Form
{
    private const int Grip = 6;         // borderless resize-grip margin (form-surface ring on the edges)
    private const int TitleH = Theme.TitleBarHeight;
    private const int BorderDip = 3;    // canvas frame margin reserved around the thumbnail

    private readonly CloneManager _manager;
    private readonly LocalizationCatalog _loc;

    // ── Title-bar window buttons (drawn on the form surface; hit-tested in mouse handlers) ──
    private Rectangle _btnMin, _btnMax, _btnClose;
    private int _hoverBtn = -1;

    // ── Right rail + option-panel host ──
    private const string PanelTargets = "targets";
    private const string PanelDisplay = "display";
    private const string PanelBehavior = "behavior";
    private const string PanelRegion = "region";
    private const string PanelProfiles = "profiles";
    private const string PanelHotkeys = "hotkeys";
    private const string PanelGroup = "group";
    private const string PanelSettings = "settings";
    private const string PanelAbout = "about";

    /// <summary>Rail order — parallel to <see cref="RailItems"/>. Every id maps to an embedded panel.</summary>
    private static readonly string[] RailIds =
    {
        PanelTargets, PanelRegion, PanelDisplay, PanelBehavior, PanelProfiles, PanelHotkeys, PanelGroup, PanelSettings, PanelAbout,
    };

    private readonly IconRail _rail = new();
    private readonly Panel _sidePanel = new();
    private bool _sideOpen = true;
    private string _activePanelId = PanelTargets;

    // shared panel header
    private readonly Label _panelTitle = new();
    private readonly FlatButton _panelClose = new();

    // ── Panel framework — one PanelView per rail id, swapped by visibility under the shared header ──
    private readonly TargetsPanel _targetsPanel;
    private readonly DisplayPanel _displayPanel;
    private readonly BehaviorPanel _behaviorPanel;
    private readonly RegionPanel _regionPanel;
    private readonly ProfilesPanel _profilesPanel;
    private readonly HotkeysPanel _hotkeysPanel;
    private readonly GroupPanel _groupPanel;
    private readonly SettingsPanel _settingsPanel;
    private readonly AboutPanel _aboutPanel;
    private readonly PanelView[] _allPanels;
    private readonly Dictionary<string, PanelView> _panels;

    // ── Region drag selection: armed by the region panel / Ctrl+Alt+R, then a drag over the mirror rect commits
    // a crop. The rubber band is a SCREEN-level reversible frame — the DWM thumbnail composites over the form
    // surface, so a GDI rubber band drawn in OnPaint would be hidden under it.
    private bool _regionSelecting;
    private bool _regionDragging;
    private Point _dragStartClient;
    private Point _dragCurrentClient;
    private Rectangle _dragFrame; // last reversible frame (screen coords), Empty when none

    // Canvas frame ring on/off — visual only; the reserved margin around the thumbnail stays either way.
    private bool _showMirrorBorder = true;

    // ── Window-control state ──
    private ClientSizeMode? _sizeMode;      // size preset; null = free size (a user drag or clamp clears it)
    private SnapAnchor? _anchor;            // null = unanchored
    private bool _clickForward;             // mutually exclusive with _clickThrough
    private bool _clickThrough;             // ON forces overlay (UI-hidden) mode
    private bool _locked;                   // in-memory only — not persisted in profiles
    private bool _alwaysOnTop;
    private bool _uiHidden;                 // overlay mode — rail/panel/title hidden, the whole client is the mirror
    private bool _preOverlaySideOpen;       // side-panel open state at overlay entry, reproduced on restore
    private Rectangle? _preOverlayBounds;   // window bounds saved at overlay entry (auto-fit to region), restored on exit
    private static readonly Size ChromeMinSize = new(960, 600); // floor while the rail + side panel are shown
    private static readonly Size OverlayMinSize = new(100, 60);  // overlay has no chrome — allow shrinking to a small region
    private OverlayGrip? _overlayGrip;      // separate top-level handle — stays clickable while the overlay is click-through
    private bool _suppressPlacementEvents;  // internal size/move changes must not clear the user's preset/anchor
    private bool _clickForwardFailWarned;   // one-time forward-failure notice; reset when forwarding is re-enabled
    private FormWindowState _lastWindowState = FormWindowState.Normal; // detects state transitions for panel reflect
    private bool _exitRequested;            // set by CloseForExit() — bypasses the close-to-tray cancel in OnFormClosing
    private bool _trayNoticeShown;          // close-to-tray residency balloon shown once per session
    private bool _userCloseGesture;         // deliberate user close: chrome X (OnMouseDown) or SC_CLOSE (Alt+F4 / system menu / taskbar) — vs a raw WM_CLOSE
    private readonly Icon _appIcon;         // borderless chrome, so only Alt-Tab/taskbar surfaces show it; disposed in OnFormClosed
    private const int MK_LBUTTON = 0x0001;
    private const int ScClose = 0xF060;     // WM_SYSCOMMAND SC_CLOSE
    private readonly uint _showInstanceMsg = User32.RegisterWindowMessage(Program.ShowInstanceMessage); // second-launch surface signal

    // ── Embedded mirror ── the canvas is bare FORM SURFACE: OnPaint draws the frame + idle hint at
    // _mirrorRect, and the DWM thumbnail composites over its inner area. No child control may cover this rect.
    private readonly MirrorSurface _mirror = new();
    private Rectangle _mirrorRect;
    private readonly System.Windows.Forms.Timer _sourceWatch = new() { Interval = 1000 };

    // ── Group cycling state ──
    private readonly GroupSwitcher _group = new();
    private readonly System.Windows.Forms.Timer _groupTimer = new();
    private bool _groupMissingWarned;
    private int _groupMissCount;

    // ── Mirror right-click context menu ──
    private readonly ContextMenuStrip _mirrorMenu = new();
    private ToolStripMenuItem _ctxRestoreUi = null!;   // overlay only — the grip menu's way back to the full window
    private ToolStripSeparator _ctxRestoreSep = null!;
    private ToolStripMenuItem _ctxTarget = null!;
    private ToolStripMenuItem _ctxStop = null!;
    private ToolStripMenuItem _ctxRegion = null!;
    private ToolStripMenuItem _ctxForward = null!;
    private ToolStripMenuItem _ctxThrough = null!;
    private ToolStripMenuItem _ctxSettings = null!;

    public MainWindow(CloneManager manager, LocalizationCatalog localization)
    {
        _manager = manager ?? throw new ArgumentNullException(nameof(manager));
        _loc = localization ?? throw new ArgumentNullException(nameof(localization));

        _appIcon = AppIcons.Load(SystemInformation.IconSize.Width);
        Icon = _appIcon;

        _targetsPanel = new TargetsPanel(_loc);
        _displayPanel = new DisplayPanel();
        _behaviorPanel = new BehaviorPanel();
        _regionPanel = new RegionPanel(_loc);
        _profilesPanel = new ProfilesPanel(_loc);
        _hotkeysPanel = new HotkeysPanel();
        _groupPanel = new GroupPanel();
        _settingsPanel = new SettingsPanel();
        _aboutPanel = new AboutPanel();
        _allPanels = new PanelView[]
        {
            _targetsPanel, _displayPanel, _behaviorPanel, _regionPanel, _profilesPanel,
            _hotkeysPanel, _groupPanel, _settingsPanel, _aboutPanel,
        };
        _panels = new Dictionary<string, PanelView>
        {
            [PanelTargets] = _targetsPanel,
            [PanelDisplay] = _displayPanel,
            [PanelBehavior] = _behaviorPanel,
            [PanelRegion] = _regionPanel,
            [PanelProfiles] = _profilesPanel,
            [PanelHotkeys] = _hotkeysPanel,
            [PanelGroup] = _groupPanel,
            [PanelSettings] = _settingsPanel,
            [PanelAbout] = _aboutPanel,
        };

        Text = _loc.Get(LocKeys.App_Title);
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Theme.WindowBg;
        ForeColor = Theme.TextPrimary;
        Font = Theme.Body;
        MinimumSize = ChromeMinSize;
        ClientSize = new Size(1400, 820);
        DoubleBuffered = true;
        KeyPreview = true;
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.ResizeRedraw, true);

        BuildUi();
        ApplyStrings();
        _loc.LanguageChanged += OnLanguageChanged;

        // Panel → shell wiring (the shell owns the mirror; panels raise intents and get state reflected back).
        _targetsPanel.TargetActivated += OnTargetActivated;
        _targetsPanel.StopRequested += OnStopRequested;
        _displayPanel.OpacityChangeRequested += OnOpacityChangeRequested;
        _displayPanel.SizeModeSelected += OnSizeModeSelected;
        _displayPanel.AnchorSelected += OnAnchorSelected;
        _behaviorPanel.BorderToggleRequested += OnBorderToggleRequested;
        _behaviorPanel.ClickForwardToggleRequested += OnClickForwardToggleRequested;
        _behaviorPanel.ClickThroughToggleRequested += OnClickThroughToggleRequested;
        _behaviorPanel.LockToggleRequested += OnLockToggleRequested;
        _behaviorPanel.AotToggleRequested += OnAotToggleRequested;
        _behaviorPanel.HideUiRequested += OnHideUiRequested;
        _regionPanel.SelectRequested += OnRegionSelectRequested;
        _regionPanel.ClearRequested += OnRegionClearRequested;
        _regionPanel.SaveRequested += OnRegionSaveRequested;
        _regionPanel.ApplyRequested += OnRegionApplyRequested;
        _regionPanel.DeleteRequested += OnRegionDeleteRequested;
        _profilesPanel.SaveRequested += OnProfileSaveRequested;
        _profilesPanel.ApplyRequested += OnProfileApplyRequested;
        _profilesPanel.DeleteRequested += OnProfileDeleteRequested;
        _groupPanel.AddCurrentRequested += OnGroupAddRequested;
        _groupPanel.ClearRequested += OnGroupClearRequested;
        _groupPanel.ToggleRunRequested += OnGroupToggleRunRequested;
        _groupPanel.IntervalChanged += OnGroupIntervalChanged;
        _settingsPanel.LanguageSelected += OnSettingsLanguageSelected;
        _settingsPanel.StartWithWindowsToggled += OnSettingsStartWithWindowsToggled;
        _settingsPanel.MinimizeToTrayToggled += OnSettingsMinimizeToTrayToggled;
        _aboutPanel.LinkActivated += OnAboutLinkActivated;
        _hotkeysPanel.ReflectFailures(_manager.HotkeyFailures); // startup hotkey-conflict flags (after ApplyStrings)
        _settingsPanel.ReflectSettings(_manager.Current);       // seed language/startup toggles
        _groupTimer.Tick += OnGroupTick;

        _mirror.SetOpacity(_manager.Current.Defaults.Opacity);
        _alwaysOnTop = _manager.Current.Defaults.AlwaysOnTop;
        TopMost = _alwaysOnTop;
        SyncPanels();

        _mirror.Changed += OnMirrorChanged;
        _manager.HotkeyRouted += OnHotkeyRouted;       // global hotkeys land on this window
        _manager.SurfaceRequested += OnSurfaceRequested; // tray show/hide/restore requests target this window
        _sourceWatch.Tick += (_, _) => _mirror.ValidateSource(); // prompt source-loss → idle canvas (no dead frame)
        _sourceWatch.Start();
    }

    // ── Construction ─────────────────────────────────────────────────────

    private void BuildUi()
    {
        _sidePanel.BackColor = Theme.PanelBg;

        BuildPanelHost();
        BuildRail();
        BuildMirrorMenu();

        Controls.Add(_sidePanel);
        Controls.Add(_rail);
    }

    /// <summary>Mirror-canvas right-click menu. Shown only over a live mirror (<see cref="OnMouseUp"/> gate); item
    /// enable/check state is set on Opening. Click-forward/through toggle through the same shell routes as the
    /// display panel.</summary>
    private void BuildMirrorMenu()
    {
        _ctxRestoreUi = new ToolStripMenuItem();
        _ctxRestoreUi.Click += (_, _) => RestoreUserInterface();
        _ctxRestoreSep = new ToolStripSeparator();
        _ctxTarget = new ToolStripMenuItem();
        _ctxTarget.Click += (_, _) => { SetActivePanel(PanelTargets); _targetsPanel.ReloadTargets(); };
        _ctxStop = new ToolStripMenuItem();
        _ctxStop.Click += (_, _) => _mirror.Stop();
        _ctxRegion = new ToolStripMenuItem();
        _ctxRegion.Click += (_, _) => EnterRegionSelect();
        _ctxForward = new ToolStripMenuItem();
        _ctxForward.Click += (_, _) => SetClickForward(!_clickForward);
        _ctxThrough = new ToolStripMenuItem();
        _ctxThrough.Click += (_, _) => SetClickThrough(!_clickThrough);
        _ctxSettings = new ToolStripMenuItem();
        _ctxSettings.Click += (_, _) => SetActivePanel(PanelSettings);

        _mirrorMenu.Items.AddRange(new ToolStripItem[]
        {
            _ctxRestoreUi, _ctxRestoreSep,
            _ctxTarget,
            new ToolStripSeparator(),
            _ctxStop, _ctxRegion,
            new ToolStripSeparator(),
            _ctxForward, _ctxThrough,
            new ToolStripSeparator(),
            _ctxSettings,
        });

        _mirrorMenu.Opening += (_, _) =>
        {
            bool m = _mirror.HasMirror;
            _ctxRestoreUi.Visible = _uiHidden; // grip-menu entry — meaningless while the UI is already shown
            _ctxRestoreSep.Visible = _uiHidden;
            _ctxStop.Enabled = m;
            _ctxRegion.Enabled = m;
            _ctxForward.Enabled = m && !_clickThrough; // forwarding and click-through are mutually exclusive
            _ctxForward.Checked = _clickForward;
            _ctxThrough.Enabled = m && _manager.IsClickThroughHotkeyLive;
            _ctxThrough.Checked = _clickThrough;
        };
    }

    private void BuildPanelHost()
    {
        _panelTitle.BackColor = Theme.PanelBg;
        _panelTitle.ForeColor = Theme.TextPrimary;
        _panelTitle.Font = Theme.H1;
        _panelTitle.AutoSize = false;
        _panelTitle.TextAlign = ContentAlignment.MiddleLeft;

        _panelClose.Kind = ButtonKind.Ghost;
        _panelClose.Glyph = Glyph.Close;
        _panelClose.GlyphSize = 12f;
        _panelClose.CornerBack = Theme.PanelBg;
        _panelClose.Size = new Size(28, 28);
        _panelClose.Click += (_, _) => SetSideOpen(false);

        _sidePanel.Controls.Add(_panelTitle);
        _sidePanel.Controls.Add(_panelClose);
        foreach (PanelView view in _allPanels)
            _sidePanel.Controls.Add(view); // all views share the content rect; exactly one is visible
        _panels[_activePanelId].Visible = true;
    }

    private void BuildRail()
    {
        _rail.SetItems(RailItems());
        _rail.SelectedIndex = Array.IndexOf(RailIds, _activePanelId);
        _rail.ItemClicked += (_, id) => SetActivePanel(id);
    }

    private List<RailItem> RailItems() => new()
    {
        new(PanelTargets, Glyph.GridView, _loc.Get(LocKeys.Main_Nav_Targets)),
        new(PanelRegion, Glyph.Crop, _loc.Get(LocKeys.Main_Nav_Region)),
        new(PanelDisplay, Glyph.Monitor, _loc.Get(LocKeys.Main_Display_Title)),
        new(PanelBehavior, Glyph.Lightning, _loc.Get(LocKeys.Main_Nav_Behavior)),
        new(PanelProfiles, Glyph.Contact, _loc.Get(LocKeys.Main_Nav_Profiles)),
        new(PanelHotkeys, Glyph.Keyboard, _loc.Get(LocKeys.Main_Nav_Hotkeys)),
        new(PanelGroup, Glyph.Switch, _loc.Get(LocKeys.Main_Nav_Group)),
        new(PanelSettings, Glyph.Settings, _loc.Get(LocKeys.Main_Nav_Settings)),
        new(PanelAbout, Glyph.Info, _loc.Get(LocKeys.Main_Nav_About)),
    };

    private string PanelTitle(string id) => id switch
    {
        PanelTargets => _loc.Get(LocKeys.Main_Nav_Targets),
        PanelDisplay => _loc.Get(LocKeys.Main_Display_Title),
        PanelBehavior => _loc.Get(LocKeys.Main_Nav_Behavior),
        PanelRegion => _loc.Get(LocKeys.Main_Nav_Region),
        PanelProfiles => _loc.Get(LocKeys.Main_Nav_Profiles),
        PanelHotkeys => _loc.Get(LocKeys.Main_Nav_Hotkeys),
        PanelGroup => _loc.Get(LocKeys.Main_Nav_Group),
        PanelSettings => _loc.Get(LocKeys.Main_Nav_Settings),
        PanelAbout => _loc.Get(LocKeys.Main_Nav_About),
        _ => _loc.Get(LocKeys.Main_Nav_Targets),
    };

    /// <summary>Switches the side panel to <paramref name="id"/>'s <see cref="PanelView"/> and expands the panel.
    /// IconRail's programmatic SelectedIndex setter does not raise ItemClicked, so this is the single routing entry
    /// point for both user clicks and programmatic switches (e.g. the PickWindow hotkey / tray OpenSettings).</summary>
    private void SetActivePanel(string id)
    {
        if (_uiHidden)
            RestoreUserInterface(); // a panel request from the grip menu implies returning to a fully USABLE window
        _activePanelId = id;
        _rail.SelectedIndex = Array.IndexOf(RailIds, id);
        _panelTitle.Text = PanelTitle(id);

        // The stores can be edited outside this window's lifetime — re-read on entry so the list is fresh without
        // a per-keystroke file watch. The settings and group panels re-seed from their sources on entry too.
        if (id == PanelRegion)
            RefreshRegionList();
        else if (id == PanelProfiles)
            RefreshProfileList();
        else if (id == PanelSettings)
            _settingsPanel.ReflectSettings(_manager.Current);
        else if (id == PanelGroup)
            ReflectGroup();

        PanelView active = _panels[id];
        foreach (PanelView view in _allPanels)
            view.Visible = view == active;
        SetSideOpen(true);
    }

    private void SetSideOpen(bool open)
    {
        if (_sideOpen == open) return;
        _sideOpen = open;
        _sidePanel.Visible = open;
        DoLayout(); // the mirror canvas grows/shrinks with the panel — re-fit the thumbnail
    }

    // ── Panels → embedded mirror (shell wiring) ──────────────────────────

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        _targetsPanel.EnsureLoaded(); // first enumeration once the window (and its handle) exist
        if (_alwaysOnTop)
            SetAlwaysOnTop(true); // constructor's TopMost set was pre-Show — force the z-order now the window exists
    }

    /// <summary>Card click = start (or retarget) the embedded mirror. Re-clicking the already-mirrored target keeps
    /// the live session (no re-register flicker); a failed registration shows a notice and leaves any current
    /// mirror running (MirrorSurface swaps only on success).</summary>
    private void OnTargetActivated(object? sender, WindowInfo target)
    {
        if (_mirror.HasMirror && _mirror.TargetHandle == target.Handle)
        {
            SyncPanels(); // already mirroring this window — just re-assert the visuals
            return;
        }

        if (!_mirror.Start(Handle, target, out string? error))
        {
            SyncPanels(); // undo the card's optimistic self-select — the mirror state didn't change
            MessageBox.Show(
                this,
                error ?? _loc.Get(LocKeys.Dialog_CloneFailed_Body), // zero-size/no-message failures get a real body, not the caption twice
                _loc.Get(LocKeys.Dialog_CloneFailed_Caption),
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }
        // success → _mirror.Changed → OnMirrorChanged syncs the panels and the canvas
    }

    private void OnStopRequested(object? sender, EventArgs e) => _mirror.Stop();

    private void OnOpacityChangeRequested(object? sender, int percent) => ApplyOpacity(percent);

    private void OnBorderToggleRequested(object? sender, bool show)
    {
        _showMirrorBorder = show;
        SyncPanels();
        Invalidate(_mirrorRect); // redraw (or clear) the canvas ring — the DWM rect itself is unchanged
    }

    /// <summary>Single route for opacity changes — the panel slider AND the Ctrl+Alt+↑/↓ hotkeys land here, so the
    /// mirror and the display-panel readout can never diverge.</summary>
    private void ApplyOpacity(int percent)
    {
        _mirror.SetOpacity(percent);
        ApplyVisualState(); // in overlay mode the Form.Opacity channel must track the new percent
        SyncPanels();
    }

    // ── Window controls (size preset / anchor / click modes / lock) ──────

    private void OnSizeModeSelected(object? sender, int index)
    {
        if (index == 3)
        {
            EnterFullscreen();
            return;
        }
        ApplyMirrorSizeMode(index switch
        {
            0 => ClientSizeMode.Source,
            1 => ClientSizeMode.Half,
            _ => ClientSizeMode.Quarter,
        });
    }

    /// <summary>Fullscreen preset = maximize. The panel segment follows via the state-transition check in
    /// <see cref="OnResize"/> plus the SyncPanels here.</summary>
    private void EnterFullscreen()
    {
        if (!_mirror.HasMirror || _locked)
        {
            SyncPanels();
            return;
        }
        if (WindowState != FormWindowState.Maximized)
            WindowState = FormWindowState.Maximized;
        SyncPanels();
    }

    /// <summary>
    /// Single route for size presets: computes the target thumbnail physical size from the active source (crop
    /// applied) via <see cref="WindowPlacement.ComputeSizeMode"/>, back-computes the chrome <see cref="DoLayout"/>
    /// reserves around the mirror (title/grips/padding/rail/side panel + the physical border margin) into a
    /// ClientSize, then re-measures the actual DWM destination. More than 4px off target (e.g. clamped by
    /// MinimumSize) means the preset no longer holds — clear it. The work-area cap is baked into the target itself,
    /// so a capped-but-exact result keeps the preset.
    /// </summary>
    private void ApplyMirrorSizeMode(ClientSizeMode mode)
    {
        if (!_mirror.HasMirror || _locked)
        {
            SyncPanels();
            return;
        }

        if (WindowState == FormWindowState.Maximized)
        {
            _suppressPlacementEvents = true;
            try { WindowState = FormWindowState.Normal; } // leave maximized before applying a preset
            finally { _suppressPlacementEvents = false; }
        }

        if (!_mirror.TryGetActiveSourceSize(out int srcW, out int srcH))
        {
            SyncPanels();
            return;
        }

        (double sx, double sy) = ClientScale();
        int margin = Math.Max(2, (int)Math.Round(BorderDip * sx)); // same formula as MirrorHostPhysical — the back-computation must agree
        int overheadW = Grip * 2 + Theme.Pad * 2 + Theme.IconRailWidth + (_sideOpen ? Theme.SidePanelWidth : 0);
        int overheadH = TitleH + Theme.Pad * 2 + Grip; // inverse of DoLayout's reserved chrome

        Rectangle wa = Screen.FromControl(this).WorkingArea;
        int maxThumbW = Math.Max(1, (int)((wa.Width - overheadW) * sx) - margin * 2);
        int maxThumbH = Math.Max(1, (int)((wa.Height - overheadH) * sy) - margin * 2);
        (int tw, int th) = WindowPlacement.ComputeSizeMode(mode, srcW, srcH, maxThumbW, maxThumbH);

        var target = new Size(
            (int)Math.Ceiling((tw + margin * 2) / sx) + overheadW,
            (int)Math.Ceiling((th + margin * 2) / sy) + overheadH);

        _suppressPlacementEvents = true;
        try { ClientSize = target; } // the Form clamps to MinimumSize itself — the measurement below detects that
        finally { _suppressPlacementEvents = false; }

        _sizeMode = mode;
        ReapplyAnchor();

        (int aw, int ah) = _mirror.LastDestSize; // measured after ClientSize → OnLayout → DoLayout → FitToHost settles
        if (Math.Abs(aw - tw) > 4 || Math.Abs(ah - th) > 4)
            _sizeMode = null; // clamped off target — the preset no longer holds

        SyncPanels();
    }

    private void OnAnchorSelected(object? sender, int index)
    {
        if (_locked)
        {
            SyncPanels();
            return;
        }
        _anchor = index switch
        {
            1 => SnapAnchor.TopLeft,
            2 => SnapAnchor.Top,
            3 => SnapAnchor.TopRight,
            4 => SnapAnchor.Left,
            5 => SnapAnchor.Center,
            6 => SnapAnchor.Right,
            7 => SnapAnchor.BottomLeft,
            8 => SnapAnchor.Bottom,
            9 => SnapAnchor.BottomRight,
            _ => null, // 0 = unanchored
        };
        ReapplyAnchor();
        SyncPanels();
    }

    /// <summary>Re-anchors the window inside the current work area (after a preset apply, resize end, or DPI move).
    /// Skipped when maximized — placement has no meaning there.</summary>
    private void ReapplyAnchor()
    {
        if (_anchor is null || WindowState != FormWindowState.Normal)
            return;

        Rectangle wa = Screen.FromControl(this).WorkingArea;
        (int x, int y) = WindowPlacement.ComputeAnchoredLocation(
            _anchor.Value, Bounds.Width, Bounds.Height, wa.Left, wa.Top, wa.Right, wa.Bottom);

        _suppressPlacementEvents = true;
        try { Location = new Point(x, y); }
        finally { _suppressPlacementEvents = false; }
    }

    private void OnClickForwardToggleRequested(object? sender, bool on) => SetClickForward(on);

    /// <summary>Single route for click forwarding (desired state — idempotent). Mutually exclusive with
    /// click-through (a transparent window receives no input); on refusal SyncPanels reverts the toggle visual.</summary>
    private void SetClickForward(bool on)
    {
        if (_clickForward != on && (!on || (_mirror.HasMirror && !_clickThrough)))
        {
            _clickForward = on;
            if (on)
                _clickForwardFailWarned = false; // re-arm the one-time unsupported notice on every enable
        }
        SyncPanels();
    }

    /// <summary>
    /// Single route for click-through — panel toggle, Ctrl+Alt+C, profile restore, tray and ESC all enter here.
    /// ON: requires a mirror and a live escape hotkey, turns forwarding off (mutually exclusive), and forces
    /// overlay (UI-hidden) mode. OFF: also restores the UI — every escape path returns a fully usable window.
    /// </summary>
    private void SetClickThrough(bool on)
    {
        if (_clickThrough == on)
        {
            SyncPanels();
            return;
        }

        if (on)
        {
            if (!_mirror.HasMirror)
            {
                SyncPanels();
                return;
            }
            if (!_manager.IsClickThroughHotkeyLive)
            {
                SyncPanels(); // revert the toggle visual
                MessageBox.Show(
                    this,
                    _loc.Format(LocKeys.Dialog_ClickThroughUnavailable_Body, ClickThroughChord()),
                    _loc.Get(LocKeys.Dialog_ClickThroughUnavailable_Caption),
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return;
            }
            _clickForward = false; // mutually exclusive
        }

        _clickThrough = on;
        SetOverlayMode(on); // ON hides the UI, OFF restores it — includes ApplyVisualState + SyncPanels
    }

    private void OnClickThroughToggleRequested(object? sender, bool on) => SetClickThrough(on);

    private static string ClickThroughChord()
    {
        foreach (HotkeyBinding b in HotkeyService.Defaults)
            if (b.Action == HotkeyAction.ClickThrough) return b.Display;
        return "";
    }

    private void OnHideUiRequested(object? sender, EventArgs e)
    {
        if (_mirror.HasMirror)
            SetOverlayMode(true);
    }

    /// <summary>Opens an about-panel link (releases / sponsors / source) in the default browser. A missing browser
    /// association or a cancelled shell prompt must not crash the app.</summary>
    private void OnAboutLinkActivated(object? sender, string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception)
        {
            // no browser association / user-cancelled shell prompt — nothing actionable
        }
    }

    /// <summary>Single route for overlay (UI-hidden) mode — state switch + visual-state reapply + panel reflect.</summary>
    private void SetOverlayMode(bool hidden)
    {
        SetUiHidden(hidden);
        ApplyVisualState();
        SyncPanels();
    }

    /// <summary>The one way back to a fully USABLE window (grip menu / panel requests from overlay): click-through
    /// must fall first — restoring the UI alone would leave WS_EX_TRANSPARENT on the whole window, an interface
    /// that shows but cannot be clicked. The click-through route itself restores the UI.</summary>
    private void RestoreUserInterface()
    {
        if (_clickThrough)
            SetClickThrough(false);
        else if (_uiHidden)
            SetOverlayMode(false);
    }

    /// <summary>Hides/restores the chrome (rail/side panel/title) so the whole client becomes the mirror canvas.
    /// Restore reproduces the side-panel open state captured at entry.</summary>
    private void SetUiHidden(bool hidden)
    {
        if (_uiHidden == hidden)
            return;

        if (hidden)
        {
            _preOverlaySideOpen = _sideOpen;
            _sideOpen = false;
            _sidePanel.Visible = false;
            _rail.Visible = false;
        }
        else
        {
            _rail.Visible = true;
            _sideOpen = _preOverlaySideOpen;
            _sidePanel.Visible = _sideOpen;
        }
        _uiHidden = hidden;

        if (hidden)
            FitWindowToOverlay(); // shrink the window to what the mirror shows so no black frame is left around the region
        else
            RestoreOverlayBounds();

        DoLayout();

        if (hidden)
            ShowOverlayGrip(); // after the fit — the dot anchors to the final top-left
        else
            HideOverlayGrip();
    }

    /// <summary>Shows (creating on first use) the overlay control dot at the window's top-left corner. A separate
    /// top-level window: it keeps taking mouse input while the overlay itself is click-through — left-drag moves
    /// the overlay, right-click opens the mirror menu.</summary>
    private void ShowOverlayGrip()
    {
        if (_overlayGrip is null)
        {
            _overlayGrip = new OverlayGrip();
            _overlayGrip.DragMoved += OnGripDragMoved;
            _overlayGrip.MenuRequested += OnGripMenuRequested;
        }
        int s = (int)Math.Round(OverlayGrip.LogicalSize * DeviceDpi / 96.0);
        _overlayGrip.Size = new Size(s, s);
        PositionOverlayGrip();
        if (!_overlayGrip.Visible)
            _overlayGrip.Show(this); // owned — always above the (topmost) overlay, hidden with it
    }

    private void HideOverlayGrip()
    {
        if (_overlayGrip is { Visible: true })
            _overlayGrip.Hide();
    }

    private void PositionOverlayGrip()
    {
        _overlayGrip?.SetBounds(Left + 6, Top + 6, _overlayGrip.Width, _overlayGrip.Height,
            BoundsSpecified.Location);
    }

    private void OnGripDragMoved(object? sender, Point delta)
    {
        if (_locked || WindowState != FormWindowState.Normal)
            return;
        Location = new Point(Left + delta.X, Top + delta.Y); // OnMove clears the anchor + re-pins the dot
    }

    private void OnGripMenuRequested(object? sender, EventArgs e)
        => _mirrorMenu.Show(Cursor.Position);

    /// <summary>Overlay entry: size the window so its canvas matches the shown content — the crop region when
    /// clipped, else the full source — removing the letterbox a chrome-sized window leaves around a differently
    /// shaped region. Skipped when locked or maximized; the pre-overlay bounds are saved for restore on exit.</summary>
    private void FitWindowToOverlay()
    {
        _preOverlayBounds = null;
        if (_locked || WindowState != FormWindowState.Normal)
            return;
        if (!_mirror.TryGetActiveSourceSize(out _, out _))
            return;
        _preOverlayBounds = Bounds;
        ApplyOverlayFit();
    }

    /// <summary>Re-fit during overlay — a DPI change or a group-cycle retarget alters what the canvas must hold,
    /// and the chrome-aware size-preset path must not run instead (it re-adds rail/title overhead the overlay does
    /// not show). Keeps the entry-captured restore bounds; no-op when the entry fit was skipped.</summary>
    private void RefitOverlay()
    {
        if (_preOverlayBounds is null || _locked || WindowState != FormWindowState.Normal)
            return;
        ApplyOverlayFit();
    }

    private void ApplyOverlayFit()
    {
        if (!_mirror.TryGetActiveSourceSize(out int srcW, out int srcH))
            return;

        (double sx, double sy) = ClientScale();
        Rectangle wa = Screen.FromControl(this).WorkingArea;
        int maxThumbW = Math.Max(1, (int)(wa.Width * sx));
        int maxThumbH = Math.Max(1, (int)(wa.Height * sy));
        // Source (1:1) capped to the work area, aspect preserved. Overlay is frameless — the client IS the
        // thumbnail, so the DWM fit fills it edge-to-edge (no letterbox).
        (int tw, int th) = WindowPlacement.ComputeSizeMode(ClientSizeMode.Source, srcW, srcH, maxThumbW, maxThumbH);

        var target = new Size(
            (int)Math.Ceiling(tw / sx),
            (int)Math.Ceiling(th / sy));

        _suppressPlacementEvents = true;
        try
        {
            MinimumSize = OverlayMinSize; // the 960×600 chrome floor would otherwise clamp a small region back into a letterbox
            ClientSize = target;
            ReapplyAnchor();              // keep an anchored overlay pinned after the resize
        }
        finally
        {
            _suppressPlacementEvents = false;
        }
    }

    /// <summary>Overlay exit: restore the chrome minimum and the window bounds captured at entry (no-op when the
    /// fit was skipped).</summary>
    private void RestoreOverlayBounds()
    {
        if (_preOverlayBounds is not Rectangle bounds)
            return;
        _preOverlayBounds = null;
        _suppressPlacementEvents = true;
        try
        {
            MinimumSize = ChromeMinSize; // WinForms grows the window to this; the bounds assignment below sets the real size
            Bounds = bounds;
        }
        finally
        {
            _suppressPlacementEvents = false;
        }
    }

    /// <summary>
    /// Reapplies the opacity invariant — one saved percent, two channels. Normal: Form.Opacity 1.0 + thumbnail at
    /// the percent. Overlay: Form.Opacity at the percent (see-through to what is behind) + thumbnail 255, avoiding
    /// double attenuation. Click-through style always last: when Opacity returns to 1.0, WinForms drops
    /// WS_EX_LAYERED|WS_EX_TRANSPARENT.
    /// </summary>
    private void ApplyVisualState()
    {
        _mirror.SetOverlay(_uiHidden);
        Opacity = _uiHidden ? _mirror.OpacityPercent / 100.0 : 1.0;
        ApplyClickThroughStyle(_clickThrough);
    }

    /// <summary>Adds/removes <c>WS_EX_LAYERED|WS_EX_TRANSPARENT</c> then commits with <c>SWP_FRAMECHANGED</c>.
    /// LAYERED is kept while Opacity &lt; 1 so the window-opacity effect survives the click-through exit.</summary>
    private void ApplyClickThroughStyle(bool on)
    {
        if (!IsHandleCreated)
            return;

        long ex = User32.GetWindowLongPtr(Handle, User32.GWL_EXSTYLE).ToInt64();
        if (on)
        {
            ex |= User32.WS_EX_LAYERED | User32.WS_EX_TRANSPARENT;
        }
        else
        {
            ex &= ~User32.WS_EX_TRANSPARENT;
            if (Opacity >= 1.0)
                ex &= ~User32.WS_EX_LAYERED;
        }

        User32.SetWindowLongPtr(Handle, User32.GWL_EXSTYLE, new IntPtr(ex));
        User32.SetWindowPos(
            Handle, IntPtr.Zero, 0, 0, 0, 0,
            User32.SWP_NOMOVE | User32.SWP_NOSIZE | User32.SWP_NOZORDER | User32.SWP_NOACTIVATE | User32.SWP_FRAMECHANGED);
    }

    private void OnLockToggleRequested(object? sender, bool on)
    {
        _locked = on; // size/anchor requests gate on this; move/resize are blocked in the chrome hit-test and WndProc
        SyncPanels();
    }

    private void OnAotToggleRequested(object? sender, bool on) => SetAlwaysOnTop(on);

    private void SetAlwaysOnTop(bool on)
    {
        _alwaysOnTop = on;
        TopMost = on;
        // Setting TopMost before the handle exists (constructor) does not always take the WS_EX_TOPMOST z-order,
        // so a fresh launch would sit behind other windows until toggled off→on. Force it explicitly once shown.
        if (on && IsHandleCreated)
            User32.SetWindowPos(Handle, User32.HWND_TOPMOST, 0, 0, 0, 0,
                User32.SWP_NOMOVE | User32.SWP_NOSIZE | User32.SWP_NOACTIVATE);
        SyncPanels();
    }

    /// <summary>Maps a mirror-canvas mouse event onto the source and posts it (the mapping lives in
    /// <see cref="MirrorSurface"/>). The shell's share: gating (forward ON / live mirror / region selection takes
    /// priority) + logical→physical conversion + a one-time failure notice.</summary>
    private bool TryForward(uint msg, MouseEventArgs e, IntPtr wParam, bool wheel = false)
    {
        if (!_clickForward || !_mirror.HasMirror || _regionSelecting || _regionDragging)
            return false;
        if (!_mirrorRect.Contains(e.Location))
            return false;

        (double sx, double sy) = ClientScale();
        bool mapped = _mirror.TryForwardMouse(
            msg, (int)Math.Round(e.X * sx), (int)Math.Round(e.Y * sy), wParam, wheel, out bool posted);

        if (mapped && !posted && msg == User32.WM_LBUTTONDOWN && !_clickForwardFailWarned)
        {
            _clickForwardFailWarned = true;
            int err = Marshal.GetLastWin32Error();
            MessageBox.Show(
                this,
                _loc.Format(LocKeys.Dialog_ClickForwardUnsupported_Body, err),
                _loc.Get(LocKeys.Dialog_ClickForwardUnsupported_Caption),
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }
        return mapped;
    }

    /// <summary>Single chokepoint for mirror transitions (start / stop / source loss): panel state and the canvas
    /// frame all follow <see cref="MirrorSurface"/> state here. Losing the mirror also releases every window mode —
    /// a stuck lock or an orphaned click-through/overlay would strand the user on a transparent, empty, unmovable
    /// window.</summary>
    private void OnMirrorChanged(object? sender, EventArgs e)
    {
        if (!_mirror.HasMirror)
        {
            if (_regionSelecting || _regionDragging)
                CancelRegionSelect(); // source lost mid-select — don't leave a cross cursor armed on an idle canvas
            _locked = false;
            _clickForward = false;
            _sizeMode = null;
            _anchor = null; // an idle window must not keep a stale anchor
            if (_group.IsRunning)
                StopGroupSwitch(); // no source left to cycle — stop the timer, keep the members
            if (_clickThrough)
                SetClickThrough(false); // the route also restores the UI — no stranding on a transparent empty window
            else if (_uiHidden)
                SetOverlayMode(false);
        }
        else if (_uiHidden)
        {
            RefitOverlay(); // group-cycle retarget: the new member's shape must not letterbox in the old member's frame
        }
        SyncPanels();
        Invalidate(_mirrorRect);
    }

    /// <summary>Reflects the mirror + window-control state into the live panels (selection/stop button in targets,
    /// the full control surface in display, crop readout/actions in region, save gate in profiles) — the one
    /// shell → panel push point.</summary>
    private void SyncPanels()
    {
        _targetsPanel.ReflectMirror(_mirror.TargetHandle, _mirror.HasMirror);
        _displayPanel.ReflectMirror(_mirror.HasMirror, ShellViewState());
        _behaviorPanel.ReflectMirror(_mirror.HasMirror, ShellViewState(), _manager.IsClickThroughHotkeyLive);
        _regionPanel.ReflectMirror(_mirror.HasMirror, _mirror.CurrentRegion);
        _profilesPanel.ReflectMirror(_mirror.HasMirror);
        _groupPanel.ReflectMirror(_mirror.HasMirror); // lightweight — button enable only; the member list goes through ReflectGroup
    }

    /// <summary>Snapshot of the shell's window-control state for the display panel (fullscreen = maximized).</summary>
    private MirrorViewState ShellViewState() => new()
    {
        SizeMode = _sizeMode,
        IsFullscreen = WindowState == FormWindowState.Maximized,
        Anchor = _anchor,
        OpacityPercent = _mirror.OpacityPercent,
        ClickForward = _clickForward,
        ClickThrough = _clickThrough,
        Locked = _locked,
        ShowBorder = _showMirrorBorder,
        AlwaysOnTop = _alwaysOnTop,
        TargetTitle = _mirror.TargetTitle,
    };

    // ── Region crop — panel/hotkey intents → mirror crop + store ─────────

    /// <summary>Single route for crop changes (clear / saved apply / profile restore) — mirror crop, panel reflect
    /// and the canvas repaint stay together (the drag commit shares the same tail in
    /// <see cref="CommitRegionDrag"/>). Not a mirror lifecycle transition, so <c>Changed</c> stays out of it.</summary>
    private void ApplyRegion(Sumbo.Core.Region? region)
    {
        _mirror.SetRegion(region);
        SyncPanels();
        Invalidate(_mirrorRect);
    }

    private void OnRegionSelectRequested(object? sender, EventArgs e) => EnterRegionSelect();

    private void OnRegionClearRequested(object? sender, EventArgs e) => ApplyRegion(null);

    private void OnRegionApplyRequested(object? sender, NamedRegion named) => ApplyRegion(named.Region);

    /// <summary>Arms the mirror-rect drag selection (session guard + cross cursor). A global Ctrl+Alt+R can arrive
    /// while tray-hidden/minimized; restore first so there is a surface to drag on (a no-op when already visible,
    /// so overlay-mode behavior is unchanged).</summary>
    private void EnterRegionSelect()
    {
        if (!_mirror.HasMirror || _clickThrough) // during click-through the mouse never reaches this window
            return;
        if (!Visible || WindowState == FormWindowState.Minimized)
            RestoreFromTray();
        _regionSelecting = true;
        Cursor = Cursors.Cross;
    }

    /// <summary>Disarms the drag selection (ESC / source loss) and erases any live rubber band.</summary>
    private void CancelRegionSelect()
    {
        EraseDragFrame();
        _regionSelecting = false;
        _regionDragging = false;
        Cursor = Cursors.Default;
    }

    /// <summary>Commits the finished drag: logical client corners → physical px (the DWM-side coordinate space —
    /// <see cref="ClientScale"/> is shared with <see cref="MirrorHostPhysical"/> so both sides of the mapping
    /// agree), then <see cref="MirrorSurface.SetRegionFromDrag"/> maps dest → source and applies the crop.
    /// A stray click (&lt;4px source span) leaves the current region unchanged.</summary>
    private void CommitRegionDrag()
    {
        _regionSelecting = false;
        _regionDragging = false;
        Cursor = Cursors.Default;

        (double sx, double sy) = ClientScale();
        bool applied = _mirror.SetRegionFromDrag(
            (int)Math.Round(_dragStartClient.X * sx), (int)Math.Round(_dragStartClient.Y * sy),
            (int)Math.Round(_dragCurrentClient.X * sx), (int)Math.Round(_dragCurrentClient.Y * sy));

        if (applied)
        {
            SyncPanels();
            Invalidate(_mirrorRect);
        }
    }

    private Rectangle DragScreenRect()
    {
        Point a = PointToScreen(_dragStartClient);
        Point b = PointToScreen(_dragCurrentClient);
        return Rectangle.FromLTRB(
            Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Max(a.X, b.X), Math.Max(a.Y, b.Y));
    }

    private void EraseDragFrame()
    {
        if (_dragFrame != Rectangle.Empty)
        {
            ControlPaint.DrawReversibleFrame(_dragFrame, Color.White, FrameStyle.Dashed); // XOR — same call erases
            _dragFrame = Rectangle.Empty;
        }
    }

    private void OnRegionSaveRequested(object? sender, EventArgs e)
    {
        if (_mirror.CurrentRegion is not Sumbo.Core.Region region)
            return;

        string? name = PromptForName(_loc.Get(LocKeys.Prompt_RegionName_Title), _loc.Get(LocKeys.Prompt_RegionName_Default));
        if (string.IsNullOrWhiteSpace(name))
            return;

        try
        {
            List<NamedRegion> list = _manager.Regions.Load().Where(r => r.Name != name).ToList(); // same-name save overwrites
            list.Add(new NamedRegion(name, region));
            _manager.Regions.Save(list);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, _loc.Get(LocKeys.Dialog_RegionSaveFailed_Caption), MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        RefreshRegionList();
    }

    private void OnRegionDeleteRequested(object? sender, NamedRegion named)
    {
        if (!ConfirmDelete(named.Name))
            return;

        try
        {
            _manager.Regions.Save(_manager.Regions.Load().Where(r => r.Name != named.Name).ToList());
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, _loc.Get(LocKeys.Dialog_RegionSaveFailed_Caption), MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        RefreshRegionList();
    }

    private void RefreshRegionList() => _regionPanel.SetRegions(_manager.Regions.Load());

    // ── Profiles — panel intents → capture/restore + store ───────────────

    private void OnProfileSaveRequested(object? sender, EventArgs e)
    {
        if (!_mirror.HasMirror)
            return;

        string? name = PromptForName(_loc.Get(LocKeys.Prompt_ProfileName_Title), _loc.Get(LocKeys.Prompt_ProfileName_Default));
        if (string.IsNullOrWhiteSpace(name))
            return;

        try
        {
            _manager.Profiles.Upsert(CaptureProfile(name));
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, _loc.Get(LocKeys.Dialog_ProfileSaveFailed_Caption), MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        RefreshProfileList();
    }

    /// <summary>Snapshots the embedded mirror + shell into a <see cref="Profile"/> — placement = MAIN-window
    /// bounds; the target spec carries the captured-identity fields (<see cref="CurrentTargetSpec"/>) so
    /// <see cref="WindowMatcher"/>'s resolution chain keeps working after the source window is gone.</summary>
    private Profile CaptureProfile(string name)
    {
        return new Profile
        {
            Id = "p_" + Guid.NewGuid().ToString("N")[..8],
            Name = name,
            Target = CurrentTargetSpec(), // durable spec for the current mirror source (shared with group members)
            Region = ProfileRegion.FromRegion(_mirror.CurrentRegion),
            Placement = new Placement
            {
                Monitor = Array.IndexOf(Screen.AllScreens, Screen.FromControl(this)),
                Anchor = _anchor,
                X = Bounds.X,
                Y = Bounds.Y,
                Width = Bounds.Width,
                Height = Bounds.Height,
            },
            Opacity = _mirror.OpacityPercent,
            ClickThrough = _clickThrough,
            ShowBorder = _showMirrorBorder,
            AlwaysOnTop = _alwaysOnTop,
        };
    }

    /// <summary>Restores a profile onto the embedded mirror: resolve via <see cref="WindowMatcher"/> → notice when
    /// unmatched → fresh <c>Start</c> (a failed start leaves the current mirror unchanged) → region + opacity +
    /// border + placement/anchor + always-on-top, and click-through LAST (OFF strips a stale style, ON stays
    /// guarded), then one settled panel push.</summary>
    private void OnProfileApplyRequested(object? sender, Profile profile)
    {
        IReadOnlyList<WindowInfo> windows;
        try
        {
            windows = WindowEnumerator.GetCloneableWindows();
        }
        catch (Exception)
        {
            windows = Array.Empty<WindowInfo>();
        }

        WindowInfo? target = WindowMatcher.Resolve(profile.Target, windows);
        if (target is null)
        {
            MessageBox.Show(
                this,
                _loc.Format(LocKeys.Dialog_ProfileRestore_Body, profile.Name),
                _loc.Get(LocKeys.Dialog_ProfileRestore_Caption),
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }

        if (!_mirror.Start(Handle, target, out string? error))
        {
            SyncPanels();
            MessageBox.Show(
                this,
                error ?? _loc.Get(LocKeys.Dialog_CloneFailed_Body),
                _loc.Get(LocKeys.Dialog_CloneFailed_Caption),
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            return;
        }

        _mirror.SetRegion(profile.Region?.ToRegion()); // Start() begins unclipped — restore the saved crop after it
        _mirror.SetOpacity(profile.Opacity);
        _showMirrorBorder = profile.ShowBorder;
        ApplyProfilePlacement(profile.Placement); // placement + anchor, clamped into the resolved monitor
        SetAlwaysOnTop(profile.AlwaysOnTop);
        SetClickThrough(profile.ClickThrough);    // last — OFF strips a stale style, ON stays guarded
        ApplyVisualState();                       // settle the opacity channels (overlay/normal)
        SyncPanels();
        Invalidate(_mirrorRect);
    }

    /// <summary>Positions the window per a saved <see cref="Placement"/>, clamped into the resolved monitor.
    /// Restoring saved bounds means free size — the size preset is cleared.</summary>
    private void ApplyProfilePlacement(Placement placement)
    {
        if (WindowState != FormWindowState.Normal)
            WindowState = FormWindowState.Normal; // placement doesn't apply while maximized — return to Normal first

        Screen screen = ResolveScreen(placement);
        Rectangle wa = screen.WorkingArea;

        int w = Math.Max(MinimumSize.Width, Math.Min(placement.Width, wa.Width));
        int h = Math.Max(MinimumSize.Height, Math.Min(placement.Height, wa.Height));
        int x = Math.Clamp(placement.X, wa.Left, Math.Max(wa.Left, wa.Right - w));
        int y = Math.Clamp(placement.Y, wa.Top, Math.Max(wa.Top, wa.Bottom - h));

        _suppressPlacementEvents = true;
        try { Bounds = new Rectangle(x, y, w, h); }
        finally { _suppressPlacementEvents = false; }

        _sizeMode = null;
        _anchor = placement.Anchor;
        ReapplyAnchor();
    }

    private static Screen ResolveScreen(Placement placement)
    {
        Screen[] screens = Screen.AllScreens;
        if (placement.Monitor >= 0 && placement.Monitor < screens.Length)
            return screens[placement.Monitor];

        // Saved monitor index no longer exists (display layout changed): prefer a screen intersecting
        // the saved bounds, else the primary (clamping happens in ApplyProfilePlacement).
        var saved = new Rectangle(placement.X, placement.Y, Math.Max(1, placement.Width), Math.Max(1, placement.Height));
        foreach (Screen s in screens)
            if (s.Bounds.IntersectsWith(saved))
                return s;
        return Screen.PrimaryScreen ?? screens[0];
    }

    private void OnProfileDeleteRequested(object? sender, Profile profile)
    {
        if (!ConfirmDelete(profile.Name))
            return;

        try
        {
            ProfilesFile file = _manager.Profiles.Load();
            _manager.Profiles.Save(file with { Profiles = file.Profiles.Where(p => p.Id != profile.Id).ToList() });
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, _loc.Get(LocKeys.Dialog_ProfileSaveFailed_Caption), MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        RefreshProfileList();
    }

    private void RefreshProfileList() => _profilesPanel.SetProfiles(_manager.Profiles.Load().Profiles);

    private bool ConfirmDelete(string name)
        => MessageBox.Show(
            this,
            _loc.Format(LocKeys.Main_ConfirmDelete_Body, name),
            _loc.Get(LocKeys.Main_ConfirmDelete_Caption),
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning) == DialogResult.Yes;

    /// <summary>Modal name prompt. TopMost so it can never render behind an always-on-top surface and look like a
    /// hang while blocked on an invisible modal.</summary>
    private string? PromptForName(string title, string initial)
    {
        using var dialog = new Form
        {
            Text = title,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            StartPosition = FormStartPosition.CenterParent,
            ClientSize = new Size(300, 96),
            MinimizeBox = false,
            MaximizeBox = false,
            ShowInTaskbar = false,
            TopMost = true,
        };
        var input = new TextBox { Left = 12, Top = 14, Width = 276, Text = initial };
        var ok = new Button { Text = _loc.Get(LocKeys.Common_Ok), DialogResult = DialogResult.OK, Left = 132, Top = 52, Width = 72 };
        var cancel = new Button { Text = _loc.Get(LocKeys.Common_Cancel), DialogResult = DialogResult.Cancel, Left = 216, Top = 52, Width = 72 };
        dialog.Controls.AddRange(new Control[] { input, ok, cancel });
        dialog.AcceptButton = ok;
        dialog.CancelButton = cancel;
        input.SelectAll();

        return dialog.ShowDialog(this) == DialogResult.OK ? input.Text.Trim() : null;
    }

    // ── Global hotkeys ────────────────────────────────────────────────────

    /// <summary>Global hotkey semantics on the single window: ToggleVisible hides to / restores from the tray,
    /// PickWindow opens + refreshes the targets panel, and Opacity± steps the embedded mirror through the same
    /// <see cref="ApplyOpacity"/> route as the panel slider. RegionSelect/GroupSwitch/ClickThrough act on the
    /// embedded mirror.</summary>
    private void OnHotkeyRouted(object? sender, HotkeyAction action)
    {
        switch (action)
        {
            case HotkeyAction.ToggleVisible:
                ToggleTraySurface();
                break;

            case HotkeyAction.PickWindow:
                RestoreFromTray(); // clears click-through/overlay, restores from tray/minimized, activates
                SetActivePanel(PanelTargets);
                _targetsPanel.ReloadTargets();
                _targetsPanel.FocusSearch();
                break;

            case HotkeyAction.ClickThrough:
                SetClickThrough(!_clickThrough); // entry and exit both go through the single route
                break;

            case HotkeyAction.OpacityUp:
                if (_mirror.HasMirror) ApplyOpacity(_mirror.OpacityPercent + 10);
                break;

            case HotkeyAction.OpacityDown:
                if (_mirror.HasMirror) ApplyOpacity(_mirror.OpacityPercent - 10);
                break;

            case HotkeyAction.RegionSelect:
                EnterRegionSelect(); // no-op without a mirror
                break;

            case HotkeyAction.GroupSwitch:
                ToggleGroupSwitch(); // gated on empty group / no mirror
                break;
        }
    }

    /// <summary>Tray surface routing — show/hide shares the hotkey's tray-toggle meaning; double-click and
    /// settings restore only (a visible window must not hide on a "restore" gesture).</summary>
    private void OnSurfaceRequested(object? sender, SurfaceRequest request)
    {
        switch (request)
        {
            case SurfaceRequest.ToggleVisible:
                ToggleTraySurface();
                break;

            case SurfaceRequest.Restore:
                RestoreFromTray();
                break;

            case SurfaceRequest.OpenSettings:
                RestoreFromTray();
                SetActivePanel(PanelSettings);
                break;
        }
    }

    // ── Tray residency (close/minimize hide to tray · single restore path) ──

    /// <summary>Show/hide toggle (hotkey + tray menu): visible → tray, tray-hidden/minimized → full restore.
    /// Explicit user intent, so no residency balloon.</summary>
    private void ToggleTraySurface()
    {
        if (!Visible || WindowState == FormWindowState.Minimized)
            RestoreFromTray();
        else
            HideToTray(notice: false);
    }

    /// <summary>Retires the window to the tray — <see cref="Control.Hide"/> drops the taskbar button, and the
    /// always-alive tray icon is the way back. Mirror/group/panel state is deliberately left untouched (hiding is
    /// not a lifecycle transition, same meaning as minimize). The first close-to-tray shows a one-time balloon via
    /// the manager → tray icon so the user knows the app did not exit.</summary>
    private void HideToTray(bool notice)
    {
        if (!Visible)
            return;
        Hide();
        if (notice && !_trayNoticeShown)
        {
            _trayNoticeShown = true;
            _manager.RequestTrayResidencyNotice();
        }
    }

    /// <summary>The single way back from the tray/minimized state: clears click-through/overlay first (a tray
    /// restore must return a fully usable window), shows the window again (tray-hidden = <c>Visible=false</c>),
    /// returns a Minimized state to Normal, activates, and re-pushes the mirror layout (the DWM destination was
    /// hidden). No-op parts are safe when already visible, so global hotkeys route here unconditionally.</summary>
    private void RestoreFromTray()
    {
        if (_clickThrough)
            SetClickThrough(false);
        else if (_uiHidden)
            SetOverlayMode(false);
        if (!Visible)
            Show();
        if (WindowState == FormWindowState.Minimized)
            WindowState = FormWindowState.Normal;
        Activate();
        _mirror.UpdateLayout(MirrorHostPhysical()); // re-fit — guards against layout drift while the destination was hidden
    }

    /// <summary>Real application exit (tray exit command): bypasses the close-to-tray policy in
    /// <see cref="OnFormClosing"/> so the close still funnels through <c>FormClosed</c> →
    /// <c>SumboAppContext.ExitApp</c> (single exit path).</summary>
    internal void CloseForExit()
    {
        _exitRequested = true;
        Close();
    }

    /// <summary>A user close GESTURE (chrome X · Alt+F4 · system menu/taskbar close = SC_CLOSE) retires to the
    /// tray instead of exiting. Everything else — a raw external <c>WM_CLOSE</c> (taskkill/scripts), Windows
    /// shutdown/logoff, task manager, <see cref="CloseForExit"/> — keeps closing for real, so automation and the
    /// OS can always end the process gracefully. The gesture flag (not the form's <c>CloseReason</c>) is the
    /// discriminator: a cancelled close leaves <c>CloseReason.UserClosing</c> behind, which would misroute the
    /// next raw <c>WM_CLOSE</c> to the tray.</summary>
    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        base.OnFormClosing(e);
        bool gesture = _userCloseGesture;
        _userCloseGesture = false; // consume once — must not contaminate the next close decision
        if (!e.Cancel && gesture && e.CloseReason == CloseReason.UserClosing && !_exitRequested)
        {
            e.Cancel = true;
            HideToTray(notice: true);
        }
    }

    // ── Group cycling — rotates the mirror source through saved targets ──

    private void OnGroupAddRequested(object? sender, EventArgs e)
    {
        if (!_mirror.HasMirror)
            return;
        _group.Add(CurrentTargetSpec());
        ReflectGroup();
    }

    private void OnGroupClearRequested(object? sender, EventArgs e)
    {
        _group.Clear();
        _groupTimer.Stop();
        ReflectGroup();
    }

    private void OnGroupToggleRunRequested(object? sender, EventArgs e) => ToggleGroupSwitch();

    private void OnGroupIntervalChanged(object? sender, int seconds)
    {
        _group.SetInterval(seconds);
        if (_group.IsRunning)
            _groupTimer.Interval = _group.IntervalSeconds * 1000; // apply the new interval immediately while cycling
        ReflectGroup();
    }

    /// <summary>Start/stop toggle for group cycling. Starting requires a live mirror (something to rotate); an
    /// empty group gets a notice, no mirror is a silent no-op (the panel button is gated, the hotkey path is not).</summary>
    private void ToggleGroupSwitch()
    {
        if (_group.IsRunning)
        {
            StopGroupSwitch();
            return;
        }

        if (_group.Count <= 1) // GroupSwitcher.Start refuses a single member too — this branch adds the notice
        {
            MessageBox.Show(
                this,
                _loc.Get(_group.Count == 0 ? LocKeys.Dialog_GroupEmpty_Body : LocKeys.Dialog_GroupSingle_Body),
                _loc.Get(LocKeys.Dialog_GroupSwitch_Caption),
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }

        if (!_mirror.HasMirror)
            return;

        _groupMissingWarned = false;
        _groupMissCount = 0;
        _group.Start();
        _groupTimer.Interval = _group.IntervalSeconds * 1000;
        _groupTimer.Start();
        ReflectGroup();
    }

    private void StopGroupSwitch()
    {
        _group.Stop();
        _groupTimer.Stop();
        ReflectGroup();
    }

    /// <summary>Rotates the embedded mirror's source to the next group member every N seconds. Skipped during a
    /// region drag (retargeting would remap the in-flight drag onto a different source); an unresolved member gets
    /// one notice then is skipped, and a full lap of misses stops the cycle. Retargeting preserves region/opacity
    /// (<see cref="MirrorSurface.RetargetPreserving"/>).</summary>
    private void OnGroupTick(object? sender, EventArgs e)
    {
        if (_regionSelecting || _regionDragging)
            return;

        TargetSpec? next = _group.Next();
        if (next is null)
        {
            StopGroupSwitch(); // empty group — nothing to rotate
            return;
        }

        IReadOnlyList<WindowInfo> windows;
        try
        {
            windows = WindowEnumerator.GetCloneableWindows();
        }
        catch (Exception)
        {
            return; // transient enumeration failure — retry next tick
        }

        WindowInfo? target = WindowMatcher.Resolve(next, windows);
        if (target is null)
        {
            _groupMissCount++;
            if (_groupMissCount >= _group.Count)
            {
                StopGroupSwitch(); // every member missed for a full lap — stop instead of spinning idle
                _groupMissCount = 0;
                MessageBox.Show(
                    this,
                    _loc.Get(LocKeys.Dialog_GroupAllMissing_Body),
                    _loc.Get(LocKeys.Dialog_GroupSwitch_Caption),
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                return;
            }

            if (!_groupMissingWarned)
            {
                _groupMissingWarned = true;
                MessageBox.Show(
                    this,
                    _loc.Format(LocKeys.Dialog_GroupMemberMissing_Body, next.CapturedTitle ?? next.Value),
                    _loc.Get(LocKeys.Dialog_GroupSwitch_Caption),
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
            }
            return; // partial miss — skip this member, keep cycling
        }

        _groupMissCount = 0; // a resolved member resets the full-lap miss counter
        if (_mirror.RetargetPreserving(target, out _)) // success → Changed → OnMirrorChanged → SyncPanels (target label refresh)
            _groupMissingWarned = false;               // a successful hop re-arms the one-time notice
    }

    /// <summary>Builds a durable target spec for the current mirror source (group members and profile targets
    /// share this shape).</summary>
    private TargetSpec CurrentTargetSpec()
    {
        WindowInfo id = WindowEnumerator.Describe(_mirror.TargetHandle);
        return new TargetSpec
        {
            MatchBy = MatchBy.Title,
            Value = id.Title,
            CapturedTitle = string.IsNullOrEmpty(id.Title) ? null : id.Title,
            CapturedProcessName = string.IsNullOrEmpty(id.ProcessName) ? null : id.ProcessName,
            CapturedClassName = string.IsNullOrEmpty(id.ClassName) ? null : id.ClassName,
        };
    }

    /// <summary>Full group reflect (membership/run/interval change or panel entry) — maps member specs to display
    /// titles and pushes them to the group panel.</summary>
    private void ReflectGroup()
    {
        var titles = new List<string>(_group.Count);
        foreach (TargetSpec spec in _group.Members)
            titles.Add(spec.CapturedTitle ?? spec.Value);
        _groupPanel.ReflectGroup(_mirror.HasMirror, titles, _group.IsRunning, _group.IntervalSeconds);
    }

    // ── Settings panel — the shell brokers CloneManager's settings state ──

    private void OnSettingsLanguageSelected(object? sender, string language)
    {
        _manager.SetLanguage(language);                   // LanguageChanged → ApplyStrings fan-out re-labels every panel
        _settingsPanel.ReflectSettings(_manager.Current); // re-assert the segment selection after relabeling
    }

    private void OnSettingsStartWithWindowsToggled(object? sender, bool on)
    {
        _manager.SetStartWithWindows(on);                 // the request can be denied by policy — reflect the authoritative state back
        _settingsPanel.ReflectSettings(_manager.Current);
    }

    private void OnSettingsMinimizeToTrayToggled(object? sender, bool on)
    {
        _manager.SetMinimizeToTray(on);
        _settingsPanel.ReflectSettings(_manager.Current);
    }

    // ── Layout ───────────────────────────────────────────────────────────

    protected override void OnLayout(LayoutEventArgs levent)
    {
        base.OnLayout(levent);
        DoLayout();
    }

    private void DoLayout()
    {
        int w = ClientSize.Width, h = ClientSize.Height;
        if (w <= 0 || h <= 0) return;

        if (_uiHidden)
        {
            // Overlay: the whole client is the mirror canvas — no rail/panel/title, no chrome-button hit zones.
            _btnMin = _btnMax = _btnClose = Rectangle.Empty;
            _mirrorRect = new Rectangle(0, 0, w, h);
            _mirror.UpdateLayout(MirrorHostPhysical());
            Invalidate();
            return;
        }

        int bodyTop = TitleH;
        int bodyBottom = h - Grip;
        int bodyH = Math.Max(0, bodyBottom - bodyTop);

        // Rail (far right) + option panel to its left
        int railX = w - Grip - Theme.IconRailWidth;
        _rail.SetBounds(railX, bodyTop, Theme.IconRailWidth, bodyH);

        int contentRight;
        if (_sideOpen)
        {
            int sideX = railX - Theme.SidePanelWidth;
            _sidePanel.SetBounds(sideX, bodyTop, Theme.SidePanelWidth, bodyH);
            LayoutSide(_sidePanel.ClientSize.Width, _sidePanel.ClientSize.Height);
            contentRight = sideX;
        }
        else
        {
            contentRight = railX;
        }

        // Everything left of the panel is the mirror canvas — bare form surface, no child controls.
        int mx = Grip + Theme.Pad;
        int my = bodyTop + Theme.Pad;
        _mirrorRect = new Rectangle(
            mx, my,
            Math.Max(0, contentRight - Theme.Pad - mx),
            Math.Max(0, bodyBottom - Theme.Pad - my));
        _mirror.UpdateLayout(MirrorHostPhysical()); // re-fit on resize / DPI / panel expand-collapse

        LayoutWindowButtons(w);
        Invalidate(); // repaint the title bar + canvas frame
    }

    private void LayoutWindowButtons(int w)
    {
        const int bw = 46, bh = 34;
        int y = (TitleH - bh) / 2;
        _btnClose = new Rectangle(w - Grip - bw, y, bw, bh);
        _btnMax = new Rectangle(_btnClose.X - bw, y, bw, bh);
        _btnMin = new Rectangle(_btnMax.X - bw, y, bw, bh);
    }

    private void LayoutSide(int w, int h)
    {
        int pad = Theme.Pad + 2;
        _panelTitle.SetBounds(pad, pad, w - pad * 2 - 32, 28);
        _panelClose.SetBounds(w - pad - 28, pad, 28, 28);

        int contentTop = pad + 28 + 10;
        var content = new Rectangle(0, contentTop, w, Math.Max(0, h - contentTop));
        foreach (PanelView view in _allPanels)
            view.Bounds = content; // every view shares the content rect; each lays out its children in OnLayout
    }

    /// <summary>
    /// Maps the logical <see cref="_mirrorRect"/> (minus the drawn frame margin) to a physical-pixel host rect for
    /// the DWM <c>rcDestination</c> (DWM wants physical client coordinates). <see cref="MirrorSurface"/> letterboxes
    /// the source inside this rect.
    /// </summary>
    private RECT MirrorHostPhysical()
    {
        (double sx, double sy) = ClientScale();

        int px = (int)Math.Round(_mirrorRect.X * sx);
        int py = (int)Math.Round(_mirrorRect.Y * sy);
        int pw = (int)Math.Round(_mirrorRect.Width * sx);
        int ph = (int)Math.Round(_mirrorRect.Height * sy);

        // Overlay is frameless — the thumbnail runs edge-to-edge; normal mode keeps the drawn frame visible.
        int margin = _uiHidden ? 0 : Math.Max(2, (int)Math.Round(BorderDip * sx));
        return new RECT(
            px + margin,
            py + margin,
            px + Math.Max(margin + 1, pw - margin),
            py + Math.Max(margin + 1, ph - margin));
    }

    /// <summary>Logical-client → physical-pixel scale (GetClientRect vs ClientSize). Shared by the DWM host rect
    /// and the region-drag commit so both sides of the dest-coordinate mapping agree.</summary>
    private (double Sx, double Sy) ClientScale()
    {
        int physW = ClientSize.Width, physH = ClientSize.Height;
        if (IsHandleCreated && User32.GetClientRect(Handle, out RECT rc) && rc.Width > 0 && rc.Height > 0)
        {
            physW = rc.Width;
            physH = rc.Height;
        }
        return (physW / (double)Math.Max(1, ClientSize.Width), physH / (double)Math.Max(1, ClientSize.Height));
    }

    // ── Strings ──────────────────────────────────────────────────────────

    private void OnLanguageChanged(object? sender, EventArgs e) => ApplyStrings();

    private void ApplyStrings()
    {
        Text = _loc.Get(LocKeys.App_Title);
        _panelTitle.Text = PanelTitle(_activePanelId);
        foreach (PanelView view in _allPanels)
            view.ApplyStrings(_loc);
        _rail.UpdateTooltips(RailItems());

        // Context-menu labels reuse the panel vocabulary (same localization keys).
        _ctxRestoreUi.Text = _loc.Get(LocKeys.Menu_RestoreUi);
        _ctxTarget.Text = _loc.Get(LocKeys.Menu_Target);
        _ctxStop.Text = _loc.Get(LocKeys.Main_StopMirror);
        _ctxRegion.Text = _loc.Get(LocKeys.Menu_Region_Select);
        _ctxForward.Text = _loc.Get(LocKeys.Menu_Mode_ClickForward);
        _ctxThrough.Text = _loc.Get(LocKeys.Menu_Mode_ClickThrough);
        _ctxSettings.Text = _loc.Get(LocKeys.Menu_Settings);

        DoLayout();
    }

    private static string PickWindowChord()
    {
        foreach (HotkeyBinding b in HotkeyService.Defaults)
            if (b.Action == HotkeyAction.PickWindow) return b.Display;
        return "";
    }

    // ── Painting ─────────────────────────────────────────────────────────

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        Graphics g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

        if (_uiHidden)
        {
            using var bg = new SolidBrush(Theme.InsetBg);
            g.FillRectangle(bg, ClientRectangle); // frameless — the thumbnail covers the whole client edge-to-edge
            return;
        }

        // Logo tile + brand
        var logo = new Rectangle(Grip + 14, (TitleH - 30) / 2, 30, 30);
        Theme.FillRounded(g, logo, 8, Theme.Accent);
        using (var lb = new SolidBrush(Theme.TextOnAccent))
        using (var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
            g.DrawString("S", Theme.Brand, lb, logo, sf);
        using (var tb = new SolidBrush(Theme.TextPrimary))
            g.DrawString("Sumbo", Theme.Brand, tb, new PointF(logo.Right + 10, TitleH / 2f - Theme.Brand.Height / 2f));

        DrawWindowButton(g, _btnMin, Glyph.Minimize, 0);
        DrawWindowButton(g, _btnMax, WindowState == FormWindowState.Maximized ? Glyph.Restore : Glyph.Maximize, 1);
        DrawWindowButton(g, _btnClose, Glyph.ChromeClose, 2);

        DrawMirrorArea(g);
    }

    /// <summary>Paints the mirror canvas frame on the FORM surface (no child control here — the DWM thumbnail
    /// composites over children). With a live mirror the thumbnail composites over the inner area, leaving the
    /// accent frame visible around it; when idle a hint tells the user to pick a target from the panel (or the
    /// PickWindow hotkey). The ring honors the border toggle — visual only, the reserved margin stays.</summary>
    private void DrawMirrorArea(Graphics g)
    {
        if (_mirrorRect.Width <= 0 || _mirrorRect.Height <= 0)
            return;

        Theme.FillRounded(g, _mirrorRect, Theme.CardRadius, Theme.InsetBg);
        if (_showMirrorBorder)
            Theme.DrawRounded(g, _mirrorRect, Theme.CardRadius, _mirror.HasMirror ? Theme.Accent : Theme.CardBorder);

        if (!_mirror.HasMirror)
        {
            using var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
            using var br = new SolidBrush(Theme.TextMuted);
            g.DrawString(_loc.Format(LocKeys.Main_Mirror_Hint, PickWindowChord()), Theme.Body, br, _mirrorRect, sf);
        }
    }

    private void DrawWindowButton(Graphics g, Rectangle r, string glyph, int index)
    {
        if (_hoverBtn == index)
        {
            Color hover = index == 2 ? Color.FromArgb(232, 17, 35) : Theme.CardBgHover;
            Theme.FillRounded(g, r, 6, hover);
        }
        Color fg = _hoverBtn == index && index == 2 ? Color.White : Theme.TextSecondary;
        using var brush = new SolidBrush(fg);
        using var iconFont = Theme.IconFont(10f);
        using var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
        g.DrawString(glyph, iconFont, brush, r, sf);
    }

    // ── Chrome interaction (drag / resize / window buttons) ───────────────

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (_regionDragging)
        {
            // Rubber band: erase the previous XOR frame, then draw at the new extent.
            EraseDragFrame();
            _dragCurrentClient = e.Location;
            _dragFrame = DragScreenRect();
            ControlPaint.DrawReversibleFrame(_dragFrame, Color.White, FrameStyle.Dashed);
            return;
        }

        // Forwarding ON + over the mirror: moves go to the source too (the button flag follows the real mouse state).
        if (TryForward(User32.WM_MOUSEMOVE, e,
                new IntPtr((MouseButtons & MouseButtons.Left) != 0 ? MK_LBUTTON : 0)))
            return;

        int hit = _btnClose.Contains(e.Location) ? 2 : _btnMax.Contains(e.Location) ? 1 : _btnMin.Contains(e.Location) ? 0 : -1;
        if (hit != _hoverBtn) { _hoverBtn = hit; Invalidate(new Rectangle(_btnMin.X, 0, ClientSize.Width - _btnMin.X, TitleH)); }
        base.OnMouseMove(e);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        if (_hoverBtn != -1) { _hoverBtn = -1; Invalidate(new Rectangle(_btnMin.X, 0, ClientSize.Width - _btnMin.X, TitleH)); }
        base.OnMouseLeave(e);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left)
        {
            if (_btnClose.Contains(e.Location)) { _userCloseGesture = true; Close(); return; } // chrome X = user close gesture
            if (_btnMax.Contains(e.Location)) { ToggleMaximize(); return; }
            if (_btnMin.Contains(e.Location)) { WindowState = FormWindowState.Minimized; return; }

            // Armed select mode + press inside the mirror canvas = start the crop drag. Only the form surface
            // gets here (children swallow their own mouse), so the rect check is the whole gate.
            if (_regionSelecting && _mirrorRect.Contains(e.Location))
            {
                _regionDragging = true;
                _dragStartClient = e.Location;
                _dragCurrentClient = e.Location;
                _dragFrame = Rectangle.Empty;
                return;
            }

            // Forwarding ON + left click over the mirror = post to the source (region selection above takes priority).
            if (TryForward(User32.WM_LBUTTONDOWN, e, new IntPtr(MK_LBUTTON)))
                return;
        }
        base.OnMouseDown(e);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        if (_regionDragging && e.Button == MouseButtons.Left)
        {
            EraseDragFrame();
            CommitRegionDrag();
            return;
        }
        if (e.Button == MouseButtons.Left && TryForward(User32.WM_LBUTTONUP, e, IntPtr.Zero))
            return;

        // Right click over a live mirror = context menu. During click-through the window receives no input, and an
        // in-flight region select/drag takes priority; the idle canvas points at the targets panel instead.
        if (e.Button == MouseButtons.Right
            && _mirror.HasMirror && _mirrorRect.Contains(e.Location)
            && !_clickThrough && !_regionSelecting && !_regionDragging)
        {
            _mirrorMenu.Show(this, e.Location);
            return;
        }
        base.OnMouseUp(e);
    }

    protected override void OnMouseDoubleClick(MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left && TryForward(User32.WM_LBUTTONDBLCLK, e, new IntPtr(MK_LBUTTON)))
            return;
        base.OnMouseDoubleClick(e);
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        // Forwarding ON: the wheel goes to the source too — even when the mapping fails in the letterbox margin,
        // consume it rather than scrolling locally.
        if (_clickForward && _mirror.HasMirror && _mirrorRect.Contains(e.Location))
        {
            TryForward(User32.WM_MOUSEWHEEL, e, new IntPtr(e.Delta << 16), wheel: true);
            return;
        }
        base.OnMouseWheel(e);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        // ESC bails out of an armed / in-flight region selection (KeyPreview=true routes it here).
        if (e.KeyCode == Keys.Escape && (_regionSelecting || _regionDragging))
        {
            CancelRegionSelect();
            e.Handled = true;
            return;
        }

        // ESC restores the UI from overlay / click-through — during click-through it only works while focus
        // remains; the reliable exits are the global hotkey and tray restore.
        if (e.KeyCode == Keys.Escape && (_clickThrough || _uiHidden))
        {
            if (_clickThrough)
                SetClickThrough(false); // the single route also restores the UI
            else
                SetOverlayMode(false);
            e.Handled = true;
            return;
        }
        base.OnKeyDown(e);
    }

    private void ToggleMaximize()
    {
        if (_locked)
            return; // lock also blocks the chrome-button path (paired with the WM_SYSCOMMAND swallow)
        WindowState = WindowState == FormWindowState.Maximized ? FormWindowState.Normal : FormWindowState.Maximized;
    }

    protected override void OnResize(EventArgs e)
    {
        // Keep a maximized borderless window off the taskbar (respect the work area).
        MaximizedBounds = Screen.FromControl(this).WorkingArea;
        base.OnResize(e);

        if (WindowState != _lastWindowState)
        {
            bool enteredMinimized = WindowState == FormWindowState.Minimized;
            _lastWindowState = WindowState; // minimize/maximize/restore transition → reflect the fullscreen segment
            SyncPanels();

            // Minimize-to-tray setting; off keeps the plain taskbar minimize. An explicit setting drives this
            // path, so no residency balloon (that is reserved for the close gesture).
            if (enteredMinimized && Visible && _manager.MinimizeToTray)
                HideToTray(notice: false);
        }
    }

    protected override void OnClientSizeChanged(EventArgs e)
    {
        base.OnClientSizeChanged(e);
        // Only a user resize clears the preset — internal (suppressed) changes are excluded, as are minimize
        // (an OS shrink) and maximize (the fullscreen segment reflects separately).
        if (_suppressPlacementEvents || WindowState != FormWindowState.Normal || _sizeMode is null)
            return;
        _sizeMode = null;
        SyncPanels(); // reflect the preset→free transition once, not per WM_SIZE
    }

    protected override void OnMove(EventArgs e)
    {
        base.OnMove(e);
        if (_uiHidden)
            PositionOverlayGrip(); // the dot rides the overlay's top-left corner
        // A user move clears the anchor. Maximize transitions also change Location, hence the Normal-only gate.
        if (_suppressPlacementEvents || _anchor is null || WindowState != FormWindowState.Normal)
            return;
        _anchor = null;
        SyncPanels();
    }

    protected override void OnVisibleChanged(EventArgs e)
    {
        base.OnVisibleChanged(e);
        // Tray-hide does not hide owned windows — the dot must follow the overlay out of and back into view.
        if (!Visible)
            HideOverlayGrip();
        else if (_uiHidden)
            ShowOverlayGrip();
    }

    protected override void OnResizeEnd(EventArgs e)
    {
        base.OnResizeEnd(e);
        ReapplyAnchor(); // re-align to the anchor after a resize ends (a move end is a no-op — OnMove already cleared the anchor)
    }

    protected override void WndProc(ref Message m)
    {
        // A second launch broadcasts this registered message instead of starting a rival process: surface the
        // existing window (it may be tray-resident or minimized) so the click feels like it focused the app.
        if (m.Msg != 0 && m.Msg == _showInstanceMsg)
        {
            RestoreFromTray();
            return;
        }

        // Mark a user close gesture (Alt+F4 / system menu / taskbar close = SC_CLOSE) here. WinForms' CloseReason
        // is order-dependent — a cancelled close leaves UserClosing behind and contaminates a following raw
        // WM_CLOSE — so this flag is the deterministic discriminator.
        if (m.Msg == User32.WM_SYSCOMMAND && (int)(m.WParam.ToInt64() & 0xFFF0) == ScClose)
            _userCloseGesture = true;

        // Position/size lock: swallow user-initiated move/resize/maximize system commands. Internal (suppressed)
        // Bounds assignments do not go through SYSCOMMAND, so they are unaffected.
        if (_locked && m.Msg == User32.WM_SYSCOMMAND)
        {
            int command = (int)(m.WParam.ToInt64() & 0xFFF0);
            if (command == User32.SC_MOVE || command == User32.SC_SIZE || command == User32.SC_MAXIMIZE)
                return;
        }

        if (m.Msg == User32.WM_NCHITTEST)
        {
            base.WndProc(ref m);
            int ht = ChromeHitTest();
            if (ht != 0) m.Result = new IntPtr(ht);
            return;
        }

        // Overlay grip-resize keeps the shown content's ratio: the fitted window must not regrow a letterbox,
        // so the sizing rect is re-derived from the drag's driving axis. Overlay is frameless — the window IS
        // the canvas, so the ratio applies to the rect directly.
        if (m.Msg == User32.WM_SIZING && _uiHidden && _mirror.TryGetActiveSourceSize(out int ovW, out int ovH))
        {
            var r = Marshal.PtrToStructure<RECT>(m.LParam);
            double ratio = (double)ovW / ovH;
            int edge = (int)m.WParam.ToInt64();

            int w = Math.Max(OverlayMinSize.Width, r.Right - r.Left);
            int h = Math.Max(OverlayMinSize.Height, r.Bottom - r.Top);
            if (edge is User32.WMSZ_TOP or User32.WMSZ_BOTTOM)
                w = (int)Math.Round(h * ratio); // height drives
            else
                h = (int)Math.Round(w / ratio); // width drives (sides + corners)

            // Grow/shrink away from the fixed side: a left/top drag moves that edge, so anchor the opposite one.
            if (edge is User32.WMSZ_LEFT or User32.WMSZ_TOPLEFT or User32.WMSZ_BOTTOMLEFT)
                r.Left = r.Right - w;
            else
                r.Right = r.Left + w;
            if (edge is User32.WMSZ_TOP or User32.WMSZ_TOPLEFT or User32.WMSZ_TOPRIGHT)
                r.Top = r.Bottom - h;
            else
                r.Bottom = r.Top + h;

            Marshal.StructureToPtr(r, m.LParam, false);
            m.Result = new IntPtr(1); // handled — DefWindowProc uses the adjusted rect
            return;
        }

        // Moved to a different-DPI monitor. WinForms rescales the window on base.WndProc; re-run layout
        // afterward so the mirror rect + its physical rcDestination are recomputed for the new DPI.
        if (m.Msg == User32.WM_DPICHANGED)
        {
            // Suppress so the DPI rescale's OnClientSizeChanged/OnMove don't clear the preset/anchor;
            // try/finally guards against a stuck flag.
            try
            {
                _suppressPlacementEvents = true;
                base.WndProc(ref m);
            }
            finally
            {
                _suppressPlacementEvents = false;
            }
            DoLayout();
            if (_uiHidden)
                RefitOverlay(); // the preset path would re-add chrome overhead the overlay does not show
            else if (_sizeMode is ClientSizeMode mode && WindowState == FormWindowState.Normal)
                ApplyMirrorSizeMode(mode); // recompute against the new monitor's DPI/work area (includes ReapplyAnchor)
            else
                ReapplyAnchor();
            return;
        }

        base.WndProc(ref m);
    }

    private int ChromeHitTest()
    {
        Point p = PointToClient(Cursor.Position);
        int w = ClientSize.Width, h = ClientSize.Height;

        if (!_locked && WindowState != FormWindowState.Maximized) // lock removes the resize grips
        {
            bool l = p.X < Grip, r = p.X >= w - Grip, t = p.Y < Grip, b = p.Y >= h - Grip;
            if (t && l) return User32.HTTOPLEFT;
            if (t && r) return User32.HTTOPRIGHT;
            if (b && l) return User32.HTBOTTOMLEFT;
            if (b && r) return User32.HTBOTTOMRIGHT;
            if (l) return User32.HTLEFT;
            if (r) return User32.HTRIGHT;
            if (t) return User32.HTTOP;
            if (b) return User32.HTBOTTOM;
        }

        if (_uiHidden)
            return 0; // overlay: no title strip — moving requires restoring the UI first (grip resize stays)

        if (p.Y < TitleH)
        {
            if (_btnMin.Contains(p) || _btnMax.Contains(p) || _btnClose.Contains(p))
                return User32.HTCLIENT; // let the mouse handlers process the window buttons
            // Lock blocks caption drag and double-click maximize at the source.
            return _locked ? User32.HTCLIENT : User32.HTCAPTION;
        }
        return 0; // leave the default (HTCLIENT) for the body
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _loc.LanguageChanged -= OnLanguageChanged;
        _manager.HotkeyRouted -= OnHotkeyRouted;
        _manager.SurfaceRequested -= OnSurfaceRequested;
        _mirror.Changed -= OnMirrorChanged;
        _targetsPanel.TargetActivated -= OnTargetActivated;
        _targetsPanel.StopRequested -= OnStopRequested;
        _displayPanel.OpacityChangeRequested -= OnOpacityChangeRequested;
        _displayPanel.SizeModeSelected -= OnSizeModeSelected;
        _displayPanel.AnchorSelected -= OnAnchorSelected;
        _behaviorPanel.BorderToggleRequested -= OnBorderToggleRequested;
        _behaviorPanel.ClickForwardToggleRequested -= OnClickForwardToggleRequested;
        _behaviorPanel.ClickThroughToggleRequested -= OnClickThroughToggleRequested;
        _behaviorPanel.LockToggleRequested -= OnLockToggleRequested;
        _behaviorPanel.AotToggleRequested -= OnAotToggleRequested;
        _behaviorPanel.HideUiRequested -= OnHideUiRequested;
        _regionPanel.SelectRequested -= OnRegionSelectRequested;
        _regionPanel.ClearRequested -= OnRegionClearRequested;
        _regionPanel.SaveRequested -= OnRegionSaveRequested;
        _regionPanel.ApplyRequested -= OnRegionApplyRequested;
        _regionPanel.DeleteRequested -= OnRegionDeleteRequested;
        _profilesPanel.SaveRequested -= OnProfileSaveRequested;
        _profilesPanel.ApplyRequested -= OnProfileApplyRequested;
        _profilesPanel.DeleteRequested -= OnProfileDeleteRequested;
        _groupPanel.AddCurrentRequested -= OnGroupAddRequested;
        _groupPanel.ClearRequested -= OnGroupClearRequested;
        _groupPanel.ToggleRunRequested -= OnGroupToggleRunRequested;
        _groupPanel.IntervalChanged -= OnGroupIntervalChanged;
        _settingsPanel.LanguageSelected -= OnSettingsLanguageSelected;
        _settingsPanel.StartWithWindowsToggled -= OnSettingsStartWithWindowsToggled;
        _settingsPanel.MinimizeToTrayToggled -= OnSettingsMinimizeToTrayToggled;
        _aboutPanel.LinkActivated -= OnAboutLinkActivated;
        _groupTimer.Tick -= OnGroupTick;
        _groupTimer.Stop();
        _groupTimer.Dispose();
        _mirrorMenu.Dispose();
        _overlayGrip?.Dispose();
        _sourceWatch.Stop();
        _sourceWatch.Dispose();
        _mirror.Dispose(); // unregister the DWM thumbnail
        _appIcon.Dispose(); // this window owns the AppIcons.Load result — released after the form is torn down

        // Cards + icon cache are torn down by TargetsPanel.Dispose (form dispose chain) in ownership order:
        // cards (image referencers) first, provider (image owner) last — the shell no longer touches either.

        base.OnFormClosed(e);
    }
}
