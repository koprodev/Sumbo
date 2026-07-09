using System;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace Sumbo.App;

/// <summary>
/// One-shot GitHub latest-release probe for the startup update check. Best-effort by design: any network,
/// rate-limit or schema failure returns null and the app stays silent (the About panel's manual link remains
/// the fallback). No telemetry — a single anonymous GET against the public releases API.
/// </summary>
internal static class UpdateChecker
{
    private const string LatestReleaseApi = "https://api.github.com/repos/koprodev/Sumbo/releases/latest";
    private const string ReleasesPage = "https://github.com/koprodev/Sumbo/releases";

    /// <summary>Latest release tag + its page URL, or null on any failure.</summary>
    public static async Task<(string Tag, string Url)?> FetchLatestAsync()
    {
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd("Sumbo"); // the GitHub API rejects UA-less requests
            using JsonDocument doc = JsonDocument.Parse(await http.GetStringAsync(LatestReleaseApi).ConfigureAwait(false));

            string? tag = doc.RootElement.TryGetProperty("tag_name", out JsonElement t) ? t.GetString() : null;
            if (string.IsNullOrWhiteSpace(tag))
                return null;
            string url = doc.RootElement.TryGetProperty("html_url", out JsonElement u)
                && u.GetString() is { Length: > 0 } page ? page : ReleasesPage;
            return (tag, url);
        }
        catch
        {
            return null;
        }
    }
}
