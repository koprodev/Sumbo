using System;
using System.Drawing;
using System.Runtime.Versioning;
using System.Windows.Forms;
using Sumbo.Core;

namespace Sumbo.App.Ui.Panels;

/// <summary>
/// The display-settings panel for the embedded mirror. The whole window-control surface is wired: size modes /
/// anchor / opacity / click-forward / click-through / lock / border / always-on-top drive the MAIN window, plus the
/// hide-UI (overlay) entry.
/// <para>
/// The panel is a pure view: it raises intent events and the shell routes them to the window/mirror, reflecting the
/// applied state back via <see cref="ReflectMirror"/> under the <see cref="_syncing"/> guard — every control's
/// programmatic setter fires its change event, so an unguarded reflect would loop back.
/// </para>
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed class DisplayPanel : PanelView
{
    private readonly LocalizationCatalog _loc;

    private readonly Label _sub = new();
    private readonly Label _targetLabel = new();
    private readonly CardPanel _targetBox = new();
    private readonly Label _targetValue = new();
    private readonly Label _sizeLabel = new();
    private readonly SegmentedControl _sizeSeg = new();
    private readonly Label _anchorLabel = new();
    private readonly AnchorGrid _anchorGrid = new();
    private readonly Label _opacityLabel = new();
    private readonly Label _opacityValue = new();
    private readonly SliderControl _opacitySlider = new();
    private readonly ModeRow _rowForward = new();
    private readonly ModeRow _rowThrough = new();
    private readonly ModeRow _rowLock = new();
    private readonly ModeRow _rowBorder = new();
    private readonly Label _aotLabel = new();
    private readonly FlatButton _aotBox = new();
    private readonly FlatButton _hideUiBtn = new();
    private readonly Label _hideUiHint = new();

    private bool _syncing; // guards the control handlers while ReflectMirror pushes shell → UI
    private bool _aot = true; // reflected AOT state — drives the button caption (ApplyStrings/ReflectMirror)

    /// <summary>User moved the opacity slider (percent, 10~100). Shell route: ApplyOpacity → mirror + reflect.</summary>
    public event EventHandler<int>? OpacityChangeRequested;

    /// <summary>User flipped the border toggle. Shell route: canvas ring on/off + reflect.</summary>
    public event EventHandler<bool>? BorderToggleRequested;

    /// <summary>User picked a size segment — 0 source / 1 half / 2 quarter / 3 fullscreen. Shell maps to
    /// <see cref="ClientSizeMode"/> / maximize; a clamped apply reflects back as no selection.</summary>
    public event EventHandler<int>? SizeModeSelected;

    /// <summary>User picked an anchor cell — <see cref="AnchorGrid"/> index 0 = none, 1..9 = TL,T,TR,L,C,R,BL,B,BR.
    /// The shell maps to <see cref="SnapAnchor"/>?.</summary>
    public event EventHandler<int>? AnchorSelected;

    /// <summary>Click-forward toggle (desired state, not a flip — idempotent under reflect).</summary>
    public event EventHandler<bool>? ClickForwardToggleRequested;

    /// <summary>Click-through toggle. The shell's single route guards/undoes it.</summary>
    public event EventHandler<bool>? ClickThroughToggleRequested;

    /// <summary>Position/size lock toggle.</summary>
    public event EventHandler<bool>? LockToggleRequested;

    /// <summary>Always-on-top button click — the shell flips and reflects the applied state back.</summary>
    public event EventHandler? AotToggleRequested;

    /// <summary>Hide-UI (overlay) entry. ESC / tray / Ctrl+Alt+C restore.</summary>
    public event EventHandler? HideUiRequested;

    public DisplayPanel(LocalizationCatalog loc)
    {
        _loc = loc ?? throw new ArgumentNullException(nameof(loc));

        AutoScroll = true; // natural content height (~800 DIP) exceeds short windows — scroll instead of clipping

        _sub.BackColor = Theme.PanelBg; _sub.ForeColor = Theme.TextMuted; _sub.Font = Theme.Small;
        _sub.AutoSize = false; _sub.TextAlign = ContentAlignment.MiddleLeft;

        _targetLabel.BackColor = Theme.PanelBg; _targetLabel.ForeColor = Theme.TextSecondary; _targetLabel.Font = Theme.H2;
        _targetLabel.AutoSize = false; _targetLabel.TextAlign = ContentAlignment.MiddleLeft;

        _targetBox.CardColor = Theme.InsetBg; _targetBox.BorderColorValue = Theme.CardBorder;
        _targetBox.CornerBack = Theme.PanelBg; _targetBox.Radius = Theme.SmallRadius;
        _targetValue.BackColor = Theme.InsetBg; _targetValue.ForeColor = Theme.TextPrimary; _targetValue.Font = Theme.Body;
        _targetValue.AutoSize = false; _targetValue.TextAlign = ContentAlignment.MiddleLeft;
        _targetValue.Text = "—"; // no mirror yet
        _targetBox.Controls.Add(_targetValue);

        _sizeLabel.BackColor = Theme.PanelBg; _sizeLabel.ForeColor = Theme.TextSecondary; _sizeLabel.Font = Theme.H2;
        _sizeLabel.AutoSize = false; _sizeLabel.TextAlign = ContentAlignment.MiddleLeft;
        _sizeSeg.SelectedIndex = -1; // free size until a preset applies

        _anchorLabel.BackColor = Theme.PanelBg; _anchorLabel.ForeColor = Theme.TextSecondary; _anchorLabel.Font = Theme.H2;
        _anchorLabel.AutoSize = false; _anchorLabel.TextAlign = ContentAlignment.MiddleLeft;
        _anchorGrid.SelectedIndex = 0;

        _opacityLabel.BackColor = Theme.PanelBg; _opacityLabel.ForeColor = Theme.TextSecondary; _opacityLabel.Font = Theme.H2;
        _opacityLabel.AutoSize = false; _opacityLabel.TextAlign = ContentAlignment.MiddleLeft;
        _opacityValue.BackColor = Theme.PanelBg; _opacityValue.ForeColor = Theme.TextPrimary; _opacityValue.Font = Theme.Value;
        _opacityValue.AutoSize = false; _opacityValue.TextAlign = ContentAlignment.MiddleRight;
        _opacitySlider.Value = 100;
        _opacityValue.Text = "100%";

        _rowBorder.Toggle.Checked = true; // matches the canvas ring's initial ON state

        _aotLabel.BackColor = Theme.PanelBg; _aotLabel.ForeColor = Theme.TextPrimary; _aotLabel.Font = Theme.BodySemi;
        _aotLabel.AutoSize = false; _aotLabel.TextAlign = ContentAlignment.MiddleLeft;
        _aotBox.Kind = ButtonKind.Dark; _aotBox.CornerBack = Theme.PanelBg; _aotBox.Font = Theme.Body;

        _hideUiBtn.Kind = ButtonKind.Dark; _hideUiBtn.CornerBack = Theme.PanelBg; _hideUiBtn.Font = Theme.BodySemi;
        _hideUiBtn.Glyph = Glyph.Hide; _hideUiBtn.GlyphSize = 12f;
        _hideUiHint.BackColor = Theme.PanelBg; _hideUiHint.ForeColor = Theme.TextMuted; _hideUiHint.Font = Theme.Small;
        _hideUiHint.AutoSize = false; _hideUiHint.TextAlign = ContentAlignment.MiddleLeft;

        // State handlers guard on _syncing because programmatic reflect setters re-raise the same events as user input; the Click handlers below need no guard (a setter never fires Click).
        _opacitySlider.ValueChanged += (_, _) =>
        {
            _opacityValue.Text = _opacitySlider.Value + "%"; // label tracks the slider even during a reflect
            if (_syncing) return;
            OpacityChangeRequested?.Invoke(this, _opacitySlider.Value);
        };
        _rowBorder.Toggle.CheckedChanged += (_, _) =>
        {
            if (_syncing) return;
            BorderToggleRequested?.Invoke(this, _rowBorder.Toggle.Checked);
        };
        _sizeSeg.SelectedIndexChanged += (_, _) =>
        {
            if (_syncing || _sizeSeg.SelectedIndex < 0) return; // -1 is only ever assigned by the reflect
            SizeModeSelected?.Invoke(this, _sizeSeg.SelectedIndex);
        };
        _anchorGrid.SelectedIndexChanged += (_, _) =>
        {
            if (_syncing) return;
            AnchorSelected?.Invoke(this, _anchorGrid.SelectedIndex);
        };
        _rowForward.Toggle.CheckedChanged += (_, _) =>
        {
            if (_syncing) return;
            ClickForwardToggleRequested?.Invoke(this, _rowForward.Toggle.Checked);
        };
        _rowThrough.Toggle.CheckedChanged += (_, _) =>
        {
            if (_syncing) return;
            ClickThroughToggleRequested?.Invoke(this, _rowThrough.Toggle.Checked);
        };
        _rowLock.Toggle.CheckedChanged += (_, _) =>
        {
            if (_syncing) return;
            LockToggleRequested?.Invoke(this, _rowLock.Toggle.Checked);
        };
        _aotBox.Click += (_, _) => AotToggleRequested?.Invoke(this, EventArgs.Empty);
        _hideUiBtn.Click += (_, _) => HideUiRequested?.Invoke(this, EventArgs.Empty);

        // No mirror yet — everything mirrors ReflectMirror's gates from the first SyncPanels push.
        _sizeSeg.Enabled = false;
        _anchorGrid.Enabled = false;
        _rowForward.SetRowEnabled(false);
        _rowThrough.SetRowEnabled(false);
        _rowLock.SetRowEnabled(false);
        _rowBorder.SetRowEnabled(false);
        _opacitySlider.Enabled = false;
        _hideUiBtn.Enabled = false;

        Controls.Add(_sub);
        Controls.Add(_targetLabel);
        Controls.Add(_targetBox);
        Controls.Add(_sizeLabel);
        Controls.Add(_sizeSeg);
        Controls.Add(_anchorLabel);
        Controls.Add(_anchorGrid);
        Controls.Add(_opacityLabel);
        Controls.Add(_opacityValue);
        Controls.Add(_opacitySlider);
        foreach (ModeRow row in new[] { _rowForward, _rowThrough, _rowLock, _rowBorder })
            Controls.Add(row);
        Controls.Add(_aotLabel);
        Controls.Add(_aotBox);
        Controls.Add(_hideUiBtn);
        Controls.Add(_hideUiHint);
    }

    /// <summary>Maps a <see cref="SnapAnchor"/>? to the <see cref="AnchorGrid"/> cell index (0 = none).</summary>
    private static int AnchorToIndex(SnapAnchor? anchor) => anchor switch
    {
        SnapAnchor.TopLeft => 1,
        SnapAnchor.Top => 2,
        SnapAnchor.TopRight => 3,
        SnapAnchor.Left => 4,
        SnapAnchor.Center => 5,
        SnapAnchor.Right => 6,
        SnapAnchor.BottomLeft => 7,
        SnapAnchor.Bottom => 8,
        SnapAnchor.BottomRight => 9,
        _ => 0,
    };

    /// <summary>Shell → panel: pushes the applied window/mirror state into the live controls under the reentry
    /// guard. With no live mirror the surface is disabled so the user can't drive settings that have no target;
    /// size/anchor also lock out while the position/size lock is on, and click-through needs the escape hotkey
    /// alive (<paramref name="clickThroughAvailable"/>).</summary>
    public void ReflectMirror(bool hasMirror, MirrorViewState state, bool clickThroughAvailable)
    {
        _syncing = true;
        try
        {
            _targetValue.Text = hasMirror && state.TargetTitle.Length > 0 ? state.TargetTitle : "—";
            _sizeSeg.SelectedIndex = !hasMirror ? -1
                : state.IsFullscreen ? 3
                : state.SizeMode switch
                {
                    ClientSizeMode.Source => 0,
                    ClientSizeMode.Half => 1,
                    ClientSizeMode.Quarter => 2,
                    _ => -1, // free/custom size — a clamped preset reflects as no selection
                };
            _anchorGrid.SelectedIndex = AnchorToIndex(state.Anchor);
            _opacitySlider.Value = state.OpacityPercent;
            _opacityValue.Text = state.OpacityPercent + "%";
            _rowForward.Toggle.Checked = state.ClickForward;
            _rowThrough.Toggle.Checked = state.ClickThrough;
            _rowLock.Toggle.Checked = state.Locked;
            _rowBorder.Toggle.Checked = state.ShowBorder;
            _aot = state.AlwaysOnTop;
            _aotBox.Text = _loc.Get(_aot ? LocKeys.Main_AlwaysOnTop_On : LocKeys.Main_AlwaysOnTop_Off);

            _sizeSeg.Enabled = hasMirror && !state.Locked;
            _anchorGrid.Enabled = hasMirror && !state.Locked;
            _opacitySlider.Enabled = hasMirror;
            _rowForward.SetRowEnabled(hasMirror && !state.ClickThrough); // mutually exclusive with click-through
            _rowThrough.SetRowEnabled(hasMirror && clickThroughAvailable);
            _rowLock.SetRowEnabled(hasMirror);
            _rowBorder.SetRowEnabled(hasMirror);
            _hideUiBtn.Enabled = hasMirror; // the overlay is a mirror-viewing mode — hiding an empty canvas is pointless
        }
        finally
        {
            _syncing = false;
        }
    }

    public override void ApplyStrings(LocalizationCatalog loc)
    {
        _sub.Text = loc.Get(LocKeys.Main_Display_Subtitle);
        _targetLabel.Text = loc.Get(LocKeys.Main_TargetSection);
        _sizeLabel.Text = loc.Get(LocKeys.Main_Display_Size);
        _sizeSeg.Items = new[]
        {
            loc.Get(LocKeys.Menu_Size_Source), loc.Get(LocKeys.Menu_Size_Half),
            loc.Get(LocKeys.Menu_Size_Quarter), loc.Get(LocKeys.Menu_Size_Fullscreen),
        };
        _anchorLabel.Text = loc.Get(LocKeys.Main_Display_Anchor);
        _anchorGrid.Labels = new[]
        {
            loc.Get(LocKeys.Menu_Anchor_None), loc.Get(LocKeys.Menu_Anchor_TopLeft), loc.Get(LocKeys.Menu_Anchor_Top),
            loc.Get(LocKeys.Menu_Anchor_TopRight), loc.Get(LocKeys.Menu_Anchor_Left), loc.Get(LocKeys.Main_Anchor_Center),
            loc.Get(LocKeys.Menu_Anchor_Right), loc.Get(LocKeys.Menu_Anchor_BottomLeft), loc.Get(LocKeys.Menu_Anchor_Bottom),
            loc.Get(LocKeys.Menu_Anchor_BottomRight),
        };
        _opacityLabel.Text = loc.Get(LocKeys.Main_Opacity);
        _rowForward.SetText(loc.Get(LocKeys.Menu_Mode_ClickForward), loc.Get(LocKeys.Main_Mode_ClickForward_Desc));
        _rowThrough.SetText(loc.Get(LocKeys.Menu_Mode_ClickThrough), loc.Get(LocKeys.Main_Mode_ClickThrough_Desc));
        _rowLock.SetText(loc.Get(LocKeys.Menu_Mode_Lock), loc.Get(LocKeys.Main_Mode_Lock_Desc));
        _rowBorder.SetText(loc.Get(LocKeys.Menu_Mode_Border), loc.Get(LocKeys.Main_Mode_Border_Desc));
        _aotLabel.Text = loc.Get(LocKeys.Main_Display_AlwaysOnTop);
        _aotBox.Text = loc.Get(_aot ? LocKeys.Main_AlwaysOnTop_On : LocKeys.Main_AlwaysOnTop_Off);
        _hideUiBtn.Text = loc.Get(LocKeys.Main_HideUi);
        _hideUiHint.Text = loc.Get(LocKeys.Main_Display_HideUi_Hint);
    }

    // ── Layout ──

    protected override void OnLayout(LayoutEventArgs levent)
    {
        base.OnLayout(levent);

        int pad = Theme.Pad + 2;
        int x = pad;
        int cw = Math.Max(0, ClientSize.Width - pad * 2);
        int top = AutoScrollPosition.Y; // negative when scrolled — keep positions in the scrolled space
        int y = top;

        _sub.SetBounds(x, y, cw, 18);
        y += 18 + 16;

        _targetLabel.SetBounds(x, y, cw, 20);
        y += 20 + 8;
        _targetBox.SetBounds(x, y, cw, 40);
        _targetValue.SetBounds(12, 0, Math.Max(0, _targetBox.Width - 40), 40);
        y += 40 + 18;

        _sizeLabel.SetBounds(x, y, cw, 20);
        y += 20 + 8;
        _sizeSeg.SetBounds(x, y, cw, 42);
        y += 42 + 18;

        _anchorLabel.SetBounds(x, y, cw, 20);
        y += 20 + 8;
        _anchorGrid.SetBounds(x, y, cw, _anchorGrid.Height);
        y += _anchorGrid.Height + 18;

        _opacityLabel.SetBounds(x, y, cw - 60, 20);
        _opacityValue.SetBounds(x + cw - 60, y, 60, 20);
        y += 20 + 6;
        _opacitySlider.SetBounds(x, y, cw, 24);
        y += 24 + 16;

        foreach (ModeRow row in new[] { _rowForward, _rowThrough, _rowLock, _rowBorder })
        {
            row.SetBounds(x, y, cw, 44);
            y += 44 + 6;
        }
        y += 6;
        _aotLabel.SetBounds(x, y, cw - 130, 34);
        _aotBox.SetBounds(x + cw - 120, y, 120, 34);
        y += 34 + 14;

        _hideUiBtn.SetBounds(x, y, cw, 44);
        y += 44 + 6;
        _hideUiHint.SetBounds(x, y, cw, 18);
        y += 18;

        AutoScrollMinSize = new Size(0, y - top + pad); // natural height (+ bottom pad) → vertical scrollbar when short
    }
}

/// <summary>A settings toggle row: label + description + <see cref="ToggleSwitch"/>.</summary>
[SupportedOSPlatform("windows")]
internal sealed class ModeRow : Panel
{
    private readonly Label _label = new();
    private readonly Label _desc = new();
    public ToggleSwitch Toggle { get; } = new();

    public ModeRow()
    {
        BackColor = Theme.PanelBg;
        _label.BackColor = Theme.PanelBg; _label.ForeColor = Theme.TextPrimary; _label.Font = Theme.BodySemi;
        _label.AutoSize = false; _label.TextAlign = ContentAlignment.MiddleLeft;
        _desc.BackColor = Theme.PanelBg; _desc.ForeColor = Theme.TextMuted; _desc.Font = Theme.Small;
        _desc.AutoSize = false; _desc.TextAlign = ContentAlignment.MiddleLeft;
        Controls.Add(_label);
        Controls.Add(_desc);
        Controls.Add(Toggle);
        Resize += (_, _) => Relayout();
    }

    public void SetText(string label, string desc) { _label.Text = label; _desc.Text = desc; }

    /// <summary>Disables the toggle and mutes the label together. The row itself stays Enabled —
    /// cascading Enabled=false onto the standard Labels would trigger WinForms' etched disabled text rendering,
    /// which fights the dark theme; muting the ForeColor keeps the palette consistent.</summary>
    public void SetRowEnabled(bool on)
    {
        if (Toggle.Enabled == on)
            return;
        Toggle.Enabled = on;
        _label.ForeColor = on ? Theme.TextPrimary : Theme.TextMuted;
    }

    private void Relayout()
    {
        _label.SetBounds(0, 2, Width - 60, 20);
        _desc.SetBounds(0, 22, Width - 60, 18);
        Toggle.SetBounds(Width - 46, (Height - 26) / 2, 46, 26);
    }
}
