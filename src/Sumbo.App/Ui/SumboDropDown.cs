using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.Versioning;
using System.Windows.Forms;

namespace Sumbo.App.Ui;

/// <summary>
/// Collapsed single-select field that opens a themed popup list (<see cref="SumboDropDownPopup"/>). Owner-drawn to
/// match <see cref="SegmentedControl"/>; used where a segmented row would be too narrow (e.g. the language picker).
/// Mirrors SegmentedControl's contract — <see cref="Items"/> / <see cref="SelectedIndex"/> /
/// <see cref="SelectedIndexChanged"/> — so it is a drop-in swap. The <see cref="Items"/> setter is event-silent, so a
/// re-label rebuild never echoes an intent event; only a user pick raises <see cref="SelectedIndexChanged"/>.
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed class SumboDropDown : Control
{
    private const int ChevronZone = 30; // right-side square that holds the disclosure arrow (logical px)

    private string[] _items = Array.Empty<string>();
    private int _selectedIndex = -1;
    private bool _hover;
    private SumboDropDownPopup? _popup;
    private long _suppressReopenUntil; // the click that dismissed the popup must not immediately reopen it

    public event EventHandler? SelectedIndexChanged;

    public SumboDropDown()
    {
        DoubleBuffered = true;
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.SupportsTransparentBackColor, true);
        SetStyle(ControlStyles.Selectable, true);
        TabStop = true;
        BackColor = Color.Transparent;
        Font = Theme.BodySemi;
        Height = 42;
        Cursor = Cursors.Hand;
    }

    /// <summary>Item labels. Event-silent (clamps the selection, never raises <see cref="SelectedIndexChanged"/>).</summary>
    public string[] Items
    {
        get => _items;
        set { _items = value ?? Array.Empty<string>(); _selectedIndex = _items.Length == 0 ? -1 : Math.Clamp(_selectedIndex, 0, _items.Length - 1); Invalidate(); }
    }

    public int SelectedIndex
    {
        get => _selectedIndex;
        set
        {
            int v = value < 0 ? -1 : Math.Clamp(value, 0, Math.Max(0, _items.Length - 1));
            if (v == _selectedIndex) return;
            _selectedIndex = v;
            Invalidate();
            SelectedIndexChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button == MouseButtons.Left)
        {
            Focus();
            TogglePopup();
        }
    }

    // Keyboard access parity: Tab to the field, Enter/Down to open. Arrows/Enter/Esc then drive the popup.
    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (Focused && (keyData == Keys.Down || keyData == Keys.Enter))
        {
            OpenPopup();
            return true;
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
    protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }

    private void TogglePopup()
    {
        if (_popup is { Visible: true }) { ClosePopup(); return; }
        if (Environment.TickCount64 < _suppressReopenUntil) return;
        OpenPopup();
    }

    private void OpenPopup()
    {
        if (_items.Length == 0 || _popup is { Visible: true }) return;

        var popup = new SumboDropDownPopup(_items, _selectedIndex);
        popup.ItemChosen += (_, idx) => { ClosePopup(); SelectedIndex = idx; };
        popup.FormClosed += (_, _) =>
        {
            _suppressReopenUntil = Environment.TickCount64 + 250;
            if (ReferenceEquals(_popup, popup)) _popup = null;
            Invalidate();
        };
        _popup = popup;
        Invalidate();
        popup.ShowBelow(this);
    }

    private void ClosePopup()
    {
        SumboDropDownPopup? p = _popup;
        _popup = null;
        if (p is { IsDisposed: false }) p.Close();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        Graphics g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

        var r = new Rectangle(0, 0, Width, Height);
        Theme.FillRounded(g, r, Theme.SmallRadius, Enabled && _hover ? Theme.CardBgHover : Theme.CardBg);
        Theme.DrawRounded(g, r, Theme.SmallRadius, Focused && Enabled ? Theme.Accent : Theme.CardBorder);

        int padX = Theme.Pad - 4;
        var textRect = new Rectangle(padX, 0, Math.Max(0, Width - ChevronZone - padX), Height);
        string label = _selectedIndex >= 0 && _selectedIndex < _items.Length ? _items[_selectedIndex] : string.Empty;
        using (var sf = new StringFormat { Alignment = StringAlignment.Near, LineAlignment = StringAlignment.Center, Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap })
        using (var brush = new SolidBrush(Enabled ? Theme.TextPrimary : Theme.TextMuted))
            g.DrawString(label, Font, brush, textRect, sf);

        var chevRect = new Rectangle(Width - ChevronZone, 0, ChevronZone, Height);
        using (var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
        using (var iconFont = Theme.IconFont(9f))
        using (var brush = new SolidBrush(Enabled ? Theme.TextSecondary : Theme.TextMuted))
            g.DrawString(Glyph.ChevronDown, iconFont, brush, chevRect, sf);
    }

    protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }

    protected override void Dispose(bool disposing)
    {
        if (disposing) ClosePopup();
        base.Dispose(disposing);
    }
}
