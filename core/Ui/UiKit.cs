using Godot;

namespace Sts2Mods.Core.Ui;

/// <summary>Small factory helpers for plain Godot controls. Everything is click-through.</summary>
public static class UiKit
{
    public static Label Label(string text, int size, Color color)
    {
        var l = new Label { Text = text, MouseFilter = Control.MouseFilterEnum.Ignore };
        l.AddThemeFontSizeOverride("font_size", size);
        l.AddThemeColorOverride("font_color", color);
        l.AddThemeColorOverride("font_outline_color", new Color(0, 0, 0, 0.9f));
        l.AddThemeConstantOverride("outline_size", Math.Max(2, size / 4));
        return l;
    }

    public static StyleBoxFlat PanelStyle(Color bg, int radius = 6, int margin = 6)
    {
        var sb = new StyleBoxFlat { BgColor = bg };
        sb.SetCornerRadiusAll(radius);
        sb.ContentMarginLeft = margin;
        sb.ContentMarginRight = margin;
        sb.ContentMarginTop = margin / 2f;
        sb.ContentMarginBottom = margin / 2f;
        return sb;
    }

    public static HBoxContainer Row(int separation = 6)
    {
        var r = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        r.AddThemeConstantOverride("separation", separation);
        return r;
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
    public static float Pulse(float time, float hz) => 0.5f + 0.5f * Mathf.Sin(time * hz * Mathf.Tau);
}
