using Sumbo.Core;

namespace Sumbo.App;

/// <summary>
/// Immutable snapshot of the mirror shell's view state (M6-C 양방향 동기). Produced by
/// <c>MainWindow.ShellViewState</c> (V2-D — 단일 창 셸의 창 제어 상태; v1 <c>CloneForm.Snapshot</c> 승계),
/// consumed by the 표시 설정 panel reflect.
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

    /// <summary>Snap anchor, or null for 해제 (no anchor).</summary>
    public SnapAnchor? Anchor { get; init; }

    /// <summary>Window opacity percent (10..100).</summary>
    public int OpacityPercent { get; init; }

    /// <summary>FR-06 클릭 전달.</summary>
    public bool ClickForward { get; init; }

    /// <summary>FR-07 클릭 통과 (WS_EX_TRANSPARENT).</summary>
    public bool ClickThrough { get; init; }

    /// <summary>FR-15 위치·크기 잠금.</summary>
    public bool Locked { get; init; }

    /// <summary>FR-15 테두리 표시.</summary>
    public bool ShowBorder { get; init; }

    /// <summary>M6-C 항상 위에 표시.</summary>
    public bool AlwaysOnTop { get; init; }

    /// <summary>FR-16 휠 매핑 (패널 미노출 — 완전성).</summary>
    public WheelAction WheelAction { get; init; }

    /// <summary>Mirror target window display title (empty when the mirror has no source).</summary>
    public string TargetTitle { get; init; }
}
