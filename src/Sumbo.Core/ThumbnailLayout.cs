using System;

namespace Sumbo.Core;

/// <summary>
/// Pure geometry helper: fits a source rectangle inside a host area while
/// preserving the source aspect ratio (centered / letterboxed). UI-independent
/// so it is unit-testable without Win32.
/// </summary>
public static class ThumbnailLayout
{
    public static (int Left, int Top, int Right, int Bottom) FitPreservingAspect(
        int srcWidth, int srcHeight, int hostWidth, int hostHeight)
    {
        if (srcWidth <= 0 || srcHeight <= 0 || hostWidth <= 0 || hostHeight <= 0)
            return (0, 0, Math.Max(0, hostWidth), Math.Max(0, hostHeight));

        double srcAspect = (double)srcWidth / srcHeight;
        double hostAspect = (double)hostWidth / hostHeight;

        int width, height;
        if (hostAspect > srcAspect)
        {
            // Host is relatively wider → constrain by height.
            height = hostHeight;
            width = (int)Math.Round(hostHeight * srcAspect);
        }
        else
        {
            // Host is relatively taller (or equal) → constrain by width.
            width = hostWidth;
            height = (int)Math.Round(hostWidth / srcAspect);
        }

        int left = (hostWidth - width) / 2;
        int top = (hostHeight - height) / 2;
        return (left, top, left + width, top + height);
    }
}
