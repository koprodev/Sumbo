using System;
using Sumbo.Core;

namespace Sumbo.Core.Tests;

public class UpdateCheckTests
{
    [Theory]
    [InlineData("v1.2.0", 1, 2, 0)]
    [InlineData("1.2.0", 1, 2, 0)]
    [InlineData("V1.2", 1, 2, 0)]        // case-insensitive prefix, missing build → 0
    [InlineData("2", 2, 0, 0)]           // bare major
    [InlineData(" v1.2.3 ", 1, 2, 3)]    // surrounding whitespace
    [InlineData("v1.2.0-rc1", 1, 2, 0)]  // pre-release suffix stripped
    [InlineData("v1.2.0+abc123", 1, 2, 0)] // build metadata stripped
    public void TryParseTag_AcceptsReleaseTagShapes(string tag, int major, int minor, int build)
    {
        Assert.True(UpdateCheck.TryParseTag(tag, out Version v));
        Assert.Equal(new Version(major, minor, build), v);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("latest")]
    [InlineData("v")]
    [InlineData("1.2.x")]
    public void TryParseTag_RejectsGarbage(string? tag)
    {
        Assert.False(UpdateCheck.TryParseTag(tag, out _));
    }

    [Theory]
    [InlineData("v1.2.1", 1, 2, 0, true)]   // patch bump
    [InlineData("v1.3.0", 1, 2, 0, true)]   // minor bump
    [InlineData("v2.0.0", 1, 2, 0, true)]   // major bump
    [InlineData("v1.2.0", 1, 2, 0, false)]  // equal
    [InlineData("v1.1.9", 1, 2, 0, false)]  // older
    [InlineData("v1.2", 1, 2, 0, false)]    // equal after normalize
    public void IsNewer_ComparesThreeComponents(string tag, int major, int minor, int build, bool expected)
    {
        Assert.Equal(expected, UpdateCheck.IsNewer(tag, new Version(major, minor, build)));
    }

    [Fact]
    public void IsNewer_IgnoresRevisionComponent() // assembly versions carry a 4th component (1.2.0.0)
    {
        Assert.False(UpdateCheck.IsNewer("v1.2.0", new Version(1, 2, 0, 0)));
        Assert.True(UpdateCheck.IsNewer("v1.2.1", new Version(1, 2, 0, 0)));
    }

    [Fact]
    public void IsNewer_FalseOnUnparsableOrMissingInput()
    {
        Assert.False(UpdateCheck.IsNewer("latest", new Version(1, 0, 0)));
        Assert.False(UpdateCheck.IsNewer("v1.2.0", null));
    }
}
