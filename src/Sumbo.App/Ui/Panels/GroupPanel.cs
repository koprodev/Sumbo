using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.Versioning;
using System.Windows.Forms;
using Sumbo.Core;

namespace Sumbo.App.Ui.Panels;

/// <summary>
/// The group-rotation panel: collects windows into a rotation group and starts/stops the
/// timer that hops the mirror source through them (Ctrl+Alt+G). Pure view — each action is an intent the shell
/// routes to its <c>GroupSwitcher</c> + timer + <c>MirrorSurface.RetargetPreserving</c>; state comes back through
/// <see cref="ReflectGroup"/>. Membership supports Add / Clear only (<c>GroupSwitcher</c> has no per-member
/// remove), so member rows are read-only — a delete button would misrepresent that.
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed class GroupPanel : PanelView
{
    private readonly Label _sub = new();
    private readonly FlatButton _addBtn = new();
    private readonly Label _intervalLabel = new();
    private readonly NumericUpDown _interval = new();
    private readonly FlatButton _toggleRunBtn = new();
    private readonly FlatButton _clearBtn = new();
    private readonly Label _membersLabel = new();
    private readonly Panel _list = new() { AutoScroll = true };
    private readonly List<GroupMemberRow> _rows = new();
    private readonly Label _emptyNote = new();

    private bool _syncing; // guards the interval handler while ReflectGroup seeds it
    private bool _running;
    private bool _hasMirror;
    private LocalizationCatalog? _loc; // set by ApplyStrings (runs before any reflect) — start/stop caption source

    // Blink pulse while rotation runs — the stop button alternates Primary/Dark so the active cycle is unmissable.
    private readonly System.Windows.Forms.Timer _blinkTimer = new() { Interval = 600 };

    /// <summary>Add-current-target button — needs a live mirror; the shell adds its current source spec.</summary>
    public event EventHandler? AddCurrentRequested;

    /// <summary>Clear-group button — clears members and stops rotation.</summary>
    public event EventHandler? ClearRequested;

    /// <summary>Start/stop rotation toggle.</summary>
    public event EventHandler? ToggleRunRequested;

    /// <summary>Rotation interval changed (seconds).</summary>
    public event EventHandler<int>? IntervalChanged;

    public GroupPanel()
    {
        _sub.BackColor = Theme.PanelBg; _sub.ForeColor = Theme.TextMuted; _sub.Font = Theme.Small;
        _sub.AutoSize = false; _sub.TextAlign = ContentAlignment.TopLeft;

        _addBtn.Kind = ButtonKind.Primary;
        _addBtn.Glyph = Glyph.Add;
        _addBtn.GlyphSize = 12f;
        _addBtn.CornerBack = Theme.PanelBg;
        _addBtn.Enabled = false; // needs a live mirror (ReflectGroup enables)
        _addBtn.Click += (_, _) => AddCurrentRequested?.Invoke(this, EventArgs.Empty);

        _intervalLabel.BackColor = Theme.PanelBg; _intervalLabel.ForeColor = Theme.TextSecondary; _intervalLabel.Font = Theme.H2;
        _intervalLabel.AutoSize = false; _intervalLabel.TextAlign = ContentAlignment.MiddleLeft;

        _interval.Minimum = 1;
        _interval.Maximum = 3600;
        _interval.Value = 5;
        _interval.BorderStyle = BorderStyle.FixedSingle;
        _interval.BackColor = Theme.InsetBg;
        _interval.ForeColor = Theme.TextPrimary;
        _interval.Font = Theme.Body;
        _interval.ValueChanged += (_, _) =>
        {
            if (_syncing) return;
            IntervalChanged?.Invoke(this, (int)_interval.Value);
        };

        _toggleRunBtn.Kind = ButtonKind.Dark;
        _toggleRunBtn.Glyph = Glyph.Switch;
        _toggleRunBtn.GlyphSize = 12f;
        _toggleRunBtn.CornerBack = Theme.PanelBg;
        _toggleRunBtn.Enabled = false;
        _toggleRunBtn.Click += (_, _) => ToggleRunRequested?.Invoke(this, EventArgs.Empty);
        _blinkTimer.Tick += (_, _) =>
            _toggleRunBtn.Kind = _toggleRunBtn.Kind == ButtonKind.Primary ? ButtonKind.Dark : ButtonKind.Primary;

        _clearBtn.Kind = ButtonKind.Dark;
        _clearBtn.CornerBack = Theme.PanelBg;
        _clearBtn.Enabled = false;
        _clearBtn.Click += (_, _) => ClearRequested?.Invoke(this, EventArgs.Empty);

        _membersLabel.BackColor = Theme.PanelBg; _membersLabel.ForeColor = Theme.TextSecondary; _membersLabel.Font = Theme.H2;
        _membersLabel.AutoSize = false; _membersLabel.TextAlign = ContentAlignment.MiddleLeft;

        _list.BackColor = Theme.PanelBg;
        _list.ClientSizeChanged += (_, _) => LayoutRows();

        _emptyNote.BackColor = Theme.PanelBg; _emptyNote.ForeColor = Theme.TextMuted; _emptyNote.Font = Theme.Small;
        _emptyNote.AutoSize = false; _emptyNote.TextAlign = ContentAlignment.MiddleLeft;
        _list.Controls.Add(_emptyNote);

        Controls.Add(_sub);
        Controls.Add(_addBtn);
        Controls.Add(_intervalLabel);
        Controls.Add(_interval);
        Controls.Add(_toggleRunBtn);
        Controls.Add(_clearBtn);
        Controls.Add(_membersLabel);
        Controls.Add(_list);
    }

    /// <summary>Shell → panel (SyncPanels): lightweight enable refresh only — add needs a live mirror, start needs a
    /// live mirror + members. No member-row rebuild (avoids control churn on every mirror transition).</summary>
    public void ReflectMirror(bool hasMirror)
    {
        _hasMirror = hasMirror;
        RefreshEnable();
    }

    /// <summary>Shell → panel: full reflect on a group state change (add/clear/toggle/interval/entry). Rebuilds the
    /// member rows, seeds the interval, and follows <paramref name="running"/> for the start/stop caption.</summary>
    public void ReflectGroup(bool hasMirror, IReadOnlyList<string> members, bool running, int intervalSeconds)
    {
        _hasMirror = hasMirror;
        _running = running;
        _toggleRunBtn.Text = _loc is null ? "" : _loc.Get(running ? LocKeys.Menu_Group_Stop : LocKeys.Menu_Group_Start);
        _toggleRunBtn.Glyph = running ? Glyph.Stop : Glyph.Switch;
        if (running != _blinkTimer.Enabled)
        {
            _blinkTimer.Enabled = running;
            _toggleRunBtn.Kind = running ? ButtonKind.Primary : ButtonKind.Dark; // deterministic pulse phase / idle restore
        }

        _syncing = true;
        try { _interval.Value = Math.Clamp(intervalSeconds, (int)_interval.Minimum, (int)_interval.Maximum); }
        finally { _syncing = false; }

        SetMembers(members);
        RefreshEnable();
    }

    /// <summary>Add = live mirror; start/stop = members &amp;&amp; (running OR live mirror — starting needs a source to
    /// retarget, stopping is always allowed); clear = members.</summary>
    private void RefreshEnable()
    {
        _addBtn.Enabled = _hasMirror;
        _toggleRunBtn.Enabled = _rows.Count > 0 && (_running || _hasMirror);
        _clearBtn.Enabled = _rows.Count > 0;
    }

    private void SetMembers(IReadOnlyList<string> members)
    {
        foreach (GroupMemberRow row in _rows)
        {
            _list.Controls.Remove(row);
            row.Dispose();
        }
        _rows.Clear();

        for (int i = 0; i < members.Count; i++)
        {
            var row = new GroupMemberRow(i + 1, members[i]);
            _rows.Add(row);
            _list.Controls.Add(row);
        }

        _emptyNote.Visible = _rows.Count == 0;
        _list.AutoScrollPosition = new Point(0, 0);
        LayoutRows();
    }

    public override void ApplyStrings(LocalizationCatalog loc)
    {
        _loc = loc;
        _sub.Text = loc.Get(LocKeys.Main_Group_Subtitle);
        _addBtn.Text = loc.Get(LocKeys.Menu_Group_Add);
        _intervalLabel.Text = loc.Get(LocKeys.Prompt_GroupInterval_Title);
        _toggleRunBtn.Text = loc.Get(_running ? LocKeys.Menu_Group_Stop : LocKeys.Menu_Group_Start);
        _clearBtn.Text = loc.Get(LocKeys.Menu_Group_Clear);
        _membersLabel.Text = loc.Get(LocKeys.Main_Group_Members);
        _emptyNote.Text = loc.Get(LocKeys.Main_Group_Empty);
    }

    protected override void OnLayout(LayoutEventArgs levent)
    {
        base.OnLayout(levent);

        int pad = Theme.Pad + 2;
        int x = pad, y = 0;
        int cw = Math.Max(0, ClientSize.Width - pad * 2);

        _sub.SetBounds(x, y, cw, 34);
        y += 34 + 12;

        _addBtn.SetBounds(x, y, cw, 40);
        y += 40 + 12;

        _intervalLabel.SetBounds(x, y, cw - 84, 26);
        _interval.SetBounds(x + cw - 76, y + 1, 76, 24);
        y += 26 + 12;

        int half = (cw - 8) / 2;
        _toggleRunBtn.SetBounds(x, y, half, 40);
        _clearBtn.SetBounds(x + half + 8, y, cw - half - 8, 40);
        y += 40 + 16;

        _membersLabel.SetBounds(x, y, cw, 20);
        y += 20 + 8;
        _list.SetBounds(x, y, cw, Math.Max(0, ClientSize.Height - y - pad));
        LayoutRows();
    }

    private void LayoutRows()
    {
        int cw = _list.ClientSize.Width;
        int y = _list.AutoScrollPosition.Y;
        foreach (GroupMemberRow row in _rows)
        {
            row.SetBounds(0, y, cw, 36);
            y += 36 + 6;
        }
        _emptyNote.SetBounds(0, 0, cw, 20);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            _blinkTimer.Dispose(); // a component, not a child control — the form dispose chain doesn't reach it
        base.Dispose(disposing);
    }
}

/// <summary>A read-only group member row: ordinal + ellipsized title. No per-member action — membership is
/// Add / Clear only, so unlike <see cref="SavedItemRow"/> it carries no apply/delete button.</summary>
[SupportedOSPlatform("windows")]
internal sealed class GroupMemberRow : Panel
{
    private readonly Label _index = new();
    private readonly Label _name = new();

    public GroupMemberRow(int ordinal, string title)
    {
        BackColor = Theme.PanelBg;

        _index.BackColor = Theme.InsetBg; _index.ForeColor = Theme.TextSecondary; _index.Font = Theme.Value;
        _index.AutoSize = false; _index.TextAlign = ContentAlignment.MiddleCenter;
        _index.Text = ordinal.ToString();

        _name.BackColor = Theme.PanelBg; _name.ForeColor = Theme.TextPrimary; _name.Font = Theme.BodySemi;
        _name.AutoSize = false; _name.AutoEllipsis = true; _name.TextAlign = ContentAlignment.MiddleLeft;
        _name.Text = string.IsNullOrEmpty(title) ? "—" : title;

        Controls.Add(_index);
        Controls.Add(_name);
        Resize += (_, _) => Relayout();
    }

    private void Relayout()
    {
        const int badge = 26;
        int y = (Height - badge) / 2;
        _index.SetBounds(0, y, badge, badge);
        _name.SetBounds(badge + 10, 0, Math.Max(0, Width - badge - 10), Height);
    }
}
