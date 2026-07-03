using System.Collections.Generic;

namespace Sumbo.Core;

/// <summary>Persisted region for a profile. <c>Enabled=false</c> ⇒ full window (no clip).</summary>
public sealed record ProfileRegion
{
    public bool Enabled { get; init; }
    public bool Relative { get; init; }
    public double Left { get; init; }
    public double Top { get; init; }
    public double Right { get; init; }
    public double Bottom { get; init; }

    /// <summary>Maps a live <see cref="Region"/> (or null) to a profile region.</summary>
    public static ProfileRegion? FromRegion(Region? region) => region is null
        ? null
        : new ProfileRegion
        {
            Enabled = true,
            Relative = region.Relative,
            Left = region.Left,
            Top = region.Top,
            Right = region.Right,
            Bottom = region.Bottom,
        };

    /// <summary>Resolves back to a live <see cref="Region"/>, or null when disabled.</summary>
    public Region? ToRegion() => Enabled
        ? new Region { Relative = Relative, Left = Left, Top = Top, Right = Right, Bottom = Bottom }
        : null;
}

/// <summary>Persisted window placement: screen-pixel bounds + monitor index + anchor.</summary>
public sealed record Placement
{
    public int Monitor { get; init; }
    public SnapAnchor? Anchor { get; init; }
    public int X { get; init; }
    public int Y { get; init; }
    public int Width { get; init; }
    public int Height { get; init; }
}

/// <summary>
/// One saved clone configuration: a single clone window's target + view settings (one profile = one
/// clone window; the schema does not model multi-clone workspaces).
/// </summary>
public sealed record Profile
{
    public string Id { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public TargetSpec Target { get; init; } = new();
    public ProfileRegion? Region { get; init; }
    public Placement Placement { get; init; } = new();
    public int Opacity { get; init; } = 100;
    public bool ClickThrough { get; init; }

    /// <summary>Border-highlight state — persisted per profile and restored on apply.</summary>
    public bool ShowBorder { get; init; }

    /// <summary>
    /// Always-on-top state — persisted per profile and restored on apply. <b>Defaults to true</b> so
    /// profiles saved without this key restore as always-on-top (System.Text.Json keeps the initializer
    /// when the key is absent).
    /// </summary>
    public bool AlwaysOnTop { get; init; } = true;
}

/// <summary>Root of <c>profiles.json</c>.</summary>
public sealed record ProfilesFile
{
    public int Version { get; init; } = 1;
    public List<Profile> Profiles { get; init; } = new();
}
