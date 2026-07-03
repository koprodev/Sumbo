using Sumbo.Core;

namespace Sumbo.App;

/// <summary>
/// Immutable snapshot of the mirror shell's view state. Produced by <c>MainWindow.ShellViewState</c>,
/// consumed by the display-settings panel reflect.
/// <para>
/// Lives in the App layer (not Core): it is a UI-reflection view over shell private fields, not a
/// persisted domain type. It only references Core value types (<see cref="ClientSizeMode"/>,
/// <see cref="SnapAnchor"/>, <see cref="WheelAction"/>) — the App→Core direction is the allowed one.
/// </para>
/// </summary>
internal readonly record struct MirrorViewState
{
    /// <summary>Preset size mode, or null for a free/custom size (wheel-zoom or user drag).</summary>
    public ClientSizeMode? SizeMode { get; init; }

    /// <summary>Fullscreen (maximized) toggle — separate from <see cref="SizeMode"/> (not a preset member).</summary>
    public bool IsFullscreen { get; init; }

    /// <summary>Snap anchor, or null when unanchored.</summary>
    public SnapAnchor? Anchor { get; init; }

    /// <summary>Window opacity percent (10..100).</summary>
    public int OpacityPercent { get; init; }

    /// <summary>Click forwarding to the mirrored source.</summary>
    public bool ClickForward { get; init; }

    /// <summary>Click-through (WS_EX_TRANSPARENT).</summary>
    public bool ClickThrough { get; init; }

    /// <summary>Position/size lock.</summary>
    public bool Locked { get; init; }

    /// <summary>Border visibility.</summary>
    public bool ShowBorder { get; init; }

    /// <summary>Always-on-top.</summary>
    public bool AlwaysOnTop { get; init; }

    /// <summary>Wheel mapping (not exposed on the panel; carried for completeness).</summary>
    public WheelAction WheelAction { get; init; }

    /// <summary>Mirror target window display title (empty when the mirror has no source).</summary>
    public string TargetTitle { get; init; }
}
