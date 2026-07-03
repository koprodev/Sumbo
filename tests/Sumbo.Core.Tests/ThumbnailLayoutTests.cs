using Sumbo.Core;

namespace Sumbo.Core.Tests;

public class ThumbnailLayoutTests
{
    [Fact]
    public void WiderHost_FitsByHeight_AndCentersHorizontally()
    {
        // src 1:1 inside a 2:1 host → 100x100 centered horizontally.
        var (left, top, right, bottom) =
            ThumbnailLayout.FitPreservingAspect(100, 100, 200, 100);

        Assert.Equal(50, left);
        Assert.Equal(0, top);
        Assert.Equal(150, right);
        Assert.Equal(100, bottom);
    }

    [Fact]
    public void TallerHost_FitsByWidth_AndCentersVertically()
    {
        // src 1:1 inside a 1:2 host → 100x100 centered vertically.
        var (left, top, right, bottom) =
            ThumbnailLayout.FitPreservingAspect(100, 100, 100, 200);

        Assert.Equal(0, left);
        Assert.Equal(50, top);
        Assert.Equal(100, right);
        Assert.Equal(150, bottom);
    }

    [Fact]
    public void WideSourceInSquareHost_PreservesAspect()
    {
        // src 16:9 (1600x900) inside a 400x400 host → fit by width, height 225.
        var (left, top, right, bottom) =
            ThumbnailLayout.FitPreservingAspect(1600, 900, 400, 400);

        Assert.Equal(0, left);
        Assert.Equal(87, top); // (400-225)/2 = 87 (integer division)
        Assert.Equal(400, right);
        Assert.Equal(312, bottom); // 87 + 225
    }

    [Fact]
    public void DegenerateInput_ReturnsFullHostArea()
    {
        var (left, top, right, bottom) =
            ThumbnailLayout.FitPreservingAspect(0, 0, 300, 200);

        Assert.Equal(0, left);
        Assert.Equal(0, top);
        Assert.Equal(300, right);
        Assert.Equal(200, bottom);
    }
}
