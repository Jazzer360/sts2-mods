using System.Reflection;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Cards;
using MegaCrit.Sts2.Core.Nodes.Combat;
using Sts2Mods.Core.Ui;

namespace Sts2Mods.Core.Game;

/// <summary>
/// Read-only helpers over live game state. No Harmony patches: everything here is a
/// lookup on public game objects (or a private field via Traverse where noted).
/// </summary>
public static class Sts2State
{
    private static CombatState? _combat;
    private static bool _subscribed;

    private static ModLog? _log;

    /// <summary>Call once at mod load. Remembers the current combat via CombatManager.CombatSetUp; <paramref name="log"/> gets the placement lines.</summary>
    public static void Init(ModLog log)
    {
        _log = log;
        if (_subscribed) return;
        _subscribed = true;
        CombatManager.Instance.CombatSetUp += combat => _combat = combat;
    }

    public static bool InCombat => _combat != null && CombatManager.Instance.IsInProgress;

    public static CombatState? Combat => InCombat ? _combat : null;

    /// <summary>
    /// The local player in the current combat, or null. Kept for the fight (the same combat and local id): the game's lookup
    /// (CombatState.GetPlayer) builds three lists every call, and every feature asks several times a frame.
    /// </summary>
    public static Player? Me
    {
        get
        {
            if (Combat is not { } combat) return null;
            var netId = LocalContext.NetId;
            if (ReferenceEquals(combat, _meCombat) && netId == _meNetId && _me != null) return _me;
            var me = LocalContext.GetMe(combat);
            (_me, _meCombat, _meNetId) = (me, combat, netId);
            return me;
        }
    }

    private static Player? _me;
    private static CombatState? _meCombat;
    private static ulong? _meNetId;

    /// <summary>
    /// The local player in the run, in a fight or not (the fight's when there is one). Kept for the run (the same run state and local
    /// id): the game's lookup (RunState.GetPlayer) makes a closure and an enumerator every call, and the features outside fights ask every
    /// frame (decision #177).
    /// </summary>
    public static Player? MeInRun
    {
        get
        {
            if (Me is { } me) return me;
            if (MegaCrit.Sts2.Core.Runs.RunManager.Instance is not { } runManager) return null;
            var state = Private<MegaCrit.Sts2.Core.Runs.RunState?>(runManager, "State", fallback: null);
            if (state == null) return null;
            var netId = LocalContext.NetId;
            if (ReferenceEquals(state, _runMeState) && netId == _runMeNetId && _runMe != null) return _runMe;
            var found = LocalContext.GetMe(state);
            (_runMe, _runMeState, _runMeNetId) = (found, state, netId);
            return found;
        }
    }

    private static Player? _runMe;
    private static MegaCrit.Sts2.Core.Runs.RunState? _runMeState;
    private static ulong? _runMeNetId;

    public static bool IsPlayerTurn => Combat is { } combat && combat.CurrentSide == CombatSide.Player
        && !CombatManager.Instance.EndingPlayerTurnPhaseOne && !CombatManager.Instance.EndingPlayerTurnPhaseTwo
        // Between the two end-of-turn phases (multiplayer waits for every player's ReadyToBeginEnemyTurnAction)
        // neither flag is set, but the end-of-turn effects have run: the player stays in the End phase until the
        // enemy turn starts (PlayerTurnPhase.End).
        && Me?.PlayerCombatState?.Phase is not (PlayerTurnPhase.AutoPostPlay or PlayerTurnPhase.End);

    public static int TurnNumber => Me?.PlayerCombatState?.TurnNumber ?? 0;

    /// <summary>
    /// Your hand as the game's own list (its pile keeps one behind an IReadOnlyList): a foreach through the interface boxes an enumerator,
    /// garbage on every frame for every feature that looks at the hand (decision #177).
    /// </summary>
    public static List<CardModel> Hand(Player me) =>
        me.PlayerCombatState?.Hand.Cards is { } cards ? cards as List<CardModel> ?? new List<CardModel>(cards) : NoCards;

    private static readonly List<CardModel> NoCards = new();

    public static int DrawPileCount(Player me) => me.PlayerCombatState?.DrawPile.Cards.Count ?? 0;

    public static int DiscardPileCount(Player me) => me.PlayerCombatState?.DiscardPile.Cards.Count ?? 0;

    /// <summary>
    /// Card nodes currently shown in the hand, paired with their models, into <paramref name="into"/> (cleared first). A list the caller
    /// keeps, not an iterator: an iterator is an object made on every call, every frame (decision #177).
    /// </summary>
    public static void HandNodes(List<(NCard Node, CardModel Card)> into)
    {
        into.Clear();
        var hand = NPlayerHand.Instance;
        if (hand == null) return;
        NCard? first = null;
        // ActiveHolders makes three lists and a child array each call: the visible holders by index instead.
        var container = hand.CardHolderContainer;
        for (var i = 0; container != null && i < container.GetChildCount(); i++)
        {
            if (container.GetChild(i) is not MegaCrit.Sts2.Core.Nodes.Cards.Holders.NHandCardHolder holder || !holder.IsVisibleInTree()) continue;
            var node = holder.CardNode;
            var model = node?.Model;
            if (node != null && model != null && Godot.GodotObject.IsInstanceValid(node))
            {
                first ??= node;
                into.Add((node, model));
            }
        }

        // The card you're holding (dragging or aiming): StartCardPlay reparents its holder from
        // the holder container to the hand itself, so it isn't in ActiveHolders. Its model is
        // still in the hand pile until the play starts.
        var held = HeldCard(hand);
        if (held is { } nodeCard && nodeCard.Node != first) into.Add(nodeCard);
    }

    /// <summary>
    /// NPlayerHand._currentCardPlay, found once. A plain field read, not FieldRefAccess: that generates code when the class is first
    /// used, and two threads doing it at once made this class's initializer throw (every read of the game failing after it).
    /// </summary>
    private static readonly FieldInfo? CurrentCardPlayField = AccessTools.Field(typeof(NPlayerHand), "_currentCardPlay");

    private static MegaCrit.Sts2.Core.Nodes.Combat.NCardPlay? CurrentCardPlay(NPlayerHand hand) => CurrentCardPlayField?.GetValue(hand) as MegaCrit.Sts2.Core.Nodes.Combat.NCardPlay;

    /// <summary>The card you're holding (dragging or aiming, NPlayerHand._currentCardPlay): its node and model, or null.</summary>
    public static (NCard Node, CardModel Card)? HeldCard() => NPlayerHand.Instance is { } hand ? HeldCard(hand) : null;

    /// <summary>The card play in progress (NPlayerHand._currentCardPlay: the mouse's or a controller's), or null.</summary>
    public static MegaCrit.Sts2.Core.Nodes.Combat.NCardPlay? CurrentCardPlay() => NPlayerHand.Instance is { } hand ? CurrentCardPlay(hand) : null;

    private static (NCard Node, CardModel Card)? HeldCard(NPlayerHand hand)
    {
        try
        {
            var play = CurrentCardPlay(hand);
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
        if (Me is not { } me || HealthBarOf(me.Creature) is not var (display, bar)) return null;
        var rect = bar != null && bar.Size.X >= 1 ? ScreenRect(bar) : ScreenRect(display);

        // The power container doesn't size itself to its icons (it reported a tiny rect in game
        // while an icon drew below it), so measure the icons themselves.
        var powers = display.GetNodeOrNull<Godot.Control>(GodotNames.Path("%PowerContainer"));
        var iconHeight = 0f;
        if (powers != null) rect = MergeVisibleControls(rect, powers, out iconHeight);

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
    /// The combat room's creature nodes as the game's own list. CreatureNodes is that list behind an IEnumerable, and a foreach through the
    /// interface makes an enumerator every time: garbage on every frame for each widget beside an HP bar (decision #173).
    /// </summary>
    public static List<MegaCrit.Sts2.Core.Nodes.Combat.NCreature> CreatureNodesOf(MegaCrit.Sts2.Core.Nodes.Rooms.NCombatRoom room) =>
        room.CreatureNodes as List<MegaCrit.Sts2.Core.Nodes.Combat.NCreature> ?? room.CreatureNodes.ToList();

    /// <summary>A creature's node in the combat room, or null.</summary>
    public static MegaCrit.Sts2.Core.Nodes.Combat.NCreature? NodeOf(MegaCrit.Sts2.Core.Entities.Creatures.Creature who)
    {
        if (MegaCrit.Sts2.Core.Nodes.Rooms.NCombatRoom.Instance is not { } room) return null;
        foreach (var creatureNode in CreatureNodesOf(room))
            if (Godot.GodotObject.IsInstanceValid(creatureNode) && creatureNode.Entity == who) return creatureNode;
        return null;
    }

    /// <summary>
    /// The teammate whose character you're hovering (multiplayer: the game then shows their HP bar,
    /// NCreature.OnFocus), or null.
    /// </summary>
    public static Player? FocusedTeammate()
    {
        var me = Me;
        var room = MegaCrit.Sts2.Core.Nodes.Rooms.NCombatRoom.Instance;
        if (me == null || room == null) return null;
        foreach (var creatureNode in CreatureNodesOf(room))
        {
            if (!Godot.GodotObject.IsInstanceValid(creatureNode) || !creatureNode.IsFocused) continue;
            if (creatureNode.Entity?.Player is { } player && player != me && creatureNode.Entity == player.Creature) return player;
        }
        return null;
    }

    /// <summary>Screen rect of the hovered teammate's block indicator (see BlockRect), or null.</summary>
    public static Godot.Rect2? FocusedTeammateBlockRect() => FocusedTeammate() is { } player ? BlockRect(player.Creature) : null;

    /// <summary>
    /// Screen rect of a player's block indicator (NHealthBar "%BlockContainer", at the left end of the HP
    /// bar), or the HP bar itself.
    /// </summary>
    public static Godot.Rect2? BlockRect(MegaCrit.Sts2.Core.Entities.Creatures.Creature who)
    {
        if (HealthBarOf(who) is not var (display, bar)) return null;
        var block = bar?.GetNodeOrNull<Godot.Control>(GodotNames.Path("%BlockContainer"));
        if (block != null && block.Size.X >= 1)
        {
            // Vertically, use the HP bar's band so the widget centers on the bar, not the icon.
            var rect = ScreenRect(block);
            var otherRect = ScreenRect(bar!);
            return new Godot.Rect2(new Godot.Vector2(rect.Position.X, otherRect.Position.Y), new Godot.Vector2(rect.Size.X, otherRect.Size.Y));
        }
        return bar != null && bar.Size.X >= 1 ? ScreenRect(bar) : ScreenRect(display);
    }

    /// <summary>
    /// A rect merged with the screen rects of the visible, non-empty controls under a node (one level of nesting deep enough for power
    /// icons). By index, not GetChildren: every frame, for each widget beside the HP bar, and GetChildren makes an array each call.
    /// </summary>
    private static Godot.Rect2 MergeVisibleControls(Godot.Rect2 rect, Godot.Node parent) => MergeVisibleControls(rect, parent, out _);

    /// <summary>MergeVisibleControls, with the tallest of those controls (0: none).</summary>
    private static Godot.Rect2 MergeVisibleControls(Godot.Rect2 rect, Godot.Node parent, out float tallest)
    {
        var tallestSeen = 0f;
        for (var i = 0; i < parent.GetChildCount(); i++)
        {
            if (parent.GetChild(i) is not Godot.Control control || !control.IsVisibleInTree()) continue;
            if (control.Size.X >= 4 && control.Size.Y >= 4) Merge(ScreenRect(control));
            for (var j = 0; j < control.GetChildCount(); j++)
                if (control.GetChild(j) is Godot.Control otherControl && otherControl.IsVisibleInTree() && otherControl.Size.X >= 4 && otherControl.Size.Y >= 4)
                    Merge(ScreenRect(otherControl));
        }
        tallest = tallestSeen;
        return rect;

        void Merge(Godot.Rect2 controlRect)
        {
            rect = rect.Merge(controlRect);
            tallestSeen = Math.Max(tallestSeen, controlRect.Size.Y);
        }
    }

    /// <summary>A control's rect in viewport (screen) coordinates, accounting for cameras and canvas layers.</summary>
    public static Godot.Rect2 ScreenRect(Godot.Control control)
    {
        // The box of its four corners on screen: a mirrored control (a rest-site character on the right of the fire, flipped to face it) has a
        // transform whose Scale puts the mirror's minus sign on Y, so origin plus Size * Scale gave a box above it and off to the side.
        var transform2D = control.GetGlobalTransformWithCanvas();
        var size = control.Size;
        var topLeft = transform2D * Godot.Vector2.Zero;
        var topRight = transform2D * new Godot.Vector2(size.X, 0);
        var bottomLeft = transform2D * new Godot.Vector2(0, size.Y);
        var bottomRight = transform2D * size;
        var min = new Godot.Vector2(Math.Min(Math.Min(topLeft.X, topRight.X), Math.Min(bottomLeft.X, bottomRight.X)),
            Math.Min(Math.Min(topLeft.Y, topRight.Y), Math.Min(bottomLeft.Y, bottomRight.Y)));
        var max = new Godot.Vector2(Math.Max(Math.Max(topLeft.X, topRight.X), Math.Max(bottomLeft.X, bottomRight.X)),
            Math.Max(Math.Max(topLeft.Y, topRight.Y), Math.Max(bottomLeft.Y, bottomRight.Y)));
        return new Godot.Rect2(min, max - min);
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
        if (HealthBarOf(who) is not var (display, bar)) return null;
        if (bar == null) return ScreenRect(display);

        var container = bar.GetNodeOrNull<Godot.Control>(GodotNames.Path("%HpBarContainer"));
        if (container == null) return ScreenRect(bar);

        var rect = MergeVisibleControls(ScreenRect(container), container);

        // One log line per combat, so placement can be checked against the game log.
        var combatId = MegaCrit.Sts2.Core.Combat.CombatManager.Instance.CurrentCombatId;
        if (log && combatId != null && combatId != _loggedHpBarFor)
        {
            _loggedHpBarFor = combatId;
            _log?.Info(
                $"HP bar rects: NHealthBar={ScreenRect(bar)} HpBarContainer={ScreenRect(container)} measured={rect} viewport={bar.GetViewportRect().Size}");
        }
        return rect;
    }

    /// <summary>
    /// A creature's status display (its NCreature's "%HealthBar", an NCreatureStateDisplay) and the NHealthBar inside it (its own
    /// "%HealthBar"), while the bar is shown (BarShown); null when the creature has no node or its bar is down.
    /// </summary>
    private static (Godot.Control Display, Godot.Control? Bar)? HealthBarOf(MegaCrit.Sts2.Core.Entities.Creatures.Creature who)
    {
        var creature = NodeOf(who);
        var display = creature?.GetNodeOrNull<Godot.Control>(GodotNames.Path("%HealthBar"));
        if (display == null || !BarShown(creature!, display)) return null;
        return (display, display.GetNodeOrNull<Godot.Control>(GodotNames.Path("%HealthBar")));
    }

    /// <summary>
    /// The creature's HP bar is up, or only faded out while you hover another player in
    /// multiplayer (NCreature.SetRemotePlayerFocused hides yours and shows theirs): its place is still
    /// valid, so what is attached to your character stays there instead of moving.
    /// </summary>
    private static bool BarShown(Godot.Control creature, Godot.Control display)
        => display.IsVisibleInTree() || creature.IsVisibleInTree();

    /// <summary>The fight whose HP bar was logged: compared as its own type (as an object it was boxed on every frame of every fight).</summary>
    private static MegaCrit.Sts2.Core.Combat.CombatId? _loggedHpBarFor;

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

    /// <summary>
    /// True while the only thing over combat is a pick of one card from a few (NChooseACardSelectionScreen: a discovery, a potion's
    /// choice): the fight is still behind it, and what you take matters to it.
    /// </summary>
    public static bool IsCardChoiceOverCombat()
    {
        if (!InCombat) return false;
        var capstone = MegaCrit.Sts2.Core.Nodes.Screens.Capstones.NCapstoneContainer.Instance;
        if (capstone != null && capstone.InUse) return false;
        var map = MegaCrit.Sts2.Core.Nodes.Screens.Map.NMapScreen.Instance;
        if (map != null && map.IsOpen) return false;
        var overlays = MegaCrit.Sts2.Core.Nodes.Screens.Overlays.NOverlayStack.Instance;
        return overlays is { ScreenCount: 1 } && overlays.Peek() is MegaCrit.Sts2.Core.Nodes.Screens.CardSelection.NChooseACardSelectionScreen;
    }

    /// <summary>Cards the local player finished playing this turn (from combat history).</summary>
    public static IEnumerable<CardModel> CardsPlayedThisTurn(Player me)
    {
        var combat = Combat;
        if (combat == null) return Array.Empty<CardModel>();
        return CombatManager.Instance.History.CardPlaysFinished
            .Where(cardPlayFinishedEntry => cardPlayFinishedEntry.HappenedThisTurn(combat) && cardPlayFinishedEntry.CardPlay.Player == me)
            .Select(cardPlayFinishedEntry => cardPlayFinishedEntry.CardPlay.Card);
    }

    public static int CountPlayedThisTurn(Player me, CardType? type = null)
        => CardsPlayedThisTurn(me).Count(card => type == null || card.Type == type);

    public static T? Relic<T>(Player me) where T : RelicModel => me.Relics.OfType<T>().FirstOrDefault();

    /// <summary>
    /// Called once per type and member that <see cref="Private{T}"/> couldn't find (a game update
    /// renamed or removed it): the rule reading it is silently using its fallback, so mods log and
    /// report it.
    /// </summary>
    public static event Action<string, string>? MissingMember;

    /// <summary>Called once per type and member that <see cref="Private{T}"/> found but couldn't read as the type asked for (a game update changed it), with the error.</summary>
    public static event Action<string, string, string>? UnreadableMember;

    private static readonly HashSet<(Type, string)> _missing = new();

    /// <summary>
    /// The members <see cref="Private{T}"/> couldn't read on this thread while it's open (missing, or not of the type asked for):
    /// whatever was built from them holds their fallbacks, a guess, so a state or play built in one is marked unknown. Scopes nest:
    /// a read an inner one misses counts in the outer one too.
    /// </summary>
    public sealed class ReadScope : IDisposable
    {
        private readonly ReadScope? _outer;
        private readonly List<string> _unread = new();

        internal ReadScope()
        {
            _outer = _current;
            _current = this;
        }

        /// <summary>Each member it couldn't read, as Type.Member, in the order they were missed.</summary>
        public IReadOnlyList<string> Unread => _unread;

        internal void Missed(string member)
        {
            _unread.Add(member);
            _outer?.Missed(member);
        }

        public void Dispose() => _current = _outer;
    }

    [ThreadStatic] private static ReadScope? _current;

    /// <summary>Starts collecting the reads that fail on this thread, until the scope is disposed.</summary>
    public static ReadScope TrackReads() => new();

    /// <summary>
    /// Reads a private member by name. Returns fallback if it's missing (game patch renamed it) or not of type T, reports a missing
    /// one once, and tells the open read scope (TrackReads) every time.
    /// </summary>
    public static T Private<T>(object obj, string member, T fallback)
    {
        try
        {
            // The property or field found once per type and name (a Traverse made three objects every call, and the features read
            // some of these every frame: decision #173).
            var found = obj == null ? null : Members.GetOrAdd((obj.GetType(), member), FindMember);
            if (found is PropertyInfo property) return (T)property.GetValue(obj)!;
            if (found is FieldInfo field) return (T)field.GetValue(obj)!;
            _current?.Missed($"{obj?.GetType().Name}.{member}");
            lock (_missing)
            {
                if (!_missing.Add((obj!.GetType(), member))) return fallback;
            }
            MissingMember?.Invoke(obj.GetType().Name, member);
            return fallback;
        }
        catch (Exception ex)
        {
            _current?.Missed($"{obj?.GetType().Name}.{member}"); // there, but not a T (a game update changed its type)
            bool first;
            lock (_missing) first = obj != null && _missing.Add((obj.GetType(), member));
            if (first) UnreadableMember?.Invoke(obj!.GetType().Name, member, $"{ex.GetType().Name}: {ex.Message}");
        }
        return fallback;
    }

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<(Type Type, string Member), MemberInfo?> Members = new();

    /// <summary>A member by name as Traverse finds it: a property first, then a field, declared on the type or a base type.</summary>
    private static MemberInfo? FindMember((Type Type, string Member) key) =>
        (MemberInfo?)AccessTools.Property(key.Type, key.Member) ?? AccessTools.Field(key.Type, key.Member);

    public static int Var(AbstractModel model, string key, int fallback = 0)
    {
        var vars = model switch
        {
            CardModel card => card.DynamicVars,
            RelicModel relic => relic.DynamicVars,
            PowerModel power => power.DynamicVars,
            PotionModel potion => potion.DynamicVars,
            _ => null,
        };
        return vars != null && vars.TryGetValue(key, out var dynamicVar) ? dynamicVar.IntValue : fallback;
    }
}
