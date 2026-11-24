using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Combat.History;
using MegaCrit.Sts2.Core.Combat.History.Entries;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using SpireQoL.Cards;
using SpireQoL.Sim;
using Sts2Mods.Core.Game;
using MegaCrit.Sts2.Core.Models.Powers;

namespace SpireQoL.Diagnostics;

/// <summary>ProcAudit, part: Every power that changed during the play, and every bonus a play gave up, against the tabs and the card's own code.</summary>
public sealed partial class ProcAudit
{
    // ------------------------------------------------------------------ every power

    /// <summary>Powers the simulator tracks itself: the after-play check compares them.</summary>
    private static readonly HashSet<string> SimComparedPowers = new()
    {
        nameof(PoisonPower), nameof(PlatingPower), nameof(JuggernautPower), nameof(ThornsPower), nameof(FlameBarrierPower), nameof(FeelNoPainPower),
        nameof(FlutterPower),
    };

    /// <summary>
    /// Every power on every creature, before the play against after it: a power that went down or
    /// away with nothing in the play to explain it. Explained: powers the card applies, powers that
    /// flashed (they acted: Slippery, Curl Up...), sources with a tab, Artifact when the card
    /// applies debuffs, temporary powers, the simulator's own powers (compared already), and anything on
    /// a creature that died. Increases are the budget check's ("unexplained: powers applied").
    /// </summary>
    private static void PowerChangeIssues(Window window, Player me, List<CombatHistoryEntry> entries, List<AbstractModel> flashed, List<string> issues, ModelLines model)
    {
        if (window.PowersBefore == null || window.PowersAfter == null)
        {
            model.Skip("unexplained powers", window.AfterSkipped ?? "the powers before or after the play weren't captured");
            return;
        }
        // The card's own powers, and those of cards it auto-played.
        var own = OwnPowerTypes(window.Card).Select(type => type.Name).ToHashSet();
        foreach (var cardPlayStartedEntry in entries.OfType<CardPlayStartedEntry>().Where(entry => entry.CardPlay.IsAutoPlay))
            own.UnionWith(OwnPowerTypes(cardPlayStartedEntry.CardPlay.Card).Select(type => type.Name));
        var acted = flashed.Select(otherModel => otherModel.GetType().Name).ToHashSet();
        var tabbed = window.Predicted.Select(tab => tab.Key.Contains(':') ? tab.Key[(tab.Key.IndexOf(':') + 1)..] : tab.Key).ToHashSet();
        // Artifact blocks any debuff, temporary ones included (Dark Shackles).
        var debuffs = own.Any(typeName => PowerTypeNamed(typeName) is { } type && SimReaders.IsDebuffType(type));
        var died = DiedInPlay(entries);
        foreach (var ((creature, type), before) in window.PowersBefore)
        {
            var after = window.PowersAfter.TryGetValue((creature, type), out var amountAfter) ? amountAfter : 0;
            if (after >= before) continue;
            if (!creature.IsAlive) continue;
            // Killed in the play but kept in the fight (the Waterfall Giant erupts): its powers come off at death
            // (ShouldPowerBeRemovedAfterOwnerDeath), all but the one keeping it there.
            if (entries.OfType<DamageReceivedEntry>().Any(damage => damage.Result.WasTargetKilled && damage.Result.Receiver == creature)) continue;
            if (own.Contains(type) || acted.Contains(type) || tabbed.Contains(type) || SimComparedPowers.Contains(type)) continue;
            if (creature == me.Creature && SpentByAnyPlay.Contains(type)) continue;
            if (creature == me.Creature && SpentByAttack(type) && window.Card.Type == CardType.Attack) continue;
            // A double-play source this play used (Burst, Duplication: ModifyCardPlayCount, then a stack goes) when the card
            // played more than once: its tag is Keyword Tags' (the game's own play-count query), not a proc rule.
            if (creature == me.Creature && PowerTypeNamed(type) is { } countSource
                && CodeIndex.Does(countSource, Array.Empty<string>(), new[] { "ModifyCardPlayCount" })
                && entries.OfType<CardPlayStartedEntry>().Any(cardPlayStartedEntry => cardPlayStartedEntry.CardPlay.Card == window.Card && cardPlayStartedEntry.CardPlay.PlayCount > 1)) continue;
            if (creature == me.Creature && Stats.Contains(type)
                && acted.Concat(tabbed).Any(source => SourceEffects.TryGetValue(source, out var effects) && effects.Contains(Effect.Power))) continue;
            // A tabbed or flashed power on that creature removes it (Plow takes the Ceremonial Beast's Strength
            // as it stuns her: PowerCmd.Remove; the index has no type arguments).
            // (Read from the powers before the play: Plow removes itself too.)
            if (window.PowersBefore.Keys.Any(key => key.Item1 == creature && (acted.Contains(key.Item2) || tabbed.Contains(key.Item2))
                                             && PowerTypeNamed(key.Item2) is { } src
                                             && CodeIndex.Does(src, new[] { "PowerCmd.Remove" }, Array.Empty<string>()))) continue;
            if (creature.IsEnemy && BaselineHasIt(window, type, creature)) continue;
            // A power that counts down on each hit through its owner's block (Slumber, Flutter: AfterDamageReceived →
            // PowerCmd.Decrement), and this play got damage through to it.
            if (entries.OfType<DamageReceivedEntry>().Any(damage => damage.Result.Receiver == creature && damage.Result.UnblockedDamage > 0)
                && PowerTypeNamed(type) is { } counted
                && CodeIndex.Does(counted, new[] { "PowerCmd.Decrement" }, Array.Empty<string>())
                && CodeIndex.Does(counted, Array.Empty<string>(), new[] { "AfterDamageReceived" })) continue;
            if (type == nameof(ArtifactPower) && debuffs) continue;
            if (creature.Powers.FirstOrDefault(power => power.GetType().Name == type) is ITemporaryPower) continue;
            // A death in the play: powers that react to it (Dampen comes off you when its caster dies).
            if (died && PowerTypeNamed(type) is { } otherType && CodeIndex.Does(otherType, Array.Empty<string>(), DeathHooks)) continue;
            var who = creature == me.Creature ? "you" : creature.Monster?.GetType().Name ?? "creature";
            issues.Add($"power went down: {type} on {who} {before}→{after}, with nothing in the play to explain it");
        }
    }

    /// <summary>
    /// The end-of-turn forecast without this play already has this simulator-tab power on <paramref name="enemy"/> going
    /// off (stunned for Shriek, Plow, Burrowed, Flutter; woken for Asleep), so its tab rule rightly showed nothing.
    /// </summary>
    private static bool BaselineHasIt(Window window, string power, Creature enemy)
    {
        if (!SimTabSources.Contains(power) || window.Prediction?.Sim is not { } sim || !sim.Ids.TryGetValue(enemy, out var id)) return false;
        try
        {
            var baseline = SimEngine.Run(sim.Start);
            return power switch
            {
                nameof(ShriekPower) or nameof(PlowPower) or nameof(BurrowedPower) or nameof(FlutterPower) => baseline.StunnedInAll(id),
                nameof(AsleepPower) => baseline.UnblockedMax(id) >= 1,
                _ => false,
            };
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Crossed tabs for giving up a relic's bonus, which the game doesn't flash (decision #117), held to what it shows instead, both
    /// ways (a tab when the bonus wasn't given up, or the bonus given up with no tab):
    /// <list type="bullet">
    /// <item>the relic going dark (ActedSeen.StatusCleared): Art of War and Ripple Basin on the turn's first Attack, Pael's Eye on
    /// its first card, each clearing its Active status in the play's hooks;</item>
    /// <item>no energy left (ActedSeen.NoEnergyLeft): Pael's Tears, whose bonus needs energy left as your turn ends; the tab says
    /// the play spends energy and leaves none, so it's held to the energy spent in the play and what's left once it's over.</item>
    /// </list>
    /// </summary>
    private static void GivenUpIssues(Window window, Player me, List<CombatHistoryEntry> entries, Dictionary<string, TabSpec> sourceOf,
        List<string> issues, ModelLines model)
    {
        if (window.FullBefore == null || window.FullAfter == null)
        {
            model.Skip("given up", window.AfterSkipped ?? "your relics before or after the play weren't captured");
            return;
        }
        foreach (var (relic, (_, statusBefore)) in window.FullBefore.Relics)
        {
            var name = relic.GetType().Name;
            if (SimModels.Get(name) is not { Harmful: true } simModel || simModel.Seen == ActedSeen.Flash) continue;
            var tabbed = sourceOf.TryGetValue(name, out var tab) && tab.Crossed;
            bool givenUp;
            string what;
            if (simModel.Seen == ActedSeen.StatusCleared)
            {
                var statusAfter = window.FullAfter.Relics.TryGetValue(relic, out var otherRelic) ? otherRelic.Status : statusBefore;
                givenUp = statusBefore == MegaCrit.Sts2.Core.Entities.Relics.RelicStatus.Active
                          && statusAfter != MegaCrit.Sts2.Core.Entities.Relics.RelicStatus.Active;
                what = givenUp ? "it went dark" : statusBefore == MegaCrit.Sts2.Core.Entities.Relics.RelicStatus.Active ? "it stayed lit" : "it was already dark";
            }
            else if (simModel.Seen == ActedSeen.CounterPassedLimit)
            {
                // Pocketwatch: its counter (cards this turn) went from within its limit to past it.
                var limit = Sts2State.Var(relic, simModel.LimitVar ?? "", int.MaxValue);
                var before = window.FullBefore.Relics[relic].Display;
                var after = window.FullAfter.Relics.TryGetValue(relic, out var otherRelic) ? otherRelic.Display : before;
                givenUp = before <= limit && after > limit;
                what = $"its count went {before} → {after}, limit {limit}";
            }
            else
            {
                var spent = entries.OfType<EnergySpentEntry>().Where(energySpentEntry => energySpentEntry.Actor == me.Creature).Sum(energySpentEntry => energySpentEntry.Amount);
                givenUp = spent > 0 && window.FullAfter.Energy <= 0;
                what = $"spent {spent}, {window.FullAfter.Energy} energy left";
            }
            if (tabbed == givenUp) continue;
            // The fight's cleanup clears the relics' status as the play ends it (AfterCombatEnd: Status = Normal).
            if (window.FightEnded) { model.Skip(tabbed ? "didn't fire" : "no tab", FightEndedSkip, name); continue; }
            // Cards the play auto-played at random (Lost Lute, The Fool) can be the turn's first Attack, or spend energy.
            if (UnpredictedAutoPlay(window, entries, me)) { model.Skip(tabbed ? "didn't fire" : "no tab", AutoPlayReason(window, entries, "random auto-plays"), name); continue; }
            issues.Add(tabbed ? $"didn't fire: {name} (crossed: {what})" : $"no tab: {name} gave up its bonus ({what})");
        }
    }

    /// <summary>The drawn-card chances shown and what happened, over the session (decision #119): a report once if they drift apart.</summary>
    private static readonly OddsTally DrawnOdds = new();
    private static bool _drawnDriftReported;

    /// <summary>
    /// A card that acts on what it draws (decision #119): what its draw gave (Escape Plan: a Skill or not; Pillage: the Attacks it
    /// drew; Scrape: the cards it drew that cost 0), read from the play's draws, must be an outcome its simulation had; the chance its
    /// tab showed is logged with what happened ([audit] [odds]) and added up over the session, as the death chances are.
    /// </summary>
    private static void DrawnIssues(Window window, Player me, List<CombatHistoryEntry> entries, List<string> issues, ModelLines model)
    {
        var name = window.Card.GetType().Name;
        if (SimAdapter.DrawnCardReaderOf(window.Card.GetType()) is not { } reader) return; // a card that doesn't act on what it draws
        if (window.Prediction?.Drawn is not { } distribution)
        {
            model.Skip("drawn", "no chances worked out for what it draws (its simulation didn't finish)", name);
            return;
        }
        if (window.FightEnded) { model.Skip("drawn", FightEndedSkip, name); return; }
        if (UnpredictedAutoPlay(window, entries, me)) { model.Skip("drawn", AutoPlayReason(window, entries, "random auto-plays"), name); return; }
        if (window.Prediction.SimIncomplete || window.Prediction.Unmodelled.Count > 0) { model.Skip("drawn", "its simulation stopped at a cap or met something not modelled", name); return; }
        var drawn = entries.OfType<CardDrawnEntry>().Where(cardDrawnEntry => !cardDrawnEntry.FromHandDraw && cardDrawnEntry.Card.Owner == me).Select(cardDrawnEntry => cardDrawnEntry.Card).ToList();
        static bool Free(CardModel card)
        {
            try { return !card.EnergyCost.CostsX && card.EnergyCost.GetWithModifiers(CostModifiers.All) == 0; } catch { return false; }
        }
        var actual = reader.What == SimAdapter.DrawnCardReader.Reads.Cost ? drawn.Count(Free)
            : reader.Counts ? drawn.Count(card => SimReaders.TypeOf(card) == reader.Type)
            : drawn.Count > 0 && SimReaders.TypeOf(drawn[0]) == reader.Type ? 1 : 0;
        if (!distribution.TryGetValue(actual, out var share) || share <= 0)
        {
            issues.Add($"drawn: {name} gave {actual}, which its simulation had no chance of ({string.Join(", ", distribution.Select(entry => $"{entry.Key}: {entry.Value:0.###}"))})");
            return;
        }
        // The chance shown, and whether it happened.
        double likelihood;
        bool happened;
        if (reader.Counts)
        {
            var (n, chance) = DrawnChance.MostLikely(distribution);
            (likelihood, happened) = (chance, actual == n);
        }
        else (likelihood, happened) = (DrawnChance.ChanceOfAny(distribution), actual > 0);
        if (likelihood <= 0 || likelihood >= 1) return; // certain either way: the check above holds it
        ModEntry.Log.Detail($"[audit] [odds] {name}: predicted {new SimChance(likelihood, likelihood, Seen: true).Text()}, {(happened ? "happened" : "didn't")} (gave {actual})");
        DrawnOdds.Add(new SimChance(likelihood, likelihood, Seen: true), happened);
        if (!DrawnOdds.Drifted || _drawnDriftReported) return;
        _drawnDriftReported = true;
        Sts2Mods.Core.Diagnostics.ErrorReporter.Instance?.Mismatch("odds", $"Drawn-card odds: what happened is far from the chances shown ({DrawnOdds.Text()})",
            new[] { "drawn-odds" }, $"Over this session, the chances on Escape Plan, Pillage and Scrape tabs don't match what their draws gave: {DrawnOdds.Text()}. " +
            "Each is logged as [audit] [odds] in the game log.", new Dictionary<string, string>(), new Dictionary<string, string> { ["check"] = "odds" });
    }

    /// <summary>
    /// The play auto-played a card of yours the simulation doesn't follow: any but the one you chose, and a Strike Hellraiser
    /// played as the play drew it, which the simulator plays too (decision #121) when nothing in it went unmodelled.
    /// </summary>
    private static bool UnpredictedAutoPlay(Window window, List<CombatHistoryEntry> entries, Player me)
        => entries.OfType<CardPlayStartedEntry>().Any(cardPlayStartedEntry => cardPlayStartedEntry.CardPlay.IsAutoPlay && cardPlayStartedEntry.CardPlay.Card.Owner == me && !ReferenceEquals(cardPlayStartedEntry.CardPlay.Card, window.Chosen)
            && !HellraiserDrew(window, entries, me, cardPlayStartedEntry.CardPlay.Card));

    /// <summary>
    /// Why an auto-play's effects aren't checked: when every card auto-played (other than the pick you chose) is one you picked for the
    /// play to discard (a Sly card playing itself), it's what your pick caused. A pick is a card, never a particular one (decision #92):
    /// what only that card does isn't predicted, so it's excused like a roll and counted under its own reason. Otherwise
    /// <paramref name="otherwise"/>.
    /// </summary>
    private static string AutoPlayReason(Window window, List<CombatHistoryEntry> entries, string otherwise)
    {
        if (!Procs.Programs.CardEffects.DiscardsOnlyPicks(window.Card.GetType())) return otherwise;
        var discarded = entries.OfType<CardDiscardedEntry>().Select(cardDiscardedEntry => cardDiscardedEntry.Card).ToHashSet(ReferenceEqualityComparer.Instance);
        var autoPlayed = entries.OfType<CardPlayStartedEntry>()
            .Where(cardPlayStartedEntry => cardPlayStartedEntry.CardPlay.IsAutoPlay && !ReferenceEquals(cardPlayStartedEntry.CardPlay.Card, window.Chosen)).ToList();
        return autoPlayed.Count > 0 && autoPlayed.All(cardPlayStartedEntry => discarded.Contains(cardPlayStartedEntry.CardPlay.Card)) ? PickPlayedItself : otherwise;
    }

    /// <summary>The skip reason for what a card you picked did itself (a Sly card you discarded playing itself).</summary>
    internal const string PickPlayedItself = "the card you picked played itself (Sly)";

    /// <summary>A Strike drawn in the play while you have Hellraiser, with a simulation that modelled everything (decision #121).</summary>
    private static bool HellraiserDrew(Window window, List<CombatHistoryEntry> entries, Player me, CardModel card)
        => window.Prediction is { Unmodelled.Count: 0 } && me.Creature.HasPower<HellraiserPower>() && card.Tags.Contains(CardTag.Strike)
           && entries.OfType<CardDrawnEntry>().Any(cardDrawnEntry => ReferenceEquals(cardDrawnEntry.Card, card));

    private static readonly string[] DeathHooks = { "AfterDeath", "BeforeDeath" };

    private static bool DiedInPlay(List<CombatHistoryEntry> entries) => entries.OfType<DamageReceivedEntry>().Any(damage => damage.Result.WasTargetKilled);

    /// <summary>A creature killed in the play had a power (before it) whose death hook applies powers (Possess Strength).</summary>
    private static bool DeathGivesPowers(Window window, List<CombatHistoryEntry> entries)
    {
        if (window.PowersBefore == null) return false;
        var killed = entries.OfType<DamageReceivedEntry>().Where(damage => damage.Result.WasTargetKilled).Select(damage => damage.Result.Receiver).ToHashSet();
        return killed.Count > 0 && window.PowersBefore.Keys.Any(key => killed.Contains(key.Item1) && PowerTypeNamed(key.Item2) is { } type
            && CodeIndex.Does(type, Array.Empty<string>(), DeathHooks) && CodeIndex.Does(type, new[] { "PowerCmd.Apply*" }, Array.Empty<string>()));
    }

    /// <summary>The game's power class with this name (powers are recorded by name before the play).</summary>
    private static Type? PowerTypeNamed(string name) => typeof(ArtifactPower).Assembly.GetType(typeof(ArtifactPower).Namespace + "." + name);
}
