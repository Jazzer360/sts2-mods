using SpireQoL.Sim;
using Xunit;

namespace SpireQoL.Sim.Tests;

/// <summary>
/// A teammate is modelled exactly like you (decision #25): the same player state as you or as a
/// teammate gains the same end-of-turn block and deals the same end-of-turn damage. Each case is one
/// end-of-turn effect read for every player (SimAdapter.FillPlayer); a new effect applied to you only
/// fails here once it has a case. Also: what Teammate Incoming Damage shows (TeammateWorst, and the run
/// without their hand).
/// </summary>
public class SimTeammateParityTests
{
    private static SimEnemy Target(int hp = 200, params int[] hits) => new() { Id = 1, Name = "E1", Hp = hp, Hits = hits.ToList() };

    public static IEnumerable<object[]> Cases() => new[]
    {
        new object[] { "Plating" },
        new object[] { "Orichalcum" },
        new object[] { "Cloak Clasp" },
        new object[] { "Ripple Basin" },
        new object[] { "Parrying Shield" },
        new object[] { "Frost and Hailstorm" },
        new object[] { "The Bomb" },
        new object[] { "Stone Calendar" },
        new object[] { "Screaming Flagon" },
        new object[] { "Lightning" },
        new object[] { "Juggernaut" },
        new object[] { "Consuming Shadow (Lightning)" },
        new object[] { "Consuming Shadow (Frost)" },
        new object[] { "Obsidian Vice" },
        new object[] { "Ethereal and Feel No Pain" },
        new object[] { "Ethereal and Charon's Ashes" },
        new object[] { "Ethereal and Forgotten Soul" },
    };

    private static void Setup(string name, SimPlayer p)
    {
        p.OrbCapacity = 3;
        switch (name)
        {
            case "Plating": p.Plating = 4; break;
            case "Orichalcum": p.OrichalcumBlock = 6; break;
            case "Cloak Clasp": p.CloakClaspPerCard = 1; p.HandCount = 5; break;
            case "Ripple Basin": p.RippleBasinBlock = 4; break;
            case "Parrying Shield": p.Plating = 10; p.ParryingShieldBlock = 10; p.ParryingShieldDamage = 6; break;
            case "Frost and Hailstorm":
                p.Orbs.Add(new SimOrb { Kind = OrbKind.Frost, Passive = 2, Evoke = 5 });
                p.Hailstorm = 3;
                break;
            case "The Bomb": p.TheBombDamage = 12; break;
            case "Stone Calendar": p.StoneCalendarDamage = 9; break;
            case "Screaming Flagon": p.ScreamingFlagonDamage = 8; p.HandCount = 0; break;
            case "Lightning": p.Orbs.Add(new SimOrb { Kind = OrbKind.Lightning, Passive = 3, Evoke = 8 }); break;
            case "Juggernaut": p.Plating = 5; p.Juggernaut = 7; break;
            case "Consuming Shadow (Lightning)": p.Orbs.Add(new SimOrb { Kind = OrbKind.Lightning, Passive = 3, Evoke = 8 }); p.ConsumingShadow = 1; break;
            case "Consuming Shadow (Frost)": p.Orbs.Add(new SimOrb { Kind = OrbKind.Frost, Passive = 2, Evoke = 5 }); p.ConsumingShadow = 1; break;
            case "Obsidian Vice": p.ObsidianViceDamage = 1; p.HandCount = 4; break;
            // Ethereal cards exhausting at end of turn (CombatManager.DoTurnEnd): each one sets off the exhaust effects.
            case "Ethereal and Feel No Pain": p.Fading = 2; p.FeelNoPain = 3; p.HandCount = 2; break;
            case "Ethereal and Charon's Ashes": p.Fading = 2; p.CharonsAshesDamage = 3; p.HandCount = 2; break;
            case "Ethereal and Forgotten Soul": p.Fading = 2; p.ForgottenSoulDamage = 1; p.HandCount = 2; break;
            case "Thorns": p.Thorns = 3; break;
            case "Flame Barrier": p.FlameBarrier = 4; break;
            case "Reflect": p.Block = 6; p.Reflect = true; break;
        }
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void YouAndATeammateGetTheSameEndOfTurn(string name)
    {
        var asYou = new SimState { Enemies = { Target() } };
        Setup(name, asYou.P);
        var you = SimEngine.Run(asYou);

        var asMate = new SimState { Enemies = { Target() } };
        var mate = new SimPlayer { Hp = 80 };
        Setup(name, mate);
        asMate.Teammates.Add(mate);
        var them = SimEngine.Run(asMate);

        Assert.Equal(you.Worst.BlockAtEnemyTurn, them.TeammateWorst(0).BlockGained);
        static int[] Hp(SimOutcome o) => o.Branches.Select(b => b.Enemy(1)!.HpAtTurnEnd).Distinct().OrderBy(x => x).ToArray();
        Assert.Equal(Hp(you), Hp(them));
    }

    /// <summary>Retaliation during the enemy turn: an enemy hitting you or a teammate takes the same back.</summary>
    [Theory]
    [InlineData("Thorns")]
    [InlineData("Flame Barrier")]
    [InlineData("Reflect")]
    public void YouAndATeammateRetaliateTheSame(string name)
    {
        var asYou = new SimState { Enemies = { Target(200, 5, 5) } };
        Setup(name, asYou.P);
        var asMate = new SimState { Enemies = { Target(200, 5, 5) } };
        var mate = new SimPlayer { Hp = 80 };
        Setup(name, mate);
        asMate.Teammates.Add(mate);
        // The enemy's HP after its turn: the same retaliation either way (it hits every player).
        static int[] Hp(SimOutcome o) => o.Branches.Select(b => b.Enemy(1)!.Hp).Distinct().OrderBy(x => x).ToArray();
        Assert.Equal(Hp(SimEngine.Run(asYou)), Hp(SimEngine.Run(asMate)));
    }

    /// <summary>
    /// Decision #93: one engine for every player. The same player taking the enemy turn as you or as a teammate ends with the same
    /// results record: HP lost, through block, Incoming, the unblockable part, Wuppy's, Osty's, death and what prevented it.
    /// </summary>
    [Theory]
    [InlineData("block")]
    [InlineData("Wuppy")]
    [InlineData("Osty")]
    [InlineData("Tungsten Rod")]
    [InlineData("Regret")]
    [InlineData("Fairy in a Bottle")]
    [InlineData("Lizard Tail")]
    [InlineData("death")]
    public void YouAndATeammateTakeTheEnemyTurnTheSame(string name)
    {
        static void Setup(string name, SimPlayer p)
        {
            p.Hp = 40;
            switch (name)
            {
                case "block": p.Block = 12; break;
                case "Wuppy": p.WuppyHp = 8; break;
                case "Osty": p.OstyHp = 10; p.OstyShields = true; break;
                case "Tungsten Rod": p.TungstenRod = 1; break;
                case "Regret": p.HandCount = 3; p.TurnEndCards.Add(new TurnEndCard("Regret", "Regret", 0)); break;
                case "Fairy in a Bottle": p.Hp = 15; p.Fairies = 1; p.FairyHeal = 20; break;
                case "Lizard Tail": p.Hp = 15; p.LizardTailHeal = 25; break;
                case "death": p.Hp = 15; break;
            }
            if (name != "Osty") p.OstyShields = false;
        }
        var asYou = new SimState { Enemies = { Target(200, 10, 10) } };
        Setup(name, asYou.P);
        var asMate = new SimState { Enemies = { Target(200, 10, 10) } };
        asMate.P.OstyShields = false;
        var mate = new SimPlayer();
        Setup(name, mate);
        asMate.Teammates.Add(mate);

        var you = SimEngine.Run(asYou).Worst.P;
        var them = SimEngine.Run(asMate).Worst.Teammates[0];
        Assert.Equal(
            (you.Taken, you.Through, you.Incoming, you.UnblockableTaken, you.WuppyTaken, you.OstyTaken, you.Died, you.FairiesUsed, you.LizardTailUsed, you.Hp),
            (them.Taken, them.Through, them.Incoming, them.UnblockableTaken, them.WuppyTaken, them.OstyTaken, them.Died, them.FairiesUsed, them.LizardTailUsed, them.Hp));
    }

    [Fact]
    public void ATeammatesHandDoesntChangeYourPlay()
    {
        // Their hand acts at the end of turn (Regret, Ethereal cards), never during your play: the play gives the same branches
        // with or without it (SimInputs.HandChangesThePlay: the card play check's tabs stand either way).
        var s = new SimState { Enemies = { Target(50, 10) } };
        var mate = new SimPlayer { Hp = 60, HandCount = 3, Fading = 2, FeelNoPain = 3 };
        mate.TurnEndCards.Add(new TurnEndCard("Regret", "Regret", 0));
        s.Teammates.Add(mate);
        var play = new SimPlay { Name = "Strike", IsAttack = true, HitTarget = SimPlay.Target.Single, TargetId = 1, HitDamage = { [1] = 6 }, Hits = 1, HandCard = 7, HandDelta = -1 };
        SimEngine.ApplyPlayTo(s, play, out var full, out _);
        SimEngine.ApplyPlayTo(s.WithoutTeammateHands(), play, out var shown, out _);
        static long[] Keys(List<SimState> b) => b.Select(x => x.WithoutTeammateHands().Key()).OrderBy(k => k).ToArray();
        Assert.Equal(Keys(shown), Keys(full));
    }

    [Fact]
    public void ATeammatesRegretIsShownOnlyWithTheirHand()
    {
        // Regret in their hand (3 cards): 3 HP no block stops. The checks count it; what's shown leaves their hand out.
        var s = new SimState { Enemies = { Target(50, 10) } };
        var mate = new SimPlayer { Hp = 60, Block = 4, HandCount = 3 };
        mate.TurnEndCards.Add(new TurnEndCard("Regret", "Regret", 0));
        s.Teammates.Add(mate);
        var full = SimEngine.Run(s).TeammateWorst(0);
        Assert.Equal((10, 3, 9), (full.Incoming, full.UnblockableTaken, full.Taken));
        var shown = SimEngine.Run(s.WithoutTeammateHands()).TeammateWorst(0);
        Assert.Equal((10, 0, 6), (shown.Incoming, shown.UnblockableTaken, shown.Taken));
    }

    [Fact]
    public void CloakClaspCoversPartOfATeammatesShortfall()
    {
        // 15 incoming against their 5 block: 10 short now. Cloak Clasp with 5 cards in hand adds 5 at end
        // of turn, so they lose 5.
        var s = new SimState { Enemies = { Target(50, 15) } };
        s.Teammates.Add(new SimPlayer { Hp = 60, Block = 5, CloakClaspPerCard = 1, HandCount = 5 });
        var o = SimEngine.Run(s);
        var t = o.TeammateWorst(0);
        Assert.Equal(15, t.Incoming);
        Assert.Equal(5, t.BlockGained);
        Assert.Equal(5, o.TeammateTakenMax(0));
    }

    [Fact]
    public void TheShownRunLeavesTheirHandOutButKeepsItsSize()
    {
        // A Burn (2, blockable) in their hand: counted for the checks, not for what's shown. Their hand
        // size still counts (Cloak Clasp).
        var s = new SimState { Enemies = { Target(50, 10) } };
        var mate = new SimPlayer { Hp = 60, CloakClaspPerCard = 1, HandCount = 3 };
        mate.TurnEndCards.Add(new TurnEndCard("Burn", "Burn", 2));
        s.Teammates.Add(mate);

        Assert.Equal(9, SimEngine.Run(s).TeammateTakenMax(0));     // 10 + 2 - 3
        var shown = SimEngine.Run(s.WithoutTeammateHands());
        Assert.Equal(7, shown.TeammateTakenMax(0));                  // 10 - 3
        Assert.Equal(10, shown.TeammateWorst(0).Incoming);
        Assert.Single(s.Teammates[0].TurnEndCards);                 // the original is untouched
    }

    [Fact]
    public void ATeammatesEtherealExhaustsCountForTheChecksNotForWhatsShown()
    {
        // Two Ethereal cards in their hand with Charon's Ashes 3: 6 to every enemy as they exhaust, killing the
        // 6 HP enemy before its 10 on you. Which cards are in their hand isn't known, so the shown run has it attack.
        var s = new SimState { Enemies = { Target(6, 10) } };
        s.Teammates.Add(new SimPlayer { Hp = 60, HandCount = 2, Fading = 2, CharonsAshesDamage = 3 });

        var full = SimEngine.Run(s);
        Assert.True(full.DeadInAll(1));
        Assert.Equal(0, full.TakenMax);
        var shown = SimEngine.Run(s.WithoutTeammateHands());
        Assert.False(shown.DeadInAny(1));
        Assert.Equal(10, shown.TakenMax);
        Assert.Equal(2, s.Teammates[0].Fading);                       // the original is untouched
    }
}
