namespace Sumbo.Core;

/// <summary>How a saved target window is identified (요건정의서 §7.4). Serialized camelCase (handle/title/…).</summary>
public enum MatchBy
{
    Handle,
    Title,
    ProcessName,
    ClassName,
}

/// <summary>
/// A persisted reference to a target window (§7.2 <c>target</c>). Stores the primary
/// <see cref="MatchBy"/>+<see cref="Value"/> (the §7.2 minimal form) plus the identifiers captured at
/// save time so restore can walk the §7.4 priority chain (processName+title → title → className) even
/// after the volatile handle is gone. Shared by profiles (FR-13) and group members (FR-08).
/// </summary>
public sealed record TargetSpec
{
    public MatchBy MatchBy { get; init; } = MatchBy.Title;
    public string Value { get; init; } = string.Empty;
    public string? CapturedTitle { get; init; }
    public string? CapturedProcessName { get; init; }
    public string? CapturedClassName { get; init; }
}
