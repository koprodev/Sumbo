using System;

namespace Sumbo.Core;

/// <summary>
/// Snap anchor relative to a monitor work area. 8 directional members + <see cref="Center"/>.
/// <para>
/// <b>Members are name-serialized — append new members at the tail only.</b> Reordering would
/// silently remap persisted <c>placement.anchor</c> strings and break profile restore.
/// </para>
/// </summary>
public enum SnapAnchor
{
    TopLeft,
    TopRight,
    BottomLeft,
    BottomRight,
    Top,
    Bottom,
    Left,
    Right,
    Center,
}

/// <summary>Sized clone modes. Fullscreen is handled at the UI layer (maximize).</summary>
public enum ClientSizeMode
{
    Source,
    Half,
    Quarter,
}

/// <summary>
/// Pure geometry for window sizing/positioning. UI-independent so it
/// is unit-testable without WinForms. Coordinates are screen pixels; the work area is
/// given as (left, top, right, bottom) so multi-monitor / negative origins are supported.
/// </summary>
public static class WindowPlacement
{
    /// <summary>
    /// Computes the window top-left so an outer window of <paramref name="w"/>×<paramref name="h"/>
    /// is anchored within the work area, clamped to stay on-screen. Pass the outer bounds size,
    /// not the client size.
    /// </summary>
    public static (int X, int Y) ComputeAnchoredLocation(
        SnapAnchor anchor, int w, int h, int waLeft, int waTop, int waRight, int waBottom)
    {
        int waWidth = waRight - waLeft;
        int waHeight = waBottom - waTop;

        int x = anchor switch
        {
            SnapAnchor.TopLeft or SnapAnchor.BottomLeft or SnapAnchor.Left => waLeft,
            SnapAnchor.TopRight or SnapAnchor.BottomRight or SnapAnchor.Right => waRight - w,
            SnapAnchor.Center => waLeft + (waWidth - w) / 2, // horizontally centered (explicit)
            _ => waLeft + (waWidth - w) / 2, // Top, Bottom → horizontally centered
        };

        int y = anchor switch
        {
            SnapAnchor.TopLeft or SnapAnchor.TopRight or SnapAnchor.Top => waTop,
            SnapAnchor.BottomLeft or SnapAnchor.BottomRight or SnapAnchor.Bottom => waBottom - h,
            SnapAnchor.Center => waTop + (waHeight - h) / 2, // vertically centered (explicit)
            _ => waTop + (waHeight - h) / 2, // Left, Right → vertically centered
        };

        // Clamp so the window never leaves the work area (oversize → pin to top-left).
        x = Math.Clamp(x, waLeft, Math.Max(waLeft, waRight - w));
        y = Math.Clamp(y, waTop, Math.Max(waTop, waBottom - h));
        return (x, y);
    }

    /// <summary>
    /// Computes the target client size for a sized mode, preserving the source aspect ratio
    /// and capping to the work area.
    /// </summary>
    public static (int Width, int Height) ComputeSizeMode(
        ClientSizeMode mode, int srcWidth, int srcHeight, int waWidth, int waHeight)
    {
        if (srcWidth <= 0 || srcHeight <= 0)
            return (Math.Min(640, Math.Max(1, waWidth)), Math.Min(400, Math.Max(1, waHeight)));

        (int w, int h) = mode switch
        {
            ClientSizeMode.Half => (srcWidth / 2, srcHeight / 2),
            ClientSizeMode.Quarter => (srcWidth / 4, srcHeight / 4),
            _ => (srcWidth, srcHeight),
        };

        w = Math.Max(1, w);
        h = Math.Max(1, h);

        // Cap to work area, preserving aspect (round to reduce truncation skew).
        if (w > waWidth || h > waHeight)
        {
            double scale = Math.Min((double)waWidth / w, (double)waHeight / h);
            w = Math.Max(1, (int)Math.Round(w * scale));
            h = Math.Max(1, (int)Math.Round(h * scale));
        }

        return (w, h);
    }
}
