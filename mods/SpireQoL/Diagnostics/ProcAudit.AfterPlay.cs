using MegaCrit.Sts2.Core.Entities.Creatures;
using SpireQoL.Combat;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Combat.History;
using MegaCrit.Sts2.Core.Combat.History.Entries;
using SpireQoL.Sim;
using MegaCrit.Sts2.Core.Models.Cards;

namespace SpireQoL.Diagnostics;

/// <summary>ProcAudit, part: The state the play left, and its lethal tab, against the simulator's prediction of the play.</summary>
public sealed partial class ProcAudit
{
    // ------------------------------------------------------------------ state after the play

    /// <summary>
    /// The lethal tab's inputs (decision #95): each enemy's chance of dying before it acts, over every outcome of the play, against
    /// the end of turn simulated from the board the play left (the game's, read back). An enemy dead in every outcome must be
    /// certain to die then; one that lives in every outcome must not die then. A chance in between can't be judged on one play:
    /// the end-of-turn check totals the chances it shows (ForecastAudit), and the forecast from this board is itself checked
    /// against the enemy turn, so every link is paired.
    /// </summary>
    private static void LethalIssues(Window window, List<CombatHistoryEntry> entries, List<string> issues, ModelLines model)
    {
        var sim = window.Prediction?.Sim;
        var deaths = window.Prediction?.DeathByAim;
        if (sim == null || deaths == null)
        {
            model.Skip("lethal", sim == null ? "no simulation for this card" : "no death chances for this play (its simulations didn't finish)");
            return;
        }
        if (entries.OfType<CardPlayStartedEntry>().Any(cardPlayStartedEntry => cardPlayStartedEntry.CardPlay.IsAutoPlay && !ReferenceEquals(cardPlayStartedEntry.CardPlay.Card, window.Chosen)
                && !HellraiserDrew(window, entries, window.Card.Owner, cardPlayStartedEntry.CardPlay.Card)))
        {
            model.Skip("lethal", AutoPlayReason(window, entries, "auto-played cards"));
            return;
        }
        if (sim.AimFor(window.Target) is not { } lethalAim || !deaths.TryGetValue(lethalAim, out var card))
        {
            model.Skip("lethal", "no death chances for the target it was played at");
            return;
        }
        if (window.FightEnded)
        {
            FightEndLethalIssues(window, sim, card, issues, model);
            return;
        }
        if (window.After == null || window.AfterIds == null)
        {
            model.Skip("lethal", window.AfterSkipped ?? "the board after the play wasn't captured");
            return;
        }
        // What's shown leaves teammates' hands out when the setting is off (decisions #25, #94): the same here (PostPlay).
        if (PostPlay(window, sim) is not { } post)
        {
            model.Skip("lethal", "the board after the play: its search stopped at a cap or met something not modelled");
            return;
        }
        // Your pick inside the play took a card the simulation follows (decision #167: the pick is yours): the board it left isn't the play's to
        // hold the chances to (True Grit+ exhausting Howl from Beyond, which kills as your turn ends).
        if (PickTookAFollowedCard(window) is { } picked)
        {
            model.Skip("lethal", "your pick inside the play took a card the simulation follows (decision #167: the pick is yours)", picked);
            return;
        }
        foreach (var (creature, id) in sim.Ids)
        {
            if (!card.TryGetValue(id, out var chance) || sim.Start.Enemy(id) is not { Hp: > 0 } start) continue;
            // A Waterfall Giant already about to blow before the play explodes on its turn whatever the play does: not the play's
            // to kill or spare (its 999999999 HP lives through any play). Not checked when the play's start wasn't seen.
            if (window.BlowingBefore == null ? IsBlowing(creature) : window.BlowingBefore.Contains(creature))
            {
                model.Skip("lethal", "an enemy about to blow before the play", start.Name);
                continue;
            }
            // Gone from the board after the play: dead. A Waterfall Giant the play brought to 0 is on the board, brought back at 999999999 HP about
            // to blow (WaterfallGiant.TriggerAboutToBlowState): the lethal tag's "dead when it would act" (DeadChanceRange, decisions #295, #336)
            // counts it alive, and so does its board after the play.
            var simChance = window.AfterIds.TryGetValue(creature, out var aid) && post.Branches[0].Enemy(aid) != null ? post.DeadChanceRange(aid) : new SimChance(Low: 1, High: 1, Seen: true);
            if (chance.Certain && !simChance.Certain)
                issues.Add($"lethal: {start.Name} predicted to die before acting in every outcome of this play, but after it: {simChance.Text()}");
            else if (!chance.Possible && simChance.Low > 0)
                issues.Add($"lethal: {start.Name} predicted to live in every outcome of this play, but after it dies before acting ({simChance.Text()})");
        }
    }

    /// <summary>
    /// The lethal tab of a play that ended the fight: there's no board after it to simulate on, but who it killed is known (an enemy
    /// dead as the fight ends died in this play, so before it acted). Checked one way: an enemy predicted to live in every outcome
    /// that died. An enemy alive at the end may have fled, or the fight ended with you, so one predicted to die isn't checked
    /// (counted).
    /// </summary>
    private static void FightEndLethalIssues(Window window, SimInputs sim, IReadOnlyDictionary<int, SimChance> card, List<string> issues, ModelLines model)
    {
        if (window.DeadAtFightEnd == null)
        {
            model.Skip("lethal", FightEndedSkip + ": who it killed wasn't read");
            return;
        }
        foreach (var (creature, id) in sim.Ids)
        {
            if (!card.TryGetValue(id, out var chance) || sim.Start.Enemy(id) is not { Hp: > 0 } start) continue;
            if (window.BlowingBefore?.Contains(creature) == true)
            {
                model.Skip("lethal", "an enemy about to blow before the play", start.Name);
                continue;
            }
            if (window.DeadAtFightEnd.Contains(creature))
            {
                if (!chance.Possible) issues.Add($"lethal: {start.Name} predicted to live in every outcome of this play, but the play killed it (it ended the fight)");
            }
            else if (chance.Possible)
                model.Skip("lethal", FightEndedSkip + ": alive as it ended (fled, or the fight ended with you)", start.Name);
        }
    }

    /// <summary>
    /// Why a play with a pick from your hand auto-played (Decisions, Decisions) isn't held to its state or powers after it: the pick's
    /// plays are simulated from the board as you chose, apart from what the card itself does around them (its draws before the choice;
    /// as it finishes, what spending its stars sets off, Black Hole, and its count toward Pale Blue Dot). A gap (docs/TRACKER.md): one
    /// simulation of the whole play would close it. A dev review: three reports of Decisions, Decisions.
    /// </summary>
    internal const string PickApart = "your pick's auto-plays are simulated apart from the card's own effects around them";

    /// <summary>
    /// The state the play left against the simulator's prediction of it (see the class summary).
    /// Each difference from the closest branch is one issue.
    /// </summary>
    private static void AfterPlayIssues(Window window, List<CombatHistoryEntry> entries, List<string> issues, ModelLines model)
    {
        var sim = window.Prediction?.Sim;
        if (sim == null)
        {
            model.Skip("state after the play", "no simulation for this card");
            return;
        }
        if (window.After == null || window.AfterIds == null)
        {
            model.Skip("state after the play", window.AfterSkipped ?? "not captured");
            return;
        }
        if (entries.OfType<CardPlayStartedEntry>().Any(cardPlayStartedEntry => cardPlayStartedEntry.CardPlay.IsAutoPlay && !ReferenceEquals(cardPlayStartedEntry.CardPlay.Card, window.Chosen)
                && !HellraiserDrew(window, entries, window.Card.Owner, cardPlayStartedEntry.CardPlay.Card)))
        {
            model.Skip("state after the play", AutoPlayReason(window, entries, "auto-played cards"));
            return;
        }
        if (window.Chosen != null && window.Chosen.TargetType == TargetType.AnyEnemy && window.Target == null)
        {
            model.Skip("state after the play", "the pick's auto-plays hit different random enemies");
            return;
        }
        if (window.Chosen != null)
        {
            model.Skip("state after the play", PickApart);
            return;
        }
        if (sim.AimFor(window.Target) is not { } aim || !sim.PlayByTarget.TryGetValue(aim, out var play))
        {
            model.Skip("state after the play", "no simulated play for its target");
            return;
        }
        // Teammates' hands included (decision #25: the check uses everything that isn't a roll).
        SimEngine.ApplyPlayTo(sim.FullStart ?? sim.Start, play, out var branches, out var truncated);
        if (truncated || branches.Count == 0)
        {
            model.Skip("state after the play", "its search stopped at a cap");
            return;
        }
        // A branch it didn't follow (decision #92) is unknown both ways: what happened may be that branch, as the play showed `?`.
        if (branches.FirstOrDefault(branch => branch.Unmodelled != null) is { } unknown)
        {
            model.Skip("state after the play", $"not modelled: {unknown.Unmodelled}");
            return;
        }
        // A choice inside the play is yours (decision #167): the prediction took a card that doesn't act when exhausted. Your pick
        // exhausting one that does (Drum of Battle's energy) made a state the prediction left to your choice: not checked.
        if (SimAdapter.ExhaustPickOf(window.Card) is { Pick: ExhaustPick.Chosen }
            && entries.OfType<CardExhaustedEntry>().Any(exhausted => !ReferenceEquals(exhausted.Card, window.Card)
                && (CardEnergy.WhenExhausted(exhausted.Card) > 0 || SimAdapter.PlaysFromExhaustPile(exhausted.Card))))
        {
            model.Skip("state after the play", "your pick inside the play exhausted a card that acts when exhausted");
            return;
        }
        var after = window.After;
        var afterIds = window.AfterIds;
        // Cards drawn during the play (decision #132: the draw tabs' numbers are part of it, with the card's own draws), when the
        // simulator drew from your pile: the history's draws of yours, not your turn's hand draw.
        var startP = (sim.FullStart ?? sim.Start).You;
        var me = window.Card.Owner;
        int? drawn = startP.Draw == null ? null : entries.OfType<CardDrawnEntry>().Count(cardDrawnEntry => cardDrawnEntry.Actor == me.Creature && !cardDrawnEntry.FromHandDraw);

        // The play won the fight: the simulator stops each command there, and CreatureCmd.Kill has no guard of its own (Bone Shards' Osty dies
        // after its winning hit), but nothing reads Osty's HP once the fight is over (as OstyCmd.Summon's: FightEndTests' reviewed list).
        const string OstyHp = "after play Osty HP";
        // So are your orbs' values: Focus reaches them through ModifyOrbValue, a combat hook that reaches no one once the fight is ending, so the
        // game's orbs read at their base then (Dualcast's second evoke on the last Nibbit: Lightning 3/8 with Focus 1). Which orbs you have still counts.
        const string Orbs = "after play orbs";
        static bool SameKinds(SimState pred, SimState game) => pred.You.Orbs.Select(orb => orb.Kind).SequenceEqual(game.You.Orbs.Select(orb => orb.Kind));
        List<string> Diff(SimState pred) => StateDifferences(pred, after, sim.Ids, afterIds, startP, drawn, "after play")
            .Where(difference => !(window.FightEnded && difference.StartsWith(OstyHp, StringComparison.Ordinal)))
            .Where(difference => !(window.FightEnded && difference.StartsWith(Orbs, StringComparison.Ordinal) && SameKinds(pred, after))).ToList();
        if (window.FightEnded && branches.All(branch => branch.You.OstyHp != after.You.OstyHp)) model.Skip("Osty HP", FightEndedSkip, $"Osty at {after.You.OstyHp} as it ended");

        var best = branches.Select(Diff).OrderBy(differences => differences.Count).First();
        if (best.Count == 0) return;
        var note = branches.Count > 1 ? $" (closest of {branches.Count} branches)" : "";
        foreach (var difference in best) issues.Add(difference + note);
    }

    /// <summary>
    /// What differs between a predicted state and the one the game left ('<paramref name="what"/>': "after play", "after potion"), each a line: the enemies' HP,
    /// block, poison, Flutter, stun and intent, your block, hand, draws, energy, the powers the simulator keeps as numbers, Osty, your orbs, and each
    /// living teammate's block, HP, energy and orbs.
    /// </summary>
    private static List<string> StateDifferences(SimState pred, SimState after, IReadOnlyDictionary<Creature, int> ids, IReadOnlyDictionary<Creature, int> afterIds,
        SimPlayer startP, int? drawn, string what)
    {
        var differences = new List<string>();
        foreach (var (creature, id) in ids)
        {
            var enemy = pred.Enemy(id);
            if (enemy == null || !afterIds.TryGetValue(creature, out var aid) || after.Enemy(aid) is not { } simEnemy) continue;
            var name = enemy.Name;
            static int Hp(SimEnemy nextEnemy) => Math.Max(0, nextEnemy.Hp);
            // The Waterfall Giant doesn't leave at 0 HP: Steam Eruption puts it about to blow at 999999999 HP (WaterfallGiant.
            // TriggerAboutToBlowState), stunned this turn, as good as dead until it explodes.
            var aboutToBlow = enemy.Hp <= 0 && IsBlowing(creature);
            if (Hp(enemy) != Hp(simEnemy) && !aboutToBlow) differences.Add($"{what} enemy HP: {name} predicted {Hp(enemy)}, actual {Hp(simEnemy)}");
            if (simEnemy.Hp <= 0 || enemy.Hp <= 0) continue;
            if (enemy.Block != simEnemy.Block) differences.Add($"{what} enemy block: {name} predicted {enemy.Block}, actual {simEnemy.Block}");
            if (enemy.Poison != simEnemy.Poison) differences.Add($"{what} enemy poison: {name} predicted {enemy.Poison}, actual {simEnemy.Poison}");
            // FlutterPower.AfterDamageReceived: one stack per powered hit that gets through (no flash until it stuns).
            if (enemy.Flutter != simEnemy.Flutter) differences.Add($"{what} enemy Flutter: {name} predicted {enemy.Flutter}, actual {simEnemy.Flutter}");
            if (enemy.Stunned != simEnemy.Stunned) differences.Add($"{what} enemy stunned: {name} predicted {(enemy.Stunned ? "stunned" : "not stunned")}, actual {(simEnemy.Stunned ? "stunned" : "not stunned")}");
            // A stunned enemy's intent becomes the stun move; the simulator keeps its hits and skips them. The predicted intent is
            // its hits as they'd land on you in the predicted state (what the play gave or took: Weak, Tainted, Conductor's
            // Baton...); Hits is only the intent as it was read before the play.
            var predHits = enemy.Intent is { Count: > 0 } intent
                ? Enumerable.Range(0, intent.Count).Select(hitIndex => SimEngine.IntentDamage(pred, enemy, hitIndex, Who.You)).ToList()
                : enemy.Hits;
            if (!enemy.Stunned && !simEnemy.Stunned && !predHits.SequenceEqual(simEnemy.Hits)) differences.Add($"{what} enemy intent: {name} predicted [{string.Join(", ", predHits)}], actual [{string.Join(", ", simEnemy.Hits)}]");
        }
        var player = pred.You;
        var simPlayer = after.You;
        // "after play cards in hand": its own name after the check's (a parameter named what, as it once was, hid the check's).
        void Cmp(string name, int predicted, int actual)
        {
            if (predicted != actual) differences.Add($"{what} {name}: predicted {predicted}, actual {actual}");
        }
        Cmp("your block", player.Block, simPlayer.Block);
        // Max HP a play or a potion raises (CreatureCmd.GainMaxHp: Feed, Fruit Juice, decision #335).
        Cmp("your max HP", player.MaxHp, simPlayer.MaxHp);
        // What it took from your hand and gave it (the room later draws have; Cloak Clasp, Regret at the end of the turn).
        Cmp("cards in hand", player.HandCount, simPlayer.HandCount);
        if (drawn is { } drawnCount) Cmp("cards drawn", player.Drawn - startP.Drawn, drawnCount);
        // Energy (decision #117): what the play spends and gives back, the card's own included (Pael's Tears reads it).
        Cmp("your energy", player.Energy, simPlayer.Energy);
        Cmp("Plating", player.Plating, simPlayer.Plating);
        Cmp(nameof(Juggernaut), player.Juggernaut, simPlayer.Juggernaut);
        Cmp("Thorns", player.Thorns, simPlayer.Thorns);
        Cmp("Flame Barrier", player.FlameBarrier, simPlayer.FlameBarrier);
        Cmp("Feel No Pain", player.FeelNoPain, simPlayer.FeelNoPain);
        // A summon is simulated as OstyCmd.Summon runs (SimEngine.Summon): Osty is held to it like any other number.
        Cmp("Osty HP", player.OstyHp, simPlayer.OstyHp);
        // An orb the simulator doesn't know (another mod's) is Unknown: any orb matches it. A random one (Chaos) is a branch per orb.
        static string Orb(SimOrb orb) => $"{orb.Kind} {orb.Passive}/{orb.Evoke}";
        var orbsMatch = player.Orbs.Count == simPlayer.Orbs.Count
                        && player.Orbs.Zip(simPlayer.Orbs).All(orbs => orbs.First.Kind == OrbKind.Unknown || Orb(orbs.First) == Orb(orbs.Second));
        if (!orbsMatch) differences.Add($"{what} orbs: predicted [{string.Join(", ", player.Orbs.Select(Orb))}], actual [{string.Join(", ", simPlayer.Orbs.Select(Orb))}]");
        // Teammates (multiplayer): the block and HP your play leaves them (Beacon of Hope's share, hits).
        // The state after is read from the living ones only (SimAdapter.IsTeammate), in player order.
        var living = pred.Teammates.Where(teammate => teammate.Hp > 0).ToList();
        if (living.Count == after.Teammates.Count)
        {
            for (var enemyIndex = 0; enemyIndex < living.Count; enemyIndex++)
            {
                var (nextPlayer, at) = (living[enemyIndex], after.Teammates[enemyIndex]);
                Cmp($"teammate {enemyIndex + 1} block", nextPlayer.Block, at.Block);
                Cmp($"teammate {enemyIndex + 1} HP", nextPlayer.Hp, at.Hp);
                Cmp($"teammate {enemyIndex + 1} energy", nextPlayer.Energy, at.Energy);
                // Their orbs (Ignition channels into them; full slots evoke their front orb): kinds, front first.
                if (!nextPlayer.Orbs.Select(orb => orb.Kind).SequenceEqual(at.Orbs.Select(orb => orb.Kind))
                    && !nextPlayer.Orbs.Any(orb => orb.Kind == OrbKind.Unknown))
                    differences.Add($"{what} teammate {enemyIndex + 1} orbs: predicted [{string.Join(", ", nextPlayer.Orbs.Select(orb => orb.Kind))}], actual [{string.Join(", ", at.Orbs.Select(orb => orb.Kind))}]");
            }
        }
        else differences.Add($"{what} teammates alive: predicted {living.Count}, actual {after.Teammates.Count}");
        return differences;
    }
}
