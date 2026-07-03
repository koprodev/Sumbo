using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace Sumbo.App.Ui;

/// <summary>
/// A rounded, optionally bordered container card (디자인샘플.png의 카드 표면). Owner-draws a rounded fill so the
/// corners blend into <see cref="CornerBack"/> (the surface the card sits on), then hosts child controls normally.
/// Used for the left target list, the preview frame, the quick-action / status blocks and the settings sections.
/// </summary>
internal class CardPanel : Panel
{
    private Color _cardColor = Theme.CardBg;
    private Color? _borderColor = Theme.CardBorder;
    private Color _cornerBack = Theme.WindowBg;
    private int _radius = Theme.CardRadius;

    public CardPanel()
    {
        DoubleBuffered = true;
        ResizeRedraw = true;
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint, true);
        BackColor = Theme.CardBg;
    }

    public Color CardColor { get => _cardColor; set { _cardColor = value; Invalidate(); } }
    public Color? BorderColorValue { get => _borderColor; set { _borderColor = value; Invalidate(); } }
    public Color CornerBack { get => _cornerBack; set { _cornerBack = value; Invalidate(); } }
    public int Radius { get => _radius; set { _radius = value; Invalidate(); } }

    protected override void OnPaint(PaintEventArgs e)
    {
        Graphics g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;

        // Erase the rectangular corners with the host surface color so the rounded card floats cleanly.
        using (var back = new SolidBrush(_cornerBack))
            g.FillRectangle(back, ClientRectangle);

        Theme.FillRounded(g, ClientRectangle, _radius, _cardColor);
        if (_borderColor is Color bc)
            Theme.DrawRounded(g, ClientRectangle, _radius, bc);

        base.OnPaint(e);
    }
}
