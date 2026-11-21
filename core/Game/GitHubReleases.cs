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

    /// <summary>
    /// The releases page for one mod, to send the player to: the repo holds several mods, so the list is searched for the mod's
    /// tag (<paramref name="tagPrefix"/> without its "-v": "spireqol"), which leaves out the other mods' releases.
    /// </summary>
    public static string PageUrl(string repo, string tagPrefix) => $"https://github.com/{repo}/releases?q={Uri.EscapeDataString(tagPrefix.TrimEnd('v').TrimEnd('-'))}";

    /// <summary>One release's page (its notes and download), by its tag: <paramref name="tagPrefix"/> and the version.</summary>
    public static string ReleaseUrl(string repo, string tagPrefix, Version version) => $"https://github.com/{repo}/releases/tag/{tagPrefix}{version.ToString(3)}";

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
                if (Versions(doc.RootElement, tagPrefix) is not { } otherVersions) return null;
                versions.AddRange(otherVersions);
                if (doc.RootElement.GetArrayLength() < 100) break;
            }
            var cur = Normalize(current);
            return new Status(versions.Count(version => version > cur), versions.Count > 0 ? versions.Max() : null, versions.Contains(cur));
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
        foreach (var jsonElement in releases.EnumerateArray())
        {
            if (jsonElement.TryGetProperty("draft", out var otherJsonElement) && otherJsonElement.ValueKind == JsonValueKind.True) continue;
            if (jsonElement.TryGetProperty("prerelease", out var nextJsonElement) && nextJsonElement.ValueKind == JsonValueKind.True) continue;
            if (!jsonElement.TryGetProperty("tag_name", out var t) || t.GetString() is not { } tag) continue;
            if (!tag.StartsWith(tagPrefix, StringComparison.OrdinalIgnoreCase)) continue;
            if (Version.TryParse(tag[tagPrefix.Length..], out var version)) list.Add(Normalize(version));
        }
        return list;
    }

    // 0.31.13 and 0.31.13.0 compare equal.
    private static Version Normalize(Version version) => new(version.Major, version.Minor, Math.Max(0, version.Build));
}
