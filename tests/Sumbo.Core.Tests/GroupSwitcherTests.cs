using Sumbo.Core;

namespace Sumbo.Core.Tests;

public class GroupSwitcherTests
{
    private static TargetSpec Spec(string title) => new() { Value = title, CapturedTitle = title };

    [Fact]
    public void Next_EmptyGroup_ReturnsNull()
    {
        var g = new GroupSwitcher();
        Assert.Null(g.Next());
        Assert.False(g.Start());
        Assert.False(g.IsRunning);
    }

    [Fact]
    public void Next_WrapsAroundMembers()
    {
        var g = new GroupSwitcher();
        g.Add(Spec("A"));
        g.Add(Spec("B"));
        g.Add(Spec("C"));

        Assert.Equal("A", g.Next()!.Value);
        Assert.Equal("B", g.Next()!.Value);
        Assert.Equal("C", g.Next()!.Value);
        Assert.Equal("A", g.Next()!.Value); // wrap
        Assert.Equal(3, g.Count);
    }

    [Fact]
    public void SingleMember_AlwaysReturnsIt()
    {
        var g = new GroupSwitcher();
        g.Add(Spec("only"));

        Assert.Equal("only", g.Next()!.Value);
        Assert.Equal("only", g.Next()!.Value);
    }

    [Fact]
    public void Start_RequiresMembers_ThenStops()
    {
        var g = new GroupSwitcher();
        Assert.False(g.Start());

        g.Add(Spec("A"));
        Assert.True(g.Start());
        Assert.True(g.IsRunning);

        g.Stop();
        Assert.False(g.IsRunning);
    }

    [Fact]
    public void Clear_ResetsMembersAndState()
    {
        var g = new GroupSwitcher();
        g.Add(Spec("A"));
        g.Next();
        g.Start();

        g.Clear();

        Assert.Equal(0, g.Count);
        Assert.False(g.IsRunning);
        Assert.Null(g.Next());
    }

    [Fact]
    public void SetInterval_ClampsToMinimumOne()
    {
        var g = new GroupSwitcher();
        g.SetInterval(0);
        Assert.Equal(1, g.IntervalSeconds);
        g.SetInterval(30);
        Assert.Equal(30, g.IntervalSeconds);
    }
}
