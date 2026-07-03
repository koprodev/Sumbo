using System;
using System.Drawing;
using System.Runtime.Versioning;
using System.Windows.Forms;
using Sumbo.Core;

namespace Sumbo.App.Ui.Panels;

/// <summary>
/// The behavior panel: mirror interaction modes — click forwarding, click-through, position/size lock, canvas
/// border, always-on-top, and the hide-UI (overlay) entry. Split out of <see cref="DisplayPanel"/> (which keeps
/// size / anchor / opacity) so neither panel scrolls.
/// <para>
/// Pure view: it raises intent events and the shell routes them, reflecting the applied state back via
/// <see cref="ReflectMirror"/> under the <see cref="_syncing"/> guard — a toggle's programmatic setter fires its
/// change event, so an unguarded reflect would loop back.
/// </para>
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed class BehaviorPanel : PanelView
{
    private readonly Label _sub = new();
    private readonly ModeRow _rowForward = new();
    private readonly ModeRow _rowThrough = new();
    private readonly ModeRow _rowLock = new();
    private readonly ModeRow _rowBorder = new();
    private readonly ModeRow _rowAot = new();
    private readonly FlatButton _hideUiBtn = new();
    private readonly Label _hideUiHint = new();

    private bool _syncing; // guards the toggle handlers while ReflectMirror pushes shell → UI

    /// <summary>Click-forward toggle (desired state, not a flip — idempotent under reflect).</summary>
    public event EventHandler<bool>? ClickForwardToggleRequested;

    /// <summary>Click-through toggle. The shell's single route guards/undoes it.</summary>
    public event EventHandler<bool>? ClickThroughToggleRequested;

    /// <summary>Position/size lock toggle.</summary>
    public event EventHandler<bool>? LockToggleRequested;

    /// <summary>Canvas border-ring toggle.</summary>
    public event EventHandler<bool>? BorderToggleRequested;

    /// <summary>Always-on-top toggle (desired state — idempotent; the shell applies and reflects it back).</summary>
    public event EventHandler<bool>? AotToggleRequested;

    /// <summary>Hide-UI (overlay) entry. ESC / tray / Ctrl+Alt+C restore.</summary>
    public event EventHandler? HideUiRequested;

    public BehaviorPanel()
    {
        AutoScroll = true; // scroll instead of clipping on short windows

        _sub.BackColor = Theme.PanelBg; _sub.ForeColor = Theme.TextMuted; _sub.Font = Theme.Small;
        _sub.AutoSize = false; _sub.TextAlign = ContentAlignment.TopLeft; // wraps to 2 lines — long-locale safe

        _rowBorder.Toggle.Checked = true; // matches the canvas ring's initial ON state

        _hideUiBtn.Kind = ButtonKind.Dark; _hideUiBtn.CornerBack = Theme.PanelBg; _hideUiBtn.Font = Theme.BodySemi;
        _hideUiBtn.Glyph = Glyph.Hide; _hideUiBtn.GlyphSize = 12f;
        _hideUiHint.BackColor = Theme.PanelBg; _hideUiHint.ForeColor = Theme.TextMuted; _hideUiHint.Font = Theme.Small;
        _hideUiHint.AutoSize = false; _hideUiHint.TextAlign = ContentAlignment.MiddleLeft;

        _rowForward.Toggle.CheckedChanged += (_, _) => { if (!_syncing) ClickForwardToggleRequested?.Invoke(this, _rowForward.Toggle.Checked); };
        _rowThrough.Toggle.CheckedChanged += (_, _) => { if (!_syncing) ClickThroughToggleRequested?.Invoke(this, _rowThrough.Toggle.Checked); };
        _rowLock.Toggle.CheckedChanged += (_, _) => { if (!_syncing) LockToggleRequested?.Invoke(this, _rowLock.Toggle.Checked); };
        _rowBorder.Toggle.CheckedChanged += (_, _) => { if (!_syncing) BorderToggleRequested?.Invoke(this, _rowBorder.Toggle.Checked); };
        _rowAot.Toggle.CheckedChanged += (_, _) => { if (!_syncing) AotToggleRequested?.Invoke(this, _rowAot.Toggle.Checked); };
        _hideUiBtn.Click += (_, _) => HideUiRequested?.Invoke(this, EventArgs.Empty);

        // Mirror-dependent rows start disabled (ReflectMirror enables). Always-on-top is a window property that
        // applies with or without a mirror, so it is never gated.
        _rowForward.SetRowEnabled(false);
        _rowThrough.SetRowEnabled(false);
        _rowLock.SetRowEnabled(false);
        _rowBorder.SetRowEnabled(false);
        _hideUiBtn.Enabled = false;

        Controls.Add(_sub);
        foreach (ModeRow row in new[] { _rowForward, _rowThrough, _rowLock, _rowBorder, _rowAot })
            Controls.Add(row);
        Controls.Add(_hideUiBtn);
        Controls.Add(_hideUiHint);
    }

    /// <summary>Shell → panel: pushes the applied mode state into the live toggles under the reentry guard.
    /// Click-forward and click-through are mutually exclusive; click-through also needs its escape hotkey alive
    /// (<paramref name="clickThroughAvailable"/>). Always-on-top stays enabled regardless of the mirror.</summary>
    public void ReflectMirror(bool hasMirror, MirrorViewState state, bool clickThroughAvailable)
    {
        _syncing = true;
        try
        {
            _rowForward.Toggle.Checked = state.ClickForward;
            _rowThrough.Toggle.Checked = state.ClickThrough;
            _rowLock.Toggle.Checked = state.Locked;
            _rowBorder.Toggle.Checked = state.ShowBorder;
            _rowAot.Toggle.Checked = state.AlwaysOnTop;

            _rowForward.SetRowEnabled(hasMirror && !state.ClickThrough); // mutually exclusive with click-through
            _rowThrough.SetRowEnabled(hasMirror && clickThroughAvailable);
            _rowLock.SetRowEnabled(hasMirror);
            _rowBorder.SetRowEnabled(hasMirror);
            _hideUiBtn.Enabled = hasMirror; // overlay is a mirror-viewing mode — hiding an empty canvas is pointless
        }
        finally
        {
            _syncing = false;
        }
    }

    public override void ApplyStrings(LocalizationCatalog loc)
    {
        _sub.Text = loc.Get(LocKeys.Main_Behavior_Subtitle);
        _rowForward.SetText(loc.Get(LocKeys.Menu_Mode_ClickForward), loc.Get(LocKeys.Main_Mode_ClickForward_Desc));
        _rowThrough.SetText(loc.Get(LocKeys.Menu_Mode_ClickThrough), loc.Get(LocKeys.Main_Mode_ClickThrough_Desc));
        _rowLock.SetText(loc.Get(LocKeys.Menu_Mode_Lock), loc.Get(LocKeys.Main_Mode_Lock_Desc));
        _rowBorder.SetText(loc.Get(LocKeys.Menu_Mode_Border), loc.Get(LocKeys.Main_Mode_Border_Desc));
        _rowAot.SetText(loc.Get(LocKeys.Main_Display_AlwaysOnTop), loc.Get(LocKeys.Main_Mode_Aot_Desc));
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

        _sub.SetBounds(x, y, cw, 34);
        y += 34 + 12;

        foreach (ModeRow row in new[] { _rowForward, _rowThrough, _rowLock, _rowBorder, _rowAot })
        {
            row.SetBounds(x, y, cw, 44);
            y += 44 + 6;
        }
        y += 10;

        _hideUiBtn.SetBounds(x, y, cw, 44);
        y += 44 + 6;
        _hideUiHint.SetBounds(x, y, cw, 18);
        y += 18;

        AutoScrollMinSize = new Size(0, y - top + pad); // natural height (+ bottom pad) → vertical scrollbar when short
    }
}
