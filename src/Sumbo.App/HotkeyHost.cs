using System;
using System.Collections.Generic;
using System.Runtime.Versioning;
using System.Windows.Forms;
using Sumbo.Core;
using Sumbo.Native;

namespace Sumbo.App;

/// <summary>
/// A single message-only window that owns all global hotkeys (FR-09, §6.4) for the whole process and
/// raises <see cref="HotkeyPressed"/> when one fires.
/// <para>
/// Centralization is mandatory: a global chord can only be registered on one HWND at a time — a second
/// <see cref="User32.RegisterHotKey"/> for the same chord fails with
/// <c>ERROR_HOTKEY_ALREADY_REGISTERED (1409)</c> (PEER 실측). So UI windows never register hotkeys
/// themselves; this host owns them and <see cref="CloneManager"/> raises <c>HotkeyRouted</c> for the main
/// window (a message-only HWND also survives click-through/UI-hidden states — FR-07 탈출 생존).
/// </para>
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class HotkeyHost : NativeWindow, IDisposable
{
    private const int HwndMessage = -3; // HWND_MESSAGE — a message-only window (no UI, receives posted messages)

    private readonly HotkeyService _hotkeys;

    /// <summary>Bindings that failed to register (chord held by another app) — surfaced to the user once.</summary>
    public IReadOnlyList<HotkeyBinding> Failures { get; }

    /// <summary>Raised on the UI thread when a registered global hotkey fires.</summary>
    public event EventHandler<HotkeyAction>? HotkeyPressed;

    public HotkeyHost()
    {
        CreateHandle(new CreateParams
        {
            Caption = "SumboHotkeyHost",
            Parent = new IntPtr(HwndMessage),
        });

        _hotkeys = new HotkeyService(Handle);
        Failures = _hotkeys.Register(HotkeyService.Defaults);
    }

    /// <summary>True when the given action's chord is currently registered (e.g. the click-through escape).</summary>
    public bool IsRegistered(HotkeyAction action) => _hotkeys.IsRegistered(action);

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == User32.WM_HOTKEY)
        {
            HotkeyAction? action = _hotkeys.Resolve(m.WParam.ToInt32());
            if (action is not null)
            {
                HotkeyPressed?.Invoke(this, action.Value);
                return;
            }
        }

        base.WndProc(ref m);
    }

    public void Dispose()
    {
        _hotkeys.Dispose();
        if (Handle != IntPtr.Zero)
            DestroyHandle();
    }
}
