using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Combat.History;
using MegaCrit.Sts2.Core.Combat.History.Entries;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using SpireQoL.Cards;
using SpireQoL.Sim;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.Models.Relics;

namespace SpireQoL.Diagnostics;

/// <summary>ProcAudit, part: A play's tabs against what the game did: procs, block, damage, draws and the rest of what the card itself does.</summary>
public sealed partial class ProcAudit
{
    private static List<string> Compare(Window window, Player me, List<CombatHistoryEntry> entries, List<AbstractModel> flashed, out ModelLines model)
    {
        var issues = new List<string>();
        var modelLines = new ModelLines();
        model = modelLines;
        // Random outcomes (several enemies): only effects the history shows a random hit caused are
        // excused. Random hits = damage to enemies with no card source (procs, orbs) or from this card
        // when it targets a random enemy.
        var random = window.Facts?.RandomOutcome == true;
        var randomHits = !random
            ? new List<DamageReceivedEntry>()
            : entries.OfType<DamageReceivedEntry>()
                .Where(damage => damage.Result.Receiver.IsEnemy
                            && (damage.CardSource == null || (window.Card.TargetType == TargetType.RandomEnemy && ReferenceEquals(damage.CardSource, window.Card))))
                .ToList();
        var randomKill = randomHits.Any(damage => damage.Result.WasTargetKilled);
        bool RandomlyHit(Creature creature) => randomHits.Any(damage => damage.Result.Receiver == creature);
        var sourceOf = new Dictionary<string, TabSpec>();
        foreach (var tab in window.Predicted)
        {
            var colon = tab.Key.IndexOf(':');
            if (colon > 0) sourceOf[tab.Key[(colon + 1)..]] = tab;
        }

        // ---- sources that fired with no tab (missing rules)
        foreach (var otherModel in flashed.Distinct())
        {
            if (sourceOf.ContainsKey(otherModel.GetType().Name) || Modifiers.Contains(otherModel.GetType().Name) || TeammateOnly.Contains(otherModel.GetType().Name)) continue;
            // A power or relic that only changes numbers (Fasten's block on Defends): the play's block, damage and HP checks cover it.
            // One with its own tab (The Boot raising a small hit) is still checked for it.
            if (CodeIndex.OnlyModifies(otherModel.GetType()) && SimModels.Get(otherModel.GetType().Name)?.Tab == null) continue;
            if (LaterPlayOnly.Contains(otherModel.GetType().Name))
            {
                modelLines.Add($"{otherModel.GetType().Name} fired (it changes a card for a later play: no tab by design)");
                continue;
            }
            // A double-play source (Throwing Axe flashes in AfterModifyingCardPlayCount) when the card did play more
            // than once: its tab is Card Indicators' (the game's own play-count query), not a proc rule.
            if (CodeIndex.Does(otherModel.GetType(), Array.Empty<string>(), new[] { "ModifyCardPlayCount" })
                && entries.OfType<CardPlayStartedEntry>().Any(cardPlayStartedEntry => cardPlayStartedEntry.CardPlay.Card == window.Card && cardPlayStartedEntry.CardPlay.PlayCount > 1)) continue;
            // A random-target hit or kill (several enemies) can set off enemy powers and kill relics
            // we can't predict: noted, not reported.
            var causedByRandom = otherModel switch
            {
                PowerModel { Owner.IsEnemy: true } power => RandomlyHit(power.Owner) || randomKill,
                // HP lost to an enemy's Thorns that a random hit set off.
                _ when HpLossReactive.Contains(otherModel.GetType().Name) => entries.OfType<DamageReceivedEntry>()
                    .Any(damage => damage.Result.Receiver == me.Creature && damage.Dealer is { IsEnemy: true } dealer && RandomlyHit(dealer)),
                _ => KillReactive.Contains(otherModel.GetType().Name) && randomKill,
            };
            if (causedByRandom)
            {
                // Random targets are branches (decision #95): excused when some outcome of the simulation set it off; reported when none did.
                var had = SimHadIt(window, otherModel);
                if (had == true)
                {
                    modelLines.Add($"{otherModel.GetType().Name} fired after a random hit or kill (some outcomes set it off: a roll decided it)");
                    continue;
                }
                if (had == null)
                {
                    modelLines.Skip("no tab", "fired after a random hit or kill; no simulation to tell", otherModel.GetType().Name);
                    continue;
                }
                issues.Add($"no tab: {otherModel.GetType().Name} fired (after a random hit or kill, but no outcome of the simulation set it off)");
                continue;
            }
            // A simulator tab shows only when the play changes the outcome; what your end of turn sets off anyway
            // (a Lightning orb's hit stunning a Shriek enemy) gets no tab, though this play set it off first.
            if (otherModel is PowerModel { Owner.IsEnemy: true } otherPower && BaselineHasIt(window, otherPower.GetType().Name, otherPower.Owner))
            {
                modelLines.Add($"{otherModel.GetType().Name} fired on this play, but your end of turn sets it off anyway (no tab by design)");
                continue;
            }
            // Cards the play auto-plays at random (The Fool: random cards from your hand) set off what their own
            // plays would: which cards is a roll.
            if (UnpredictedAutoPlay(window, entries, me))
            {
                modelLines.Skip("no tab", "fired during random auto-plays", otherModel.GetType().Name);
                continue;
            }
            if (DrawReactive.Contains(otherModel.GetType().Name) && entries.OfType<CardDrawnEntry>().Any(cardDrawnEntry => !cardDrawnEntry.FromHandDraw && cardDrawnEntry.Card.Owner == me))
            {
                // Draws are branches (decisions #90, #95): excused when some outcome of the simulation set it off; reported when none did.
                var had = SimHadIt(window, otherModel);
                if (had == false)
                {
                    issues.Add($"no tab: {otherModel.GetType().Name} fired (on a drawn card, but no outcome of the simulation set it off)");
                    continue;
                }
                if (had == true) modelLines.Add($"{otherModel.GetType().Name} fired on a drawn card (some outcomes set it off: the draw decided it)");
                else modelLines.Skip("no tab", "fired on a drawn card; no simulation to tell", otherModel.GetType().Name);
                sourceOf[otherModel.GetType().Name] = new TabSpec("proc:" + otherModel.GetType().Name, Icon: null);
                continue;
            }
            // The play ended the fight: a proc with a rule fires with nothing left to hit (Violent Chord after the
            // killing blow), so its rule rightly showed no tab.
            if (window.FightEnded && SpireQoL.Menu.SettingsCatalog.Sources.Any(sourceEntry => sourceEntry.Type == otherModel.GetType().Name && sourceEntry.Tag != null))
            {
                modelLines.Add($"{otherModel.GetType().Name} fired as the play ended the fight (nothing left for it to act on)");
                continue;
            }
            issues.Add($"no tab: {otherModel.GetType().Name} fired");
        }

        // ---- relic tabs whose relic didn't fire (crossed ones mean "this breaks it": no flash)
        foreach (var (name, otherTab) in sourceOf)
        {
            if (otherTab.Crossed || !otherTab.Key.StartsWith("proc:", StringComparison.Ordinal) || EndOfTurnRelicTabs.Contains(name) || SilentRelics.Contains(name)) continue;
            // The play ended the fight: the game stops at the killing hit, so a relic that acts after it (Bone Flute after
            // Osty's attack) never gets to.
            if (window.FightEnded) continue;
            // An aimed tab (The Boot: only at a target whose block leaves a small hit) is held to the target played at: a flash where
            // every outcome there sets it off, none where no outcome does.
            if (otherTab.Aimed)
            {
                if (CheckedAim(window) is not { } aim || window.Facts?.FiresEveryByAim?.TryGetValue(aim, out var everyHere) != true
                    || window.Facts.FiresAnyByAim?.TryGetValue(aim, out var anyHere) != true)
                {
                    modelLines.Skip("didn't fire", "an aimed tab, with no complete simulation of the target played at", name);
                    continue;
                }
                var flashedHere = flashed.Any(otherModel => otherModel.GetType().Name == name);
                if (everyHere!.Any(type => type.Name == name))
                {
                    if (!flashedHere) issues.Add($"didn't fire: {name} (at the target played at)");
                }
                else if (!anyHere!.Any(type => type.Name == name) && flashedHere)
                    issues.Add($"no tab: {name} fired (its tab aims at other targets)");
                continue;
            }
            // Razor Tooth upgrades the played card without flashing (RazorTooth.AfterCardPlayed: no Flash call): its
            // effect is checked instead (CardCmd.Upgrade does nothing once the fight is ending).
            if (name == nameof(RazorTooth))
            {
                if (window.UpgradeBefore < 0 || window.FightEnded) continue;
                if (window.Card.CurrentUpgradeLevel <= window.UpgradeBefore) issues.Add($"didn't fire: {name} (the card wasn't upgraded)");
                continue;
            }
            var players = me.Creature.CombatState?.Players ?? (IReadOnlyList<Player>)new[] { me };
            if (players.Any(player => player.Relics.Any(relic => relic.GetType().Name == name)) && !flashed.Any(otherModel => otherModel.GetType().Name == name))
                issues.Add($"didn't fire: {name}");
        }

        GivenUpIssues(window, me, entries, sourceOf, issues, modelLines);

        // ---- Tabbed sources every outcome of the play sets off (decision #95): powers included, crossed or not, when their code
        // flashes. Relics are checked above (their plain tabs); a source that changes nothing visible doesn't flash.
        if (!window.FightEnded && CheckedAim(window) is { } everyAim && window.Facts?.FiresEveryByAim?.TryGetValue(everyAim, out var every) == true)
        {
            foreach (var type in every!)
            {
                // Powers, and relics whose crossed tab a model fires when it acts and flashes (Biiig Hug); plain relic tabs are
                // checked above, and crossed ones that give up a bonus by what the game shows instead (GivenUpIssues).
                var flashingRelic = typeof(RelicModel).IsAssignableFrom(type) && SimModels.Get(type.Name) is { Harmful: true, Seen: ActedSeen.Flash };
                if (!(typeof(PowerModel).IsAssignableFrom(type) || flashingRelic) || LaterPlayOnly.Contains(type.Name)) continue;
                if (!CodeIndex.FlashesOutsideAmountHooks(type)) continue;
                if (flashed.Any(otherModel => otherModel.GetType() == type)) continue;
                issues.Add($"didn't fire: {type.Name} (every outcome of this play sets it off)");
            }
        }

        // ---- Powers a play spends without flashing, so their tabs are checked against the amount: Stretch (block cards,
        // StretchPower.AfterCardPlayed), Vigor and Gigantification (attacks, AfterAttack). Cards the play auto-played (Lost
        // Lute) can spend them too; a card that gives you the same power itself can leave it where it was.
        if (window.PowersBefore != null && window.PowersAfter != null && !window.FightEnded
            && !entries.OfType<CardPlayStartedEntry>().Any(cardPlayStartedEntry => cardPlayStartedEntry.CardPlay.IsAutoPlay))
        {
            var ownTypes = OwnPowerTypes(window.Card).Select(type => type.Name).ToHashSet();
            foreach (var spent in new[] { "StretchPower", nameof(VigorPower), nameof(GigantificationPower) })
            {
                if (!window.PowersBefore.TryGetValue((me.Creature, spent), out var before) || before <= 0) continue;
                if (spent != "StretchPower" && ownTypes.Contains(spent)) continue;
                var after = window.PowersAfter.TryGetValue((me.Creature, spent), out var spentAfter) ? spentAfter : 0;
                var tabbed = sourceOf.ContainsKey(spent);
                // Spent: the history shows it going down, even when the card gives it back (Cho Ki re-applies the
                // Stretch it had, so the amount ends where it started).
                var wasSpent = after < before || entries.OfType<PowerReceivedEntry>()
                    .Any(powerReceivedEntry => powerReceivedEntry.Actor == me.Creature && powerReceivedEntry.Power.GetType().Name == spent && powerReceivedEntry.Amount < 0);
                if (tabbed && !wasSpent) issues.Add($"didn't fire: {spent} (still {after})");
                if (!tabbed && wasSpent) issues.Add($"no tab: {spent} spent ({before}→{after})");
            }
            // Free card powers: a "free:" tab (plain or crossed) exactly when the play takes a charge.
            foreach (var spent in SpireQoL.Procs.FreeCards.Powers)
            {
                if (!window.PowersBefore.TryGetValue((me.Creature, spent), out var before) || before <= 0) continue;
                var after = window.PowersAfter.TryGetValue((me.Creature, spent), out var spentAfter) ? spentAfter : 0;
                var tabbed = window.Predicted.Any(tab => tab.Key == SpireQoL.Procs.FreeCards.Prefix + spent);
                // Spent: the history shows a charge going, even when the card gives one back (Unrelenting spends Free Attack and
                // applies it again, so the amount ends where it started).
                var wasSpent = after < before || entries.OfType<PowerReceivedEntry>()
                    .Any(powerReceivedEntry => powerReceivedEntry.Actor == me.Creature && powerReceivedEntry.Power.GetType().Name == spent && powerReceivedEntry.Amount < 0);
                if (tabbed && !wasSpent) issues.Add($"didn't fire: {spent} (still {after})");
                if (!tabbed && wasSpent && !ownTypes.Contains(spent)) issues.Add($"no tab: {spent} spent ({before}→{after})");
            }
        }

        // ---- block from procs (exact)
        var procBlock = entries.OfType<BlockGainedEntry>()
            .Where(blockGainedEntry => blockGainedEntry.Actor == me.Creature && blockGainedEntry.CardPlay == null)
            .Sum(blockGainedEntry => blockGainedEntry.Amount);
        // Frost evokes: the simulator's block for the target played at (every orb the play evokes, the ones it channels too); the
        // card's own count of the orbs it evokes without a complete simulation.
        var frostBlock = window.Facts?.OrbBlock ?? 0;
        var frostSpread = 0; // more Frost block some outcomes give (a random evoke deciding which orb goes)
        if (CheckedAim(window) is { } blockAim && window.Facts?.FrostBlockByAim?.TryGetValue(blockAim, out var frost) == true)
        {
            frostBlock = frost.Min;
            frostSpread = frost.Max - frost.Min;
        }
        var predictedBlock = window.Predicted.Where(tab => tab.Kind == TabAmount.Block).Sum(tab => tab.Amount) + frostBlock;
        // The play ended the fight: the history stops at the killing hit, so less than predicted is expected.
        void Shortfall(string what, string predicted, int actual) =>
            modelLines.Skip(what, FightEndedSkip + ": the game logs nothing from the killing hit on", $"predicted {predicted}, actual {actual}");
        if (procBlock < predictedBlock && window.FightEnded) Shortfall("block", predictedBlock.ToString(), procBlock);
        else if (frostSpread > 0 && procBlock >= predictedBlock && procBlock <= predictedBlock + frostSpread)
            modelLines.Add($"block {procBlock}: Frost evokes give {frostBlock} to {frostBlock + frostSpread} by outcome");
        // Cards auto-played at random (Lost Lute, The Fool) set off block procs of their own (Song Path on a Cho it played).
        else if (procBlock > predictedBlock && UnpredictedAutoPlay(window, entries, me)) modelLines.Skip("block", "random auto-plays", $"predicted {predictedBlock}, actual {procBlock}");
        else if (procBlock != predictedBlock) issues.Add($"block: predicted {predictedBlock}, actual {procBlock}");

        // ---- damage from procs (no card source), against each tab's range
        var procDamage = entries.OfType<DamageReceivedEntry>().Where(damage => damage.CardSource == null).ToList();
        // A card that sets off poison (Outbreak: PoisonPower.Trigger) deals it with no card and no dealer: the card's own effect,
        // checked by the enemies' HP after the play (the simulator), not a proc.
        if (Sim.SimAdapter.TriggerPoisonCards.Contains(window.Card.GetType().Name))
            procDamage = procDamage.Where(damage => !(damage.Dealer == null && damage.Result.Receiver.IsEnemy)).ToList();
        int Dealt(DamageReceivedEntry damage) => damage.Result.TotalDamage + damage.Result.OverkillDamage;
        var toEnemies = procDamage.Where(damage => damage.Result.Receiver.IsEnemy).Sum(Dealt);
        var toMe = procDamage.Where(damage => damage.Result.Receiver == me.Creature).Sum(Dealt);
        var enemyTabs = window.Predicted.Where(tab => tab.Kind == TabAmount.Damage && !tab.Crossed).ToList();
        var meTabs = window.Predicted.Where(tab => tab.Kind == TabAmount.Damage && tab.Crossed).ToList();
        var (eMin, eMax) = (enemyTabs.Sum(tab => tab.AuditMin), enemyTabs.Sum(tab => tab.AuditMax));
        // Orbs the card itself evokes (Dualcast, channelling into full slots) deal unsourced damage too.
        eMin += window.Facts?.OrbDamageMin ?? 0;
        eMax += window.Facts?.OrbDamageMax ?? 0;
        var (mMin, mMax) = (meTabs.Sum(tab => tab.AuditMin), meTabs.Sum(tab => tab.AuditMax));
        // A damage cap outside the game's damage math stopped some of it, seen by its flash: Hardened Shell (HardenedShellPower caps HP
        // lost per turn in ModifyHpLostBeforeOstyLate, which the damage hooks don't reach), Slippery (SlipperyPower.ModifyHpLostAfterOsty: at
        // most 1 HP a hit; Vantom).
        static bool Caps(AbstractModel otherModel) => otherModel is PowerModel { Owner.IsEnemy: true } && (Modifiers.Contains(otherModel.GetType().Name) || otherModel.GetType().Name == nameof(SlipperyPower));
        var capped = flashed.Any(Caps);
        if (toEnemies < eMin && capped) modelLines.Add($"damage to enemies: predicted {Range(eMin, eMax)}, actual {toEnemies}, capped by {string.Join(", ", flashed.Where(Caps).Select(otherModel => otherModel.GetType().Name).Distinct())}");
        else if (toEnemies < eMin && window.FightEnded) Shortfall("damage to enemies", Range(eMin, eMax), toEnemies);
        else if (toEnemies > eMax && UnpredictedAutoPlay(window, entries, me)) modelLines.Skip("damage to enemies", "random auto-plays", $"predicted {Range(eMin, eMax)}, actual {toEnemies}");
        else if (toEnemies < eMin || toEnemies > eMax) issues.Add($"damage to enemies: predicted {Range(eMin, eMax)}, actual {toEnemies}");
        if (toMe < mMin && window.FightEnded) Shortfall("damage to you", Range(mMin, mMax), toMe);
        else if (toMe < mMin || toMe > mMax) issues.Add($"damage to you: predicted {Range(mMin, mMax)}, actual {toMe}");

        // ---- budget: everything else the play caused
        var autoPlayed = UnpredictedAutoPlay(window, entries, me);
        if (autoPlayed) modelLines.Skip("budget", "random auto-plays");
        var facts = autoPlayed ? null : window.Facts;
        bool Explained(Effect effect) => sourceOf.Keys.Any(key => SourceEffects.TryGetValue(key, out var effects) && effects.Contains(effect));
        void Budget(Effect effect, string what, int actual, int own)
        {
            if (actual > own && !Explained(effect) && randomKill) modelLines.Skip($"unexplained {what}", "after a random kill", $"{actual - own} more than the card's own {own}");
            else if (actual > own && !Explained(effect)) issues.Add($"unexplained: {actual - own} more {what} than the card's own {own}");
            // Fewer than the card's own: "up to" counts you choose (exhausts, discards, created cards), or a pile ran out. Draws and
            // channels, which aren't a choice, are reported above.
            else if (actual < own) modelLines.Add($"expected {own} {what}, saw {actual}");
        }
        var draws = entries.OfType<CardDrawnEntry>().Count(cardDrawnEntry => !cardDrawnEntry.FromHandDraw && cardDrawnEntry.Card.Owner == me);
        // A card whose draws depend on its target (Ki Hop, Wrest): what it draws at the target it was played at.
        var expectedDraws = facts == null ? 0 : facts.Draws;
        if (facts != null && window.TargetDraws is { } targetDraws)
            expectedDraws = facts.DrawsWithOwn(targetDraws) ?? Math.Max(0, facts.Draws - (facts.OwnDraws - facts.EnchantmentDraws) + targetDraws);
        var exhausts = entries.OfType<CardExhaustedEntry>().Count(cardExhaustedEntry => cardExhaustedEntry.Card.Owner == me);
        var discards = entries.OfType<CardDiscardedEntry>().Count(cardDiscardedEntry => cardDiscardedEntry.Card.Owner == me);
        var generated = entries.OfType<CardGeneratedEntry>().Count(cardGeneratedEntry => cardGeneratedEntry.Creator == me || cardGeneratedEntry.Card.Owner == me);
        var orbs = entries.OfType<OrbChanneledEntry>().Count();
        // A card that summons every player (Legion of Bone) summons each up to its own count; any other card's summons all count as its own.
        var summoned = entries.OfType<SummonedEntry>().ToList();
        var summons = SimAdapter.AllPlayersSummonCards.Contains(window.Card.GetType().Name)
            ? summoned.GroupBy(summonedEntry => summonedEntry.Actor).Select(byActor => byActor.Count()).DefaultIfEmpty(0).Max()
            : summoned.Count;
        // Tabs that count the cards they make (Personal Hive's Dazed, decision #161): the cards created beyond the card's own are
        // theirs, between the least and the most their branches made, unless another source that creates cards fired too.
        void MadeByTabs(int own)
        {
            var made = sourceOf.Values.Where(tab => tab.Kind == TabAmount.CardsMade).ToList();
            if (made.Count == 0) return;
            var otherMakers = sourceOf.Where(entry => entry.Value.Kind != TabAmount.CardsMade)
                .Where(entry => SourceEffects.TryGetValue(entry.Key, out var effects) && effects.Contains(Effect.Generate)).Select(entry => entry.Key).ToList();
            if (otherMakers.Count > 0) { modelLines.Skip("cards made by tabs", "another source made cards", string.Join(", ", otherMakers)); return; }
            var (least, most) = (made.Sum(tab => tab.AuditMin), made.Sum(tab => tab.AuditMax));
            var what = $"cards made: tabs {least}-{most} beyond the card's own {own}, created {generated}";
            if (generated > own + most || generated < least)
            {
                if (randomKill) modelLines.Skip("cards made by tabs", "after a random kill", what);
                else issues.Add(what);
            }
        }
        if (facts != null)
        {
            // Draws and channels aren't a choice: fewer than predicted is a model gap (FTL's play limit),
            // unless the fight ended (nothing more runs), fewer cards were exhausted than the upper bound
            // (Dark Embrace and Joss Paper draw per exhaust), or you have no orb slots left (the channel fails).
            if (draws < expectedDraws && !window.FightEnded && exhausts >= facts.TotalExhausts) issues.Add($"draws: predicted {expectedDraws}, actual {draws}");
            if (orbs < facts.Channels && !window.FightEnded && window.After?.You.OrbCapacity != 0) issues.Add($"orbs channeled: predicted {facts.Channels}, actual {orbs}");
            // A card whose draws depend on what it draws (Pillage) may draw up to its most (decision #118).
            Budget(Effect.Draw, "draws", draws, Math.Max(expectedDraws, facts.DrawsAtMost));
            Budget(Effect.Exhaust, "exhausts", exhausts, facts.TotalExhausts);
            Budget(Effect.Discard, "discards", discards, facts.Discards);
            // Transformed cards are logged as created too (CardCmd.Transform), up to what the card can transform.
            Budget(Effect.Generate, "created cards", generated, facts.Generated + facts.Transforms);
            MadeByTabs(facts.Generated + facts.Transforms);
            Budget(Effect.Orb, "orbs channeled", orbs, facts.Channels);
            Budget(Effect.Summon, "summons", summons, window.Card.DynamicVars.ContainsKey("Summon") ? facts.Plays : 0);
        }

        // Powers applied: types the card doesn't carry itself (its PowerVar<T> values), with no tab
        // whose source applies powers. Strength/Dexterity changes from Vigor-style consumption and
        // temporary powers expiring are excluded.
        var own = OwnPowerTypes(window.Card);
        var foreign = entries.OfType<PowerReceivedEntry>()
            .Where(powerReceivedEntry => powerReceivedEntry.Amount > 0 && !own.Contains(powerReceivedEntry.Power.GetType()) && powerReceivedEntry.Power is not ITemporaryPower)
            .Where(powerReceivedEntry => !(powerReceivedEntry.Actor.IsEnemy && powerReceivedEntry.Applier is { IsEnemy: true })) // enemies acting on each other (Heist)
            // An enemy that comes in as another dies, set up with its powers by no one (StockPower.AfterDeath spawns the
            // next Axebot and gives it Stock).
            .Where(powerReceivedEntry => !(powerReceivedEntry.Actor.IsEnemy && powerReceivedEntry.Applier == null && DiedInPlay(entries)))
            // A death in the play giving back what a power on the dead creature took, applied by no one
            // (PossessStrengthPower.AfterDeath: the stolen Strength back to each player).
            .Where(powerReceivedEntry => !(powerReceivedEntry.Applier == null && DeathGivesPowers(window, entries)))
            // A summon that makes a new Osty gives it Die For You (OstyCmd.Summon), applied by no one.
            .Where(powerReceivedEntry => !(powerReceivedEntry.Power is DieForYouPower && powerReceivedEntry.Applier == null && entries.OfType<SummonedEntry>().Any()))
            .Select(powerReceivedEntry => powerReceivedEntry.Power.GetType().Name)
            .Distinct()
            .ToList();
        if (foreign.Count > 0 && !Explained(Effect.Power))
        {
            if (randomKill) modelLines.Skip("unexplained powers", "after a random kill", string.Join(", ", foreign));
            else if (autoPlayed) modelLines.Skip("unexplained powers", "random auto-plays", string.Join(", ", foreign));
            else issues.Add($"unexplained: powers applied ({string.Join(", ", foreign)})");
        }

        return issues;
    }
}
