using System.Text;
using Xunit;

namespace SpireQoL.Sim.Tests;

/// <summary>
/// What Magic Bomb (its amount to its owner as their side's turn ends, while the enemy that applied it lives, then it goes) does to a card's play:
/// 200 generated fights, Magic Bomb on you or a teammate (sometimes both), each applied by one of one to three enemies (some low on HP, some with
/// block, its applier sometimes dead already), you with block or not; an Attack aimed at each enemy, at all of them or at a random one, or a Skill
/// that gives you block, each simulated against ending the turn now and read into its tabs by the rules the game shows them by (CardTabs: every
/// tab's key, crossing, number, chance and aim marker, in order, and what the play check holds the play to), with each player's HP and whether
/// they still have Magic Bomb at the end of the turn, over the outcomes. Recorded with the hand-written model (Cases/magic-bomb-tab-parity.txt,
/// SPIREQOL_RECORD_MAGIC_BOMB_TABS=path writes it): read from its code, it shows the same, but where its code places it otherwise (ReviewedTabs:
/// each a fix, with the game's line).
/// </summary>
public class MagicBombTabParityTests
{
    private const int Scenarios = 200;

    private static string ParityPath => Path.Combine(AppContext.BaseDirectory, "Cases", "magic-bomb-tab-parity.txt");

    /// <summary>Scenarios a class read from its code places otherwise, each a fix: the scenario, and the game's line that moves them.</summary>
    private static readonly Dictionary<int, string> ReviewedTabs = new();

    [Fact]
    public void TheTabsAreWhatTheHandWrittenModelShowed()
    {
        var actual = Enumerable.Range(0, Scenarios).Select(scenario => $"{scenario} {Tabs(scenario)}").ToList();
        if (Environment.GetEnvironmentVariable("SPIREQOL_RECORD_MAGIC_BOMB_TABS") is { Length: > 0 } recordTo)
        {
            File.WriteAllLines(recordTo, actual);
            return;
        }
        var recorded = File.ReadAllLines(ParityPath).ToList();
        Assert.Equal(recorded.Count, actual.Count);
        // Whether a player still has Magic Bomb is held apart (Reviewed: its code takes it off as its applier dies); the tabs and the HP as recorded.
        var expected = recorded.Select(WithoutTheBomb).ToList();
        var changed = actual.Select(WithoutTheBomb).Zip(expected, (now, was) => (Now: now, Was: was)).Select((pair, scenario) => (pair.Now, pair.Was, Scenario: scenario))
            .Where(entry => entry.Now != entry.Was && !ReviewedTabs.ContainsKey(entry.Scenario)).Select(entry => $"{entry.Was}\n  now: {entry.Now}").ToList();
        Assert.True(changed.Count == 0, $"the tabs changed in {changed.Count} of {actual.Count} scenarios:\n{string.Join("\n", changed.Take(6))}");
        var stale = ReviewedTabs.Keys.Where(scenario => WithoutTheBomb(actual[scenario]) == expected[scenario]).ToList();
        Assert.True(stale.Count == 0, $"reviewed, but the tabs are as recorded: {string.Join(", ", stale)}");
        var bombs = Enumerable.Range(0, Scenarios).SelectMany(scenario => Reviewed(scenario, Bombs(recorded[scenario]), Bombs(actual[scenario]))).ToList();
        Assert.True(bombs.Count == 0, $"Magic Bomb kept or taken off otherwise than its applier's death:\n{string.Join("\n", bombs.Take(6))}");
    }

    private static string WithoutTheBomb(string line) => System.Text.RegularExpressions.Regex.Replace(line, " bomb [01]+;", ";");

    private static List<string> Bombs(string line) => System.Text.RegularExpressions.Regex.Matches(line, " bomb ([01]+);").Select(match => match.Groups[1].Value).ToList();

    /// <summary>
    /// The bomb columns as reviewed: MagicBombPower.AfterDeath, `!wasRemovalPrevented &amp;&amp; creature == base.Applier` → PowerCmd.Remove(this): the read
    /// model takes Magic Bomb off as its applier dies, where the hand-written model kept it and did nothing with it (its AfterSideTurnEnd waits on its
    /// applier alive, so no damage changes). A Magic Bomb the recording kept that's gone now is one whose applier is dead in every outcome; any
    /// other change is listed.
    /// </summary>
    private static IEnumerable<string> Reviewed(int scenario, List<string> was, List<string> now)
    {
        var (state, play, enemies) = Scenario(scenario);
        var outcomes = Outcomes(state, play, enemies);
        var players = state.PlayerIds.ToList();
        for (var column = 0; column < was.Count; column++)
            for (var aim = 0; aim < was[column].Length; aim++)
            {
                if (was[column][aim] == now[column][aim]) continue;
                var player = players[column];
                var applier = DataCreature.In(state.Player(player)!.Powers.DataOf(PowerId.MagicBomb));
                if (was[column][aim] == '1' && outcomes[aim].InEvery(branch => branch.Enemy(applier) is not { Alive: true })) continue;
                yield return $"{scenario}: player {player}, outcome {aim}: {was[column][aim]} now {now[column][aim]}";
            }
    }

    [Fact]
    public void TheScenariosShowTheBombAtWork()
    {
        // The recording is only a proof where the bomb goes off in some outcomes and not in others: a play that kills its applier stops it.
        var lines = Enumerable.Range(0, Scenarios).Select(Tabs).ToList();
        Assert.Contains(lines, line => line.Contains(" -2 hp ", StringComparison.Ordinal));
        var stopped = Enumerable.Range(0, Scenarios).Count(scenario =>
        {
            var (state, play, _) = Scenario(scenario);
            return SimEngine.Run(state, play).Branches.Sum(branch => branch.You.Hp) != SimEngine.Run(state).Branches.Sum(branch => branch.You.Hp);
        });
        Assert.True(stopped > 30, $"only {stopped} scenarios end otherwise than ending the turn now");
    }

    /// <summary>One generated fight and its play.</summary>
    internal static (SimState State, SimPlay Play, int Enemies) Scenario(int index)
    {
        var random = new Random(6100 + index);
        var state = new SimState { TurnNumber = 1 };
        var enemies = random.Next(1, 4);
        for (var i = 0; i < enemies; i++)
        {
            var enemy = new SimEnemy { Id = i, Name = $"E{i}", Hp = random.Next(1, 30), Block = random.Next(3) == 0 ? random.Next(1, 10) : 0,
                Hits = Enumerable.Range(0, random.Next(0, 2)).Select(_ => random.Next(1, 8)).ToList() };
            enemy.IntendsToAttack = enemy.Hits.Count > 0;
            if (random.Next(8) == 0) { enemy.Hp = 0; enemy.Hittable = false; }
            state.Enemies.Add(enemy);
        }
        state.You.Hp = random.Next(5, 50);
        state.You.MaxHp = 70;
        state.You.Energy = 3;
        state.You.HandCount = 3;
        if (random.Next(2) == 0) state.You.Block = random.Next(1, 15);
        var teammate = random.Next(3) == 0;
        if (teammate) state.Teammates.Add(new SimPlayer { Hp = random.Next(5, 40), MaxHp = 60, HandCount = 2, Energy = 2, Block = random.Next(2) == 0 ? random.Next(1, 15) : 0 });
        var on = teammate ? random.Next(3) : 0;
        if (on != 1) Bomb(state.You, amount: random.Next(3, 16), applier: random.Next(enemies));
        if (on != 0) Bomb(state.Teammates[0], amount: random.Next(3, 16), applier: random.Next(enemies));
        SimPlay play;
        if (random.Next(5) == 0)
        {
            play = new SimPlay { Name = "Played", Card = new SimCardPlay(new SimCard("Played", SimCardType.Skill) { Energy = 1, GainsBlock = true }, Who.You, Who.None, PlayIndex: 0, PlayCount: 1, AutoPlay: false) };
            play.BlockAmount = random.Next(1, 15);
            play.BlockGains = 1;
        }
        else
        {
            play = new SimPlay { Name = "Played", IsAttack = true, Card = new SimCardPlay(new SimCard("Played", SimCardType.Attack) { Energy = 1 }, Who.You, Who.None, PlayIndex: 0, PlayCount: 1, AutoPlay: false) };
            play.HitTarget = random.Next(3) switch { 0 => SimPlay.Target.Single, 1 => SimPlay.Target.All, _ => SimPlay.Target.Random };
            play.Hits = random.Next(1, 4);
            for (var i = 0; i < enemies; i++) play.HitDamage[i] = random.Next(1, 20);
        }
        return (state, play, enemies);
    }

    /// <summary>
    /// Magic Bomb on a player at <paramref name="amount"/>, applied by enemy <paramref name="applier"/>: its applier as its Data (the hand-written
    /// model's and a read model's that asks who applied it: SimReaders.Read), and its one copy among its owner's fields (an instanced power's).
    /// </summary>
    internal static void Bomb(SimPlayer player, int amount, int applier)
    {
        player.Powers = player.Powers.With(PowerId.MagicBomb, amount, data: DataCreature.Of(applier));
        player.Fields = player.Fields.With(PowerId.MagicBomb.Name + "@1", amount, DataCreature.Of(applier));
    }

    /// <summary>Each aim's outcome (each living enemy for a card aimed at one, else one), then ending the turn now's.</summary>
    private static SimOutcome[] Outcomes(SimState state, SimPlay play, int enemies)
    {
        var aims = play.HitTarget == SimPlay.Target.Single ? Enumerable.Range(0, enemies).Where(enemy => state.Enemy(enemy)!.Alive).DefaultIfEmpty(0).ToList() : new List<int> { Who.None };
        return aims.Select(aim =>
        {
            play.TargetId = aim == Who.None ? 0 : aim;
            return SimEngine.Run(state, play);
        }).Append(SimEngine.Run(state)).ToArray();
    }

    /// <summary>One generated fight and play: its tabs, and each player's HP and Magic Bomb at the end of each aim's turn, as text.</summary>
    internal static string Tabs(int index)
    {
        var (state, play, enemies) = Scenario(index);
        var all = Outcomes(state, play, enemies);
        var (outcomes, baseline) = (all[..^1], all[^1]);
        var sources = new List<TabSource>();
        foreach (var player in state.PlayerIds)
            foreach (var power in state.Player(player)!.Powers.Items) sources.Add(new TabSource(power.Name.Name, power.Name.Name, player, OutcomePass: 0));
        var tabs = new CardTabs { Indirect = index % 2 == 0, Chances = true, ProcNumbers = true };
        tabs.Add(sources, state, baseline, outcomes);
        var text = new StringBuilder();
        foreach (var tab in tabs.Tabs.Values)
            text.Append($"{tab.Key} {(tab.Crossed ? "crossed" : "plain")}{(tab.Chance != null ? " chance " + tab.Chance : "")}{(tab.Number is { } number ? $" {tab.Counted} {number}" : "")}"
                + $" amount {tab.Amount} {tab.AuditMin}-{tab.AuditMax}{(tab.Aimed ? " aimed" : "")}; ");
        text.Append("| checked ");
        foreach (var (source, (every, any)) in tabs.FiresBy.OrderBy(entry => entry.Key, StringComparer.Ordinal))
            text.Append($"{source} {string.Concat(every.Select(fires => fires ? '1' : '0'))}/{string.Concat(any.Select(fires => fires ? '1' : '0'))}; ");
        // Each player's HP, least and most, and whether every outcome leaves them Magic Bomb, for each aim's play and then ending the turn now.
        text.Append("| players ");
        string Range(SimOutcome outcome, Func<SimState, int> hp) => outcome.Branches.Count == 0 ? "-" : $"{outcome.Branches.Min(hp)}-{outcome.Branches.Max(hp)}";
        foreach (var player in state.PlayerIds)
            text.Append($"{player} hp {string.Join(",", outcomes.Append(baseline).Select(outcome => Range(outcome, branch => branch.Player(player)!.Hp)))}"
                + $" bomb {string.Concat(outcomes.Append(baseline).Select(outcome => outcome.InEvery(branch => branch.Player(player)!.Powers.Has(PowerId.MagicBomb)) ? '1' : '0'))}; ");
        return text.ToString();
    }
}
