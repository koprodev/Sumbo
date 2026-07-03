using System;
using System.Drawing;
using System.Reflection;
using System.Runtime.Versioning;
using System.Windows.Forms;
using Sumbo.Core;

namespace Sumbo.App.Ui.Panels;

/// <summary>
/// The about panel: product name + runtime version, an update-check link, a support (donation) invitation, and
/// license / source links. Auto-update is not implemented — the update button opens the releases page for a manual
/// check. The version follows the csproj <c>&lt;Version&gt;</c> as the single source of truth.
/// <para>
/// Pure view: link clicks raise <see cref="LinkActivated"/> with the target URL and the shell opens the browser.
/// </para>
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed class AboutPanel : PanelView
{
    private const string UrlReleases = "https://github.com/koprodev/Sumbo/releases";
    private const string UrlSponsors = "https://github.com/sponsors/koprodev";
    private const string UrlRepo = "https://github.com/koprodev/Sumbo";

    private readonly Label _sub = new();
    private readonly Label _product = new();
    private readonly Label _version = new();
    private readonly Label _updateNote = new();
    private readonly FlatButton _updateBtn = new();
    private readonly Label _supportNote = new();
    private readonly FlatButton _supportBtn = new();
    private readonly Label _license = new();
    private readonly FlatButton _sourceBtn = new();
    private readonly string _versionText = ResolveVersion();

    /// <summary>A link button was clicked — the argument is the target URL; the shell launches the browser.</summary>
    public event EventHandler<string>? LinkActivated;

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

        _updateBtn.Kind = ButtonKind.Dark; _updateBtn.CornerBack = Theme.PanelBg; _updateBtn.Font = Theme.BodySemi;
        _updateBtn.Click += (_, _) => LinkActivated?.Invoke(this, UrlReleases);

        _supportNote.BackColor = Theme.PanelBg; _supportNote.ForeColor = Theme.TextSecondary; _supportNote.Font = Theme.Small;
        _supportNote.AutoSize = false; _supportNote.TextAlign = ContentAlignment.TopLeft;

        _supportBtn.Kind = ButtonKind.Primary; _supportBtn.CornerBack = Theme.PanelBg; _supportBtn.Font = Theme.BodySemi;
        _supportBtn.Click += (_, _) => LinkActivated?.Invoke(this, UrlSponsors);

        _license.BackColor = Theme.PanelBg; _license.ForeColor = Theme.TextMuted; _license.Font = Theme.Small;
        _license.AutoSize = false; _license.TextAlign = ContentAlignment.MiddleLeft;

        _sourceBtn.Kind = ButtonKind.Ghost; _sourceBtn.CornerBack = Theme.PanelBg; _sourceBtn.Font = Theme.Body;
        _sourceBtn.Click += (_, _) => LinkActivated?.Invoke(this, UrlRepo);

        Controls.Add(_sub);
        Controls.Add(_product);
        Controls.Add(_version);
        Controls.Add(_updateNote);
        Controls.Add(_updateBtn);
        Controls.Add(_supportNote);
        Controls.Add(_supportBtn);
        Controls.Add(_license);
        Controls.Add(_sourceBtn);
    }

    public override void ApplyStrings(LocalizationCatalog loc)
    {
        _product.Text = loc.Get(LocKeys.App_Title);
        _sub.Text = loc.Get(LocKeys.Main_About_Subtitle);
        _version.Text = loc.Format(LocKeys.Main_About_Version, _versionText);
        _updateNote.Text = loc.Get(LocKeys.Main_About_UpdateNote);
        _updateBtn.Text = loc.Get(LocKeys.Main_About_UpdateCheck);
        _supportNote.Text = loc.Get(LocKeys.Main_About_SupportNote);
        _supportBtn.Text = loc.Get(LocKeys.Main_About_Support);
        _license.Text = loc.Get(LocKeys.Main_About_License);
        _sourceBtn.Text = loc.Get(LocKeys.Main_About_Source);
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
        y += 22 + 28;

        _updateNote.SetBounds(x, y, cw, 36);
        y += 36 + 8;
        _updateBtn.SetBounds(x, y, cw, 40);
        y += 40 + 28;

        _supportNote.SetBounds(x, y, cw, 36);
        y += 36 + 8;
        _supportBtn.SetBounds(x, y, cw, 44);
        y += 44 + 28;

        _license.SetBounds(x, y, cw, 18);
        y += 18 + 8;
        _sourceBtn.SetBounds(x, y, cw, 40);
    }
}
