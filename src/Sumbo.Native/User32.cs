using System;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;

namespace Sumbo.Native;

/// <summary>
/// P/Invoke wrappers for window enumeration / identification (user32.dll).
/// x64-only build: the 64-bit <c>GetWindowLongPtrW</c> export is used directly;
/// the 32-bit GetWindowLong is intentionally not declared.
/// </summary>
[SupportedOSPlatform("windows")]
public static class User32
{
    /// <summary>GWL_EXSTYLE index for Get/SetWindowLongPtr.</summary>
    public const int GWL_EXSTYLE = -20;

    /// <summary>WS_EX_TOOLWINDOW — excluded from the cloneable window list.</summary>
    public const long WS_EX_TOOLWINDOW = 0x00000080;

    /// <summary>WS_EX_LAYERED — required for per-window alpha and click-through.</summary>
    public const long WS_EX_LAYERED = 0x00080000;

    /// <summary>WS_EX_TRANSPARENT — the window is skipped for hit-testing (click-through).</summary>
    public const long WS_EX_TRANSPARENT = 0x00000020;

    // ── Window messages ──────────────────────────────────────────────────
    public const int WM_HOTKEY = 0x0312;
    /// <summary>WM_DPICHANGED — top-level window moved to a monitor with a different DPI.</summary>
    public const int WM_DPICHANGED = 0x02E0;
    /// <summary>WM_SYSCOMMAND / WM_NCHITTEST — used by the position/size lock to block user move/resize/maximize.</summary>
    public const int WM_SYSCOMMAND = 0x0112;
    public const int WM_NCHITTEST = 0x0084;
    // SC_* system commands (WM_SYSCOMMAND wParam, masked with 0xFFF0).
    public const int SC_SIZE = 0xF000;
    public const int SC_MOVE = 0xF010;
    public const int SC_MINIMIZE = 0xF020;
    public const int SC_MAXIMIZE = 0xF030;
    // HT* hit-test codes (WM_NCHITTEST result). Caption + the eight resize borders are remapped to HTCLIENT when locked.
    public const int HTCLIENT = 1;
    public const int HTCAPTION = 2;
    public const int HTLEFT = 10;
    public const int HTRIGHT = 11;
    public const int HTTOP = 12;
    public const int HTTOPLEFT = 13;
    public const int HTTOPRIGHT = 14;
    public const int HTBOTTOM = 15;
    public const int HTBOTTOMLEFT = 16;
    public const int HTBOTTOMRIGHT = 17;
    public const uint WM_MOUSEMOVE = 0x0200;
    public const uint WM_LBUTTONDOWN = 0x0201;
    public const uint WM_LBUTTONUP = 0x0202;
    public const uint WM_LBUTTONDBLCLK = 0x0203;
    public const uint WM_RBUTTONDOWN = 0x0204;
    public const uint WM_RBUTTONUP = 0x0205;
    public const uint WM_MOUSEWHEEL = 0x020A;

    // ── RegisterHotKey modifiers (fsModifiers) ───────────────────────────
    public const uint MOD_ALT = 0x0001;
    public const uint MOD_CONTROL = 0x0002;
    public const uint MOD_SHIFT = 0x0004;
    public const uint MOD_WIN = 0x0008;
    public const uint MOD_NOREPEAT = 0x4000;

    // ── SetWindowPos flags ───────────────────────────────────────────────
    public const uint SWP_NOSIZE = 0x0001;
    public const uint SWP_NOMOVE = 0x0002;
    public const uint SWP_NOZORDER = 0x0004;
    public const uint SWP_NOACTIVATE = 0x0010;
    public const uint SWP_FRAMECHANGED = 0x0020;

    /// <summary>SetWindowPos hWndInsertAfter = topmost band; forces WS_EX_TOPMOST z-order (property set pre-Show can be lost).</summary>
    public static readonly IntPtr HWND_TOPMOST = new(-1);

    /// <summary>PostMessage target = all top-level windows; used to signal an already-running instance to surface.</summary>
    public static readonly IntPtr HWND_BROADCAST = new(0xffff);

    // ── WM_SIZING (interactive resize loop) ──────────────────────────────
    public const int WM_SIZING = 0x0214;
    public const int WMSZ_LEFT = 1;
    public const int WMSZ_RIGHT = 2;
    public const int WMSZ_TOP = 3;
    public const int WMSZ_TOPLEFT = 4;
    public const int WMSZ_TOPRIGHT = 5;
    public const int WMSZ_BOTTOM = 6;
    public const int WMSZ_BOTTOMLEFT = 7;
    public const int WMSZ_BOTTOMRIGHT = 8;

    /// <summary>AllowSetForegroundWindow dwProcessId wildcard — any process may take the foreground next.</summary>
    public const int ASFW_ANY = -1;

    public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern int GetWindowTextLength(IntPtr hWnd);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
    public static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    public static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetWindowPos(
        IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint uFlags);

    [DllImport("user32.dll")]
    public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    // ── Global hotkeys ───────────────────────────────────────────────────
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    // ── Click forwarding ─────────────────────────────────────────────────
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    /// <summary>Registers (or returns) a system-wide message id for a string — identical across processes, so a second
    /// instance and the running one agree on the "surface yourself" signal.</summary>
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern uint RegisterWindowMessage(string lpString);

    /// <summary>Hands the caller's foreground-activation right to another process. Without this the running
    /// instance's Activate() is foreground-locked and only flashes the taskbar instead of popping the window.</summary>
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool AllowSetForegroundWindow(int dwProcessId);

    /// <summary>Top-level window under a screen point.</summary>
    [DllImport("user32.dll")]
    public static extern IntPtr WindowFromPoint(POINT point);

    /// <summary>
    /// Deepest *real* child under a point given in the parent's client coordinates
    /// (skips transparent group boxes, unlike ChildWindowFromPoint).
    /// </summary>
    [DllImport("user32.dll")]
    public static extern IntPtr RealChildWindowFromPoint(IntPtr hwndParent, POINT ptParentClientCoords);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool ClientToScreen(IntPtr hWnd, ref POINT point);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool ScreenToClient(IntPtr hWnd, ref POINT point);

    /// <summary>Source window bounds in screen coords — maps DWM full-window points to the screen.</summary>
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);

    /// <summary>
    /// Client-area rectangle in <b>physical pixels</b> (origin 0,0), independent of WinForms
    /// logical scaling. Used to size the DWM <c>rcDestination</c> correctly under Per-Monitor V2
    /// DPI so the clone stays sharp / offset-free on any monitor.
    /// </summary>
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetClientRect(IntPtr hWnd, out RECT rect);

    /// <summary>Window class name — part of the durable target identity used for profile matching.</summary>
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);
}
