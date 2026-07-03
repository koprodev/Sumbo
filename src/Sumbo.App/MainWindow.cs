using System;
using System.Collections.Generic;
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
/// The v2 single-window shell (체크리스트v2.md V2-A~D): the main window itself IS the mirror output — the whole center
/// is a child-free form-surface canvas hosting the DWM thumbnail (<see cref="MirrorSurface"/>), with a right icon
/// rail whose items switch the side panel between <see cref="PanelView"/>s. The shell owns the mirror and the V2-D
/// window-control state (크기 preset/앵커/클릭전달/클릭통과/잠금/AOT — v1 CloneForm 창 셸 절반의 재해석) and wires
/// panel events to them — panels are pure views. The UI 숨김 (overlay) mode collapses the chrome to a mirror-only
/// canvas; click-through forces it (Q3 확정) with Ctrl+Alt+C / tray restore / ESC as the exits.
/// Custom chrome (borderless title bar / window buttons / resize grips / per-monitor DPI) carries over from v1.
/// <para>
/// DWM 게이트 (v1 실측 승계): the thumbnail destination must be this top-level window's handle, and the thumbnail
/// composites OVER child controls — so no child control may intersect <see cref="_mirrorRect"/>. The side panel and
/// rail never overlap the canvas; expanding/collapsing the panel re-fits the mirror via <see cref="DoLayout"/>.
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
    private const string PanelRegion = "region";
    private const string PanelProfiles = "profiles";
    private const string PanelHotkeys = "hotkeys";
    private const string PanelGroup = "group";
    private const string PanelSettings = "settings";
    private const string PanelAbout = "about";

    /// <summary>Rail order — parallel to <see cref="RailItems"/>. All eight ids map to embedded panels
    /// ("settings" included — V2-E1 SettingsPanel 흡수).</summary>
    private static readonly string[] RailIds =
    {
        PanelTargets, PanelDisplay, PanelRegion, PanelProfiles, PanelHotkeys, PanelGroup, PanelSettings, PanelAbout,
    };

    private readonly IconRail _rail = new();
    private readonly Panel _sidePanel = new();
    private bool _sideOpen = true;
    private string _activePanelId = PanelTargets;

    // shared panel header
    private readonly Label _panelTitle = new();
    private readonly FlatButton _panelClose = new();

    // ── Panel framework (V2-B/E1) — one PanelView per rail id, swapped by visibility under the shared header.
    // V2-E1 filled the last four ids (hotkeys/group/settings/about) with real panels; the shared PendingPanel is gone.
    private readonly TargetsPanel _targetsPanel;
    private readonly DisplayPanel _displayPanel;
    private readonly RegionPanel _regionPanel;
    private readonly ProfilesPanel _profilesPanel;
    private readonly HotkeysPanel _hotkeysPanel;
    private readonly GroupPanel _groupPanel;
    private readonly SettingsPanel _settingsPanel;
    private readonly AboutPanel _aboutPanel;
    private readonly PanelView[] _allPanels;
    private readonly Dictionary<string, PanelView> _panels;

    // ── FR-02 영역 드래그 (V2-C — CloneForm 이식): armed by the region panel / Ctrl+Alt+R, then a drag over the
    // mirror rect commits a crop. The rubber band is a SCREEN-level reversible frame — the DWM thumbnail composites
    // over the form surface, so a GDI rubber band drawn in OnPaint would be hidden under it (v1 실증 :1373).
    private bool _regionSelecting;
    private bool _regionDragging;
    private Point _dragStartClient;
    private Point _dragCurrentClient;
    private Rectangle _dragFrame; // last reversible frame (screen coords), Empty when none

    // FR-15 드로우 절반 (V2-B): canvas frame ring on/off — visual only; margin/영속 재해석은 V2-D.
    private bool _showMirrorBorder = true;

    // ── V2-D 창 제어 상태 (v1 CloneForm 창 셸 절반의 메인창 재해석) ──
    private ClientSizeMode? _sizeMode;      // FR-03 preset; null = free size (사용자 드래그/clamp 해제 — [2차] F1)
    private SnapAnchor? _anchor;            // FR-04; null = 해제
    private bool _clickForward;             // FR-06
    private bool _clickThrough;             // FR-07 — 통과 ON 은 UI 숨김을 동반 (Q3 확정)
    private bool _locked;                   // FR-15 (in-memory — v1 승계, §7.2 에 lock 필드 없음)
    private bool _alwaysOnTop;              // 항상 위에 표시 (TopMost 반영)
    private bool _uiHidden;                 // V2-D 오버레이 모드 — rail/패널/타이틀 숨김, 클라이언트 전체가 미러
    private bool _preOverlaySideOpen;       // 오버레이 진입 시점의 패널 열림 스냅샷 (복원 재현 — R5)
    private bool _suppressPlacementEvents;  // 내부 크기/위치 변경이 preset/anchor 를 해제하지 않도록 ([2차] F2)
    private bool _clickForwardFailWarned;   // FR-06 실패 안내 1회 (전달 재켜기 시 리셋 — v1 승계)
    private FormWindowState _lastWindowState = FormWindowState.Normal; // 상태 전이 감지 → 전체화면 세그먼트 반영
    private bool _exitRequested;            // V2-E3: CloseForExit() 경유 실종료 — OnFormClosing 의 close=트레이 취소 우회
    private bool _trayNoticeShown;          // V2-E3: close→트레이 잔류 풍선 1회 게이트 (세션 한정 — Q1 채택)
    private bool _userCloseGesture;         // V2-E3 [5차] hotfix: SC_CLOSE(X·Alt+F4·시스템 메뉴)만 마킹 — raw WM_CLOSE 와 결정적 구분
    private readonly Icon _appIcon;         // 브랜드 아이콘 (배포 cycle) — borderless 크롬이라 Alt-Tab/taskbar 표면 전용, OnFormClosed 해제
    private const int MK_LBUTTON = 0x0001;
    private const int ScClose = 0xF060;     // SC_CLOSE (Native User32 는 무수정 재사용 원칙 — MK_LBUTTON 선례)

    // ── Embedded mirror (V2-A core) ── the canvas is FORM SURFACE: OnPaint draws the frame + idle hint at
    // _mirrorRect, and the DWM thumbnail composites over its inner area. No child control may cover this rect.
    private readonly MirrorSurface _mirror = new();
    private Rectangle _mirrorRect;
    private readonly System.Windows.Forms.Timer _sourceWatch = new() { Interval = 1000 };

    // ── FR-08 그룹 순환 (V2-E1 — v1 CloneForm 그룹 로직의 단일 미러 재해석) ──
    private readonly GroupSwitcher _group = new();
    private readonly System.Windows.Forms.Timer _groupTimer = new();
    private bool _groupMissingWarned;
    private int _groupMissCount;

    // ── FR-10 미러 우클릭 컨텍스트 메뉴 (V2-E1) ──
    private readonly ContextMenuStrip _mirrorMenu = new();
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
        _displayPanel = new DisplayPanel(_loc);
        _regionPanel = new RegionPanel(_loc);
        _profilesPanel = new ProfilesPanel(_loc);
        _hotkeysPanel = new HotkeysPanel();
        _groupPanel = new GroupPanel();
        _settingsPanel = new SettingsPanel();
        _aboutPanel = new AboutPanel();
        _allPanels = new PanelView[]
        {
            _targetsPanel, _displayPanel, _regionPanel, _profilesPanel,
            _hotkeysPanel, _groupPanel, _settingsPanel, _aboutPanel,
        };
        _panels = new Dictionary<string, PanelView>
        {
            [PanelTargets] = _targetsPanel,
            [PanelDisplay] = _displayPanel,
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
        MinimumSize = new Size(960, 600);
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
        _displayPanel.BorderToggleRequested += OnBorderToggleRequested;
        _displayPanel.SizeModeSelected += OnSizeModeSelected;
        _displayPanel.AnchorSelected += OnAnchorSelected;
        _displayPanel.ClickForwardToggleRequested += OnClickForwardToggleRequested;
        _displayPanel.ClickThroughToggleRequested += OnClickThroughToggleRequested;
        _displayPanel.LockToggleRequested += OnLockToggleRequested;
        _displayPanel.AotToggleRequested += OnAotToggleRequested;
        _displayPanel.HideUiRequested += OnHideUiRequested;
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
        _hotkeysPanel.ReflectFailures(_manager.HotkeyFailures); // FR-09 startup-static conflict flags (ApplyStrings 후)
        _settingsPanel.ReflectSettings(_manager.Current);       // seed 언어/시작 토글
        _groupTimer.Tick += OnGroupTick;                        // FR-08 순환 타이머

        _mirror.SetOpacity(_manager.Current.Defaults.Opacity); // FR-05 초기값 = 새 미러 기본값 승계 (B-Q3)
        _alwaysOnTop = _manager.Current.Defaults.AlwaysOnTop; // v1 미러 창 AOT 기본값의 메인창 재해석 (V2-D 결선)
        TopMost = _alwaysOnTop;
        SyncPanels();

        _mirror.Changed += OnMirrorChanged;
        _manager.HotkeyRouted += OnHotkeyRouted;       // v2: with no clone windows the global hotkeys land here
        _manager.SurfaceRequested += OnSurfaceRequested; // v2 interim ([5차] F1): tray 표시/숨김·복원도 이 창 대상
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

    /// <summary>FR-10 (V2-E1): the mirror-canvas right-click menu (v1 CloneForm 컨텍스트 메뉴의 단일 미러 재해석,
    /// 패널 중복 최소 세트). Shown only over a live mirror (<see cref="OnMouseUp"/> gate); item enable/check is set
    /// on Opening. Click-forward/through toggle through the same shell routes as the display panel.</summary>
    private void BuildMirrorMenu()
    {
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
            _ctxStop.Enabled = m;
            _ctxRegion.Enabled = m;
            _ctxForward.Enabled = m && !_clickThrough; // 전달·통과 상호배타 (v1 :729)
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
        new(PanelDisplay, Glyph.Monitor, _loc.Get(LocKeys.Main_Display_Title)),
        new(PanelRegion, Glyph.Crop, _loc.Get(LocKeys.Main_Nav_Region)),
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
        PanelRegion => _loc.Get(LocKeys.Main_Nav_Region),
        PanelProfiles => _loc.Get(LocKeys.Main_Nav_Profiles),
        PanelHotkeys => _loc.Get(LocKeys.Main_Nav_Hotkeys),
        PanelGroup => _loc.Get(LocKeys.Main_Nav_Group),
        PanelSettings => _loc.Get(LocKeys.Main_Nav_Settings),
        PanelAbout => _loc.Get(LocKeys.Main_Nav_About),
        _ => _loc.Get(LocKeys.Main_Nav_Targets),
    };

    /// <summary>Switches the side panel to <paramref name="id"/>'s <see cref="PanelView"/> and expands the panel
    /// (V2-E1: all eight rail ids now map to real panels — settings is the absorbed <see cref="SettingsPanel"/>, not
    /// a separate window; region/profiles/settings/group re-seed from their sources on entry). IconRail's programmatic
    /// SelectedIndex setter does not raise ItemClicked, so this is the single routing entry point for both user clicks
    /// and programmatic switches (e.g. the PickWindow hotkey / tray OpenSettings).</summary>
    private void SetActivePanel(string id)
    {
        _activePanelId = id;
        _rail.SelectedIndex = Array.IndexOf(RailIds, id);
        _panelTitle.Text = PanelTitle(id);

        // V2-C: the stores are edited outside this window's lifetime too (v1 files carry over) — re-read on entry
        // so the list is fresh without a per-keystroke file watch (v1 lazy menu-open enumeration 승계). V2-E1: the
        // settings panel (v1 설정 창 흡수) and group panel re-seed from their sources on entry too.
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
    }

    /// <summary>Card click = start (or retarget) the embedded mirror. Re-clicking the already-mirrored target keeps
    /// the live session (no re-register flicker); a failed registration shows the v1 notice and leaves any current
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
                error ?? _loc.Get(LocKeys.Dialog_CloneFailed_Body), // [5차] F3: zero-size/no-message failures get a real body, not the caption twice
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

    /// <summary>Single route for opacity changes — panel slider AND the Ctrl+Alt+↑/↓ hotkeys land here, so the
    /// mirror and the display-panel readout can never diverge ([2차] F1, V2-B).</summary>
    private void ApplyOpacity(int percent)
    {
        _mirror.SetOpacity(percent);
        ApplyVisualState(); // 오버레이 중엔 Form.Opacity 채널이 p% 를 따라간다 ([2차] F4 invariant)
        SyncPanels();
    }

    // ── FR-03/04/06/07/15 창 제어 (V2-D — v1 CloneForm 창 셸 절반의 메인창 재해석) ────────

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

    /// <summary>FR-03 전체화면 = maximize (v1 borderless 전체화면의 메인창 재해석 — WindowPlacement.cs:25 주석 승계).
    /// 세그먼트 반영은 <see cref="OnResize"/> 의 상태 전이 감지와 본 SyncPanels 가 담당.</summary>
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
    /// FR-03 크기 preset 단일 route ([2차] F1): active source(크롭 반영 — v1 보완 1) 기준 목표 썸네일 물리 크기를
    /// <see cref="WindowPlacement.ComputeSizeMode"/> 로 산출하고, <see cref="DoLayout"/> 이 미러 주변에 예약하는
    /// 크롬(타이틀/그립/패드/rail/사이드패널 + BorderDip 물리 마진)을 역산해 ClientSize 로 적용한 뒤, 실제 DWM dest
    /// 크기를 재실측한다. MinimumSize(960×600) 등으로 목표에서 4px 넘게 벗어나면 preset 의미가 깨진 것 — 해제(-1)로
    /// 정직하게 반영한다. v1 작업영역 cap 은 목표 자체가 cap 된 값이라 편차 0 = preset 유지 (v1 의미 보존).
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
            try { WindowState = FormWindowState.Normal; } // 전체화면 해제 후 preset 적용 (v1 ApplySizeMode :495 승계)
            finally { _suppressPlacementEvents = false; }
        }

        if (!_mirror.TryGetActiveSourceSize(out int srcW, out int srcH))
        {
            SyncPanels();
            return;
        }

        (double sx, double sy) = ClientScale();
        int margin = Math.Max(2, (int)Math.Round(BorderDip * sx)); // MirrorHostPhysical 과 동일식 — 역산 정합
        int overheadW = Grip * 2 + Theme.Pad * 2 + Theme.IconRailWidth + (_sideOpen ? Theme.SidePanelWidth : 0);
        int overheadH = TitleH + Theme.Pad * 2 + Grip; // DoLayout 역산

        Rectangle wa = Screen.FromControl(this).WorkingArea;
        int maxThumbW = Math.Max(1, (int)((wa.Width - overheadW) * sx) - margin * 2);
        int maxThumbH = Math.Max(1, (int)((wa.Height - overheadH) * sy) - margin * 2);
        (int tw, int th) = WindowPlacement.ComputeSizeMode(mode, srcW, srcH, maxThumbW, maxThumbH);

        var target = new Size(
            (int)Math.Ceiling((tw + margin * 2) / sx) + overheadW,
            (int)Math.Ceiling((th + margin * 2) / sy) + overheadH);

        _suppressPlacementEvents = true;
        try { ClientSize = target; } // MinimumSize 는 Form 이 자체 clamp — 아래 실측 비교가 감지한다
        finally { _suppressPlacementEvents = false; }

        _sizeMode = mode;
        ReapplyAnchor();

        (int aw, int ah) = _mirror.LastDestSize; // ClientSize 변경 → OnLayout → DoLayout → FitToHost 완료 후 실측
        if (Math.Abs(aw - tw) > 4 || Math.Abs(ah - th) > 4)
            _sizeMode = null; // clamp 로 preset 의미 상실 → 해제 반영 ([2차] F1 정책)

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
            _ => null, // 0 = 해제
        };
        ReapplyAnchor();
        SyncPanels();
    }

    /// <summary>FR-04: re-anchors the window inside the current work area (v1 <c>ReapplyAnchor</c> :539 승계 —
    /// preset 적용/리사이즈 종료/DPI 이동 후 재정렬). Maximized 는 배치 개념이 없어 스킵.</summary>
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

    /// <summary>FR-06 단일 route (desired state — idempotent, v1 M6-C F2 승계). 통과와 상호배타 (v1 :729 — 투명
    /// 창은 입력을 받지 못함); 거부 시 SyncPanels 가 토글 시각을 원복한다.</summary>
    private void SetClickForward(bool on)
    {
        if (_clickForward != on && (!on || (_mirror.HasMirror && !_clickThrough)))
        {
            _clickForward = on;
            if (on)
                _clickForwardFailWarned = false; // 켤 때마다 미지원 안내 1회 재허용 (v1 승계)
        }
        SyncPanels();
    }

    /// <summary>
    /// FR-07 단일 route ([2차] F3) — 패널 토글/Ctrl+Alt+C/프로필 복원/트레이/ESC 전 진입점이 여기로 온다.
    /// ON: 미러 + 탈출 핫키 guard(v1 :718) → 전달 OFF(상호배타) → UI 숨김 동반(Q3 확정) → 스타일.
    /// OFF: 통과 해제 = UI 복원 동반 — 탈출 경로가 항상 온전한 창을 돌려준다 ([2차] Q3 CODEX 정합).
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
                SyncPanels(); // 토글 시각 원복
                MessageBox.Show(
                    this,
                    _loc.Format(LocKeys.Dialog_ClickThroughUnavailable_Body, ClickThroughChord()),
                    _loc.Get(LocKeys.Dialog_ClickThroughUnavailable_Caption),
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return;
            }
            _clickForward = false; // 상호배타 (v1 :729)
        }

        _clickThrough = on;
        SetOverlayMode(on); // ON = 숨김 동반 / OFF = 복원 동반 — ApplyVisualState + SyncPanels 포함
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

    /// <summary>UI 숨김(오버레이) 모드 단일 route — 상태 전환 + [2차] F4 invariant 재적용 + 패널 반영.</summary>
    private void SetOverlayMode(bool hidden)
    {
        SetUiHidden(hidden);
        ApplyVisualState();
        SyncPanels();
    }

    /// <summary>Hides/restores the chrome (rail/사이드패널/타이틀) so the whole client becomes the mirror canvas
    /// (이월 ① — v1 <c>_hideAllBtn</c> 재해석). 복원은 진입 시점의 패널 열림 상태를 재현한다 (R5).</summary>
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
        DoLayout();
    }

    /// <summary>
    /// [2차] F4 invariant 단일 재적용 — 일반: Form.Opacity 1.0 + 썸네일 p% / 오버레이: Form.Opacity p%(배후 투시,
    /// Q2 확정) + 썸네일 255 (이중 감쇠 방지). 통과 스타일은 항상 마지막: Opacity 가 1.0 으로 돌아오면 WinForms 가
    /// WS_EX_LAYERED|TRANSPARENT 를 떨군다 (v1 보완 3 실측 — CloneForm.cs:557).
    /// </summary>
    private void ApplyVisualState()
    {
        _mirror.SetOverlay(_uiHidden);
        Opacity = _uiHidden ? _mirror.OpacityPercent / 100.0 : 1.0;
        ApplyClickThroughStyle(_clickThrough);
    }

    /// <summary>Adds/removes <c>WS_EX_LAYERED|WS_EX_TRANSPARENT</c> then commits with <c>SWP_FRAMECHANGED</c>
    /// (v1 <c>ApplyClickThroughStyle</c> :742 이식 — LAYERED 는 Opacity&lt;1 인 동안 보존해 FR-05 를 지킨다).</summary>
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
        _locked = on; // FR-15 — in-memory (v1 :91 승계). 크기·앵커는 ReflectMirror 게이트, 이동/리사이즈는 크롬 gate.
        SyncPanels();
    }

    private void OnAotToggleRequested(object? sender, EventArgs e) => SetAlwaysOnTop(!_alwaysOnTop);

    private void SetAlwaysOnTop(bool on)
    {
        _alwaysOnTop = on;
        TopMost = on;
        SyncPanels();
    }

    /// <summary>FR-06: maps a mirror-canvas mouse event onto the source and posts it (<see cref="MirrorSurface"/>
    /// 캡슐화 — [2차] Q2). Shell 몫 = 게이트(전달 ON/미러/영역 선택 우선) + 논리→물리 변환 + 실패 1회 안내.</summary>
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
    /// frame all follow <see cref="MirrorSurface"/> state here. Losing the mirror also releases the V2-D window
    /// modes — a stuck lock (v1 :684 승계) or an orphaned click-through/overlay would strand the user on a
    /// transparent, empty, unmovable window.</summary>
    private void OnMirrorChanged(object? sender, EventArgs e)
    {
        if (!_mirror.HasMirror)
        {
            if (_regionSelecting || _regionDragging)
                CancelRegionSelect(); // source lost mid-select — don't leave a cross cursor armed on an idle canvas
            _locked = false;
            _clickForward = false;
            _sizeMode = null;
            _anchor = null; // v1 ResetToEmptyState :679 정합 — idle 창에 앵커 잔존 금지 ([5차] LOW 1)
            if (_group.IsRunning)
                StopGroupSwitch(); // FR-08: 미러 소실 = 순환할 소스 없음 (타이머 정지, 멤버는 보존)
            if (_clickThrough)
                SetClickThrough(false); // route 가 UI 복원 동반 — 투명 빈 창 고립 차단
            else if (_uiHidden)
                SetOverlayMode(false);
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
        _displayPanel.ReflectMirror(_mirror.HasMirror, ShellViewState(), _manager.IsClickThroughHotkeyLive);
        _regionPanel.ReflectMirror(_mirror.HasMirror, _mirror.CurrentRegion);
        _profilesPanel.ReflectMirror(_mirror.HasMirror);
        _groupPanel.ReflectMirror(_mirror.HasMirror); // FR-08 경량 반영 (버튼 활성만; 멤버 목록은 ReflectGroup)
    }

    /// <summary>Snapshot of the shell's window-control state for the 표시 패널 (V2-D — v1 <c>CloneForm.Snapshot</c>
    /// 의 메인창 재해석; FR-03 전체화면 = maximize).</summary>
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

    // ── FR-02 영역 (V2-C) — panel/hotkey intents → mirror crop + store ────

    /// <summary>Single route for crop changes (clear / saved apply / profile restore) — mirror crop, panel reflect
    /// and the canvas repaint stay together ([2차] F3; the drag commit shares the same tail in
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

    /// <summary>Arms the mirror-rect drag selection (v1 <c>EnterRegionSelect</c> 승계 — 세션 가드 + 십자 커서).
    /// A global Ctrl+Alt+R can arrive tray-hidden/minimized; restore first so there is a surface to drag on
    /// (V2-E3 — 보이는 일반 상태에선 no-op 이라 오버레이 중 동작은 불변).</summary>
    private void EnterRegionSelect()
    {
        if (!_mirror.HasMirror || _clickThrough) // 통과 중엔 마우스가 창에 닿지 않음 — v1 :857 가드 승계
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

    /// <summary>Commits the finished drag: logical client corners → physical px (the DWM-side coordinate space,
    /// [2차] F2 — <see cref="ClientScale"/> is shared with <see cref="MirrorHostPhysical"/> so both sides of the
    /// mapping agree), then <see cref="MirrorSurface.SetRegionFromDrag"/> maps dest → source and applies the crop.
    /// A stray click (&lt;4px source span) leaves the current region (v1 승계).</summary>
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
            List<NamedRegion> list = _manager.Regions.Load().Where(r => r.Name != name).ToList(); // same-name overwrite (v1 승계)
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

    // ── FR-13 프로필 (V2-C) — panel intents → capture/restore + store ─────

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

    /// <summary>Snapshots the embedded mirror + shell into a §7.2 <see cref="Profile"/> (v1 <c>CaptureProfile</c>
    /// 재해석 — placement = MAIN-window bounds, schema-compatible; target spec = the 5-field
    /// <c>CurrentTargetSpec</c> pattern so <see cref="WindowMatcher"/>'s captured-identity chain keeps working,
    /// [2차] F1). The V2-D-owned fields (anchor / click-through / AOT) are captured from their current interim
    /// values and preserved on restore ([2차] P1).</summary>
    private Profile CaptureProfile(string name)
    {
        return new Profile
        {
            Id = "p_" + Guid.NewGuid().ToString("N")[..8],
            Name = name,
            Target = CurrentTargetSpec(), // FR-08/13 공용 — 현재 미러 소스의 durable spec
            Region = ProfileRegion.FromRegion(_mirror.CurrentRegion),
            Placement = new Placement
            {
                Monitor = Array.IndexOf(Screen.AllScreens, Screen.FromControl(this)),
                Anchor = _anchor, // V2-D 결선 — 실값 캡처
                X = Bounds.X,
                Y = Bounds.Y,
                Width = Bounds.Width,
                Height = Bounds.Height,
            },
            Opacity = _mirror.OpacityPercent,
            ClickThrough = _clickThrough,  // V2-D 결선
            ShowBorder = _showMirrorBorder,
            AlwaysOnTop = _alwaysOnTop,    // V2-D 결선
        };
    }

    /// <summary>Restores a profile onto the embedded mirror (v1 <c>ApplyProfile</c> 이식, V2-D 전체 복원): resolve
    /// via <see cref="WindowMatcher"/> → notice when unmatched → fresh <c>Start</c> (프로필 = fresh mirror 구성;
    /// 실패 시 기존 미러 무변경 — v1 M6-C F3 승계) → region + opacity + border + placement/anchor + AOT, and
    /// click-through LAST (v1 :1101 — OFF strips a stale style, ON stays guarded), then one settled panel push.</summary>
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
        ApplyProfilePlacement(profile.Placement); // V2-D 결선: 배치+앵커 실적용 (모니터 clamp — v1 :1115 이식)
        SetAlwaysOnTop(profile.AlwaysOnTop);      // V2-D 결선: AOT 실적용
        SetClickThrough(profile.ClickThrough);    // 마지막 — OFF 는 잔존 스타일 해제, ON 은 guard (v1 :1097-1101 승계)
        ApplyVisualState();                       // [2차] F4 invariant 정착 (오버레이/일반 채널)
        SyncPanels();
        Invalidate(_mirrorRect);
    }

    /// <summary>Positions the window per a saved <see cref="Placement"/>, clamped into the resolved monitor
    /// (v1 <c>ApplyPlacement</c>/<c>ResolveScreen</c> :1115/:1134 이식 — 대상만 메인창). 저장 bounds 복원 = free
    /// size (ClientSizeMode 프로필 영속은 체크리스트v2.md §8 이월 유지).</summary>
    private void ApplyProfilePlacement(Placement placement)
    {
        if (WindowState != FormWindowState.Normal)
            WindowState = FormWindowState.Normal; // 최대화 위엔 배치 복원이 안 먹는다 — 먼저 복귀

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
        // the saved bounds, else the primary (v1 PEER Q3 보완 승계 — clamp happens in ApplyProfilePlacement).
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

    /// <summary>Modal name prompt (v1 <c>CloneForm.PromptForName</c> 이식). TopMost so it can never render behind an
    /// always-on-top surface and look like a hang while blocked on an invisible modal (v1 보완 승계).</summary>
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

    // ── Global hotkeys (v2 routing — no clone windows) ───────────────────

    /// <summary>v2 hotkey semantics on the single window: ToggleVisible hides to / restores from the tray
    /// (V2-E3 트레이 상주), PickWindow opens + refreshes the targets panel, and Opacity± steps the embedded
    /// mirror through the same <see cref="ApplyOpacity"/> route as the panel slider (V2-B, [2차] F1).
    /// RegionSelect/GroupSwitch/ClickThrough act on the mirror from V2-C/D.</summary>
    private void OnHotkeyRouted(object? sender, HotkeyAction action)
    {
        switch (action)
        {
            case HotkeyAction.ToggleVisible:
                ToggleTraySurface();
                break;

            case HotkeyAction.PickWindow:
                RestoreFromTray(); // 통과/오버레이 해제 + 트레이·최소화 복원 + Activate (V2-E3 공용 복원 경로)
                SetActivePanel(PanelTargets);
                _targetsPanel.ReloadTargets();
                _targetsPanel.FocusSearch();
                break;

            case HotkeyAction.ClickThrough:
                SetClickThrough(!_clickThrough); // FR-07 Ctrl+Alt+C — 진입/탈출 모두 단일 route ([2차] F3)
                break;

            case HotkeyAction.OpacityUp:
                if (_mirror.HasMirror) ApplyOpacity(_mirror.OpacityPercent + 10); // FR-05 10% step (§6.4)
                break;

            case HotkeyAction.OpacityDown:
                if (_mirror.HasMirror) ApplyOpacity(_mirror.OpacityPercent - 10);
                break;

            case HotkeyAction.RegionSelect:
                EnterRegionSelect(); // FR-02 (V2-C) — v1 per-clone 핫키의 내장 미러 재해석 (미러 없으면 no-op)
                break;

            case HotkeyAction.GroupSwitch:
                ToggleGroupSwitch(); // FR-08 (V2-E1) — 내장 미러 소스 순환 토글 (빈 그룹/미러 없음 게이트)
                break;
        }
    }

    /// <summary>Tray surface routing (V2-E3 트레이 상주) — 표시/숨김 shares the hotkey's tray toggle meaning,
    /// 더블클릭/설정 restore only (a visible window must not hide on a "restore" gesture).</summary>
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
                // V2-E1: 트레이 '설정' → 온전한 창 복원 후 흡수된 설정 패널 열기 (ShowSettings 별도 창 대체).
                RestoreFromTray();
                SetActivePanel(PanelSettings);
                break;
        }
    }

    // ── V2-E3 트레이 상주 (close=트레이 잔류 · 최소화=트레이 · 복원 단일 경로) ────

    /// <summary>표시/숨김 (hotkey + tray menu): visible → tray, tray-hidden/minimized → full restore.
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
    /// not a lifecycle transition — 최소화와 동일 의미, R4). The first close-to-tray shows a one-time balloon via
    /// the manager → tray icon (Q1 채택) so the user knows the app did not exit.</summary>
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

    /// <summary>The single way back from the tray/minimized state: clears click-through/overlay first (트레이
    /// 복원 = 온전한 UI — [2차] F3 승계), shows the window again (tray-hidden = <c>Visible=false</c>), returns a
    /// Minimized state to Normal, activates, and re-pushes the mirror layout (the DWM destination was hidden —
    /// R2 재피팅). No-op parts are safe when already visible, so global hotkeys route here unconditionally.</summary>
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
        _mirror.UpdateLayout(MirrorHostPhysical()); // R2: destination 숨김 동안의 상태 드리프트 방지 재피팅
    }

    /// <summary>Real application exit (tray "종료" — V2-E3): bypasses the close=tray policy in
    /// <see cref="OnFormClosing"/> so the close still funnels through <c>FormClosed</c> →
    /// <c>SumboAppContext.ExitApp</c> (single exit path).</summary>
    internal void CloseForExit()
    {
        _exitRequested = true;
        Close();
    }

    /// <summary>V2-E3 트레이 상주: a user close GESTURE (크롬 X · Alt+F4 · 시스템 메뉴/taskbar 닫기 = SC_CLOSE)
    /// retires to the tray instead of exiting. Everything else — a raw external <c>WM_CLOSE</c> (taskkill·스크립트),
    /// Windows shutdown/logoff, task manager, <see cref="CloseForExit"/> — keeps closing for real, so automation
    /// and the OS can always end the process gracefully. The gesture flag (not <see cref="Form.CloseReason"/>) is
    /// the discriminator: a cancelled close leaves <c>CloseReason.UserClosing</c> behind, which would misroute the
    /// next raw <c>WM_CLOSE</c> to the tray ([5차] CODEX 실측 + 순서 의존 재현).</summary>
    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        base.OnFormClosing(e);
        bool gesture = _userCloseGesture;
        _userCloseGesture = false; // 1회 소비 — 다음 close 판정을 오염시키지 않음
        if (!e.Cancel && gesture && e.CloseReason == CloseReason.UserClosing && !_exitRequested)
        {
            e.Cancel = true;
            HideToTray(notice: true);
        }
    }

    // ── FR-08 그룹 순환 (V2-E1 — v1 CloneForm 그룹 로직의 단일 미러 재해석) ────

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
            _groupTimer.Interval = _group.IntervalSeconds * 1000; // 순환 중 간격 즉시 반영
        ReflectGroup();
    }

    /// <summary>FR-08 시작/정지 토글 (v1 <c>ToggleGroupSwitch</c> :1197 재해석). 시작은 라이브 미러가 있어야 순환할
    /// 소스가 있다 — 빈 그룹은 안내, 미러 없음은 no-op 게이트 (버튼은 hasMirror 게이트지만 핫키 경로 방어).</summary>
    private void ToggleGroupSwitch()
    {
        if (_group.IsRunning)
        {
            StopGroupSwitch();
            return;
        }

        if (_group.Count == 0)
        {
            MessageBox.Show(
                this,
                _loc.Get(LocKeys.Dialog_GroupEmpty_Body),
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

    /// <summary>Rotates the embedded mirror's source to the next group member every N seconds (FR-08 해석 α —
    /// v1 <c>OnGroupTick</c> :1233 이식). 영역 드래그 중엔 스킵(진행 중 드래그를 다른 소스로 remap 방지); 미해결
    /// 멤버는 1회 안내 후 스킵하고 전멸(한 바퀴 전건 실패) 시 정지 (v1 승계). 재타깃은 region/opacity 보존
    /// (<see cref="MirrorSurface.RetargetPreserving"/>).</summary>
    private void OnGroupTick(object? sender, EventArgs e)
    {
        if (_regionSelecting || _regionDragging)
            return;

        TargetSpec? next = _group.Next();
        if (next is null)
        {
            StopGroupSwitch(); // 빈 그룹 — 순환할 것 없음
            return;
        }

        IReadOnlyList<WindowInfo> windows;
        try
        {
            windows = WindowEnumerator.GetCloneableWindows();
        }
        catch (Exception)
        {
            return; // 일시적 열거 실패 — 다음 tick 재시도
        }

        WindowInfo? target = WindowMatcher.Resolve(next, windows);
        if (target is null)
        {
            _groupMissCount++;
            if (_groupMissCount >= _group.Count)
            {
                StopGroupSwitch(); // 전멸 lap — idle 타이머 spin 대신 정지 (v1 :1265 승계)
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
            return; // 부분 miss — 이 멤버 스킵, 순환 유지
        }

        _groupMissCount = 0; // 해결된 멤버 = 전멸 lap 카운터 리셋
        if (_mirror.RetargetPreserving(target, out _)) // 성공 → Changed → OnMirrorChanged → SyncPanels (target 라벨 갱신)
            _groupMissingWarned = false;               // 성공 hop = 1회 안내 재무장
    }

    /// <summary>Builds a durable target spec for the current mirror source (group member / profile target — v1
    /// <c>CurrentTargetSpec</c> :1152 이식, <see cref="CaptureProfile"/> 와 공용).</summary>
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

    // ── 설정 패널 (V2-E1 — v1 설정 창 흡수, 셸이 CloneManager 설정 SSOT 중개) ────

    private void OnSettingsLanguageSelected(object? sender, string language)
    {
        _manager.SetLanguage(language);                   // LanguageChanged → ApplyStrings fan-out re-labels every panel
        _settingsPanel.ReflectSettings(_manager.Current); // relabel 후 세그먼트 선택 재확정
    }

    private void OnSettingsStartWithWindowsToggled(object? sender, bool on)
    {
        _manager.SetStartWithWindows(on);                 // 정책 차단 가능 — 권위 상태로 재반영 (v1 설정 창 승계)
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
            // 오버레이 (이월 ①): 클라이언트 전체 = 미러 캔버스 — rail/패널/타이틀 없음, 크롬 버튼 히트 제거.
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

        // v2: everything left of the panel is the mirror canvas — bare form surface, no child controls.
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
    /// the DWM <c>rcDestination</c> (FR-11 — DWM wants physical client coords). <see cref="MirrorSurface"/> letterboxes
    /// the source inside this rect.
    /// </summary>
    private RECT MirrorHostPhysical()
    {
        (double sx, double sy) = ClientScale();

        int px = (int)Math.Round(_mirrorRect.X * sx);
        int py = (int)Math.Round(_mirrorRect.Y * sy);
        int pw = (int)Math.Round(_mirrorRect.Width * sx);
        int ph = (int)Math.Round(_mirrorRect.Height * sy);

        int margin = Math.Max(2, (int)Math.Round(BorderDip * sx)); // keep the drawn frame visible around the thumbnail
        return new RECT(
            px + margin,
            py + margin,
            px + Math.Max(margin + 1, pw - margin),
            py + Math.Max(margin + 1, ph - margin));
    }

    /// <summary>Logical-client → physical-pixel scale (GetClientRect vs ClientSize — FR-11). Shared by the DWM host
    /// rect and the region-drag commit so both sides of the dest-coordinate mapping agree ([2차] F2).</summary>
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

        // FR-10 미러 우클릭 메뉴 라벨 (기존 키 재사용 — 패널과 동일 어휘)
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
            DrawMirrorArea(g); // 오버레이: 타이틀/로고/버튼 없이 미러 프레임만 (이월 ①)
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

    /// <summary>Paints the mirror canvas frame on the FORM surface (no child control here — DWM 게이트). With a live
    /// mirror the thumbnail composites over the inner area, leaving the accent frame visible around it; when idle a
    /// hint tells the user to pick a target from the panel (or the PickWindow hotkey). The ring honors the 테두리
    /// 표시 toggle (V2-B, FR-15 드로우 절반 — visual only, the reserved margin stays).</summary>
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
            // FR-02 rubber band — erase the previous XOR frame, then draw at the new extent (v1 승계).
            EraseDragFrame();
            _dragCurrentClient = e.Location;
            _dragFrame = DragScreenRect();
            ControlPaint.DrawReversibleFrame(_dragFrame, Color.White, FrameStyle.Dashed);
            return;
        }

        // FR-06 (V2-D): 전달 ON + 미러 위 = 이동도 소스로 (버튼 상태는 실제 마우스 상태를 따른다 — v1 :1380 승계)
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
            if (_btnClose.Contains(e.Location)) { _userCloseGesture = true; Close(); return; } // 크롬 X = 사용자 제스처
            if (_btnMax.Contains(e.Location)) { ToggleMaximize(); return; }
            if (_btnMin.Contains(e.Location)) { WindowState = FormWindowState.Minimized; return; }

            // FR-02 (V2-C): armed select mode + press inside the mirror canvas = start the crop drag. Only the
            // form surface gets here (children swallow their own mouse), so the rect check is the whole gate.
            if (_regionSelecting && _mirrorRect.Contains(e.Location))
            {
                _regionDragging = true;
                _dragStartClient = e.Location;
                _dragCurrentClient = e.Location;
                _dragFrame = Rectangle.Empty;
                return;
            }

            // FR-06 (V2-D): 전달 ON + 미러 위 좌클릭 = 소스로 post (영역 선택이 위에서 우선 — v1 우선순위 승계)
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

        // FR-10 (V2-E1): 미러 위 우클릭 = 컨텍스트 메뉴. 통과 중엔 창이 입력을 못 받고, 영역 선택/드래그 중엔
        // 그 조작이 우선 ([2차] Q5 표시 조건). 유휴 캔버스는 rail [대상 창] 패널로 안내(main.mirror.hint).
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
        // FR-06: 전달 ON 이면 휠도 소스로 — 레터박스 마진에서 매핑이 실패해도 로컬로 흘리지 않고 소비 (v1 :1413 승계).
        if (_clickForward && _mirror.HasMirror && _mirrorRect.Contains(e.Location))
        {
            TryForward(User32.WM_MOUSEWHEEL, e, new IntPtr(e.Delta << 16), wheel: true);
            return;
        }
        base.OnMouseWheel(e);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        // ESC bails out of an armed / in-flight region selection (v1 승계; KeyPreview=true routes it here).
        if (e.KeyCode == Keys.Escape && (_regionSelecting || _regionDragging))
        {
            CancelRegionSelect();
            e.Handled = true;
            return;
        }

        // ESC restores the UI from the overlay / click-through (B2 탈출 — 통과 중엔 포커스가 남아 있을 때만 유효,
        // 확실한 탈출은 Ctrl+Alt+C 전역 핫키와 트레이 복원).
        if (e.KeyCode == Keys.Escape && (_clickThrough || _uiHidden))
        {
            if (_clickThrough)
                SetClickThrough(false); // 단일 route 가 UI 복원 동반
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
            return; // FR-15 — 크롬 버튼 경로도 잠금 (WM_SYSCOMMAND swallow 와 세트)
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
            _lastWindowState = WindowState; // 최소화/최대화/복원 전이 → 전체화면 세그먼트 등 패널 반영
            SyncPanels();

            // V2-E3 최소화=트레이: E2 에서 보존한 MinimizeToTray(§7.1, 기본 true)의 첫 실소비 지점.
            // off = 현행 taskbar 최소화 유지. 명시적 설정 경로라 풍선 없음 (Q1 은 close 경로 한정).
            if (enteredMinimized && Visible && _manager.MinimizeToTray)
                HideToTray(notice: false);
        }
    }

    protected override void OnClientSizeChanged(EventArgs e)
    {
        base.OnClientSizeChanged(e);
        // [2차] F2: 사용자 리사이즈만 preset 해제 — 내부(suppressed) 변경과 최소화(OS 축소)·최대화(전체화면
        // 세그먼트가 별도 반영) 상태는 제외 (v1 :1740 최소화 가드의 확장).
        if (_suppressPlacementEvents || WindowState != FormWindowState.Normal || _sizeMode is null)
            return;
        _sizeMode = null;
        SyncPanels(); // preset→free 전이 1회만 (v1 WM_SIZE storm 가드 승계)
    }

    protected override void OnMove(EventArgs e)
    {
        base.OnMove(e);
        // [2차] F2: 사용자 이동 = 앵커 해제 (v1 OnMove :1752 승계). 최대화 전이도 Location 을 바꾸므로 Normal 한정.
        if (_suppressPlacementEvents || _anchor is null || WindowState != FormWindowState.Normal)
            return;
        _anchor = null;
        SyncPanels();
    }

    protected override void OnResizeEnd(EventArgs e)
    {
        base.OnResizeEnd(e);
        ReapplyAnchor(); // FR-04: 리사이즈 종료 후 앵커 모서리 재정렬 (이동 종료는 OnMove 가 이미 앵커를 해제 → no-op)
    }

    protected override void WndProc(ref Message m)
    {
        // V2-E3 [5차] hotfix: 사용자 close 제스처(Alt+F4/시스템 메뉴/taskbar 닫기 = SC_CLOSE)를 여기서 마킹.
        // WinForms 의 CloseReason 은 취소된 close 의 UserClosing 이 잔존해 뒤따르는 raw WM_CLOSE 를 오염시키는
        // 순서 의존이 실측됐다(fresh=종료·SC_CLOSE 취소 후=잔류) — 이 플래그가 결정적 판별자다.
        if (m.Msg == User32.WM_SYSCOMMAND && (int)(m.WParam.ToInt64() & 0xFFF0) == ScClose)
            _userCloseGesture = true;

        // FR-15 위치·크기 잠금: 사용자발 이동/리사이즈/최대화 시스템 명령 삼킴 (v1 :1652 이식 — suppressed 내부
        // Bounds 설정은 SYSCOMMAND 를 타지 않아 비영향).
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

        // FR-11 §8.6: moved to a different-DPI monitor. WinForms rescales the window on base.WndProc; re-run layout
        // afterward so the mirror rect + its physical rcDestination are recomputed for the new DPI.
        if (m.Msg == User32.WM_DPICHANGED)
        {
            // [2차] F2: WinForms 의 DPI 리스케일이 OnClientSizeChanged/OnMove 를 발화시켜 preset/anchor 를
            // 지우지 않도록 감싼다 (v1 :1661-1684 stuck-flag 하드닝 승계 — try/finally).
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
            if (_sizeMode is ClientSizeMode mode && WindowState == FormWindowState.Normal)
                ApplyMirrorSizeMode(mode); // 새 모니터 DPI/작업영역 기준 재산출 (v1 :1677 승계 — ReapplyAnchor 포함)
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

        if (!_locked && WindowState != FormWindowState.Maximized) // FR-15: 잠금 = 리사이즈 그립 제거
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
            return 0; // 오버레이: 타이틀 스트립 없음 — 창 이동은 UI 복원 후 (그립 리사이즈만 유지)

        if (p.Y < TitleH)
        {
            if (_btnMin.Contains(p) || _btnMax.Contains(p) || _btnClose.Contains(p))
                return User32.HTCLIENT; // let the mouse handlers process the window buttons
            // FR-15: 잠금 중 캡션 드래그·더블클릭 최대화 차단 — 커스텀 크롬은 원천 gate 가 v1 사후 remap(:1691) 등가
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
        _displayPanel.BorderToggleRequested -= OnBorderToggleRequested;
        _displayPanel.SizeModeSelected -= OnSizeModeSelected;
        _displayPanel.AnchorSelected -= OnAnchorSelected;
        _displayPanel.ClickForwardToggleRequested -= OnClickForwardToggleRequested;
        _displayPanel.ClickThroughToggleRequested -= OnClickThroughToggleRequested;
        _displayPanel.LockToggleRequested -= OnLockToggleRequested;
        _displayPanel.AotToggleRequested -= OnAotToggleRequested;
        _displayPanel.HideUiRequested -= OnHideUiRequested;
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
        _groupTimer.Tick -= OnGroupTick;
        _groupTimer.Stop();
        _groupTimer.Dispose();
        _mirrorMenu.Dispose();
        _sourceWatch.Stop();
        _sourceWatch.Dispose();
        _mirror.Dispose(); // unregister the DWM thumbnail
        _appIcon.Dispose(); // AppIcons.Load 소유권 — 폼 해체 후 해제 ([5차] 조건 3)

        // Cards + icon cache are torn down by TargetsPanel.Dispose (form dispose chain) in the F2 ownership order:
        // cards (image referencers) first, provider (image owner) last — the shell no longer touches either.

        base.OnFormClosed(e);
    }
}
