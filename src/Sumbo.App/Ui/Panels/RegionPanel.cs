using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.Versioning;
using System.Windows.Forms;
using Sumbo.Core;

namespace Sumbo.App.Ui.Panels;

/// <summary>
/// The region-crop panel: arms the mirror-rect drag selection, clears the crop, saves the current region
/// under a name and manages the saved list (apply / delete). Pure view — each intent is an event the SHELL routes
/// to <c>MirrorSurface</c>/<c>RegionStore</c>; applied state comes back through <see cref="ReflectMirror"/> and the
/// saved list through <see cref="SetRegions"/> (the panel never touches the store or the mirror).
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed class RegionPanel : PanelView
{
    private readonly LocalizationCatalog _loc;

    private readonly Label _sub = new();
    private readonly FlatButton _selectBtn = new();
    private readonly FlatButton _clearBtn = new();
    private readonly Label _currentLabel = new();
    private readonly CardPanel _currentBox = new();
    private readonly Label _currentValue = new();
    private readonly FlatButton _saveBtn = new();
    private readonly Label _savedLabel = new();
    private readonly Panel _list = new() { AutoScroll = true };
    private readonly List<SavedItemRow> _rows = new();
    private readonly Label _emptyNote = new();

    private bool _hasMirror; // last reflected mirror state — saved rows apply onto a LIVE session only

    /// <summary>Select-region button — the shell arms the mirror-rect drag mode (the Ctrl+Alt+R hotkey's panel twin).</summary>
    public event EventHandler? SelectRequested;

    /// <summary>Clear-region button — the shell clears the crop back to the full source.</summary>
    public event EventHandler? ClearRequested;

    /// <summary>Save-current-region button — the shell prompts for a name and persists to the store.</summary>
    public event EventHandler? SaveRequested;

    /// <summary>Saved-row apply — the shell crops the live mirror to this region.</summary>
    public event EventHandler<NamedRegion>? ApplyRequested;

    /// <summary>Saved-row delete — destructive; the shell confirms before touching the store.</summary>
    public event EventHandler<NamedRegion>? DeleteRequested;

    public RegionPanel(LocalizationCatalog loc)
    {
        _loc = loc ?? throw new ArgumentNullException(nameof(loc));

        _sub.BackColor = Theme.PanelBg; _sub.ForeColor = Theme.TextMuted; _sub.Font = Theme.Small;
        _sub.AutoSize = false; _sub.TextAlign = ContentAlignment.TopLeft;

        _selectBtn.Kind = ButtonKind.Primary;
        _selectBtn.Glyph = Glyph.Crop;
        _selectBtn.GlyphSize = 12f;
        _selectBtn.CornerBack = Theme.PanelBg;
        _selectBtn.Click += (_, _) => SelectRequested?.Invoke(this, EventArgs.Empty);

        _clearBtn.Kind = ButtonKind.Dark;
        _clearBtn.CornerBack = Theme.PanelBg;
        _clearBtn.Click += (_, _) => ClearRequested?.Invoke(this, EventArgs.Empty);

        _currentLabel.BackColor = Theme.PanelBg; _currentLabel.ForeColor = Theme.TextSecondary; _currentLabel.Font = Theme.H2;
        _currentLabel.AutoSize = false; _currentLabel.TextAlign = ContentAlignment.MiddleLeft;

        _currentBox.CardColor = Theme.InsetBg; _currentBox.BorderColorValue = Theme.CardBorder;
        _currentBox.CornerBack = Theme.PanelBg; _currentBox.Radius = Theme.SmallRadius;
        _currentValue.BackColor = Theme.InsetBg; _currentValue.ForeColor = Theme.TextPrimary; _currentValue.Font = Theme.Body;
        _currentValue.AutoSize = false; _currentValue.TextAlign = ContentAlignment.MiddleLeft;
        _currentValue.Text = "—"; // unclipped (or no mirror)
        _currentBox.Controls.Add(_currentValue);

        _saveBtn.Kind = ButtonKind.Dark;
        _saveBtn.CornerBack = Theme.PanelBg;
        _saveBtn.Click += (_, _) => SaveRequested?.Invoke(this, EventArgs.Empty);

        _savedLabel.BackColor = Theme.PanelBg; _savedLabel.ForeColor = Theme.TextSecondary; _savedLabel.Font = Theme.H2;
        _savedLabel.AutoSize = false; _savedLabel.TextAlign = ContentAlignment.MiddleLeft;

        _list.BackColor = Theme.PanelBg;
        _list.ClientSizeChanged += (_, _) => LayoutRows(); // scrollbar appearing shrinks the client width

        _emptyNote.BackColor = Theme.PanelBg; _emptyNote.ForeColor = Theme.TextMuted; _emptyNote.Font = Theme.Small;
        _emptyNote.AutoSize = false; _emptyNote.TextAlign = ContentAlignment.MiddleLeft;
        _list.Controls.Add(_emptyNote);

        // No mirror yet — everything that acts on the live session starts disabled (ReflectMirror enables).
        _selectBtn.Enabled = false;
        _clearBtn.Enabled = false;
        _saveBtn.Enabled = false;

        Controls.Add(_sub);
        Controls.Add(_selectBtn);
        Controls.Add(_clearBtn);
        Controls.Add(_currentLabel);
        Controls.Add(_currentBox);
        Controls.Add(_saveBtn);
        Controls.Add(_savedLabel);
        Controls.Add(_list);
    }

    /// <summary>Shell → panel: enables the live-session actions and shows the applied crop readout. Saved rows stay
    /// listed while idle but their apply is disabled — a region is session state (a new mirror starts unclipped).</summary>
    public void ReflectMirror(bool hasMirror, Sumbo.Core.Region? region)
    {
        _hasMirror = hasMirror;
        _selectBtn.Enabled = hasMirror;
        _clearBtn.Enabled = hasMirror && region is not null;
        _saveBtn.Enabled = hasMirror && region is not null;
        _currentValue.Text = region is null ? "—" : Describe(region);
        foreach (SavedItemRow row in _rows)
            row.ApplyButton.Enabled = hasMirror;
    }

    /// <summary>Shell → panel: rebuilds the saved-region rows from the store snapshot.</summary>
    public void SetRegions(IReadOnlyList<NamedRegion> regions)
    {
        ClearRows();
        foreach (NamedRegion named in regions)
        {
            NamedRegion entry = named;
            var row = new SavedItemRow(entry.Name) { CornerBack = Theme.PanelBg };
            row.ApplyButton.Text = _loc.Get(LocKeys.Main_Item_Apply);
            row.ApplyButton.Enabled = _hasMirror;
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
        _sub.Text = loc.Get(LocKeys.Main_Region_Subtitle);
        _selectBtn.Text = loc.Get(LocKeys.Menu_Region_Select);
        _clearBtn.Text = loc.Get(LocKeys.Menu_Region_Clear);
        _currentLabel.Text = loc.Get(LocKeys.Main_Region_Current);
        _saveBtn.Text = loc.Get(LocKeys.Menu_Region_Save);
        _savedLabel.Text = loc.Get(LocKeys.Menu_Region_Saved);
        _emptyNote.Text = loc.Get(LocKeys.Menu_Placeholder_NoRegions);
        foreach (SavedItemRow row in _rows)
            row.ApplyButton.Text = loc.Get(LocKeys.Main_Item_Apply);
    }

    /// <summary>Human-readable crop readout — relative regions as percentages, absolute ones in source px.</summary>
    private static string Describe(Sumbo.Core.Region r) => r.Relative
        ? string.Format("{0:P0} · {1:P0}  —  {2:P0} · {3:P0}", r.Left, r.Top, r.Right, r.Bottom)
        : string.Format("{0:0} · {1:0}  —  {2:0} · {3:0} px", r.Left, r.Top, r.Right, r.Bottom);

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

        _selectBtn.SetBounds(x, y, cw, 40);
        y += 40 + 8;
        _clearBtn.SetBounds(x, y, cw, 40);
        y += 40 + 16;

        _currentLabel.SetBounds(x, y, cw, 20);
        y += 20 + 8;
        _currentBox.SetBounds(x, y, cw, 40);
        _currentValue.SetBounds(12, 0, Math.Max(0, _currentBox.Width - 24), 40);
        y += 40 + 8;
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

/// <summary>
/// A saved-item row shared by the region/profiles panels: bounded, ellipsized name label + hit-separated
/// apply/delete buttons. <c>ProfileChip</c>'s natural-width pill with a single <c>Selected</c> event fits
/// neither long names nor a destructive second action, so the list rows use explicit buttons instead.
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed class SavedItemRow : Panel
{
    private readonly Label _name = new();

    /// <summary>Apply button — text set by the owning panel (localized), enabled per the mirror state it reflects.</summary>
    public FlatButton ApplyButton { get; } = new();

    /// <summary>Delete button — glyph-only; the shell confirms before deleting.</summary>
    public FlatButton DeleteButton { get; } = new();

    public Color CornerBack { get; set; } = Theme.WindowBg;

    public SavedItemRow(string name)
    {
        BackColor = Theme.PanelBg;

        _name.BackColor = Theme.PanelBg; _name.ForeColor = Theme.TextPrimary; _name.Font = Theme.BodySemi;
        _name.AutoSize = false; _name.AutoEllipsis = true; _name.TextAlign = ContentAlignment.MiddleLeft;
        _name.Text = name;

        ApplyButton.Kind = ButtonKind.Dark;
        ApplyButton.CornerBack = Theme.PanelBg;
        ApplyButton.Font = Theme.Body;

        DeleteButton.Kind = ButtonKind.Ghost;
        DeleteButton.Glyph = Glyph.Delete;
        DeleteButton.GlyphSize = 11f;
        DeleteButton.CornerBack = Theme.PanelBg;

        Controls.Add(_name);
        Controls.Add(ApplyButton);
        Controls.Add(DeleteButton);
        Resize += (_, _) => Relayout();
    }

    private void Relayout()
    {
        const int btnH = 30;
        int y = (Height - btnH) / 2;
        DeleteButton.SetBounds(Math.Max(0, Width - btnH), y, btnH, btnH);
        ApplyButton.SetBounds(Math.Max(0, DeleteButton.Left - 6 - 56), y, 56, btnH);
        _name.SetBounds(0, 0, Math.Max(0, ApplyButton.Left - 8), Height);
    }
}
