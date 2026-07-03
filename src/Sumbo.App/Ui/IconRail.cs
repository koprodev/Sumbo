using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace Sumbo.App.Ui;

/// <summary>One entry on the <see cref="IconRail"/> — an MDL2 glyph plus a tooltip and a stable id.</summary>
internal sealed record RailItem(string Id, string Glyph, string Tooltip);

/// <summary>
/// The far-right vertical icon bar (디자인샘플.png 우측 rail). Clicking an icon selects it (blue wash + accent bar)
/// and raises <see cref="ItemClicked"/> — the shell uses this to expand the matching side panel (사용자 원문:
/// "아이콘 메뉴 클릭시 메뉴 확장"). Owner-drawn; the active item shows a soft-blue rounded square and a right-edge bar.
/// </summary>
internal sealed class IconRail : Control
{
    private readonly List<RailItem> _items = new();
    private readonly ToolTip _tip = new() { InitialDelay = 400, ReshowDelay = 100 };
    private int _selectedIndex;
    private int _hoverIndex = -1;
    private const int ItemSize = 44;
    private const int ItemGap = 10;
    private const int TopPad = 18;

    /// <summary>Raised with the clicked item's id (also updates <see cref="SelectedIndex"/>).</summary>
    public event EventHandler<string>? ItemClicked;

    public IconRail()
    {
        DoubleBuffered = true;
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint, true);
        BackColor = Theme.PanelBg;
        Width = Theme.IconRailWidth;
        Cursor = Cursors.Hand;
    }

    public void SetItems(IEnumerable<RailItem> items)
    {
        _items.Clear();
        _items.AddRange(items);
        if (_selectedIndex >= _items.Count) _selectedIndex = 0;
        Invalidate();
    }

    public int SelectedIndex
    {
        get => _selectedIndex;
        set { int v = Math.Clamp(value, 0, Math.Max(0, _items.Count - 1)); if (v == _selectedIndex) return; _selectedIndex = v; Invalidate(); }
    }

    public string? SelectedId => _selectedIndex >= 0 && _selectedIndex < _items.Count ? _items[_selectedIndex].Id : null;

    /// <summary>Refreshes tooltips after a language switch (called by the shell's ApplyStrings).</summary>
    public void UpdateTooltips(IReadOnlyList<RailItem> items)
    {
        for (int i = 0; i < items.Count && i < _items.Count; i++)
            _items[i] = _items[i] with { Tooltip = items[i].Tooltip };
    }

    private Rectangle ItemRect(int i)
    {
        int x = (Width - ItemSize) / 2;
        int y = TopPad + i * (ItemSize + ItemGap);
        return new Rectangle(x, y, ItemSize, ItemSize);
    }

    private int HitTest(Point p)
    {
        for (int i = 0; i < _items.Count; i++)
            if (ItemRect(i).Contains(p)) return i;
        return -1;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        int idx = HitTest(e.Location);
        if (idx != _hoverIndex)
        {
            _hoverIndex = idx;
            _tip.SetToolTip(this, idx >= 0 ? _items[idx].Tooltip : string.Empty);
            Invalidate();
        }
        base.OnMouseMove(e);
    }

    protected override void OnMouseLeave(EventArgs e) { _hoverIndex = -1; Invalidate(); base.OnMouseLeave(e); }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        int idx = HitTest(e.Location);
        if (idx >= 0)
        {
            SelectedIndex = idx;
            ItemClicked?.Invoke(this, _items[idx].Id);
        }
        base.OnMouseDown(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        Graphics g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.Clear(Theme.PanelBg);

        using var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
        using Font iconFont = Theme.IconFont(15f);

        for (int i = 0; i < _items.Count; i++)
        {
            Rectangle r = ItemRect(i);
            bool selected = i == _selectedIndex;
            if (selected)
                Theme.FillRounded(g, r, Theme.SmallRadius, Theme.AccentSoft);
            else if (i == _hoverIndex)
                Theme.FillRounded(g, r, Theme.SmallRadius, Theme.CardBg);

            Color fg = selected ? Theme.AccentHi : (i == _hoverIndex ? Theme.TextPrimary : Theme.TextSecondary);
            using var brush = new SolidBrush(fg);
            g.DrawString(_items[i].Glyph, iconFont, brush, r, sf);

            if (selected)
            {
                // accent bar hugging the right edge of the rail
                using var bar = new SolidBrush(Theme.Accent);
                g.FillRectangle(bar, Width - 3, r.Y + 6, 3, r.Height - 12);
            }
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _tip.Dispose();
        base.Dispose(disposing);
    }
}
