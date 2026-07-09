using System.Collections.Generic;

namespace Sumbo.Core;

/// <summary>
/// Global application settings persisted to <c>settings.json</c> via <see cref="SettingsService"/>
/// (<b>camelCase</b> names, <b>string</b> enums — existing stores depend on this wire format).
/// <para>
/// <see cref="Hotkeys"/> is <b>persisted only</b> — round-tripped so the schema stays stable, but not wired
/// into behaviour (hotkey re-binding is not implemented).
/// </para>
/// </summary>
public sealed record Settings
{
    public int Version { get; init; } = 1;
    public string Language { get; init; } = "ko";
    public bool StartWithWindows { get; init; }
    public bool MinimizeToTray { get; init; } = true;
    public bool CheckUpdateOnStart { get; init; } = true;
    public SettingsDefaults Defaults { get; init; } = new();

    /// <summary>
    /// Hotkey chord overrides keyed by action name. Persisted only — the live bindings remain
    /// <see cref="HotkeyService.Defaults"/> (re-binding is not implemented).
    /// </summary>
    public Dictionary<string, string> Hotkeys { get; init; } = new();
}

/// <summary>Per-clone default values seeded into new clones.</summary>
public sealed record SettingsDefaults
{
    public int Opacity { get; init; } = 100;
    public bool ShowBorder { get; init; }
    public bool ClickThrough { get; init; }
    public WheelAction WheelAction { get; init; } = WheelAction.Opacity;

    /// <summary>
    /// Default always-on-top for new clones. <b>Defaults to true</b> so configs missing the key and fresh
    /// installs get always-on-top — System.Text.Json preserves this initializer when the JSON key is
    /// absent (parameterless-ctor record).
    /// </summary>
    public bool AlwaysOnTop { get; init; } = true;
}

/// <summary>Mouse-wheel mapping over a clone. Serialized as a camelCase string.</summary>
public enum WheelAction
{
    Opacity,
    Zoom,
}
