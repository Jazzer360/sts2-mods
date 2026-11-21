using Godot;
using SpireQoL.Features;
using Sts2Mods.Core.Ui;

namespace SpireQoL.Menu;

/// <summary>
/// The About page (decision #68): the version and author, links to the project, the philosophy and every release's patch
/// notes, all read from files built into the mod (ReleaseNotes), none written here.
/// </summary>
public sealed partial class SettingsMenu
{
    private const int ReleasesPerPage = 8;
    private int _notesPage;

    private void BuildAbout()
    {
        // A development build shows which: "1.25.0-dev+abc1234" (BuildInfo.Label).
        var version = BuildInfo.Dev ? BuildInfo.Label : typeof(ModEntry).Assembly.GetName().Version?.ToString(3) ?? "?";
        Section($"{ReleaseNotes.ModName} {version}");
        var author = ReleaseNotes.Author;
        if (author.Length > 0) Paragraph($"Made by {author}.");
        var links = UiKit.Row(10);
        links.AddChild(LinkButton("Project on GitHub", $"https://github.com/{UpdateNoticeFeature.Repo}"));
        links.AddChild(LinkButton("All releases", UpdateNoticeFeature.ReleasesLink(null)));
        links.AddChild(LinkButton("Report a problem", $"https://github.com/{UpdateNoticeFeature.Repo}/issues"));
        _rows!.AddChild(Indented(links, bottom: 8));

        var philosophy = ReleaseNotes.Philosophy;
        if (philosophy.Count > 0)
        {
            Section("Philosophy");
            foreach (var point in philosophy) Paragraph(point);
        }

        BuildPatchNotes();
    }

    private void BuildPatchNotes()
    {
        var all = ReleaseNotes.Releases;
        Section("Patch notes");
        if (all.Count == 0)
        {
            Paragraph("No patch notes in this build.");
            return;
        }

        // A search shows every matching line across all releases, unpaged.
        var q = _search?.Text?.Trim();
        if (!string.IsNullOrEmpty(q))
        {
            var shown = 0;
            foreach (var r in all)
            {
                var sections = r.Sections
                    .Select(s => s with { Items = s.Items.Where(i => Matches(i, s.Kind, r.Version, r.Title)).ToList() })
                    .Where(s => s.Items.Count > 0).ToList();
                if (sections.Count == 0) continue;
                ReleaseBlock(r with { Sections = sections });
                shown++;
            }
            if (shown == 0) Paragraph("No patch notes match the search.");
            return;
        }

        var pages = (all.Count + ReleasesPerPage - 1) / ReleasesPerPage;
        _notesPage = Math.Clamp(_notesPage, 0, pages - 1);
        var pager = Pager(pages);
        _rows!.AddChild(Indented(pager, bottom: 4));
        foreach (var r in all.Skip(_notesPage * ReleasesPerPage).Take(ReleasesPerPage)) ReleaseBlock(r);
        if (pages > 1) _rows.AddChild(Indented(Pager(pages), bottom: 8));
    }

    private HBoxContainer Pager(int pages)
    {
        var row = UiKit.Row(10);
        var newer = MakeButton("◀ Newer", 18);
        newer.Disabled = _notesPage == 0;
        newer.Pressed += () => { _notesPage--; Rebuild(); };
        var older = MakeButton("Older ▶", 18);
        older.Disabled = _notesPage >= pages - 1;
        older.Pressed += () => { _notesPage++; Rebuild(); };
        row.AddChild(newer);
        row.AddChild(Label($"Page {_notesPage + 1} of {pages}", 18, Muted));
        row.AddChild(older);
        return row;
    }

    private void ReleaseBlock(Release r)
    {
        var current = typeof(ModEntry).Assembly.GetName().Version?.ToString(3);
        var when = r.Date ?? (r.Version == current ? "this version" : null);
        var head = UiKit.Row(12);
        head.AddChild(Label(r.Version, 22, Text));
        if (when != null) head.AddChild(Label(when, 16, Muted));
        if (r.Title != null) head.AddChild(Label(r.Title, 16, Muted));
        _rows!.AddChild(Indented(head, top: 10));
        foreach (var s in r.Sections)
        {
            if (s.Kind != null) _rows.AddChild(Indented(Label(s.Kind, 16, Accent), left: 20));
            foreach (var item in s.Items)
            {
                var nested = item.StartsWith("  ◦ ");
                var l = Label(nested ? item.Trim() : "• " + item, 17, Muted);
                l.AutowrapMode = TextServer.AutowrapMode.WordSmart;
                l.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
                _rows.AddChild(Indented(l, left: nested ? 56 : 32, bottom: 3));
            }
        }
    }

    private static Button LinkButton(string text, string url)
    {
        var b = MakeButton(text, 18);
        b.Pressed += () => OS.ShellOpen(url);
        return b;
    }

    private static MarginContainer Indented(Control c, int left = 8, int top = 0, int bottom = 0)
    {
        var m = new MarginContainer { MouseFilter = Control.MouseFilterEnum.Pass };
        m.AddThemeConstantOverride("margin_left", left);
        m.AddThemeConstantOverride("margin_right", 8);
        m.AddThemeConstantOverride("margin_top", top);
        m.AddThemeConstantOverride("margin_bottom", bottom);
        m.AddChild(c);
        return m;
    }
}
