using System;
using Sumbo.Native;

namespace Sumbo.Core;

/// <summary>
/// Pure inverse of the thumbnail layout: maps a point in the clone's destination client area
/// back to a point in the source window (요건정의서 §8.4 단계 2). Shared by click forwarding
/// (FR-06) and region selection (FR-02).
/// <para>
/// The clone draws <see cref="ActiveSource"/> (the full source, or the selected region) into
/// <c>rcDestination</c> — a letterboxed sub-rect of the host client area. Mapping is therefore
/// relative to <b>rcDestination + activeSource</b>, not the full source (PEER 보완 1), so the
/// two stay consistent whether or not a region is active.
/// </para>
/// </summary>
public static class CoordinateMapper
{
    /// <summary>The source rectangle actually shown: the region resolved to pixels, else the full window.</summary>
    public static RECT ActiveSource(Region? region, int srcWidth, int srcHeight)
        => region?.ToSourceRect(srcWidth, srcHeight) ?? new RECT(0, 0, Math.Max(0, srcWidth), Math.Max(0, srcHeight));

    /// <summary>
    /// Maps a destination-client point to source-client coordinates, or <c>null</c> when the point
    /// falls in the letterbox margin / outside the rendered clone (no input to forward there).
    /// </summary>
    public static (int X, int Y)? MapDestToSource(int destX, int destY, RECT rcDestination, RECT activeSource)
    {
        if (rcDestination.Width <= 0 || rcDestination.Height <= 0)
            return null;
        if (destX < rcDestination.Left || destX > rcDestination.Right ||
            destY < rcDestination.Top || destY > rcDestination.Bottom)
            return null;

        return Project(destX, destY, rcDestination, activeSource);
    }

    /// <summary>
    /// Like <see cref="MapDestToSource"/> but clamps the point into the clone instead of rejecting it
    /// — used while dragging a selection rectangle so a drag past the edge selects up to the boundary.
    /// </summary>
    public static (int X, int Y) MapDestToSourceClamped(int destX, int destY, RECT rcDestination, RECT activeSource)
    {
        if (rcDestination.Width <= 0 || rcDestination.Height <= 0)
            return (activeSource.Left, activeSource.Top);

        int cx = Math.Clamp(destX, rcDestination.Left, rcDestination.Right);
        int cy = Math.Clamp(destY, rcDestination.Top, rcDestination.Bottom);
        return Project(cx, cy, rcDestination, activeSource);
    }

    private static (int X, int Y) Project(int destX, int destY, RECT rcDestination, RECT activeSource)
    {
        double u = (double)(destX - rcDestination.Left) / rcDestination.Width;
        double v = (double)(destY - rcDestination.Top) / rcDestination.Height;

        int srcX = activeSource.Left + (int)Math.Round(u * activeSource.Width);
        int srcY = activeSource.Top + (int)Math.Round(v * activeSource.Height);

        // Keep the result strictly inside the active source so it resolves to a real pixel/child.
        srcX = Math.Clamp(srcX, activeSource.Left, Math.Max(activeSource.Left, activeSource.Right - 1));
        srcY = Math.Clamp(srcY, activeSource.Top, Math.Max(activeSource.Top, activeSource.Bottom - 1));
        return (srcX, srcY);
    }
}
