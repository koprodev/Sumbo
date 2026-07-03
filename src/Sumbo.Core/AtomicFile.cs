using System;
using System.IO;

namespace Sumbo.Core;

/// <summary>
/// Atomic text-file writer shared by the JSON stores (<see cref="RegionStore"/>, <see cref="ProfileService"/>).
/// Writes the full content to a sibling temp file, then swaps it in with an atomic move (cleaning up the
/// temp on failure) — a crash mid-write never leaves a torn file, and saves on the single UI thread
/// resolve to last-complete-write-wins.
/// </summary>
internal static class AtomicFile
{
    public static void WriteAllText(string filePath, string contents)
    {
        string? directory = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        string tempPath = filePath + ".tmp";
        File.WriteAllText(tempPath, contents);
        try
        {
            // Atomic on the same volume (MOVEFILE_REPLACE_EXISTING) — handles both create and replace in
            // one call and avoids File.Replace's fragility when the destination is briefly locked.
            File.Move(tempPath, filePath, overwrite: true);
        }
        catch
        {
            try { File.Delete(tempPath); } catch { /* best-effort cleanup */ }
            throw;
        }
    }
}
