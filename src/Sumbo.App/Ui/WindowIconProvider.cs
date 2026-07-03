using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.Versioning;
using Sumbo.Core;

namespace Sumbo.App.Ui;

/// <summary>
/// Loads and caches app icons for the target list. The icon is extracted from the window's process image
/// (<see cref="Icon.ExtractAssociatedIcon(string)"/>) and cached by executable path so several windows of the same
/// app share one bitmap.
/// <para>
/// Ownership: this provider is the sole owner of every cached <see cref="Image"/>. Callers
/// (<c>TargetCard.IconImage</c>) hold references only and must NOT dispose them — a rebuild of the list on refresh
/// reuses the cache, so disposing a card's image would corrupt other cards sharing the same exe. The provider's
/// images live until <see cref="Dispose"/>, which the owner calls after the cards are gone.
/// </para>
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed class WindowIconProvider : IDisposable
{
    private readonly Dictionary<string, Image?> _cache = new(StringComparer.OrdinalIgnoreCase);
    private bool _disposed;

    /// <summary>Returns the cached (or freshly extracted) app icon for <paramref name="window"/>, or null when the
    /// image path is unavailable / extraction fails — the card then draws its glyph placeholder.</summary>
    public Image? GetIcon(WindowInfo window)
    {
        ArgumentNullException.ThrowIfNull(window);
        if (_disposed)
            return null;

        string path = window.ExecutablePath;
        if (string.IsNullOrEmpty(path))
            return null;

        if (_cache.TryGetValue(path, out Image? cached))
            return cached;

        Image? image = null;
        try
        {
            using Icon? icon = Icon.ExtractAssociatedIcon(path);
            if (icon is not null)
                image = icon.ToBitmap();
        }
        catch
        {
            image = null; // inaccessible / not an icon-bearing file — fall back to the placeholder
        }

        _cache[path] = image; // cache misses too, so a failing path isn't retried every refresh
        return image;
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        foreach (Image? image in _cache.Values)
            image?.Dispose();
        _cache.Clear();
    }
}
