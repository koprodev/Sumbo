using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace Sumbo.App.Ui;

/// <summary>
/// Owner-drawn pill on/off switch — accent track + white knob when on, gray track when off.
/// Raises <see cref="CheckedChanged"/> on click.
/// </summary>
internal sealed class ToggleSwitch : Control
{
    private bool _checked;

    public event EventHandler? CheckedChanged;

    public ToggleSwitch()
    {
        DoubleBuffered = true;
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.SupportsTransparentBackColor, true);
        BackColor = Color.Transparent;
        Size = new Size(46, 26);
        Cursor = Cursors.Hand;
    }

    public bool Checked
    {
        get => _checked;
        set { if (_checked == value) return; _checked = value; Invalidate(); CheckedChanged?.Invoke(this, EventArgs.Empty); }
    }

    protected override void OnClick(EventArgs e) { Checked = !Checked; base.OnClick(e); }

    // Owner-drawn surface must repaint on Enabled changes; there is no system disabled rendering.
    protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }

    protected override void OnPaint(PaintEventArgs e)
    {
        Graphics g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;

        int h = Math.Min(Height, 26);
        int w = Math.Min(Width, 46);
        int top = (Height - h) / 2;
        var track = new Rectangle(0, top, w - 1, h - 1);

        // Disabled keeps the state readable: on-state dims to the soft accent wash instead of the full blue.
        Color trackColor = Enabled
            ? (_checked ? Theme.Accent : Theme.ToggleTrackOff)
            : (_checked ? Theme.AccentSoft : Theme.ToggleTrackOff);
        using (var brush = new SolidBrush(trackColor))
        using (GraphicsPath path = Theme.RoundedRect(track, h / 2))
            g.FillPath(brush, path);

        int knob = h - 8;
        int kx = _checked ? track.Right - knob - 4 : track.Left + 4;
        int ky = top + 4;
        using var kb = new SolidBrush(Enabled ? Theme.ToggleKnob : Theme.TextMuted);
        g.FillEllipse(kb, kx, ky, knob, knob);
    }
}
