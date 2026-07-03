using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace Sumbo.Core;

/// <summary>A saved region with a user-facing name.</summary>
public sealed record NamedRegion(string Name, Region Region);

/// <summary>
/// Persists named regions to a JSON file. The file path is injected so the App supplies
/// <c>%AppData%\Sumbo\regions.json</c> while tests use a temp path — Core stays
/// UI-/environment-independent and tests never touch the real AppData.
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
    /// Atomically overwrites the store with <paramref name="regions"/> (via <see cref="AtomicFile"/>),
    /// creating the directory if needed. Every consumer shares this one store on the single UI thread
    /// and each save serializes the full model, so the last complete write wins — never a partial one.
    /// </summary>
    public void Save(IEnumerable<NamedRegion> regions)
    {
        ArgumentNullException.ThrowIfNull(regions);
        AtomicFile.WriteAllText(_filePath, JsonSerializer.Serialize(new List<NamedRegion>(regions), Options));
    }
}
