using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.Versioning;
using System.Windows.Forms;

namespace Sumbo.App.Ui;

/// <summary>
/// The overlay control dot — a tiny always-on-top handle floating at the mirror's top-left corner while the UI
/// is hidden. The overlay window itself can be click-through (<c>WS_EX_TRANSPARENT</c>), which leaves no mouse
/// path to it; this is a SEPARATE top-level window, so it keeps receiving input regardless: left-drag moves the
/// overlay, right-click opens the mirror menu. Deliberately small and amber so stray clicks are unlikely.
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed class OverlayGrip : Form
{
    /// <summary>Logical (96-dpi) badge size; the shell scales by its DeviceDpi when showing.</summary>
    public const int LogicalSize = 18;

    private static readonly Color Fill = Color.FromArgb(0xF5, 0x9E, 0x0B);   // amber
    private static readonly Color Ring = Color.FromArgb(0x7A, 0x4F, 0x06);

    private Point _dragAnchor; // screen coords of the last drag event
    private bool _dragging;

    /// <summary>Left-drag movement since the last raise, in screen pixels (packed as a Point).</summary>
    public event EventHandler<Point>? DragMoved;

    /// <summary>Right-click release — the shell opens the mirror context menu at the cursor.</summary>
    public event EventHandler? MenuRequested;

    public OverlayGrip()
    {
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        TopMost = true;
        // Everything painted in the key color vanishes AND lets clicks through — only the dot itself is hit.
        BackColor = Color.Black;
        TransparencyKey = Color.Black;
        Size = new Size(LogicalSize, LogicalSize);
        Cursor = Cursors.SizeAll;
        DoubleBuffered = true;
    }

    /// <summary>Tool window (no Alt-Tab entry) that never takes activation — the window under the overlay keeps
    /// keyboard focus while the user drags the dot.</summary>
    protected override CreateParams CreateParams
    {
        get
        {
            CreateParams cp = base.CreateParams;
            cp.ExStyle |= 0x08000000 /* WS_EX_NOACTIVATE */ | 0x00000080 /* WS_EX_TOOLWINDOW */;
            return cp;
        }
    }

    protected override bool ShowWithoutActivation => true;

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        Graphics g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;

        // Rounded amber badge with a hamburger glyph — reads as "menu/controls here", not a stray dot.
        var r = new Rectangle(1, 1, ClientSize.Width - 3, ClientSize.Height - 3);
        int radius = Math.Max(3, r.Width / 4);
        using var path = new GraphicsPath();
        path.AddArc(r.X, r.Y, radius * 2, radius * 2, 180, 90);
        path.AddArc(r.Right - radius * 2, r.Y, radius * 2, radius * 2, 270, 90);
        path.AddArc(r.Right - radius * 2, r.Bottom - radius * 2, radius * 2, radius * 2, 0, 90);
        path.AddArc(r.X, r.Bottom - radius * 2, radius * 2, radius * 2, 90, 90);
        path.CloseFigure();
        using (var fill = new SolidBrush(Fill))
            g.FillPath(fill, path);
        using (var ring = new Pen(Ring, 1f))
            g.DrawPath(ring, path);

        // Three menu bars, centered, scaled with the badge.
        float inset = r.Width * 0.28f;
        float x1 = r.X + inset, x2 = r.Right - inset;
        float cy = r.Y + r.Height / 2f;
        float gap = r.Height * 0.22f;
        using var bar = new Pen(Ring, Math.Max(1.5f, r.Height * 0.11f)) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        g.DrawLine(bar, x1, cy - gap, x2, cy - gap);
        g.DrawLine(bar, x1, cy, x2, cy);
        g.DrawLine(bar, x1, cy + gap, x2, cy + gap);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button != MouseButtons.Left)
            return;
        _dragging = true;
        _dragAnchor = Cursor.Position;
        Capture = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (!_dragging)
            return;
        Point now = Cursor.Position;
        if (now == _dragAnchor)
            return;
        var delta = new Point(now.X - _dragAnchor.X, now.Y - _dragAnchor.Y);
        _dragAnchor = now;
        DragMoved?.Invoke(this, delta);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (e.Button == MouseButtons.Left)
        {
            _dragging = false;
            Capture = false;
        }
        else if (e.Button == MouseButtons.Right)
        {
            MenuRequested?.Invoke(this, EventArgs.Empty);
        }
    }
}
