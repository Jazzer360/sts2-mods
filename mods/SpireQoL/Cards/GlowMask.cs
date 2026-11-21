using Godot;

namespace SpireQoL.Cards;

/// <summary>
/// The card's glow (NCardHighlight: a TextureRect drawn behind the card through its own shader) left out where the card covers it,
/// while the card is a see-through Ethereal picture (decision #127): otherwise the glow shows through the card's frame and doubles
/// its border. The glow gets a copy of its shader with a few lines added: the flattened card image (FlatFade) as a mask, mapped
/// from the glow's own pixels to the image's, and the glow's alpha cut where the card's alpha is. The game animates the glow by
/// setting its shader's values on its own material (NCardHighlight._shaderMaterial, kept from _Ready), so those values are copied
/// onto the copy every frame. The original material goes back on <see cref="Dispose"/>. One texture read per glow pixel; the copy
/// is made once per card.
/// </summary>
internal sealed class GlowMask
{
    private const string Mask = "spireqol_mask", Row0 = "spireqol_row0", Row1 = "spireqol_row1", Origin = "spireqol_origin", Size = "spireqol_size";

    private static readonly Dictionary<ulong, Shader?> Masked = new();
    private static readonly HashSet<ulong> Warned = new();

    private readonly CanvasItem _glow;
    private readonly Material? _original;
    private readonly ShaderMaterial _copy;
    private readonly List<StringName> _params = new();

    private GlowMask(CanvasItem glow, ShaderMaterial original, Shader masked)
    {
        _glow = glow;
        _original = glow.Material;
        _copy = new ShaderMaterial { Shader = masked };
        foreach (var variant in original.Shader.GetShaderUniformList())
            if (variant.AsGodotDictionary()["name"].AsString() is { Length: > 0 } name) _params.Add(name);
        glow.Material = _copy;
    }

    /// <summary>A mask for this glow, or null when its shader can't take one (then the glow is left as it is).</summary>
    public static GlowMask? Create(CanvasItem? glow)
    {
        if (glow == null || !GodotObject.IsInstanceValid(glow) || glow.Material is not ShaderMaterial { Shader: { } shader } shaderMaterial) return null;
        var id = shader.GetInstanceId();
        if (!Masked.TryGetValue(id, out var masked))
        {
            masked = MaskedCopy(shader);
            Masked[id] = masked;
        }
        return masked == null ? null : new GlowMask(glow, shaderMaterial, masked);
    }

    /// <summary>
    /// The glow's shader with the mask added: uniforms and the glow's local position (VERTEX, set in vertex()), and at the end of
    /// fragment() its alpha cut by the card image's alpha where the image is. Null when the code isn't the shape this expects.
    /// </summary>
    internal static Shader? MaskedCopy(Shader shader)
    {
        var code = MaskedCode(shader.Code);
        if (code == null)
        {
            if (Warned.Add(shader.GetInstanceId())) ModEntry.Log.Warn("Ethereal: the card glow's shader isn't one the mask can extend; the glow stays as it is");
            return null;
        }
        return new Shader { Code = code };
    }

    /// <summary>The code with the mask added (pure, for the tests), or null.</summary>
    internal static string? MaskedCode(string code)
    {
        if (string.IsNullOrEmpty(code) || !code.Contains("canvas_item")) return null;
        var typeEnd = code.IndexOf(';', code.IndexOf("shader_type", StringComparison.Ordinal) is var t and >= 0 ? t : 0);
        if (typeEnd < 0) return null;
        var decls = $"\nuniform sampler2D {Mask} : filter_linear;\nuniform vec2 {Row0} = vec2(1.0, 0.0);\nuniform vec2 {Row1} = vec2(0.0, 1.0);\n" +
                    $"uniform vec2 {Origin} = vec2(0.0);\nuniform vec2 {Size} = vec2(1.0);\nvarying vec2 spireqol_local;\n";
        // After shader_type and any render_mode line.
        var insertAt = typeEnd + 1;
        var render = code.IndexOf("render_mode", insertAt, StringComparison.Ordinal);
        if (render >= 0 && code.IndexOf(';', render) is var r and >= 0 && code.IndexOf('{', insertAt) is var firstBrace && (firstBrace < 0 || render < firstBrace)) insertAt = r + 1;
        code = code.Insert(insertAt, decls);

        const string grab = "\n\tspireqol_local = VERTEX;\n";
        var vertex = BodyStart(code, "vertex");
        code = vertex >= 0 ? code.Insert(vertex, grab) : code + "\nvoid vertex() {" + grab + "}\n";

        var fragment = BodyStart(code, "fragment");
        if (fragment < 0) return null;
        var end = BodyEnd(code, fragment);
        if (end < 0) return null;
        var cut = $"\n\t{{\n\t\tvec2 spireqol_uv = ({Row0} * spireqol_local.x + {Row1} * spireqol_local.y + {Origin}) / {Size};\n" +
                  "\t\tif (spireqol_uv.x >= 0.0 && spireqol_uv.x <= 1.0 && spireqol_uv.y >= 0.0 && spireqol_uv.y <= 1.0)\n" +
                  $"\t\t\tCOLOR.a *= 1.0 - smoothstep(0.5, 0.9, texture({Mask}, spireqol_uv).a);\n\t}}\n";
        return code.Insert(end, cut);
    }

    /// <summary>Just after the opening brace of `void name(`, or -1.</summary>
    private static int BodyStart(string code, string name)
    {
        var at = System.Text.RegularExpressions.Regex.Match(code, @"void\s+" + name + @"\s*\(\s*\)\s*\{");
        return at.Success ? at.Index + at.Length : -1;
    }

    /// <summary>The index of the closing brace of the body starting at <paramref name="start"/>, or -1. Comments and strings aside, GLSL bodies nest only braces.</summary>
    private static int BodyEnd(string code, int start)
    {
        var depth = 1;
        for (var i = start; i < code.Length; i++)
        {
            if (code[i] == '/' && i + 1 < code.Length && code[i + 1] == '/') { i = code.IndexOf('\n', i); if (i < 0) return -1; continue; }
            if (code[i] == '/' && i + 1 < code.Length && code[i + 1] == '*') { i = code.IndexOf("*/", i + 2, StringComparison.Ordinal); if (i < 0) return -1; i++; continue; }
            if (code[i] == '{') depth++;
            else if (code[i] == '}' && --depth == 0) return i;
        }
        return -1;
    }

    /// <summary>Every frame: the game's glow values onto the copy, and where the card image (<paramref name="image"/>) is, from the glow's pixels.</summary>
    public void Update(Control image, Texture2D texture)
    {
        if (!GodotObject.IsInstanceValid(_glow)) return;
        if (_glow.Material != _copy) _glow.Material = _copy; // the game put its own back (a pooled node reset)
        if (_original is ShaderMaterial shaderMaterial)
            foreach (var stringName in _params) _copy.SetShaderParameter(stringName, shaderMaterial.GetShaderParameter(stringName));
        // A point in the glow's own coordinates to the image's (both CanvasItems: their global transforms).
        var toImage = image.GetGlobalTransform().AffineInverse() * _glow.GetGlobalTransform();
        _copy.SetShaderParameter(Mask, texture);
        _copy.SetShaderParameter(Row0, toImage.X);
        _copy.SetShaderParameter(Row1, toImage.Y);
        _copy.SetShaderParameter(Origin, toImage.Origin);
        _copy.SetShaderParameter(Size, image.Size);
    }

    public void Dispose()
    {
        if (GodotObject.IsInstanceValid(_glow) && _glow.Material == _copy) _glow.Material = _original;
    }
}
