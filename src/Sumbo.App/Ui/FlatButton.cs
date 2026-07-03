using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace Sumbo.App.Ui;

/// <summary>Visual style for a <see cref="FlatButton"/> (디자인샘플.png의 버튼 계열).</summary>
internal enum ButtonKind
{
    /// <summary>Blue gradient primary CTA ("미러링 시작").</summary>
    Primary,
    /// <summary>Dark card-colored secondary button ("UI 숨기기", "프레임 숨기기").</summary>
    Dark,
    /// <summary>Transparent / dashed ghost button ("사용자 지정 추가").</summary>
    Ghost,
}

/// <summary>
/// A flat, rounded, owner-drawn button with an optional leading MDL2 glyph — matches the mockup's pill buttons.
/// Extends <see cref="Control"/> (not <see cref="Button"/>) for full paint control while inheriting the standard
/// <see cref="Control.Click"/> event and hover tracking.
/// </summary>
internal sealed class FlatButton : Control
{
    private ButtonKind _kind = ButtonKind.Dark;
    private string _glyph = string.Empty;
    private float _glyphSize = 11f;
    private bool _hover;
    private int _radius = Theme.SmallRadius;
    private bool _dashed;

    public FlatButton()
    {
        DoubleBuffered = true;
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.SupportsTransparentBackColor, true);
        BackColor = Color.Transparent;
        ForeColor = Theme.TextPrimary;
        Font = Theme.BodySemi;
        Cursor = Cursors.Hand;
        Height = 44;
    }

    public ButtonKind Kind { get => _kind; set { _kind = value; Invalidate(); } }
    public string Glyph { get => _glyph; set { _glyph = value ?? string.Empty; Invalidate(); } }
    public float GlyphSize { get => _glyphSize; set { _glyphSize = value; Invalidate(); } }
    public int Radius { get => _radius; set { _radius = value; Invalidate(); } }
    public bool Dashed { get => _dashed; set { _dashed = value; Invalidate(); } }
    public Color CornerBack { get; set; } = Theme.WindowBg;

    protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }

    // V2-D 이월 ②: the owner-drawn surface must reflect Enabled itself (no system disabled rendering).
    protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }

    protected override void OnPaint(PaintEventArgs e)
    {
        Graphics g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

        using (var back = new SolidBrush(CornerBack))
            g.FillRectangle(back, ClientRectangle);

        var r = new Rectangle(0, 0, Width, Height);
        Color textColor = Enabled ? ForeColor : Theme.TextMuted;

        switch (_kind)
        {
            case ButtonKind.Primary when Enabled:
                using (GraphicsPath path = Theme.RoundedRect(r, _radius))
                using (var brush = new LinearGradientBrush(r, Theme.Accent, Theme.AccentHi, LinearGradientMode.Horizontal))
                    g.FillPath(brush, path);
                textColor = Theme.TextOnAccent;
                break;
            case ButtonKind.Primary: // disabled CTA drops to a muted card (이월 ②)
                Theme.FillRounded(g, r, _radius, Theme.CardBg);
                Theme.DrawRounded(g, r, _radius, Theme.CardBorder);
                break;
            case ButtonKind.Dark:
                Theme.FillRounded(g, r, _radius, Enabled && _hover ? Theme.CardBgHover : Theme.CardBg);
                Theme.DrawRounded(g, r, _radius, Theme.CardBorder);
                break;
            case ButtonKind.Ghost:
                if (Enabled && _hover)
                    Theme.FillRounded(g, r, _radius, Theme.CardBg);
                var rr = new Rectangle(r.X, r.Y, r.Width - 1, r.Height - 1);
                using (GraphicsPath path = Theme.RoundedRect(rr, _radius))
                using (var pen = new Pen(Theme.CardBorder, 1) { DashStyle = _dashed ? DashStyle.Dash : DashStyle.Solid })
                    g.DrawPath(pen, path);
                textColor = Enabled ? Theme.TextSecondary : Theme.TextMuted;
                break;
        }

        DrawContent(g, r, textColor);
    }

    private void DrawContent(Graphics g, Rectangle r, Color textColor)
    {
        bool hasGlyph = _glyph.Length > 0;
        bool hasText = Text.Length > 0;

        using var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center, FormatFlags = StringFormatFlags.NoWrap, Trimming = StringTrimming.EllipsisCharacter };
        using var textBrush = new SolidBrush(textColor);

        if (hasGlyph && hasText)
        {
            using Font iconFont = Theme.IconFont(_glyphSize);
            SizeF gs = g.MeasureString(_glyph, iconFont);
            SizeF ts = g.MeasureString(Text, Font);
            const int gap = 8;
            float total = gs.Width + gap + ts.Width;
            float x = r.X + (r.Width - total) / 2f;
            float cy = r.Height / 2f;
            g.DrawString(_glyph, iconFont, textBrush, new PointF(x, cy - gs.Height / 2f));
            g.DrawString(Text, Font, textBrush, new PointF(x + gs.Width + gap, cy - ts.Height / 2f));
        }
        else if (hasGlyph)
        {
            using Font iconFont = Theme.IconFont(_glyphSize);
            g.DrawString(_glyph, iconFont, textBrush, r, sf);
        }
        else if (hasText)
        {
            g.DrawString(Text, Font, textBrush, r, sf);
        }
    }

    protected override void OnTextChanged(EventArgs e) { Invalidate(); base.OnTextChanged(e); }
}
