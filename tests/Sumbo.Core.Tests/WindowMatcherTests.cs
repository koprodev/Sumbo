using System;
using System.Collections.Generic;
using Sumbo.Core;

namespace Sumbo.Core.Tests;

public class WindowMatcherTests
{
    private static WindowInfo Win(string title, string proc = "", string cls = "")
        => new(IntPtr.Zero, title, proc, cls);

    [Fact]
    public void Resolve_ProcessNamePlusTitle_HasPriority()
    {
        var spec = new TargetSpec
        {
            CapturedProcessName = "chrome",
            CapturedTitle = "Docs",
            MatchBy = MatchBy.Title,
            Value = "Docs",
        };
        var windows = new List<WindowInfo>
        {
            Win("Docs - Word", "winword", "OpusApp"),
            Win("Docs - Chrome", "chrome", "Chrome_WidgetWin_1"),
        };

        WindowInfo? m = WindowMatcher.Resolve(spec, windows);

        Assert.NotNull(m);
        Assert.Equal("Docs - Chrome", m!.Title);
    }

    [Fact]
    public void Resolve_FallsBackToTitle_WhenProcessMissing()
    {
        var spec = new TargetSpec { CapturedTitle = "Zoom", MatchBy = MatchBy.Title, Value = "Zoom" };
        var windows = new List<WindowInfo> { Win("Zoom Meeting", "zoom") };

        WindowInfo? m = WindowMatcher.Resolve(spec, windows);

        Assert.NotNull(m);
        Assert.Equal("Zoom Meeting", m!.Title);
    }

    [Fact]
    public void Resolve_Title_IsPartialAndCaseInsensitive()
    {
        var spec = new TargetSpec { CapturedTitle = "zoom" };
        var windows = new List<WindowInfo> { Win("Daily ZOOM standup") };

        Assert.NotNull(WindowMatcher.Resolve(spec, windows));
    }

    [Fact]
    public void Resolve_FallsBackToClassName_Exact()
    {
        var spec = new TargetSpec { CapturedClassName = "Notepad", MatchBy = MatchBy.ClassName, Value = "Notepad" };
        var windows = new List<WindowInfo> { Win("무제 - 메모장", "notepad", "Notepad") };

        WindowInfo? m = WindowMatcher.Resolve(spec, windows);

        Assert.NotNull(m);
        Assert.Equal("Notepad", m!.ClassName);
    }

    [Fact]
    public void Resolve_ProcessNameMatchBy_ExactFallback()
    {
        var spec = new TargetSpec { MatchBy = MatchBy.ProcessName, Value = "chrome" };
        var windows = new List<WindowInfo> { Win("Whatever", "chrome") };

        Assert.NotNull(WindowMatcher.Resolve(spec, windows));
    }

    [Fact]
    public void Resolve_ReturnsNull_WhenNoMatch()
    {
        var spec = new TargetSpec { CapturedTitle = "Nonexistent" };
        var windows = new List<WindowInfo> { Win("Something else", "explorer") };

        Assert.Null(WindowMatcher.Resolve(spec, windows));
    }

    [Fact]
    public void Resolve_ReturnsNull_WhenEmptyList()
    {
        var spec = new TargetSpec { CapturedTitle = "x" };
        Assert.Null(WindowMatcher.Resolve(spec, new List<WindowInfo>()));
    }

    [Fact]
    public void Resolve_HandleMatchBy_FallsThroughToCapturedIdentity()
    {
        // Window handles are volatile — a Handle spec is never resolved by handle; it must still
        // resolve gracefully via the captured title tier.
        var spec = new TargetSpec { MatchBy = MatchBy.Handle, Value = "0x1234", CapturedTitle = "Zoom" };
        var windows = new List<WindowInfo> { Win("Zoom Meeting", "zoom") };

        WindowInfo? m = WindowMatcher.Resolve(spec, windows);

        Assert.NotNull(m);
        Assert.Equal("Zoom Meeting", m!.Title);
    }
}
