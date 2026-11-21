using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using SpireQoL.Data;

namespace SpireQoL.Features;

/// <summary>
/// The checks for card relics outside fights (docs/CARD_ODDS.md phase 4, decision #63):
///  - Card rules: after an event option or a card reward pick, each card that joined your deck against the rules of the relics
///    that change cards (the eggs, Fresnel Lens). Every card joining the deck, transforms included, goes through
///    Hook.ModifyCardBeingAddedToDeck, so a card the rule says the relic changes but that arrived unchanged is a wrong rule:
///    reported (<c>card-rule</c>). A changed card the rule says it doesn't change is only logged (the card may have come
///    upgraded or enchanted some other way: a reward's upgrade roll, Glitter).
///  - Odds: for each icon with a chance, whether one of its cards came up (among the cards offered while the option ran, or
///    the cards it added). Logged only (<c>[audit] [odds]</c>): a chance can't be checked on one choice, only across many.
/// </summary>
public sealed partial class RunRemindersFeature
{
    private static void CheckCardRules(Player me, List<CardModel> added, bool givesUpgraded, ICollection<RelicModel>? shown, string where)
    {
        var cards = added.Where(card => card.Type != CardType.Curse).ToList();
        if (cards.Count == 0) return;
        foreach (var relic in me.Relics)
        {
            var name = relic.GetType().Name;
            if (CardAffinity.For(name) is not { Kind: AffinityKind.Modifies, Shows: { } shows } rule) continue;
            // Cards the option upgrades itself (Astrolabe) can't tell an egg's upgrade apart.
            if (givesUpgraded && rule.Change == CardChange.Upgrade) continue;
            foreach (var card in cards)
            {
                // A card that arrived with another enchantment (Glitter, an event's) can't take Fresnel Lens' Nimble.
                if (rule.Change == CardChange.Enchant && card.Enchantment != null && !rule.Shows!(card)) continue;
                var canonical = card.IsCanonical ? card : card.CanonicalInstance;
                bool predicted, changed;
                try
                {
                    predicted = CardAffinity.AppliesTo(rule, canonical);
                    changed = shows(card);
                }
                catch (Exception ex)
                {
                    // The rule or the card's state couldn't be read: say so, counted, rather than pass it.
                    ModEntry.Log.Warn($"[audit] [card-rule] {where}: {name} on {card.GetType().Name}: couldn't be checked ({ex.Message})");
                    Telemetry.Audits.Skipped(Telemetry.Audits.Reminders, "card rule: couldn't read the card or the rule", $"{name} on {card.GetType().Name}");
                    continue;
                }
                var what = $"{where}: {name} on {card.GetType().Name}: rule says {(predicted ? "changes" : "doesn't change")}, card {(changed ? "changed" : "unchanged")}"
                    + (shown != null ? $", icon {(shown.Contains(relic) ? "shown" : "not shown")}" : "");
                if (predicted == changed)
                {
                    ModEntry.Log.Info($"[audit] [card-rule] {what}, matched");
                    Telemetry.Audits.Done(Telemetry.Audits.Reminders, true, "card rule");
                    continue;
                }
                ModEntry.Log.Info($"[audit] [card-rule] {what}");
                // The rule said no change and the card changed: logged only (something else may have changed it), and counted.
                if (!predicted) Telemetry.Audits.Skipped(Telemetry.Audits.Reminders, "card rule: a card changed that the rule says it doesn't", $"{name} on {card.GetType().Name}");
                else Telemetry.Audits.Done(Telemetry.Audits.Reminders, false, "card-rule");
                if (predicted && !changed)
                {
                    Sts2Mods.Core.Diagnostics.ErrorReporter.Instance?.Mismatch("reminders", $"{name}'s card rule says it changes {card.GetType().Name}, but it arrived unchanged",
                        new[] { "card-rule", name, card.GetType().Name }, what);
                }
            }
        }
    }

    /// <summary>Whether a card that came up (offered as it was, or added) is one a relic's icon was about.</summary>
    private static bool CameUp(CardAffinityRule rule, CardModel card, List<CardAffinityRule> held, bool givesUpgraded)
    {
        var canonical = card.IsCanonical ? card : card.CanonicalInstance;
        // A modifier is judged on the card before it changed it; a boost on the card as you got it, or as a modifier would change it.
        if (rule.Kind == AffinityKind.Modifies) return CardAffinity.AppliesWith(rule, canonical, held, givesUpgraded);
        return CardAffinity.AppliesTo(rule, card) || CardAffinity.Made(card).Any(otherCard => CardAffinity.AppliesTo(rule, otherCard))
            || CardAffinity.AppliesWith(rule, canonical, held, givesUpgraded);
    }

    private static void LogOdds(Window window, List<CardModel> added)
    {
        if (!CardsRead.TryGetValue(window.Option, out var read)) return;
        var held = Held(window.Me);
        foreach (var relic in window.Shown)
        {
            if (CardAffinity.For(relic.GetType().Name) is not { } rule) continue;
            (double Low, double High)? chance;
            try { chance = read.Chance(window.Me, card => CardAffinity.AppliesWith(rule, card, held, read.GivesUpgraded)); }
            catch { continue; }
            if (chance is not { } lowHigh || lowHigh.Low >= 1) continue;
            var seen = window.Offered.Concat(added).ToList();
            var cameUp = seen.Any(card => CameUp(rule, card, held, read.GivesUpgraded));
            ModEntry.Log.Info($"[audit] [odds] {window.Option.TextKey} {relic.GetType().Name}: predicted {lowHigh.Low:P0}"
                + (lowHigh.High > lowHigh.Low ? $"–{lowHigh.High:P0}" : "") + $", came up: {(cameUp ? "yes" : "no")} ({window.Offered.Count} offered, {added.Count} added)");
        }
    }
}
