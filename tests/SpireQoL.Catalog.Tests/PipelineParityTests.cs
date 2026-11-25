using System.Reflection;
using SpireQoL.Procs.Programs;
using SpireQoL.Sim;
using Xunit;

namespace SpireQoL.Catalog.Tests;

/// <summary>
/// Pipeline steps (docs/CARD_PROGRAMS.md B1): each game command the simulator mirrors (SimPipelines) runs the game's hooks in the
/// game's order. The game's are read from the command's code on both branches (each decompile's assembly/sts2.dll): its Hook calls in
/// order, through the methods of its own class it calls (overloads, private helpers, CreatureCmd.Kill inside Damage), each hook once
/// where it first runs. Every difference (a hook the simulator doesn't run, one the game doesn't, one out of order) is modelled or
/// reviewed below. The simulator's draw skipping Hook.ShouldDraw (No Draw) is what this would have caught.
/// </summary>
public class PipelineParityTests
{
    public static TheoryData<string> Branches => new() { "beta", "main" };

    private static string PathOf(string branch) => branch == "main" ? BranchCode.MainPath : BranchCode.BetaPath;

    public enum Difference { Missing, Extra, Order }

    /// <summary>
    /// Differences reviewed, by pipeline, kind and hook: "no model:" (no simulator model listens to it there, so it can't change a
    /// prediction, and why) or "gap:" (what isn't modelled). Never a card or relic named to quiet one.
    /// </summary>
    private static readonly Dictionary<(string Pipeline, Difference Kind, string Hook), string> Reviewed = new()
    {
        // ---- CreatureCmd.GainBlock
        [("CreatureCmd.GainBlock", Difference.Missing, "BeforeBlockGained")] = "no model: no game class overrides BeforeBlockGained on either branch",

        // ---- CreatureCmd.Damage (on an enemy)
        [("CreatureCmd.Damage", Difference.Missing, "ModifyPreBlockDamageTarget")] = "no model: its one listener, Wuppy's Loyalty (beta), takes powered hits meant for its owner, a player: the player pipeline runs it",
        [("CreatureCmd.Damage", Difference.Missing, "ModifyUnblockedDamageTarget")] = "no model: its one listener, Die For You, gives Osty powered hits meant for his owner, a player: the player pipeline runs it",
        [("CreatureCmd.Damage", Difference.Missing, "AfterCurrentHpChanged")] = "no model: on an enemy's HP only Crusher and Rocket listen, to play a hurt animation; the others (Red Skull, Meat on the Bone, Necro Mastery, Wuppy) act on a player's own creatures",
        [("CreatureCmd.Damage", Difference.Missing, "BeforeDeath")] = "no model: its listeners play sounds and animations (Crusher, Rocket) or hand back what a thief stole as a reward (Heist, Swipe)",
        [("CreatureCmd.Damage", Difference.Missing, "ShouldDie")] = "no model: its listeners save a player (Fairy in a Bottle; Lizard Tail's ShouldDieLate), never an enemy: the player pipeline runs it",
        [("CreatureCmd.Damage", Difference.Missing, "ShouldCreatureBeRemovedFromCombatAfterDeath")] = "no model: it keeps a dead creature in the fight (Adaptable, Illusion and Reattach revive it on a later turn, so its kill holds for the turn: CoverageLedger), which changes nothing the turn's simulation follows",
        [("CreatureCmd.Damage", Difference.Missing, "AfterPreventingDeath")] = "no model: its listeners heal a player whose death was prevented (Fairy in a Bottle, Lizard Tail): the player pipeline runs it",
        [("CreatureCmd.Damage", Difference.Order, "ModifyHpLost")] = "branch rule: DamageWorkedOutFirst (main tells BeforeDamageReceived before working out the HP loss, and each change as it's made; the simulator follows the running game's order, MainRule.ReceivedBeforeTheResults)",

        // ---- CreatureCmd.Damage on a player (an enemy's hit)
        [("CreatureCmd.Damage on a player", Difference.Missing, "AfterModifyingDamageAmount")] = "gap: an enemy's hit on a player doesn't run it, so the flash of what capped it (the player's Intangible) isn't logged",
        [("CreatureCmd.Damage on a player", Difference.Order, "ModifyHpLost")] = "branch rule: DamageWorkedOutFirst (main tells BeforeDamageReceived before working out the hit, and each HP-loss change as it's made; the simulator follows the running game's order, MainRule.ReceivedBeforeTheResults)",
        [("CreatureCmd.Damage on a player", Difference.Missing, "AfterBlockBroken")] = "no model: its listeners (Burrowed, Hand Drill) act on an enemy's block; no game class acts on a player's",
        [("CreatureCmd.Damage on a player", Difference.Missing, "BeforeDeath")] = "no model: its listeners act on their own enemy's death (Crusher, Rocket, Heist, Swipe), never a player's",
        [("CreatureCmd.Damage on a player", Difference.Missing, "ShouldCreatureBeRemovedFromCombatAfterDeath")] = "no model: its listeners keep their own enemy or pet in the fight (Adaptable, Illusion, Reattach, Die For You, Wuppy's Loyalty...), never a player",
        [("CreatureCmd.Damage on a player", Difference.Missing, "AfterDeath")] = "gap: a player's death runs no AfterDeath, so what reacts to it isn't simulated (Covered and Guarded go when the teammate who gave them dies)",
        [("CreatureCmd.Damage on a player", Difference.Extra, "ModifyPreBlockDamageTarget")] = "no model: main has no such step and no Wuppy (beta only), so a player's Wuppy HP is always 0 there and the step does nothing",
        [("CreatureCmd.Damage on a player", Difference.Order, "ModifyUnblockedDamageTarget")] = "branch rule: DamageWorkedOutFirst (main tells the BeforeOsty phase's changes before Osty takes his share; the simulator tells each phase's as it's made there, MainRule.ReceivedBeforeTheResults: HpLossOnPlayer)",
        [("CreatureCmd.Damage on a player", Difference.Order, "AfterCurrentHpChanged")] = "gap: the player's own result's HP change isn't told (its listener on a player, Red Skull, isn't simulated); Osty's is, at his result's place in the results loop (TellOstyHit: after the player's result on beta, before it on main)",
        [("CreatureCmd.Damage on a player", Difference.Order, "ShouldDie")] = "gap: a player's death (Fairy in a Bottle, Lizard Tail) is settled as their HP drops (Lose), before AfterDamageGiven and AfterDamageReceived; the game settles it after them (CreatureCmd.Kill) and skips AfterDamageReceived for a hit that killed",
        [("CreatureCmd.Damage on a player", Difference.Order, "AfterPreventingDeath")] = "gap: the heal from 0 comes with the death check (Lose), before AfterDamageGiven and AfterDamageReceived; the game heals after them",

        // ---- AttackCommand.Execute
        [("AttackCommand.Execute", Difference.Missing, "ModifyAttackHitCount")] = "no model: the adapter's hit count already went through the game's own Hook.ModifyAttackHitCount (BoardCounts.HitsPerPlay: Thumbscrew)",
        [("AttackCommand.Execute", Difference.Missing, "ShouldCreatureBeRemovedFromCombatAfterDeath")] = "no model: v0.114.0 asks it for an attacker killed during its own attack, once the attack is over (its death waits: Creature.IsAttacking); its listeners keep their own creature in the fight (Adaptable, Illusion, Reattach revive it on a later turn), which changes nothing the turn's simulation follows",

        // ---- PowerCmd.Apply
        [("PowerCmd.Apply", Difference.Missing, "BeforePowerAmountChanged")] = "no model: Unsettling Lamp's binding (its BeforePowerAmountChanged) is in its ModifyPowerAmountGiven, the step after; Void Form's only hides a cost visual",
        [("PowerCmd.Apply", Difference.Missing, "AfterModifyingPowerAmountGiven")] = "no model: its one listener, Snecko Skull, only flashes, and its ModifyPowerAmountGiven logs that",

        // ---- CardPileCmd.Draw
        [("CardPileCmd.Draw", Difference.Missing, "AfterPreventingDraw")] = "no model: its one listener, Fiddle, only flashes (its ShouldDraw is in DrawsStopped)",
        [("CardPileCmd.Draw", Difference.Missing, "ShouldAddToDeck")] = "no model: CardPileCmd.Add runs it only for a card added to the deck, which a draw or a shuffle never does",
        [("CardPileCmd.Draw", Difference.Missing, "ModifyCardBeingAddedToDeck")] = "no model: CardPileCmd.Add runs it only for a new card added to the deck, which a draw or a shuffle never does",
        [("CardPileCmd.Draw", Difference.Missing, "AfterCardEnteredCombat")] = "no model: CardPileCmd.Add runs it only for a card that had no pile (a new one), never one drawn or shuffled",
        [("CardPileCmd.Draw", Difference.Missing, "AfterCardChangedPiles")] = "no model: its listeners act on a card added to the deck (Bing Bong, Book of Five Rings, Darkstone Periapt, Lucky Fysh, Spoils Map, Hoarder) or on Sovereign Blade made or brought back from the exhaust pile, never on a draw or a shuffle",

        // ---- CardCmd.Discard
        [("CardCmd.Discard", Difference.Missing, "ShouldPlay")] = "gap: a Sly card discarded from the hand plays itself (CardCmd.AutoPlay); the simulator marks that unmodelled (`?`) instead",
        [("CardCmd.Discard", Difference.Missing, "BeforeCardAutoPlayed")] = "gap: a Sly card discarded from the hand plays itself (CardCmd.AutoPlay); the simulator marks that unmodelled (`?`) instead",

        // ---- PlayerCmd.GainEnergy
        [("PlayerCmd.GainEnergy", Difference.Missing, "ModifyEnergyGain")] = "no model: its one listener, No Energy Gain, is applied by nothing in the game on either branch",
        [("PlayerCmd.GainEnergy", Difference.Missing, "AfterModifyingEnergyGain")] = "no model: its one listener, No Energy Gain, is applied by nothing in the game on either branch",
    };

    [Theory]
    [MemberData(nameof(Branches))]
    public void EveryPipelineRunsTheGameCommandsHooksInItsOrder(string branch)
    {
        if (!File.Exists(PathOf(branch))) return; // no decompile of this branch here (docs/DEVELOPMENT.md "Decompiled game source")
        var code = BranchCode.Of(PathOf(branch));
        var unreviewed = new List<string>();
        foreach (var pipeline in SimPipelines.All)
        {
            var game = GameHooks(code, pipeline.GameCommand);
            var found = Differences(game, pipeline.Hooks).Where(difference => !Reviewed.ContainsKey((pipeline.Name, difference.Kind, difference.Hook))).ToList();
            if (found.Count == 0) continue;
            unreviewed.Add($"{pipeline.Name} ({pipeline.Runs}):\n  game:      {string.Join(", ", game)}\n  simulator: {string.Join(", ", pipeline.Hooks)}");
            unreviewed.AddRange(found.Select(difference => $"  {difference.Kind} {difference.Hook}"));
        }
        Assert.True(unreviewed.Count == 0, $"Pipeline steps that differ from the game's ({branch}): model each, or review it here\n" + string.Join("\n", unreviewed));
    }

    [Fact]
    public void EveryReviewedDifferenceIsStillOne()
    {
        if (!File.Exists(BranchCode.BetaPath) || !File.Exists(BranchCode.MainPath)) return; // both branches are needed to tell
        var seen = new HashSet<(string, Difference, string)>();
        foreach (var path in new[] { BranchCode.BetaPath, BranchCode.MainPath })
        {
            var code = BranchCode.Of(path);
            foreach (var pipeline in SimPipelines.All)
                foreach (var difference in Differences(GameHooks(code, pipeline.GameCommand), pipeline.Hooks))
                    seen.Add((pipeline.Name, difference.Kind, difference.Hook));
        }
        var stale = Reviewed.Keys.Where(key => !seen.Contains(key)).Select(key => $"{key.Pipeline}: {key.Kind} {key.Hook}").ToList();
        Assert.True(stale.Count == 0, "Reviewed pipeline differences that no branch has any more (remove them):\n" + string.Join("\n", stale));
    }

    [Fact]
    public void EveryReviewSaysWhy()
    {
        // "branch rule: X": the simulator follows each branch's order by a rule told from the running game's code (decision #169), X its name.
        static bool NamesABranchRule(string review) => review.StartsWith("branch rule: ", StringComparison.Ordinal)
            && SpireQoL.Sim.BranchRules.All.Any(rule => review.StartsWith("branch rule: " + rule.Name + " ", StringComparison.Ordinal));
        var bad = Reviewed.Where(entry => !entry.Value.StartsWith("no model: ", StringComparison.Ordinal) && !entry.Value.StartsWith("gap: ", StringComparison.Ordinal) && !NamesABranchRule(entry.Value))
            .Select(entry => $"{entry.Key.Pipeline}: {entry.Key.Kind} {entry.Key.Hook}").ToList();
        Assert.True(bad.Count == 0, "Reviews that don't start with \"no model: \" or \"gap: \":\n" + string.Join("\n", bad));
        var unknown = Reviewed.Keys.Select(key => key.Pipeline).Where(name => SimPipelines.All.All(pipeline => pipeline.Name != name)).ToList();
        Assert.True(unknown.Count == 0, "Reviews of pipelines SimPipelines doesn't have: " + string.Join(", ", unknown));
    }

    [Theory]
    [MemberData(nameof(Branches))]
    public void TheReaderFindsTheShouldDrawStep(string branch)
    {
        // The step the simulator once skipped (No Draw): the game's draw asks Hook.ShouldDraw before anything else.
        if (!File.Exists(PathOf(branch))) return;
        var code = BranchCode.Of(PathOf(branch));
        Assert.Equal("ShouldDraw", GameHooks(code, "CardPileCmd.Draw")[0]);
        Assert.Equal(new[] { "BeforeBlockGained", "ModifyBlock", "AfterModifyingBlockAmount", "AfterBlockGained" }, GameHooks(code, "CreatureCmd.GainBlock"));
    }

    // ---- the comparison

    /// <summary>Hooks the game runs and the simulator doesn't, ones it runs that the game doesn't, and shared ones out of the game's order.</summary>
    internal static List<(Difference Kind, string Hook)> Differences(IReadOnlyList<string> game, IReadOnlyList<string> simulator)
    {
        var differences = new List<(Difference, string)>();
        differences.AddRange(game.Where(hook => !simulator.Contains(hook)).Select(hook => (Difference.Missing, hook)));
        differences.AddRange(simulator.Where(hook => !game.Contains(hook)).Select(hook => (Difference.Extra, hook)));
        var gameShared = game.Where(simulator.Contains).ToList();
        var simulatorShared = simulator.Where(game.Contains).ToList();
        var inOrder = LongestInOrder(simulatorShared, gameShared);
        differences.AddRange(simulatorShared.Where(hook => !inOrder.Contains(hook)).Select(hook => (Difference.Order, hook)));
        return differences;
    }

    /// <summary>The longest run of hooks both lists have in the same order (the rest of the simulator's are out of order).</summary>
    private static HashSet<string> LongestInOrder(List<string> simulator, List<string> game)
    {
        var lengths = new int[simulator.Count + 1, game.Count + 1];
        for (var i = simulator.Count - 1; i >= 0; i--)
            for (var j = game.Count - 1; j >= 0; j--)
                lengths[i, j] = simulator[i] == game[j] ? lengths[i + 1, j + 1] + 1 : Math.Max(lengths[i + 1, j], lengths[i, j + 1]);
        var kept = new HashSet<string>();
        for (int i = 0, j = 0; i < simulator.Count && j < game.Count;)
        {
            if (simulator[i] == game[j])
            {
                kept.Add(simulator[i]);
                i++;
                j++;
            }
            else if (lengths[i + 1, j] >= lengths[i, j + 1]) i++;
            else j++;
        }
        return kept;
    }

    // ---- the game's hooks

    private const BindingFlags All = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

    /// <summary>
    /// A game command's Hook calls in order ("CreatureCmd.GainBlock"), each once where it first runs. Its overloads all end in the
    /// same code: the longest is the one that runs it all.
    /// </summary>
    internal static List<string> GameHooks(BranchCode code, string command)
    {
        var (typeName, method) = (command[..command.IndexOf('.')], command[(command.IndexOf('.') + 1)..]);
        var owner = code.Assembly.GetTypes().SingleOrDefault(type => type.Name == typeName && type.Namespace?.StartsWith("MegaCrit.Sts2.Core.Commands", StringComparison.Ordinal) == true);
        Assert.True(owner != null, $"No game command class {typeName}");
        var overloads = owner!.GetMethods(All).Where(candidate => candidate.Name == method).ToList();
        Assert.True(overloads.Count > 0, $"No game command {command}");
        return overloads.Select(entry => Once(Calls(code, owner, entry, new HashSet<MethodBase>()))).OrderByDescending(hooks => hooks.Count).First();
    }

    private static List<string> Once(IEnumerable<string> hooks)
    {
        var seen = new List<string>();
        foreach (var hook in hooks) if (!seen.Contains(hook)) seen.Add(hook);
        return seen;
    }

    /// <summary>The Hook calls in a method's code, in order, through the methods of the command's own class (and its nested ones) it calls.</summary>
    private static IEnumerable<string> Calls(BranchCode code, Type owner, MethodBase method, HashSet<MethodBase> visiting)
    {
        if (!visiting.Add(method)) yield break;
        var body = AsyncBody(method) ?? method;
        foreach (var instruction in code.Read(body))
        {
            if (!instruction.IsCall || instruction.Member is not { Kind: CodeMemberKind.Method } member) continue;
            if (member.DeclaringType == "Hook" && member.Namespace == "MegaCrit.Sts2.Core.Hooks")
            {
                yield return member.Name;
                continue;
            }
            if (code.Callee(body, instruction) is { } callee && Within(callee.DeclaringType, owner))
                foreach (var hook in Calls(code, owner, callee, visiting)) yield return hook;
        }
        visiting.Remove(method);
    }

    private static bool Within(Type? type, Type owner)
    {
        for (var current = type; current != null; current = current.DeclaringType)
            if (current == owner) return true;
        return false;
    }

    /// <summary>An async method's code: its state machine's MoveNext (each overload has its own, named by its AsyncStateMachine attribute).</summary>
    private static MethodBase? AsyncBody(MethodBase method)
    {
        var machine = method.GetCustomAttributesData().FirstOrDefault(attribute => attribute.AttributeType.Name == "AsyncStateMachineAttribute");
        return machine?.ConstructorArguments[0].Value is Type type ? type.GetMethod("MoveNext", All) : null;
    }
}
