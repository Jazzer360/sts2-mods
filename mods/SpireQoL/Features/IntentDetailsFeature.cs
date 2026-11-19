using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.MonsterMoves.Intents;
using MegaCrit.Sts2.Core.MonsterMoves.MonsterMoveStateMachine;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using SpireQoL.Cards;
using SpireQoL.Sim;
using Sts2Mods.Core.Features;
using Sts2Mods.Core.Game;
using Sts2Mods.Core.Ui;

namespace SpireQoL.Features;

/// <summary>
/// Intent Details (decision #98): on each enemy's intent on screen, during your turn, what its move does that the intent doesn't
/// say: the block it gains, as a number on its defend intent (like the damage on an attack), and each power it applies (icon and
/// amount: Strength 3, Weak 2) just right of its intents, read from the move's code as Intent Forecast reads later moves
/// (MovePowers). The game already shows a status intent's card count. Nothing while the intent is hidden. Checked against what the
/// move did (ForecastAudit). Spec: docs/FEATURES.md#intent-details.
/// </summary>
public sealed class IntentDetailsFeature : IFeature, IFrameFeature
{
    private const string BlockName = "SpireQoLIntentBlock";
    private const string PowersName = "SpireQoLIntentPowers";
    internal static readonly Color BlockColor = new(0.55f, 0.8f, 1f);

    public IntentDetailsFeature(QolContext ctx) { }

    public string Id => "intent-details";
    public string DisplayName => "Intent Details";
    public bool EnabledByDefault => true;

    public void Apply(Harmony harmony) { }

    public void Attach(OverlayHost host) { }

    /// <summary>What an enemy's current move shows: its block (null: none, or not a number we can read) and the powers it applies.</summary>
    internal sealed record Shown(string MoveId, int? Block, IReadOnlyList<MovePowers.Applied> Powers);

    /// <summary>What the enemy's intent on screen gets (null when its intent is hidden or it has no move).</summary>
    internal static Shown? For(Creature enemy)
    {
        if (enemy is not { IsEnemy: true, IsAlive: true } || enemy.Monster is not { } monster || monster.NextMove is not { } move) return null;
        if (move.Intents.Any(i => i is HiddenIntent or UnknownIntent)) return null;
        var block = move.Intents.Any(i => i is DefendIntent) ? MovePowers.BlockOf(monster, move) : null;
        return new Shown(move.Id, block, MovePowers.Of(monster, move));
    }

    public void OnFrame(OverlayHost host)
    {
        if (NCombatRoom.Instance is not { } room) return;
        foreach (var node in room.CreatureNodes)
        {
            if (!GodotObject.IsInstanceValid(node) || node.IntentContainer is not { } container || !GodotObject.IsInstanceValid(container)) continue;
            var intents = container.GetChildren().OfType<NIntent>().Where(GodotObject.IsInstanceValid).ToList();
            // As the intent on screen says: nothing while it's hidden there (its move would be given away).
            var hidden = intents.Any(i => Sts2State.Private<AbstractIntent?>(i, "_intent", null) is HiddenIntent or UnknownIntent);
            var shown = Sts2State.IsPlayerTurn && !hidden && node.Entity is { } e ? For(e) : null;
            foreach (var intent in intents)
            {
                var holder = intent.GetNodeOrNull<Control>("%IntentHolder");
                if (holder == null) continue;
                var isDefend = Sts2State.Private<AbstractIntent?>(intent, "_intent", null) is DefendIntent;
                Block(holder, intent, isDefend && shown?.Block is > 0 ? shown.Block : null);
                Powers(holder, intent, intent == intents[^1] && shown is { Powers.Count: > 0 } ? shown.Powers : null);
            }
        }
    }

    /// <summary>The block number on a defend intent, where the game puts an attack's damage (its %Value label).</summary>
    private static void Block(Control holder, NIntent intent, int? amount)
    {
        var label = holder.GetNodeOrNull<Label>(BlockName);
        if (amount == null)
        {
            label?.QueueFree();
            return;
        }
        if (label == null)
        {
            label = UiKit.Label("", 22, BlockColor);
            label.Name = BlockName;
            label.MouseFilter = Control.MouseFilterEnum.Ignore;
            label.AddThemeConstantOverride("outline_size", 8);
            label.AddThemeColorOverride("font_outline_color", new Color(0, 0, 0, 0.95f));
            label.HorizontalAlignment = HorizontalAlignment.Right;
            label.VerticalAlignment = VerticalAlignment.Bottom;
            holder.AddChild(label);
        }
        var text = amount.Value.ToString();
        if (label.Text != text) label.Text = text;
        // Over the game's own value label (empty on a defend intent): bottom right of the icon, as an attack's damage.
        if (intent.GetNodeOrNull<Control>("%Value") is { } value)
        {
            label.Position = value.Position;
            label.Size = value.Size;
        }
        else
        {
            label.Position = new Vector2(0, holder.Size.Y - 28);
            label.Size = new Vector2(holder.Size.X, 28);
        }
    }

    /// <summary>The powers the move applies, just right of its last intent, level with its icon (in its holder: it bobs with it).</summary>
    private static void Powers(Control holder, NIntent intent, IReadOnlyList<MovePowers.Applied>? powers)
    {
        var row = holder.GetNodeOrNull<HBoxContainer>(PowersName);
        var key = powers == null ? "" : string.Join(",", powers.Select(p => $"{p.Power.Name}:{p.Amount}"));
        if (powers == null)
        {
            row?.QueueFree();
            return;
        }
        if (row != null && row.GetMeta("key", "").AsString() != key)
        {
            row.QueueFree();
            row = null;
        }
        if (row == null)
        {
            row = UiKit.Row(4);
            row.Name = PowersName;
            row.MouseFilter = Control.MouseFilterEnum.Ignore;
            row.SetMeta("key", key);
            foreach (var p in powers) row.AddChild(Chip(p));
            holder.AddChild(row);
        }
        var size = row.GetCombinedMinimumSize();
        row.Size = size;
        var centre = intent.GetNodeOrNull<Node2D>("%Intent") is { } sprite ? sprite.Position.Y : holder.Size.Y / 2;
        row.Position = new Vector2(holder.Size.X + 2, centre - size.Y / 2);
    }

    /// <summary>A power's icon with its amount beside it (the icon alone when the amount isn't a number the move keeps).</summary>
    private static Control Chip(MovePowers.Applied applied)
    {
        var chip = UiKit.Row(1);
        chip.MouseFilter = Control.MouseFilterEnum.Ignore;
        var icon = UiKit.Icon(GameIcons.Power(applied.Power), 30);
        icon.MouseFilter = Control.MouseFilterEnum.Ignore;
        chip.AddChild(icon);
        if (applied.Amount is { } a)
        {
            var amount = UiKit.Label(a.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture), 18, Colors.White);
            amount.AddThemeConstantOverride("outline_size", 6);
            amount.AddThemeColorOverride("font_outline_color", new Color(0, 0, 0, 0.9f));
            amount.VerticalAlignment = VerticalAlignment.Center;
            amount.MouseFilter = Control.MouseFilterEnum.Ignore;
            chip.AddChild(amount);
        }
        return chip;
    }
}
