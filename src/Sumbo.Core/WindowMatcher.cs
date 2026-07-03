using System;
using System.Collections.Generic;
using System.Linq;

namespace Sumbo.Core;

/// <summary>
/// Resolves a saved <see cref="TargetSpec"/> to a live window (FR-13 프로파일 복원, §7.4). Priority:
/// <c>processName+title → title → className</c>, then the explicit stored <see cref="TargetSpec.MatchBy"/>
/// as a last resort. Title matches are partial (contains, case-insensitive) per §7.4; processName/className
/// are exact. Returns null when nothing matches so the caller can prompt the user (§7.4 미발견 사용자 선택).
/// <para><see cref="MatchBy.Handle"/> is volatile (§7.4 세션 한정) — a saved handle can't identify a window
/// across sessions, so it is intentionally not resolved here; a Handle spec falls through to the captured
/// identity chain (title / processName / className).</para>
/// Pure — unit-tested without Win32 (§14.1 matchBy 우선순위).
/// </summary>
public static class WindowMatcher
{
    public static WindowInfo? Resolve(TargetSpec spec, IReadOnlyList<WindowInfo> windows)
    {
        if (spec is null || windows is null || windows.Count == 0)
            return null;

        // 1. processName + title (both present) — the most specific §7.4 tier.
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
