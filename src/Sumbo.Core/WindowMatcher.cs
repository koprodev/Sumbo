using System;
using System.Collections.Generic;
using System.Linq;

namespace Sumbo.Core;

/// <summary>
/// Resolves a saved <see cref="TargetSpec"/> to a live window. Priority:
/// <c>processName+title → title → className</c>, then the explicit stored <see cref="TargetSpec.MatchBy"/>
/// as a last resort. Title matches are partial (contains, case-insensitive); processName/className
/// are exact. Returns null when nothing matches so the caller can prompt the user.
/// <para><see cref="MatchBy.Handle"/> is volatile — a saved handle can't identify a window
/// across sessions, so it is intentionally not resolved here; a Handle spec falls through to the captured
/// identity chain (title / processName / className).</para>
/// Pure — unit-tested without Win32.
/// </summary>
public static class WindowMatcher
{
    public static WindowInfo? Resolve(TargetSpec spec, IReadOnlyList<WindowInfo> windows)
    {
        if (spec is null || windows is null || windows.Count == 0)
            return null;

        // 1. processName + title (both present) — the most specific tier.
        if (!string.IsNullOrEmpty(spec.CapturedProcessName) && !string.IsNullOrEmpty(spec.CapturedTitle))
        {
            WindowInfo? m = windows.FirstOrDefault(w =>
                ProcessMatches(w, spec.CapturedProcessName) && TitleMatches(w, spec.CapturedTitle!));
            if (m is not null)
                return m;
        }

        // 2. title (partial).
        string? title = spec.CapturedTitle ?? (spec.MatchBy == MatchBy.Title ? spec.Value : null);
        if (!string.IsNullOrEmpty(title))
        {
            WindowInfo? m = windows.FirstOrDefault(w => TitleMatches(w, title!));
            if (m is not null)
                return m;
        }

        // 3. className (exact).
        string? className = spec.CapturedClassName ?? (spec.MatchBy == MatchBy.ClassName ? spec.Value : null);
        if (!string.IsNullOrEmpty(className))
        {
            WindowInfo? m = windows.FirstOrDefault(w => string.Equals(w.ClassName, className, StringComparison.Ordinal));
            if (m is not null)
                return m;
        }

        // 4. explicit processName matchBy fallback (exact).
        if (spec.MatchBy == MatchBy.ProcessName && !string.IsNullOrEmpty(spec.Value))
        {
            WindowInfo? m = windows.FirstOrDefault(w => ProcessMatches(w, spec.Value));
            if (m is not null)
                return m;
        }

        return null;
    }

    private static bool TitleMatches(WindowInfo w, string value)
        => !string.IsNullOrEmpty(w.Title) && w.Title.Contains(value, StringComparison.OrdinalIgnoreCase);

    private static bool ProcessMatches(WindowInfo w, string value)
        => !string.IsNullOrEmpty(w.ProcessName) && string.Equals(w.ProcessName, value, StringComparison.OrdinalIgnoreCase);
}
