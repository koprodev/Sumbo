using System;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace Sumbo.App.Ui;

/// <summary>
/// Central design tokens (colors / fonts / metrics) for the M6 control-panel UI (디자인샘플.png). Single source
/// of truth so every custom-drawn control (<see cref="CardPanel"/>, <see cref="IconRail"/>, toggles, …) shares
/// one palette — the mockup is a dark, rounded, blue-accented dashboard, so the values below encode that theme.
/// <para>
/// Icons use the <b>Segoe Fluent Icons</b> / <b>Segoe MDL2 Assets</b> system font (present on Windows 10+), so the
/// pixel-faithful glyphs ship with the OS rather than as embedded assets. <see cref="Glyph"/> holds the code points.
/// </para>
/// </summary>
internal static class Theme
{
    // ── Surfaces (dark navy stack) ──
    public static readonly Color WindowBg = FromHex("#0E1119");     // outermost window fill
    public static readonly Color PanelBg = FromHex("#141925");      // right settings panel / rail column
    public static readonly Color CardBg = FromHex("#171D2B");       // rounded cards
    public static readonly Color CardBgHover = FromHex("#1C2434");  // card hover
    public static readonly Color InsetBg = FromHex("#10141E");      // inset fields (search box, preview)
    public static readonly Color CardBorder = FromHex("#252D3D");   // subtle card outline

    // ── Accent ──
    public static readonly Color Accent = FromHex("#3B5BFE");       // primary blue (buttons, active, toggles-on)
    public static readonly Color AccentHi = FromHex("#5B7BFF");     // gradient highlight / hover
    public static readonly Color AccentSoft = FromHex("#1E284A");   // active icon background wash

    // ── Text ──
    public static readonly Color TextPrimary = FromHex("#E8EAF1");  // headings / values
    public static readonly Color TextSecondary = FromHex("#9AA3B7");// labels
    public static readonly Color TextMuted = FromHex("#5E6678");    // exe names / hints
    public static readonly Color TextOnAccent = Color.White;

    // ── Status ──
    public static readonly Color Good = FromHex("#22C55E");         // running / ready green dot
    public static readonly Color ToggleTrackOff = FromHex("#2A3242");
    public static readonly Color ToggleKnob = Color.White;

    // ── Metrics ──
    public const int TitleBarHeight = 60;
    public const int IconRailWidth = 64;
    public const int SidePanelWidth = 360;
    public const int LeftColumnWidth = 372;
    public const int BottomBarHeight = 48;
    public const int CardRadius = 12;
    public const int SmallRadius = 8;
    public const int WindowRadius = 14;
    public const int Pad = 16;

    // ── Fonts ── (created once; long-lived for the app lifetime)
    public static readonly Font Brand = new("Segoe UI Semibold", 15f, FontStyle.Bold);
    public static readonly Font H1 = new("Segoe UI Semibold", 13f, FontStyle.Bold);
    public static readonly Font H2 = new("Segoe UI Semibold", 11f, FontStyle.Bold);
    public static readonly Font Body = new("Segoe UI", 10f, FontStyle.Regular);
    public static readonly Font BodySemi = new("Segoe UI Semibold", 10f, FontStyle.Bold);
    public static readonly Font Small = new("Segoe UI", 8.5f, FontStyle.Regular);
    public static readonly Font Value = new("Segoe UI Semibold", 10.5f, FontStyle.Bold);

    private static readonly string IconFamily = ResolveIconFamily();

    /// <summary>An icon font at <paramref name="size"/> pt (Segoe Fluent Icons, falling back to MDL2 Assets).</summary>
    public static Font IconFont(float size) => new(IconFamily, size, FontStyle.Regular);

    private static string ResolveIconFamily()
    {
        // Prefer Segoe Fluent Icons (Win11) then Segoe MDL2 Assets (Win10). Both expose the same code points used
        // in Glyph. Fall back to Segoe UI Symbol so text never renders as tofu on an older host.
        foreach (string family in new[] { "Segoe Fluent Icons", "Segoe MDL2 Assets", "Segoe UI Symbol" })
        {
            try
            {
                using var probe = new Font(family, 10f);
                if (string.Equals(probe.Name, family, StringComparison.OrdinalIgnoreCase))
                    return family;
            }
            catch
            {
                // try the next candidate
            }
        }
        return "Segoe UI Symbol";
    }

    /// <summary>Builds a rounded-rectangle path; a zero radius yields a plain rectangle.</summary>
    public static GraphicsPath RoundedRect(Rectangle r, int radius)
    {
        var path = new GraphicsPath();
        if (radius <= 0 || r.Width <= 0 || r.Height <= 0)
        {
            path.AddRectangle(r);
            return path;
        }

        int d = Math.Min(radius * 2, Math.Min(r.Width, r.Height));
        path.AddArc(r.X, r.Y, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    public static void FillRounded(Graphics g, Rectangle r, int radius, Color fill)
    {
        using GraphicsPath path = RoundedRect(r, radius);
        using var brush = new SolidBrush(fill);
        g.FillPath(brush, path);
    }

    public static void DrawRounded(Graphics g, Rectangle r, int radius, Color border, int thickness = 1)
    {
        // Inset by half the pen width so the stroke stays inside the bounds (no clipped edge).
        var rr = new Rectangle(r.X, r.Y, r.Width - 1, r.Height - 1);
        using GraphicsPath path = RoundedRect(rr, radius);
        using var pen = new Pen(border, thickness);
        g.DrawPath(pen, path);
    }

    private static Color FromHex(string hex)
    {
        hex = hex.TrimStart('#');
        int r = Convert.ToInt32(hex.Substring(0, 2), 16);
        int g = Convert.ToInt32(hex.Substring(2, 2), 16);
        int b = Convert.ToInt32(hex.Substring(4, 2), 16);
        return Color.FromArgb(r, g, b);
    }
}

/// <summary>Segoe Fluent Icons / MDL2 Assets code points used by the shell (Private-Use-Area, given as
/// <c>\uXXXX</c> escapes for encoding stability).</summary>
internal static class Glyph
{
    public const string Search = "";       // magnifier
    public const string Refresh = "";      // circular arrows
    public const string ChevronDown = "";
    public const string ChevronRight = "";
    public const string Add = "";          // plus
    public const string More = "";         // ⋯
    public const string Close = "";        // ✕ (panel collapse)
    public const string ChromeClose = "";  // window close
    public const string Minimize = "";
    public const string Maximize = "";
    public const string Restore = "";
    public const string Play = "";         // ▶ mirror start
    public const string Hide = "";         // eye-off (UI 숨기기)
    public const string Frame = "";        // full-screen / window frame
    public const string Star = "";         // filled star (active profile)
    public const string Settings = "";     // gear
    public const string Contact = "";      // person / account
    public const string GridView = "";     // layout / all-apps
    public const string Crop = "";         // region select
    public const string Monitor = "";      // display (TV/monitor)
    public const string Keyboard = "";     // hotkeys
    public const string Switch = "";       // group rotation (sync)
    public const string Lightning = "";    // quick actions (light)
    public const string Info = "";         // about
    public const string Globe = "";        // web (chrome placeholder icon)
    public const string Delete = "";       // trash can (saved region/profile row delete)
}
