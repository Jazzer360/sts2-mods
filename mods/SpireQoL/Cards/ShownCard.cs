using Godot;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Cards;

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

    /// <summary>Show the cost as a free card makes it in your hand: the game's green 0 (NCard.UpdateEnergyCostColor).</summary>
    public bool FreeCost { get; set; }

    /// <summary>The tabs drawn on it (change them, and the next tick draws the new ones).</summary>
    public CardDecor Decor => _decor;

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
        foreach (var t in tabs) _decor.AddTab(t);
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
        try
        {
            if (!_visuals)
            {
                _visuals = true;
                _card.UpdateVisuals(PileType.None, CardPreviewMode.Normal);
                if (FreeCost && _card.GetNodeOrNull<Label>("%EnergyLabel") is { } cost)
                {
                    cost.Text = "0";
                    cost.AddThemeColorOverride("font_color", StsColors.green);
                    cost.AddThemeColorOverride("font_outline_color", StsColors.energyGreenOutline);
                }
                Quiet(_card);
            }
            Place();
            _example ??= new CardOverlayRenderer.Example(_card);
            _example.Draw(_decor);
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

    /// <summary>Moves the card so its frame sits centred at the bottom of the box (wherever the scene puts its origin).</summary>
    private void Place()
    {
        if (_card!.GetNodeOrNull<Control>("%Frame") is not { } frame || frame.Size.X < 1) return;
        var rel = Root.GetGlobalTransform().AffineInverse() * frame.GetGlobalTransform();
        var shown = frame.Size * _scale;
        var want = new Vector2((Root.Size.X - shown.X) / 2f, Root.Size.Y - shown.Y);
        var delta = want - rel.Origin;
        if (delta.LengthSquared() > 0.25f) _card.Position += delta;
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
