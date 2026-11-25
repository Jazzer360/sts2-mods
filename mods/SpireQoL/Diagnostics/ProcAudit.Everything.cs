using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Combat.History;
using MegaCrit.Sts2.Core.Combat.History.Entries;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using SpireQoL.Data;
using SpireQoL.Procs;
using Sts2Mods.Core.Game;
using MegaCrit.Sts2.Core.Models.Relics;

namespace SpireQoL.Diagnostics;

/// <summary>ProcAudit, part: Everything else the play changed that no code that ran explains (energy, HP, cards, relics), and shuffles.</summary>
public sealed partial class ProcAudit
{
    // ------------------------------------------------------------------ everything else

    /// <summary>
    /// Energy, stars, gold, HP, max HP, your cards and relics before and after the play (PlaySnapshot):
    /// a change no code that ran explains is reported with the sources present that could have made it.
    /// </summary>
    private static void EverythingIssues(Window window, Player me, List<CombatHistoryEntry> entries, List<AbstractModel> flashed, List<string> issues, ModelLines model)
    {
        if (window.FullBefore != null && window.FullAfter == null && window.AfterSkipped != null) model.Skip("your cards, relics and resources after the play", window.AfterSkipped);
        if (window.FullBefore == null || window.FullAfter == null) return;
        var combat = Sts2State.Combat;
        if (combat == null) return;
        var present = new List<AbstractModel>();
        present.AddRange(me.Relics);
        foreach (var creature in combat.Creatures)
        {
            present.AddRange(creature.Powers);
            if (creature.Monster != null) present.Add(creature.Monster);
        }
        foreach (var card in window.FullBefore.Cards.Keys)
        {
            present.Add(card);
            if (card.Enchantment != null) present.Add(card.Enchantment);
            if (card.Affliction != null) present.Add(card.Affliction);
        }
        var tabbed = window.Predicted.Select(tab => tab.Key.Contains(':') ? tab.Key[(tab.Key.IndexOf(':') + 1)..] : tab.Key).ToHashSet();
        // Code that ran: the card, sources with a tab, sources that flashed, cards it auto-played, cards
        // it drew (and their enchantments), sources that react to draws when it drew, and cards it exhausted that react to that.
        var autoPlayed = entries.OfType<CardPlayStartedEntry>().Where(cardPlayStartedEntry => cardPlayStartedEntry.CardPlay.IsAutoPlay).Select(cardPlayStartedEntry => cardPlayStartedEntry.CardPlay.Card).ToList();
        var drawn = entries.OfType<CardDrawnEntry>().Where(cardDrawnEntry => !cardDrawnEntry.FromHandDraw && cardDrawnEntry.Card.Owner == me).Select(cardDrawnEntry => cardDrawnEntry.Card).ToList();
        // Cards it exhausted that act when exhausted run their own AfterCardExhausted (Drum of Battle's energy); not their play's code.
        var exhaustReactors = entries.OfType<CardExhaustedEntry>().Where(cardExhaustedEntry => cardExhaustedEntry.Card.Owner == me).Select(cardExhaustedEntry => cardExhaustedEntry.Card.GetType())
            .Where(type => CodeIndex.Does(type, Array.Empty<string>(), new[] { nameof(AbstractModel.AfterCardExhausted) }));
        var drawReactors = drawn.Count == 0 ? Enumerable.Empty<AbstractModel>()
            : present.Where(otherModel => CodeIndex.Does(otherModel.GetType(), Array.Empty<string>(), new[] { "AfterCardDrawn", "AfterCardDrawnEarly" }));
        // A death in the play runs death hooks, including those of powers gone by its end (Dampen comes
        // off you when its caster dies and upgrades your cards back).
        var deathReactors = !DiedInPlay(entries) ? Enumerable.Empty<Type>()
            : present.Select(otherModel => otherModel.GetType())
                .Concat(window.PowersBefore?.Keys.Select(key => PowerTypeNamed(key.Item2)).OfType<Type>() ?? Enumerable.Empty<Type>())
                .Where(type => CodeIndex.Does(type, Array.Empty<string>(), DeathHooks));
        var explainers = present.Where(otherModel => tabbed.Contains(otherModel.GetType().Name)).Select(otherModel => otherModel.GetType())
            .Concat(flashed.Select(otherModel => otherModel.GetType())).Append(window.Card.GetType())
            .Concat(autoPlayed.Select(card => card.GetType()))
            .Concat(drawn.Select(card => card.GetType())).Concat(drawn.Where(card => card.Enchantment != null).Select(card => card.Enchantment!.GetType()))
            .Concat(drawReactors.Select(drawReactor => drawReactor.GetType()))
            .Concat(exhaustReactors)
            // Orbs the play may have evoked (channelling into full slots, evoke cards): Plasma's energy.
            .Concat(entries.OfType<OrbChanneledEntry>().Any() || CodeIndex.Does(window.Card.GetType(), new[] { "OrbCmd.Evoke*" }, Array.Empty<string>()) ? window.FullBefore.Orbs : Enumerable.Empty<Type>())
            .Concat(deathReactors)
            // Your powers a play uses up (Free Attack, Free Skill, Free Power): the costs they lowered go back up.
            .Concat(window.PowersBefore?.Keys.Where(key => key.Item1 == me.Creature && SpentByAnyPlay.Contains(key.Item2)).Select(key => PowerTypeNamed(key.Item2)).OfType<Type>() ?? Enumerable.Empty<Type>())
            .Distinct().ToList();
        var tracked = new HashSet<string>(tabbed);
        foreach (var relicCounterSpec in RelicSpecs.All) tracked.Add(relicCounterSpec.TypeName);
        tracked.UnionWith(RelicTracking.CountsForLater);
        // Random auto-plays (The Fool) can change any relic's state the way their own plays would (Ripple Basin
        // armed until an attack is played): which cards they were is a roll.
        if (UnpredictedAutoPlay(window, entries, me)) foreach (var relic in me.Relics) tracked.Add(relic.GetType().Name);
        issues.AddRange(PlaySnapshot.Unexplained(window.FullBefore, window.FullAfter, window.Card, entries, window.Shuffles, explainers, present, me, tracked));

        // ---- Cards that come back to your hand (Procs.ReturnToHand): what moved back during the play against the return
        // tabs. A card moving itself always explains its own move, so this is the only check on those tabs.
        if (window.FullBefore != null && window.FullAfter != null)
        {
            var drawnNow = entries.OfType<CardDrawnEntry>().Select(cardDrawnEntry => cardDrawnEntry.Card).ToHashSet(ReferenceEqualityComparer.Instance);
            var cameBack = window.FullAfter.Cards
                .Where(entry => ReturnToHand.Modelled.Contains(entry.Key.GetType().Name) && entry.Value.Pile == PileType.Hand && !ReferenceEquals(entry.Key, window.Card)
                             && window.FullBefore.Cards.TryGetValue(entry.Key, out var was) && was.Pile != PileType.Hand && !drawnNow.Contains(entry.Key))
                .Select(entry => entry.Key.GetType().Name).ToHashSet();
            var tabbedBack = window.Predicted.Where(tab => tab.Key.StartsWith("return:", StringComparison.Ordinal)).Select(tab => tab.Key["return:".Length..]).ToHashSet();
            var randomPlays = UnpredictedAutoPlay(window, entries, me);
            foreach (var name in cameBack.Except(tabbedBack))
            {
                if (randomPlays) model.Skip("no tab", AutoPlayReason(window, entries, "random auto-plays"), $"{name} came back to your hand");
                else issues.Add($"no tab: {name} came back to your hand");
            }
            foreach (var name in tabbedBack.Except(cameBack))
                issues.Add($"didn't fire: {name} didn't come back to your hand");
        }
    }

    // ------------------------------------------------------------------ shuffles

    /// <summary>
    /// Shuffles during the play (CardPileCmd.Shuffle prefix) against its simulation's (the Shuffle Tag and shuffle procs): at the target it
    /// was played at, a shuffle in every outcome is predicted, one in none not (decision #193).
    /// </summary>
    private static void ShuffleIssues(Window window, Player me, List<CombatHistoryEntry> entries, List<AbstractModel> flashed, List<string> issues, ModelLines model)
    {
        var prediction = window.Prediction;
        if (prediction == null) return;
        if (prediction.Sim?.AimFor(window.Target) is not { } aim || prediction.OutcomeByAim is not { } outcomes || !outcomes.TryGetValue(aim, out var outcome) || outcome.Truncated)
        {
            model.Skip("shuffle", "its simulation has no finished outcome at the target it was played at");
            return;
        }
        var shuffled = window.Shuffles > 0;
        var key = SpireQoL.Sim.SimKeys.PlayEvent(SpireQoL.Sim.CardEvent.Shuffled);
        if (shuffled ? outcome.PlayFiredAny[key] > 0 : outcome.PlayFiredEvery[key] == 0) return;
        var autoPlayed = UnpredictedAutoPlay(window, entries, me);
        var drawReactive = flashed.Any(otherModel => DrawReactive.Contains(otherModel.GetType().Name) || otherModel.GetType().Name is nameof(GremlinHorn) or nameof(UnceasingTop));
        var draws = entries.OfType<CardDrawnEntry>().Count(cardDrawnEntry => !cardDrawnEntry.FromHandDraw && cardDrawnEntry.Card.Owner == me);
        var why = $"(draws: predicted {outcome.PlayFiredEvery[SpireQoL.Sim.SimKeys.PlayEvent(SpireQoL.Sim.CardEvent.Drawn)]}-{outcome.PlayFiredAny[SpireQoL.Sim.SimKeys.PlayEvent(SpireQoL.Sim.CardEvent.Drawn)]}{(prediction.Logged(nameof(MegaCrit.Sts2.Core.Models.Powers.NoDrawPower), window.Target) is { Most: > 0 } ? ", blocked" : "")}, actual {draws})";
        if (!shuffled && window.FightEnded)
        {
            // The play's hit ended the fight (Compile Driver on the last enemy): its draws, and their shuffle, never came.
            model.Skip("shuffle", FightEndedSkip, $"didn't happen {why}");
            return;
        }
        if (autoPlayed || (shuffled && drawReactive))
        {
            model.Skip("shuffle", "after random auto-plays or a draw-reactive source", $"{(shuffled ? "happened" : "didn't happen")} {why}");
            return;
        }
        issues.Add(shuffled
            ? $"shuffle: not predicted, but the discard pile was shuffled {why}"
            : $"shuffle: predicted, but no shuffle happened {why}");
    }
}
