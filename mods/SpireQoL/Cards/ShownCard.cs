using Godot;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Cards;
using Sts2Mods.Core.Ui;

namespace SpireQoL.Cards;

/// <summary>
/// One of the game's cards shown in our own screens (the settings menu's Guide page, the Inspect screen): the game's card
/// scene with this model, not a pooled hand card, with tabs drawn by the hand's renderer. It's laid out once it's ready: the
/// card frame sits at the bottom of the box, with room above it for the tabs. Call <see cref="Tick"/> every frame.
/// </summary>
internal sealed class ShownCard
{
    private const float TabRoom = 70; // above the card, in card units: the tabs stick up about this far

    public Control Root { get; }
    private readonly NCard? _card;
    private readonly CardDecor _decor = new();
    private readonly float _scale;
    private CardOverlayRenderer.Example? _example;
    private bool _visuals;
    private bool _failed;
    private CardModel? _fallback;
    /// <summary>Frames in a row it hasn't moved or changed; at SettleFrames it stops redrawing until its tabs change.</summary>
    private int _still;
    private int _lastKey;
    private bool _settled;

    /// <summary>
    /// Long enough for the faded copy to draw (FlatFade: a few frames) and the layout to settle. A shown card doesn't change once
    /// drawn, and redrawing every frame (about 17 on the Guide page) made garbage the game collected in 30 ms stalls.
    /// </summary>
    private const int SettleFrames = 30;

    /// <summary>Show the cost as a free card makes it in your hand: the game's green 0 (NCard.UpdateEnergyCostColor).</summary>
    public bool FreeCost { get; set; }

    /// <summary>The tabs drawn on it (change them, and the next tick draws the new ones).</summary>
    public CardDecor Decor => _decor;

    /// <summary>Where the card's frame is drawn in <see cref="Root"/> (null until it's laid out): for a mark around the card itself.</summary>
    public Rect2? CardRect { get; private set; }

    /// <summary>The card node, once made (null when the card scene couldn't be made).</summary>
    public NCard? Node => _card;

    public ShownCard(CardModel model, float scale, float width, IEnumerable<TabSpec> tabs, float? opacity = null)
    {
        _scale = scale;
        var size = NCard.defaultSize * scale;
        Root = new Control
        {
            CustomMinimumSize = new Vector2(width, size.Y + TabRoom * scale),
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        foreach (var tab in tabs) _decor.AddTab(tab);
        _decor.Opacity = opacity;
        try
        {
            var scene = ResourceLoader.Load<PackedScene>("res://scenes/cards/card.tscn");
            _card = scene.Instantiate<NCard>();
            _card.Model = model;
            if (model.IsMutable) _fallback = ModelDb.GetById<CardModel>(model.Id);
            _card.Scale = new Vector2(scale, scale);
            Root.AddChild(_card);
        }
        catch (Exception ex)
        {
            ModEntry.Log.Warn($"shown card: {model.GetType().Name} unavailable: {ex.Message}");
            _card = null;
        }
    }

    public void Tick()
    {
        if (_failed || _card == null || !GodotObject.IsInstanceValid(_card) || !_card.IsNodeReady()) return;
        var key = DecorKey();
        if (_settled && key == _lastKey) return;
        _settled = false;
        try
        {
            if (!_visuals)
            {
                _visuals = true;
                _card.UpdateVisuals(PileType.None, CardPreviewMode.Normal);
                if (FreeCost && _card.GetNodeOrNull<Label>(GodotNames.Path("%EnergyLabel")) is { } cost)
                {
                    cost.Text = "0";
                    cost.AddThemeColorOverride(GodotNames.Name("font_color"), StsColors.green);
                    cost.AddThemeColorOverride(GodotNames.Name("font_outline_color"), StsColors.energyGreenOutline);
                }
                Quiet(_card);
            }
            var moved = Place();
            _example ??= new CardOverlayRenderer.Example(_card);
            _example.Draw(_decor);
            _still = moved || key != _lastKey ? 0 : _still + 1;
            _lastKey = key;
            _settled = _still >= SettleFrames;
        }
        catch (Exception ex)
        {
            // A copy with keywords that won't draw outside a fight: fall back to the plain card.
            if (_fallback != null)
            {
                _card.Model = _fallback;
                _fallback = null;
                _visuals = false;
                return;
            }
            _failed = true; // once: not every frame
            ModEntry.Log.Warn($"shown card: {ex.Message}");
            _card.Visible = false;
        }
    }

    /// <summary>What's drawn on it: its tabs and fade (no allocation: it runs every frame).</summary>
    private int DecorKey()
    {
        var hashCode = new HashCode();
        foreach (var tab in _decor.Tabs) hashCode.Add(tab);
        hashCode.Add(_decor.Opacity);
        hashCode.Add(FreeCost);
        return hashCode.ToHashCode();
    }

    /// <summary>Moves the card so its frame sits centred at the bottom of the box (wherever the scene puts its origin). True if it moved.</summary>
    private bool Place()
    {
        if (_card!.GetNodeOrNull<Control>(GodotNames.Path("%Frame")) is not { } frame || frame.Size.X < 1) return true;
        var rel = Root.GetGlobalTransform().AffineInverse() * frame.GetGlobalTransform();
        var shown = frame.Size * _scale;
        var want = new Vector2((Root.Size.X - shown.X) / 2f, Root.Size.Y - shown.Y);
        CardRect = new Rect2(want, shown);
        var delta = want - rel.Origin;
        if (delta.LengthSquared() <= 0.25f) return false;
        _card.Position += delta;
        return true;
    }

    private static void Quiet(Node node)
    {
        if (node is Control control)
        {
            control.MouseFilter = Control.MouseFilterEnum.Ignore;
            control.FocusMode = Control.FocusModeEnum.None;
        }
        foreach (var child in node.GetChildren()) Quiet(child);
    }
}
