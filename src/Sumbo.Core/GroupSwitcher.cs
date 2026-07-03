using System.Collections.Generic;

namespace Sumbo.Core;

/// <summary>
/// Rotation state machine for group switching: one clone window cycles its <b>source</b> through an
/// ordered group of target windows every N seconds. Holds membership and advance logic only; the actual
/// timer + re-target lives in the UI (App). Pure — unit-tested without Win32.
/// <para>Group membership is in-memory only (not persisted); <c>TargetSpec</c> itself is
/// serialization-ready.</para>
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

    /// <summary>Starts rotation if there are at least two members (a single member would just hop onto
    /// itself); returns whether it is now running.</summary>
    public bool Start()
    {
        IsRunning = _members.Count > 1;
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
