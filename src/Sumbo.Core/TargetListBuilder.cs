using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Sumbo.Core;

/// <summary>
/// Pure, UI-independent helpers for the control-panel target list (M6-B): filtering the enumerated windows by a
/// search query and deriving their display strings. Kept in Core so the (non-Win32) list logic is unit-testable;
/// icon loading and the actual <c>WindowEnumerator</c> call stay in the App layer.
/// </summary>
public static class TargetListBuilder
{
    /// <summary>
    /// Filters <paramref name="windows"/> by a case-insensitive substring match on the window title, process name
    /// or executable file name, then orders the result by title (stable, case-insensitive). An empty/whitespace
    /// query returns every window (still ordered).
    /// </summary>
    public static IReadOnlyList<WindowInfo> Filter(IReadOnlyList<WindowInfo> windows, string? query)
    {
        ArgumentNullException.ThrowIfNull(windows);

        IEnumerable<WindowInfo> source = windows;
        string q = (query ?? string.Empty).Trim();
        if (q.Length > 0)
            source = windows.Where(w => Matches(w, q));

        return source
            .OrderBy(w => w.Title, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    private static bool Matches(WindowInfo w, string q)
        => Contains(w.Title, q) || Contains(w.ProcessName, q) || Contains(DisplayExe(w), q);

    private static bool Contains(string? value, string q)
        => !string.IsNullOrEmpty(value) && value.Contains(q, StringComparison.CurrentCultureIgnoreCase);

    /// <summary>The executable file name for the secondary row label (e.g. <c>chrome.exe</c>). Falls back to the
    /// process name with an <c>.exe</c> suffix when the full image path is unavailable (access denied).</summary>
    public static string DisplayExe(WindowInfo w)
    {
        ArgumentNullException.ThrowIfNull(w);
        if (!string.IsNullOrEmpty(w.ExecutablePath))
            return Path.GetFileName(w.ExecutablePath);
        return string.IsNullOrEmpty(w.ProcessName) ? string.Empty : w.ProcessName + ".exe";
    }
}
