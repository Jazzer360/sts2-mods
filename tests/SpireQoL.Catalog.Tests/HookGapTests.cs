using System.Text;
using System.Text.RegularExpressions;
using SpireQoL.Sim;
using Xunit;

namespace SpireQoL.Catalog.Tests;

/// <summary>
/// What each hand-written model's game class still needs before it can be read from its code (decision #195): every hook body's first gap, on
/// beta, named by the reader capability it waits on, and each capability with the classes it blocks (Snapshots/hook-gaps.txt, regenerated with
/// SPIREQOL_UPDATE_SNAPSHOT=1). A class blocked by several capabilities counts in each; "alone" counts the classes it's the last one for. The
/// reader stops a body at its first gap, so a body can need more than its line says: the measurement is a floor, by what the code reads to.
/// </summary>
public class HookGapTests
{
    /// <summary>
    /// A gap's capability, by what the reader says it couldn't follow: the first pattern that matches, in order (the specific before the general).
    /// A gap no pattern names fails the test, so a new kind of gap gets a name before it's counted.
    /// </summary>
    private const string InstancedCopies = "instanced copies (each copy its own amount and data)";

    private static readonly (string Pattern, string Capability)[] Capabilities =
    {
        (@"each copy acts on its own", InstancedCopies),
        (@"ModifyVulnerableMultiplier|ModifyWeakMultiplier", "a call into another model's method, its owner rebound (Paper Phrog, Paper Krane, Cruelty, Debilitate)"),
        (@"Instance\.History|the fight's log", "the fight's log (the history ledger, decision #191)"),
        (@"the value \{", "a value of several ways (a local set on each branch, merged)"),
        (@"read model's (AfterAttack|BeforeAttack)\b", "hooks: BeforeAttack and AfterAttack"),
        (@"commandToModify", "hooks: BeforeAttack and AfterAttack"),
        (@"read model's (ModifyHpLost\w*|AfterModifyingHpLost\w*)", "the HP-loss pipeline"),
        (@"read model's (ModifyDamageCap|AfterModifyingDamageAmount)", "the damage cap and AfterModifyingDamageAmount"),
        (@"read model's (ModifyCardPlayResultLocation|AfterModifyingCardPlayResultLocation)", "the play's result location (ModifyCardPlayResultLocation)"),
        (@"read model's AfterApplied", "hooks: AfterApplied"),
        (@"read model's BeforeDamageReceived", "hooks: BeforeDamageReceived"),
        (@"read model's (AfterBlockBroken|ShouldClearBlock|AfterModifyingBlockAmount|AfterBlockGained|AfterBlockCleared|AfterPreventingBlockClear)", "hooks: block (AfterBlockBroken, ShouldClearBlock, AfterModifyingBlockAmount)"),
        (@"read model's AfterCurrentHpChanged", "hooks: AfterCurrentHpChanged"),
        (@"read model's AfterCardEnteredCombat", "hooks: AfterCardEnteredCombat"),
        (@"read model's (AfterArrowShot|AfterArrowImpaled)|arrow\.owner|DislodgeAllArrows", "arrows (AfterArrowShot, AfterArrowImpaled, an arrow's owner)"),
        (@"read model's AfterOrbChanneled|OrbCmd\.EvokeLast|an orb made first", "orbs (AfterOrbChanneled, EvokeLast, an orb made first)"),
        (@"read model's (ModifyPowerAmountGiven\w*|AfterModifyingPowerAmountGiven|BeforePowerAmountChanged)", "hooks: the power amount given"),
        (@"read model's (TryModifyEnergyCost\w*|TryModifyStarCost|TryModifyKeywordsInCombat|ModifyXValue)", "card costs and keywords (cards' own hooks, Part B3)"),
        (@"read model's (ModifyHandDraw|AfterModifyingHandDraw)", "hooks: the hand draw"),
        (@"read model's (AfterTakingExtraTurn|ShouldTakeExtraTurn\w*)|acts as a side's turn starts", "a turn's start (the enemies' turn start, an extra turn)"),
        (@"read model's GetScaledAmountForMultiplayer", "multiplayer scaling (GetScaledAmountForMultiplayer)"),
        (@"read model's (ShouldStopCombatFromEnding|ShouldCreatureBeRemovedFromCombatAfterDeath|ShouldPowerBeRemovedAfterOwnerDeath)", "a death the game holds back (ShouldStopCombatFromEnding and the like)"),
        (@"read model's (AfterHandEmptied|ShouldPlay|BeforePotionUsed)", "hooks: other (AfterHandEmptied, ShouldPlay, BeforePotionUsed)"),
        (@"PlayerCmd\.GainStars", "commands: GainStars"),
        (@"CardCmd\.ApplyKeyword|CardCmd\.Upgrade|SetToFreeThisTurn", "commands: a card changed (ApplyKeyword, Upgrade, SetToFreeThisTurn)"),
        (@"AddGeneratedCardToCombat|makes a ", "commands: a card made (AddGeneratedCardToCombat, a made card)"),
        (@"AutoPlayFromDrawPile", "commands: AutoPlayFromDrawPile"),
        (@"OstyCmd\.", "commands: OstyCmd.Summon"),
        (@"CreatureCmd\.Stun|CreatureCmd\.Kill|SetStunned", "outcomes: a stun or a kill (outcome tabs)"),
        (@"(\w+)\.\w+ isn't translated", "a model's own method (its body read where it's called)"),
        (@"applied by someone else", "a power applied by someone other than its owner"),
        (@"PowerCmd\.\w+ of another power", "a command on another power (PowerCmd.Remove of another)"),
        (@"TurnNumber", "the turn number"),
        (@"PlayerCombatState\.Energy", "the owner's energy"),
        (@"a loop", "a loop over items (creatures, cards)"),
        (@"a pick", "a pick"),
        (@"changes its own data|uses its own field|changes a collection|GetInternalData|the value null|the value cardPlay", "its own data: a card or a play kept, a field used directly"),
        (@"acts on |damage to |block for |a heal for ", "a command on another creature (the players, a card's owner, a roll's list)"),
        (@"HasFlag|IsCardOrMonsterMove", "queries: the damage's or block's props"),
        (@"\.Side\b|the value side|tests (dealer|target)\b|the value dealer|IsMonster", "queries: a creature's side or presence"),
        (@"cardSource|causedByEthereal|IsAutoPlay|Keywords|the value cardPlay\.Card", "queries: the card's facts (its pile, tags, keywords, class; an auto-play; an Ethereal exhaust)"),
        (@"the value amount", "queries: the hook's amount (AfterStarsSpent's)"),
        (@"result\.(TotalDamage|UnblockedDamage)", "queries: the hit's result"),
        (@"Pow\b|op_UnaryNegation", "queries: arithmetic (Math.Pow, a negation)"),
        (@"the value card\.Owner", "queries: the card's facts (its pile, tags, keywords, class; an auto-play; an Ethereal exhaust)"),
        (@"this\.Owner\.Powers", "queries: the owner's powers"),
        (@"Applier", "queries: the power's applier (alive, its side)"),
    };

    /// <summary>The capability a gap waits on (a monster's or another model's state set is its own kind: read from the class it sets).</summary>
    private static string CapabilityOf(string gap, Func<string, bool> isMonster)
    {
        var set = Regex.Match(gap, @"(?:sets|stores) (\w+)\.\w+ where|changes a game object outside a command \((\w+)\.");
        if (set.Success)
        {
            var declaring = set.Groups[1].Success ? set.Groups[1].Value : set.Groups[2].Value;
            if (isMonster(declaring)) return "a monster's own state set (its flag, its next move)";
            if (declaring != "CardModel") return "another model's value set (PowerModel.Target, DynamicVar.BaseValue, AfflictionModel.Amount)";
        }
        foreach (var (pattern, capability) in Capabilities)
            if (Regex.IsMatch(gap, pattern)) return capability;
        throw new InvalidOperationException($"no capability names the gap \"{gap}\": add one to HookGapTests.Capabilities");
    }

    [Fact]
    public void EachHandWrittenModelsGapsAreTheSnapshots()
    {
        if (!File.Exists(BranchCode.BetaPath)) return; // no decompile here (docs/DEVELOPMENT.md "Decompiled game source")
        using var code = new BranchCode(BranchCode.BetaPath);
        var types = code.Assembly.GetTypes();
        var classes = HookReader.Powers(types).Concat(HookReader.Relics(types)).ToDictionary(type => type.Name, StringComparer.Ordinal);
        var monsters = types.Where(type => Derives(type, "MonsterModel")).Select(type => type.Name).ToHashSet(StringComparer.Ordinal);
        var byClass = new List<(string Class, List<(string Capability, string Gap)> Gaps)>();
        foreach (var name in SimModels.HandWrittenNames.Order(StringComparer.Ordinal))
        {
            if (!classes.TryGetValue(name, out var type))
            {
                byClass.Add((name, new() { ("the simulator's own model (no game class by its name)", "no game class") }));
                continue;
            }
            var reading = HookReader.Read(type, code.Read);
            var gaps = reading.Hooks.Where(hook => !hook.Read).Select(hook => (CapabilityOf(hook.Gap!, monsters.Contains), $"{hook.Hook}: {Shortened(hook.Gap!)}")).ToList();
            // An instanced power's bodies are behind its copies' gap: read past it, so what else each body needs is counted too.
            if (gaps.Any(gap => gap.Item1 == InstancedCopies))
                gaps = gaps.Where(gap => gap.Item1 == InstancedCopies).Take(1).Select(gap => (gap.Item1, "(the class): copies"))
                    .Concat(HookReader.Read(type, code.Read, pastCopies: true).Hooks.Where(hook => !hook.Read).Select(hook => (CapabilityOf(hook.Gap!, monsters.Contains), $"{hook.Hook}: {Shortened(hook.Gap!)}"))).ToList();
            // A class with no hook of its own: what it does is in another class's code (Sovereign Blade's plays read Parry, Seeking Edge).
            if (gaps.Count == 0 && reading.Hooks.Count == 0) gaps.Add(("the simulator's own model (its game class has no hooks: another class's code reads it)", "no hooks"));
            if (gaps.Count == 0) gaps.Add(("read whole: a parity proof (its hand-written model does more than its hooks: a tab, a rule)", "read whole"));
            byClass.Add((name, gaps));
        }
        var text = new StringBuilder();
        text.Append("# The hand-written models' gaps (HookGapTests: SPIREQOL_UPDATE_SNAPSHOT=1 regenerates). Beta. Each hook body's first gap, by capability.\n");
        text.Append($"# {byClass.Count} hand-written models.\n\n## Capabilities, by the classes each blocks (alone: the classes it's the last one for)\n\n");
        var capabilities = byClass.SelectMany(entry => entry.Gaps.Select(gap => gap.Capability).Distinct().Select(capability => (capability, entry.Class, Alone: entry.Gaps.All(gap => gap.Capability == capability))))
            .GroupBy(entry => entry.capability).OrderByDescending(group => group.Count(entry => entry.Alone)).ThenByDescending(group => group.Count()).ThenBy(group => group.Key, StringComparer.Ordinal);
        foreach (var group in capabilities)
            text.Append($"{group.Key}: {group.Count()} classes, {group.Count(entry => entry.Alone)} alone: {string.Join(", ", group.Select(entry => entry.Alone ? entry.Class + "*" : entry.Class))}\n");
        text.Append("\n## Classes, each hook body's gap in reading order\n\n");
        foreach (var (name, gaps) in byClass)
            text.Append($"{name}: {string.Join(" | ", gaps.Select(gap => $"[{gap.Capability}] {gap.Gap}"))}\n");
        var path = Path.Combine(HookProgramTests.RepoRoot(), "tests", "SpireQoL.Catalog.Tests", "Snapshots", "hook-gaps.txt");
        if (Environment.GetEnvironmentVariable("SPIREQOL_UPDATE_SNAPSHOT") == "1")
        {
            File.WriteAllText(path, text.ToString());
            return;
        }
        Assert.True(File.Exists(path), $"missing {path}: generate it with SPIREQOL_UPDATE_SNAPSHOT=1");
        Assert.True(File.ReadAllText(path).ReplaceLineEndings("\n") == text.ToString(), $"{path} isn't the hand-written models' gaps: regenerate it with SPIREQOL_UPDATE_SNAPSHOT=1 and explain the change");
    }

    /// <summary>A gap's text cut short (a Modify hook's whole ways can run to pages: Vulnerable's).</summary>
    private static string Shortened(string gap) => gap.Length <= 160 ? gap : gap[..157] + "...";

    private static bool Derives(Type type, string baseName)
    {
        for (var current = type.BaseType; current != null; current = current.BaseType)
            if (current.Name == baseName) return true;
        return false;
    }
}
