using SpireQoL.Sim;
using Xunit;

namespace SpireQoL.Sim.Tests;

/// <summary>
/// Teammates' end-of-turn effects in multiplayer (decompile v0.111.0, CombatManager end of turn):
/// BeforeSideTurnEnd for every player (Hailstorm, The Bomb, Stone Calendar...), then each player's
/// DoTurnEnd in player order (their orbs' passives), then AfterSideTurnEnd (Consuming Shadow).
/// Only what's on screen is modelled: their orbs, powers and relics. Their block only protects them,
/// and what depends on their hand isn't counted (the non-optimistic side).
/// </summary>
public class SimTeammateTests
{
    private static SimEnemy Enemy(int id, int hp, params int[] hits) => new() { Id = id, Name = $"E{id}", Hp = hp, Hits = hits.ToList() };

    private static SimPlayer Defect(params SimOrb[] orbs)
    {
        var p = new SimPlayer { OrbCapacity = 3 };
        p.Orbs.AddRange(orbs);
        return p;
    }

    private static SimOrb Lightning(int passive = 3, int evoke = 8) => new() { Kind = OrbKind.Lightning, Passive = passive, Evoke = evoke };

    [Fact]
    public void ATeammatesLightningKillsBeforeTheEnemyActs()
    {
        // One enemy at 3 HP attacking for 10: your end of turn does nothing, a teammate's Lightning
        // passive (3) kills it, so it never attacks you.
        var s = new SimState { Enemies = { Enemy(1, 3, 10) } };
        s.Teammates.Add(Defect(Lightning()));
        var o = SimEngine.Run(s);
        Assert.True(o.DeadInAll(1));
        Assert.Equal(0, o.TakenMax);
    }

    [Fact]
    public void TeammatesOrbsRunInPlayerOrderAroundYours()
    {
        // A teammate before you in player order: their Lightning (random target, two enemies) runs
        // before your Lightning, so your orb can only hit what's still alive.
        var s = new SimState { Enemies = { Enemy(1, 3, 5), Enemy(2, 3, 5) } };
        s.P.OrbCapacity = 3;
        s.P.Orbs.Add(Lightning());
        s.Teammates.Add(Defect(Lightning()));
        s.TeammatesBeforeMe = 1;
        var o = SimEngine.Run(s);
        // Both Lightnings land on different enemies whichever goes first: both die in every branch.
        Assert.True(o.DeadInAll(1));
        Assert.True(o.DeadInAll(2));
    }

    [Fact]
    public void TeammatesFrostDoesNotBlockForYou()
    {
        var s = new SimState { Enemies = { Enemy(1, 50, 10) } };
        s.Teammates.Add(Defect(new SimOrb { Kind = OrbKind.Frost, Passive = 2, Evoke = 5 }));
        var o = SimEngine.Run(s);
        Assert.Equal(10, o.TakenMax);
    }

    [Fact]
    public void TeammatesBeforeSideTurnEndDamageAndConsumingShadow()
    {
        // The Bomb (all enemies) and Consuming Shadow evoking their Glass orb (all enemies) at end of turn.
        var s = new SimState { Enemies = { Enemy(1, 30, 5), Enemy(2, 30, 5) } };
        var tm = Defect(new SimOrb { Kind = OrbKind.Glass, Passive = 0, Evoke = 0, GlassBase = 0 });
        tm.TheBombDamage = 20;
        tm.Orbs[0] = new SimOrb { Kind = OrbKind.Glass, Passive = 4, Evoke = 8, GlassBase = 4 };
        tm.ConsumingShadow = 1;
        s.Teammates.Add(tm);
        var o = SimEngine.Run(s);
        // 20 (The Bomb) + 4 (Glass passive, then base 3: evoke 6) + 6 (Consuming Shadow evokes the Glass) = 30.
        Assert.True(o.DeadInAll(1));
        Assert.True(o.DeadInAll(2));
    }

    [Fact]
    public void ATeammatesHandIsUnknownSoItsEffectsDontCount()
    {
        // Screaming Flagon needs an empty hand; a teammate's hand isn't known (HandCount -1): no damage.
        var s = new SimState { Enemies = { Enemy(1, 5, 10) } };
        var tm = Defect();
        tm.HandCount = -1;
        tm.ScreamingFlagonDamage = 20;
        s.Teammates.Add(tm);
        Assert.False(SimEngine.Run(s).DeadInAll(1));
    }
}
