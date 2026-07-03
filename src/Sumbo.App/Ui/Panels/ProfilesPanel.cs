using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.Versioning;
using System.Windows.Forms;
using Sumbo.Core;

namespace Sumbo.App.Ui.Panels;

/// <summary>
/// The profiles panel: saves the current mirror setup under a name and manages the saved list
/// (apply / delete). Pure view — each intent is an event the SHELL routes to <c>ProfileService</c>/<c>MirrorSurface</c>;
/// the saved list is pushed via <see cref="SetProfiles"/>. Save needs a live mirror; APPLY does not:
/// applying re-resolves the target and starts a fresh mirror.
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed class ProfilesPanel : PanelView
{
    private readonly LocalizationCatalog _loc;

    private readonly Label _sub = new();
    private readonly FlatButton _saveBtn = new();
    private readonly Label _savedLabel = new();
    private readonly Panel _list = new() { AutoScroll = true };
    private readonly List<SavedItemRow> _rows = new();
    private readonly Label _emptyNote = new();

    /// <summary>Save-current-setup button — the shell prompts for a name, captures and upserts.</summary>
    public event EventHandler? SaveRequested;

    /// <summary>Saved-row apply — the shell resolves the target and restores the profile onto the mirror.</summary>
    public event EventHandler<Profile>? ApplyRequested;

    /// <summary>Saved-row delete — destructive; the shell confirms before touching the store.</summary>
    public event EventHandler<Profile>? DeleteRequested;

    public ProfilesPanel(LocalizationCatalog loc)
    {
        _loc = loc ?? throw new ArgumentNullException(nameof(loc));

        _sub.BackColor = Theme.PanelBg; _sub.ForeColor = Theme.TextMuted; _sub.Font = Theme.Small;
        _sub.AutoSize = false; _sub.TextAlign = ContentAlignment.TopLeft;

        _saveBtn.Kind = ButtonKind.Primary;
        _saveBtn.Glyph = Glyph.Add;
        _saveBtn.GlyphSize = 12f;
        _saveBtn.CornerBack = Theme.PanelBg;
        _saveBtn.Enabled = false; // needs a live mirror (ReflectMirror enables)
        _saveBtn.Click += (_, _) => SaveRequested?.Invoke(this, EventArgs.Empty);

        _savedLabel.BackColor = Theme.PanelBg; _savedLabel.ForeColor = Theme.TextSecondary; _savedLabel.Font = Theme.H2;
        _savedLabel.AutoSize = false; _savedLabel.TextAlign = ContentAlignment.MiddleLeft;

        _list.BackColor = Theme.PanelBg;
        _list.ClientSizeChanged += (_, _) => LayoutRows(); // scrollbar appearing shrinks the client width

        _emptyNote.BackColor = Theme.PanelBg; _emptyNote.ForeColor = Theme.TextMuted; _emptyNote.Font = Theme.Small;
        _emptyNote.AutoSize = false; _emptyNote.TextAlign = ContentAlignment.MiddleLeft;
        _list.Controls.Add(_emptyNote);

        Controls.Add(_sub);
        Controls.Add(_saveBtn);
        Controls.Add(_savedLabel);
        Controls.Add(_list);
    }

    /// <summary>Shell → panel: only Save tracks the live mirror — applying a profile re-resolves its own target.</summary>
    public void ReflectMirror(bool hasMirror) => _saveBtn.Enabled = hasMirror;

    /// <summary>Shell → panel: rebuilds the saved-profile rows from the store snapshot.</summary>
    public void SetProfiles(IReadOnlyList<Profile> profiles)
    {
        ClearRows();
        foreach (Profile profile in profiles)
        {
            Profile entry = profile;
            var row = new SavedItemRow(entry.Name) { CornerBack = Theme.PanelBg };
            row.ApplyButton.Text = _loc.Get(LocKeys.Main_Item_Apply);
            row.ApplyButton.Click += (_, _) => ApplyRequested?.Invoke(this, entry);
            row.DeleteButton.Click += (_, _) => DeleteRequested?.Invoke(this, entry);
            _rows.Add(row);
            _list.Controls.Add(row);
        }

        _emptyNote.Visible = _rows.Count == 0;
        _list.AutoScrollPosition = new Point(0, 0);
        LayoutRows();
    }

    public override void ApplyStrings(LocalizationCatalog loc)
    {
        _sub.Text = loc.Get(LocKeys.Main_Profiles_Subtitle);
        _saveBtn.Text = loc.Get(LocKeys.Menu_Profile_Save);
        _savedLabel.Text = loc.Get(LocKeys.Menu_Profile_Load);
        _emptyNote.Text = loc.Get(LocKeys.Menu_Placeholder_NoProfiles);
        foreach (SavedItemRow row in _rows)
            row.ApplyButton.Text = loc.Get(LocKeys.Main_Item_Apply);
    }

    private void ClearRows()
    {
        foreach (SavedItemRow row in _rows)
        {
            _list.Controls.Remove(row);
            row.Dispose(); // click handlers die with the row's buttons
        }
        _rows.Clear();
    }

    // ── Layout ── (the saved list fills the rest and scrolls)

    protected override void OnLayout(LayoutEventArgs levent)
    {
        base.OnLayout(levent);

        int pad = Theme.Pad + 2;
        int x = pad, y = 0;
        int cw = Math.Max(0, ClientSize.Width - pad * 2);

        _sub.SetBounds(x, y, cw, 34);
        y += 34 + 12;

        _saveBtn.SetBounds(x, y, cw, 40);
        y += 40 + 16;

        _savedLabel.SetBounds(x, y, cw, 20);
        y += 20 + 8;
        _list.SetBounds(x, y, cw, Math.Max(0, ClientSize.Height - y - pad));
        LayoutRows();
    }

    private void LayoutRows()
    {
        int cw = _list.ClientSize.Width;
        int y = _list.AutoScrollPosition.Y; // negative when scrolled — keep positions in the scrolled space
        foreach (SavedItemRow row in _rows)
        {
            row.SetBounds(0, y, cw, 40);
            y += 40 + 6;
        }
        _emptyNote.SetBounds(0, 0, cw, 20);
    }
}
