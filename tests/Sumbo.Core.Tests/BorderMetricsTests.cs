using Sumbo.Core;

namespace Sumbo.Core.Tests;

public class BorderMetricsTests
{
    [Fact]
    public void PhysicalThickness_ScalesByDpiRatio()
    {
        Assert.Equal(4, BorderMetrics.PhysicalThickness(2, 200, 100)); // 2 * 200/100
        Assert.Equal(3, BorderMetrics.PhysicalThickness(3, 100, 100)); // 100% DPI — unchanged
        Assert.Equal(2, BorderMetrics.PhysicalThickness(1, 150, 100)); // 1 * 1.5 → round-away → 2
    }

    [Fact]
    public void PhysicalThickness_EnabledBorderNeverZero()
    {
        Assert.Equal(1, BorderMetrics.PhysicalThickness(1, 50, 100)); // rounds to 0 but clamps to ≥ 1
    }

    [Fact]
    public void PhysicalThickness_NonPositiveExtent_ReturnsZero()
    {
        Assert.Equal(0, BorderMetrics.PhysicalThickness(3, 0, 100));
        Assert.Equal(0, BorderMetrics.PhysicalThickness(0, 100, 100));
        Assert.Equal(0, BorderMetrics.PhysicalThickness(3, 100, 0));
    }

    [Fact]
    public void ToLogicalRect_UniformScale()
    {
        var (left, top, width, height) = BorderMetrics.ToLogicalRect(0, 0, 200, 100, 200, 100, 100, 50);
        Assert.Equal((0, 0, 100, 50), (left, top, width, height));
    }

    [Fact]
    public void ToLogicalRect_IndependentAxisScale()
    {
        // physical 200x100 → logical 100x100: x halves, y doubles.
        var (left, top, width, height) = BorderMetrics.ToLogicalRect(0, 0, 200, 200, 200, 100, 100, 100);
        Assert.Equal((0, 0, 100, 200), (left, top, width, height));
    }

    [Fact]
    public void ToLogicalRect_ZeroPhysical_FallsBackToLogical()
    {
        var (left, top, width, height) = BorderMetrics.ToLogicalRect(0, 0, 10, 10, 0, 100, 640, 400);
        Assert.Equal((0, 0, 640, 400), (left, top, width, height));
    }
}
