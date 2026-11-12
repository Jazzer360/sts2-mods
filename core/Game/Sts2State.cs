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
        NCard? first = null;
        foreach (var holder in hand.ActiveHolders)
        {
            var node = holder.CardNode;
            var model = node?.Model;
            if (node != null && model != null && Godot.GodotObject.IsInstanceValid(node))
            {
                first ??= node;
                yield return (node, model);
            }
        }

        // The card you're holding (dragging or aiming): StartCardPlay reparents its holder from
        // the holder container to the hand itself, so it isn't in ActiveHolders. Its model is
        // still in the hand pile until the play starts.
        var held = HeldCard(hand);
        if (held is { } h && h.Node != first) yield return h;
    }

    private static (NCard Node, CardModel Card)? HeldCard(NPlayerHand hand)
    {
        try
        {
            var play = HarmonyLib.Traverse.Create(hand).Field("_currentCardPlay").GetValue<MegaCrit.Sts2.Core.Nodes.Combat.NCardPlay>();
            if (play == null || !Godot.GodotObject.IsInstanceValid(play)) return null;
            var node = play.Holder?.CardNode;
            var model = node?.Model;
            if (node == null || model == null || !Godot.GodotObject.IsInstanceValid(node)) return null;
            return (node, model);
        }
        catch
        {
            return null;
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
        if (display == null || !BarShown(creature!, display)) return null;

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
    public static Godot.Rect2? PlayerBlockRect() => Me is { } me ? BlockRect(me.Creature) : null;

    /// <summary>
    /// The teammate whose character you're hovering (multiplayer: the game then shows their HP bar,
    /// NCreature.OnFocus), or null.
    /// </summary>
    public static Player? FocusedTeammate()
    {
        var me = Me;
        var room = MegaCrit.Sts2.Core.Nodes.Rooms.NCombatRoom.Instance;
        if (me == null || room == null) return null;
        foreach (var n in room.CreatureNodes)
        {
            if (!Godot.GodotObject.IsInstanceValid(n) || !n.IsFocused) continue;
            if (n.Entity?.Player is { } p && p != me && n.Entity == p.Creature) return p;
        }
        return null;
    }

    /// <summary>Screen rect of the hovered teammate's block indicator (see BlockRect), or null.</summary>
    public static Godot.Rect2? FocusedTeammateBlockRect() => FocusedTeammate() is { } p ? BlockRect(p.Creature) : null;

    /// <summary>
    /// Screen rect of a player's block indicator (NHealthBar "%BlockContainer", at the left end of the HP
    /// bar), or the HP bar itself.
    /// </summary>
    public static Godot.Rect2? BlockRect(MegaCrit.Sts2.Core.Entities.Creatures.Creature who)
    {
        var room = MegaCrit.Sts2.Core.Nodes.Rooms.NCombatRoom.Instance;
        if (room == null) return null;

        var creature = room.CreatureNodes.FirstOrDefault(n => Godot.GodotObject.IsInstanceValid(n) && n.Entity == who);
        var display = creature?.GetNodeOrNull<Godot.Control>("%HealthBar");
        if (display == null || !BarShown(creature!, display)) return null;

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

    /// <summary>
    /// Screen rect of the local player's HP bar as drawn, or null. The NHealthBar node's own box
    /// stopped short of the visible bar in game (a widget placed at its right edge sat over the
    /// bar), so this measures NHealthBar's "%HpBarContainer" and the visible pieces inside it.
    /// </summary>
    public static Godot.Rect2? PlayerHpBarRect() => Me is { } me ? HpBarRect(me.Creature, log: true) : null;

    /// <summary>
    /// Screen rect of Osty's HP bar (Necrobinder), or null when Osty isn't out. The bar stays up
    /// while Osty is dead (it reads "Dead"; NHealthBar.RefreshText), so this doesn't flicker.
    /// </summary>
    public static Godot.Rect2? OstyHpBarRect() => Me?.Osty is { } osty ? HpBarRect(osty, log: false) : null;

    private static Godot.Rect2? HpBarRect(MegaCrit.Sts2.Core.Entities.Creatures.Creature who, bool log)
    {
        var room = MegaCrit.Sts2.Core.Nodes.Rooms.NCombatRoom.Instance;
        if (room == null) return null;
        var creature = room.CreatureNodes.FirstOrDefault(n => Godot.GodotObject.IsInstanceValid(n) && n.Entity == who);
        var display = creature?.GetNodeOrNull<Godot.Control>("%HealthBar");
        if (display == null || !BarShown(creature!, display)) return null;
        var bar = display.GetNodeOrNull<Godot.Control>("%HealthBar");
        if (bar == null) return ScreenRect(display);

        var container = bar.GetNodeOrNull<Godot.Control>("%HpBarContainer");
        if (container == null) return ScreenRect(bar);

        var rect = ScreenRect(container);
        foreach (var r in VisibleControlRects(container))
        {
            rect = rect.Merge(r);
        }

        // One log line per combat, so placement can be checked against the game log.
        var combatId = MegaCrit.Sts2.Core.Combat.CombatManager.Instance.CurrentCombatId;
        if (log && combatId != null && !Equals(combatId, _loggedHpBarFor))
        {
            _loggedHpBarFor = combatId;
            MegaCrit.Sts2.Core.Logging.Log.Info(
                $"[SpireQoL] HP bar rects: NHealthBar={ScreenRect(bar)} HpBarContainer={ScreenRect(container)} measured={rect} viewport={bar.GetViewportRect().Size}");
        }
        return rect;
    }

    /// <summary>
    /// The creature's HP bar is up, or only faded out while you hover another player in
    /// multiplayer (NCreature.SetRemotePlayerFocused hides yours and shows theirs): its place is still
    /// valid, so what is attached to your character stays there instead of moving.
    /// </summary>
    private static bool BarShown(Godot.Control creature, Godot.Control display)
        => display.IsVisibleInTree() || creature.IsVisibleInTree();

    private static object? _loggedHpBarFor;

    /// <summary>
    /// Where floating widgets should draw so cards in hand cover them: directly before the
    /// NPlayerHand node in its parent (NCombatUi). Null outside combat.
    /// </summary>
    public static (Godot.Node Parent, int Index)? WidgetUnderlay()
    {
        // Only during live combat with nothing on top. The combat room (and our widgets with it,
        // if they were still inside) is freed when you leave for the map, quit from the pause
        // menu, or reach game over, and each of those is preceded by combat ending or a screen
        // opening. Returning null moves the widgets back to our own layer first.
        if (!InCombat || IsScreenOpenOverCombat()) return null;
        var hand = NPlayerHand.Instance;
        if (hand == null || !Godot.GodotObject.IsInstanceValid(hand) || !hand.IsInsideTree()) return null;
        var parent = hand.GetParent();
        return parent == null ? null : (parent, hand.GetIndex());
    }

    /// <summary>
    /// True while a game screen covers combat: pause menu, pile and deck views (capstone screens:
    /// NCapstoneContainer), rewards and card-choice screens (NOverlayStack), or the map.
    /// </summary>
    public static bool IsScreenOpenOverCombat()
    {
        var capstone = MegaCrit.Sts2.Core.Nodes.Screens.Capstones.NCapstoneContainer.Instance;
        if (capstone != null && capstone.InUse) return true;
        var overlays = MegaCrit.Sts2.Core.Nodes.Screens.Overlays.NOverlayStack.Instance;
        if (overlays != null && overlays.ScreenCount > 0) return true;
        var map = MegaCrit.Sts2.Core.Nodes.Screens.Map.NMapScreen.Instance;
        return map != null && map.IsOpen;
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

    /// <summary>
    /// Called once per type and member that <see cref="Private{T}"/> couldn't find (a game update
    /// renamed or removed it): the rule reading it is silently using its fallback, so mods log and
    /// report it.
    /// </summary>
    public static event Action<string, string>? MissingMember;

    private static readonly HashSet<(Type, string)> _missing = new();

    /// <summary>Reads a private member by name. Returns fallback if it's missing (game patch renamed it), and reports that once.</summary>
    public static T Private<T>(object obj, string member, T fallback)
    {
        try
        {
            var t = Traverse.Create(obj);
            var p = t.Property(member);
            if (p.PropertyExists()) return p.GetValue<T>();
            var f = t.Field(member);
            if (f.FieldExists()) return f.GetValue<T>();
            lock (_missing)
            {
                if (!_missing.Add((obj.GetType(), member))) return fallback;
            }
            MissingMember?.Invoke(obj.GetType().Name, member);
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
}
