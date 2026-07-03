using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.Versioning;
using System.Windows.Forms;

namespace Sumbo.App.Ui;

/// <summary>
/// Themed popup list for <see cref="SumboDropDown"/> — a borderless top-level window (like <see cref="OverlayGrip"/>)
/// so it escapes the side panel's clip bounds and composites above the mirror. Owner-drawn rows reuse the
/// <see cref="SegmentedControl"/> paint recipe. Dismisses on selection, Esc, or losing activation. Being a top-level
/// window it does not inherit the parent's font autoscale, so row metrics are scaled by the owner's DeviceDpi. When the
/// list is taller than the available screen space it caps its height and scrolls (wheel / keyboard) with a slim thumb.
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed class SumboDropDownPopup : Form
{
    private const int LogicalRow = 34;
    private const int LogicalVPad = 4;
    private const int LogicalGap = 4;    // vertical offset from the field
    private const int LogicalMargin = 8; // keep off the screen edge
    private const int LogicalScrollW = 4;

    private readonly string[] _items;
    private int _activeIndex;   // keyboard highlight (also the initial selection)
    private int _hoverIndex = -1;
    private int _rowH;
    private int _vpad;
    private int _scrollW;
    private int _viewportH;     // pixels available for rows (excludes vpad)
    private int _maxScroll;     // 0 when everything fits
    private int _scroll;        // current scroll offset in pixels

    /// <summary>Raised with the chosen row index; the owner applies the selection and closes this popup.</summary>
    public event EventHandler<int>? ItemChosen;

    public SumboDropDownPopup(string[] items, int selectedIndex)
    {
        _items = items ?? Array.Empty<string>();
        _activeIndex = selectedIndex >= 0 && selectedIndex < _items.Length ? selectedIndex : 0;

        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        BackColor = Theme.PanelBg;
        DoubleBuffered = true;
        Cursor = Cursors.Hand;
    }

    protected override CreateParams CreateParams
    {
        get
        {
            const int CS_DROPSHADOW = 0x00020000;
            const int WS_EX_TOOLWINDOW = 0x00000080; // no Alt-Tab entry
            CreateParams cp = base.CreateParams;
            cp.ClassStyle |= CS_DROPSHADOW;
            cp.ExStyle |= WS_EX_TOOLWINDOW;
            return cp;
        }
    }

    private static int Scale(int logical, int dpi) => (int)Math.Round(logical * dpi / 96.0);

    /// <summary>Positions the popup under <paramref name="field"/> (flipping above / capping height + scrolling when
    /// there is no room) and shows it owned by the field's form so it stays above and closes with it.</summary>
    public void ShowBelow(Control field)
    {
        Form? owner = field.FindForm();
        int dpi = field.DeviceDpi;
        _rowH = Scale(LogicalRow, dpi);
        _vpad = Scale(LogicalVPad, dpi);
        _scrollW = Scale(LogicalScrollW, dpi);
        int gap = Scale(LogicalGap, dpi);
        int margin = Scale(LogicalMargin, dpi);

        int width = field.Width;
        int contentH = _items.Length * _rowH;
        int fullH = _vpad * 2 + contentH;

        Rectangle wa = Screen.FromControl(field).WorkingArea;
        Point fieldTop = field.PointToScreen(Point.Empty);
        int belowY = fieldTop.Y + field.Height + gap;
        int spaceBelow = wa.Bottom - belowY - margin;
        int spaceAbove = fieldTop.Y - gap - wa.Top - margin;

        int height;
        bool placeBelow;
        if (spaceBelow >= fullH) { height = fullH; placeBelow = true; }
        else if (spaceAbove >= fullH) { height = fullH; placeBelow = false; }
        else
        {
            placeBelow = spaceBelow >= spaceAbove;
            int avail = Math.Max(placeBelow ? spaceBelow : spaceAbove, _vpad * 2 + _rowH);
            int rows = Math.Max(1, (avail - _vpad * 2) / _rowH); // whole rows only
            height = _vpad * 2 + rows * _rowH;
        }

        _viewportH = height - _vpad * 2;
        _maxScroll = Math.Max(0, contentH - _viewportH);
        EnsureVisible(_activeIndex);

        int x = Math.Max(wa.Left, Math.Min(fieldTop.X, wa.Right - width));
        int y = placeBelow ? belowY : fieldTop.Y - gap - height;

        Bounds = new Rectangle(x, y, width, height);
        using (GraphicsPath path = Theme.RoundedRect(new Rectangle(0, 0, Width, Height), Theme.SmallRadius))
            Region = new Region(path);

        if (owner is not null) Show(owner); else Show();
        Activate();
    }

    // Row rectangle in current (scrolled) client coordinates.
    private Rectangle RowRect(int i) => new(_vpad, _vpad + i * _rowH - _scroll, Width - _vpad * 2, _rowH);

    private Rectangle ViewportRect => new(_vpad, _vpad, Width - _vpad * 2, _viewportH);

    private int IndexAt(Point p)
    {
        if (!ViewportRect.Contains(p)) return -1;
        for (int i = 0; i < _items.Length; i++)
            if (RowRect(i).Contains(p)) return i;
        return -1;
    }

    private void EnsureVisible(int i)
    {
        int top = i * _rowH;
        int bottom = top + _rowH;
        if (top < _scroll) _scroll = top;
        else if (bottom > _scroll + _viewportH) _scroll = bottom - _viewportH;
        _scroll = Math.Clamp(_scroll, 0, _maxScroll);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        int idx = IndexAt(e.Location);
        if (idx != _hoverIndex) { _hoverIndex = idx; Invalidate(); }
    }

    protected override void OnMouseLeave(EventArgs e) { _hoverIndex = -1; Invalidate(); base.OnMouseLeave(e); }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);
        if (_maxScroll == 0) return;
        _scroll = Math.Clamp(_scroll - e.Delta / 120 * _rowH, 0, _maxScroll);
        _hoverIndex = IndexAt(PointToClient(Cursor.Position));
        Invalidate();
    }

    protected override void OnMouseClick(MouseEventArgs e)
    {
        base.OnMouseClick(e);
        if (e.Button != MouseButtons.Left) return;
        int idx = IndexAt(e.Location);
        if (idx >= 0) ItemChosen?.Invoke(this, idx);
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (_items.Length == 0) return base.ProcessCmdKey(ref msg, keyData);
        switch (keyData)
        {
            case Keys.Down: _activeIndex = (_activeIndex + 1) % _items.Length; _hoverIndex = -1; EnsureVisible(_activeIndex); Invalidate(); return true;
            case Keys.Up: _activeIndex = (_activeIndex - 1 + _items.Length) % _items.Length; _hoverIndex = -1; EnsureVisible(_activeIndex); Invalidate(); return true;
            case Keys.Enter: ItemChosen?.Invoke(this, _activeIndex); return true;
            case Keys.Escape: Close(); return true;
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    protected override void OnDeactivate(EventArgs e)
    {
        base.OnDeactivate(e);
        Close(); // dismiss when the user clicks away / focus leaves
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        Graphics g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

        var full = new Rectangle(0, 0, Width, Height);
        Theme.FillRounded(g, full, Theme.SmallRadius, Theme.PanelBg);

        int padX = Theme.Pad - 4;
        g.SetClip(ViewportRect); // clip partial rows at the top/bottom edges while scrolling
        using (var sf = new StringFormat { Alignment = StringAlignment.Near, LineAlignment = StringAlignment.Center, Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap })
        {
            for (int i = 0; i < _items.Length; i++)
            {
                Rectangle row = RowRect(i);
                if (row.Bottom < _vpad || row.Top > Height - _vpad) continue; // off-screen
                // Mouse hover wins; with no hover the keyboard-active row (initially the current selection) is highlighted.
                bool active = _hoverIndex >= 0 ? i == _hoverIndex : i == _activeIndex;
                Color fg;
                if (active) { Theme.FillRounded(g, row, Theme.SmallRadius, Theme.Accent); fg = Theme.TextOnAccent; }
                else fg = Theme.TextSecondary;

                var textRect = new Rectangle(row.X + padX, row.Y, Math.Max(0, row.Width - padX * 2), row.Height);
                using var brush = new SolidBrush(fg);
                g.DrawString(_items[i], Theme.BodySemi, brush, textRect, sf);
            }
        }
        g.ResetClip();

        if (_maxScroll > 0)
        {
            int trackH = _viewportH;
            int thumbH = Math.Max(_rowH, (int)((long)trackH * _viewportH / (_items.Length * _rowH)));
            int thumbY = _vpad + (int)((long)(trackH - thumbH) * _scroll / _maxScroll);
            var thumb = new Rectangle(Width - _vpad - _scrollW, thumbY, _scrollW, thumbH);
            Theme.FillRounded(g, thumb, _scrollW / 2, Theme.CardBorder);
        }

        Theme.DrawRounded(g, full, Theme.SmallRadius, Theme.CardBorder);
    }
}
