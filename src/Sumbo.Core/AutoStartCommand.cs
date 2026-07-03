using System;
using System.IO;

namespace Sumbo.Core;

/// <summary>
/// Builds and compares the Windows "Run" key command string for launch-at-startup. Kept as a pure helper —
/// separate from the Registry side effect in the App — so the quoting/normalization that guards against
/// space-containing install paths (e.g. <c>C:\Program Files\...</c>) is unit-testable.
/// </summary>
public static class AutoStartCommand
{
    /// <summary>
    /// Returns the Run value for an executable: the absolute path wrapped in double quotes. Quoting is
    /// mandatory — an unquoted value containing a space is parsed by the shell only up to the first space.
    /// </summary>
    public static string Build(string exePath)
    {
        ArgumentException.ThrowIfNullOrEmpty(exePath);
        return "\"" + Path.GetFullPath(exePath) + "\"";
    }

    /// <summary>
    /// True when a stored Run value already points at <paramref name="exePath"/>, tolerating surrounding
    /// quotes and path-normalization differences — used by startup self-heal to skip a redundant rewrite.
    /// A malformed stored value yields <c>false</c> (self-heal then rewrites it).
    /// </summary>
    public static bool Matches(string? storedValue, string exePath)
    {
        if (string.IsNullOrEmpty(storedValue) || string.IsNullOrEmpty(exePath))
            return false;

        try
        {
            string stored = Unquote(storedValue.Trim());
            return string.Equals(
                Path.GetFullPath(stored),
                Path.GetFullPath(exePath),
                StringComparison.OrdinalIgnoreCase);
        }
        catch (ArgumentException)
        {
            // Stored value is not a valid path (illegal chars) — treat as a mismatch so self-heal rewrites.
            return false;
        }
    }

    private static string Unquote(string value)
        => value.Length >= 2 && value[0] == '"' && value[^1] == '"' ? value[1..^1] : value;
}
