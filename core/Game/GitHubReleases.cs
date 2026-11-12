using System.Net.Http;
using System.Text.Json;

namespace Sts2Mods.Core.Game;

/// <summary>
/// Where the running mod stands against its published releases, from the repo's GitHub
/// releases list (public API, no sign-in). Sends nothing about the player: one GET with the mod's
/// name as the User-Agent. Callers decide when to ask (the main menu, not continuously).
/// </summary>
public static class GitHubReleases
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(8) };

    /// <summary>The releases page, to send the player to.</summary>
    public static string PageUrl(string repo) => $"https://github.com/{repo}/releases";

    /// <summary>What GitHub's releases say about the running version.</summary>
    /// <param name="Newer">Releases newer than it.</param>
    /// <param name="Latest">The newest release (null when there are none).</param>
    /// <param name="Released">It's one of the releases (a build that isn't is a test build).</param>
    public sealed record Status(int Newer, Version? Latest, bool Released);

    /// <summary>
    /// Releases tagged <paramref name="tagPrefix"/>&lt;version&gt; (not drafts or prereleases), against
    /// <paramref name="current"/>; null when GitHub can't be reached or answers with something else.
    /// </summary>
    public static async Task<Status?> Check(string repo, string tagPrefix, Version current, string userAgent)
    {
        try
        {
            // The list isn't newest-first, so read every page (100 each; a few at most).
            var versions = new List<Version>();
            for (var page = 1; page <= 10; page++)
            {
                using var req = new HttpRequestMessage(HttpMethod.Get, $"https://api.github.com/repos/{repo}/releases?per_page=100&page={page}");
                req.Headers.UserAgent.ParseAdd(userAgent);
                req.Headers.Accept.ParseAdd("application/vnd.github+json");
                using var res = await Http.SendAsync(req).ConfigureAwait(false);
                if (!res.IsSuccessStatusCode) return null;
                using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync().ConfigureAwait(false));
                if (Versions(doc.RootElement, tagPrefix) is not { } v) return null;
                versions.AddRange(v);
                if (doc.RootElement.GetArrayLength() < 100) break;
            }
            var cur = Normalize(current);
            return new Status(versions.Count(v => v > cur), versions.Count > 0 ? versions.Max() : null, versions.Contains(cur));
        }
        catch
        {
            return null;
        }
    }

    private static List<Version>? Versions(JsonElement releases, string tagPrefix)
    {
        if (releases.ValueKind != JsonValueKind.Array) return null;
        var list = new List<Version>();
        foreach (var r in releases.EnumerateArray())
        {
            if (r.TryGetProperty("draft", out var d) && d.ValueKind == JsonValueKind.True) continue;
            if (r.TryGetProperty("prerelease", out var p) && p.ValueKind == JsonValueKind.True) continue;
            if (!r.TryGetProperty("tag_name", out var t) || t.GetString() is not { } tag) continue;
            if (!tag.StartsWith(tagPrefix, StringComparison.OrdinalIgnoreCase)) continue;
            if (Version.TryParse(tag[tagPrefix.Length..], out var v)) list.Add(Normalize(v));
        }
        return list;
    }

    // 0.31.13 and 0.31.13.0 compare equal.
    private static Version Normalize(Version v) => new(v.Major, v.Minor, Math.Max(0, v.Build));
}
