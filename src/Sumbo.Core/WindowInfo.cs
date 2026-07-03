using System;

namespace Sumbo.Core;

/// <summary>
/// A cloneable top-level window. <see cref="Handle"/> is volatile (session only, §7.4); the
/// <see cref="ProcessName"/> / <see cref="ClassName"/> are captured for durable matching (FR-13 matchBy).
/// <para>
/// <see cref="ProcessId"/> and <see cref="ExecutablePath"/> (M6-B) are transient enrichment for the control-panel
/// target list — the process image path drives the app-icon lookup (<c>WindowIconProvider</c>). They are appended
/// (optional, defaulted) so existing call sites and the <c>WindowMatcher</c> identity fields are unaffected, and are
/// intentionally NOT persisted into <c>TargetSpec</c>/<c>Profile</c>.
/// </para>
/// </summary>
public sealed record WindowInfo(
    IntPtr Handle,
    string Title,
    string ProcessName = "",
    string ClassName = "",
    uint ProcessId = 0,
    string ExecutablePath = "")
{
    public override string ToString() => Title;
}
