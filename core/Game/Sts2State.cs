using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Cards;
using MegaCrit.Sts2.Core.Nodes.Combat;

namespace Sts2Mods.Core.Game;

/// <summary>
/// Read-only helpers over live game state. No Harmony patches: everything here is a
/// lookup on public game objects (or a private field via Traverse where noted).
/// </summary>
public static class Sts2State
{
    private static CombatState? _combat;
    private static bool _subscribed;

    /// <summary>Call once at mod load. Remembers the current combat via CombatManager.CombatSetUp.</summary>
    public static void Init()
    {
        if (_subscribed) return;
        _subscribed = true;
        CombatManager.Instance.CombatSetUp += cs => _combat = cs;
    }

    public static bool InCombat => _combat != null && CombatManager.Instance.IsInProgress;

    public static CombatState? Combat => InCombat ? _combat : null;

    /// <summary>The local player in the current combat, or null.</summary>
    public static Player? Me => Combat is { } cs ? LocalContext.GetMe(cs) : null;

    public static bool IsPlayerTurn => Combat is { } cs && cs.CurrentSide == CombatSide.Player
        && !CombatManager.Instance.EndingPlayerTurnPhaseOne && !CombatManager.Instance.EndingPlayerTurnPhaseTwo;

    public static int TurnNumber => Me?.PlayerCombatState?.TurnNumber ?? 0;

    public static IReadOnlyList<CardModel> Hand(Player me) => me.PlayerCombatState?.Hand.Cards ?? (IReadOnlyList<CardModel>)Array.Empty<CardModel>();

    public static int DrawPileCount(Player me) => me.PlayerCombatState?.DrawPile.Cards.Count ?? 0;

    public static int DiscardPileCount(Player me) => me.PlayerCombatState?.DiscardPile.Cards.Count ?? 0;

    /// <summary>Card nodes currently shown in the hand, paired with their models.</summary>
    public static IEnumerable<(NCard Node, CardModel Card)> HandNodes()
    {
        var hand = NPlayerHand.Instance;
        if (hand == null) yield break;
        foreach (var holder in hand.ActiveHolders)
        {
            var node = holder.CardNode;
            var model = node?.Model;
            if (node != null && model != null && Godot.GodotObject.IsInstanceValid(node))
            {
                yield return (node, model);
            }
        }
    }

    /// <summary>
    /// Screen rect covering the local player's HP bar and the power icons under it, or null.
    /// From NCombatRoom.CreatureNodes → NCreature "%HealthBar" (NCreatureStateDisplay), whose
    /// own scene has "%HealthBar" (NHealthBar) and "%PowerContainer".
    /// </summary>
    public static Godot.Rect2? PlayerStatusRect()
    {
        var me = Me;
        var room = MegaCrit.Sts2.Core.Nodes.Rooms.NCombatRoom.Instance;
        if (me == null || room == null) return null;

        var creature = room.CreatureNodes.FirstOrDefault(n => Godot.GodotObject.IsInstanceValid(n) && n.Entity == me.Creature);
        var display = creature?.GetNodeOrNull<Godot.Control>("%HealthBar");
        if (display == null || !display.IsVisibleInTree()) return null;

        var bar = display.GetNodeOrNull<Godot.Control>("%HealthBar");
        var rect = bar != null && bar.Size.X >= 1 ? ScreenRect(bar) : ScreenRect(display);

        // The power container doesn't size itself to its icons (it reported a tiny rect in game
        // while an icon drew below it), so measure the icons themselves.
        var powers = display.GetNodeOrNull<Godot.Control>("%PowerContainer");
        var iconHeight = 0f;
        if (powers != null)
        {
            foreach (var r in VisibleControlRects(powers))
            {
                rect = rect.Merge(r);
                iconHeight = Math.Max(iconHeight, r.Size.Y);
            }
        }

        // Always leave room for one row of power icons under the bar, so the widget doesn't
        // jump when the first buff or debuff appears.
        if (iconHeight > 0) _lastPowerRowHeight = iconHeight;
        var reserveBottom = (bar != null ? ScreenRect(bar).End.Y : rect.End.Y) + _lastPowerRowHeight + 4;
        if (rect.End.Y < reserveBottom)
        {
            rect = new Godot.Rect2(rect.Position, new Godot.Vector2(rect.Size.X, reserveBottom - rect.Position.Y));
        }
        return rect;
    }

    private static float _lastPowerRowHeight = 44f;

    /// <summary>
    /// Screen rect of the player's block indicator (NHealthBar "%BlockContainer", at the left end
    /// of the HP bar), or the HP bar itself. The block icon hides at 0 block, but its node keeps
    /// its place, so the rect is stable either way.
    /// </summary>
    public static Godot.Rect2? PlayerBlockRect()
    {
        var me = Me;
        var room = MegaCrit.Sts2.Core.Nodes.Rooms.NCombatRoom.Instance;
        if (me == null || room == null) return null;

        var creature = room.CreatureNodes.FirstOrDefault(n => Godot.GodotObject.IsInstanceValid(n) && n.Entity == me.Creature);
        var display = creature?.GetNodeOrNull<Godot.Control>("%HealthBar");
        if (display == null || !display.IsVisibleInTree()) return null;

        var bar = display.GetNodeOrNull<Godot.Control>("%HealthBar");
        var block = bar?.GetNodeOrNull<Godot.Control>("%BlockContainer");
        if (block != null && block.Size.X >= 1)
        {
            // Vertically, use the HP bar's band so the widget centers on the bar, not the icon.
            var b = ScreenRect(block);
            var hp = ScreenRect(bar!);
            return new Godot.Rect2(new Godot.Vector2(b.Position.X, hp.Position.Y), new Godot.Vector2(b.Size.X, hp.Size.Y));
        }
        return bar != null && bar.Size.X >= 1 ? ScreenRect(bar) : ScreenRect(display);
    }

    /// <summary>Screen rects of visible, non-empty controls under a node (one level of nesting deep enough for power icons).</summary>
    private static IEnumerable<Godot.Rect2> VisibleControlRects(Godot.Node parent)
    {
        foreach (var child in parent.GetChildren())
        {
            if (child is not Godot.Control c || !c.IsVisibleInTree()) continue;
            if (c.Size.X >= 4 && c.Size.Y >= 4) yield return ScreenRect(c);
            foreach (var grand in c.GetChildren())
            {
                if (grand is Godot.Control g && g.IsVisibleInTree() && g.Size.X >= 4 && g.Size.Y >= 4) yield return ScreenRect(g);
            }
        }
    }

    /// <summary>A control's rect in viewport (screen) coordinates, accounting for cameras and canvas layers.</summary>
    public static Godot.Rect2 ScreenRect(Godot.Control c)
    {
        var xf = c.GetGlobalTransformWithCanvas();
        return new Godot.Rect2(xf.Origin, c.Size * xf.Scale);
    }

    /// <summary>Cards the local player finished playing this turn (from combat history).</summary>
    public static IEnumerable<CardModel> CardsPlayedThisTurn(Player me)
    {
        var cs = Combat;
        if (cs == null) return Array.Empty<CardModel>();
        return CombatManager.Instance.History.CardPlaysFinished
            .Where(e => e.HappenedThisTurn(cs) && e.CardPlay.Player == me)
            .Select(e => e.CardPlay.Card);
    }

    public static int CountPlayedThisTurn(Player me, CardType? type = null)
        => CardsPlayedThisTurn(me).Count(c => type == null || c.Type == type);

    public static T? Relic<T>(Player me) where T : RelicModel => me.Relics.OfType<T>().FirstOrDefault();

    /// <summary>Reads a private member by name. Returns fallback if it's missing (game patch renamed it).</summary>
    public static T Private<T>(object obj, string member, T fallback)
    {
        try
        {
            var t = Traverse.Create(obj);
            var p = t.Property(member);
            if (p.PropertyExists()) return p.GetValue<T>();
            var f = t.Field(member);
            if (f.FieldExists()) return f.GetValue<T>();
        }
        catch
        {
            // fall through
        }
        return fallback;
    }

    public static int Var(AbstractModel model, string key, int fallback = 0)
    {
        var vars = model switch
        {
            CardModel c => c.DynamicVars,
            RelicModel r => r.DynamicVars,
            PowerModel p => p.DynamicVars,
            PotionModel po => po.DynamicVars,
            _ => null,
        };
        return vars != null && vars.TryGetValue(key, out var v) ? v.IntValue : fallback;
    }

    public static bool HasVar(CardModel card, string key) => card.DynamicVars.ContainsKey(key);
}
