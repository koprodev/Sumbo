using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text.Json;

namespace Sumbo.Core;

/// <summary>
/// Runtime string catalog for FR-16 다국어 (한국어/영어 리소스 분리 + 런타임 전환, §5.2). Keyed by
/// <see cref="LocKeys"/> IDs; values come from per-language JSON tables. The catalog is an injected instance
/// (no static/framework culture probing) so it composes with the app's DI convention and stays test-isolated.
/// <para>
/// <b>Loading (임베디드 + 외부 오버라이드).</b> <see cref="Load"/> reads the base <c>lang.{lang}.json</c>
/// embedded in this assembly (so it ships inside the single-file publish, §11.3 — no satellite/loose files),
/// then merges any <c>{externalDir}\{lang}.json</c> over it key-by-key. This lets a translator/AI edit or
/// override language JSON post-deploy without a rebuild, while a corrupt/missing override falls back to the
/// embedded default.
/// </para>
/// <para>
/// <b>Runtime switch.</b> <see cref="SetLanguage"/> swaps the active language and raises
/// <see cref="LanguageChanged"/>. The App re-labels persistent surfaces on that signal (tray and main
/// window via direct subscription; the window fans out to its panels).
/// </para>
/// </summary>
public sealed class LocalizationCatalog
{
    public const string DefaultLanguage = "ko";

    // Supported UI languages (FR-16 = 한국어/영어). Kept ordinal + explicit; extend by adding an embedded table.
    private static readonly string[] SupportedLanguages = { "ko", "en" };

    // lang -> (key -> value). Read-only after construction.
    private readonly IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> _tables;
    private string _language;

    /// <summary>Raised after the active language changes (runtime switch, FR-16). UI re-labels on this.</summary>
    public event EventHandler? LanguageChanged;

    public LocalizationCatalog(
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> tables,
        string language)
    {
        _tables = tables ?? throw new ArgumentNullException(nameof(tables));
        _language = Normalize(language);
    }

    /// <summary>The active (already-normalized) language code.</summary>
    public string Language => _language;

    /// <summary>The UI languages the app offers (FR-16 = ko/en).</summary>
    public static IReadOnlyList<string> AvailableLanguages => SupportedLanguages;

    /// <summary>Coerces an arbitrary/persisted value to a supported language, else the default (FR-16 F1).</summary>
    public static string Normalize(string? language)
    {
        if (language is not null)
            foreach (string supported in SupportedLanguages)
                if (string.Equals(language, supported, StringComparison.OrdinalIgnoreCase))
                    return supported;
        return DefaultLanguage;
    }

    /// <summary>Switches the active language and raises <see cref="LanguageChanged"/> when it actually changes.</summary>
    public void SetLanguage(string language)
    {
        string next = Normalize(language);
        if (next == _language)
            return;
        _language = next;
        LanguageChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// The localized string for <paramref name="key"/> in the active language, falling back to the neutral
    /// default language, then to the raw key (so a missing string is visible, never a crash).
    /// </summary>
    public string Get(string key)
    {
        if (_tables.TryGetValue(_language, out IReadOnlyDictionary<string, string>? active)
            && active.TryGetValue(key, out string? value))
            return value;
        if (_language != DefaultLanguage
            && _tables.TryGetValue(DefaultLanguage, out IReadOnlyDictionary<string, string>? neutral)
            && neutral.TryGetValue(key, out string? fallback))
            return fallback;
        return key;
    }

    /// <summary>
    /// <see cref="Get"/> with numbered-placeholder substitution (<c>{0}</c>, <c>{1}</c>, …). Format strings
    /// use numbered placeholders (not C# interpolation) so substitution survives the catalog lookup.
    /// </summary>
    public string Format(string key, params object?[] args)
    {
        string template = Get(key);
        try
        {
            return string.Format(template, args);
        }
        catch (FormatException)
        {
            // A malformed placeholder in an external override (valid JSON, broken "{0" etc.) must not crash the
            // UI from a dialog/menu path — degrade to the raw template rather than throwing (F2).
            return template;
        }
    }

    /// <summary>Looks up a value in a specific language table without touching the active language (tests).</summary>
    public bool TryGet(string language, string key, out string value)
    {
        value = string.Empty;
        if (_tables.TryGetValue(Normalize(language), out IReadOnlyDictionary<string, string>? table)
            && table.TryGetValue(key, out string? found))
        {
            value = found;
            return true;
        }
        return false;
    }

    /// <summary>
    /// Builds a catalog from the assembly-embedded language tables, overlaying any external override files
    /// found under <paramref name="externalDir"/> (<c>{lang}.json</c>). A malformed or absent override is
    /// ignored (embedded default stands). The returned catalog starts on the normalized
    /// <paramref name="language"/>.
    /// </summary>
    public static LocalizationCatalog Load(string? language, string? externalDir)
    {
        var tables = new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.Ordinal);
        foreach (string lang in SupportedLanguages)
        {
            var table = new Dictionary<string, string>(StringComparer.Ordinal);

            Dictionary<string, string>? embedded = ReadEmbedded($".lang.{lang}.json");
            if (embedded is not null)
                Merge(table, embedded);

            if (!string.IsNullOrEmpty(externalDir))
            {
                Dictionary<string, string>? external = ReadExternal(externalDir, lang);
                if (external is not null)
                    Merge(table, external); // override embedded key-by-key
            }

            tables[lang] = table;
        }

        return new LocalizationCatalog(tables, Normalize(language));
    }

    private static void Merge(Dictionary<string, string> target, Dictionary<string, string> source)
    {
        foreach (KeyValuePair<string, string> kv in source)
            target[kv.Key] = kv.Value;
    }

    private static Dictionary<string, string>? ReadEmbedded(string manifestSuffix)
    {
        Assembly assembly = typeof(LocalizationCatalog).Assembly;
        string? name = null;
        foreach (string candidate in assembly.GetManifestResourceNames())
        {
            if (candidate.EndsWith(manifestSuffix, StringComparison.OrdinalIgnoreCase))
            {
                name = candidate;
                break;
            }
        }
        if (name is null)
            return null;

        try
        {
            using Stream? stream = assembly.GetManifestResourceStream(name);
            if (stream is null)
                return null;
            using var reader = new StreamReader(stream);
            return JsonSerializer.Deserialize<Dictionary<string, string>>(reader.ReadToEnd());
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            return null; // a broken embedded resource must not crash startup — fall back to keys
        }
    }

    private static Dictionary<string, string>? ReadExternal(string externalDir, string lang)
    {
        string path = Path.Combine(externalDir, $"{lang}.json");
        if (!File.Exists(path))
            return null;

        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(path));
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            return null; // a corrupt/locked override is ignored; embedded default stands
        }
    }
}
