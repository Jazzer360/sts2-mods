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

    private static void Setup(string name, SimPlayer player)
    {
        player.OrbCapacity = 3;
        switch (name)
        {
            case "Plating": player.Plating = 4; break;
            case "Orichalcum": player.OrichalcumBlock = 6; break;
            case "Cloak Clasp": player.CloakClaspPerCard = 1; player.HandCount = 5; break;
            case "Ripple Basin": player.RippleBasinBlock = 4; break;
            case "Parrying Shield": player.Plating = 10; player.ParryingShieldBlock = 10; player.ParryingShieldDamage = 6; break;
            case "Frost and Hailstorm":
                player.Orbs.Add(new SimOrb { Kind = OrbKind.Frost, Passive = 2, Evoke = 5 });
                player.Hailstorm = 3;
                break;
            case "The Bomb": player.TheBombDamage = 12; break;
            case "Stone Calendar": player.StoneCalendarDamage = 9; break;
            case "Screaming Flagon": player.ScreamingFlagonDamage = 8; player.HandCount = 0; break;
            case "Lightning": player.Orbs.Add(new SimOrb { Kind = OrbKind.Lightning, Passive = 3, Evoke = 8 }); break;
            case "Juggernaut": player.Plating = 5; player.Juggernaut = 7; break;
            case "Consuming Shadow (Lightning)": player.Orbs.Add(new SimOrb { Kind = OrbKind.Lightning, Passive = 3, Evoke = 8 }); player.ConsumingShadow = 1; break;
            case "Consuming Shadow (Frost)": player.Orbs.Add(new SimOrb { Kind = OrbKind.Frost, Passive = 2, Evoke = 5 }); player.ConsumingShadow = 1; break;
            case "Obsidian Vice": player.ObsidianViceDamage = 1; player.HandCount = 4; break;
            // Ethereal cards exhausting at end of turn (CombatManager.DoTurnEnd): each one sets off the exhaust effects.
            case "Ethereal and Feel No Pain": player.Fading = 2; player.FeelNoPain = 3; player.HandCount = 2; break;
            case "Ethereal and Charon's Ashes": player.Fading = 2; player.CharonsAshesDamage = 3; player.HandCount = 2; break;
            case "Ethereal and Forgotten Soul": player.Fading = 2; player.ForgottenSoulDamage = 1; player.HandCount = 2; break;
            case "Thorns": player.Thorns = 3; break;
            case "Flame Barrier": player.FlameBarrier = 4; break;
            case "Reflect": player.Block = 6; player.Reflect = true; break;
        }
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void YouAndATeammateGetTheSameEndOfTurn(string name)
    {
        var asYou = new SimState { Enemies = { Target() } };
        Setup(name, asYou.You);
        var you = SimEngine.Run(asYou);

        var asMate = new SimState { Enemies = { Target() } };
        var mate = new SimPlayer { Hp = 80 };
        Setup(name, mate);
        asMate.Teammates.Add(mate);
        var them = SimEngine.Run(asMate);

        Assert.Equal(you.Worst.BlockAtEnemyTurn, them.TeammateWorst(teammateIndex: 0).BlockGained);
        static int[] Hp(SimOutcome outcome) => outcome.Branches.Select(branch => branch.Enemy(id: 1)!.HpAtTurnEnd).Distinct().OrderBy(hpLeft => hpLeft).ToArray();
        Assert.Equal(Hp(you), Hp(them));
    }

    /// <summary>Retaliation during the enemy turn: an enemy hitting you or a teammate takes the same back.</summary>
    [Theory]
    [InlineData("Thorns")]
    [InlineData("Flame Barrier")]
    [InlineData("Reflect")]
    public void YouAndATeammateRetaliateTheSame(string name)
    {
        var asYou = new SimState { Enemies = { Target(hp: 200, hits: [5, 5]) } };
        Setup(name, asYou.You);
        var asMate = new SimState { Enemies = { Target(hp: 200, hits: [5, 5]) } };
        var mate = new SimPlayer { Hp = 80 };
        Setup(name, mate);
        asMate.Teammates.Add(mate);
        // The enemy's HP after its turn: the same retaliation either way (it hits every player).
        static int[] Hp(SimOutcome outcome) => outcome.Branches.Select(branch => branch.Enemy(id: 1)!.Hp).Distinct().OrderBy(hpLeft => hpLeft).ToArray();
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
        static void Setup(string name, SimPlayer player)
        {
            player.Hp = 40;
            switch (name)
            {
                case "block": player.Block = 12; break;
                case "Wuppy": player.WuppyHp = 8; break;
                case "Osty": player.OstyHp = 10; player.OstyShields = true; break;
                case "Tungsten Rod": player.TungstenRod = 1; break;
                case "Regret": player.HandCount = 3; player.TurnEndCards.Add(new TurnEndCard("Regret", "Regret", Amount: 0)); player.Cards = player.Cards.With(CardId.Regret, amount: 1); break;
                case "Fairy in a Bottle": player.Hp = 15; player.Fairies = 1; player.FairyHeal = 20; break;
                case "Lizard Tail": player.Hp = 15; player.LizardTailHeal = 25; break;
                case "death": player.Hp = 15; break;
            }
            if (name != "Osty") player.OstyShields = false;
        }
        var asYou = new SimState { Enemies = { Target(hp: 200, hits: [10, 10]) } };
        Setup(name, asYou.You);
        var asMate = new SimState { Enemies = { Target(hp: 200, hits: [10, 10]) } };
        asMate.You.OstyShields = false;
        var mate = new SimPlayer();
        Setup(name, mate);
        asMate.Teammates.Add(mate);

        var you = SimEngine.Run(asYou).Worst.You;
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
        var state = new SimState { Enemies = { Target(hp: 50, hits: 10) } };
        var mate = new SimPlayer { Hp = 60, HandCount = 3, Fading = 2, FeelNoPain = 3 };
        mate.TurnEndCards.Add(new TurnEndCard("Regret", "Regret", Amount: 0));
        mate.Cards = mate.Cards.With(CardId.Regret, amount: 1); // its BeforeSideTurnEnd counts the hand (RegretCard)
        state.Teammates.Add(mate);
        var play = new SimPlay { Name = "Strike", IsAttack = true, HitTarget = SimPlay.Target.Single, TargetId = 1, HitDamage = { [1] = 6 }, Hits = 1, HandCard = 7, HandDelta = -1 };
        SimEngine.ApplyPlayTo(state, play, out var full, out _);
        SimEngine.ApplyPlayTo(state.WithoutTeammateHands(), play, out var shown, out _);
        static long[] Keys(List<SimState> states) => states.Select(branch => branch.WithoutTeammateHands().Key()).OrderBy(key => key).ToArray();
        Assert.Equal(Keys(shown), Keys(full));
    }

    [Fact]
    public void ATeammatesRegretIsShownOnlyWithTheirHand()
    {
        // Regret in their hand (3 cards): 3 HP no block stops. The checks count it; what's shown leaves their hand out.
        var state = new SimState { Enemies = { Target(hp: 50, hits: 10) } };
        var mate = new SimPlayer { Hp = 60, Block = 4, HandCount = 3 };
        mate.TurnEndCards.Add(new TurnEndCard("Regret", "Regret", Amount: 0));
        mate.Cards = mate.Cards.With(CardId.Regret, amount: 1); // its BeforeSideTurnEnd counts the hand (RegretCard)
        state.Teammates.Add(mate);
        var full = SimEngine.Run(state).TeammateWorst(teammateIndex: 0);
        Assert.Equal((10, 3, 9), (full.Incoming, full.UnblockableTaken, full.Taken));
        var shown = SimEngine.Run(state.WithoutTeammateHands()).TeammateWorst(teammateIndex: 0);
        Assert.Equal((10, 0, 6), (shown.Incoming, shown.UnblockableTaken, shown.Taken));
    }

    [Fact]
    public void CloakClaspCoversPartOfATeammatesShortfall()
    {
        // 15 incoming against their 5 block: 10 short now. Cloak Clasp with 5 cards in hand adds 5 at end
        // of turn, so they lose 5.
        var state = new SimState { Enemies = { Target(hp: 50, hits: 15) } };
        state.Teammates.Add(new SimPlayer { Hp = 60, Block = 5, CloakClaspPerCard = 1, HandCount = 5 });
        var outcome = SimEngine.Run(state);
        var player = outcome.TeammateWorst(teammateIndex: 0);
        Assert.Equal(15, player.Incoming);
        Assert.Equal(5, player.BlockGained);
        Assert.Equal(5, outcome.TeammateTakenMax(teammateIndex: 0));
    }

    [Fact]
    public void TheShownRunLeavesTheirHandOutButKeepsItsSize()
    {
        // A Burn (2, blockable) in their hand: counted for the checks, not for what's shown. Their hand
        // size still counts (Cloak Clasp).
        var state = new SimState { Enemies = { Target(hp: 50, hits: 10) } };
        var mate = new SimPlayer { Hp = 60, CloakClaspPerCard = 1, HandCount = 3 };
        mate.TurnEndCards.Add(new TurnEndCard("Burn", "Burn", Amount: 2));
        state.Teammates.Add(mate);

        Assert.Equal(9, SimEngine.Run(state).TeammateTakenMax(teammateIndex: 0));     // 10 + 2 - 3
        var shown = SimEngine.Run(state.WithoutTeammateHands());
        Assert.Equal(7, shown.TeammateTakenMax(teammateIndex: 0));                  // 10 - 3
        Assert.Equal(10, shown.TeammateWorst(teammateIndex: 0).Incoming);
        Assert.Single(state.Teammates[0].TurnEndCards);                 // the original is untouched
    }

    [Fact]
    public void ATeammatesEtherealExhaustsCountForTheChecksNotForWhatsShown()
    {
        // Two Ethereal cards in their hand with Charon's Ashes 3: 6 to every enemy as they exhaust, killing the
        // 6 HP enemy before its 10 on you. Which cards are in their hand isn't known, so the shown run has it attack.
        var state = new SimState { Enemies = { Target(hp: 6, hits: 10) } };
        state.Teammates.Add(new SimPlayer { Hp = 60, HandCount = 2, Fading = 2, CharonsAshesDamage = 3 });

        var full = SimEngine.Run(state);
        Assert.True(full.DeadInAll(enemyId: 1));
        Assert.Equal(0, full.TakenMax);
        var shown = SimEngine.Run(state.WithoutTeammateHands());
        Assert.False(shown.DeadInAny(enemyId: 1));
        Assert.Equal(10, shown.TakenMax);
        Assert.Equal(2, state.Teammates[0].Fading);                       // the original is untouched
    }
}
