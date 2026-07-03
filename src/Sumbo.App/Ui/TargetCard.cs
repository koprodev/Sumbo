using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace Sumbo.App.Ui;

/// <summary>
/// A selectable target-process card in the left column (디자인샘플.png 좌측 대상 목록): app icon tile + name + exe +
/// a green "running" status. Selected → blue border + lighter fill. In M6-A these are static placeholders; the live
/// window list (icons via WindowIconProvider, real enumeration) arrives in M6-B. Raises <see cref="Selected"/>.
/// </summary>
internal sealed class TargetCard : CardPanel
{
    private bool _selected;
    private bool _hover;

    public event EventHandler? Selected;

    public string AppName { get; set; } = string.Empty;
    public string ExeName { get; set; } = string.Empty;
    public string StatusText { get; set; } = string.Empty;
    public string IconGlyph { get; set; } = Glyph.Globe;
    public Color IconColor { get; set; } = Theme.Accent;
    public Image? IconImage { get; set; }

    /// <summary>The window this card represents (M6-B). The provider owns <see cref="IconImage"/> — the card never
    /// disposes it (F2 보완).</summary>
    public Sumbo.Core.WindowInfo? Target { get; set; }

    public TargetCard()
    {
        Radius = Theme.CardRadius;
        Height = 76;
        Cursor = Cursors.Hand;
        UpdateVisual();
    }

    public bool IsSelected
    {
        get => _selected;
        set { if (_selected == value) return; _selected = value; UpdateVisual(); }
    }

    private void UpdateVisual()
    {
        CardColor = _selected ? Theme.CardBgHover : (_hover ? Theme.CardBgHover : Theme.CardBg);
        BorderColorValue = _selected ? Theme.Accent : Theme.CardBorder;
        Invalidate();
    }

    protected override void OnMouseEnter(EventArgs e) { _hover = true; UpdateVisual(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hover = false; UpdateVisual(); base.OnMouseLeave(e); }
    protected override void OnClick(EventArgs e) { IsSelected = true; Selected?.Invoke(this, EventArgs.Empty); base.OnClick(e); }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e); // card fill + border
        Graphics g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

        // ── Icon tile ──
        var tile = new Rectangle(14, (Height - 44) / 2, 44, 44);
        if (IconImage is not null)
        {
            using GraphicsPath clip = Theme.RoundedRect(tile, Theme.SmallRadius);
            var old = g.Clip;
            g.SetClip(clip, CombineMode.Replace);
            g.DrawImage(IconImage, tile);
            g.Clip = old;
        }
        else
        {
            Theme.FillRounded(g, tile, Theme.SmallRadius, Color.FromArgb(40, IconColor));
            using var ib = new SolidBrush(IconColor);
            using Font iconFont = Theme.IconFont(16f);
            using var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
            g.DrawString(IconGlyph, iconFont, ib, tile, sf);
        }

        // ── Running status geometry (measured first — its left edge caps the name/exe width) ──
        Font stFont = Theme.Small; // shared static font — do NOT dispose (a 'using' here corrupts every later use)
        bool hasStatus = StatusText.Length > 0;
        SizeF ss = hasStatus ? g.MeasureString(StatusText, stFont) : SizeF.Empty;
        float statusLeft = hasStatus
            ? Width - 16 - ss.Width - 14 // status text + its green dot
            : Width - 16;

        // ── Name + exe ── V2-B 겹침 폴리시: at 324px the status block starts at ~252 while the texts start at 72
        // unbounded — bound them to the status block's left edge and ellipsize instead of colliding.
        int textX = tile.Right + 14;
        float maxW = Math.Max(0f, statusLeft - 8 - textX);
        using var fmt = new StringFormat { FormatFlags = StringFormatFlags.NoWrap, Trimming = StringTrimming.EllipsisCharacter };
        using (var name = new SolidBrush(Theme.TextPrimary))
            g.DrawString(AppName, Theme.BodySemi, name, new RectangleF(textX, Height / 2f - 18, maxW, 20), fmt);
        using (var exe = new SolidBrush(Theme.TextMuted))
            g.DrawString(ExeName, Theme.Small, exe, new RectangleF(textX, Height / 2f + 2, maxW, 18), fmt);

        // ── Running status (right) ──
        if (hasStatus)
        {
            float sx = Width - 16 - ss.Width;
            float sy = Height / 2f - ss.Height / 2f;
            using (var dot = new SolidBrush(Theme.Good))
                g.FillEllipse(dot, sx - 14, sy + ss.Height / 2f - 3, 6, 6);
            using (var stb = new SolidBrush(Theme.Good))
                g.DrawString(StatusText, stFont, stb, new PointF(sx, sy));
        }
    }
}
