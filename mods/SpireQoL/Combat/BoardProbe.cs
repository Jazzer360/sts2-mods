using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using Sts2Mods.Core.Game;

namespace SpireQoL.Combat;

/// <summary>
/// Whether anything a prediction reads could have changed since the last frame (decision #130): one hash of plain values read
/// from the game, worked out once a frame, calling none of its hooks and allocating nothing. The combat log's length (every
/// play, hit, power, draw and move adds to it), each creature's HP, block, powers and next move, each player's energy, stars,
/// piles, hand (each card: its upgrade, cost, enchantment, affliction and own keywords), relic counters, orbs and potions, the
/// turn and side, and the mod's settings. When it's the same as the probe a result was worked out from, the turn forecast, Proc
/// Indicators and Card Indicators use that result instead of working it out again from the game. Dev mode works them out anyway every
/// 30th such frame and logs it when they differ (a change this misses), so a gap shows up in the logs before it shows on screen.
/// </summary>
internal static class BoardProbe
{
    private static ulong _frame = ulong.MaxValue;
    private static int _hash;

    /// <summary>How often dev mode works everything out anyway on an unchanged frame, to check the probe.</summary>
    public const int CheckEvery = 30;

    /// <summary>
    /// This frame's probe (worked out on the first call of the frame). A result is reused only when this equals the probe it was
    /// worked out from (each keeps its own), never "the same as last frame": the board can change on frames where that result
    /// isn't asked for (the enemy turn discarding your hand, its powers ticking down) and settle before it is asked again.
    /// </summary>
    public static int Hash(Player me, ICombatState cs)
    {
        var frame = Godot.Engine.GetProcessFrames();
        if (frame == _frame) return _hash;
        _frame = frame;
        _hash = Compute(me, cs);
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
            ModEntry.Log.Warn($"[perf] board probe missed a change: {what}: {why}{(detail == null ? "" : $" ({detail})")} (results reused on unchanged frames could be stale)");
    }

    /// <summary>Where two simulator states differ (their JSON, path by path; the first few), to tell what the probe should read.</summary>
    internal static string Diff(Sim.SimState before, Sim.SimState after)
    {
        try
        {
            var a = System.Text.Json.Nodes.JsonNode.Parse(before.ToJson());
            var b = System.Text.Json.Nodes.JsonNode.Parse(after.ToJson());
            var diffs = new List<string>();
            void Walk(System.Text.Json.Nodes.JsonNode? x, System.Text.Json.Nodes.JsonNode? y, string path)
            {
                if (diffs.Count >= 6) return;
                if (x is System.Text.Json.Nodes.JsonObject ox && y is System.Text.Json.Nodes.JsonObject oy)
                {
                    foreach (var k in ox.Select(kv => kv.Key).Union(oy.Select(kv => kv.Key))) Walk(ox[k], oy[k], path == "" ? k : path + "." + k);
                    return;
                }
                if (x is System.Text.Json.Nodes.JsonArray ax && y is System.Text.Json.Nodes.JsonArray ay && ax.Count == ay.Count)
                {
                    for (var i = 0; i < ax.Count; i++) Walk(ax[i], ay[i], $"{path}[{i}]");
                    return;
                }
                var sx = x?.ToJsonString() ?? "null";
                var sy = y?.ToJsonString() ?? "null";
                if (sx != sy) diffs.Add($"{path}: {Short(sx)} → {Short(sy)}");
            }
            static string Short(string v) => v.Length > 60 ? v[..57] + "..." : v;
            Walk(a, b, "");
            return diffs.Count == 0 ? "no difference in its JSON" : string.Join("; ", diffs);
        }
        catch (Exception ex)
        {
            return "couldn't compare: " + ex.Message;
        }
    }

    private static readonly AccessTools.FieldRef<CardModel, HashSet<CardKeyword>?> OwnKeywords = AccessTools.FieldRefAccess<CardModel, HashSet<CardKeyword>?>("_keywords");
    private static readonly AccessTools.FieldRef<CardModel, HashSet<CardKeyword>> AddedKeywords = AccessTools.FieldRefAccess<CardModel, HashSet<CardKeyword>>("_keywordsAddedFromOutside");
    private static readonly AccessTools.FieldRef<CardModel, HashSet<CardKeyword>> RemovedKeywords = AccessTools.FieldRefAccess<CardModel, HashSet<CardKeyword>>("_keywordsRemovedFromOutside");
    private static readonly AccessTools.FieldRef<CardEnergyCost, int> BaseCost = AccessTools.FieldRefAccess<CardEnergyCost, int>("_base");

    internal static int Compute(Player me, ICombatState cs)
    {
        var h = new HashCode();
        try
        {
            h.Add(cs.CurrentSide);
            h.Add(Sts2State.TurnNumber);
            h.Add(QolSettings.Revision);
            h.Add(CombatManager.Instance?.History.Entries is ICollection<MegaCrit.Sts2.Core.Combat.History.CombatHistoryEntry> log ? log.Count : -1);
            var creatures = cs.Creatures;
            for (var i = 0; i < creatures.Count; i++)
            {
                var c = creatures[i];
                h.Add(System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(c));
                h.Add(c.CurrentHp);
                h.Add(c.MaxHp);
                h.Add(c.Block);
                h.Add(c.IsAlive);
                var powers = c.Powers;
                for (var j = 0; j < powers.Count; j++) { h.Add(powers[j].GetType()); h.Add(powers[j].Amount); }
                if (c.Monster is { } m) h.Add(m.NextMove?.Id);
                if (c.Player is { } p) AddPlayer(ref h, p);
            }
        }
        catch
        {
            h.Add(Godot.Engine.GetProcessFrames()); // couldn't read it all: never the same as last frame
        }
        return h.ToHashCode();
    }

    private static void AddPlayer(ref HashCode h, Player p)
    {
        if (p.PlayerCombatState is not { } pcs) return;
        h.Add(pcs.Energy);
        h.Add(pcs.Stars);
        h.Add(PileType.Draw.GetPile(p).Cards.Count);
        h.Add(PileType.Discard.GetPile(p).Cards.Count);
        h.Add(PileType.Exhaust.GetPile(p).Cards.Count);
        var hand = PileType.Hand.GetPile(p).Cards;
        h.Add(hand.Count);
        for (var i = 0; i < hand.Count; i++)
        {
            var card = hand[i];
            h.Add(System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(card));
            h.Add(card.CurrentUpgradeLevel);
            h.Add(card.Enchantment);
            h.Add(card.Enchantment?.Amount);
            h.Add(card.Affliction);
            h.Add(OwnKeywords(card)?.Count);
            h.Add(AddedKeywords(card).Count);
            h.Add(RemovedKeywords(card).Count);
            h.Add(BaseCost(card.EnergyCost));
            h.Add(card.EnergyCost.HasLocalModifiers);
        }
        var relics = p.Relics;
        for (var i = 0; i < relics.Count; i++) { h.Add(relics[i].DisplayAmount); h.Add(relics[i].Status); }
        var orbs = pcs.OrbQueue.Orbs;
        h.Add(pcs.OrbQueue.Capacity);
        for (var i = 0; i < orbs.Count; i++) h.Add(orbs[i].GetType());
        var potions = p.PotionSlots;
        for (var i = 0; i < potions.Count; i++) h.Add(potions[i]);
    }
}
