using Godot;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Screens.RunHistoryScreen;

namespace SpireQoL.Cards;

/// <summary>
/// A card as an icon (decision #42): the game's tiny card from the run history's deck list (NTinyCard: a card frame in its
/// character's color, the portrait shape of its type, the banner in its rarity's color). The game builds it only inside a
/// deck entry (NDeckHistoryEntry.Create), so this holds a whole entry with its title and enchantment hidden, scaled so the
/// tiny card fills a square of <c>size</c>. Nothing reacts to the mouse.
/// </summary>
internal sealed class TinyCardIcon
{
    public Control Root { get; }
    private readonly NDeckHistoryEntry? _entry;
    private readonly float _size;
    private static bool _logged;

    public TinyCardIcon(CardModel card, float size)
    {
        _size = size;
        Root = new Control { CustomMinimumSize = new Vector2(size, size), Size = new Vector2(size, size), MouseFilter = Control.MouseFilterEnum.Ignore, ClipContents = false };
        try
        {
            _entry = NDeckHistoryEntry.Create(card, 1);
            Root.AddChild(_entry);
            Quiet(_entry);
        }
        catch (Exception ex)
        {
            ModEntry.Log.Warn($"tiny card for {card.GetType().Name} unavailable: {ex.Message}");
            _entry = null;
        }
    }

    /// <summary>Fits the tiny card in the square (its layout settles a frame after it enters the tree).</summary>
    public void Fit()
    {
        if (_entry == null || !GodotObject.IsInstanceValid(_entry)) return;
        if (_entry.GetNodeOrNull<Control>("%Label") is { } label && label.GetParent() is Control labelBox) labelBox.Visible = false;
        if (_entry.GetNodeOrNull<Control>("%Enchantment") is { } ench) ench.Visible = false;
        if (_entry.GetNodeOrNull<Control>("%Card") is not { } tiny || tiny.Size.X < 1 || tiny.Size.Y < 1) return;
        // Centre what the tiny card draws, not its layout box: its art needn't fill the box or sit in its middle.
        // In the entry's own unscaled units (the tiny card can be nested).
        var toEntry = _entry.GetGlobalTransform().AffineInverse();
        Rect2? drawn = null;
        Union(tiny, toEntry, ref drawn);
        var layout = new Rect2((toEntry * tiny.GetGlobalTransform()).Origin, tiny.Size);
        var box = drawn ?? layout;
        if (!_logged)
        {
            _logged = true;
            ModEntry.Log.Info($"tiny card: layout box {layout}, drawn {box}");
        }
        if (box.Size.X < 1 || box.Size.Y < 1) return;
        var scale = _size / Math.Max(box.Size.X, box.Size.Y);
        _entry.Scale = new Vector2(scale, scale);
        var shown = box.Size * scale;
        _entry.Position = new Vector2((_size - shown.X) / 2f, (_size - shown.Y) / 2f) - box.Position * scale;
        Quiet(_entry);
    }

    /// <summary>The area the visible textures under <paramref name="node"/> draw, in the entry's units.</summary>
    private static void Union(Node node, Transform2D toEntry, ref Rect2? drawn)
    {
        if (node is CanvasItem { Visible: false }) return;
        // A drop shadow is offset from what casts it: centring on it would pull the card off centre.
        if (node.Name.ToString().Contains("Shadow")) return;
        if (node is TextureRect { Texture: { } tex } tr && tr.Size.X >= 1 && tr.Size.Y >= 1)
        {
            var local = DrawnRect(tr, tex.GetSize());
            var t = toEntry * tr.GetGlobalTransform();
            var a = t * local.Position;
            var b = t * local.End;
            var r = new Rect2(new Vector2(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y)), (b - a).Abs());
            drawn = drawn is { } d ? d.Merge(r) : r;
        }
        foreach (var child in node.GetChildren()) Union(child, toEntry, ref drawn);
    }

    /// <summary>Where a TextureRect draws its texture inside its own rect (Godot's stretch modes).</summary>
    private static Rect2 DrawnRect(TextureRect tr, Vector2 tex)
    {
        var size = tr.Size;
        if (tex.X < 1 || tex.Y < 1) return new Rect2(Vector2.Zero, size);
        var fit = Math.Min(size.X / tex.X, size.Y / tex.Y);
        return tr.StretchMode switch
        {
            TextureRect.StretchModeEnum.Keep => new Rect2(Vector2.Zero, tex),
            TextureRect.StretchModeEnum.KeepCentered => new Rect2((size - tex) / 2f, tex),
            TextureRect.StretchModeEnum.KeepAspect => new Rect2(Vector2.Zero, tex * fit),
            TextureRect.StretchModeEnum.KeepAspectCentered => new Rect2((size - tex * fit) / 2f, tex * fit),
            _ => new Rect2(Vector2.Zero, size), // Scale, Tile, KeepAspectCovered (clipped to the rect)
        };
    }

    private static void Quiet(Node node)
    {
        if (node is Control c)
        {
            c.MouseFilter = Control.MouseFilterEnum.Ignore;
            c.FocusMode = Control.FocusModeEnum.None;
        }
        foreach (var child in node.GetChildren()) Quiet(child);
    }
}
