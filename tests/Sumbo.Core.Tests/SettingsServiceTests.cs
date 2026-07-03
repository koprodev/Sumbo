using System;
using System.Collections.Generic;
using System.IO;
using Sumbo.Core;

namespace Sumbo.Core.Tests;

public class SettingsServiceTests : IDisposable
{
    private readonly string _path;

    public SettingsServiceTests()
    {
        _path = Path.Combine(Path.GetTempPath(), $"sumbo-settings-{Guid.NewGuid():N}", "settings.json");
    }

    public void Dispose()
    {
        string? dir = Path.GetDirectoryName(_path);
        if (dir is not null && Directory.Exists(dir))
            Directory.Delete(dir, recursive: true);
    }

    [Fact]
    public void Load_MissingFile_ReturnsDefaults()
    {
        Settings s = new SettingsService(_path).Load();

        Assert.Equal(1, s.Version);
        Assert.Equal("ko", s.Language);
        Assert.False(s.StartWithWindows);
        Assert.True(s.MinimizeToTray);
        Assert.True(s.CheckUpdateOnStart);
        Assert.Equal(100, s.Defaults.Opacity);
        Assert.Equal(WheelAction.Opacity, s.Defaults.WheelAction);
    }

    [Fact]
    public void SaveThenLoad_RoundTrips()
    {
        var svc = new SettingsService(_path);
        svc.Save(new Settings
        {
            Language = "en",
            StartWithWindows = true,
            MinimizeToTray = false,
            CheckUpdateOnStart = false,
            Defaults = new SettingsDefaults { Opacity = 80, ShowBorder = true, ClickThrough = true, WheelAction = WheelAction.Zoom },
            Hotkeys = new Dictionary<string, string> { ["toggleVisible"] = "Ctrl+Alt+S" },
        });

        Settings s = svc.Load();

        Assert.Equal("en", s.Language);
        Assert.True(s.StartWithWindows);
        Assert.False(s.MinimizeToTray);
        Assert.False(s.CheckUpdateOnStart);
        Assert.Equal(80, s.Defaults.Opacity);
        Assert.True(s.Defaults.ShowBorder);
        Assert.True(s.Defaults.ClickThrough);
        Assert.Equal(WheelAction.Zoom, s.Defaults.WheelAction);
        Assert.Equal("Ctrl+Alt+S", s.Hotkeys["toggleVisible"]);
    }

    [Fact]
    public void Serialize_UsesCamelCaseAndStringEnums()
    {
        string json = SettingsService.Serialize(new Settings());

        // Wire format: camelCase property names + string enum values.
        Assert.Contains("\"startWithWindows\": false", json);
        Assert.Contains("\"minimizeToTray\": true", json);
        Assert.Contains("\"checkUpdateOnStart\": true", json);
        Assert.Contains("\"language\": \"ko\"", json);
        Assert.Contains("\"wheelAction\": \"opacity\"", json);
        Assert.DoesNotContain("\"StartWithWindows\"", json);
        Assert.DoesNotContain("\"MinimizeToTray\"", json);
    }

    [Fact]
    public void Load_CorruptFile_ReturnsDefaults()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        File.WriteAllText(_path, "{ not valid json ]");

        Settings s = new SettingsService(_path).Load();

        Assert.True(s.MinimizeToTray); // fell back to defaults
        Assert.Equal(1, s.Version);
    }

    [Fact]
    public void Load_MissingFile_DefaultsAlwaysOnTopTrue()
        => Assert.True(new SettingsService(_path).Load().Defaults.AlwaysOnTop);

    [Fact]
    public void SaveThenLoad_RoundTripsAlwaysOnTopFalse()
    {
        var svc = new SettingsService(_path);
        svc.Save(new Settings { Defaults = new SettingsDefaults { AlwaysOnTop = false } });

        Assert.False(svc.Load().Defaults.AlwaysOnTop);
    }

    [Fact]
    public void Load_LegacyFileWithoutAlwaysOnTop_DefaultsTrue()
    {
        // A settings.json without an "alwaysOnTop" key: System.Text.Json constructs SettingsDefaults via its
        // parameterless ctor (initializer = true runs) and only overwrites keys PRESENT in the JSON, so the missing
        // key keeps the true initializer — older files stay always-on-top without any migration.
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        File.WriteAllText(_path, "{ \"version\": 1, \"language\": \"en\", \"defaults\": { \"opacity\": 80, \"showBorder\": true } }");

        SettingsDefaults d = new SettingsService(_path).Load().Defaults;

        Assert.True(d.AlwaysOnTop); // missing key → true (not CLR default false)
        Assert.Equal(80, d.Opacity);
        Assert.True(d.ShowBorder);
    }
}
