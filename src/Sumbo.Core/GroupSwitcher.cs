using System.Collections.Generic;

namespace Sumbo.Core;

/// <summary>
/// Rotation state machine for group switching (FR-08, §6.4 Ctrl+Alt+G). Holds an ordered set of target
/// specs and advances through them; the actual timer + re-target lives in the UI (App). Interpretation α:
/// one clone window cycles its <b>source</b> through a group of target windows every N seconds
/// (OnTopReplica 계승). Pure — unit-tested without Win32 (§14.1).
/// <para>Group membership is in-memory for cycle② (T-08); <c>TargetSpec</c> is serialization-ready so a
/// later schema-extension cycle can persist it (Q2).</para>
/// </summary>
public sealed class GroupSwitcher
{
    private readonly List<TargetSpec> _members = new();
    private int _index = -1;

    /// <summary>Rotation interval in seconds (clamped to a sane minimum). Default 5s.</summary>
    public int IntervalSeconds { get; private set; } = 5;

    /// <summary>Whether rotation is currently active (mirrors the UI timer's enabled state).</summary>
    public bool IsRunning { get; private set; }

    public IReadOnlyList<TargetSpec> Members => _members;
    public int Count => _members.Count;

    /// <summary>The current member without advancing, or null when empty / not yet started.</summary>
    public TargetSpec? Current => _index >= 0 && _index < _members.Count ? _members[_index] : null;

    public void Add(TargetSpec spec)
    {
        if (spec is not null)
            _members.Add(spec);
    }

    public void Clear()
    {
        _members.Clear();
        _index = -1;
        IsRunning = false;
    }

    public void SetInterval(int seconds) => IntervalSeconds = seconds < 1 ? 1 : seconds;

    /// <summary>Starts rotation if there is at least one member; returns whether it is now running.</summary>
    public bool Start()
    {
        IsRunning = _members.Count > 0;
        return IsRunning;
    }

    public void Stop() => IsRunning = false;

    /// <summary>Advances to and returns the next member (wrapping), or null when the group is empty.</summary>
    public TargetSpec? Next()
    {
        if (_members.Count == 0)
        {
            _index = -1;
            return null;
        }

        _index = (_index + 1) % _members.Count;
        return _members[_index];
    }
}
