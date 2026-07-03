using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace Sumbo.Core;

/// <summary>A saved region with a user-facing name (FR-02 저장/불러오기).</summary>
public sealed record NamedRegion(string Name, Region Region);

/// <summary>
/// Persists named regions to a JSON file (FR-02). The file path is injected (보완 5) so the
/// App supplies <c>%AppData%\Sumbo\regions.json</c> while tests use a temp path — Core stays
/// UI-/environment-independent and tests never touch the real AppData (요건정의서 §14.1).
/// </summary>
public sealed class RegionStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
    };

    private readonly string _filePath;

    public RegionStore(string filePath)
    {
        _filePath = filePath ?? throw new ArgumentNullException(nameof(filePath));
    }

    /// <summary>Loads saved regions; returns empty when the file is absent or unreadable.</summary>
    public IReadOnlyList<NamedRegion> Load()
    {
        if (!File.Exists(_filePath))
            return Array.Empty<NamedRegion>();

        try
        {
            string json = File.ReadAllText(_filePath);
            var regions = JsonSerializer.Deserialize<List<NamedRegion>>(json, Options);
            return regions ?? new List<NamedRegion>();
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            // A corrupt or locked store must not crash the app; the user can re-save.
            return Array.Empty<NamedRegion>();
        }
    }

    /// <summary>
    /// Overwrites the store with <paramref name="regions"/>, creating the directory if needed.
    /// The write is <b>atomic</b> — serialized to a sibling temp file then swapped in via
    /// <see cref="File.Replace(string,string,string?)"/> / <see cref="File.Move(string,string)"/> —
    /// so a crash mid-write never leaves a torn JSON file. With every consumer sharing this one
    /// store on the single UI thread, the canonical model is serialized in full each save, so the
    /// last complete write wins rather than a partial one (atomic persistence 보완).
    /// </summary>
    public void Save(IEnumerable<NamedRegion> regions)
    {
        ArgumentNullException.ThrowIfNull(regions);
        AtomicFile.WriteAllText(_filePath, JsonSerializer.Serialize(new List<NamedRegion>(regions), Options));
    }
}
