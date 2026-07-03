using System.Runtime.Versioning;
using System.Windows.Forms;
using Sumbo.Core;

namespace Sumbo.App.Ui.Panels;

/// <summary>
/// Base class for the side option-panel views: one view per rail id, hosted by <c>MainWindow</c> under the shared
/// panel header and swapped by visibility. A view owns its child controls and internal layout
/// (<see cref="Control.OnLayout"/>); the shell owns the mirror and wires view events to it — a view never drives
/// <c>MirrorSurface</c> directly.
/// </summary>
[SupportedOSPlatform("windows")]
internal abstract class PanelView : Panel
{
    protected PanelView()
    {
        BackColor = Theme.PanelBg;
        Visible = false; // the shell shows exactly one view (SetActivePanel)
        DoubleBuffered = true;
    }

    /// <summary>Re-applies localized strings (initial build + language switch).</summary>
    public abstract void ApplyStrings(LocalizationCatalog loc);
}
