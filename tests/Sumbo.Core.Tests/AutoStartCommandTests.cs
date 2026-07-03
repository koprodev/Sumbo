using System;
using Sumbo.Core;

namespace Sumbo.Core.Tests;

public class AutoStartCommandTests
{
    private const string Exe = @"C:\Program Files\Sumbo\Sumbo.exe"; // space-containing path — the reason quoting matters

    [Fact]
    public void Build_QuotesAbsolutePath()
    {
        Assert.Equal("\"" + Exe + "\"", AutoStartCommand.Build(Exe));
    }

    [Fact]
    public void Build_NullOrEmpty_Throws()
    {
        Assert.Throws<ArgumentException>(() => AutoStartCommand.Build(""));
        Assert.Throws<ArgumentNullException>(() => AutoStartCommand.Build(null!));
    }

    [Fact]
    public void Matches_QuotedStoredValue_True()
    {
        Assert.True(AutoStartCommand.Matches("\"" + Exe + "\"", Exe));
    }

    [Fact]
    public void Matches_UnquotedStoredValue_True()
    {
        Assert.True(AutoStartCommand.Matches(Exe, Exe));
    }

    [Fact]
    public void Matches_DifferentPath_False()
    {
        Assert.False(AutoStartCommand.Matches("\"C:\\Other\\Sumbo.exe\"", Exe));
    }

    [Fact]
    public void Matches_NullOrEmpty_False()
    {
        Assert.False(AutoStartCommand.Matches(null, Exe));
        Assert.False(AutoStartCommand.Matches("", Exe));
    }
}
