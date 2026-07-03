using System;
using System.Collections.Generic;
using System.IO;
using Sumbo.Core;

namespace Sumbo.Core.Tests;

public class ProfileServiceTests : IDisposable
{
    private readonly string _path;

    public ProfileServiceTests()
    {
        _path = Path.Combine(Path.GetTempPath(), $"sumbo-profiles-{Guid.NewGuid():N}", "profiles.json");
    }

    public void Dispose()
    {
        string? dir = Path.GetDirectoryName(_path);
        if (dir is not null && Directory.Exists(dir))
            Directory.Delete(dir, recursive: true);
    }

    private static Profile Sample() => new()
    {
        Id = "p_001",
        Name = "회의 모니터링",
        Target = new TargetSpec
        {
            MatchBy = MatchBy.Title,
            Value = "Zoom",
            CapturedTitle = "Zoom",
            CapturedProcessName = "zoom",
        },
        Region = new ProfileRegion { Enabled = true, Relative = true, Left = 0.1, Top = 0.1, Right = 0.9, Bottom = 0.9 },
        Placement = new Placement { Monitor = 1, Anchor = SnapAnchor.TopRight, X = 0, Y = 0, Width = 480, Height = 270 },
        Opacity = 90,
        ClickThrough = true,
        ShowBorder = false,
    };

    [Fact]
    public void Load_MissingFile_ReturnsEmpty()
    {
        var svc = new ProfileService(_path);
        Assert.Empty(svc.Load().Profiles);
    }

    [Fact]
    public void SaveThenLoad_RoundTripsProfile()
    {
        var svc = new ProfileService(_path);
        svc.Save(new ProfilesFile { Version = 1, Profiles = new List<Profile> { Sample() } });

        ProfilesFile loaded = svc.Load();

        Assert.Single(loaded.Profiles);
        Profile p = loaded.Profiles[0];
        Assert.Equal("p_001", p.Id);
        Assert.Equal(MatchBy.Title, p.Target.MatchBy);
        Assert.Equal("Zoom", p.Target.Value);
        Assert.Equal("zoom", p.Target.CapturedProcessName);
        Assert.Equal(SnapAnchor.TopRight, p.Placement.Anchor);
        Assert.Equal(1, p.Placement.Monitor);
        Assert.True(p.Region!.Enabled);
        Assert.True(p.Region.Relative);
        Assert.Equal(0.9, p.Region.Right);
        Assert.Equal(90, p.Opacity);
        Assert.True(p.ClickThrough);
    }

    [Fact]
    public void Serialize_UsesCamelCaseAndStringEnums()
    {
        string json = ProfileService.Serialize(new ProfilesFile { Profiles = new List<Profile> { Sample() } });

        // Wire format: camelCase property names + string enum values.
        Assert.Contains("\"matchBy\": \"title\"", json);
        Assert.Contains("\"anchor\": \"topRight\"", json);
        Assert.Contains("\"showBorder\": false", json);
        Assert.Contains("\"profiles\"", json);
        Assert.DoesNotContain("\"MatchBy\"", json);
        Assert.DoesNotContain("\"TopRight\"", json);
    }

    [Fact]
    public void Upsert_ReplacesById()
    {
        var svc = new ProfileService(_path);
        svc.Upsert(Sample());
        svc.Upsert(Sample() with { Name = "변경됨" });

        ProfilesFile loaded = svc.Load();
        Assert.Single(loaded.Profiles);
        Assert.Equal("변경됨", loaded.Profiles[0].Name);
    }

    [Fact]
    public void NullRegion_RoundTripsAsNull()
    {
        var svc = new ProfileService(_path);
        svc.Save(new ProfilesFile { Profiles = new List<Profile> { Sample() with { Region = null } } });

        Assert.Null(svc.Load().Profiles[0].Region);
    }

    [Fact]
    public void Load_CorruptFile_ReturnsEmpty()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        File.WriteAllText(_path, "{ not valid json ]");

        Assert.Empty(new ProfileService(_path).Load().Profiles);
    }

    [Fact]
    public void SaveThenLoad_RoundTripsAlwaysOnTop()
    {
        var svc = new ProfileService(_path);
        svc.Save(new ProfilesFile { Profiles = new List<Profile> { Sample() with { AlwaysOnTop = false } } });

        Assert.False(svc.Load().Profiles[0].AlwaysOnTop);
    }

    [Fact]
    public void Load_LegacyProfileWithoutAlwaysOnTop_DefaultsTrue()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        // A profiles.json entry with no "alwaysOnTop" key restores as true (initializer preserved, no migration).
        File.WriteAllText(_path, "{ \"version\": 1, \"profiles\": [ { \"id\": \"p_x\", \"name\": \"legacy\", \"opacity\": 90 } ] }");

        Assert.True(new ProfileService(_path).Load().Profiles[0].AlwaysOnTop);
    }

    [Fact]
    public void SaveThenLoad_RoundTripsCenterAnchor() // Center is a tail-appended enum member
    {
        var svc = new ProfileService(_path);
        svc.Save(new ProfilesFile
        {
            Profiles = new List<Profile> { Sample() with { Placement = new Placement { Anchor = SnapAnchor.Center } } },
        });

        Assert.Equal(SnapAnchor.Center, svc.Load().Profiles[0].Placement.Anchor);
    }

    [Fact]
    public void Serialize_CenterAnchor_UsesStringName()
    {
        string json = ProfileService.Serialize(new ProfilesFile
        {
            Profiles = new List<Profile> { Sample() with { Placement = new Placement { Anchor = SnapAnchor.Center } } },
        });

        Assert.Contains("\"anchor\": \"center\"", json);
    }
}
