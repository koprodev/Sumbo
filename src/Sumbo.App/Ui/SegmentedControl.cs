using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace Sumbo.App.Ui;

/// <summary>
/// Row of mutually-exclusive segment buttons. The selected segment fills with the accent color; the rest render
/// as dark cards with a subtle border. Raises <see cref="SelectedIndexChanged"/>.
/// </summary>
internal sealed class SegmentedControl : Control
{
    private string[] _items = Array.Empty<string>();
    private int _selectedIndex;
    private int _hoverIndex = -1;
    private const int Gap = 8;

    public event EventHandler? SelectedIndexChanged;

    public SegmentedControl()
    {
        DoubleBuffered = true;
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.SupportsTransparentBackColor, true);
        BackColor = Color.Transparent;
        Font = Theme.BodySemi;
        Height = 44;
        Cursor = Cursors.Hand;
    }

    public string[] Items { get => _items; set { _items = value ?? Array.Empty<string>(); _selectedIndex = Math.Min(_selectedIndex, Math.Max(0, _items.Length - 1)); Invalidate(); } }

    public int SelectedIndex
    {
        // -1 = no segment selected (a free/custom-sized mirror matches no preset; OnPaint then highlights none).
        // User clicks always set a real 0..len-1 index; -1 is only ever assigned programmatically.
        get => _selectedIndex;
        set { int v = value < 0 ? -1 : Math.Clamp(value, 0, Math.Max(0, _items.Length - 1)); if (v == _selectedIndex) return; _selectedIndex = v; Invalidate(); SelectedIndexChanged?.Invoke(this, EventArgs.Empty); }
    }

    private Rectangle CellRect(int i)
    {
        if (_items.Length == 0) return Rectangle.Empty;
        float cellW = (Width - Gap * (_items.Length - 1)) / (float)_items.Length;
        int x = (int)Math.Round(i * (cellW + Gap));
        int right = (int)Math.Round(i * (cellW + Gap) + cellW);
        return new Rectangle(x, 0, right - x, Height);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        int idx = -1;
        for (int i = 0; i < _items.Length; i++)
            if (CellRect(i).Contains(e.Location)) { idx = i; break; }
        if (idx != _hoverIndex) { _hoverIndex = idx; Invalidate(); }
        base.OnMouseMove(e);
    }

    protected override void OnMouseLeave(EventArgs e) { _hoverIndex = -1; Invalidate(); base.OnMouseLeave(e); }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        for (int i = 0; i < _items.Length; i++)
            if (CellRect(i).Contains(e.Location)) { SelectedIndex = i; break; }
        base.OnMouseDown(e);
    }

    // Owner-drawn surface must repaint on Enabled changes; there is no system disabled rendering.
    protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }

    protected override void OnPaint(PaintEventArgs e)
    {
        Graphics g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

        using var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center, Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap };
        for (int i = 0; i < _items.Length; i++)
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
            using var brush = new SolidBrush(fg);
            g.DrawString(_items[i], Font, brush, cell, sf);
        }
    }
}
