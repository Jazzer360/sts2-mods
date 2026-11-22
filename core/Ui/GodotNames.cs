using Godot;

namespace Sts2Mods.Core.Ui;

/// <summary>
/// Godot's names, each made once (decision #173). A string passed where Godot takes a StringName (a theme item: "font_color") or a
/// NodePath ("%HealthBar") is converted on every call, a new object each time: in code that runs every frame, that is garbage every
/// frame. Everything here is kept for the launch; the game's thread only.
/// </summary>
public static class GodotNames
{
    private static readonly Dictionary<string, StringName> Names = new();
    private static readonly Dictionary<string, NodePath> Paths = new();

    /// <summary>The StringName for a theme item, signal or method name.</summary>
    public static StringName Name(string name)
    {
        if (!Names.TryGetValue(name, out var stringName)) Names[name] = stringName = new StringName(name);
        return stringName;
    }

    /// <summary>The NodePath for a node's path ("%HealthBar", "Icon").</summary>
    public static NodePath Path(string path)
    {
        if (!Paths.TryGetValue(path, out var nodePath)) Paths[path] = nodePath = new NodePath(path);
        return nodePath;
    }
}
