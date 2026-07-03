using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Sumbo.Core;

/// <summary>
/// Persists clone profiles to <c>profiles.json</c> (FR-13, §7.2). The file path is injected (as with
/// <see cref="RegionStore"/>) so the App supplies <c>%AppData%\Sumbo\profiles.json</c> while tests use a
/// temp path. Wire format matches the §7.2 example exactly — <b>camelCase</b> property names and
/// <b>string</b> enums (<c>"matchBy":"title"</c>, <c>"anchor":"topRight"</c>) — via
/// <see cref="JsonNamingPolicy.CamelCase"/> + <see cref="JsonStringEnumConverter"/> (PEER 보완 HIGH).
/// Writes are atomic (<see cref="AtomicFile"/>).
/// </summary>
public sealed class ProfileService
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    private readonly string _filePath;

    public ProfileService(string filePath)
    {
        _filePath = filePath ?? throw new ArgumentNullException(nameof(filePath));
    }

    /// <summary>Loads all profiles; returns an empty file when absent or unreadable/corrupt.</summary>
    public ProfilesFile Load()
    {
        if (!File.Exists(_filePath))
            return new ProfilesFile();

        try
        {
            string json = File.ReadAllText(_filePath);
            return JsonSerializer.Deserialize<ProfilesFile>(json, Options) ?? new ProfilesFile();
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            // A corrupt or locked store must not crash the app; the user can re-save.
            return new ProfilesFile();
        }
    }

    /// <summary>Atomically overwrites the store.</summary>
    public void Save(ProfilesFile file)
    {
        ArgumentNullException.ThrowIfNull(file);
        AtomicFile.WriteAllText(_filePath, JsonSerializer.Serialize(file, Options));
    }

    /// <summary>Serializes a profiles file to its JSON wire form (exposed for tests / previews).</summary>
    public static string Serialize(ProfilesFile file)
    {
        ArgumentNullException.ThrowIfNull(file);
        return JsonSerializer.Serialize(file, Options);
    }

    /// <summary>Inserts or replaces a profile by <see cref="Profile.Id"/> and persists (load→merge→write).</summary>
    public void Upsert(Profile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);

        ProfilesFile current = Load();
        var merged = new List<Profile>(current.Profiles);
        int index = merged.FindIndex(p => p.Id == profile.Id);
        if (index >= 0)
            merged[index] = profile; // replace in place — keep menu/list order stable across edits
        else
            merged.Add(profile);
        Save(current with { Profiles = merged });
    }
}
