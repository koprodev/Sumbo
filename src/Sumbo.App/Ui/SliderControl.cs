using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace Sumbo.App.Ui;

/// <summary>
/// Horizontal value slider: inset track, accent-filled portion, white knob. Draggable; raises
/// <see cref="ValueChanged"/>. Range is <see cref="Minimum"/>..<see cref="Maximum"/>.
/// </summary>
internal sealed class SliderControl : Control
{
    private int _min = 10;
    private int _max = 100;
    private int _value = 85;
    private bool _dragging;
    private const int Track = 6;
    private const int Knob = 16;

    public event EventHandler? ValueChanged;

    public SliderControl()
    {
        DoubleBuffered = true;
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.SupportsTransparentBackColor, true);
        BackColor = Color.Transparent;
        Height = 24;
        Cursor = Cursors.Hand;
    }

    public int Minimum { get => _min; set { _min = value; Invalidate(); } }
    public int Maximum { get => _max; set { _max = value; Invalidate(); } }

    public int Value
    {
        get => _value;
        set { int v = Math.Clamp(value, _min, _max); if (v == _value) return; _value = v; Invalidate(); ValueChanged?.Invoke(this, EventArgs.Empty); }
    }

    private int TrackLeft => Knob / 2;
    private int TrackRight => Width - Knob / 2;
    private int TrackWidth => Math.Max(1, TrackRight - TrackLeft);

    private void SetFromX(int x)
    {
        double t = (x - TrackLeft) / (double)TrackWidth;
        Value = (int)Math.Round(_min + t * (_max - _min));
    }

    protected override void OnMouseDown(MouseEventArgs e) { _dragging = true; SetFromX(e.X); base.OnMouseDown(e); }
    protected override void OnMouseMove(MouseEventArgs e) { if (_dragging) SetFromX(e.X); base.OnMouseMove(e); }
    protected override void OnMouseUp(MouseEventArgs e) { _dragging = false; base.OnMouseUp(e); }

    // Owner-drawn surface must repaint on Enabled changes; there is no system disabled rendering.
    protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }

    protected override void OnPaint(PaintEventArgs e)
    {
        Graphics g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;

        Color accent = Enabled ? Theme.Accent : Theme.AccentSoft; // disabled dims the filled portion + knob ring
        int cy = Height / 2;
        var full = new Rectangle(TrackLeft, cy - Track / 2, TrackWidth, Track);
        using (GraphicsPath path = Theme.RoundedRect(full, Track / 2))
        using (var b = new SolidBrush(Theme.ToggleTrackOff))
            g.FillPath(b, path);

        double t = (_value - _min) / (double)Math.Max(1, _max - _min);
        int fillW = (int)Math.Round(TrackWidth * t);
        if (fillW > 0)
        {
            var filled = new Rectangle(TrackLeft, cy - Track / 2, fillW, Track);
            using GraphicsPath path = Theme.RoundedRect(filled, Track / 2);
            using var b = new SolidBrush(accent);
            g.FillPath(b, path);
        }

        int kx = TrackLeft + fillW - Knob / 2;
        var knob = new Rectangle(kx, cy - Knob / 2, Knob, Knob);
        using (var kb = new SolidBrush(Enabled ? Theme.ToggleKnob : Theme.TextMuted))
            g.FillEllipse(kb, knob);
        using (var kp = new Pen(accent, 2))
            g.DrawEllipse(kp, knob.X + 1, knob.Y + 1, knob.Width - 2, knob.Height - 2);
    }
}
