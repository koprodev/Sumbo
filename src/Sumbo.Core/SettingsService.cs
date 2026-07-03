using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Sumbo.Core;

/// <summary>
/// Persists global <see cref="Settings"/> to <c>settings.json</c> (FR-14, §7.1). The path is injected (as
/// with <see cref="ProfileService"/> / <see cref="RegionStore"/>) so the App supplies
/// <c>%AppData%\Sumbo\settings.json</c> while tests use a temp path. Wire format matches §7.1 — camelCase
/// names + string enums — via the same <see cref="JsonSerializerOptions"/> shape as <see cref="ProfileService"/>.
/// Writes are atomic (<see cref="AtomicFile"/>).
/// </summary>
public sealed class SettingsService
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

    public SettingsService(string filePath)
    {
        _filePath = filePath ?? throw new ArgumentNullException(nameof(filePath));
    }

    /// <summary>Loads settings; returns defaults when the file is absent or unreadable/corrupt.</summary>
    public Settings Load()
    {
        if (!File.Exists(_filePath))
            return new Settings();

        try
        {
            string json = File.ReadAllText(_filePath);
            return JsonSerializer.Deserialize<Settings>(json, Options) ?? new Settings();
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            // A corrupt or locked settings file must not crash the app; defaults let the user re-save.
            return new Settings();
        }
    }

    /// <summary>Atomically overwrites the settings file.</summary>
    public void Save(Settings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        AtomicFile.WriteAllText(_filePath, JsonSerializer.Serialize(settings, Options));
    }

    /// <summary>Serializes settings to their JSON wire form (exposed for tests / previews).</summary>
    public static string Serialize(Settings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return JsonSerializer.Serialize(settings, Options);
    }
}
