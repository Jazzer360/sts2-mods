using MegaCrit.Sts2.Core.Combat.History;
using MegaCrit.Sts2.Core.Combat.History.Entries;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Relics;
using MegaCrit.Sts2.Core.Models;
using SpireQoL.Data;
using Sts2Mods.Core.Game;

namespace SpireQoL.Diagnostics;

/// <summary>
/// Everything a card play can change besides what the other checks compare: energy, stars, gold,
/// every creature's HP and max HP, every card of yours (pile, cost, upgrade, keywords, affliction,
/// enchantment), and every relic's counter and status. Taken when the play's action starts and when
/// it finishes (ProcAudit). Each change must be explained by code that ran: the card's own class,
/// a source with a tab, or a source that flashed, whose compiled code issues the game command that
/// makes that change (CodeIndex). Anything else is reported, naming the sources present whose code
/// could have done it, so a missing tab or tracker row shows up from players' games.
/// </summary>
internal sealed class PlaySnapshot
{
    public int Energy, Stars, Gold;
    public readonly Dictionary<Creature, (int Hp, int MaxHp)> Creatures = new();
    public readonly Dictionary<CardModel, (PileType Pile, int Cost, bool Upgraded, string Keywords, string Affliction, string Enchantment)> Cards = new(ReferenceEqualityComparer.Instance);
    public readonly Dictionary<RelicModel, (int Display, RelicStatus Status)> Relics = new(ReferenceEqualityComparer.Instance);

    public static PlaySnapshot? Take(Player me)
    {
        try
        {
            var s = new PlaySnapshot();
            var pcs = me.PlayerCombatState!;
            s.Energy = pcs.Energy;
            s.Stars = pcs.Stars;
            s.Gold = me.Gold;
            foreach (var c in me.Creature.CombatState!.Creatures) s.Creatures[c] = (c.CurrentHp, c.MaxHp);
            foreach (var pile in new[] { pcs.Hand, pcs.DrawPile, pcs.DiscardPile, pcs.ExhaustPile })
            foreach (var card in pile.Cards)
                s.Cards[card] = (pile.Type, SafeCost(card), card.IsUpgraded, string.Join(",", card.Keywords.OrderBy(k => k)),
                    card.Affliction?.GetType().Name ?? "", card.Enchantment?.GetType().Name ?? "");
            foreach (var r in me.Relics) s.Relics[r] = (SafeDisplay(r), r.Status);
            return s;
        }
        catch
        {
            return null;
        }
    }

    private static int SafeCost(CardModel c)
    {
        try { return c.EnergyCost.GetResolved(); } catch { return -99; }
    }

    private static int SafeDisplay(RelicModel r)
    {
        try { return r.DisplayAmount; } catch { return -99; }
    }

    // ------------------------------------------------------------------ explaining changes

    /// <summary>A kind of change and the game code that makes it (calls "Type.Method", or overridden hooks).</summary>
    private sealed record Kind(string Name, string[] Calls, string[] Overrides);

    private static readonly Kind EnergyGain = new("energy gained", new[] { "PlayerCmd.GainEnergy", "PlayerCmd.SetEnergy" }, Array.Empty<string>());
    private static readonly Kind EnergyLoss = new("energy lost", new[] { "PlayerCmd.LoseEnergy", "PlayerCmd.SetEnergy" }, Array.Empty<string>());
    private static readonly Kind StarGain = new("stars gained", new[] { "PlayerCmd.GainStars" }, Array.Empty<string>());
    private static readonly Kind StarLoss = new("stars lost", new[] { "PlayerCmd.LoseStars" }, Array.Empty<string>());
    private static readonly Kind GoldChange = new("gold", new[] { "PlayerCmd.GainGold", "PlayerCmd.LoseGold" }, Array.Empty<string>());
    private static readonly Kind Heal = new("HP gained", new[] { "CreatureCmd.Heal", "CreatureCmd.SetCurrentHp", "CreatureCmd.SetMaxAndCurrentHp" }, Array.Empty<string>());
    private static readonly Kind MaxHp = new("max HP", new[] { "CreatureCmd.GainMaxHp", "CreatureCmd.LoseMaxHp", "CreatureCmd.SetMaxHp", "CreatureCmd.SetMaxAndCurrentHp" }, Array.Empty<string>());
    private static readonly Kind Cost = new("card cost", new[] { "CardEnergyCost.Set*", "CardEnergyCost.Add*" },
        new[] { "TryModifyEnergyCostInCombat", "TryModifyEnergyCostInCombatLate", "TryModifyStarCost" });
    private static readonly Kind Upgrade = new("card upgraded", new[] { "CardCmd.Upgrade", "CardCmd.Downgrade" }, Array.Empty<string>());
    private static readonly Kind Keyword = new("card keywords", new[] { "CardCmd.ApplyKeyword", "CardCmd.RemoveKeyword" }, new[] { "TryModifyKeywordsInCombat" });
    private static readonly Kind Affliction = new("card affliction", new[] { "CardCmd.Afflict", "CardCmd.AfflictAndPreview", "CardCmd.ClearAffliction" }, Array.Empty<string>());
    private static readonly Kind Enchant = new("card enchantment", new[] { "CardCmd.Enchant" }, Array.Empty<string>());
    private static readonly Kind Move = new("card moved", new[] { "CardPileCmd.Add", "CardPileCmd.AutoPlayFromDrawPile", "CardCmd.AutoPlay" }, new[] { "ModifyCardPlayResultLocation" });

    /// <summary>
    /// Changes between <paramref name="before"/> and <paramref name="after"/> that no code that ran
    /// explains. <paramref name="explainers"/>: the card, tab sources and flashed sources (their
    /// classes); <paramref name="present"/>: every model in the fight, named as possible sources.
    /// </summary>
    public static List<string> Unexplained(PlaySnapshot before, PlaySnapshot after, CardModel played, List<CombatHistoryEntry> entries, int shuffles,
        IReadOnlyCollection<Type> explainers, IReadOnlyCollection<AbstractModel> present, Player me, ISet<string> tabbedOrTracked)
    {
        var issues = new List<string>();
        void Check(Kind k, string what, Type? alsoOwn = null)
        {
            if (explainers.Any(t => CodeIndex.Does(t, k.Calls, k.Overrides))) return;
            if (alsoOwn != null && CodeIndex.Does(alsoOwn, k.Calls, k.Overrides)) return;
            var could = present.Select(m => m.GetType()).Distinct().Where(t => CodeIndex.Does(t, k.Calls, k.Overrides)).Select(t => t.Name).OrderBy(n => n).ToList();
            issues.Add($"untracked {k.Name}: {what} (could be: {(could.Count == 0 ? "nothing present" : string.Join(", ", could))})");
        }

        // Energy and stars: what the history shows spent is the card's cost.
        var spent = entries.OfType<EnergySpentEntry>().Where(e => e.Actor == me.Creature).Sum(e => e.Amount);
        var energy = after.Energy - before.Energy + spent;
        if (energy > 0) Check(EnergyGain, $"+{energy}");
        if (energy < 0) Check(EnergyLoss, $"{energy}");
        var starsSpent = entries.OfType<StarsModifiedEntry>().Where(e => e.Actor == me.Creature && e.Amount < 0).Sum(e => -e.Amount);
        var stars = after.Stars - before.Stars + starsSpent;
        if (stars > 0) Check(StarGain, $"+{stars}");
        if (stars < 0) Check(StarLoss, $"{stars}");
        if (after.Gold != before.Gold) Check(GoldChange, $"{after.Gold - before.Gold:+#;-#;0}");

        // HP gained and max HP, on any creature alive before and after.
        foreach (var (c, (hp, maxHp)) in before.Creatures)
        {
            if (!after.Creatures.TryGetValue(c, out var a) || !c.IsAlive) continue;
            var who = c == me.Creature ? "you" : c.Monster?.GetType().Name ?? "creature";
            if (a.Hp > hp) Check(Heal, $"{who} {hp}→{a.Hp}");
            if (a.MaxHp != maxHp) Check(MaxHp, $"{who} {maxHp}→{a.MaxHp}");
        }

        // Your cards: state, and moves the history doesn't record (draws, discards, exhausts, shuffles).
        var drawn = entries.OfType<CardDrawnEntry>().Select(e => e.Card).ToHashSet(ReferenceEqualityComparer.Instance);
        var discarded = entries.OfType<CardDiscardedEntry>().Select(e => e.Card).ToHashSet(ReferenceEqualityComparer.Instance);
        var exhausted = entries.OfType<CardExhaustedEntry>().Select(e => e.Card).ToHashSet(ReferenceEqualityComparer.Instance);
        foreach (var (card, b) in before.Cards)
        {
            if (!after.Cards.TryGetValue(card, out var a)) continue; // gone (exhausted, removed): the budget's
            var own = card.GetType();
            var name = card.GetType().Name;
            if (a.Cost != b.Cost && b.Cost != -99 && a.Cost != -99) Check(Cost, $"{name} {b.Cost}→{a.Cost}", own);
            if (a.Upgraded != b.Upgraded) Check(Upgrade, name, own);
            if (a.Keywords != b.Keywords) Check(Keyword, $"{name} [{b.Keywords}]→[{a.Keywords}]", own);
            if (a.Affliction != b.Affliction) Check(Affliction, $"{name} {(b.Affliction == "" ? "none" : b.Affliction)}→{(a.Affliction == "" ? "none" : a.Affliction)}", own);
            if (a.Enchantment != b.Enchantment) Check(Enchant, $"{name} {(b.Enchantment == "" ? "none" : b.Enchantment)}→{(a.Enchantment == "" ? "none" : a.Enchantment)}", own);
            if (a.Pile != b.Pile && !ReferenceEquals(card, played))
            {
                var recorded = (a.Pile == PileType.Hand && drawn.Contains(card)) || (a.Pile == PileType.Discard && discarded.Contains(card))
                               || (a.Pile == PileType.Exhaust && exhausted.Contains(card))
                               || (shuffles > 0 && b.Pile == PileType.Discard && a.Pile == PileType.Draw);
                if (!recorded) Check(Move, $"{name} {b.Pile}→{a.Pile}", own);
            }
        }

        // Relics whose counter or status changed with nothing showing it: no tab, no tracker row, no flash.
        foreach (var (r, b) in before.Relics)
        {
            if (!after.Relics.TryGetValue(r, out var a) || a == b) continue;
            var name = r.GetType().Name;
            if (tabbedOrTracked.Contains(name) || explainers.Contains(r.GetType())) continue;
            issues.Add($"untracked relic: {name} changed ({b.Display}/{b.Status} → {a.Display}/{a.Status}) with no tab, tracker row or flash (could be: {name})");
        }
        return issues;
    }
}
