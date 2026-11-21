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
/// piles, hand (each card: its upgrade, cost, enchantment, affliction and own keywords), relic counters, orbs, arrows and potions, the
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
        _hash = Compute(me, combat);
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

    private static readonly AccessTools.FieldRef<CardModel, HashSet<CardKeyword>?> OwnKeywords = AccessTools.FieldRefAccess<CardModel, HashSet<CardKeyword>?>("_keywords");
    private static readonly AccessTools.FieldRef<CardModel, HashSet<CardKeyword>> AddedKeywords = AccessTools.FieldRefAccess<CardModel, HashSet<CardKeyword>>("_keywordsAddedFromOutside");
    private static readonly AccessTools.FieldRef<CardModel, HashSet<CardKeyword>> RemovedKeywords = AccessTools.FieldRefAccess<CardModel, HashSet<CardKeyword>>("_keywordsRemovedFromOutside");
    private static readonly AccessTools.FieldRef<CardEnergyCost, int> BaseCost = AccessTools.FieldRefAccess<CardEnergyCost, int>("_base");
    /// <summary>The Fool's arrows you can shoot (PlayerCombatState.AvailableArrowCount; none before v0.113.0, the main branch).</summary>
    private static readonly Func<PlayerCombatState, int>? Arrows = AccessTools.PropertyGetter(typeof(PlayerCombatState), "AvailableArrowCount") is { } method
        ? AccessTools.MethodDelegate<Func<PlayerCombatState, int>>(method)
        : null;

    internal static int Compute(Player me, ICombatState combat)
    {
        var hashCode = new HashCode();
        try
        {
            hashCode.Add(combat.CurrentSide);
            hashCode.Add(Sts2State.TurnNumber);
            hashCode.Add(QolSettings.Revision);
            hashCode.Add(CombatManager.Instance?.History.Entries is ICollection<MegaCrit.Sts2.Core.Combat.History.CombatHistoryEntry> log ? log.Count : -1);
            var creatures = combat.Creatures;
            for (var i = 0; i < creatures.Count; i++)
            {
                var creature = creatures[i];
                hashCode.Add(System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(creature));
                hashCode.Add(creature.CurrentHp);
                hashCode.Add(creature.MaxHp);
                hashCode.Add(creature.Block);
                hashCode.Add(creature.IsAlive);
                var powers = creature.Powers;
                for (var j = 0; j < powers.Count; j++) { hashCode.Add(powers[j].GetType()); hashCode.Add(powers[j].Amount); }
                if (creature.Monster is { } monster) hashCode.Add(monster.NextMove?.Id);
                if (creature.Player is { } player) AddPlayer(ref hashCode, player);
            }
        }
        catch
        {
            hashCode.Add(Godot.Engine.GetProcessFrames()); // couldn't read it all: never the same as last frame
        }
        return hashCode.ToHashCode();
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
