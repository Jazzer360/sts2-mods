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
        mate.HandBlockable.Add(("Burn", 2));
        s.Teammates.Add(mate);

        Assert.Equal(9, SimEngine.Run(s).TeammateTakenMax(0));     // 10 + 2 - 3
        var shown = SimEngine.Run(s.WithoutTeammateHands());
        Assert.Equal(7, shown.TeammateTakenMax(0));                  // 10 - 3
        Assert.Equal(10, shown.TeammateWorst(0).Incoming);
        Assert.Single(s.Teammates[0].HandBlockable);                 // the original is untouched
    }
}
