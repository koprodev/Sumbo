using System;
using System.Runtime.InteropServices;

namespace Sumbo.Native;

/// <summary>Win32 RECT (left, top, right, bottom).</summary>
[StructLayout(LayoutKind.Sequential)]
public struct RECT
{
    public int Left;
    public int Top;
    public int Right;
    public int Bottom;

    public RECT(int left, int top, int right, int bottom)
    {
        Left = left;
        Top = top;
        Right = right;
        Bottom = bottom;
    }

    public readonly int Width => Right - Left;
    public readonly int Height => Bottom - Top;
}

/// <summary>Win32 SIZE (cx, cy).</summary>
[StructLayout(LayoutKind.Sequential)]
public struct SIZE
{
    public int Cx;
    public int Cy;
}

/// <summary>Win32 POINT (x, y). Passed by value to WindowFromPoint / by ref to ClientToScreen.</summary>
[StructLayout(LayoutKind.Sequential)]
public struct POINT
{
    public int X;
    public int Y;

    public POINT(int x, int y)
    {
        X = x;
        Y = y;
    }
}

/// <summary>dwFlags values for <see cref="DWM_THUMBNAIL_PROPERTIES"/> (DWM_TNP_*).</summary>
[Flags]
public enum DwmThumbnailFlags : uint
{
    RectDestination = 0x00000001,
    RectSource = 0x00000002,
    Opacity = 0x00000004,
    Visible = 0x00000008,
    SourceClientAreaOnly = 0x00000010,
}

/// <summary>
/// DWM_THUMBNAIL_PROPERTIES. Win32 BOOL fields are marshalled as 4-byte BOOL
/// via <see cref="UnmanagedType.Bool"/> (PEER 보완 B).
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct DWM_THUMBNAIL_PROPERTIES
{
    public uint dwFlags;
    public RECT rcDestination;
    public RECT rcSource;
    public byte opacity;
    [MarshalAs(UnmanagedType.Bool)] public bool fVisible;
    [MarshalAs(UnmanagedType.Bool)] public bool fSourceClientAreaOnly;
}
