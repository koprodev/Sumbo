using System;

namespace Sumbo.Core;

/// <summary>
/// Pure geometry helpers for the FR-15 경계 강조(테두리) frame. The DWM destination is measured in physical
/// pixels while WinForms <c>OnPaint</c> draws in logical (DIP) client coordinates, so the border needs a
/// physical-pixel inset (to reserve the frame margin) and a physical→logical rect transform (to paint the
/// frame around the thumbnail). Kept UI-independent so the DPI math is unit-testable (요건정의서 §14.1).
/// </summary>
public static class BorderMetrics
{
    /// <summary>
    /// Scales a logical (DIP) border thickness to physical pixels for the DWM destination inset. Returns 0
    /// when any extent is non-positive, else at least 1 physical pixel so an enabled border is never invisible.
    /// </summary>
    public static int PhysicalThickness(int dipThickness, int physicalExtent, int logicalExtent)
    {
        if (dipThickness <= 0 || physicalExtent <= 0 || logicalExtent <= 0)
            return 0;

        int scaled = (int)Math.Round((double)dipThickness * physicalExtent / logicalExtent, MidpointRounding.AwayFromZero);
        return Math.Max(1, scaled);
    }

    /// <summary>
    /// Converts a physical-pixel rect (DWM client coords) to a logical WinForms client rect for painting, with
    /// independent x/y scale and zero guards (PEER F4). Returns <c>(Left, Top, Width, Height)</c>; width/height
    /// are clamped to ≥ 0.
    /// </summary>
    public static (int Left, int Top, int Width, int Height) ToLogicalRect(
        int physicalLeft, int physicalTop, int physicalRight, int physicalBottom,
        int physicalWidth, int physicalHeight, int logicalWidth, int logicalHeight)
    {
        if (physicalWidth <= 0 || physicalHeight <= 0)
            return (0, 0, Math.Max(0, logicalWidth), Math.Max(0, logicalHeight));

        double scaleX = (double)logicalWidth / physicalWidth;
        double scaleY = (double)logicalHeight / physicalHeight;

        int left = (int)Math.Round(physicalLeft * scaleX, MidpointRounding.AwayFromZero);
        int top = (int)Math.Round(physicalTop * scaleY, MidpointRounding.AwayFromZero);
        int right = (int)Math.Round(physicalRight * scaleX, MidpointRounding.AwayFromZero);
        int bottom = (int)Math.Round(physicalBottom * scaleY, MidpointRounding.AwayFromZero);

        return (left, top, Math.Max(0, right - left), Math.Max(0, bottom - top));
    }
}
