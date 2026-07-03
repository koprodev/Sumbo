using System;
using System.Drawing;
using System.Runtime.Versioning;
using System.Windows.Forms;

namespace Sumbo.App.Ui.Panels;

/// <summary>A settings toggle row: label + description + <see cref="ToggleSwitch"/>. Shared by the display /
/// behavior / settings panels.</summary>
[SupportedOSPlatform("windows")]
internal sealed class ModeRow : Panel
{
    private readonly Label _label = new();
    private readonly Label _desc = new();
    public ToggleSwitch Toggle { get; } = new();

    public ModeRow()
    {
        BackColor = Theme.PanelBg;
        _label.BackColor = Theme.PanelBg; _label.ForeColor = Theme.TextPrimary; _label.Font = Theme.BodySemi;
        _label.AutoSize = false; _label.TextAlign = ContentAlignment.MiddleLeft;
        _desc.BackColor = Theme.PanelBg; _desc.ForeColor = Theme.TextMuted; _desc.Font = Theme.Small;
        _desc.AutoSize = false; _desc.TextAlign = ContentAlignment.MiddleLeft;
        Controls.Add(_label);
        Controls.Add(_desc);
        Controls.Add(Toggle);
        Resize += (_, _) => Relayout();
    }

    public void SetText(string label, string desc) { _label.Text = label; _desc.Text = desc; }

    /// <summary>Disables the toggle and mutes the label together. The row itself stays Enabled —
    /// cascading Enabled=false onto the standard Labels would trigger WinForms' etched disabled text rendering,
    /// which fights the dark theme; muting the ForeColor keeps the palette consistent.</summary>
    public void SetRowEnabled(bool on)
    {
        if (Toggle.Enabled == on)
            return;
        Toggle.Enabled = on;
        _label.ForeColor = on ? Theme.TextPrimary : Theme.TextMuted;
    }

    private void Relayout()
    {
        _label.SetBounds(0, 2, Width - 60, 20);
        _desc.SetBounds(0, 22, Width - 60, 18);
        Toggle.SetBounds(Width - 46, (Height - 26) / 2, 46, 26);
    }
}
