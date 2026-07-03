using System;
using System.Drawing;
using System.Runtime.Versioning;
using System.Windows.Forms;
using Sumbo.Core;

namespace Sumbo.App.Ui.Panels;

/// <summary>
/// The display-settings panel: mirror layout only — size preset / screen anchor / opacity. Interaction modes
/// (click-forward/through, position lock, border, always-on-top, hide-UI) live in <see cref="BehaviorPanel"/>.
/// <para>
/// Pure view: it raises intent events and the shell routes them, reflecting the applied state back via
/// <see cref="ReflectMirror"/> under the <see cref="_syncing"/> guard — a control's programmatic setter fires its
/// change event, so an unguarded reflect would loop back.
/// </para>
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed class DisplayPanel : PanelView
{
    private readonly Label _sub = new();
    private readonly Label _sizeLabel = new();
    private readonly SegmentedControl _sizeSeg = new();
    private readonly Label _anchorLabel = new();
    private readonly AnchorGrid _anchorGrid = new();
    private readonly Label _opacityLabel = new();
    private readonly Label _opacityValue = new();
    private readonly SliderControl _opacitySlider = new();

    private bool _syncing; // guards the control handlers while ReflectMirror pushes shell → UI

    /// <summary>User moved the opacity slider (percent, 10~100). Shell route: ApplyOpacity → mirror + reflect.</summary>
    public event EventHandler<int>? OpacityChangeRequested;

    /// <summary>User picked a size segment — 0 source / 1 half / 2 quarter / 3 fullscreen. Shell maps to
    /// <see cref="ClientSizeMode"/> / maximize; a clamped apply reflects back as no selection.</summary>
    public event EventHandler<int>? SizeModeSelected;

    /// <summary>User picked an anchor cell — <see cref="AnchorGrid"/> index 0 = none, 1..9 = TL,T,TR,L,C,R,BL,B,BR.
    /// The shell maps to <see cref="SnapAnchor"/>?.</summary>
    public event EventHandler<int>? AnchorSelected;

    public DisplayPanel()
    {
        AutoScroll = true; // scroll instead of clipping on short windows

        _sub.BackColor = Theme.PanelBg; _sub.ForeColor = Theme.TextMuted; _sub.Font = Theme.Small;
        _sub.AutoSize = false; _sub.TextAlign = ContentAlignment.MiddleLeft;

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

        _opacitySlider.ValueChanged += (_, _) =>
        {
            _opacityValue.Text = _opacitySlider.Value + "%"; // label tracks the slider even during a reflect
            if (_syncing) return;
            OpacityChangeRequested?.Invoke(this, _opacitySlider.Value);
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

        // No mirror yet — the surface mirrors ReflectMirror's gates from the first SyncPanels push.
        _sizeSeg.Enabled = false;
        _anchorGrid.Enabled = false;
        _opacitySlider.Enabled = false;

        Controls.Add(_sub);
        Controls.Add(_sizeLabel);
        Controls.Add(_sizeSeg);
        Controls.Add(_anchorLabel);
        Controls.Add(_anchorGrid);
        Controls.Add(_opacityLabel);
        Controls.Add(_opacityValue);
        Controls.Add(_opacitySlider);
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

    /// <summary>Shell → panel: pushes the applied layout state into the live controls under the reentry guard. With
    /// no live mirror the surface is disabled; size/anchor also lock out while the position/size lock is on.</summary>
    public void ReflectMirror(bool hasMirror, MirrorViewState state)
    {
        _syncing = true;
        try
        {
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

            _sizeSeg.Enabled = hasMirror && !state.Locked;
            _anchorGrid.Enabled = hasMirror && !state.Locked;
            _opacitySlider.Enabled = hasMirror;
        }
        finally
        {
            _syncing = false;
        }
    }

    public override void ApplyStrings(LocalizationCatalog loc)
    {
        _sub.Text = loc.Get(LocKeys.Main_Display_Subtitle);
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

        AutoScrollMinSize = new Size(0, y - top + pad); // natural height (+ bottom pad) → vertical scrollbar when short
    }
}
