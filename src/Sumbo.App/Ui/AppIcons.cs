using System;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Runtime.Versioning;

namespace Sumbo.App.Ui;

/// <summary>
/// Loads the branded multi-size icon (assets/sumbo.ico) from the embedded resource, so single-file publish
/// needs no on-disk asset and the tray gets a crisp small frame instead of a downscaled 32px one. Each call
/// returns a NEW <see cref="Icon"/> the caller owns and must dispose (TrayHost keeps a field released after
/// the NotifyIcon; MainWindow releases in OnFormClosed).
/// </summary>
[SupportedOSPlatform("windows")]
internal static class AppIcons
{
    private const string ResourceName = "Sumbo.App.assets.sumbo.ico";

    /// <summary>New <see cref="Icon"/> with the best-matching frame for <paramref name="size"/> (px).</summary>
    public static Icon Load(int size)
    {
        using Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"Missing embedded icon resource '{ResourceName}'.");
        return new Icon(stream, size, size);
    }
}
