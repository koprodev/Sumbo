using System;
using System.Runtime.Versioning;
using Sumbo.Native;

namespace Sumbo.Core;

/// <summary>
/// Owns the lifecycle of a single DWM thumbnail (register → update → unregister).
/// <para>
/// Both the destination and source handles MUST be top-level windows; passing a
/// child control handle returns E_INVALIDARG. The destination is the
/// top-level host window; the visible region is set via <see cref="UpdateDestination"/>.
/// </para>
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class ThumbnailSession : IDisposable
{
    private IntPtr _thumb;

    public IntPtr Destination { get; }
    public IntPtr Source { get; }

    private ThumbnailSession(IntPtr thumb, IntPtr destination, IntPtr source)
    {
        _thumb = thumb;
        Destination = destination;
        Source = source;
    }

    /// <param name="destTopLevel">Top-level host window handle.</param>
    /// <param name="srcTopLevel">Top-level source window handle to clone.</param>
    public static ThumbnailSession Register(IntPtr destTopLevel, IntPtr srcTopLevel)
    {
        int hr = Dwm.DwmRegisterThumbnail(destTopLevel, srcTopLevel, out IntPtr thumb);
        if (hr != Dwm.Ok)
        {
            throw new InvalidOperationException(
                $"DwmRegisterThumbnail failed (HRESULT 0x{hr:X8}). " +
                "Destination and source must both be top-level windows.");
        }

        return new ThumbnailSession(thumb, destTopLevel, srcTopLevel);
    }

    /// <summary>Current source window size, used to preserve aspect ratio.</summary>
    public SIZE GetSourceSize()
    {
        int hr = Dwm.DwmQueryThumbnailSourceSize(_thumb, out SIZE size);
        if (hr != Dwm.Ok)
            throw new InvalidOperationException($"DwmQueryThumbnailSourceSize failed (HRESULT 0x{hr:X8}).");

        return size;
    }

    /// <summary>
    /// Renders the clone into <paramref name="destination"/> (destination-window coordinates).
    /// When <paramref name="source"/> is supplied, only that sub-rectangle of the source window
    /// is shown (<c>DWM_TNP_RECTSOURCE</c>); otherwise the full window is shown.
    /// </summary>
    public void UpdateDestination(RECT destination, RECT? source = null, byte opacity = 255, bool visible = true)
    {
        var flags = DwmThumbnailFlags.RectDestination
                    | DwmThumbnailFlags.Opacity
                    | DwmThumbnailFlags.Visible;
        if (source is not null)
            flags |= DwmThumbnailFlags.RectSource;

        var props = new DWM_THUMBNAIL_PROPERTIES
        {
            dwFlags = (uint)flags,
            rcDestination = destination,
            rcSource = source ?? default,
            opacity = opacity,
            fVisible = visible,
            fSourceClientAreaOnly = false,
        };

        int hr = Dwm.DwmUpdateThumbnailProperties(_thumb, ref props);
        if (hr != Dwm.Ok)
            throw new InvalidOperationException($"DwmUpdateThumbnailProperties failed (HRESULT 0x{hr:X8}).");
    }

    public void Dispose()
    {
        if (_thumb != IntPtr.Zero)
        {
            Dwm.DwmUnregisterThumbnail(_thumb);
            _thumb = IntPtr.Zero;
        }
    }
}
