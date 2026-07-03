using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace Sumbo.App.Ui;

/// <summary>
/// The position-snap picker (디자인샘플.png의 "위치 고정") — a grid of anchor cells, each an owner-drawn mini-frame
/// icon with a label, laid out as the mockup: row0 = 해제/좌상/상/우상, row1 = 좌/가운데/우, row2 = 좌하/하/우하.
/// The selected cell is blue-filled. Raises <see cref="SelectedIndexChanged"/>. (Wired to <c>SnapAnchor</c> in M6-C;
/// "가운데" is a new center option reserved for that cycle.)
/// </summary>
internal sealed class AnchorGrid : Control
{
    /// <summary>Anchor kinds in <see cref="Labels"/> order.</summary>
    private static readonly AnchorKind[] Kinds =
    {
        AnchorKind.None, AnchorKind.TopLeft, AnchorKind.Top, AnchorKind.TopRight,
        AnchorKind.Left, AnchorKind.Center, AnchorKind.Right,
        AnchorKind.BottomLeft, AnchorKind.Bottom, AnchorKind.BottomRight,
    };
    private static readonly int[] RowCounts = { 4, 3, 3 };

    private string[] _labels = new string[10];
    private int _selectedIndex;
    private int _hoverIndex = -1;
    private const int Gap = 8;
    private const int RowH = 66;

    public event EventHandler? SelectedIndexChanged;

    public AnchorGrid()
    {
        DoubleBuffered = true;
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.SupportsTransparentBackColor, true);
        BackColor = Color.Transparent;
        Font = Theme.Small;
        Height = RowH * 3 + Gap * 2;
        Cursor = Cursors.Hand;
    }

    /// <summary>10 labels in the order None, TL, T, TR, L, C, R, BL, B, BR.</summary>
    public string[] Labels { get => _labels; set { _labels = value ?? new string[10]; Invalidate(); } }

    public int SelectedIndex
    {
        get => _selectedIndex;
        set { int v = Math.Clamp(value, 0, Kinds.Length - 1); if (v == _selectedIndex) return; _selectedIndex = v; Invalidate(); SelectedIndexChanged?.Invoke(this, EventArgs.Empty); }
    }

    private Rectangle CellRect(int index)
    {
        int row = 0, first = 0;
        while (row < RowCounts.Length && index >= first + RowCounts[row]) { first += RowCounts[row]; row++; }
        if (row >= RowCounts.Length) return Rectangle.Empty;
        int col = index - first;
        int count = RowCounts[row];
        float cellW = (Width - Gap * (count - 1)) / (float)count;
        int x = (int)Math.Round(col * (cellW + Gap));
        int right = (int)Math.Round(col * (cellW + Gap) + cellW);
        int y = row * (RowH + Gap);
        return new Rectangle(x, y, right - x, RowH);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        int idx = HitTest(e.Location);
        if (idx != _hoverIndex) { _hoverIndex = idx; Invalidate(); }
        base.OnMouseMove(e);
    }

    protected override void OnMouseLeave(EventArgs e) { _hoverIndex = -1; Invalidate(); base.OnMouseLeave(e); }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        int idx = HitTest(e.Location);
        if (idx >= 0) SelectedIndex = idx;
        base.OnMouseDown(e);
    }

    private int HitTest(Point p)
    {
        for (int i = 0; i < Kinds.Length; i++)
            if (CellRect(i).Contains(p)) return i;
        return -1;
    }

    // V2-D 이월 ②: the owner-drawn surface must reflect Enabled itself (no system disabled rendering).
    protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }

    protected override void OnPaint(PaintEventArgs e)
    {
        Graphics g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

        using var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center, FormatFlags = StringFormatFlags.NoWrap };
        for (int i = 0; i < Kinds.Length; i++)
        {
            Rectangle cell = CellRect(i);
            bool selected = i == _selectedIndex;
            if (selected)
                Theme.FillRounded(g, cell, Theme.SmallRadius, Enabled ? Theme.Accent : Theme.AccentSoft);
            else
            {
                Theme.FillRounded(g, cell, Theme.SmallRadius, Enabled && i == _hoverIndex ? Theme.CardBgHover : Theme.CardBg);
                Theme.DrawRounded(g, cell, Theme.SmallRadius, Theme.CardBorder);
            }

            Color fg = !Enabled ? Theme.TextMuted : selected ? Theme.TextOnAccent : Theme.TextSecondary;
            var iconBox = new Rectangle(cell.X + cell.Width / 2 - 12, cell.Y + 10, 24, 20);
            DrawAnchorIcon(g, iconBox, Kinds[i], fg);

            string label = i < _labels.Length ? _labels[i] ?? string.Empty : string.Empty;
            var textRect = new Rectangle(cell.X, cell.Bottom - 22, cell.Width, 18);
            using var brush = new SolidBrush(fg);
            g.DrawString(label, Font, brush, textRect, sf);
        }
    }

    private static void DrawAnchorIcon(Graphics g, Rectangle box, AnchorKind kind, Color color)
    {
        using var pen = new Pen(color, 1.6f);
        using var fill = new SolidBrush(color);
        var frame = new Rectangle(box.X, box.Y, box.Width - 1, box.Height - 1);
        using (GraphicsPath path = Theme.RoundedRect(frame, 3))
            g.DrawPath(pen, path);

        if (kind == AnchorKind.None)
        {
            // four corner ticks = "released"
            int t = 5;
            g.DrawLine(pen, frame.Left, frame.Top + t, frame.Left, frame.Top);
            g.DrawLine(pen, frame.Left, frame.Top, frame.Left + t, frame.Top);
            g.DrawLine(pen, frame.Right - t, frame.Top, frame.Right, frame.Top);
            g.DrawLine(pen, frame.Right, frame.Top, frame.Right, frame.Top + t);
            g.DrawLine(pen, frame.Left, frame.Bottom - t, frame.Left, frame.Bottom);
            g.DrawLine(pen, frame.Left, frame.Bottom, frame.Left + t, frame.Bottom);
            g.DrawLine(pen, frame.Right - t, frame.Bottom, frame.Right, frame.Bottom);
            g.DrawLine(pen, frame.Right, frame.Bottom, frame.Right, frame.Bottom - t);
            return;
        }

        // A small filled dot at the anchored position within the frame.
        const int m = 4, d = 6;
        int cx = kind switch
        {
            AnchorKind.TopLeft or AnchorKind.Left or AnchorKind.BottomLeft => frame.Left + m,
            AnchorKind.Top or AnchorKind.Center or AnchorKind.Bottom => frame.Left + frame.Width / 2 - d / 2,
            _ => frame.Right - m - d,
        };
        int cy = kind switch
        {
            AnchorKind.TopLeft or AnchorKind.Top or AnchorKind.TopRight => frame.Top + m,
            AnchorKind.Left or AnchorKind.Center or AnchorKind.Right => frame.Top + frame.Height / 2 - d / 2,
            _ => frame.Bottom - m - d,
        };
        g.FillEllipse(fill, cx, cy, d, d);
    }

    private enum AnchorKind { None, TopLeft, Top, TopRight, Left, Center, Right, BottomLeft, Bottom, BottomRight }
}
