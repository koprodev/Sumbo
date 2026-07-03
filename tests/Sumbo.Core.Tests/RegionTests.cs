using Sumbo.Core;
using Sumbo.Native;

namespace Sumbo.Core.Tests;

public class RegionTests
{
    [Fact]
    public void Absolute_NormalizesCornerOrder()
    {
        var region = Region.Absolute(300, 150, 100, 50); // swapped corners
        Assert.Equal((100.0, 50.0, 300.0, 150.0), (region.Left, region.Top, region.Right, region.Bottom));
        Assert.False(region.Relative);
    }

    [Fact]
    public void RelativeFromSource_ComputesRatios()
    {
        var region = Region.RelativeFromSource(160, 90, 800, 450, 1600, 900);
        Assert.True(region.Relative);
        Assert.Equal(0.1, region.Left, 6);
        Assert.Equal(0.1, region.Top, 6);
        Assert.Equal(0.5, region.Right, 6);
        Assert.Equal(0.5, region.Bottom, 6);
    }

    [Fact]
    public void ToSourceRect_Relative_ResolvesToPixels()
    {
        var region = new Region { Relative = true, Left = 0.1, Top = 0.1, Right = 0.9, Bottom = 0.9 };
        RECT r = region.ToSourceRect(1000, 500);
        Assert.Equal((100, 50, 900, 450), (r.Left, r.Top, r.Right, r.Bottom));
    }

    [Fact]
    public void ToSourceRect_Absolute_ClampsToSource()
    {
        var region = Region.Absolute(-20, -10, 5000, 4000); // beyond the source bounds
        RECT r = region.ToSourceRect(1600, 900);
        Assert.Equal((0, 0, 1600, 900), (r.Left, r.Top, r.Right, r.Bottom));
    }

    [Fact]
    public void ToSourceRect_FullRelative_CoversWholeSource()
    {
        var region = new Region { Relative = true, Left = 0.0, Top = 0.0, Right = 1.0, Bottom = 1.0 };
        RECT r = region.ToSourceRect(1280, 720);
        Assert.Equal((0, 0, 1280, 720), (r.Left, r.Top, r.Right, r.Bottom));
    }

    [Fact]
    public void ToSourceRect_DegenerateSelection_GuaranteesOnePixel()
    {
        var region = new Region { Relative = false, Left = 100, Top = 100, Right = 100, Bottom = 100 };
        RECT r = region.ToSourceRect(800, 600);
        Assert.True(r.Width >= 1);
        Assert.True(r.Height >= 1);
    }

    [Fact]
    public void ToSourceRect_DegenerateSource_ReturnsFallback()
    {
        var region = new Region { Relative = true, Left = 0.1, Top = 0.1, Right = 0.9, Bottom = 0.9 };
        RECT r = region.ToSourceRect(0, 0);
        Assert.Equal((0, 0, 0, 0), (r.Left, r.Top, r.Right, r.Bottom));
    }
}
