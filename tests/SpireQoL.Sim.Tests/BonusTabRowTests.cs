using Xunit;

namespace SpireQoL.Sim.Tests;

/// <summary>
/// The bonus tabs as each source's settings row shows them (decision #244): the simulator works out both directions of a payout from the
/// code (decision #239), the row picks which are tabs (TabShows, from SettingsCatalog through ReadModels/tab-rows.json). Each source's tab
/// here is the one a card's play shows (CardTabs, the rules the game shows), on boards built from the game's own code's conditions.
/// </summary>
public class BonusTabRowTests
{
    /// <summary>
    /// A card's tabs as CardPredictions reads them: one simulation per aim (each enemy for a card aimed at one, else one), against ending the
    /// turn now, every source on the board with what its row shows.
    /// </summary>
    internal static CardTabs TabsOf(SimState state, SimPlay play, bool aimed = false, bool chances = true)
    {
        var aims = aimed ? state.Enemies.Select(enemy => enemy.Id).ToList() : new List<int> { play.TargetId };
        var outcomes = aims.Select(aim =>
        {
            play.TargetId = aim;
            return SimEngine.Run(state, play);
        }).ToArray();
        var baseline = SimEngine.Run(state);
        var sources = new List<TabSource>();
        foreach (var power in state.You.Powers.Items) sources.Add(Source(power.Name.Name, owner: null, pass: 2));
        foreach (var relic in state.You.Relics.Items) sources.Add(Source(relic.Name.Name, owner: null, pass: 1));
        foreach (var enemy in state.Enemies)
            foreach (var power in enemy.Powers.Items) sources.Add(Source(power.Name.Name, enemy.Id, pass: 0));
        var tabs = new CardTabs { Chances = chances, ProcNumbers = true };
        tabs.Add(sources, state, baseline, outcomes);
        return tabs;
    }

    private static TabSource Source(string name, int? owner, int pass) => new(name, name, owner, pass, Shows: TabRowsFile.ShowsOf(name));

    private static SourceTab? Tab(CardTabs tabs, string sourceClass) => tabs.Tabs.GetValueOrDefault("proc:" + sourceClass);

    // ---- Parrying Shield (ParryingShield.AfterSideTurnEnd: 6 damage to a random enemy when your block is at least its Block): its row shows the
    // play that sets it up, never one that stops it (the owner's call).

    private static SimState Parrying(int block)
    {
        var state = new SimState { Enemies = { new SimEnemy { Id = 1, Name = "E1", Hp = 50, Hits = { 3 } } } };
        state.You.Block = block;
        state.WithRelic(Who.You, RelicId.ParryingShield, "Block", value: 10);
        state.WithRelic(Who.You, RelicId.ParryingShield, "Damage", value: 6);
        return state;
    }

    [Fact]
    public void ParryingShieldShowsThePlayThatSetsItUp()
    {
        var tabs = TabsOf(Parrying(block: 5), new SimPlay { Name = "Defend", BlockGains = 1, BlockAmount = 5 });
        Assert.Equal(new SourceTab("proc:ParryingShield", Crossed: false), Tab(tabs, "ParryingShield"));
    }

    [Fact]
    public void ParryingShieldShowsNoTabOnAPlayThatStopsIt()
    {
        // 12 block: it fires ending the turn now. An attack on a 5-Thorns enemy takes 5 of it (7 left): it doesn't, and its row shows no warning.
        var state = Parrying(block: 12);
        state.Enemies[0].Thorns = 5;
        var play = new SimPlay { Name = "Strike", IsAttack = true, HitTarget = SimPlay.Target.Single, Hits = 1, TargetId = 1 };
        play.HitDamage[1] = 6;
        Assert.False(SimEngine.Run(state, play).PaidInSome(RelicId.ParryingShield.Name, owner: null));
        Assert.Null(Tab(TabsOf(state, play), "ParryingShield"));
    }

    private static SimState Board(int block = 0, int hand = 4)
    {
        var state = new SimState { Enemies = { new SimEnemy { Id = 1, Name = "E1", Hp = 80, Hits = { 3 } }, new SimEnemy { Id = 2, Name = "E2", Hp = 80, Hits = { 3 } } } };
        state.You.Block = block;
        state.You.HandCount = hand;
        return state;
    }

    private static SimPlay Defend => new() { Name = "Defend", BlockGains = 1, BlockAmount = 5 };
    private static SimPlay Skill => new() { Name = "Skill" };

    // ---- Orichalcum and Fake Orichalcum (BeforeSideTurnEndVeryEarly: ShouldTrigger when its owner's block is 0, before Plating; BeforeSideTurnEnd:
    // its Block): a play that gives you block when you'd end the turn with none gives it up. Each acts on its own.

    [Theory]
    [InlineData("Orichalcum")]
    [InlineData("FakeOrichalcum")]
    public void OrichalcumIsCrossedOnAPlayThatGivesYouBlock(string relic)
    {
        var state = Board().WithRelic(Who.You, RelicId.Of(relic), "Block", value: 6);
        Assert.Equal(new SourceTab("proc:" + relic, Crossed: true), Tab(TabsOf(state, Defend), relic));
        // A play that gains no block keeps it: no tab.
        Assert.Null(Tab(TabsOf(state, Skill), relic));
        // With block already, ending the turn now doesn't pay it, and a play can't set it up by giving more: no tab.
        Assert.Null(Tab(TabsOf(Board(block: 3).WithRelic(Who.You, RelicId.Of(relic), "Block", value: 6), Defend), relic));
    }

    [Fact]
    public void BothOrichalcumsAreGivenUpOnTheirOwn()
    {
        var state = Board().WithRelic(Who.You, RelicId.Orichalcum, "Block", value: 6).WithRelic(Who.You, RelicId.FakeOrichalcum, "Block", value: 3);
        var tabs = TabsOf(state, Defend);
        Assert.True(Tab(tabs, "Orichalcum")?.Crossed);
        Assert.True(Tab(tabs, "FakeOrichalcum")?.Crossed);
    }

    [Fact]
    public void PlatingKeepsOrichalcumArmedAsItChecksBeforePlating()
    {
        // Plating gives its block after Orichalcum's check: a play that only adds Plating doesn't give it up.
        var state = Board().WithRelic(Who.You, RelicId.Orichalcum, "Block", value: 6);
        Assert.Null(Tab(TabsOf(state, new SimPlay { Name = "Plated", AddPlating = 4 }), "Orichalcum"));
    }

    // ---- Cloak Clasp (BeforeSideTurnEnd: its Block per card in your hand) and Obsidian Vice (BeforeSideTurnEnd: its Damage per card in your hand
    // to a random enemy): every play that leaves fewer cards in your hand makes them smaller, a crossed tab with no number.

    [Theory]
    [InlineData("CloakClasp", "Block")]
    [InlineData("ObsidianVice", "Damage")]
    public void AHandRelicIsCrossedOnEveryPlayThatShrinksYourHand(string relic, string value)
    {
        var state = Board(hand: 4).WithRelic(Who.You, RelicId.Of(relic), value, value: 1);
        Assert.Equal(new SourceTab("proc:" + relic, Crossed: true), Tab(TabsOf(state, Skill), relic));
        // The last card in hand: it stops paying (still the warning).
        Assert.Equal(new SourceTab("proc:" + relic, Crossed: true), Tab(TabsOf(Board(hand: 1).WithRelic(Who.You, RelicId.Of(relic), value, value: 1), Skill), relic));
        // A card that brings two back for itself leaves more: no tab (its row shows no plain direction).
        Assert.Null(Tab(TabsOf(state, new SimPlay { Name = "Draw two", HandDelta = 1 }), relic));
        // One that leaves the hand as it was (it returns itself): no tab.
        Assert.Null(Tab(TabsOf(state, new SimPlay { Name = "Returns", HandDelta = 0 }), relic));
    }

    // ---- Screaming Flagon (BeforeSideTurnEnd: its Damage to every enemy when your hand is empty): plain on a play that leaves your hand empty.

    [Fact]
    public void ScreamingFlagonShowsThePlayThatEmptiesYourHand()
    {
        var last = Board(hand: 1).WithRelic(Who.You, RelicId.ScreamingFlagon, "Damage", value: 20);
        Assert.Equal(new SourceTab("proc:ScreamingFlagon", Crossed: false), Tab(TabsOf(last, Skill), "ScreamingFlagon"));
        Assert.Null(Tab(TabsOf(Board(hand: 3).WithRelic(Who.You, RelicId.ScreamingFlagon, "Damage", value: 20), Skill), "ScreamingFlagon"));
    }

    // ---- Hailstorm (BeforeSideTurnEnd: its amount to every enemy while you have at least its FrostOrbs Frost orbs): plain on the play that
    // channels your first Frost, crossed on one that evokes your last, with the chance when only some outcomes do (Chaos's random orb).

    private static SimState Defect(params OrbKind[] orbs)
    {
        var state = Board();
        state.You.OrbCapacity = Math.Max(1, orbs.Length);
        state.You.Orbs = orbs.Select(kind => new SimOrb { Kind = kind, Passive = 2, Evoke = 5 }).ToList();
        state.You.Powers = state.You.Powers.With(PowerId.Hailstorm, amount: 6);
        return state;
    }

    private static SimPlay Channel(OrbKind orb) => new() { Name = "Channel", Body = { new OrbAction(OrbAction.Op.Channel) { Orb = orb } } };

    [Fact]
    public void HailstormIsPlainOnThePlayThatChannelsYourFirstFrost()
    {
        var state = Defect();
        state.You.OrbCapacity = 3;
        Assert.Equal(new SourceTab("proc:HailstormPower", Crossed: false), Tab(TabsOf(state, Channel(OrbKind.Frost)), "HailstormPower"));
        Assert.Null(Tab(TabsOf(state, Channel(OrbKind.Lightning)), "HailstormPower"));
    }

    [Fact]
    public void HailstormIsCrossedOnThePlayThatEvokesYourLastFrost()
    {
        // One slot, a Frost in it: channelling Lightning evokes the Frost first.
        Assert.Equal(new SourceTab("proc:HailstormPower", Crossed: true), Tab(TabsOf(Defect(OrbKind.Frost), Channel(OrbKind.Lightning)), "HailstormPower"));
        // Channelling Frost pushes a Frost out and puts one in: still paid, no tab.
        Assert.Null(Tab(TabsOf(Defect(OrbKind.Frost), Channel(OrbKind.Frost)), "HailstormPower"));
    }

    [Fact]
    public void HailstormWithChaosShowsTheChanceItsRandomOrbGivesItUp()
    {
        // Chaos channels a random orb (OrbModel.GetRandomOrb: one of five) into a full slot holding your only Frost: in the four branches that
        // aren't Frost you end with none, 80%.
        var tab = Tab(TabsOf(Defect(OrbKind.Frost), Channel(OrbKind.Random)), "HailstormPower");
        Assert.Equal(new SourceTab("proc:HailstormPower", Crossed: true, Chance: "80%"), tab);
        // With no Frost, Chaos channels one in one branch of five: Hailstorm's row shows a bonus some outcomes set up with their chance (its
        // SetsUpByChance, the owner's decision #249), 20%; with Show chances off, set up in every outcome only, so no tab.
        var empty = Defect();
        empty.You.OrbCapacity = 3;
        Assert.Equal(new SourceTab("proc:HailstormPower", Crossed: false, Chance: "20%"), Tab(TabsOf(empty, Channel(OrbKind.Random)), "HailstormPower"));
        Assert.Null(Tab(TabsOf(empty, Channel(OrbKind.Random), chances: false), "HailstormPower"));
    }

    // ---- Tune (v0.114.0: AfterCardPlayed upgrades its amount of random upgradable cards in your hand when the card played is your first Ki this
    // turn, MelodyCmd.IsFirstPlayThisTurn): a plain tab on that play.

    private static SimPlay Ki(string name = "Ki Card") => new() { Name = name, Card = new SimCardPlay(new SimCard(name, SimCardType.Skill) { Tags = { SimCardTag.Ki } }, Who.You, Who.None, PlayIndex: 0, PlayCount: 1, AutoPlay: false) };

    private static SimState Tuned()
    {
        var state = Board();
        state.You.Powers = state.You.Powers.With(PowerId.Tune, amount: 1);
        return state;
    }

    [Fact]
    public void TuneShowsTheTurnsFirstKi()
    {
        Assert.Equal(new SourceTab("proc:TunePower", Crossed: false), Tab(TabsOf(Tuned(), Ki()), "TunePower"));
        // A card that isn't a Ki: no tab.
        Assert.Null(Tab(TabsOf(Tuned(), Skill), "TunePower"));
    }

    [Fact]
    public void TuneShowsNoTabOnceAKiWasPlayedThisTurn()
    {
        var state = Tuned();
        state.You.Turn = SimKeys.CountPlay(SimKeys.CountPlay(state.You.Turn, PlayStage.Started, SimCardType.Skill, new[] { SimCardTag.Ki }), PlayStage.Finished, SimCardType.Skill, new[] { SimCardTag.Ki });
        Assert.Null(Tab(TabsOf(state, Ki()), "TunePower"));
    }

    [Fact]
    public void OnGeneratedBoardsTunesTabIsWhatItsCodeDidOnThePlay()
    {
        // TunePower.AfterCardPlayed (v0.114.0) flashes and upgrades random cards in your hand when the card played is your first Ki this turn: its tab
        // is plain where the play flashes it in every outcome, and ending the turn now never does.
        var seen = new HashSet<bool>();
        for (var scenario = 0; scenario < 120; scenario++)
        {
            var random = new Random(9500 + scenario);
            var state = Tuned();
            state.You.HandCount = random.Next(1, 5);
            var played = random.Next(3) == 0 ? new[] { SimCardTag.Ki } : random.Next(2) == 0 ? new[] { SimCardTag.Cho } : Array.Empty<SimCardTag>();
            if (played.Length > 0 || random.Next(2) == 0)
                state.You.Turn = SimKeys.CountPlay(SimKeys.CountPlay(state.You.Turn, PlayStage.Started, SimCardType.Skill, played), PlayStage.Finished, SimCardType.Skill, played);
            var tag = random.Next(3) switch { 0 => SimCardTag.Ki, 1 => SimCardTag.Wah, _ => SimCardTag.Shiv };
            var play = new SimPlay { Name = "Card", Card = new SimCardPlay(new SimCard("Card", SimCardType.Attack) { Tags = { tag } }, Who.You, Who.None, PlayIndex: 0, PlayCount: 1, AutoPlay: false) };
            bool Flashed(SimState branch) => branch.FlashedAny.Has(PowerId.Tune.Name);
            Assert.False(SimEngine.Run(state).InEvery(Flashed));
            var expected = SimEngine.Run(state, play).InEvery(Flashed);
            var tab = Tab(TabsOf(state, play), "TunePower");
            Assert.True(expected == (tab != null), $"scenario {scenario}: expected {(expected ? "a plain tab" : "none")}, shown {tab}");
            if (tab != null) Assert.False(tab.Crossed);
            seen.Add(expected);
        }
        Assert.Equal(2, seen.Count);
    }

    // ---- on generated boards: each end-of-turn payout's tab against what its code did as the turn ended

    /// <summary>The end-of-turn payouts given rows (decision #244), each with the value its code reads and that value.</summary>
    private static readonly (string Class, string Var, int Value)[] EndOfTurnRelics =
    {
        ("Orichalcum", "Block", 6), ("FakeOrichalcum", "Block", 3), ("CloakClasp", "Block", 1), ("ObsidianVice", "Damage", 1), ("ScreamingFlagon", "Damage", 20),
    };

    [Fact]
    public void OnGeneratedBoardsEachPayoutsTabIsWhatItsCodeDidAsTheTurnEnded()
    {
        // Each class's code flashes exactly where it pays (Orichalcum.BeforeSideTurnEnd, CloakClasp, ObsidianVice, ScreamingFlagon, HailstormPower:
        // Flash() then the command), and logs what it gave: the tab is held to that, ending the turn now against after the play, in every branch.
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (var scenario = 0; scenario < 300; scenario++)
        {
            var random = new Random(9100 + scenario);
            var state = Board(block: random.Next(3) == 0 ? random.Next(1, 9) : 0, hand: random.Next(0, 6));
            state.Enemies.RemoveAll(enemy => enemy.Id == 2 && random.Next(2) == 0);
            if (random.Next(4) == 0) state.You.Plating = random.Next(1, 5);
            foreach (var (relic, value, amount) in EndOfTurnRelics)
                if (random.Next(2) == 0) state.WithRelic(Who.You, RelicId.Of(relic), value, amount);
            if (random.Next(2) == 0)
            {
                state.You.Powers = state.You.Powers.With(PowerId.Hailstorm, amount: random.Next(1, 6));
                state.You.OrbCapacity = random.Next(1, 4);
                state.You.Orbs = Enumerable.Range(0, random.Next(0, state.You.OrbCapacity + 1))
                    .Select(_ => new SimOrb { Kind = new[] { OrbKind.Frost, OrbKind.Lightning, OrbKind.Dark }[random.Next(3)], Passive = 2, Evoke = 5 }).ToList();
            }
            var play = random.Next(5) switch
            {
                0 => new SimPlay { Name = "Defend", BlockGains = 1, BlockAmount = random.Next(2, 9) },
                1 => Channel(new[] { OrbKind.Frost, OrbKind.Lightning, OrbKind.Random }[random.Next(3)]),
                2 => new SimPlay { Name = "Plated", AddPlating = random.Next(1, 4) },
                3 => new SimPlay { Name = "Strike", IsAttack = true, HitTarget = SimPlay.Target.Single, Hits = 1, HitDamage = { [1] = random.Next(3, 9) } },
                _ => new SimPlay { Name = "Skill" },
            };
            play.HandDelta = random.Next(-1, 2);
            play.TargetId = play.IsAttack ? 1 : Who.None;
            var tabs = TabsOf(state, play);
            var now = SimEngine.Run(state);
            var after = SimEngine.Run(state, play);
            foreach (var source in state.You.Relics.Items.Select(relic => relic.Name.Name).Concat(state.You.Powers.Items.Select(power => power.Name.Name)))
            {
                var expected = Expected(source, TabRowsFile.ShowsOf(source), now, after);
                Assert.True(expected == Tab(tabs, source)?.Crossed, $"scenario {scenario}, {source}: expected {(expected == null ? "no tab" : expected.Value ? "crossed" : "plain")}, shown {Tab(tabs, source)}");
                if (expected is { } crossed) seen.Add($"{source} {(crossed ? "crossed" : "plain")}");
            }
        }
        // Every direction each row shows came up.
        foreach (var direction in new[] { "Orichalcum crossed", "FakeOrichalcum crossed", "CloakClasp crossed", "ObsidianVice crossed", "ScreamingFlagon plain", "HailstormPower plain", "HailstormPower crossed" })
            Assert.Contains(direction, seen);
    }

    /// <summary>
    /// A payout's tab from what its code did as the turn ended (its flash, and what it logged it gave), ending the turn now against after the play, as
    /// its row shows it: crossed where ending now pays in every outcome and the play pays nothing (or less) in some; plain where ending now doesn't
    /// pay in every outcome and the play pays in every one; null for no tab.
    /// </summary>
    private static bool? Expected(string source, TabShows shows, SimOutcome now, SimOutcome after)
    {
        bool Paid(SimState branch) => branch.FlashedAny.Has(source);
        int Amount(SimState branch) => branch.FiredAny.DataOf(source);
        if (now.InEvery(Paid))
        {
            var least = now.Branches.Min(Amount);
            var crossed = ((shows & TabShows.GivesUp) != 0 && !after.InEvery(Paid)) || ((shows & TabShows.Reduces) != 0 && !after.InEvery(branch => Paid(branch) && Amount(branch) >= least));
            return crossed ? true : null;
        }
        // Plain where every outcome pays; where only some do, plain too for a row that shows a bonus by chance (decision #249: chances on here).
        var setsUp = (shows & TabShows.SetsUpByChance) != 0 ? after.Branches.Any(Paid) : after.InEvery(Paid);
        return (shows & TabShows.SetsUp) != 0 && setsUp ? false : null;
    }
}
