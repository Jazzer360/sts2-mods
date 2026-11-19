using System.Text.Json;

namespace Sts2Mods.Core.Diagnostics;

/// <summary>
/// The oldest mod version whose error reports are still wanted (decision #81), from reporting.json on the repo's main
/// branch: a build older than that sends nothing, since its reports are about code that has been rewritten since. One GET
/// at startup with the mod's name as the User-Agent (nothing about the player); until GitHub answers, or if it can't be
/// reached, reports go out as before (the Sentry mirror drops the same versions anyway).
/// </summary>
public sealed class ReportFloor
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(8) };

    private readonly Version _current;
    private volatile string? _floor;
    private volatile IReadOnlyCollection<string> _gameplayMods = Array.Empty<string>();

    /// <summary>
    /// Mod ids reporting.json lists as changing the game though their manifest says they don't (gameplayMods, decision #97):
    /// treated like any mod that changes it (no mismatches sent with one loaded). Empty until GitHub answers.
    /// </summary>
    public IReadOnlyCollection<string> GameplayMods => _gameplayMods;

    private ReportFloor(Version current) => _current = current;

    /// <summary>Starts reading the floor for <paramref name="modId"/> in the background.</summary>
    public static ReportFloor Start(string repo, string modId, Version current, ModLog log)
    {
        var f = new ReportFloor(current);
        _ = Task.Run(async () =>
        {
            try
            {
                using var req = new HttpRequestMessage(HttpMethod.Get, $"https://raw.githubusercontent.com/{repo}/main/reporting.json");
                req.Headers.UserAgent.ParseAdd(modId);
                using var res = await Http.SendAsync(req).ConfigureAwait(false);
                if (!res.IsSuccessStatusCode) return;
                var text = await res.Content.ReadAsStringAsync().ConfigureAwait(false);
                f._gameplayMods = ParseGameplayMods(text);
                var floor = Parse(text, modId);
                if (floor == null) return;
                f._floor = floor.ToString(3);
                if (current < floor) log.Info($"error reporting off: this version is older than the oldest one reports are taken from ({f._floor})");
            }
            catch
            {
                // unreachable or unreadable: reports go out as before
            }
        });
        return f;
    }

    /// <summary>The floor for <paramref name="modId"/> in reporting.json's text; null when it isn't there.</summary>
    public static Version? Parse(string json, string modId)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("oldestReported", out var all) || !all.TryGetProperty(modId, out var v)) return null;
            return Version.TryParse(v.GetString(), out var floor) ? floor : null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>reporting.json's gameplayMods: mod ids (empty when it has none).</summary>
    public static IReadOnlyCollection<string> ParseGameplayMods(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("gameplayMods", out var list) || list.ValueKind != JsonValueKind.Array) return Array.Empty<string>();
            return list.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.String).Select(x => x.GetString()!).ToList();
        }
        catch
        {
            return Array.Empty<string>();
        }
    }

    /// <summary>Why nothing is sent (ErrorReporter.SkipReason), or null when this version is at or above the floor (or it's unknown).</summary>
    public string? SkipReason() =>
        _floor is { } f && Version.TryParse(f, out var floor) && _current < floor
            ? $"this version is older than the oldest one reports are taken from ({f})"
            : null;
}
