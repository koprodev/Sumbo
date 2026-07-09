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
    private IntPtr _sourceHook;              // WinEvent hook on the source thread — fires the re-fit on source resize
    private User32.WinEventProc? _sourceHookProc; // kept alive: the native hook holds a raw pointer to this delegate
    private SIZE _lastFitSource;             // source size at the last fit — resize detection diffs against this

    /// <summary>Raised after every start / stop / source-loss transition — the single chokepoint from which the
    /// host repaints the canvas and refreshes target labels.</summary>
    public event EventHandler? Changed;

    /// <summary>Raised (on the UI thread) when the mirrored source window changed its own size and the thumbnail
    /// re-fit in place. The host reshapes the frame around the new aspect (overlay re-fit / size-preset re-apply).
    /// Distinct from <see cref="Changed"/>: the mirror stays on the same target — no lifecycle transition.</summary>
    public event EventHandler? SourceResized;

    /// <summary>Raised when <see cref="SourceDegraded"/> flips. The host swaps between the live thumbnail and a
    /// restore-the-window hint on the canvas.</summary>
    public event EventHandler? DegradedChanged;

    public bool HasMirror => _session is not null;
    public string TargetTitle => _targetTitle;

    /// <summary>True while DWM serves a small iconic ghost (app icon on a gradient) instead of the source's live
    /// surface — long-minimized Chromium windows do this once the browser's occlusion pause lets the surface be
    /// reclaimed. The thumbnail is hidden meanwhile; only restoring the source window brings frames back.</summary>
    public bool SourceDegraded { get; private set; }

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
        if (_session is null || SourceDegraded) // ghost size (~200px) must not drive size presets / overlay fits
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
        if (_session is null || SourceDegraded) // the coordinate map is ghost-based while degraded
            return false;

        (int X, int Y)? mapped = CoordinateMapper.MapDestToSource(physX, physY, _lastDestRect, _lastActiveSource);
        if (mapped is null)
            return false; // letterbox margin — not over the thumbnail

        posted = PostToSource(msg, mapped.Value.X, mapped.Value.Y, wParam, wheel);
        return true;
    }

    /// <summary>
    /// Posts <paramref name="msg"/> to the deepest input-capable child under the mapped point.
    /// The point is in <b>source-window</b> coordinates (<c>fSourceClientAreaOnly=false</c> shows the full window),
    /// so it is converted to screen via <see cref="User32.GetWindowRect"/> before descending. Mouse messages carry
    /// client coords in lParam; WM_MOUSEWHEEL carries screen coords.
    /// </summary>
    private bool PostToSource(uint msg, int srcWindowX, int srcWindowY, IntPtr wParam, bool wheel)
    {
        if (_session is null || !User32.GetWindowRect(_session.Source, out RECT wr))
            return false;

        var screen = new POINT(wr.Left + srcWindowX, wr.Top + srcWindowY);

        // Descend from the top-level source into the deepest input-capable child containing the screen point.
        // Manual z-order hit-test instead of ChildWindowFromPointEx/RealChildWindowFromPoint: the OS hit-tests
        // clip to the parent's client rect — a 160x28 stub for MINIMIZED sources whose children keep restored
        // offsets — and the "Real" variant returns disabled render-only overlays that swallow the click
        // (Chromium's "Intermediate D3D Window"). Screen-space rects stay affine-consistent even at -32000.
        IntPtr target = _session.Source;
        for (int depth = 0; depth < 16; depth++)
        {
            IntPtr child = TopChildAt(target, screen);
            if (child == IntPtr.Zero)
                break;
            target = child;
        }

        POINT lp = screen;
        if (!wheel)
            User32.ScreenToClient(target, ref lp);

        return User32.PostMessage(target, msg, wParam, MakeLParam(lp.X, lp.Y));
    }

    /// <summary>Top-most visible+enabled direct child containing the screen point; zero when none. Disabled
    /// children are render-only surfaces here — forwarding into them drops the input.</summary>
    private static IntPtr TopChildAt(IntPtr parent, POINT screen)
    {
        for (IntPtr c = User32.GetWindow(parent, User32.GW_CHILD); c != IntPtr.Zero; c = User32.GetWindow(c, User32.GW_HWNDNEXT))
        {
            if (!User32.IsWindowVisible(c) || !User32.IsWindowEnabled(c))
                continue;
            if (User32.GetWindowRect(c, out RECT r)
                && screen.X >= r.Left && screen.X < r.Right && screen.Y >= r.Top && screen.Y < r.Bottom)
                return c;
        }
        return IntPtr.Zero;
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
        SourceDegraded = false; // re-evaluated by the 1s poll against the new source
        InstallSourceHook(session.Source);
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
        SourceDegraded = false; // re-evaluated by the 1s poll against the new source
        InstallSourceHook(session.Source); // re-scope the resize hook to the new source's thread
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

        RemoveSourceHook();
        _session.Dispose();
        _session = null;
        _targetTitle = string.Empty;
        TargetHandle = IntPtr.Zero;
        _region = null;
        _lastDestRect = default;
        _lastActiveSource = default;
        _lastFitSource = default;
        SourceDegraded = false; // no DegradedChanged — Changed already repaints the (now idle) canvas
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
            else
                UpdateDegraded(src);
        }
        catch
        {
            Stop();
        }
    }

    /// <summary>Degraded = minimized source whose DWM size collapsed far below its restored geometry — the iconic
    /// ghost. Transition hides/reshows the thumbnail (FitToHost reads the flag) and notifies the host.</summary>
    private void UpdateDegraded(SIZE src)
    {
        bool degraded = false;
        if (_session is not null && User32.IsIconic(_session.Source))
        {
            var wp = new WINDOWPLACEMENT { Length = System.Runtime.InteropServices.Marshal.SizeOf<WINDOWPLACEMENT>() };
            if (User32.GetWindowPlacement(_session.Source, ref wp)
                && wp.NormalPosition.Width > 0 && wp.NormalPosition.Height > 0)
                degraded = src.Cx < wp.NormalPosition.Width / 2 && src.Cy < wp.NormalPosition.Height / 2;
        }

        if (degraded == SourceDegraded)
            return;
        SourceDegraded = degraded;
        FitToHost();
        DegradedChanged?.Invoke(this, EventArgs.Empty);
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
            _lastFitSource = source; // resize detection baseline — the source size this fit was computed against

            RECT active = CoordinateMapper.ActiveSource(_region, source.Cx, source.Cy);
            (int left, int top, int right, int bottom) =
                ThumbnailLayout.FitPreservingAspect(active.Width, active.Height, _hostRect.Width, _hostRect.Height);

            var dest = new RECT(_hostRect.Left + left, _hostRect.Top + top, _hostRect.Left + right, _hostRect.Top + bottom);
            // Always pass rcSource (the full-source rect when unclipped): DwmUpdateThumbnailProperties updates only
            // the flagged members, so dropping DWM_TNP_RECTSOURCE on a crop-clear would leave the prior crop's source
            // rect in place — the mirror would stay zoomed to the old region instead of returning to the full source.
            // Overlay pushes the thumbnail opaque (the host Form.Opacity carries the percent); normal mode uses the
            // DWM byte. One saved percent (_opacity), two channels. Degraded pushes 0 — the iconic ghost must not
            // paint over the host's restore-the-window hint.
            _session.UpdateDestination(dest, active, SourceDegraded ? (byte)0 : (_overlay ? (byte)255 : _opacity));

            _lastDestRect = dest;
            _lastActiveSource = active;
        }
        catch
        {
            Stop();
        }
    }

    /// <summary>Installs a WinEvent hook scoped to the source window's own UI thread so a source resize refits the
    /// mirror without waiting on the 1s source-loss poll. Best-effort: a failed hook simply forgoes live resize
    /// tracking (the mirror and the poll are unaffected). The delegate is retained in a field — the native hook
    /// stores a raw pointer to it, so letting it be collected would crash the callback.</summary>
    private void InstallSourceHook(IntPtr source)
    {
        RemoveSourceHook();
        uint tid = User32.GetWindowThreadProcessId(source, out uint pid);
        if (tid == 0)
            return;
        _sourceHookProc ??= OnSourceWinEvent;
        _sourceHook = User32.SetWinEventHook(
            User32.EVENT_OBJECT_LOCATIONCHANGE, User32.EVENT_OBJECT_LOCATIONCHANGE,
            IntPtr.Zero, _sourceHookProc, pid, tid, User32.WINEVENT_OUTOFCONTEXT);
    }

    private void RemoveSourceHook()
    {
        if (_sourceHook != IntPtr.Zero)
        {
            User32.UnhookWinEvent(_sourceHook);
            _sourceHook = IntPtr.Zero;
        }
    }

    /// <summary>WinEvent callback (UI thread, out-of-context). A LOCATIONCHANGE on the source top-level window that
    /// actually changed its size refits the thumbnail and notifies the host; pure moves (size unchanged) and
    /// child-object events are ignored.</summary>
    private void OnSourceWinEvent(IntPtr hook, uint ev, IntPtr hwnd, int idObject, int idChild, uint thread, uint time)
    {
        if (_session is null || hwnd != _session.Source
            || idObject != User32.OBJID_WINDOW || idChild != User32.CHILDID_SELF)
            return;

        SIZE size;
        try
        {
            size = _session.GetSourceSize();
        }
        catch
        {
            return; // source vanished mid-event — the 1s poll drives the teardown
        }
        if (size.Cx <= 0 || size.Cy <= 0)
            return; // gone — the 1s poll drives the teardown

        UpdateDegraded(size); // ghost swap / restore can announce itself as a size change — classify it first
        if (SourceDegraded)
            return; // the host shows the hint; don't reshape the frame around the ghost's aspect

        if (size.Cx == _lastFitSource.Cx && size.Cy == _lastFitSource.Cy)
            return; // a move with no size change (or a fit already applied by the degraded-clear transition)

        FitToHost(); // re-letterbox + refresh the click-forward coordinate map (updates _lastFitSource)
        if (_session is not null)
            SourceResized?.Invoke(this, EventArgs.Empty); // host reshapes the frame around the new aspect
    }

    public void Dispose()
    {
        // Teardown only — no Changed raise; the host is going away with us.
        RemoveSourceHook();
        _session?.Dispose();
        _session = null;
    }
}
