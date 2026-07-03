using System;
using System.Drawing;
using System.Windows.Forms;

namespace Sumbo.App.Ui;

/// <summary>
/// Profile tab chip: a pill with an optional leading glyph and an optional trailing star marking the active
/// profile. Active renders with the accent fill; others as a dark card with border. Measures its own natural
/// width for the caller's flow layout. Raises <see cref="Selected"/>.
/// </summary>
internal sealed class ProfileChip : Control
{
    private bool _active;
    private bool _hover;
    private string _glyph = string.Empty;
    private bool _showStar;

    public event EventHandler? Selected;

    public ProfileChip()
    {
        DoubleBuffered = true;
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.SupportsTransparentBackColor, true);
        BackColor = Color.Transparent;
        Font = Theme.BodySemi;
        Height = 40;
        Cursor = Cursors.Hand;
    }

    public bool Active { get => _active; set { _active = value; Invalidate(); } }
    public string Glyph { get => _glyph; set { _glyph = value ?? string.Empty; Invalidate(); } }
    public bool ShowStar { get => _showStar; set { _showStar = value; Invalidate(); } }
    public Color CornerBack { get; set; } = Theme.WindowBg;

    /// <summary>Measures the chip's natural width for the caller's flow layout.</summary>
    public int MeasureWidth(Graphics g)
    {
        int w = 28; // horizontal padding
        using Font iconFont = Theme.IconFont(11f);
        if (_glyph.Length > 0) w += (int)g.MeasureString(_glyph, iconFont).Width + 8;
        w += (int)g.MeasureString(Text, Font).Width;
        if (_showStar) w += (int)g.MeasureString(global::Sumbo.App.Ui.Glyph.Star, iconFont).Width + 8;
        return w;
    }


    protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnClick(EventArgs e) { Selected?.Invoke(this, EventArgs.Empty); base.OnClick(e); }
    protected override void OnTextChanged(EventArgs e) { Invalidate(); base.OnTextChanged(e); }

    protected override void OnPaint(PaintEventArgs e)
    {
        Graphics g = e.Graphics;
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

        using (var back = new SolidBrush(CornerBack))
            g.FillRectangle(back, ClientRectangle);

        var r = new Rectangle(0, 0, Width, Height);
        if (_active)
            Theme.FillRounded(g, r, Theme.SmallRadius, Theme.Accent);
        else
        {
            Theme.FillRounded(g, r, Theme.SmallRadius, _hover ? Theme.CardBgHover : Theme.CardBg);
            Theme.DrawRounded(g, r, Theme.SmallRadius, Theme.CardBorder);
        }

        Color fg = _active ? Theme.TextOnAccent : Theme.TextSecondary;
        float x = 14;
        float cy = Height / 2f;
        using var brush = new SolidBrush(fg);

        if (_glyph.Length > 0)
        {
            using Font iconFont = Theme.IconFont(11f);
            SizeF gs = g.MeasureString(_glyph, iconFont);
            g.DrawString(_glyph, iconFont, brush, new PointF(x, cy - gs.Height / 2f));
            x += gs.Width + 8;
        }

        SizeF ts = g.MeasureString(Text, Font);
        g.DrawString(Text, Font, brush, new PointF(x, cy - ts.Height / 2f));
        x += ts.Width + 8;

        if (_showStar)
        {
            using Font iconFont = Theme.IconFont(10f);
            using var star = new SolidBrush(_active ? Color.FromArgb(255, 214, 102) : Theme.TextMuted);
            SizeF ss = g.MeasureString(global::Sumbo.App.Ui.Glyph.Star, iconFont);
            g.DrawString(global::Sumbo.App.Ui.Glyph.Star, iconFont, star, new PointF(x, cy - ss.Height / 2f));
        }
    }
}
