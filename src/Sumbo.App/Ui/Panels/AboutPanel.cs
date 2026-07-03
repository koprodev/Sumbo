using System;
using System.Drawing;
using System.Reflection;
using System.Runtime.Versioning;
using System.Windows.Forms;
using Sumbo.Core;

namespace Sumbo.App.Ui.Panels;

/// <summary>
/// The about panel: product name + runtime version + slogan + an update-status note. Auto-update is not
/// implemented — the note states it is planned, no update action is wired.
/// Pure static view: no intent events, no <c>ReflectMirror</c> (nothing depends on the mirror state). The version is
/// read from the running assembly and follows the csproj <c>&lt;Version&gt;</c> as the single source of truth.
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed class AboutPanel : PanelView
{
    private readonly Label _sub = new();
    private readonly Label _product = new();
    private readonly Label _version = new();
    private readonly Label _updateNote = new();
    private readonly string _versionText = ResolveVersion();

    public AboutPanel()
    {
        _sub.BackColor = Theme.PanelBg; _sub.ForeColor = Theme.TextMuted; _sub.Font = Theme.Small;
        _sub.AutoSize = false; _sub.TextAlign = ContentAlignment.MiddleLeft;

        _product.BackColor = Theme.PanelBg; _product.ForeColor = Theme.TextPrimary; _product.Font = Theme.H1;
        _product.AutoSize = false; _product.TextAlign = ContentAlignment.MiddleLeft;

        _version.BackColor = Theme.PanelBg; _version.ForeColor = Theme.TextSecondary; _version.Font = Theme.Body;
        _version.AutoSize = false; _version.TextAlign = ContentAlignment.MiddleLeft;

        _updateNote.BackColor = Theme.PanelBg; _updateNote.ForeColor = Theme.TextMuted; _updateNote.Font = Theme.Small;
        _updateNote.AutoSize = false; _updateNote.TextAlign = ContentAlignment.TopLeft;

        Controls.Add(_sub);
        Controls.Add(_product);
        Controls.Add(_version);
        Controls.Add(_updateNote);
    }

    public override void ApplyStrings(LocalizationCatalog loc)
    {
        _product.Text = loc.Get(LocKeys.App_Title);
        _sub.Text = loc.Get(LocKeys.Main_About_Subtitle);
        _version.Text = loc.Format(LocKeys.Main_About_Version, _versionText);
        _updateNote.Text = loc.Get(LocKeys.Main_About_UpdateNote);
    }

    /// <summary>Runtime assembly version — the informational version (SourceLink commit suffix stripped) with the
    /// assembly version as fallback. Reflects the csproj <c>&lt;Version&gt;</c>.</summary>
    private static string ResolveVersion()
    {
        try
        {
            Assembly asm = Assembly.GetExecutingAssembly();
            string? info = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
            if (!string.IsNullOrWhiteSpace(info))
            {
                int plus = info.IndexOf('+'); // strip "+<commit>" SourceLink suffix
                return plus >= 0 ? info[..plus] : info;
            }
            return asm.GetName().Version?.ToString() ?? "—";
        }
        catch
        {
            return "—";
        }
    }

    protected override void OnLayout(LayoutEventArgs levent)
    {
        base.OnLayout(levent);

        int pad = Theme.Pad + 2;
        int x = pad, y = 0;
        int cw = Math.Max(0, ClientSize.Width - pad * 2);

        _sub.SetBounds(x, y, cw, 18);
        y += 18 + 20;

        _product.SetBounds(x, y, cw, 28);
        y += 28 + 6;
        _version.SetBounds(x, y, cw, 22);
        y += 22 + 24;

        _updateNote.SetBounds(x, y, cw, 40);
    }
}
