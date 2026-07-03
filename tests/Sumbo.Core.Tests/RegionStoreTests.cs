using System;
using System.Collections.Generic;
using System.IO;
using Sumbo.Core;

namespace Sumbo.Core.Tests;

public class RegionStoreTests : IDisposable
{
    private readonly string _path;

    public RegionStoreTests()
    {
        _path = Path.Combine(Path.GetTempPath(), $"sumbo-regions-{Guid.NewGuid():N}", "regions.json");
    }

    public void Dispose()
    {
        string? dir = Path.GetDirectoryName(_path);
        if (dir is not null && Directory.Exists(dir))
            Directory.Delete(dir, recursive: true);
    }

    [Fact]
    public void Load_MissingFile_ReturnsEmpty()
    {
        var store = new RegionStore(_path);
        Assert.Empty(store.Load());
    }

    [Fact]
    public void SaveThenLoad_RoundTripsRegions()
    {
        var store = new RegionStore(_path);
        var saved = new List<NamedRegion>
        {
            new("절대 영역", Region.Absolute(10, 20, 110, 220)),
            new("상대 영역", new Region { Relative = true, Left = 0.1, Top = 0.2, Right = 0.8, Bottom = 0.9 }),
        };

        store.Save(saved);
        IReadOnlyList<NamedRegion> loaded = store.Load();

        Assert.Equal(2, loaded.Count);

        Assert.Equal("절대 영역", loaded[0].Name);
        Assert.False(loaded[0].Region.Relative);
        Assert.Equal((10.0, 20.0, 110.0, 220.0),
            (loaded[0].Region.Left, loaded[0].Region.Top, loaded[0].Region.Right, loaded[0].Region.Bottom));

        Assert.Equal("상대 영역", loaded[1].Name);
        Assert.True(loaded[1].Region.Relative);
        Assert.Equal((0.1, 0.2, 0.8, 0.9),
            (loaded[1].Region.Left, loaded[1].Region.Top, loaded[1].Region.Right, loaded[1].Region.Bottom));
    }

    [Fact]
    public void Save_OverwritesPreviousContent()
    {
        var store = new RegionStore(_path);
        store.Save(new List<NamedRegion> { new("first", Region.Absolute(0, 0, 1, 1)) });
        store.Save(new List<NamedRegion> { new("second", Region.Absolute(2, 2, 3, 3)) });

        IReadOnlyList<NamedRegion> loaded = store.Load();
        Assert.Single(loaded);
        Assert.Equal("second", loaded[0].Name);
    }

    [Fact]
    public void Load_CorruptFile_ReturnsEmpty()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        File.WriteAllText(_path, "{ this is not valid json ]");

        var store = new RegionStore(_path);
        Assert.Empty(store.Load());
    }
}
