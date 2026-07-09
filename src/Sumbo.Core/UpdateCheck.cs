using System;

namespace Sumbo.Core;

/// <summary>
/// Pure version logic for the startup update check: release-tag parsing ("v1.2.0" / "1.2") and a
/// strictly-newer comparison. The network probe stays in the App layer; this part is unit-testable.
/// </summary>
public static class UpdateCheck
{
    /// <summary>Parses a release tag into a 3-component version. Accepts an optional leading "v" and strips
    /// pre-release/build suffixes ("1.2.0-rc1", "1.2.0+abc"). Missing components normalize to 0.</summary>
    public static bool TryParseTag(string? tag, out Version version)
    {
        version = new Version(0, 0, 0);
        if (string.IsNullOrWhiteSpace(tag))
            return false;

        string t = tag.Trim();
        if (t.StartsWith("v", StringComparison.OrdinalIgnoreCase))
            t = t[1..];
        int cut = t.IndexOfAny(new[] { '-', '+', ' ' });
        if (cut >= 0)
            t = t[..cut];
        if (!t.Contains('.'))
            t += ".0"; // Version.TryParse rejects a bare major ("2")

        if (!Version.TryParse(t, out Version? parsed))
            return false;
        version = Normalize(parsed);
        return true;
    }

    /// <summary>True when <paramref name="latestTag"/> denotes a version strictly newer than
    /// <paramref name="current"/> (compared on major.minor.build; revision ignored).</summary>
    public static bool IsNewer(string? latestTag, Version? current)
        => current is not null && TryParseTag(latestTag, out Version latest) && latest > Normalize(current);

    private static Version Normalize(Version v)
        => new(v.Major, Math.Max(v.Minor, 0), Math.Max(v.Build, 0));
}
