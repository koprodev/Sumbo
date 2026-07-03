using System;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Runtime.Versioning;

namespace Sumbo.App.Ui;

/// <summary>
/// Loads the multi-size app icon from the embedded resource, so single-file publish needs no on-disk asset and
/// small surfaces (tray) get a crisp native frame instead of a downscaled one. Each call returns a NEW
/// <see cref="Icon"/> that the caller owns and must dispose.
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
