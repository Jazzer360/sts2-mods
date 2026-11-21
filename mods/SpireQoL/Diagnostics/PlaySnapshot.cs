using MegaCrit.Sts2.Core.Combat.History;
using MegaCrit.Sts2.Core.Combat.History.Entries;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Relics;
using MegaCrit.Sts2.Core.Models;

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
    /// <summary>Your orbs' classes (an orb a play evokes is gone by its end, but its evoke ran: Plasma's energy).</summary>
    public readonly List<Type> Orbs = new();

    public static PlaySnapshot? Take(Player me)
    {
        try
        {
            var playSnapshot = new PlaySnapshot();
            var pcs = me.PlayerCombatState!;
            playSnapshot.Energy = pcs.Energy;
            playSnapshot.Stars = pcs.Stars;
            playSnapshot.Gold = me.Gold;
            foreach (var creature in me.Creature.CombatState!.Creatures) playSnapshot.Creatures[creature] = (creature.CurrentHp, creature.MaxHp);
            foreach (var pile in new[] { pcs.Hand, pcs.DrawPile, pcs.DiscardPile, pcs.ExhaustPile })
            foreach (var card in pile.Cards)
                playSnapshot.Cards[card] = (pile.Type, SafeCost(card), card.IsUpgraded, string.Join(",", card.Keywords.OrderBy(cardKeyword => cardKeyword)),
                    card.Affliction?.GetType().Name ?? "", card.Enchantment?.GetType().Name ?? "");
            foreach (var relic in me.Relics) playSnapshot.Relics[relic] = (SafeDisplay(relic), relic.Status);
            foreach (var orb in pcs.OrbQueue?.Orbs ?? Enumerable.Empty<OrbModel>()) playSnapshot.Orbs.Add(orb.GetType());
            return playSnapshot;
        }
        catch
        {
            return null;
        }
    }

    // -99: not compared. An X cost resolves to the energy captured when it's played (CardEnergyCost.
    // CapturedXValue), which the play itself sets and clears: Whirlwind "1→0".
    private static int SafeCost(CardModel card)
    {
        try { return card.EnergyCost.CostsX ? -99 : card.EnergyCost.GetWithModifiers(CostModifiers.Local); } catch { return -99; }
    }

    private static int SafeDisplay(RelicModel relic)
    {
        try { return relic.DisplayAmount; } catch { return -99; }
    }

    // ------------------------------------------------------------------ explaining changes

    /// <summary>A kind of change and the game code that makes it (calls "Type.Method", or overridden hooks).</summary>
    private sealed record Kind(string Name, string[] Calls, string[] Overrides);

    private static readonly Kind EnergyGain = new("energy gained", new[] { "PlayerCmd.GainEnergy", "PlayerCmd.SetEnergy" }, Array.Empty<string>());
    private static readonly Kind EnergyLoss = new("energy lost", new[] { "PlayerCmd.LoseEnergy", "PlayerCmd.SetEnergy" }, Array.Empty<string>());
    private static readonly Kind StarGain = new("stars gained", new[] { "PlayerCmd.GainStars" }, Array.Empty<string>());
    private static readonly Kind StarLoss = new("stars lost", new[] { "PlayerCmd.LoseStars" }, Array.Empty<string>());
    private static readonly Kind GoldChange = new("gold", new[] { "PlayerCmd.GainGold", "PlayerCmd.LoseGold" }, Array.Empty<string>());
    private static readonly Kind Heal = new("HP gained", new[] { "CreatureCmd.Heal", "CreatureCmd.SetCurrentHp", "CreatureCmd.SetMaxAndCurrentHp", "OstyCmd.Summon*" }, Array.Empty<string>());
    private static readonly Kind MaxHp = new("max HP", new[] { "CreatureCmd.GainMaxHp", "CreatureCmd.LoseMaxHp", "CreatureCmd.SetMaxHp", "CreatureCmd.SetMaxAndCurrentHp", "OstyCmd.Summon*" }, Array.Empty<string>());
    private static readonly Kind Cost = new("card cost", new[] { "CardEnergyCost.Set*", "CardEnergyCost.Add*", "CardModel.SetToFree*" },
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
        void Check(Kind kind, string what, Type? alsoOwn = null)
        {
            if (explainers.Any(type => CodeIndex.Does(type, kind.Calls, kind.Overrides))) return;
            if (alsoOwn != null && CodeIndex.Does(alsoOwn, kind.Calls, kind.Overrides)) return;
            var could = present.Select(model => model.GetType()).Distinct().Where(type => CodeIndex.Does(type, kind.Calls, kind.Overrides)).Select(type => type.Name).OrderBy(name => name).ToList();
            issues.Add($"untracked {kind.Name}: {what} (could be: {(could.Count == 0 ? "nothing present" : string.Join(", ", could))})");
        }

        // Energy and stars: what the history shows spent is the card's cost.
        var spent = entries.OfType<EnergySpentEntry>().Where(energySpentEntry => energySpentEntry.Actor == me.Creature).Sum(energySpentEntry => energySpentEntry.Amount);
        var energy = after.Energy - before.Energy + spent;
        // The played card's own enchantment acts in its play (Sown: energy on each play until the card finishes).
        var enchantment = played.Enchantment?.GetType();
        if (energy > 0) Check(EnergyGain, $"+{energy}", enchantment);
        if (energy < 0) Check(EnergyLoss, $"{energy}", enchantment);
        var starsSpent = entries.OfType<StarsModifiedEntry>().Where(starsModifiedEntry => starsModifiedEntry.Actor == me.Creature && starsModifiedEntry.Amount < 0).Sum(starsModifiedEntry => -starsModifiedEntry.Amount);
        var stars = after.Stars - before.Stars + starsSpent;
        if (stars > 0) Check(StarGain, $"+{stars}", enchantment);
        if (stars < 0) Check(StarLoss, $"{stars}", enchantment);
        if (after.Gold != before.Gold) Check(GoldChange, $"{after.Gold - before.Gold:+#;-#;0}");

        // HP gained and max HP, on any creature alive before and after.
        foreach (var (creature, (hp, maxHp)) in before.Creatures)
        {
            if (!after.Creatures.TryGetValue(creature, out var otherCreature) || !creature.IsAlive) continue;
            var who = creature == me.Creature ? "you" : creature.Monster?.GetType().Name ?? "creature";
            if (otherCreature.Hp > hp) Check(Heal, $"{who} {hp}→{otherCreature.Hp}");
            if (otherCreature.MaxHp != maxHp) Check(MaxHp, $"{who} {maxHp}→{otherCreature.MaxHp}");
        }

        // Your cards: state, and moves the history doesn't record (draws, discards, exhausts, shuffles).
        var drawn = entries.OfType<CardDrawnEntry>().Select(cardDrawnEntry => cardDrawnEntry.Card).ToHashSet(ReferenceEqualityComparer.Instance);
        var discarded = entries.OfType<CardDiscardedEntry>().Select(cardDiscardedEntry => cardDiscardedEntry.Card).ToHashSet(ReferenceEqualityComparer.Instance);
        var exhausted = entries.OfType<CardExhaustedEntry>().Select(cardExhaustedEntry => cardExhaustedEntry.Card).ToHashSet(ReferenceEqualityComparer.Instance);
        foreach (var (card, pileCost) in before.Cards)
        {
            if (!after.Cards.TryGetValue(card, out var otherCard)) continue; // gone (exhausted, removed): the budget's
            var own = card.GetType();
            var name = card.GetType().Name;
            // The played card's cost changing is the game clearing its "this turn or until played" cost, either way
            // (CardModel → CardEnergyCost.AfterCardPlayedCleanup: Splash, Bullet Time, Bookmark lowered it; a random cost raised it).
            var playedCleanup = ReferenceEquals(card, played) && otherCard.Cost != pileCost.Cost;
            // An upgrade or downgrade changes the card's cost and keywords itself (OnUpgrade: Big Bang+
            // gains Innate): those are the upgrade's, checked as one change.
            var upgradeChanged = otherCard.Upgraded != pileCost.Upgraded;
            if (otherCard.Cost != pileCost.Cost && pileCost.Cost != -99 && otherCard.Cost != -99 && !playedCleanup && !upgradeChanged) Check(Cost, $"{name} {pileCost.Cost}→{otherCard.Cost}", own);
            if (upgradeChanged) Check(Upgrade, name, own);
            if (otherCard.Keywords != pileCost.Keywords && !upgradeChanged) Check(Keyword, $"{name} [{pileCost.Keywords}]→[{otherCard.Keywords}]", own);
            if (otherCard.Affliction != pileCost.Affliction) Check(Affliction, $"{name} {(pileCost.Affliction == "" ? "none" : pileCost.Affliction)}→{(otherCard.Affliction == "" ? "none" : otherCard.Affliction)}", own);
            if (otherCard.Enchantment != pileCost.Enchantment) Check(Enchant, $"{name} {(pileCost.Enchantment == "" ? "none" : pileCost.Enchantment)}→{(otherCard.Enchantment == "" ? "none" : otherCard.Enchantment)}", own);
            if (otherCard.Pile != pileCost.Pile && !ReferenceEquals(card, played))
            {
                var recorded = (otherCard.Pile == PileType.Hand && drawn.Contains(card)) || (otherCard.Pile == PileType.Discard && discarded.Contains(card))
                               || (otherCard.Pile == PileType.Exhaust && exhausted.Contains(card))
                               || (shuffles > 0 && otherCard.Pile == PileType.Draw && (pileCost.Pile == PileType.Discard || discarded.Contains(card)));
                if (!recorded) Check(Move, $"{name} {pileCost.Pile}→{otherCard.Pile}", own);
            }
        }

        // Relics whose counter or status changed with nothing showing it: no tab, no tracker row, no flash.
        foreach (var (relic, displayStatus) in before.Relics)
        {
            if (!after.Relics.TryGetValue(relic, out var otherRelic) || otherRelic == displayStatus) continue;
            var name = relic.GetType().Name;
            if (tabbedOrTracked.Contains(name) || explainers.Contains(relic.GetType())) continue;
            issues.Add($"untracked relic: {name} changed ({displayStatus.Display}/{displayStatus.Status} → {otherRelic.Display}/{otherRelic.Status}) with no tab, tracker row or flash (could be: {name})");
        }
        return issues;
    }
}
