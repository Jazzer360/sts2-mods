using System.Text;
using Xunit;

namespace SpireQoL.Sim.Tests;

/// <summary>
/// The tabs of the relics and powers whose tab says a play gives up or sets up a bonus, or reacts to a hit (Art of War, Ripple Basin, Parrying
/// Shield, Emotion Chip, Demon Tongue, Self-Forming Clay, Monarch's Gaze, Conqueror, Slippery, Rupture), as a card's play shows them: 400 generated
/// fights, you (and sometimes a teammate) with those relics and powers, enemies with Slippery, Conqueror and Thorns, a card played (an Attack aimed at
/// each enemy, a Skill, a Power, Sovereign Blade, block, an HP cost), each simulated against ending the turn now and read into its tabs by the
/// rules the game shows them by (CardTabs: every tab's key, crossing, number, chance and aim marker, in order, and what the play check holds the play
/// to). Recorded with the hand-written models (Cases/bonus-tab-parity.txt, SPIREQOL_RECORD_BONUS_TABS=path writes it): a class read from its code
/// must show the same tabs.
/// </summary>
public class BonusTabParityTests
{
    private const int Scenarios = 400;

    private static string ParityPath => Path.Combine(AppContext.BaseDirectory, "Cases", "bonus-tab-parity.txt");

    private static readonly RelicId[] Relics =
    {
        RelicId.ArtOfWar, RelicId.RippleBasin, RelicId.ParryingShield, RelicId.EmotionChip, RelicId.DemonTongue, RelicId.SelfFormingClay,
    };

    [Fact]
    public void TheTabsAreWhatTheHandWrittenModelsShowed()
    {
        var actual = Enumerable.Range(0, Scenarios).Select(scenario => $"{scenario} {Tabs(scenario)}").ToList();
        if (Environment.GetEnvironmentVariable("SPIREQOL_RECORD_BONUS_TABS") is { Length: > 0 } recordTo)
        {
            File.WriteAllLines(recordTo, actual);
            return;
        }
        var expected = File.ReadAllLines(ParityPath).Select(line => ReviewedTabs.Values.Aggregate(line, (rewritten, reviewed) => reviewed.Recorded(rewritten))).ToArray();
        var changed = actual.Zip(expected).Where(pair => Compared(pair.First) != Compared(pair.Second)).Select(pair => $"{pair.Second}\n  now: {pair.First}").ToList();
        Assert.True(expected.Length == actual.Count && changed.Count == 0, $"the tabs changed in {changed.Count} of {actual.Count} scenarios:\n{string.Join("\n", changed.Take(6))}");
    }

    /// <summary>
    /// What the play check holds a class to, where reading it from its code changed it on purpose (the tabs never change): the class, and why. A
    /// read model is held to its code's flashes (decision #199), a hand-written one to its acting log.
    /// </summary>
    private static readonly Dictionary<string, string> ReviewedChecks = new(StringComparer.Ordinal)
    {
        ["MonarchsGazePower"] = "its code never flashes (MonarchsGazePower.AfterDamageGiven: PowerCmd.Apply alone), so the play check no longer expects a flash where its tab shows",
        ["ArtOfWar"] = "its crossed tab is held to the relic going dark (ActedSeen.StatusCleared: GivenUpIssues), never to a flash; a read model's entry here is its code's flashes during the play, of which it has none (ArtOfWar.AfterCardPlayed sets its Status alone)",
        ["RippleBasin"] = "its crossed tab is held to the relic going dark (ActedSeen.StatusCleared: GivenUpIssues), never to a flash; a read model's entry here is its code's flashes during the play, of which it has none (RippleBasin.AfterCardPlayed sets its Status alone)",
    };

    /// <summary>
    /// The tabs the owner changed on purpose since the recording (decision #250: what each source's settings row shows), each class's tabs as the
    /// recording had them, rewritten to what the row shows now: the class, why, and the rewrite of a recorded line. Every other tab of every
    /// scenario stays as recorded, and each class's new tabs are held to the game's code on boards of their own (BonusTabRowTests).
    /// </summary>
    private static readonly Dictionary<string, (string Why, Func<string, string> Recorded)> ReviewedTabs = new(StringComparer.Ordinal)
    {
        ["ParryingShield"] = ("its row shows the play that sets it up only (TabShows.SetsUp): no crossed tab on a play that stops it",
            line => System.Text.RegularExpressions.Regex.Replace(line, @"proc:ParryingShield crossed[^;]*; ", "")),
    };

    /// <summary>
    /// A scenario's line as compared: the play check's entry for a reviewed class left out, and every entry no aim sets off (the play check reads
    /// only the aims a source is set off at: one with none is the same as none).
    /// </summary>
    private static string Compared(string line)
    {
        foreach (var name in ReviewedChecks.Keys)
            line = System.Text.RegularExpressions.Regex.Replace(line, $@"(\| checked [^|]*?){name} [01]+/[01]+; ", "$1");
        while (System.Text.RegularExpressions.Regex.Match(line, @"(\| checked [^|]*?)\w+ 0+/0+; ") is { Success: true } none)
            line = line.Remove(none.Index + none.Groups[1].Length, none.Length - none.Groups[1].Length);
        return line;
    }

    [Fact]
    public void TheScenariosShowEachTab()
    {
        // The recording is only a proof where the tabs show: every source has a tab in some scenario, crossed or plain where it can be either.
        var lines = Enumerable.Range(0, Scenarios).Select(Tabs).ToList();
        foreach (var name in new[] { "ArtOfWar", "RippleBasin", "ParryingShield", "EmotionChip", "DemonTongue", "SelfFormingClay", "MonarchsGazePower", "ConquerorPower", "SlipperyPower", "RupturePower" })
            Assert.True(lines.Any(line => line.Contains($"proc:{name} ", StringComparison.Ordinal)), $"no scenario shows {name}'s tab");
        Assert.Contains(lines, line => line.Contains("proc:ParryingShield plain", StringComparison.Ordinal));
        // Each reviewed change took something the recording showed (the rewrite isn't idle).
        var recorded = File.ReadAllLines(ParityPath);
        foreach (var (name, reviewed) in ReviewedTabs)
            Assert.True(recorded.Any(line => reviewed.Recorded(line) != line), $"{name}'s reviewed change rewrites nothing the recording has");
        Assert.Contains(lines, line => line.Contains("proc:SlipperyPower crossed chance", StringComparison.Ordinal));
        Assert.Contains(lines, line => line.Contains("aimed", StringComparison.Ordinal));
    }

    /// <summary>One generated fight and play: its tabs, as text.</summary>
    private static string Tabs(int index)
    {
        var random = new Random(7700 + index);
        var state = new SimState { TurnNumber = random.Next(1, 4) };
        var enemies = random.Next(1, 4);
        for (var i = 0; i < enemies; i++)
        {
            var enemy = new SimEnemy { Id = i, Name = $"E{i}", Hp = random.Next(1, 40), Block = random.Next(3) == 0 ? random.Next(1, 10) : 0,
                Hits = Enumerable.Range(0, random.Next(0, 3)).Select(_ => random.Next(1, 12)).ToList() };
            if (random.Next(5) == 0) enemy.Thorns = random.Next(1, 4);
            if (random.Next(3) == 0) enemy.Powers = enemy.Powers.With(PowerId.Slippery, amount: random.Next(1, 4));
            if (random.Next(4) == 0) enemy.Powers = enemy.Powers.With(PowerId.Conqueror, amount: random.Next(1, 3));
            state.Enemies.Add(enemy);
        }
        Player(random, state, Who.You);
        if (random.Next(3) == 0)
        {
            state.Teammates.Add(new SimPlayer());
            Player(random, state, Who.Teammate(teammateIndex: 0));
        }
        var type = random.Next(4) switch { 0 or 1 => SimCardType.Attack, 2 => SimCardType.Skill, _ => SimCardType.Power };
        var name = type == SimCardType.Attack && random.Next(3) == 0 ? CardId.SovereignBlade.Name : "Played";
        var play = new SimPlay { Name = name, IsAttack = type == SimCardType.Attack, Card = new SimCardPlay(new SimCard(name, type) { Energy = random.Next(0, 3) }, Who.You, Who.None, PlayIndex: 0, PlayCount: 1, AutoPlay: false) };
        if (play.IsAttack)
        {
            play.HitTarget = random.Next(3) switch { 0 => SimPlay.Target.Single, 1 => SimPlay.Target.All, _ => SimPlay.Target.Random };
            play.Hits = random.Next(1, 4);
            for (var i = 0; i < enemies; i++) play.HitDamage[i] = random.Next(1, 14);
        }
        if (random.Next(3) == 0) { play.BlockGains = 1; play.BlockAmount = random.Next(2, 14); }
        if (random.Next(5) == 0) { play.HpCost = random.Next(1, 5); play.HpCostTimes = 1; }
        if (random.Next(8) == 0) play.AddThorns = random.Next(1, 4);
        // One simulation per aim: each enemy for a card aimed at one, else one.
        var aims = play.HitTarget == SimPlay.Target.Single ? Enumerable.Range(0, enemies).ToList() : new List<int> { Who.None };
        var outcomes = aims.Select(aim =>
        {
            play.TargetId = aim == Who.None ? (enemies > 0 ? 0 : Who.None) : aim;
            if (aim == Who.None && !play.IsAttack) play.TargetId = Who.None;
            return SimEngine.Run(state, play);
        }).ToArray();
        var baseline = SimEngine.Run(state);
        // The sources as CardPredictions lists them: your powers, your relics, every enemy's powers, every teammate's powers and relics.
        // Each with what its settings row shows of its bonus (decision #250), as CardPredictions gives it.
        var sources = new List<TabSource>();
        void Add(SimPlayer player, int? owner)
        {
            foreach (var power in player.Powers.Items) sources.Add(new TabSource(power.Name.Name, power.Name.Name, owner, OutcomePass: 2, Shows: TabRowsFile.ShowsOf(power.Name.Name)));
            foreach (var relic in player.Relics.Items) sources.Add(new TabSource(relic.Name.Name, relic.Name.Name, owner, OutcomePass: 1, Shows: TabRowsFile.ShowsOf(relic.Name.Name)));
        }
        Add(state.You, owner: null);
        foreach (var enemy in state.Enemies)
            foreach (var power in enemy.Powers.Items) sources.Add(new TabSource(power.Name.Name, power.Name.Name, enemy.Id, OutcomePass: 0, Shows: TabRowsFile.ShowsOf(power.Name.Name)));
        for (var i = 0; i < state.Teammates.Count; i++) Add(state.Teammates[i], Who.Teammate(i));
        var tabs = new CardTabs { Indirect = index % 2 == 0, Chances = true, ProcNumbers = true };
        tabs.Add(sources, state, baseline, outcomes);
        var text = new StringBuilder();
        foreach (var tab in tabs.Tabs.Values)
            text.Append($"{tab.Key} {(tab.Crossed ? "crossed" : "plain")}{(tab.Chance != null ? " chance " + tab.Chance : "")}{(tab.Number is { } number ? $" {tab.Counted} {number}" : "")}"
                + $" amount {tab.Amount} {tab.AuditMin}-{tab.AuditMax}{(tab.Aimed ? " aimed" : "")}; ");
        text.Append("| checked ");
        foreach (var (source, (every, any)) in tabs.FiresBy.OrderBy(entry => entry.Key, StringComparer.Ordinal))
            text.Append($"{source} {string.Concat(every.Select(fires => fires ? '1' : '0'))}/{string.Concat(any.Select(fires => fires ? '1' : '0'))}; ");
        // How the play check sees each source go off (a flash, the relic going dark) and its crossing for its owner.
        text.Append("| seen ");
        foreach (var source in sources.Select(source => (source.Model, Owner: source.Owner ?? Who.You)).Distinct())
            if (SimModels.Get(source.Model) is { } model)
                text.Append($"{source.Model}@{source.Owner} {model.Seen} {(model.HarmfulFor(source.Owner) ? "crossed" : "plain")}; ");
        return text.ToString();
    }

    /// <summary>A player with some of the relics and powers, their values as the adapter reads them (the hand-written models' and the code's).</summary>
    private static void Player(Random random, SimState state, int id)
    {
        var player = state.MutablePlayer(id);
        player.Hp = random.Next(5, 60);
        player.MaxHp = 70;
        player.Block = random.Next(3) == 0 ? random.Next(1, 16) : 0;
        player.Energy = random.Next(0, 4);
        player.HandCount = random.Next(0, 6);
        if (random.Next(4) == 0) player.Plating = random.Next(1, 6);
        if (random.Next(5) == 0) player.Thorns = random.Next(1, 4);
        // An Attack already played this turn (the fight's log and Art of War's own flag).
        var attacked = random.Next(3) == 0;
        if (attacked)
            player.Turn = SimKeys.CountPlay(SimKeys.CountPlay(player.Turn, PlayStage.Started, SimCardType.Attack, new HashSet<SimCardTag>()), PlayStage.Finished, SimCardType.Attack, new HashSet<SimCardTag>());
        void Var(string model, string key, int value) => state.Vars[$"{id}:{model}:{key}"] = value;
        foreach (var relic in Relics.OrderBy(_ => random.Next()))
        {
            if (random.Next(2) == 0) continue;
            var (amount, data) = (0, (int)RelicUse.Armed);
            if (relic == RelicId.ArtOfWar)
            {
                Var(relic.Name, "Energy", 1);
                SimEngine.SetField(state, id, relic.Name + ".AnyAttacksPlayedThisTurn", attacked ? 1 : 0);
            }
            else if (relic == RelicId.RippleBasin)
            {
                (amount, data) = (4, 0);
                Var(relic.Name, "Block", 4);
                Var(relic.Name, "Block.Props", (int)SimProps.Unpowered);
            }
            else if (relic == RelicId.ParryingShield)
            {
                (amount, data) = (6, 10);
                Var(relic.Name, "Damage", 6);
                Var(relic.Name, "Damage.Props", (int)SimProps.Unpowered);
                Var(relic.Name, "Block", 10);
                Var(relic.Name, "Block.Props", (int)SimProps.Unpowered);
            }
            else if (relic == RelicId.DemonTongue)
            {
                var used = random.Next(3) == 0;
                data = used ? (int)RelicUse.UsedThisTurn : 0;
                SimEngine.SetField(state, id, relic.Name + "._triggeredThisTurn", used ? 1 : 0);
            }
            else if (relic == RelicId.SelfFormingClay) Var(relic.Name, "BlockNextTurn", 3);
            player.Relics = player.Relics.With(relic, amount, data);
        }
        if (random.Next(3) == 0) player.Powers = player.Powers.With(PowerId.MonarchsGaze, amount: random.Next(1, 4));
        if (random.Next(4) == 0) player.Powers = player.Powers.With(PowerId.Rupture, amount: random.Next(1, 3));
    }
}
