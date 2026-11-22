using Godot;

namespace Sts2Mods.Core.Ui;

/// <summary>Small factory helpers for plain Godot controls. Everything is click-through.</summary>
public static class UiKit
{
    /// <summary>
    /// Takes a node out of its parent now and frees it at the end of the frame. QueueFree alone leaves it in the tree until then, so a
    /// node added in its place with the same name is renamed by Godot, and code finding it by name loses track of it (a stale row left on
    /// the map legend).
    /// </summary>
    public static void RemoveAndFree(this Node? node)
    {
        if (node == null || !GodotObject.IsInstanceValid(node)) return;
        node.GetParent()?.RemoveChild(node);
        node.QueueFree();
    }

    public static Label Label(string text, int size, Color color)
    {
        var label = new Label { Text = text, MouseFilter = Control.MouseFilterEnum.Ignore };
        label.AddThemeFontSizeOverride("font_size", size);
        label.AddThemeColorOverride("font_color", color);
        label.AddThemeColorOverride("font_outline_color", new Color(0, 0, 0, 0.9f));
        label.AddThemeConstantOverride("outline_size", Math.Max(2, size / 4));
        return label;
    }

    public static StyleBoxFlat PanelStyle(Color color, int radius = 6, int margin = 6)
    {
        var styleBoxFlat = new StyleBoxFlat { BgColor = color };
        styleBoxFlat.SetCornerRadiusAll(radius);
        styleBoxFlat.ContentMarginLeft = margin;
        styleBoxFlat.ContentMarginRight = margin;
        styleBoxFlat.ContentMarginTop = margin / 2f;
        styleBoxFlat.ContentMarginBottom = margin / 2f;
        return styleBoxFlat;
    }

    public static HBoxContainer Row(int separation = 6)
    {
        var hBoxContainer = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        hBoxContainer.AddThemeConstantOverride("separation", separation);
        return hBoxContainer;
    }

    public static TextureRect Icon(Texture2D? tex, float size)
    {
        return new TextureRect
        {
            Texture = tex,
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            CustomMinimumSize = new Vector2(size, size),
            Size = new Vector2(size, size),
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
    }

    /// <summary>Remove and free all children of a node.</summary>
    public static void Clear(Node node)
    {
        foreach (var child in node.GetChildren())
        {
            node.RemoveChild(child);
            child.QueueFree();
        }
    }

    /// <summary>0..1 triangle-ish pulse for flashing effects.</summary>
    public static float Pulse(float time, float frequency) => 0.5f + 0.5f * Mathf.Sin(time * frequency * Mathf.Tau);
}
