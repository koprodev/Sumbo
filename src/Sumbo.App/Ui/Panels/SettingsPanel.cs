using System;
using System.Collections.Generic;
using System.Runtime.Versioning;
using System.Windows.Forms;
using Sumbo.Core;

namespace Sumbo.App.Ui.Panels;

/// <summary>
/// The settings panel: the language + startup/tray globals live in the embedded side panel.
/// Pure view: it raises intent events the shell routes to <see cref="CloneManager"/>'s setters, and applied state
/// comes back through <see cref="ReflectSettings"/> under the <see cref="_syncing"/> guard (programmatic setters
/// re-raise change events). Language re-labels ride the shell's <c>ApplyStrings</c> fan-out — no self-subscription.
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed class SettingsPanel : PanelView
{
    private readonly Label _sub = new();
    private readonly Label _langLabel = new();
    private readonly SumboDropDown _langDrop = new();
    private readonly Label _startupLabel = new();

    // Ordered supported-language codes = the single source for item order and index↔code mapping (no ko/en literals).
    private readonly IReadOnlyList<string> _codes = LocalizationCatalog.AvailableLanguages;
    private readonly ModeRow _rowStartWithWindows = new();
    private readonly ModeRow _rowMinimizeToTray = new();

    private bool _syncing; // guards handlers while ReflectSettings seeds the controls

    /// <summary>User picked a supported language code. Shell route: <see cref="CloneManager.SetLanguage"/> + reflect.</summary>
    public event EventHandler<string>? LanguageSelected;

    /// <summary>Launch-at-startup toggle. Shell route: <see cref="CloneManager.SetStartWithWindows"/> (may be
    /// policy-blocked — the shell re-reflects the authoritative state).</summary>
    public event EventHandler<bool>? StartWithWindowsToggled;

    /// <summary>Minimize-to-tray toggle. Shell route: <see cref="CloneManager.SetMinimizeToTray"/> + reflect.</summary>
    public event EventHandler<bool>? MinimizeToTrayToggled;

    public SettingsPanel()
    {
        _sub.BackColor = Theme.PanelBg; _sub.ForeColor = Theme.TextMuted; _sub.Font = Theme.Small;
        _sub.AutoSize = false; _sub.TextAlign = ContentAlignment.MiddleLeft;

        _langLabel.BackColor = Theme.PanelBg; _langLabel.ForeColor = Theme.TextSecondary; _langLabel.Font = Theme.H2;
        _langLabel.AutoSize = false; _langLabel.TextAlign = ContentAlignment.MiddleLeft;

        _startupLabel.BackColor = Theme.PanelBg; _startupLabel.ForeColor = Theme.TextSecondary; _startupLabel.Font = Theme.H2;
        _startupLabel.AutoSize = false; _startupLabel.TextAlign = ContentAlignment.MiddleLeft;

        _langDrop.SelectedIndexChanged += (_, _) =>
        {
            if (_syncing) return;
            int i = _langDrop.SelectedIndex;
            if (i >= 0 && i < _codes.Count)
                LanguageSelected?.Invoke(this, _codes[i]);
        };
        _rowStartWithWindows.Toggle.CheckedChanged += (_, _) =>
        {
            if (_syncing) return;
            StartWithWindowsToggled?.Invoke(this, _rowStartWithWindows.Toggle.Checked);
        };
        _rowMinimizeToTray.Toggle.CheckedChanged += (_, _) =>
        {
            if (_syncing) return;
            MinimizeToTrayToggled?.Invoke(this, _rowMinimizeToTray.Toggle.Checked);
        };

        Controls.Add(_sub);
        Controls.Add(_langLabel);
        Controls.Add(_langDrop);
        Controls.Add(_startupLabel);
        Controls.Add(_rowStartWithWindows);
        Controls.Add(_rowMinimizeToTray);
    }

    /// <summary>Shell → panel: seeds the controls from the current <see cref="Settings"/> under the reentry guard
    /// (entry + after each change — StartWithWindows may revert if the Registry write was policy-blocked).</summary>
    public void ReflectSettings(Settings settings)
    {
        _syncing = true;
        try
        {
            _langDrop.SelectedIndex = Math.Max(0, IndexOfCode(LocalizationCatalog.Normalize(settings.Language)));
            _rowStartWithWindows.Toggle.Checked = settings.StartWithWindows;
            _rowMinimizeToTray.Toggle.Checked = settings.MinimizeToTray;
        }
        finally
        {
            _syncing = false;
        }
    }

    public override void ApplyStrings(LocalizationCatalog loc)
    {
        _sub.Text = loc.Get(LocKeys.Main_Settings_Subtitle);
        _langLabel.Text = loc.Get(LocKeys.Settings_Section_Language);
        var langItems = new string[_codes.Count];
        for (int i = 0; i < _codes.Count; i++)
            langItems[i] = loc.Get("settings.language." + _codes[i]); // convention key == LocKeys.Settings_Language_* (All[]-covered)
        _langDrop.Items = langItems;
        _startupLabel.Text = loc.Get(LocKeys.Settings_Section_Startup);
        _rowStartWithWindows.SetText(loc.Get(LocKeys.Tray_AutoStart), string.Empty);
        _rowMinimizeToTray.SetText(loc.Get(LocKeys.Tray_MinimizeToTray), string.Empty);
    }

    private int IndexOfCode(string code)
    {
        for (int i = 0; i < _codes.Count; i++)
            if (_codes[i] == code) return i;
        return -1;
    }

    protected override void OnLayout(LayoutEventArgs levent)
    {
        base.OnLayout(levent);

        int pad = Theme.Pad + 2;
        int x = pad, y = 0;
        int cw = Math.Max(0, ClientSize.Width - pad * 2);

        _sub.SetBounds(x, y, cw, 34);
        y += 34 + 14;

        _langLabel.SetBounds(x, y, cw, 20);
        y += 20 + 8;
        _langDrop.SetBounds(x, y, cw, 42);
        y += 42 + 18;

        _startupLabel.SetBounds(x, y, cw, 20);
        y += 20 + 8;
        _rowStartWithWindows.SetBounds(x, y, cw, 44);
        y += 44 + 6;
        _rowMinimizeToTray.SetBounds(x, y, cw, 44);
    }
}
