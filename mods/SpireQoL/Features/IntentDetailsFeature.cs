using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.MonsterMoves.Intents;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using SpireQoL.Combat;
using SpireQoL.Sim;
using Sts2Mods.Core.Features;
using Sts2Mods.Core.Game;
using Sts2Mods.Core.Ui;

namespace SpireQoL.Features;

/// <summary>
/// Intent Details (decision #98): on each enemy's intent on screen, during your turn, what its move does that the intent doesn't
/// say: the block it gains, as a number on its defend intent (like the damage on an attack), and each power it applies (icon and
/// amount: Strength 3, Weak 2) small in the corner of the buff or debuff intent it belongs to (IntentCorner), read from the move's code as Intent Forecast reads later moves
/// (MovePowers). The game already shows a status intent's card count. Nothing while the intent is hidden. Checked against what the
/// move did (ForecastAudit). Spec: docs/FEATURES.md#intent-details.
/// </summary>
public sealed class IntentDetailsFeature : IFeature, IFrameFeature
{
    private const string BlockName = "SpireQoLIntentBlock";

    /// <summary>Each enemy's intents as last drawn (the board probe, the turn, its intent nodes): drawn again only when that changes.</summary>
    private readonly Dictionary<NCreature, int> _drawnFor = new(ReferenceEqualityComparer.Instance);
    internal static readonly Color BlockColor = new(0.55f, 0.8f, 1f);

    public IntentDetailsFeature(QolContext ctx) { }

    public string Id => "intent-details";
    public string DisplayName => "Intent Details";
    public bool EnabledByDefault => true;

    public void Apply(Harmony harmony) { }

    public void Attach(OverlayHost host) { }

    /// <summary>
    /// What an enemy's current move shows: its block (null: none, or not a number we can read) and the powers it applies. ForecastBlock: its block
    /// as Intent Forecast shows the move on a later turn (the move's own amounts: decision #399), for the check.
    /// </summary>
    internal sealed record Shown(string MoveId, int? Block, IReadOnlyList<MovePowers.Applied> Powers, int? ForecastBlock = null);

    /// <summary>What the enemy's intent on screen gets (null when its intent is hidden or it has no move).</summary>
    internal static Shown? For(Creature enemy)
    {
        if (enemy is not { IsEnemy: true, IsAlive: true } || enemy.Monster is not { } monster || monster.NextMove is not { } move) return null;
        if (move.Intents.Any(abstractIntent => abstractIntent is HiddenIntent or UnknownIntent)) return null;
        var defends = move.Intents.Any(abstractIntent => abstractIntent is DefendIntent);
        var block = defends ? MovePowers.BlockOf(monster, move) : null;
        var forecastBlock = defends ? MovePowers.BlockOf(monster, move, now: false) : null;
        return new Shown(move.Id, block, MovePowers.Of(monster, move), forecastBlock is > 0 ? forecastBlock : null);
    }

    public void OnFrame(OverlayHost host)
    {
        if (NCombatRoom.Instance is not { } room) return;
        var playerTurn = Sts2State.IsPlayerTurn;
        var probe = Sts2State.Me is { } me && Sts2State.Combat is { } combat ? BoardProbe.Hash(me, combat) : 0;
        foreach (var node in Sts2State.CreatureNodesOf(room))
        {
            if (!GodotObject.IsInstanceValid(node) || node.IntentContainer is not { } container || !GodotObject.IsInstanceValid(container)) continue;
            // The same intent nodes on screen, the same board and turn as last frame: what's on them already stands.
            var drawnFor = new HashCode();
            drawnFor.Add(probe);
            drawnFor.Add(playerTurn);
            // Each intent node and the intent it holds: the game reuses its nodes for the next move (NIntent.UpdateIntent), so the same nodes can show
            // another move (the Seapunk's Bubble Burp, its block missing, drawn as the Spinning Kick before it).
            for (var i = 0; i < container.GetChildCount(); i++)
            {
                var child = container.GetChild(i);
                drawnFor.Add(child.GetInstanceId());
                if (child is NIntent shownIntent && Sts2State.Private<AbstractIntent?>(shownIntent, "_intent", fallback: null) is { } held)
                    drawnFor.Add(System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(held));
            }
            var drawnKey = drawnFor.ToHashCode();
            if (_drawnFor.TryGetValue(node, out var drawn) && drawn == drawnKey) continue;
            _drawnFor[node] = drawnKey;
            if (_drawnFor.Count > 64) _drawnFor.Clear();
            var intents = container.GetChildren().OfType<NIntent>().Where(GodotObject.IsInstanceValid).ToList();
            // As the intent on screen says: nothing while it's hidden there (its move would be given away).
            var hidden = intents.Any(nIntent => Sts2State.Private<AbstractIntent?>(nIntent, "_intent", fallback: null) is HiddenIntent or UnknownIntent);
            var shown = playerTurn && !hidden && node.Entity is { } creature ? For(creature) : null;
            var onScreen = intents.Select(nIntent => Sts2State.Private<AbstractIntent?>(nIntent, "_intent", fallback: null)).ToList();
            var corners = shown != null && onScreen.All(abstractIntent => abstractIntent != null) ? IntentCorner.Assign(onScreen!, shown.Powers) : new();
            // Every block number, then each power in its intent's bottom-left corner (IntentCorner.Place).
            for (var intentIndex = 0; intentIndex < intents.Count; intentIndex++)
                if (intents[intentIndex].GetNodeOrNull<Control>(GodotNames.Path("%IntentHolder")) is { } holder)
                    Block(holder, intents[intentIndex], onScreen[intentIndex] is DefendIntent && shown?.Block is > 0 ? shown.Block : null);
            // Each power in the corner of the intent it belongs to (decision #99).
            for (var intentIndex = 0; intentIndex < intents.Count; intentIndex++)
                IntentCorner.Place(intents[intentIndex], corners.TryGetValue(intentIndex, out var mine) ? mine : null);
        }
    }

    /// <summary>The block number on a defend intent, where the game puts an attack's damage (its %Value label). Intent Forecast's too.</summary>
    internal static void Block(Control holder, NIntent intent, int? amount)
    {
        var label = holder.GetNodeOrNull<Label>(GodotNames.Path(BlockName));
        if (amount == null)
        {
            label.RemoveAndFree();
            return;
        }
        if (label == null)
        {
            label = UiKit.Label("", size: 22, BlockColor);
            label.Name = GodotNames.Name(BlockName);
            label.MouseFilter = Control.MouseFilterEnum.Ignore;
            label.AddThemeConstantOverride(GodotNames.Name("outline_size"), 8);
            label.AddThemeColorOverride(GodotNames.Name("font_outline_color"), new Color(0, 0, 0, 0.95f));
            label.HorizontalAlignment = HorizontalAlignment.Right;
            label.VerticalAlignment = VerticalAlignment.Bottom;
            holder.AddChild(label);
        }
        var text = amount.Value.ToString();
        if (label.Text != text) label.Text = text;
        // Over the game's own value label (empty on a defend intent): bottom right of the icon, as an attack's damage.
        if (intent.GetNodeOrNull<Control>(GodotNames.Path("%Value")) is { } value)
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
}
