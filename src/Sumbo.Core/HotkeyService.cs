using System;
using System.Collections.Generic;
using System.Runtime.Versioning;
using Sumbo.Native;

namespace Sumbo.Core;

/// <summary>Global hotkey actions.</summary>
public enum HotkeyAction
{
    ToggleVisible,
    PickWindow,
    ClickThrough,
    OpacityUp,
    OpacityDown,
    RegionSelect,
    GroupSwitch,
}

/// <summary>A hotkey action bound to a modifier+key combination, with a display string for the UI.</summary>
public sealed record HotkeyBinding(HotkeyAction Action, uint Modifiers, uint Vk, string Display);

/// <summary>
/// Registers global hotkeys for a host window and resolves incoming <c>WM_HOTKEY</c> ids back to
/// actions. Registration failures (key already held by another app) are returned so the UI can
/// advise the user to reassign.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class HotkeyService : IDisposable
{
    // Virtual-key codes (winuser.h).
    private const uint VkS = 0x53, VkW = 0x57, VkC = 0x43, VkR = 0x52, VkG = 0x47, VkUp = 0x26, VkDown = 0x28;
    private const uint CtrlAlt = User32.MOD_CONTROL | User32.MOD_ALT;

    /// <summary>Default bindings — user-overridable; the App may pass an overridden set instead.</summary>
    public static IReadOnlyList<HotkeyBinding> Defaults { get; } = new[]
    {
        new HotkeyBinding(HotkeyAction.ToggleVisible, CtrlAlt, VkS, "Ctrl+Alt+S"),
        new HotkeyBinding(HotkeyAction.PickWindow, CtrlAlt, VkW, "Ctrl+Alt+W"),
        new HotkeyBinding(HotkeyAction.ClickThrough, CtrlAlt, VkC, "Ctrl+Alt+C"),
        new HotkeyBinding(HotkeyAction.OpacityUp, CtrlAlt, VkUp, "Ctrl+Alt+Up"),
        new HotkeyBinding(HotkeyAction.OpacityDown, CtrlAlt, VkDown, "Ctrl+Alt+Down"),
        new HotkeyBinding(HotkeyAction.RegionSelect, CtrlAlt, VkR, "Ctrl+Alt+R"),
        new HotkeyBinding(HotkeyAction.GroupSwitch, CtrlAlt, VkG, "Ctrl+Alt+G"),
    };

    private readonly IntPtr _hwnd;
    private readonly Dictionary<int, HotkeyAction> _idToAction = new();
    private readonly Dictionary<HotkeyAction, int> _actionToId = new();
    private int _nextId = 1;

    public HotkeyService(IntPtr hwnd) => _hwnd = hwnd;

    /// <summary>True when the action's hotkey registered successfully (failed bindings are excluded).</summary>
    public bool IsRegistered(HotkeyAction action) => _actionToId.ContainsKey(action);

    /// <summary>
    /// Registers each binding; returns the subset that failed (key conflict). Already-registered
    /// actions are skipped. <c>MOD_NOREPEAT</c> is added so a held chord fires once.
    /// </summary>
    public IReadOnlyList<HotkeyBinding> Register(IEnumerable<HotkeyBinding> bindings)
    {
        var failures = new List<HotkeyBinding>();
        foreach (HotkeyBinding b in bindings)
        {
            if (_actionToId.ContainsKey(b.Action))
                continue;

            int id = _nextId++;
            if (User32.RegisterHotKey(_hwnd, id, b.Modifiers | User32.MOD_NOREPEAT, b.Vk))
            {
                _idToAction[id] = b.Action;
                _actionToId[b.Action] = id;
            }
            else
            {
                _nextId--; // reuse the id slot for the next attempt
                failures.Add(b);
            }
        }

        return failures;
    }

    /// <summary>Resolves a <c>WM_HOTKEY</c> id (wParam) to its action, or null if unknown.</summary>
    public HotkeyAction? Resolve(int hotkeyId)
        => _idToAction.TryGetValue(hotkeyId, out HotkeyAction action) ? action : null;

    public void Dispose()
    {
        foreach (int id in _idToAction.Keys)
            User32.UnregisterHotKey(_hwnd, id);

        _idToAction.Clear();
        _actionToId.Clear();
    }
}
