using System;
using System.Collections.Generic;
using System.Linq;
using Sumbo.Core;

namespace Sumbo.Core.Tests;

public class TargetListBuilderTests
{
    private static WindowInfo Win(string title, string process = "", string exePath = "")
        => new(IntPtr.Zero, title, process, ClassName: "", ProcessId: 0, ExecutablePath: exePath);

    private static readonly IReadOnlyList<WindowInfo> Sample = new[]
    {
        Win("Chrome", "chrome", @"C:\Program Files\Google\Chrome\chrome.exe"),
        Win("메모장", "notepad", @"C:\Windows\System32\notepad.exe"),
        Win("Photoshop", "Photoshop", @"C:\Adobe\Photoshop.exe"),
    };

    [Fact]
    public void EmptyQuery_ReturnsAll_OrderedByTitle_CaseInsensitive()
    {
        // Latin-only titles so the expected order (a < m < z, case-insensitive) is culture-independent.
        var windows = new[] { Win("Zeta"), Win("alpha"), Win("Mike") };

        var result = TargetListBuilder.Filter(windows, "");

        Assert.Equal(new[] { "alpha", "Mike", "Zeta" }, result.Select(w => w.Title).ToArray());
    }

    [Fact]
    public void WhitespaceQuery_ReturnsAll()
        => Assert.Equal(3, TargetListBuilder.Filter(Sample, "   ").Count);

    [Fact]
    public void Query_MatchesTitleSubstring()
    {
        var result = TargetListBuilder.Filter(Sample, "chro");
        Assert.Single(result);
        Assert.Equal("Chrome", result[0].Title);
    }

    [Fact]
    public void Query_IsCaseInsensitive()
    {
        var result = TargetListBuilder.Filter(Sample, "PHOTO");
        Assert.Single(result);
        Assert.Equal("Photoshop", result[0].Title);
    }

    [Fact]
    public void Query_MatchesExeFileName_NotJustTitle()
    {
        // "메모장" title doesn't contain "notepad", but its exe does.
        var result = TargetListBuilder.Filter(Sample, "notepad");
        Assert.Single(result);
        Assert.Equal("메모장", result[0].Title);
    }

    [Fact]
    public void Query_NoMatch_ReturnsEmpty()
        => Assert.Empty(TargetListBuilder.Filter(Sample, "zzz-nothing"));

    [Fact]
    public void DisplayExe_UsesFileNameFromPath()
        => Assert.Equal("chrome.exe", TargetListBuilder.DisplayExe(Sample[0]));

    [Fact]
    public void DisplayExe_FallsBackToProcessNameWithExe_WhenPathMissing()
    {
        var w = Win("Some App", "someapp", exePath: "");
        Assert.Equal("someapp.exe", TargetListBuilder.DisplayExe(w));
    }

    [Fact]
    public void DisplayExe_EmptyWhenNoPathAndNoProcessName()
        => Assert.Equal(string.Empty, TargetListBuilder.DisplayExe(Win("Untitled")));
}
