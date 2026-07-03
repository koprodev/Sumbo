using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.Versioning;
using System.Text;
using Sumbo.Native;

namespace Sumbo.Core;


/// <summary>
/// Enumerates visible, titled, top-level windows that can be cloned.
/// Filters out tool windows and the calling process's own windows.
/// </summary>
[SupportedOSPlatform("windows")]
public static class WindowEnumerator
{
    public static IReadOnlyList<WindowInfo> GetCloneableWindows()
    {
        var result = new List<WindowInfo>();
        uint ownPid = (uint)Environment.ProcessId;
        // Resolve each process image path at most once per enumeration — many top-level windows share a pid, and
        // MainModule access is slow / can throw.
        var exeCache = new Dictionary<uint, string>();

        // The delegate is held in a local so it stays rooted for the duration
        // of the synchronous EnumWindows call.
        User32.EnumWindowsProc callback = (hWnd, _) =>
        {
            if (!User32.IsWindowVisible(hWnd))
                return true;

            int length = User32.GetWindowTextLength(hWnd);
            if (length == 0)
                return true;

            long exStyle = (long)User32.GetWindowLongPtr(hWnd, User32.GWL_EXSTYLE);
            if ((exStyle & User32.WS_EX_TOOLWINDOW) != 0)
                return true;

            User32.GetWindowThreadProcessId(hWnd, out uint pid);
            if (pid == ownPid)
                return true;

            var buffer = new StringBuilder(length + 1);
            User32.GetWindowText(hWnd, buffer, buffer.Capacity);
            string title = buffer.ToString();
            if (string.IsNullOrWhiteSpace(title))
                return true;

            result.Add(Describe(hWnd, title, exeCache));
            return true;
        };

        User32.EnumWindows(callback, IntPtr.Zero);
        GC.KeepAlive(callback);
        return result;
    }

    /// <summary>
    /// Captures the full identity (title/process/class) of a single window — used to build a durable
    /// <see cref="TargetSpec"/> when saving a profile or adding a group member.
    /// </summary>
    public static WindowInfo Describe(IntPtr hwnd)
    {
        int length = User32.GetWindowTextLength(hwnd);
        var buffer = new StringBuilder(length + 1);
        User32.GetWindowText(hwnd, buffer, buffer.Capacity);
        return Describe(hwnd, buffer.ToString(), exeCache: null);
    }

    private static WindowInfo Describe(IntPtr hwnd, string title, Dictionary<uint, string>? exeCache)
    {
        // Per-window capture failures (process exited, access denied) must not abort enumeration —
        // fall back to empty identifiers.
        string processName = string.Empty;
        string exePath = string.Empty;
        User32.GetWindowThreadProcessId(hwnd, out uint pid);
        if (pid != 0)
        {
            try
            {
                using Process process = Process.GetProcessById((int)pid);
                processName = process.ProcessName;
                exePath = ResolveExePath(pid, process, exeCache);
            }
            catch
            {
                // leave processName/exePath empty
            }
        }

        string className = string.Empty;
        try
        {
            var classBuffer = new StringBuilder(256);
            if (User32.GetClassName(hwnd, classBuffer, classBuffer.Capacity) > 0)
                className = classBuffer.ToString();
        }
        catch
        {
            // leave className empty
        }

        return new WindowInfo(hwnd, title, processName, className, pid, exePath);
    }

    /// <summary>
    /// Resolves the process image path (for the app-icon lookup), memoized per pid within one enumeration.
    /// The <c>MainModule</c> access can throw (protected / cross-arch process) or be slow, so failures are
    /// cached as an empty string to avoid retrying the same pid.
    /// </summary>
    private static string ResolveExePath(uint pid, Process process, Dictionary<uint, string>? exeCache)
    {
        if (exeCache is not null && exeCache.TryGetValue(pid, out string? cached))
            return cached;

        string path;
        try
        {
            path = process.MainModule?.FileName ?? string.Empty;
        }
        catch
        {
            path = string.Empty; // access denied / protected process — icon falls back to a placeholder
        }

        if (exeCache is not null)
            exeCache[pid] = path;
        return path;
    }
}
