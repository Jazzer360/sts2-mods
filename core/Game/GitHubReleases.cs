using System.Net.Http;
using System.Text.Json;

namespace Sts2Mods.Core.Game;

/// <summary>
/// How many published releases of a mod are newer than the running one, from the repo's GitHub
/// releases list (public API, no sign-in). Sends nothing about the player: one GET with the mod's
/// name as the User-Agent. Callers decide when to ask (the main menu, not continuously).
/// </summary>
public static class GitHubReleases
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(8) };

    /// <summary>The releases page, to send the player to.</summary>
    public static string PageUrl(string repo) => $"https://github.com/{repo}/releases";

    /// <summary>
    /// Releases tagged <paramref name="tagPrefix"/>&lt;version&gt; (not drafts or prereleases) newer than
    /// <paramref name="current"/>; null when GitHub can't be reached or answers with something else.
    /// </summary>
    public static async Task<int?> CountNewer(string repo, string tagPrefix, Version current, string userAgent)
    {
        try
        {
            // The list isn't newest-first, so read every page (100 each; a few at most).
            var total = 0;
            for (var page = 1; page <= 10; page++)
            {
                using var req = new HttpRequestMessage(HttpMethod.Get, $"https://api.github.com/repos/{repo}/releases?per_page=100&page={page}");
                req.Headers.UserAgent.ParseAdd(userAgent);
                req.Headers.Accept.ParseAdd("application/vnd.github+json");
                using var res = await Http.SendAsync(req).ConfigureAwait(false);
                if (!res.IsSuccessStatusCode) return null;
                using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync().ConfigureAwait(false));
                if (CountNewer(doc.RootElement, tagPrefix, current) is not { } n) return null;
                total += n;
                if (doc.RootElement.GetArrayLength() < 100) break;
            }
            return total;
        }
        catch
        {
            return null;
        }
    }

    internal static int? CountNewer(JsonElement releases, string tagPrefix, Version current)
    {
        if (releases.ValueKind != JsonValueKind.Array) return null;
        var n = 0;
        foreach (var r in releases.EnumerateArray())
        {
            if (r.TryGetProperty("draft", out var d) && d.ValueKind == JsonValueKind.True) continue;
            if (r.TryGetProperty("prerelease", out var p) && p.ValueKind == JsonValueKind.True) continue;
            if (!r.TryGetProperty("tag_name", out var t) || t.GetString() is not { } tag) continue;
            if (!tag.StartsWith(tagPrefix, StringComparison.OrdinalIgnoreCase)) continue;
            if (Version.TryParse(tag[tagPrefix.Length..], out var v) && Normalize(v) > Normalize(current)) n++;
        }
        return n;
    }

    // 0.31.13 and 0.31.13.0 compare equal.
    private static Version Normalize(Version v) => new(v.Major, v.Minor, Math.Max(0, v.Build));
}
