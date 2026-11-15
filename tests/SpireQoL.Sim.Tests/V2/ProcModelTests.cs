using SpireQoL.Sim;
using Xunit;
using static SpireQoL.Sim.Tests.V2.V2Script;

namespace SpireQoL.Sim.Tests.V2;

/// <summary>
/// Powers and relics that act on your plays and card events (Sim/Models/ProcPowerModels.cs, ProcRelicModels.cs), each as its hook
/// does in the decompile (v0.113.1): what it does, and what it logs (the play log the tabs come from).
/// </summary>
public class ProcModelTests
{
    private static SimState State(int enemies = 1, int hp = 100)
    {
        var s = new SimState();
        for (var i = 0; i < enemies; i++) s.Enemies.Add(Enemy(i, hp));
        return s;
    }

    private static SimState Power(SimState s, string name, int amount, int data = 0)
    {
        s.P.Powers = s.P.Powers.With(name, amount, data);
        return s;
    }

    private static SimState Relic(SimState s, string name, int amount = 0, int data = 1, params (string Var, int Value)[] vars)
    {
        s.P.Relics = s.P.Relics.With(name, amount, data);
        foreach (var (v, value) in vars) s.Vars[$"{Who.You}:{name}:{v}"] = value;
        return s;
    }

    private static SimCard Typed(string name, SimCardType type) => new(name, type);

    private static SimState One(SimState s, SimScript script) => Assert.Single(After(s, script));

    private static SimScript Events(SimCard card, int plays, params SimAction[] body) => Play(card, -1, plays, body);

    // ---- your powers on play

    [Fact]
    public void AfterimageBlocksPerPlayButNotOnThePlayThatAppliesIt()
    {
        var after = One(Power(State(), "AfterimagePower", 2), Events(Card("Defend", SimCardType.Skill), 2));
        Assert.Equal(4, after.P.Block);
        Assert.Equal(2, after.Fired["AfterimagePower"]);
        Assert.Equal(4, after.Fired.DataOf("AfterimagePower"));
        // The Afterimage card: the power lands during the play, so nothing is bound to it.
        var own = One(State(), Events(Card("Afterimage", SimCardType.Power), 1, new ApplyPowerAction("AfterimagePower", 1) { To = ApplyPowerAction.Where.You }));
        Assert.Equal(0, own.P.Block);
        Assert.False(own.Fired.Has("AfterimagePower"));
    }

    [Fact]
    public void RageBlocksOnAttacksOnly()
    {
        var s = Power(State(), "RagePower", 3);
        Assert.Equal(3, One(s, Play(Card("Strike"), 0, 1, Attack(6))).P.Block);
        Assert.Equal(0, One(s, Events(Card("Defend", SimCardType.Skill), 1)).P.Block);
    }

    [Fact]
    public void SpiritOfAshAndDanseMacabreBlockBeforeThePlay()
    {
        var ethereal = new SimCard("Wraith", SimCardType.Skill) { Ethereal = true };
        Assert.Equal(4, One(Power(State(), "SpiritOfAshPower", 4), Events(ethereal, 1)).P.Block);
        var s = Power(State(), "DanseMacabrePower", 3);
        s.Vars[$"{Who.You}:DanseMacabrePower:Energy"] = 2;
        Assert.Equal(3, One(s, Events(new SimCard("Big", SimCardType.Skill) { Energy = 2 }, 1)).P.Block);
        Assert.Equal(0, One(s, Events(new SimCard("Small", SimCardType.Skill) { Energy = 1 }, 1)).P.Block);
    }

    [Fact]
    public void GravityHitsEveryEnemyAndSerpentFormBranchesOnItsTarget()
    {
        var after = One(Power(State(2), "GravityPower", 3), Events(Card("Defend", SimCardType.Skill), 1));
        Assert.Equal(97, after.Enemy(0)!.Hp);
        Assert.Equal(97, after.Enemy(1)!.Hp);
        Assert.Equal(6, after.Fired.DataOf("GravityPower"));
        var branches = After(Power(State(2), "SerpentFormPower", 5), Events(Card("Defend", SimCardType.Skill), 1));
        Assert.Equal(2, branches.Count);
        Assert.All(branches, b => Assert.Equal(5, b.Fired.DataOf("SerpentFormPower")));
    }

    [Fact]
    public void PanacheFiresOnTheCardThatEndsItsCount()
    {
        var s = Power(State(), "PanachePower", 10, data: 2);
        var after = One(s, Events(Card("A", SimCardType.Skill), 2));
        Assert.Equal(90, after.Enemy(0)!.Hp);
        Assert.Equal(5, after.P.Powers.DataOf("PanachePower"));
        Assert.Equal(100, One(Power(State(), "PanachePower", 10, data: 3), Events(Card("A", SimCardType.Skill), 2)).Enemy(0)!.Hp);
    }

    [Fact]
    public void TheSealedThroneGivesStarsThatSetOffBlackHole()
    {
        var s = Power(Power(State(), "TheSealedThronePower", 1), "BlackHolePower", 4);
        var after = One(s, Events(Card("A", SimCardType.Skill), 1));
        Assert.Equal(96, after.Enemy(0)!.Hp);
        Assert.True(after.Fired.Has("TheSealedThronePower"));
        // A card that spends stars sets Black Hole off after its last play.
        var spender = new SimCard("Stardust", SimCardType.Skill) { Stars = 2 };
        Assert.Equal(96, One(Power(State(), "BlackHolePower", 4), Events(spender, 2)).Enemy(0)!.Hp);
    }

    [Fact]
    public void JugglingCopiesTheThirdAttackOfTheTurn()
    {
        var s = Power(State(), "JugglingPower", 1, data: 2);
        var after = One(s, Play(Card("Strike"), 0, 1, Attack(1)));
        Assert.True(after.Fired.Has("JugglingPower"));
        Assert.False(One(Power(State(), "JugglingPower", 1, data: 0), Play(Card("Strike"), 0, 1, Attack(1))).Fired.Has("JugglingPower"));
    }

    [Fact]
    public void MonologueGivesStrengthThatTheReplayUses()
    {
        var s = Power(State(), "MonologuePower", 1);
        s.Vars[$"{Who.You}:MonologuePower:Strength"] = 2;
        var after = One(s, Play(Card("Strike"), 0, 2, Attack(5)));
        Assert.Equal(100 - 5 - 7, after.Enemy(0)!.Hp);
        Assert.Equal(4, after.P.Powers["StrengthPower"]);
    }

    [Fact]
    public void FeralNostalgiaAndPaleBlueDotCountTheirCharges()
    {
        var free = new SimCard("Claw", SimCardType.Attack) { Energy = 0 };
        Assert.True(One(Power(State(), "FeralPower", 1, data: 1), Play(free, 0, 1, Attack(1))).Fired.Has("FeralPower"));
        Assert.False(One(Power(State(), "FeralPower", 1, data: 0), Play(free, 0, 1, Attack(1))).Fired.Has("FeralPower"));
        Assert.False(One(Power(State(), "FeralPower", 1, data: 1), Play(free with { Dupe = true }, 0, 1, Attack(1))).Fired.Has("FeralPower"));

        var s = Power(State(), "NostalgiaPower", 1);
        Assert.True(One(s, Play(Card("Strike"), 0, 1, Attack(1))).Fired.Has("NostalgiaPower"));
        s.P.Turn = s.P.Turn.Add("Started:Skill", 1);
        Assert.False(One(s, Play(Card("Strike"), 0, 1, Attack(1))).Fired.Has("NostalgiaPower"));

        var dot = Power(State(), "PaleBlueDotPower", 1);
        dot.P.Turn = dot.P.Turn.Add("Finished", 4);
        Assert.True(One(dot, Events(Card("A", SimCardType.Skill), 1)).Fired.Has("PaleBlueDotPower"));
        dot.P.Powers = dot.P.Powers.With("PaleBlueDotPower", 1, 1);
        Assert.False(One(dot, Events(Card("A", SimCardType.Skill), 1)).Fired.Has("PaleBlueDotPower"));
    }

    [Fact]
    public void TenderAndSmoggyAreWarnings()
    {
        Assert.True(SimModels.Get("TenderPower")!.Harmful);
        Assert.True(SimModels.Get("SmoggyPower")!.Harmful);
        Assert.True(One(Power(State(), "SmoggyPower", 1), Events(Card("A", SimCardType.Skill), 1)).Fired.Has("SmoggyPower"));
        Assert.False(One(Power(State(), "SmoggyPower", 1), Play(Card("Strike"), 0, 1, Attack(1))).Fired.Has("SmoggyPower"));
    }

    // ---- melodies and arrows

    [Fact]
    public void FirstMelodiesFireTheirPowers()
    {
        var wah = Card("WahSlap", SimCardType.Attack, "Wah");
        var s = Power(Power(Power(State(), "ViolentChordPower", 3), "MotifPower", 1), "SongPathPower", 4);
        var after = One(s, Play(wah, 0, 2, Attack(0)));
        Assert.Equal(97, after.Enemy(0)!.Hp); // the first Wah only, not the replay's second play
        Assert.Equal(1, after.Fired["MotifPower"]);
        Assert.False(after.Fired.Has("SongPathPower"));
        s.P.Turn = s.P.Turn.Add("Started#Wah", 1);
        Assert.False(One(s, Play(wah, 0, 1, Attack(0))).Fired.Has("ViolentChordPower"));
    }

    [Fact]
    public void LullabyAndConductorsBatonFireOnTheThirdMelody()
    {
        var ki = Card("KiStep", SimCardType.Skill, "Ki");
        var s = Power(State(), "LullabyPower", 5);
        Relic(s, "ConductorsBaton", data: 0);
        s.P.Turn = s.P.Turn.Add("Finished#Wah", 1).Add("Finished#Cho", 1);
        var after = One(s, Events(ki, 1));
        Assert.Equal(5, after.P.Block);
        Assert.True(after.Fired.Has("ConductorsBaton"));
        s.P.Turn = s.P.Turn.Add("Finished#Ki", 1); // already complete
        Assert.Equal(0, One(s, Events(ki, 1)).P.Block);
    }

    [Fact]
    public void ExplosiveArrowsNeedsAnArrowAndYahAndTwineReactToDislodges()
    {
        var shot = new SimCard("Shoot", SimCardType.Attack) { Shoots = true };
        Assert.Equal(96, One(Power(State(), "ExplosiveArrowsPower", 4), Play(shot, 0, 1)).Enemy(0)!.Hp);
        Assert.Equal(100, One(Power(State(), "ExplosiveArrowsPower", 4), Play(shot with { Shoots = false }, 0, 1)).Enemy(0)!.Hp);

        var s = Power(State(), "YahPower", 1);
        Relic(s, "Twine", vars: ("Block", 2));
        var script = Play(Card("Recall", SimCardType.Skill), -1, 1);
        script.Actions.Add(new CardEventAction(CardEvent.ArrowDislodged, 2));
        var after = One(s, script);
        Assert.Equal(1, after.Fired["YahPower"]); // once a turn
        Assert.Equal(4, after.P.Block);
    }

    // ---- card events

    [Fact]
    public void ExhaustsSetOffFeelNoPainDarkEmbraceAndTheirDraws()
    {
        var s = Power(Power(Power(State(), "FeelNoPainPower", 3), "DarkEmbracePower", 1), "SpeedsterPower", 2);
        var after = One(s, Events(Card("TrueGrit", SimCardType.Skill), 1, new CardEventAction(CardEvent.Exhausted, 2) { Indirect = true }));
        Assert.Equal(6, after.P.Block);
        Assert.Equal(2, after.Fired["DarkEmbracePower"]);
        Assert.Equal(96, after.Enemy(0)!.Hp); // two draws from Dark Embrace, Speedster on each
        Assert.Equal(2, after.Fired["~FeelNoPainPower"]); // indirect: from the exhausts
    }

    [Fact]
    public void CountdownPowersFireWhenTheirCountRunsOut()
    {
        var s = Power(State(), "CacophonyPower", 7, data: 2);
        var after = One(s, Events(Card("A", SimCardType.Skill), 1, new CardEventAction(CardEvent.Drawn, 3)));
        Assert.Equal(93, after.Enemy(0)!.Hp);
        Assert.Equal(1, after.Fired["CacophonyPower"]);
        var orbit = One(Power(State(), "OrbitPower", 1, data: 3), Events(Card("A", SimCardType.Skill), 1, new CardEventAction(CardEvent.EnergySpent, 2)));
        Assert.False(orbit.Fired.Has("OrbitPower"));
    }

    [Fact]
    public void CreatedCardsSetOffTheirPowersByKind()
    {
        var s = Power(Power(Power(State(), "PillarOfCreationPower", 2), "SmokestackPower", 3), "AllIsWellPower", 4);
        var after = One(s, Events(Card("A", SimCardType.Skill), 1,
            new CardEventAction(CardEvent.Generated, 1), new CardEventAction(CardEvent.GeneratedStatus, 1), new CardEventAction(CardEvent.GeneratedSadness, 1)));
        Assert.Equal(3 * 2 + 4, after.P.Block); // Pillar on every created card, All Is Well on the Sadness
        Assert.Equal(97, after.Enemy(0)!.Hp);   // Smokestack on the status
    }

    [Fact]
    public void ChildOfTheStarsAndGalacticDustCountStarsSpent()
    {
        var s = Power(State(), "ChildOfTheStarsPower", 2);
        Relic(s, "GalacticDust", amount: 8, vars: new[] { ("Stars", 10), ("Block", 5) });
        var spender = new SimCard("Stardust", SimCardType.Skill) { Stars = 3 };
        var after = One(s, Events(spender, 2, new CardEventAction(CardEvent.StarsSpent, 1) { FirstPlayOnly = true }));
        Assert.Equal(6 + 5, after.P.Block);
        Assert.Equal(1, after.P.Relics["GalacticDust"]);
    }

    [Fact]
    public void DebuffsSetOffSleightOfFleshViciousAndShroud()
    {
        var s = Power(Power(Power(State(), "SleightOfFleshPower", 3), "ViciousPower", 1), "ShroudPower", 2);
        var after = One(s, Play(Card("Bash"), 0, 1,
            new ApplyPowerAction("VulnerablePower", 2) { Debuff = true }, new ApplyPowerAction("DoomPower", 3) { Debuff = true }));
        Assert.Equal(94, after.Enemy(0)!.Hp);
        Assert.True(after.Fired.Has("ViciousPower"));
        Assert.Equal(2, after.P.Block);
        // Artifact takes the debuff: nothing lands, nothing fires.
        s.Enemies[0].Powers = PowerSet.Empty.With("ArtifactPower", 1);
        Assert.Equal(100, One(s, Play(Card("Bash"), 0, 1, new ApplyPowerAction("VulnerablePower", 2) { Debuff = true })).Enemy(0)!.Hp);
    }

    // ---- enemy powers

    [Fact]
    public void StrangleHitsItsOwnerForEachOfTheAppliersPlays()
    {
        var s = State();
        s.Enemies[0].Powers = PowerSet.Empty.With("StranglePower", 2, Who.You + 100);
        var after = One(s, Events(Card("A", SimCardType.Skill), 2));
        Assert.Equal(96, after.Enemy(0)!.Hp);
        Assert.Equal(2, after.Fired["StranglePower@0"]);
    }

    [Fact]
    public void GalvanicHitsYouForAGalvanizedCard()
    {
        var s = State();
        s.P.Block = 1;
        s.P.Hp = 50;
        s.Enemies[0].Powers = PowerSet.Empty.With("GalvanicPower", 4);
        var after = One(s, Events(Card("A", SimCardType.Skill, "Galvanized"), 1));
        Assert.Equal(47, after.P.Hp);
        Assert.Equal(4, after.Fired.DataOf("GalvanicPower@0"));
        Assert.Equal(50, One(s, Events(Card("A", SimCardType.Skill), 1)).P.Hp);
    }

    [Fact]
    public void WitheringPresenceCountsYourCardsDown()
    {
        var s = State();
        s.Enemies[0].Powers = PowerSet.Empty.With("WitheringPresencePower", 1, 2);
        Assert.True(One(s, Events(Card("A", SimCardType.Skill), 2)).Fired.Has("WitheringPresencePower@0"));
        Assert.False(One(s, Events(Card("A", SimCardType.Skill), 1)).Fired.Has("WitheringPresencePower@0"));
    }

    // ---- relics

    [Fact]
    public void ArtOfWarAndPaelsEyeWarnOnTheTurnsFirstPlays()
    {
        var s = Relic(Relic(State(), "ArtOfWar"), "PaelsEye");
        var after = One(s, Play(Card("Strike"), 0, 2, Attack(1)));
        Assert.Equal(1, after.Fired["ArtOfWar"]);
        Assert.Equal(1, after.Fired["PaelsEye"]);
        s.P.Turn = s.P.Turn.Add("Started", 1).Add("Started:Attack", 1);
        var later = One(s, Play(Card("Strike"), 0, 1, Attack(1)));
        Assert.False(later.Fired.Has("ArtOfWar"));
        Assert.False(later.Fired.Has("PaelsEye"));
    }

    [Fact]
    public void PaelsTearsWarnsWhenTheLastEnergyIsSpent()
    {
        var s = Relic(State(), "PaelsTears");
        s.P.Energy = 2;
        var spend = Events(new SimCard("Big", SimCardType.Skill) { Energy = 2 }, 1);
        spend.Actions.Insert(0, new CardEventAction(CardEvent.EnergySpent, 2));
        Assert.True(One(s, spend).Fired.Has("PaelsTears"));
        s.P.Energy = 3;
        Assert.False(One(s, spend).Fired.Has("PaelsTears"));
    }

    [Fact]
    public void UnsettlingLampDoublesTheFirstDebuffingCardsDebuffs()
    {
        var s = Relic(State(2), "UnsettlingLamp");
        var after = One(s, Play(Card("Bash"), 0, 1, new ApplyPowerAction("VulnerablePower", 2) { Debuff = true }));
        Assert.Equal(4, after.Enemy(0)!.Powers["VulnerablePower"]);
        Assert.True(after.Fired.Has("UnsettlingLamp"));
        Assert.Equal(0, after.P.Relics.DataOf("UnsettlingLamp"));
        // An enemy with Artifact doesn't set it off.
        s.Enemies[0].Powers = PowerSet.Empty.With("ArtifactPower", 1);
        Assert.False(One(s, Play(Card("Bash"), 0, 1, new ApplyPowerAction("VulnerablePower", 2) { Debuff = true })).Fired.Has("UnsettlingLamp"));
    }

    [Fact]
    public void CounterRelicsFireOnTheCardThatCompletesTheCount()
    {
        var s = Relic(State(), "Kunai", amount: 2, vars: new[] { ("Cards", 3), ("Dexterity", 1) });
        var after = One(s, Play(Card("Strike"), 0, 1, Attack(1)));
        Assert.True(after.Fired.Has("Kunai"));
        Assert.Equal(1, after.P.Powers["DexterityPower"]);
        Assert.Equal(0, after.P.Relics["Kunai"]);
        var fan = Relic(State(), "OrnamentalFan", amount: 1, vars: new[] { ("Cards", 3), ("Block", 4) });
        Assert.Equal(0, One(fan, Play(Card("Strike"), 0, 1, Attack(1))).P.Block);
        Assert.Equal(4, One(fan, Play(Card("Strike"), 0, 2, Attack(1))).P.Block);
        var opener = Relic(State(2), "LetterOpener", amount: 2, vars: new[] { ("Cards", 3), ("Damage", 5) });
        var opened = One(opener, Events(Card("Defend", SimCardType.Skill), 1));
        Assert.Equal(95, opened.Enemy(1)!.Hp);
        Assert.Equal(10, opened.Fired.DataOf("LetterOpener"));
    }

    [Fact]
    public void OnPlayRelicsCheckTheCard()
    {
        var s = Relic(Relic(Relic(State(), "DaughterOfTheWind", vars: ("Block", 1)), "LostWisp", vars: ("Damage", 8)), "IntimidatingHelmet", vars: new[] { ("Energy", 2), ("Block", 4) });
        Assert.Equal(1, One(s, Play(Card("Strike"), 0, 1, Attack(0))).P.Block);
        Assert.Equal(92, One(s, Events(Card("Inflame", SimCardType.Power), 1)).Enemy(0)!.Hp);
        Assert.Equal(4, One(s, Events(new SimCard("Big", SimCardType.Skill) { Energy = 2 }, 1)).P.Block);
        var once = Relic(State(), "Permafrost", vars: ("Block", 6));
        Assert.Equal(6, One(once, Events(Card("Inflame", SimCardType.Power), 2)).P.Block);
    }

    [Fact]
    public void RainbowRingFiresOnTheTypeThatCompletesTheSet()
    {
        var s = Relic(State(), "RainbowRing", data: 0);
        s.P.Turn = s.P.Turn.Add("Finished:Attack", 1).Add("Finished:Skill", 1);
        Assert.True(One(s, Events(Card("Inflame", SimCardType.Power), 1)).Fired.Has("RainbowRing"));
        Assert.False(One(s, Events(Card("Defend", SimCardType.Skill), 1)).Fired.Has("RainbowRing"));
    }

    [Fact]
    public void ExhaustDiscardAndShuffleRelics()
    {
        var s = Relic(Relic(Relic(State(), "CharonsAshes", vars: ("Damage", 3)), "ToughBandages", vars: ("Block", 3)), "TheAbacus", vars: ("Block", 6));
        Relic(s, "BurningSticks");
        var after = One(s, Events(Card("Burn", SimCardType.Skill), 1,
            new CardEventAction(CardEvent.Exhausted, 1) { Card = Card("Burn", SimCardType.Skill) },
            new CardEventAction(CardEvent.Discarded, 1), new CardEventAction(CardEvent.Shuffled, 1)));
        Assert.Equal(97, after.Enemy(0)!.Hp);
        Assert.Equal(9, after.P.Block);
        Assert.True(after.Fired.Has("BurningSticks"));
        var joss = Relic(State(), "JossPaper", amount: 4, vars: ("ExhaustAmount", 5));
        Assert.True(One(joss, Events(Card("A", SimCardType.Skill), 1, new CardEventAction(CardEvent.Exhausted, 1))).Fired.Has("JossPaper"));
    }

    [Fact]
    public void UnceasingTopNeedsTheHandToEmpty()
    {
        var s = Relic(State(), "UnceasingTop");
        s.P.HandCount = 1;
        Assert.True(One(s, Events(Card("A", SimCardType.Skill), 1)).Fired.Has("UnceasingTop"));
        Assert.False(One(s, Events(Card("A", SimCardType.Skill), 1, new CardEventAction(CardEvent.Drawn, 1))).Fired.Has("UnceasingTop"));
        s.P.HandCount = 2;
        Assert.False(One(s, Events(Card("A", SimCardType.Skill), 1)).Fired.Has("UnceasingTop"));
    }

    [Fact]
    public void EveryProcModelHasATab()
    {
        foreach (var name in SimModels.Names)
        {
            var m = SimModels.Get(name)!;
            if (m is Models.ProcModel) Assert.NotNull(m.Tab);
        }
    }
}
