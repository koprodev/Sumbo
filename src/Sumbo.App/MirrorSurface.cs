using System;
using System.Runtime.Versioning;
using Sumbo.Core;
using Sumbo.Native;

namespace Sumbo.App;

/// <summary>
/// The embedded DWM mirror engine — a non-Form component that owns the <see cref="ThumbnailSession"/> whose
/// destination is the HOST top-level window (the main window), fitting the source into a caller-supplied
/// physical host rect. Click forwarding and the region crop both map through the cached
/// <see cref="_lastDestRect"/>/<see cref="_lastActiveSource"/> pair.
/// <para>
/// DWM constraints: the destination handle MUST be a top-level window — a child control handle fails with
/// E_INVALIDARG — and the thumbnail composites over every child control intersecting the rect, so the host must
/// keep the mirror rect free of child controls.
/// </para>
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed class MirrorSurface : IDisposable
{
    private ThumbnailSession? _session;
    private IntPtr _hostTopLevel;    // host (main-window) handle — captured on Start, reused by RetargetPreserving
    private RECT _hostRect;          // last physical-pixel host rect (destination-window client coords)
    private RECT _lastDestRect;      // final rcDestination — the coordinate SSOT click forwarding/region map through
    private RECT _lastActiveSource;  // active source rect: the crop region when clipped, else the full source
    private Sumbo.Core.Region? _region; // fully qualified to disambiguate from System.Drawing.Region (global WinForms usings)
    private byte _opacity = 255;     // normal-mode opacity — the DWM thumbnail opacity channel
    private bool _overlay;           // overlay mode: opacity moves to the host's Form.Opacity channel
    private string _targetTitle = string.Empty;

    /// <summary>Raised after every start / stop / source-loss transition — the single chokepoint from which the
    /// host repaints the canvas and refreshes target labels.</summary>
    public event EventHandler? Changed;

    public bool HasMirror => _session is not null;
    public string TargetTitle => _targetTitle;

    /// <summary>The mirrored source window, <see cref="IntPtr.Zero"/> when idle — used to guard re-selecting the
    /// already-mirrored target (avoids a re-register flicker).</summary>
    public IntPtr TargetHandle { get; private set; }

    /// <summary>Mirror opacity in percent. This is a user setting, not session state — <see cref="Stop"/> keeps
    /// it for the next mirror.</summary>
    public int OpacityPercent { get; private set; } = 100;

    /// <summary>Sets the mirror opacity (clamped to 10~100%) and pushes it to the live thumbnail — the DWM opacity
    /// property updates in place through <c>UpdateDestination</c>, no re-register. No-op without a session beyond
    /// retaining the value.</summary>
    public void SetOpacity(int percent)
    {
        int p = Math.Clamp(percent, 10, 100);
        if (p == OpacityPercent)
            return;
        OpacityPercent = p;
        _opacity = (byte)Math.Round(p * 255.0 / 100.0);
        FitToHost();
    }

    /// <summary>
    /// Switches the opacity channel for overlay (hidden-UI) mode — one saved percent, two channels. In overlay
    /// the HOST window carries the translucency (<c>Form.Opacity</c>), so the thumbnail is pushed opaque here to
    /// avoid double attenuation; normal mode restores the DWM byte channel. The shell's visual-state route owns
    /// the Form.Opacity half of the invariant.
    /// </summary>
    public void SetOverlay(bool on)
    {
        if (_overlay == on)
            return;
        _overlay = on;
        FitToHost();
    }

    /// <summary>Active-source size in physical px (crop-aware — the region rect when clipped, else the full source).
    /// Size modes compute against this so the window matches what is shown.</summary>
    public bool TryGetActiveSourceSize(out int width, out int height)
    {
        width = 0;
        height = 0;
        if (_session is null)
            return false;

        SIZE source;
        try
        {
            source = _session.GetSourceSize();
        }
        catch
        {
            return false;
        }
        if (source.Cx <= 0 || source.Cy <= 0)
            return false;

        RECT active = CoordinateMapper.ActiveSource(_region, source.Cx, source.Cy);
        width = active.Width;
        height = active.Height;
        return true;
    }

    /// <summary>The last DWM destination size in physical px — the target-vs-actual check reads this after a
    /// size-mode apply. Size only; the rect coordinates stay encapsulated.</summary>
    public (int Width, int Height) LastDestSize => (_lastDestRect.Width, _lastDestRect.Height);

    /// <summary>The active crop region, null when unclipped. Session state — <see cref="Start"/> begins
    /// unclipped and <see cref="Stop"/> drops it (unlike <see cref="OpacityPercent"/>, which is a user setting).</summary>
    public Sumbo.Core.Region? CurrentRegion => _region;

    /// <summary>Sets (or clears, with null) the crop region and re-fits the thumbnail in place. Not a lifecycle
    /// transition — no <see cref="Changed"/>; the shell route reflects the panels itself.
    /// No-op when idle: the region is session state and a fresh <see cref="Start"/> begins unclipped.</summary>
    public void SetRegion(Sumbo.Core.Region? region)
    {
        if (_session is null)
            return;
        _region = region;
        FitToHost();
    }

    /// <summary>
    /// Commits a region drag given PHYSICAL host-client corner points:
    /// maps both corners through the cached <see cref="_lastDestRect"/>/<see cref="_lastActiveSource"/> pair (so a
    /// drag over an existing crop refines within it), ignores a stray click (&lt;4px source span, keeping
    /// the current region), and stores the region RELATIVE so it tracks a resizing source.
    /// Returns true when a new region was applied.
    /// </summary>
    public bool SetRegionFromDrag(int startX, int startY, int endX, int endY)
    {
        if (_session is null || _lastDestRect.Width <= 0 || _lastDestRect.Height <= 0)
            return false;

        SIZE source;
        try
        {
            source = _session.GetSourceSize();
        }
        catch
        {
            return false;
        }
        if (source.Cx <= 0 || source.Cy <= 0)
            return false;

        (int x1, int y1) = CoordinateMapper.MapDestToSourceClamped(startX, startY, _lastDestRect, _lastActiveSource);
        (int x2, int y2) = CoordinateMapper.MapDestToSourceClamped(endX, endY, _lastDestRect, _lastActiveSource);

        if (Math.Abs(x2 - x1) < 4 || Math.Abs(y2 - y1) < 4)
            return false;

        _region = Sumbo.Core.Region.RelativeFromSource(x1, y1, x2, y2, source.Cx, source.Cy);
        FitToHost();
        return true;
    }

    /// <summary>
    /// Click forwarding:
    /// maps a PHYSICAL host-client point through the cached <see cref="_lastDestRect"/>/<see cref="_lastActiveSource"/>
    /// pair (the coordinate SSOT) and posts <paramref name="msg"/> to the deepest real child under it. Returns whether
    /// the point mapped into the thumbnail (= the host consumes the event); <paramref name="posted"/> reports the
    /// <c>PostMessage</c> result so the shell can raise the one-shot unsupported-target notice.
    /// </summary>
    public bool TryForwardMouse(uint msg, int physX, int physY, IntPtr wParam, bool wheel, out bool posted)
    {
        posted = false;
        if (_session is null)
            return false;

        (int X, int Y)? mapped = CoordinateMapper.MapDestToSource(physX, physY, _lastDestRect, _lastActiveSource);
        if (mapped is null)
            return false; // letterbox margin — not over the thumbnail

        posted = PostToSource(msg, mapped.Value.X, mapped.Value.Y, wParam, wheel);
        return true;
    }

    /// <summary>
    /// Posts <paramref name="msg"/> to the deepest real child under the mapped point.
    /// The point is in <b>source-window</b> coordinates (<c>fSourceClientAreaOnly=false</c> shows the full window),
    /// so it is converted to screen via <see cref="User32.GetWindowRect"/> before descending. Mouse messages carry
    /// client coords in lParam; WM_MOUSEWHEEL carries screen coords.
    /// </summary>
    private bool PostToSource(uint msg, int srcWindowX, int srcWindowY, IntPtr wParam, bool wheel)
    {
        if (_session is null || !User32.GetWindowRect(_session.Source, out RECT wr))
            return false;

        var screen = new POINT(wr.Left + srcWindowX, wr.Top + srcWindowY);

        // Descend from the top-level source into the deepest real child containing the screen point.
        // RealChildWindowFromPoint expects parent-client coords, recomputed via ScreenToClient each level.
        IntPtr target = _session.Source;
        for (int depth = 0; depth < 16; depth++)
        {
            POINT clientPt = screen;
            User32.ScreenToClient(target, ref clientPt);
            IntPtr child = User32.RealChildWindowFromPoint(target, clientPt);
            if (child == IntPtr.Zero || child == target)
                break;
            target = child;
        }

        POINT lp = screen;
        if (!wheel)
            User32.ScreenToClient(target, ref lp);

        return User32.PostMessage(target, msg, wParam, MakeLParam(lp.X, lp.Y));
    }

    private static IntPtr MakeLParam(int low, int high)
        => new IntPtr((high << 16) | (low & 0xFFFF));

    /// <summary>
    /// Starts (or retargets) the mirror onto <paramref name="target"/>. Registers into a local first and swaps only
    /// on success, so a failed start (target closed between enumeration and click) keeps the current working mirror.
    /// <paramref name="error"/> carries the registration failure message for the host's
    /// notice dialog. Raises <see cref="Changed"/> once on success.
    /// </summary>
    public bool Start(IntPtr hostTopLevel, WindowInfo target, out string? error)
    {
        error = null;

        ThumbnailSession session;
        try
        {
            session = ThumbnailSession.Register(hostTopLevel, target.Handle);
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }

        try
        {
            SIZE src = session.GetSourceSize();
            if (src.Cx <= 0 || src.Cy <= 0)
            {
                session.Dispose();
                return false;
            }
        }
        catch (Exception ex)
        {
            session.Dispose();
            error = ex.Message;
            return false;
        }

        _session?.Dispose();
        _session = session;
        _hostTopLevel = hostTopLevel; // remembered for RetargetPreserving hops
        _targetTitle = target.Title;
        TargetHandle = target.Handle;
        _region = null; // a fresh target starts unclipped
        FitToHost();
        Changed?.Invoke(this, EventArgs.Empty);
        return true;
    }

    /// <summary>
    /// Group switching: swaps the DWM source to <paramref name="target"/> while PRESERVING the crop region and
    /// opacity (unlike <see cref="Start"/>, which begins a fresh unclipped mirror). Registers into a local and swaps
    /// only on success, so a hop to a closed member keeps the current mirror alive. Raises
    /// <see cref="Changed"/> once on success so the host reflects the new target label. No-op when idle.
    /// </summary>
    public bool RetargetPreserving(WindowInfo target, out string? error)
    {
        error = null;
        if (_session is null)
            return false; // switching applies to a live mirror only — when idle, the fresh Start path is used

        ThumbnailSession session;
        try
        {
            session = ThumbnailSession.Register(_hostTopLevel, target.Handle);
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false; // keep the current mirror; the next tick retries
        }

        try
        {
            SIZE src = session.GetSourceSize();
            if (src.Cx <= 0 || src.Cy <= 0)
            {
                session.Dispose();
                return false;
            }
        }
        catch (Exception ex)
        {
            session.Dispose();
            error = ex.Message;
            return false;
        }

        _session.Dispose();
        _session = session;
        _targetTitle = target.Title;
        TargetHandle = target.Handle;
        // _region / _opacity intentionally preserved across the hop
        FitToHost();
        Changed?.Invoke(this, EventArgs.Empty);
        return true;
    }

    /// <summary>Stops the mirror and returns to the idle state (explicit stop and source-loss share this path).
    /// Raises <see cref="Changed"/> when a session was actually torn down.</summary>
    public void Stop()
    {
        if (_session is null)
            return;

        _session.Dispose();
        _session = null;
        _targetTitle = string.Empty;
        TargetHandle = IntPtr.Zero;
        _region = null;
        _lastDestRect = default;
        _lastActiveSource = default;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Re-fits the thumbnail into <paramref name="hostPhysical"/> (called on resize / DPI change /
    /// side-panel expand-collapse).</summary>
    public void UpdateLayout(RECT hostPhysical)
    {
        _hostRect = hostPhysical;
        FitToHost();
    }

    /// <summary>Poll hook for prompt source-loss detection (the source closing does not notify the destination):
    /// probes the source and drops to the idle state when it is gone, so the canvas doesn't hold a dead frame.</summary>
    public void ValidateSource()
    {
        if (_session is null)
            return;

        try
        {
            SIZE src = _session.GetSourceSize();
            if (src.Cx <= 0 || src.Cy <= 0)
                Stop();
        }
        catch
        {
            Stop();
        }
    }

    /// <summary>Aspect-fit the active source into the host rect and push the DWM destination. Source loss during the
    /// probe/update is absorbed into <see cref="Stop"/>.</summary>
    private void FitToHost()
    {
        if (_session is null || _hostRect.Width <= 0 || _hostRect.Height <= 0)
            return;

        try
        {
            SIZE source = _session.GetSourceSize();
            if (source.Cx <= 0 || source.Cy <= 0)
                return;

            RECT active = CoordinateMapper.ActiveSource(_region, source.Cx, source.Cy);
            (int left, int top, int right, int bottom) =
                ThumbnailLayout.FitPreservingAspect(active.Width, active.Height, _hostRect.Width, _hostRect.Height);

            var dest = new RECT(_hostRect.Left + left, _hostRect.Top + top, _hostRect.Left + right, _hostRect.Top + bottom);
            // Always pass rcSource (the full-source rect when unclipped): DwmUpdateThumbnailProperties updates only
            // the flagged members, so dropping DWM_TNP_RECTSOURCE on a crop-clear would leave the prior crop's source
            // rect in place — the mirror would stay zoomed to the old region instead of returning to the full source.
            // Overlay pushes the thumbnail opaque (the host Form.Opacity carries the percent); normal mode uses the
            // DWM byte. One saved percent (_opacity), two channels.
            _session.UpdateDestination(dest, active, _overlay ? (byte)255 : _opacity);

            _lastDestRect = dest;
            _lastActiveSource = active;
        }
        catch
        {
            Stop();
        }
    }

    public void Dispose()
    {
        // Teardown only — no Changed raise; the host is going away with us.
        _session?.Dispose();
        _session = null;
    }
}
