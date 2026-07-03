using Sumbo.Core;

namespace Sumbo.Core.Tests;

public class WindowPlacementTests
{
    // Primary monitor work area 1920x1080, window 480x270.
    private const int WaL = 0, WaT = 0, WaR = 1920, WaB = 1080;
    private const int W = 480, H = 270;

    [Fact]
    public void Anchor_TopLeft()
        => Assert.Equal((0, 0), WindowPlacement.ComputeAnchoredLocation(SnapAnchor.TopLeft, W, H, WaL, WaT, WaR, WaB));

    [Fact]
    public void Anchor_BottomRight()
        => Assert.Equal((1440, 810), WindowPlacement.ComputeAnchoredLocation(SnapAnchor.BottomRight, W, H, WaL, WaT, WaR, WaB));

    [Fact]
    public void Anchor_Top_CentersHorizontally()
        => Assert.Equal((720, 0), WindowPlacement.ComputeAnchoredLocation(SnapAnchor.Top, W, H, WaL, WaT, WaR, WaB));

    [Fact]
    public void Anchor_Right_CentersVertically()
        => Assert.Equal((1440, 405), WindowPlacement.ComputeAnchoredLocation(SnapAnchor.Right, W, H, WaL, WaT, WaR, WaB));

    [Fact]
    public void Anchor_Center_CentersBothAxes()
        => Assert.Equal((720, 405), WindowPlacement.ComputeAnchoredLocation(SnapAnchor.Center, W, H, WaL, WaT, WaR, WaB));

    [Fact]
    public void Anchor_NegativeMonitorCoordinates()
    {
        // Secondary monitor to the left: work area (-1920..0).
        var (x, y) = WindowPlacement.ComputeAnchoredLocation(SnapAnchor.TopRight, W, H, -1920, 0, 0, 1080);
        Assert.Equal((-480, 0), (x, y));
    }

    [Fact]
    public void Anchor_OversizeWindow_ClampsToTopLeft()
    {
        // Window larger than the work area → pinned to (waLeft, waTop).
        var (x, y) = WindowPlacement.ComputeAnchoredLocation(SnapAnchor.BottomRight, 3000, 2000, WaL, WaT, WaR, WaB);
        Assert.Equal((0, 0), (x, y));
    }

    [Fact]
    public void SizeMode_Source_Half_Quarter()
    {
        Assert.Equal((1600, 900), WindowPlacement.ComputeSizeMode(ClientSizeMode.Source, 1600, 900, 1920, 1040));
        Assert.Equal((800, 450), WindowPlacement.ComputeSizeMode(ClientSizeMode.Half, 1600, 900, 1920, 1040));
        Assert.Equal((400, 225), WindowPlacement.ComputeSizeMode(ClientSizeMode.Quarter, 1600, 900, 1920, 1040));
    }

    [Fact]
    public void SizeMode_OversizeSource_CapsToWorkAreaPreservingAspect()
    {
        // 4000x2000 (2:1) capped into 1920x1040 → scale 0.48 → 1920x960.
        Assert.Equal((1920, 960), WindowPlacement.ComputeSizeMode(ClientSizeMode.Source, 4000, 2000, 1920, 1040));
    }

    [Fact]
    public void SizeMode_DegenerateSource_ReturnsDefault()
        => Assert.Equal((640, 400), WindowPlacement.ComputeSizeMode(ClientSizeMode.Source, 0, 0, 1920, 1040));

    [Fact]
    public void SizeMode_ChromeReducedArea_StaysWithinGivenClientArea()
    {
        // Caller passes work area minus chrome (1920-16, 1040-39) so the outer window fits.
        // 4000x2000 (2:1) capped into 1904x1001 → 1904x952 (aspect preserved, both ≤ given).
        Assert.Equal((1904, 952), WindowPlacement.ComputeSizeMode(ClientSizeMode.Source, 4000, 2000, 1904, 1001));
    }
}
