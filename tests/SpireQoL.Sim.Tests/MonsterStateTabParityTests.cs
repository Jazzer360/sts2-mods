using System.Text;
using Xunit;

namespace SpireQoL.Sim.Tests;

/// <summary>
/// The tabs of the enemy powers that change their monster's own state (the monster-state group: Imbalanced, Plow, Asleep, Slumber, Curl Up,
/// Infested, Steam Eruption, Stock), as a card's play shows them: 300 generated fights, enemies with those powers (some with block, some low on
/// HP, some attacking), you (sometimes with block) and sometimes a teammate, an Attack aimed at each enemy, at all of them or at a random one, or a
/// Skill that gives you block, each simulated against ending the turn now and read into its tabs by the rules the game shows them by (CardTabs:
/// every tab's key, crossing, number, chance and aim marker, in order, and what the play check holds the play to). Recorded with the hand-written
/// models (Cases/monster-state-tab-parity.txt, SPIREQOL_RECORD_MONSTER_STATE_TABS=path writes it): a class read from its code shows the same tabs,
/// but where its code places them otherwise (ReviewedTabs: each a fix, with the game's line it follows).
/// </summary>
public class MonsterStateTabParityTests
{
    private const int Scenarios = 300;

    private static string ParityPath => Path.Combine(AppContext.BaseDirectory, "Cases", "monster-state-tab-parity.txt");

    /// <summary>The powers the fights give their enemies, with the amounts each is given.</summary>
    private static readonly PowerId[] Group =
    {
        PowerId.Imbalanced, PowerId.Plow, PowerId.Asleep, PowerId.Slumber, PowerId.CurlUp, PowerId.Infested, PowerId.SteamEruption, PowerId.Stock,
    };

    /// <summary>
    /// Scenarios whose tabs a class read from its code places otherwise, each a fix: the scenario, and the game's line that moves them. Every other
    /// scenario is held as recorded.
    /// </summary>
    private static readonly Dictionary<int, string> ReviewedTabs = new()
    {
        // ImbalancedPower.AfterDamageGiven: `dealer == base.Owner && result.WasFullyBlocked`, for every player's result.
        [169] = "Imbalanced: the play kills the Asleep enemy whose attack would have spent your teammate's 11 block, so the block takes the Imbalanced enemy's 9 whole: stunned in every outcome (a teammate's full block sets it off, where the hand-written model judged only your first hit)",
        // SteamEruptionPower.AfterDeath: TriggerAboutToBlowState's CreatureCmd.SetMaxAndCurrentHp(base.Creature, 999999999m): the enemy it kills is in the
        // fight again (ShouldCreatureBeRemovedFromCombatAfterDeath false for it), so the random attack's later hits can land on it (decision #276).
        [35] = "Steam Eruption: a random hit that kills enemy 0 brings it back, a target for the later hits: fewer kill the Infested enemy or reach the Slumber one (32% to 26%)",
        [163] = "Steam Eruption: a random hit that kills enemy 2 brings it back, a target for the later hits: fewer reach the Asleep and Curl Up enemy (72% to 70%)",
        [209] = "Steam Eruption: a random hit that kills enemy 0 brings it back, a target for the 4-hit attack's later hits: fewer reach the Curl Up enemy (73% to 69%)",
    };

    /// <summary>
    /// The recording as reviewed: what the play check holds a play to (FiresBy) for a read model is its code's flashes (decision #199), and
    /// CurlUpPower's hooks call no Flash(): none expected, where the hand-written model expected one wherever it acted (decision #274). Its tabs are
    /// unchanged.
    /// </summary>
    private static string Reviewed(string line) => System.Text.RegularExpressions.Regex.Replace(line, @"(?<=\| checked [^|]*)(CurlUpPower) ([01]+)/([01]+)",
        match => match.Groups[1].Value + " " + new string('0', match.Groups[2].Length) + "/" + new string('0', match.Groups[3].Length));


    /// <summary>
    /// A line without Asleep's and Slumber's tabs and play checks (decision #307: their tab compares the enemy's next move with and without the
    /// play, held by WakeTabParityTests): every other tab is held as recorded with the hand-written models.
    /// </summary>
    internal static string WithoutWakeTabs(string line) => System.Text.RegularExpressions.Regex.Replace(line, @"(proc:(AsleepPower|SlumberPower) [^;]*; )|((?<=| checked [^|]*)(AsleepPower|SlumberPower) [01]+/[01]+; )", "");

    [Fact]
    public void TheTabsAreWhatTheHandWrittenModelsShowed()
    {
        var actual = Enumerable.Range(0, Scenarios).Select(scenario => $"{scenario} {Tabs(scenario)}").ToList();
        if (Environment.GetEnvironmentVariable("SPIREQOL_RECORD_MONSTER_STATE_TABS") is { Length: > 0 } recordTo)
        {
            File.WriteAllLines(recordTo, actual);
            return;
        }
        // The tabs and what the play check holds the play to; what the simulations end with for each enemy (its column, as recorded) is shown, not
        // held: a class read from its code simulates what the hand-written model only judged (Imbalanced's stun now lands in the enemy turn).
        static string TabsOf(string line) => line.Split(" | enemies ")[0];
        var expected = File.ReadAllLines(ParityPath).Select(TabsOf).Select(Reviewed).Select(WithoutWakeTabs).ToList();
        actual = actual.Select(TabsOf).Select(WithoutWakeTabs).ToList();
        Assert.Equal(expected.Count, actual.Count);
        var changed = actual.Zip(expected, (now, was) => (Now: now, Was: was)).Select((pair, scenario) => (pair.Now, pair.Was, Scenario: scenario))
            .Where(entry => entry.Now != entry.Was && !ReviewedTabs.ContainsKey(entry.Scenario)).Select(entry => $"{entry.Was}\n  now: {entry.Now}").ToList();
        Assert.True(changed.Count == 0, $"the tabs changed in {changed.Count} of {actual.Count} scenarios:\n{string.Join("\n", changed.Take(6))}");
        var stale = ReviewedTabs.Keys.Where(scenario => actual[scenario] == expected[scenario]).ToList();
        Assert.True(stale.Count == 0, $"reviewed, but the tabs are as recorded: {string.Join(", ", stale)}");
    }

    [Fact]
    public void TheScenariosShowEachTab()
    {
        // The recording is only a proof where the tabs show: every source with a tab has one in some scenario.
        var lines = Enumerable.Range(0, Scenarios).Select(Tabs).ToList();
        foreach (var power in new[] { PowerId.Imbalanced, PowerId.Plow, PowerId.Asleep, PowerId.Slumber, PowerId.CurlUp, PowerId.Infested, PowerId.SteamEruption, PowerId.Stock })
            Assert.True(lines.Any(line => line.Contains($"proc:{power.Name} ", StringComparison.Ordinal)), $"no scenario shows {power.Name}'s tab");
        Assert.Contains(lines, line => line.Contains("aimed", StringComparison.Ordinal));
    }

    /// <summary>One generated fight and its play.</summary>
    internal static (SimState State, SimPlay Play, int Enemies) Scenario(int index)
    {
        var random = new Random(9400 + index);
        var state = new SimState { TurnNumber = 1 };
        var enemies = random.Next(1, 4);
        for (var i = 0; i < enemies; i++)
        {
            var enemy = new SimEnemy { Id = i, Name = $"E{i}", Hp = random.Next(1, 40), Block = random.Next(3) == 0 ? random.Next(1, 10) : 0,
                Hits = Enumerable.Range(0, random.Next(0, 3)).Select(_ => random.Next(1, 12)).ToList() };
            enemy.IntendsToAttack = enemy.Hits.Count > 0;
            var power = Group[random.Next(Group.Length)];
            var amount = power == PowerId.Plow ? random.Next(1, 30) : power == PowerId.Stock ? random.Next(0, 3) : power == PowerId.CurlUp ? random.Next(3, 9) : random.Next(1, 4);
            if (power == PowerId.Imbalanced || power == PowerId.Infested) amount = 1;
            if (amount > 0 || power != PowerId.Stock) enemy.Powers = enemy.Powers.With(power, amount);
            // A second power now and then: two of the group on one enemy.
            if (random.Next(5) == 0 && Group[random.Next(Group.Length)] is var second && second != power && second != PowerId.Stock && second != PowerId.Imbalanced)
                enemy.Powers = enemy.Powers.With(second, second == PowerId.Plow ? random.Next(1, 30) : second == PowerId.CurlUp ? random.Next(3, 9) : 1);
            // The move Steam Eruption's death sets, as the adapter reads it from the Waterfall Giant (SimEnemy.SetMoves: About to Blow, no attack and
            // nothing a simulation follows), so a revived enemy makes it (decision #276).
            if (enemy.Powers.Has(PowerId.SteamEruption)) enemy.SetMoves = new Dictionary<string, SimMove?> { ["AboutToBlowState"] = new SimMove(Array.Empty<int>(), Array.Empty<SimAction>()) };
            WakeTabParityTests.WithSleeperRules(enemy); // its move rules, as the adapter reads them for Asleep and Slumber (decision #307)
            state.Enemies.Add(enemy);
        }
        state.You.Hp = 50;
        state.You.MaxHp = 70;
        state.You.Energy = 3;
        state.You.HandCount = 3;
        if (random.Next(2) == 0) state.You.Block = random.Next(1, 15);
        if (random.Next(3) == 0) state.Teammates.Add(new SimPlayer { Hp = 40, MaxHp = 60, HandCount = 2, Energy = 2, Block = random.Next(2) == 0 ? random.Next(1, 15) : 0 });
        SimPlay play;
        if (random.Next(4) == 0)
        {
            // A Skill that gives you block.
            play = new SimPlay { Name = "Played", Card = new SimCardPlay(new SimCard("Played", SimCardType.Skill) { Energy = 1, GainsBlock = true }, Who.You, Who.None, PlayIndex: 0, PlayCount: 1, AutoPlay: false) };
            play.BlockAmount = random.Next(1, 15);
            play.BlockGains = 1;
        }
        else
        {
            play = new SimPlay { Name = "Played", IsAttack = true, Card = new SimCardPlay(new SimCard("Played", SimCardType.Attack) { Energy = 1 }, Who.You, Who.None, PlayIndex: 0, PlayCount: 1, AutoPlay: false) };
            play.HitTarget = random.Next(3) switch { 0 => SimPlay.Target.Single, 1 => SimPlay.Target.All, _ => SimPlay.Target.Random };
            play.Hits = random.Next(1, 5);
            for (var i = 0; i < enemies; i++) play.HitDamage[i] = random.Next(1, 20);
        }
        return (state, play, enemies);
    }

    /// <summary>One generated fight and play: its tabs, as text.</summary>
    internal static string Tabs(int index)
    {
        var (state, play, enemies) = Scenario(index);
        // One simulation per aim: each enemy for a card aimed at one, else one.
        var aims = play.HitTarget == SimPlay.Target.Single ? Enumerable.Range(0, enemies).ToList() : new List<int> { Who.None };
        var outcomes = aims.Select(aim =>
        {
            play.TargetId = aim == Who.None ? 0 : aim;
            return SimEngine.Run(state, play);
        }).ToArray();
        var baseline = SimEngine.Run(state);
        var sources = new List<TabSource>();
        foreach (var enemy in state.Enemies)
            foreach (var power in enemy.Powers.Items) sources.Add(new TabSource(power.Name.Name, power.Name.Name, enemy.Id, OutcomePass: 0));
        var tabs = new CardTabs { Indirect = index % 2 == 0, Chances = true, ProcNumbers = true };
        tabs.Add(sources, state, baseline, outcomes);
        var text = new StringBuilder();
        foreach (var tab in tabs.Tabs.Values)
            text.Append($"{tab.Key} {(tab.Crossed ? "crossed" : "plain")}{(tab.Chance != null ? " chance " + tab.Chance : "")}{(tab.Number is { } number ? $" {tab.Counted} {number}" : "")}"
                + $" amount {tab.Amount} {tab.AuditMin}-{tab.AuditMax}{(tab.Aimed ? " aimed" : "")}; ");
        text.Append("| checked ");
        foreach (var (source, (every, any)) in tabs.FiresBy.OrderBy(entry => entry.Key, StringComparer.Ordinal))
            text.Append($"{source} {string.Concat(every.Select(fires => fires ? '1' : '0'))}/{string.Concat(any.Select(fires => fires ? '1' : '0'))}; ");
        text.Append("| seen ");
        foreach (var source in sources.Select(source => (source.Model, Owner: source.Owner ?? Who.You)).Distinct())
            if (SimModels.Get(source.Model) is { } model)
                text.Append($"{source.Model}@{source.Owner} {model.Seen} {(model.HarmfulFor(source.Owner) ? "crossed" : "plain")}; ");
        // What the simulations end with for each enemy: stunned, alive, its block.
        text.Append("| enemies ");
        foreach (var enemy in state.Enemies)
            text.Append($"{enemy.Id} stunned {string.Concat(outcomes.Select(outcome => outcome.StunnedInAll(enemy.Id) ? '1' : '0'))}"
                + $" alive {string.Concat(outcomes.Select(outcome => outcome.InEvery(branch => branch.Enemy(enemy.Id) is { Alive: true }) ? '1' : '0'))}"
                + $" block {string.Join(",", outcomes.Select(outcome => outcome.Branches.Count == 0 ? 0 : outcome.Branches.Max(branch => branch.Enemy(enemy.Id)?.Block ?? 0)))}; ");
        return text.ToString();
    }
}
