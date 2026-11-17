namespace SpireQoLCapture;

/// <summary>One scene file: a run set up line by line, ending in one or more screenshots.</summary>
public sealed class Scene
{
    public required string Name { get; init; }
    public required string Path { get; init; }
    public string Title { get; set; } = "";
    public string Section { get; set; } = "";
    public string Character { get; set; } = "Ironclad";
    public string? Seed { get; set; }
    public bool NoRun { get; set; }
    public List<Line> Lines { get; } = new();

    public sealed record Line(int Number, string Text, string[] Tokens)
    {
        public string Verb => Tokens.Length > 0 ? Tokens[0].ToLowerInvariant() : "";
        public string Rest => Text.Length > Tokens[0].Length ? Text[Tokens[0].Length..].Trim() : "";
    }

    /// <summary>
    /// Header lines (title, section, character, seed, no-run) may appear anywhere; everything else runs in order.
    /// Blank lines and lines starting with # are skipped. Tokens split on spaces; "double quotes" keep spaces.
    /// </summary>
    public static Scene Parse(string path)
    {
        var scene = new Scene { Name = System.IO.Path.GetFileNameWithoutExtension(path), Path = path };
        var n = 0;
        foreach (var raw in File.ReadAllLines(path))
        {
            n++;
            var text = raw.Trim();
            if (text.Length == 0 || text.StartsWith('#')) continue;
            var tokens = Tokenize(text);
            var line = new Line(n, text, tokens);
            switch (line.Verb)
            {
                case "title": scene.Title = line.Rest; break;
                case "section": scene.Section = line.Rest; break;
                case "character": scene.Character = line.Rest; break;
                case "seed": scene.Seed = line.Rest; break;
                case "no-run": scene.NoRun = true; break;
                default: scene.Lines.Add(line); break;
            }
        }
        return scene;
    }

    public static string[] Tokenize(string text)
    {
        var list = new List<string>();
        var current = new System.Text.StringBuilder();
        var quoted = false;
        foreach (var c in text)
        {
            if (c == '"') { quoted = !quoted; continue; }
            if (c == ' ' && !quoted)
            {
                if (current.Length > 0) { list.Add(current.ToString()); current.Clear(); }
                continue;
            }
            current.Append(c);
        }
        if (current.Length > 0) list.Add(current.ToString());
        return list.ToArray();
    }
}
