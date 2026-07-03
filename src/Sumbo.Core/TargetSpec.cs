namespace Sumbo.Core;

/// <summary>How a saved target window is identified. Serialized camelCase (handle/title/…).</summary>
public enum MatchBy
{
    Handle,
    Title,
    ProcessName,
    ClassName,
}

/// <summary>
/// A persisted reference to a target window. Stores the primary <see cref="MatchBy"/>+<see cref="Value"/>
/// pair plus the identifiers captured at save time so restore can walk the match priority chain
/// (processName+title → title → className) even after the volatile handle is gone. Shared by profiles
/// and group members.
/// </summary>
public sealed record TargetSpec
{
    public MatchBy MatchBy { get; init; } = MatchBy.Title;
    public string Value { get; init; } = string.Empty;
    public string? CapturedTitle { get; init; }
    public string? CapturedProcessName { get; init; }
    public string? CapturedClassName { get; init; }
}
