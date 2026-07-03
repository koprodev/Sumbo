using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.Versioning;
using System.Windows.Forms;
using Sumbo.Core;

namespace Sumbo.App.Ui.Panels;

/// <summary>
/// The 대상 창 panel (V2-A flow, extracted into the V2-B panel framework): search box filtering a cached
/// enumeration, refresh, the target-card list and the stop button. The panel is a pure view — a card click raises
/// <see cref="TargetActivated"/> and the SHELL decides whether to start/keep the mirror, then reflects the outcome
/// back through <see cref="ReflectMirror"/> (an optimistic card self-select is undone here).
/// <para>
/// Ownership ([2차] F2): this panel owns the <see cref="WindowIconProvider"/>; cards hold image references only.
/// <see cref="Dispose(bool)"/> therefore unhooks + disposes the cards FIRST and the provider LAST.
/// </para>
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed class TargetsPanel : PanelView
{
    private readonly LocalizationCatalog _loc;

    private readonly CardPanel _searchBox = new();
    private readonly TextBox _searchInput = new();
    private readonly FlatButton _refreshBtn = new();
    private readonly Panel _targetList = new() { AutoScroll = true };
    private readonly List<TargetCard> _cards = new();
    private readonly FlatButton _stopBtn = new();
    private readonly WindowIconProvider _iconProvider = new();
    // Last enumeration result — the search box filters this cached list rather than re-enumerating per keystroke;
    // only ReloadTargets (initial / refresh / PickWindow hotkey) re-runs the Win32 enumeration (v1 F2 승계).
    private readonly List<WindowInfo> _allTargets = new();
    private bool _loaded;

    // Live-mirror state as last reflected by the shell — drives card selection + the stop button.
    private IntPtr _liveHandle;
    private bool _hasMirror;

    /// <summary>Card click on a real target. The shell decides start / retarget / keep (same-handle guard).</summary>
    public event EventHandler<WindowInfo>? TargetActivated;

    /// <summary>미러링 중지 button.</summary>
    public event EventHandler? StopRequested;

    public TargetsPanel(LocalizationCatalog loc)
    {
        _loc = loc ?? throw new ArgumentNullException(nameof(loc));

        _searchBox.CardColor = Theme.InsetBg;
        _searchBox.BorderColorValue = Theme.CardBorder;
        _searchBox.CornerBack = Theme.PanelBg;
        _searchBox.Radius = Theme.SmallRadius;
        _searchInput.BorderStyle = BorderStyle.None;
        _searchInput.BackColor = Theme.InsetBg;
        _searchInput.ForeColor = Theme.TextPrimary;
        _searchInput.Font = Theme.Body;
        _searchInput.TextChanged += (_, _) => ApplyFilter(); // filter the cached list only, no re-enumeration (F2 승계)
        _searchBox.Controls.Add(_searchInput);

        _refreshBtn.Kind = ButtonKind.Ghost;
        _refreshBtn.Glyph = Glyph.Refresh;
        _refreshBtn.GlyphSize = 12f;
        _refreshBtn.CornerBack = Theme.PanelBg;
        _refreshBtn.Size = new Size(30, 30);
        _refreshBtn.Click += (_, _) => ReloadTargets(); // re-enumerate

        _targetList.BackColor = Theme.PanelBg;
        _targetList.ClientSizeChanged += (_, _) => LayoutCards(); // scrollbar appearing shrinks the client width

        _stopBtn.Kind = ButtonKind.Dark;
        _stopBtn.Glyph = Glyph.Hide;
        _stopBtn.GlyphSize = 12f;
        _stopBtn.CornerBack = Theme.PanelBg;
        _stopBtn.Enabled = false;
        _stopBtn.Click += (_, _) => StopRequested?.Invoke(this, EventArgs.Empty);

        Controls.Add(_searchBox);
        Controls.Add(_refreshBtn);
        Controls.Add(_targetList);
        Controls.Add(_stopBtn);
    }

    /// <summary>Runs the first enumeration exactly once (called from the shell's OnShown, when the window exists).</summary>
    public void EnsureLoaded()
    {
        if (_loaded) return;
        _loaded = true;
        ReloadTargets();
    }

    /// <summary>Re-enumerates cloneable windows into the cached list, then rebuilds the filtered cards. This is the
    /// only path that runs the Win32 enumeration — refresh button, first show and the PickWindow hotkey (F2 승계).</summary>
    public void ReloadTargets()
    {
        _allTargets.Clear();
        try
        {
            _allTargets.AddRange(WindowEnumerator.GetCloneableWindows());
        }
        catch
        {
            // enumeration failed — leave the list empty; the empty panel simply shows no cards
        }
        ApplyFilter();
    }

    public void FocusSearch() => _searchInput.Focus();

    /// <summary>Shell → panel: aligns card selection and the stop button with the live mirror (start / stop /
    /// source loss / rejected click). Marks exactly the live target's card as selected, or none when idle.</summary>
    public void ReflectMirror(IntPtr liveHandle, bool hasMirror)
    {
        _liveHandle = liveHandle;
        _hasMirror = hasMirror;
        foreach (TargetCard card in _cards)
            card.IsSelected = hasMirror && card.Target?.Handle == liveHandle;
        _stopBtn.Enabled = hasMirror;
    }

    public override void ApplyStrings(LocalizationCatalog loc)
    {
        _searchInput.PlaceholderText = loc.Get(LocKeys.Main_SearchPlaceholder);
        _stopBtn.Text = loc.Get(LocKeys.Main_StopMirror);
        foreach (TargetCard c in _cards) { c.StatusText = loc.Get(LocKeys.Main_StatusRunning); c.Invalidate(); }
    }

    /// <summary>
    /// Rebuilds the target cards from the cached enumeration filtered by the search box. No auto-select (V2-A 승계):
    /// selecting a card STARTS the real mirror, so the card matching the live mirror is re-marked instead.
    /// </summary>
    private void ApplyFilter()
    {
        ClearCards();

        IReadOnlyList<WindowInfo> filtered = TargetListBuilder.Filter(_allTargets, _searchInput.Text);
        foreach (WindowInfo window in filtered)
        {
            var card = new TargetCard
            {
                Target = window,
                AppName = window.Title,
                ExeName = TargetListBuilder.DisplayExe(window),
                StatusText = _loc.Get(LocKeys.Main_StatusRunning),
                IconImage = _iconProvider.GetIcon(window),
                IconColor = Theme.Accent,
                CornerBack = Theme.PanelBg,
            };
            card.Selected += OnCardSelected;
            _cards.Add(card);
            _targetList.Controls.Add(card);
        }

        _targetList.AutoScrollPosition = new Point(0, 0);
        LayoutCards();
        ReflectMirror(_liveHandle, _hasMirror); // re-mark the live target among the rebuilt cards
    }

    private void OnCardSelected(object? sender, EventArgs e)
    {
        if (sender is not TargetCard card)
            return;

        if (card.Target is not WindowInfo target)
        {
            ReflectMirror(_liveHandle, _hasMirror); // undo the optimistic self-select — nothing to activate
            return;
        }

        TargetActivated?.Invoke(this, target);
    }

    /// <summary>Unhooks + disposes the cards and empties the list. Card images stay alive — they belong to
    /// <see cref="_iconProvider"/> (F2 ownership).</summary>
    private void ClearCards()
    {
        foreach (TargetCard card in _cards)
        {
            card.Selected -= OnCardSelected;
            _targetList.Controls.Remove(card);
            card.Dispose(); // disposes the card, NOT its IconImage (provider-owned)
        }
        _cards.Clear();
    }

    // ── Layout ── (metrics carried over 1:1 from the V2-A inline view)

    protected override void OnLayout(LayoutEventArgs levent)
    {
        base.OnLayout(levent);

        int pad = Theme.Pad + 2;
        int w = ClientSize.Width, h = ClientSize.Height;
        int cw = Math.Max(0, w - pad * 2);
        const int stopH = 44;

        _searchBox.SetBounds(pad, 0, Math.Max(0, cw - 38), 40);
        _searchInput.SetBounds(12, 10, Math.Max(0, _searchBox.Width - 20), 22);
        _refreshBtn.SetBounds(pad + cw - 30, 5, 30, 30);

        int listTop = 40 + 12;
        _targetList.SetBounds(pad, listTop, cw, Math.Max(0, h - listTop - stopH - pad - 10));
        LayoutCards();

        _stopBtn.SetBounds(pad, h - pad - stopH, cw, stopH);
    }

    private void LayoutCards()
    {
        int cw = _targetList.ClientSize.Width;
        int y = _targetList.AutoScrollPosition.Y; // negative when scrolled — keep positions in the scrolled space
        foreach (TargetCard c in _cards)
        {
            c.SetBounds(0, y, cw, 76);
            y += 76 + 10;
        }
    }

    /// <summary>[2차] F2 dispose contract: cards (image referencers) go first, the provider (image owner) last.</summary>
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            ClearCards();
            _iconProvider.Dispose();
        }
        base.Dispose(disposing);
    }
}
