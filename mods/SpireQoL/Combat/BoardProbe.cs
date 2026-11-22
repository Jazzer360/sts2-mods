using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using Sts2Mods.Core.Game;

namespace SpireQoL.Combat;

/// <summary>
/// Whether anything a prediction reads could have changed since the last frame (decision #130): one hash of plain values read
/// from the game, worked out once a frame, calling none of its hooks and allocating nothing. The combat log's length (every
/// play, hit, power, draw and move adds to it), each creature's HP, block, powers (their amount and shown count) and next move, each player's energy, stars,
/// piles, hand (each card: its upgrade, cost, enchantment, affliction and own keywords), relic counters, orbs, arrows and potions, the
/// turn and side, and the mod's settings. When it's the same as the probe a result was worked out from, the turn forecast, Proc
/// Indicators and Card Indicators use that result instead of working it out again from the game. Dev mode works them out anyway on
/// such a frame once a second and logs it when they differ (a change this misses), so a gap shows up in the logs before it shows on screen.
/// </summary>
internal static class BoardProbe
{
    private static ulong _frame = ulong.MaxValue;
    private static int _hash;

    /// <summary>When the frame's probe last differed from the frame before (Godot's ms clock): the dev stream's quiet moments (decision #176).</summary>
    public static ulong ChangedAtMs { get; private set; }

    /// <summary>
    /// How often dev mode works a kept result out anyway on unchanged frames, to check the probe: once a second for each (decision
    /// #173; every 30th frame before, which at a high frame rate was about half the mod's garbage in a quiet fight).
    /// </summary>
    public const double CheckSeconds = 1;

    /// <summary>
    /// This frame's probe (worked out on the first call of the frame). A result is reused only when this equals the probe it was
    /// worked out from (each keeps its own), never "the same as last frame": the board can change on frames where that result
    /// isn't asked for (the enemy turn discarding your hand, its powers ticking down) and settle before it is asked again.
    /// </summary>
    public static int Hash(Player me, ICombatState combat)
    {
        // A game callback (the audit's prediction as a play begins, inside the game's hook; decision #131): the board can change between it and
        // the frame run in the same frame, so it reads the board now, and the frame run reads it again.
        if (!Sts2Mods.Core.Ui.OverlayHost.InFrameRun)
        {
            _frame = ulong.MaxValue;
            return Compute(me, combat);
        }
        var frame = Godot.Engine.GetProcessFrames();
        if (frame == _frame) return _hash;
        _frame = frame;
        var hash = Compute(me, combat);
        if (hash != _hash) ChangedAtMs = Godot.Time.GetTicksMsec();
        _hash = hash;
        return _hash;
    }

    private static readonly HashSet<string> Warned = new();

    /// <summary>
    /// Dev mode: a result worked out on an unchanged frame differed from the one that would have been reused, so the probe missed a
    /// change. Logged once per what and why (a warning: the dev dashboard and the log show it).
    /// </summary>
    public static void Missed(string what, string why, string? detail = null)
    {
        if (Warned.Add(what + why + detail))
            ModEntry.Log.Detail($"[perf] board probe missed a change: {what}: {why}{(detail == null ? "" : $" ({detail})")} (results reused on unchanged frames could be stale)");
    }

    /// <summary>Where two simulator states differ (their JSON, path by path; the first few), to tell what the probe should read.</summary>
    internal static string Diff(Sim.SimState before, Sim.SimState after)
    {
        try
        {
            var json = System.Text.Json.Nodes.JsonNode.Parse(before.ToJson());
            var otherJson = System.Text.Json.Nodes.JsonNode.Parse(after.ToJson());
            var diffs = new List<string>();
            void Walk(System.Text.Json.Nodes.JsonNode? beforeNode, System.Text.Json.Nodes.JsonNode? afterNode, string path)
            {
                if (diffs.Count >= 6) return;
                if (beforeNode is System.Text.Json.Nodes.JsonObject beforeObject && afterNode is System.Text.Json.Nodes.JsonObject afterObject)
                {
                    foreach (var key in beforeObject.Select(entry => entry.Key).Union(afterObject.Select(entry => entry.Key))) Walk(beforeObject[key], afterObject[key], path == "" ? key : path + "." + key);
                    return;
                }
                if (beforeNode is System.Text.Json.Nodes.JsonArray beforeArray && afterNode is System.Text.Json.Nodes.JsonArray afterArray && beforeArray.Count == afterArray.Count)
                {
                    for (var i = 0; i < beforeArray.Count; i++) Walk(beforeArray[i], afterArray[i], $"{path}[{i}]");
                    return;
                }
                var beforeText = beforeNode?.ToJsonString() ?? "null";
                var afterText = afterNode?.ToJsonString() ?? "null";
                if (beforeText != afterText) diffs.Add($"{path}: {Short(beforeText)} → {Short(afterText)}");
            }
            static string Short(string text) => text.Length > 60 ? text[..57] + "..." : text;
            Walk(json, otherJson, "");
            return diffs.Count == 0 ? "no difference in its JSON" : string.Join("; ", diffs);
        }
        catch (Exception ex)
        {
            return "couldn't compare: " + ex.Message;
        }
    }

    // The game's private fields the probe reads, through the runtime's own accessors (UnsafeAccessor): nothing generated when the
    // class starts (FieldRefAccess generates code then, and a failure there, two threads at once, would break the class for good)
    // and nothing allocated per read. A field a game update renamed throws when it's read: Compute's fallback (never the same).
    [System.Runtime.CompilerServices.UnsafeAccessor(System.Runtime.CompilerServices.UnsafeAccessorKind.Field, Name = "_keywords")]
    private static extern ref HashSet<CardKeyword>? OwnKeywords(CardModel card);
    [System.Runtime.CompilerServices.UnsafeAccessor(System.Runtime.CompilerServices.UnsafeAccessorKind.Field, Name = "_keywordsAddedFromOutside")]
    private static extern ref HashSet<CardKeyword> AddedKeywords(CardModel card);
    [System.Runtime.CompilerServices.UnsafeAccessor(System.Runtime.CompilerServices.UnsafeAccessorKind.Field, Name = "_keywordsRemovedFromOutside")]
    private static extern ref HashSet<CardKeyword> RemovedKeywords(CardModel card);
    [System.Runtime.CompilerServices.UnsafeAccessor(System.Runtime.CompilerServices.UnsafeAccessorKind.Field, Name = "_base")]
    private static extern ref int BaseCost(CardEnergyCost cost);
    private static bool _warned;
    /// <summary>The Fool's arrows you can shoot (beta only: null on main, which has no arrows).</summary>
    private static readonly Func<PlayerCombatState, int>? Arrows = Procs.Fool.ArrowCount;

    internal static int Compute(Player me, ICombatState combat)
    {
        var hashCode = new HashCode();
        try
        {
            hashCode.Add(combat.CurrentSide);
            hashCode.Add(Sts2State.TurnNumber);
            hashCode.Add(QolSettings.Revision);
            hashCode.Add(CombatManager.Instance?.History.Entries is ICollection<MegaCrit.Sts2.Core.Combat.History.CombatHistoryEntry> log ? log.Count : -1);
            // Allies, then enemies: Creatures makes a list of both each read.
            AddCreatures(ref hashCode, combat.Allies);
            AddCreatures(ref hashCode, combat.Enemies);
        }
        catch (Exception ex)
        {
            hashCode.Add(Godot.Engine.GetProcessFrames()); // couldn't read it all: never the same as last frame
            // Once a launch: every frame is then worked out from the game (correct, only slower), which nothing else would show.
            if (!_warned)
            {
                _warned = true;
                ModEntry.Log.Warn($"[probe] the board probe couldn't read the board, so nothing is reused between frames: {ex.GetType().Name}: {ex.Message}");
            }
        }
        return hashCode.ToHashCode();
    }

    /// <summary>A creature in the hash: a stand-in for its id in what a power read names (any value that's the same for the same creature).</summary>
    private static readonly Func<MegaCrit.Sts2.Core.Entities.Creatures.Creature, int?> ProbeId = creature => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(creature);

    private static void AddCreatures(ref HashCode hashCode, IReadOnlyList<MegaCrit.Sts2.Core.Entities.Creatures.Creature> creatures)
    {
        for (var i = 0; i < creatures.Count; i++)
        {
            var creature = creatures[i];
            hashCode.Add(System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(creature));
            hashCode.Add(creature.CurrentHp);
            hashCode.Add(creature.MaxHp);
            hashCode.Add(creature.Block);
            hashCode.Add(creature.IsAlive);
            var powers = creature.Powers;
            // Its shown count, and what the simulator reads from it (SimReaders.Read: a flag such as Skittish's block this turn, a
            // count kept apart from Amount such as Withering Presence's CardsLeft): a change in either is a change.
            for (var j = 0; j < powers.Count; j++)
            {
                hashCode.Add(powers[j].GetType());
                hashCode.Add(powers[j].Amount);
                hashCode.Add(powers[j].DisplayAmount);
                var (_, amount, data) = Sim.SimReaders.Read(powers[j], Sim.SimAdapter.SimPowerName(powers[j].GetType()), ProbeId);
                hashCode.Add(amount);
                hashCode.Add(data);
            }
            if (creature.Monster is { } monster) hashCode.Add(monster.NextMove?.Id);
            if (creature.Player is { } player) AddPlayer(ref hashCode, player);
        }
    }

    private static void AddPlayer(ref HashCode hashCode, Player player)
    {
        if (player.PlayerCombatState is not { } pcs) return;
        hashCode.Add(pcs.Energy);
        hashCode.Add(pcs.Stars);
        if (Arrows != null) hashCode.Add(Arrows(pcs));
        hashCode.Add(PileType.Draw.GetPile(player).Cards.Count);
        hashCode.Add(PileType.Discard.GetPile(player).Cards.Count);
        hashCode.Add(PileType.Exhaust.GetPile(player).Cards.Count);
        var hand = PileType.Hand.GetPile(player).Cards;
        hashCode.Add(hand.Count);
        for (var i = 0; i < hand.Count; i++)
        {
            var card = hand[i];
            hashCode.Add(System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(card));
            hashCode.Add(card.CurrentUpgradeLevel);
            hashCode.Add(card.Enchantment);
            hashCode.Add(card.Enchantment?.Amount);
            hashCode.Add(card.Affliction);
            hashCode.Add(OwnKeywords(card)?.Count);
            hashCode.Add(AddedKeywords(card).Count);
            hashCode.Add(RemovedKeywords(card).Count);
            hashCode.Add(BaseCost(card.EnergyCost));
            hashCode.Add(card.EnergyCost.HasLocalModifiers);
        }
        var relics = player.Relics;
        for (var i = 0; i < relics.Count; i++) { hashCode.Add(relics[i].DisplayAmount); hashCode.Add(relics[i].Status); }
        var orbs = pcs.OrbQueue.Orbs;
        hashCode.Add(pcs.OrbQueue.Capacity);
        for (var i = 0; i < orbs.Count; i++) hashCode.Add(orbs[i].GetType());
        var potions = player.PotionSlots;
        for (var i = 0; i < potions.Count; i++) hashCode.Add(potions[i]);
    }
}
