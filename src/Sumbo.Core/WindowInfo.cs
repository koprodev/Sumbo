using System;

namespace Sumbo.Core;

/// <summary>
/// A cloneable top-level window. <see cref="Handle"/> is volatile (valid for the current session only);
/// <see cref="ProcessName"/> / <see cref="ClassName"/> are captured for durable matching.
/// <para>
/// <see cref="ProcessId"/> and <see cref="ExecutablePath"/> are transient enrichment for the control-panel
/// target list — the process image path drives the app-icon lookup. They are intentionally NOT persisted
/// into <c>TargetSpec</c>/<c>Profile</c>.
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
