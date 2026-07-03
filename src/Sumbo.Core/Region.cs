using System;
using Sumbo.Native;

namespace Sumbo.Core;

/// <summary>
/// A sub-region of a source window for partial cloning.
/// <para>
/// <b>Absolute</b> mode stores source pixel coordinates; <b>Relative</b> mode stores ratios
/// (0.0~1.0) of the source size so the region tracks proportionally when the source is resized.
/// <see cref="ToSourceRect"/> resolves either mode to a concrete <see cref="RECT"/> for
/// <c>DWM_TNP_RECTSOURCE</c>.
/// </para>
/// Init-only properties + parameterless construction so <c>System.Text.Json</c> round-trips it.
/// </summary>
public sealed class Region
{
    public bool Relative { get; init; }
    public double Left { get; init; }
    public double Top { get; init; }
    public double Right { get; init; }
    public double Bottom { get; init; }

    /// <summary>Builds an absolute (source pixel) region, normalizing corner order.</summary>
    public static Region Absolute(int left, int top, int right, int bottom) => new()
    {
        Relative = false,
        Left = Math.Min(left, right),
        Top = Math.Min(top, bottom),
        Right = Math.Max(left, right),
        Bottom = Math.Max(top, bottom),
    };

    /// <summary>
    /// Builds a relative region from a pixel rectangle measured against a source of
    /// <paramref name="srcWidth"/>×<paramref name="srcHeight"/> (ratios in [0,1]).
    /// </summary>
    public static Region RelativeFromSource(int left, int top, int right, int bottom, int srcWidth, int srcHeight)
    {
        if (srcWidth <= 0 || srcHeight <= 0)
            throw new ArgumentOutOfRangeException(nameof(srcWidth), "Source size must be positive.");

        int l = Math.Min(left, right);
        int r = Math.Max(left, right);
        int t = Math.Min(top, bottom);
        int b = Math.Max(top, bottom);

        return new Region
        {
            Relative = true,
            Left = Math.Clamp((double)l / srcWidth, 0.0, 1.0),
            Top = Math.Clamp((double)t / srcHeight, 0.0, 1.0),
            Right = Math.Clamp((double)r / srcWidth, 0.0, 1.0),
            Bottom = Math.Clamp((double)b / srcHeight, 0.0, 1.0),
        };
    }

    /// <summary>
    /// Resolves this region to source pixel coordinates, clamped to [0, source] and guaranteed
    /// to span at least 1px in each axis (degenerate selections never produce an empty rect).
    /// </summary>
    public RECT ToSourceRect(int srcWidth, int srcHeight)
    {
        if (srcWidth <= 0 || srcHeight <= 0)
            return new RECT(0, 0, Math.Max(0, srcWidth), Math.Max(0, srcHeight));

        int l = ResolveX(Left, srcWidth);
        int r = ResolveX(Right, srcWidth);
        int t = ResolveY(Top, srcHeight);
        int b = ResolveY(Bottom, srcHeight);

        if (r < l)
            (l, r) = (r, l);
        if (b < t)
            (t, b) = (b, t);

        // Guarantee a non-empty rect (pull the near edge in if pinned to the far boundary).
        if (r - l < 1)
        {
            if (l > 0) l -= 1;
            else r = Math.Min(srcWidth, l + 1);
        }
        if (b - t < 1)
        {
            if (t > 0) t -= 1;
            else b = Math.Min(srcHeight, t + 1);
        }

        return new RECT(l, t, r, b);
    }

    private int ResolveX(double value, int srcWidth)
    {
        double px = Relative ? value * srcWidth : value;
        return Math.Clamp((int)Math.Round(px), 0, srcWidth);
    }

    private int ResolveY(double value, int srcHeight)
    {
        double px = Relative ? value * srcHeight : value;
        return Math.Clamp((int)Math.Round(px), 0, srcHeight);
    }
}
