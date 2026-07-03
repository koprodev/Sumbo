using System.Collections.Generic;

namespace Sumbo.Core;

/// <summary>
/// Global application settings persisted to <c>settings.json</c> (FR-14, §7.1). The wire format matches the
/// §7.1 example exactly — <b>camelCase</b> names and <b>string</b> enums — via <see cref="SettingsService"/>.
/// <para>
/// cycle A (FR-14) consumes <see cref="StartWithWindows"/> and <see cref="MinimizeToTray"/> only. The
/// remaining fields (<see cref="Language"/>, <see cref="CheckUpdateOnStart"/>, <see cref="Defaults"/>,
/// <see cref="Hotkeys"/>) are <b>forward-compatible persistence only</b> — modelled and round-tripped now so
/// the schema is stable, but not yet wired into behaviour (FR-16 language, FR-17 update, FR-09 hotkey
/// re-binding land in later cycles).
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
    /// Hotkey chord overrides keyed by action name (§7.1 <c>hotkeys</c>). Persisted only — the live bindings
    /// remain <see cref="HotkeyService.Defaults"/> until FR-09 re-binding is implemented.
    /// </summary>
    public Dictionary<string, string> Hotkeys { get; init; } = new();
}

/// <summary>Per-clone default values for new clones (§7.1 <c>defaults</c>). Persisted only in cycle A.</summary>
public sealed record SettingsDefaults
{
    public int Opacity { get; init; } = 100;
    public bool ShowBorder { get; init; }
    public bool ClickThrough { get; init; }
    public WheelAction WheelAction { get; init; } = WheelAction.Opacity;

    /// <summary>
    /// Default 항상 위에 표시 for new clones. <b>Defaults to true</b> so legacy configs (missing the key)
    /// and fresh installs keep the historical always-on-top behaviour — STJ preserves this initializer when
    /// the JSON key is absent (parameterless-ctor record). M6-C makes it a per-mirror toggle.
    /// </summary>
    public bool AlwaysOnTop { get; init; } = true;
}

/// <summary>Mouse-wheel mapping over a clone (§6.2 / §7.1 <c>wheelAction</c>). Serialized as a camelCase string.</summary>
public enum WheelAction
{
    Opacity,
    Zoom,
}
