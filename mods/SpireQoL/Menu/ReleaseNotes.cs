using System.Text.Json;
using System.Text.RegularExpressions;

namespace SpireQoL.Menu;

/// <summary>One group of a release's notes (Added, Changed, Fixed...; null when the notes have no groups).</summary>
internal sealed record ReleaseSection(string? Kind, IReadOnlyList<string> Items);

/// <summary>One release: its version, the title some early ones carry, the date it was tagged (null when unknown).</summary>
internal sealed record Release(string Version, string? Title, string? Date, IReadOnlyList<ReleaseSection> Sections);

/// <summary>
/// The About page's content, read from files built into the mod (decision #68), so nothing is kept by hand: the patch notes
/// from CHANGELOG.md, the philosophy from the README's "Philosophy" section, the author from the manifest, and release
/// dates from the release tags at build time (SpireQoL.csproj). Parsing is plain C#, checked by the catalog tests.
/// </summary>
internal static class ReleaseNotes
{
    public const string ModName = "Spire QoL";

    private static IReadOnlyList<Release>? _releases;
    private static IReadOnlyList<string>? _philosophy;
    private static string? _author;

    /// <summary>Every Spire QoL release, newest first.</summary>
    public static IReadOnlyList<Release> Releases =>
        _releases ??= Parse(Resource("CHANGELOG.md") ?? "", ModName, Dates(Resource("release-dates.txt") ?? ""));

    /// <summary>The README's Philosophy section, one entry per point.</summary>
    public static IReadOnlyList<string> Philosophy => _philosophy ??= Section(Resource("README.md") ?? "", "Philosophy");

    /// <summary>The manifest's author.</summary>
    public static string Author => _author ??= ManifestAuthor(Resource("manifest.json") ?? "") ?? "";

    public static string? Resource(string name)
    {
        using var stream = typeof(ReleaseNotes).Assembly.GetManifestResourceStream("SpireQoL." + name);
        if (stream == null) return null;
        using var streamReader = new StreamReader(stream);
        return streamReader.ReadToEnd();
    }

    private static readonly Regex Heading = new(@"^(\d+\.\d+\.\d+)\s*[:\-–]?\s*(.*)$");

    /// <summary>The releases of <paramref name="mod"/> in a Keep a Changelog file ("### Spire QoL 1.2.3", "#### Added", "- item").</summary>
    public static IReadOnlyList<Release> Parse(string changelog, string mod, IReadOnlyDictionary<string, string>? dates = null)
    {
        var list = new List<Release>();
        var prefix = "### " + mod + " ";
        string? version = null, title = null, kind = null;
        var sections = new List<ReleaseSection>();
        List<string>? items = null;

        void FlushSection()
        {
            if (items is { Count: > 0 }) sections.Add(new ReleaseSection(kind, items));
            items = null;
        }
        void FlushRelease()
        {
            FlushSection();
            if (version != null) list.Add(new Release(version, title, dates?.GetValueOrDefault(version), sections));
            version = title = kind = null;
            sections = new List<ReleaseSection>();
        }

        foreach (var raw in changelog.Split('\n'))
        {
            var line = raw.TrimEnd('\r');
            if (line.StartsWith("## ") || line.StartsWith("### "))
            {
                FlushRelease();
                if (line.StartsWith(prefix) && Heading.Match(line[prefix.Length..].Trim()) is { Success: true } match)
                {
                    version = match.Groups[1].Value;
                    var heading = match.Groups[2].Value.Trim().TrimStart('(').TrimEnd(')').Trim();
                    title = heading.Length == 0 ? null : Plain(heading);
                }
                continue;
            }
            if (version == null) continue;
            if (line.StartsWith("#### "))
            {
                FlushSection();
                kind = Plain(line[5..].Trim());
                continue;
            }
            var text = line.TrimStart();
            if (text.Length == 0) continue;
            if (text.StartsWith("- ") || text.StartsWith("* "))
            {
                items ??= new List<string>();
                var nested = line.Length - text.Length >= 2;
                items.Add((nested ? "  ◦ " : "") + Plain(text[2..]));
            }
            else if (items is { Count: > 0 }) items[^1] += " " + Plain(text);
            else (items ??= new List<string>()).Add(Plain(text));
        }
        FlushRelease();
        return list;
    }

    /// <summary>"spireqol-v1.2.3 2026-11-16" lines (git for-each-ref at build time) → version → date.</summary>
    public static IReadOnlyDictionary<string, string> Dates(string lines)
    {
        var dates = new Dictionary<string, string>();
        foreach (var raw in lines.Split('\n'))
        {
            var parts = raw.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2) continue;
            var tag = parts[0];
            var versionAt = tag.LastIndexOf("-v", StringComparison.Ordinal);
            if (versionAt < 0) continue;
            dates[tag[(versionAt + 2)..]] = parts[1];
        }
        return dates;
    }

    /// <summary>The points of a "## <paramref name="name"/>" section of a markdown file: each bullet (or paragraph) as plain text.</summary>
    public static IReadOnlyList<string> Section(string markdown, string name)
    {
        var points = new List<string>();
        var inside = false;
        var blank = true;
        foreach (var raw in markdown.Split('\n'))
        {
            var line = raw.TrimEnd('\r');
            if (line.StartsWith("## "))
            {
                inside = line[3..].Trim().Equals(name, StringComparison.OrdinalIgnoreCase);
                continue;
            }
            if (!inside || line.StartsWith("#")) continue;
            var text = line.Trim();
            if (text.Length == 0)
            {
                blank = true;
                continue;
            }
            if (text.StartsWith("- ") || text.StartsWith("* ")) points.Add(Plain(text[2..]));
            else if (!blank && points.Count > 0) points[^1] += " " + Plain(text);
            else points.Add(Plain(text));
            blank = false;
        }
        return points;
    }

    private static string? ManifestAuthor(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.TryGetProperty("author", out var jsonElement) ? jsonElement.GetString() : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static readonly Regex Link = new(@"\[([^\]]+)\]\([^)]*\)");

    /// <summary>Markdown to plain text: links to their text, no bold, italics or code marks.</summary>
    public static string Plain(string markdown)
    {
        var text = Link.Replace(markdown, "$1");
        text = text.Replace("**", "").Replace("__", "").Replace("`", "");
        return text.Trim();
    }
}
