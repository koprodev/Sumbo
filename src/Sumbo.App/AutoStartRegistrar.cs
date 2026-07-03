using System;
using System.IO;
using System.Runtime.Versioning;
using System.Security;
using Microsoft.Win32;
using Sumbo.Core;

namespace Sumbo.App;

/// <summary>
/// Registers / unregisters "launch at Windows startup" (FR-14, §7.1 <c>startWithWindows</c>) via the
/// per-user HKCU <c>...\CurrentVersion\Run</c> key — no admin rights required (contrast: a machine-wide
/// HKLM entry or a Startup-folder shortcut). The command string (quoting/normalization that survives
/// space-containing install paths) is built by the pure <see cref="AutoStartCommand"/>; this type only
/// performs the Registry side effect.
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed class AutoStartRegistrar
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "Sumbo";

    /// <summary>
    /// Writes (enabled) or removes (disabled) the Run entry. Throws on a policy-blocked Registry or when the
    /// executable path can't be resolved — the caller reverts the setting and notifies the user (PEER F2).
    /// </summary>
    public void Set(bool enabled)
    {
        // Neutral (English) technical detail: this surfaces as the {0} inside the localized auto-start-failed
        // dialog, so it must not hardcode one UI language (FR-16). It only appears on a policy-locked Registry.
        using RegistryKey key = Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true)
            ?? throw new InvalidOperationException("Cannot open the HKCU Run registry key.");

        if (enabled)
            key.SetValue(ValueName, AutoStartCommand.Build(RequireExePath()), RegistryValueKind.String);
        else
            key.DeleteValue(ValueName, throwOnMissingValue: false);
    }

    /// <summary>
    /// Startup reconciliation (self-heal): if the setting wants auto-start but the stored command is missing
    /// or stale (e.g. the app moved), rewrite it; if it wants none but an entry lingers, remove it. Registry
    /// failures are swallowed — a blocked Registry simply means self-heal is a no-op this run.
    /// </summary>
    public void Reconcile(bool enabled)
    {
        try
        {
            string? exe = enabled ? TryExePath() : null;
            if (enabled && exe is null)
                return; // can't determine the executable — leave any existing entry untouched

            // Read first (no write handle, no key creation side effect) to decide whether a change is even
            // needed; only open the key writable when we actually have to write/delete (PEER LOW Finding 3).
            bool needsWrite;
            using (RegistryKey? readKey = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false))
            {
                object? existing = readKey?.GetValue(ValueName);
                needsWrite = enabled
                    ? existing is not string stored || !AutoStartCommand.Matches(stored, exe!)
                    : existing is not null;
            }

            if (!needsWrite)
                return;

            using RegistryKey? key = Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true);
            if (key is null)
                return;

            if (enabled)
                key.SetValue(ValueName, AutoStartCommand.Build(exe!), RegistryValueKind.String);
            else
                key.DeleteValue(ValueName, throwOnMissingValue: false);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or SecurityException or IOException)
        {
            // Policy-restricted Registry — non-fatal; auto-start just won't self-heal on this launch.
        }
    }

    private static string RequireExePath()
        => TryExePath() ?? throw new InvalidOperationException("Cannot determine the executable path.");

    private static string? TryExePath()
    {
        string? path = Environment.ProcessPath;
        return !string.IsNullOrEmpty(path) && File.Exists(path) ? path : null;
    }
}
