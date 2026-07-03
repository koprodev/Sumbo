using System.Collections.Generic;

namespace Sumbo.Core;

/// <summary>Persisted region for a profile (§7.2 <c>region</c>). <c>Enabled=false</c> ⇒ full window (no clip).</summary>
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

/// <summary>Persisted window placement (§7.2 <c>placement</c>). Screen-pixel bounds + monitor index + anchor.</summary>
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
/// One saved clone configuration (FR-13, §7.2). A profile is a single clone window's target + view
/// settings; multi-clone workspaces are a later schema extension (cycle② 결정 — 프로파일 1 = 복제창 1).
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

    /// <summary>FR-15 경계 강조 state (§7.2) — persisted per profile and restored by <c>ApplyProfile</c>.</summary>
    public bool ShowBorder { get; init; }

    /// <summary>
    /// 항상 위에 표시 state (M6-C) — persisted per profile and restored by <c>ApplyProfile</c>.
    /// <b>Defaults to true</b> so pre-M6-C profiles (missing the key) restore as always-on-top, matching the
    /// historical fixed <c>TopMost=true</c> behaviour (STJ keeps this initializer when the key is absent).
    /// </summary>
    public bool AlwaysOnTop { get; init; } = true;
}

/// <summary>Root of <c>profiles.json</c> (§7.2).</summary>
public sealed record ProfilesFile
{
    public int Version { get; init; } = 1;
    public List<Profile> Profiles { get; init; } = new();
}
