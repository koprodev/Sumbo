using Sumbo.Core;
using Sumbo.Native;

namespace Sumbo.Core.Tests;

public class CoordinateMapperTests
{
    [Fact]
    public void ActiveSource_NoRegion_IsFullSource()
    {
        RECT active = CoordinateMapper.ActiveSource(null, 1600, 900);
        Assert.Equal((0, 0, 1600, 900), (active.Left, active.Top, active.Right, active.Bottom));
    }

    [Fact]
    public void ActiveSource_WithRegion_IsResolvedRect()
    {
        var region = Region.Absolute(100, 50, 300, 150);
        RECT active = CoordinateMapper.ActiveSource(region, 1600, 900);
        Assert.Equal((100, 50, 300, 150), (active.Left, active.Top, active.Right, active.Bottom));
    }

    [Fact]
    public void MapDestToSource_Center_MapsToSourceCenter()
    {
        // dest letterboxed at (10,20)-(210,120) [200x100] shows full source 400x200.
        var rcDest = new RECT(10, 20, 210, 120);
        var active = new RECT(0, 0, 400, 200);

        Assert.Equal((200, 100), CoordinateMapper.MapDestToSource(110, 70, rcDest, active));
    }

    [Fact]
    public void MapDestToSource_TopLeftCorner_MapsToSourceOrigin()
    {
        var rcDest = new RECT(10, 20, 210, 120);
        var active = new RECT(0, 0, 400, 200);

        Assert.Equal((0, 0), CoordinateMapper.MapDestToSource(10, 20, rcDest, active));
    }

    [Fact]
    public void MapDestToSource_FarCorner_ClampsInsideSource()
    {
        var rcDest = new RECT(10, 20, 210, 120);
        var active = new RECT(0, 0, 400, 200);

        // u=v=1.0 → (400,200) clamped to (Right-1, Bottom-1).
        Assert.Equal((399, 199), CoordinateMapper.MapDestToSource(210, 120, rcDest, active));
    }

    [Fact]
    public void MapDestToSource_OutsideLetterbox_ReturnsNull()
    {
        var rcDest = new RECT(10, 20, 210, 120);
        var active = new RECT(0, 0, 400, 200);

        Assert.Null(CoordinateMapper.MapDestToSource(5, 70, rcDest, active));   // left margin
        Assert.Null(CoordinateMapper.MapDestToSource(110, 5, rcDest, active));  // top margin
        Assert.Null(CoordinateMapper.MapDestToSource(250, 70, rcDest, active)); // right margin
    }

    [Fact]
    public void MapDestToSource_RegionOffset_MapsWithinRegion()
    {
        // A region in the middle of the source: active source origin is (100,50).
        var rcDest = new RECT(0, 0, 200, 100);
        var active = new RECT(100, 50, 300, 150); // 200x100

        Assert.Equal((100, 50), CoordinateMapper.MapDestToSource(0, 0, rcDest, active));
        Assert.Equal((200, 100), CoordinateMapper.MapDestToSource(100, 50, rcDest, active));
    }

    [Fact]
    public void MapDestToSourceClamped_OutsidePoint_ClampsToEdge()
    {
        var rcDest = new RECT(10, 20, 210, 120);
        var active = new RECT(0, 0, 400, 200);

        // Far above-left of the clone → clamps to the source origin.
        Assert.Equal((0, 0), CoordinateMapper.MapDestToSourceClamped(-50, -50, rcDest, active));
        // Far below-right → clamps inside the source.
        Assert.Equal((399, 199), CoordinateMapper.MapDestToSourceClamped(999, 999, rcDest, active));
    }

    [Fact]
    public void MapDestToSource_DegenerateDestination_ReturnsNull()
        => Assert.Null(CoordinateMapper.MapDestToSource(0, 0, new RECT(0, 0, 0, 0), new RECT(0, 0, 100, 100)));
}
