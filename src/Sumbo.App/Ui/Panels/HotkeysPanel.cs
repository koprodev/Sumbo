using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Runtime.Versioning;
using System.Windows.Forms;
using Sumbo.Core;

namespace Sumbo.App.Ui.Panels;

/// <summary>
/// The read-only hotkeys panel: lists the seven global hotkeys (<see cref="HotkeyService.Defaults"/>) as action
/// label + chord, marking any that failed to register (conflict). Rebinding is not supported — it needs a chord
/// parser / per-action re-register API / hotkey persistence, none of which exist. Pure view: reads the Core
/// defaults and takes the registration failures from the shell via <see cref="ReflectFailures"/>.
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed class HotkeysPanel : PanelView
{
    private static readonly (HotkeyAction Action, string Key)[] Order =
    {
        (HotkeyAction.ToggleVisible, LocKeys.Main_Hotkey_ToggleVisible),
        (HotkeyAction.PickWindow, LocKeys.Main_Hotkey_PickWindow),
        (HotkeyAction.ClickThrough, LocKeys.Main_Hotkey_ClickThrough),
        (HotkeyAction.OpacityUp, LocKeys.Main_Hotkey_OpacityUp),
        (HotkeyAction.OpacityDown, LocKeys.Main_Hotkey_OpacityDown),
        (HotkeyAction.RegionSelect, LocKeys.Main_Hotkey_RegionSelect),
        (HotkeyAction.GroupSwitch, LocKeys.Main_Hotkey_GroupSwitch),
    };

    private static readonly Color Conflict = Color.FromArgb(232, 96, 96);

    private readonly Label _sub = new();
    private readonly Row[] _rows;
    private readonly HashSet<HotkeyAction> _failed = new();

    public HotkeysPanel()
    {
        _sub.BackColor = Theme.PanelBg; _sub.ForeColor = Theme.TextMuted; _sub.Font = Theme.Small;
        _sub.AutoSize = false; _sub.TextAlign = ContentAlignment.MiddleLeft;

        // Build all rows BEFORE any Controls.Add — the first Add triggers OnLayout, which iterates _rows (would
        // NRE on a null array / null elements otherwise).
        _rows = new Row[Order.Length];
        for (int i = 0; i < Order.Length; i++)
        {
            string chord = HotkeyService.Defaults.FirstOrDefault(b => b.Action == Order[i].Action)?.Display ?? "";
            _rows[i] = new Row(chord);
        }

        Controls.Add(_sub);
        foreach (Row row in _rows)
            Controls.Add(row);
    }

    /// <summary>Shell → panel: the bindings that failed to register globally (<see cref="CloneManager.HotkeyFailures"/>)
    /// so the conflicting rows can be flagged. Startup-static, so the shell pushes it once.</summary>
    public void ReflectFailures(IReadOnlyList<HotkeyBinding> failures)
    {
        _failed.Clear();
        foreach (HotkeyBinding b in failures)
            _failed.Add(b.Action);
        for (int i = 0; i < Order.Length; i++)
            _rows[i].SetConflict(_failed.Contains(Order[i].Action));
    }

    public override void ApplyStrings(LocalizationCatalog loc)
    {
        _sub.Text = loc.Get(LocKeys.Main_Hotkeys_Subtitle);
        string conflictWord = loc.Get(LocKeys.Main_Hotkeys_Conflict);
        for (int i = 0; i < Order.Length; i++)
            _rows[i].SetText(loc.Get(Order[i].Key), conflictWord);
    }

    protected override void OnLayout(LayoutEventArgs levent)
    {
        base.OnLayout(levent);
        if (_rows is null) return; // defensive — a base-ctor layout pass can precede _rows assignment

        int pad = Theme.Pad + 2;
        int x = pad, y = 0;
        int cw = Math.Max(0, ClientSize.Width - pad * 2);

        _sub.SetBounds(x, y, cw, 34);
        y += 34 + 12;

        foreach (Row row in _rows)
        {
            row.SetBounds(x, y, cw, 40);
            y += 40 + 6;
        }
    }

    /// <summary>One hotkey line: action name (left) + chord in an inset pill (right), with a conflict flag.</summary>
    [SupportedOSPlatform("windows")]
    private sealed class Row : Panel
    {
        private readonly Label _name = new();
        private readonly Label _chord = new();
        private readonly string _chordText;
        private bool _conflict;
        private string _conflictWord = "";

        public Row(string chord)
        {
            _chordText = chord;
            BackColor = Theme.PanelBg;

            _name.BackColor = Theme.PanelBg; _name.ForeColor = Theme.TextPrimary; _name.Font = Theme.BodySemi;
            _name.AutoSize = false; _name.TextAlign = ContentAlignment.MiddleLeft; _name.AutoEllipsis = true;

            _chord.BackColor = Theme.InsetBg; _chord.ForeColor = Theme.TextSecondary; _chord.Font = Theme.Value;
            _chord.AutoSize = false; _chord.TextAlign = ContentAlignment.MiddleCenter;
            _chord.Text = chord;

            Controls.Add(_name);
            Controls.Add(_chord);
            Resize += (_, _) => Relayout();
        }

        public void SetText(string name, string conflictWord)
        {
            _name.Text = name;
            _conflictWord = conflictWord;
            RefreshChord();
        }

        public void SetConflict(bool on)
        {
            if (_conflict == on)
                return;
            _conflict = on;
            RefreshChord();
        }

        private void RefreshChord()
        {
            _chord.ForeColor = _conflict ? Conflict : Theme.TextSecondary;
            _chord.Text = _conflict && _conflictWord.Length > 0 ? $"{_chordText} · {_conflictWord}" : _chordText;
        }

        private void Relayout()
        {
            const int chordW = 150;
            int y = (Height - 28) / 2;
            _chord.SetBounds(Math.Max(0, Width - chordW), y, chordW, 28);
            _name.SetBounds(0, 0, Math.Max(0, _chord.Left - 8), Height);
        }
    }
}
