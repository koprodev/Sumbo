using System;
using System.Runtime.Versioning;
using Sumbo.Core;
using Sumbo.Native;

namespace Sumbo.App;

/// <summary>
/// The embedded DWM mirror engine (v2 단일 창 모델) — a non-Form component that owns the
/// <see cref="ThumbnailSession"/> whose destination is the HOST top-level window (the main window), fitting the
/// source into a caller-supplied physical host rect. Extracted from v1 <c>CloneForm.TryUpdateThumbnailLayout</c> /
/// <c>ResetToEmptyState</c> (tag <c>v1</c>); the input router / region crop / border inset seams arrive in
/// V2-C/D on top of the cached <see cref="_lastDestRect"/>/<see cref="_lastActiveSource"/> pair.
/// <para>
/// DWM constraints (v1 실측): the destination handle MUST be a top-level window — a child control handle fails with
/// E_INVALIDARG — and the thumbnail composites over every child control intersecting the rect, so the host must keep
/// the mirror rect child-free form surface.
/// </para>
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed class MirrorSurface : IDisposable
{
    private ThumbnailSession? _session;
    private IntPtr _hostTopLevel;    // host (main-window) handle — captured on Start, reused by RetargetPreserving (FR-08)
    private RECT _hostRect;          // last physical-pixel host rect (destination-window client coords)
    private RECT _lastDestRect;      // final rcDestination — the coordinate SSOT click-forward/region map through (V2-D)
    private RECT _lastActiveSource;  // full source for now; Region crop (FR-02) plugs in here in V2-C
    private Sumbo.Core.Region? _region; // 완전 수식 — WinForms 전역 using의 System.Drawing.Region과 모호성 회피
    private byte _opacity = 255;     // Q2 확정: 일반 모드 불투명도 = DWM thumbnail opacity channel (V2-B: SetOpacity)
    private bool _overlay;           // V2-D overlay mode: opacity moves to the host's Form.Opacity channel (Q2 확정)
    private string _targetTitle = string.Empty;

    /// <summary>Raised after every start / stop / source-loss transition so the host can repaint the canvas and
    /// refresh target labels from one chokepoint (v1 <c>ViewStateChanged</c> 축소 승계).</summary>
    public event EventHandler? Changed;

    public bool HasMirror => _session is not null;
    public string TargetTitle => _targetTitle;

    /// <summary>The mirrored source window, <see cref="IntPtr.Zero"/> when idle — used to guard re-selecting the
    /// already-mirrored target (no re-register flicker, v1 F2 승계).</summary>
    public IntPtr TargetHandle { get; private set; }

    /// <summary>Mirror opacity in percent (FR-05, Q2 확정: 일반 모드 = DWM thumbnail opacity channel). This is a
    /// user setting, not session state — <see cref="Stop"/> keeps it for the next mirror (프로필 영속은 V2-C).</summary>
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
    /// Switches the FR-05 opacity channel for the V2-D overlay (UI 숨김) mode — Q2 확정: one saved percent, two
    /// channels. In overlay the HOST window carries the translucency (<c>Form.Opacity</c>, 배후 투시), so the
    /// thumbnail is pushed opaque here to avoid double attenuation; normal mode restores the DWM byte channel.
    /// The shell's visual-state route owns the Form.Opacity half of the invariant ([2차] F4).
    /// </summary>
    public void SetOverlay(bool on)
    {
        if (_overlay == on)
            return;
        _overlay = on;
        FitToHost();
    }

    /// <summary>Active-source size in physical px (crop-aware — the region rect when clipped, else the full source).
    /// FR-03 size modes compute against this so the window matches what is shown (v1 ApplySizeMode 보완 1 승계).</summary>
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

    /// <summary>The last DWM destination size in physical px — the FR-03 target-vs-actual check reads this after a
    /// size-mode apply ([2차] F1). Size only; the rect coordinates stay encapsulated (V2-C Q1-① 경계 유지).</summary>
    public (int Width, int Height) LastDestSize => (_lastDestRect.Width, _lastDestRect.Height);

    /// <summary>The active crop region, null when unclipped (FR-02). Session state — <see cref="Start"/> begins
    /// unclipped and <see cref="Stop"/> drops it (unlike <see cref="OpacityPercent"/>, which is a user setting).</summary>
    public Sumbo.Core.Region? CurrentRegion => _region;

    /// <summary>Sets (or clears, with null) the crop region and re-fits the thumbnail in place. Not a lifecycle
    /// transition — no <see cref="Changed"/>; the shell route reflects the panels itself (V2-B <see cref="SetOpacity"/>
    /// 경계 승계). No-op when idle: the region is session state and a fresh <see cref="Start"/> begins unclipped.</summary>
    public void SetRegion(Sumbo.Core.Region? region)
    {
        if (_session is null)
            return;
        _region = region;
        FitToHost();
    }

    /// <summary>
    /// Commits a region drag given PHYSICAL host-client corner points (FR-02 — v1 <c>CommitRegionDrag</c> 승계):
    /// maps both corners through the cached <see cref="_lastDestRect"/>/<see cref="_lastActiveSource"/> pair (so a
    /// drag over an existing crop refines within it, as in v1), ignores a stray click (&lt;4px source span, keeping
    /// the current region), and stores the region RELATIVE so it tracks a resizing source (§7.3).
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
    /// FR-06 click forwarding (V2-D — v1 <c>CloneForm.ForwardMouse</c>/<c>PostToSource</c> 캡슐화 이식, [2차] Q2):
    /// maps a PHYSICAL host-client point through the cached <see cref="_lastDestRect"/>/<see cref="_lastActiveSource"/>
    /// pair (the coordinate SSOT) and posts <paramref name="msg"/> to the deepest real child under it. Returns whether
    /// the point mapped into the thumbnail (= the host consumes the event); <paramref name="posted"/> reports the
    /// <c>PostMessage</c> result so the shell can raise the one-shot unsupported-target notice (FR-06 실패 안내).
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
    /// Posts <paramref name="msg"/> to the deepest real child under the mapped point (v1 <c>PostToSource</c> 이식).
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
    /// on success, so a failed start (target closed between enumeration and click) keeps the current working mirror
    /// (v1 StartClone LOW 보완 승계). <paramref name="error"/> carries the registration failure message for the host's
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
        _hostTopLevel = hostTopLevel; // remember for FR-08 RetargetPreserving hops
        _targetTitle = target.Title;
        TargetHandle = target.Handle;
        _region = null; // a fresh target starts unclipped (v1 StartClone 승계)
        FitToHost();
        Changed?.Invoke(this, EventArgs.Empty);
        return true;
    }

    /// <summary>
    /// FR-08 그룹 순환: swaps the DWM source to <paramref name="target"/> while PRESERVING the crop region and
    /// opacity (unlike <see cref="Start"/>, which begins a fresh unclipped mirror). Registers into a local and swaps
    /// only on success, so a hop to a closed member keeps the current mirror alive (v1 <c>RetargetPreserving</c>
    /// :1298 캡슐화 이식, [2차] Q4 — the dest/source coordinate SSOT stays inside the surface). Raises
    /// <see cref="Changed"/> once on success so the host reflects the new target label. No-op when idle.
    /// </summary>
    public bool RetargetPreserving(WindowInfo target, out string? error)
    {
        error = null;
        if (_session is null)
            return false; // 순환은 라이브 미러 위에서만 — 유휴 시 fresh Start 경로 사용

        ThumbnailSession session;
        try
        {
            session = ThumbnailSession.Register(_hostTopLevel, target.Handle);
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false; // keep the current mirror; the next tick retries (v1 :1305 승계)
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
        // _region / _opacity intentionally preserved across the hop (v1 preserve-on-retarget 의미)
        FitToHost();
        Changed?.Invoke(this, EventArgs.Empty);
        return true;
    }

    /// <summary>Stops the mirror and returns to the idle state (explicit stop and source-loss share this path —
    /// v1 <c>ResetToEmptyState</c> 승계). Raises <see cref="Changed"/> when a session was actually torn down.</summary>
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

    /// <summary>Re-fits the thumbnail into <paramref name="hostPhysical"/> (called on resize / DPI change / side-panel
    /// expand-collapse — the v1 preview re-fit path promoted to the full mirror canvas).</summary>
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
    /// probe/update is absorbed into <see cref="Stop"/> (v1 TryUpdateThumbnailLayout 승계).</summary>
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
            // Q2 확정 채널 분리: overlay pushes the thumbnail opaque (the host Form.Opacity carries p%), normal
            // mode uses the DWM byte. One saved percent (_opacity), two channels ([2차] F4 invariant).
            _session.UpdateDestination(dest, _region is null ? null : active, _overlay ? (byte)255 : _opacity);

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
