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
        // Where the tiny card sits inside the entry (it can be nested), in the entry's own unscaled units.
        var offset = (_entry.GetGlobalTransform().AffineInverse() * tiny.GetGlobalTransform()).Origin;
        var scale = _size / Math.Max(tiny.Size.X, tiny.Size.Y);
        _entry.Scale = new Vector2(scale, scale);
        // The tiny card's box, centered in the square.
        var box = tiny.Size * scale;
        _entry.Position = new Vector2((_size - box.X) / 2f, (_size - box.Y) / 2f) - offset * scale;
        Quiet(_entry);
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
