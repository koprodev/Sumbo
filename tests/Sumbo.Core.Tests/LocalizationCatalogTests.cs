using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Sumbo.Core;

namespace Sumbo.Core.Tests;

public class LocalizationCatalogTests : IDisposable
{
    private readonly string _dir;

    public LocalizationCatalogTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), $"sumbo-lang-{Guid.NewGuid():N}");
    }

    public void Dispose()
    {
        if (Directory.Exists(_dir))
            Directory.Delete(_dir, recursive: true);
    }

    private static LocalizationCatalog InMemory(string language)
    {
        var tables = new Dictionary<string, IReadOnlyDictionary<string, string>>
        {
            ["ko"] = new Dictionary<string, string> { ["k.only.ko"] = "코", ["k.both"] = "양", ["k.fmt"] = "값 {0}/{1}" },
            ["en"] = new Dictionary<string, string> { ["k.both"] = "both" }, // deliberately missing k.only.ko + k.fmt
        };
        return new LocalizationCatalog(tables, language);
    }

    [Fact]
    public void Load_EmbeddedTables_CoverEveryKey() // drift guard — every LocKeys const must exist in ko + en
    {
        LocalizationCatalog catalog = LocalizationCatalog.Load("ko", null);

        var missing = new List<string>();
        foreach (string key in LocKeys.All)
        {
            if (!catalog.TryGet("ko", key, out _)) missing.Add($"ko:{key}");
            if (!catalog.TryGet("en", key, out _)) missing.Add($"en:{key}");
        }

        Assert.Empty(missing);
    }

    [Fact]
    public void EmbeddedManifest_ContainsBothLanguageResources()
    {
        string[] names = typeof(LocalizationCatalog).Assembly.GetManifestResourceNames();

        Assert.Contains(names, n => n.EndsWith("lang.ko.json", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(names, n => n.EndsWith("lang.en.json", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Get_FallsBackToNeutralThenKey()
    {
        LocalizationCatalog catalog = InMemory("en");

        Assert.Equal("both", catalog.Get("k.both"));      // present in active (en)
        Assert.Equal("코", catalog.Get("k.only.ko"));      // missing in en → neutral ko
        Assert.Equal("k.no.such", catalog.Get("k.no.such")); // missing everywhere → raw key (no crash)
    }

    [Fact]
    public void Format_SubstitutesNumberedPlaceholders()
    {
        LocalizationCatalog catalog = InMemory("ko");

        Assert.Equal("값 A/B", catalog.Format("k.fmt", "A", "B"));
    }

    [Fact]
    public void SetLanguage_RaisesEventOnceAndSwitchesActive()
    {
        LocalizationCatalog catalog = InMemory("ko");
        int raised = 0;
        catalog.LanguageChanged += (_, _) => raised++;

        catalog.SetLanguage("en");
        Assert.Equal("en", catalog.Language);
        Assert.Equal(1, raised);

        catalog.SetLanguage("en"); // no-op — same language, no event
        Assert.Equal(1, raised);
    }

    [Theory]
    [InlineData("ko", "ko")]
    [InlineData("en", "en")]
    [InlineData("EN", "en")]   // case-insensitive
    [InlineData("ja", "ko")]   // unsupported → default
    [InlineData(null, "ko")]   // null → default
    public void Normalize_CoercesToSupportedOrDefault(string? input, string expected)
    {
        Assert.Equal(expected, LocalizationCatalog.Normalize(input));
    }

    [Fact]
    public void Load_UnsupportedLanguage_StartsOnNormalizedDefault() // FR-16 F1
    {
        LocalizationCatalog catalog = LocalizationCatalog.Load("ja", null);

        Assert.Equal("ko", catalog.Language);
    }

    [Fact]
    public void Load_ExternalOverride_TakesPrecedenceOverEmbedded_PerKey()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Path.Combine(_dir, "ko.json"), "{ \"app.title\": \"OVERRIDE\" }");

        LocalizationCatalog embedded = LocalizationCatalog.Load("ko", null);
        LocalizationCatalog overridden = LocalizationCatalog.Load("ko", _dir);

        Assert.Equal("OVERRIDE", overridden.Get(LocKeys.App_Title));                         // override wins
        Assert.Equal(embedded.Get(LocKeys.Menu_Target), overridden.Get(LocKeys.Menu_Target)); // non-overridden key stays embedded
    }

    [Fact]
    public void Format_MalformedOverridePlaceholder_DoesNotThrow() // F2
    {
        Directory.CreateDirectory(_dir);
        // Valid JSON, but the value carries a broken format placeholder ("{0" is never closed).
        File.WriteAllText(Path.Combine(_dir, "ko.json"), "{ \"dialog.autoStartFailed.body\": \"broken {0\" }");
        LocalizationCatalog catalog = LocalizationCatalog.Load("ko", _dir);

        string result = catalog.Format(LocKeys.Dialog_AutoStartFailed_Body, "detail");

        Assert.Equal("broken {0", result); // degraded to the raw template — no FormatException
    }

    [Fact]
    public void Load_CorruptExternalOverride_IgnoredAndEmbeddedStands()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Path.Combine(_dir, "ko.json"), "{ not valid json ]");

        LocalizationCatalog embedded = LocalizationCatalog.Load("ko", null);
        LocalizationCatalog withCorrupt = LocalizationCatalog.Load("ko", _dir);

        Assert.Equal(embedded.Get(LocKeys.App_Title), withCorrupt.Get(LocKeys.App_Title));
    }
}
