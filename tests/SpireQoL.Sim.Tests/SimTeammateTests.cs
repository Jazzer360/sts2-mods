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

    // ---- teammates as players (AttackCommand: each hit, CreatureCmd.Damage over every living player in order)

    private static SimPlayer Mate(int hp = 50, int block = 0) => new() { Hp = hp, Block = block, HandCount = 0 };

    [Fact]
    public void EachHitLandsOnEveryPlayerWithTheirOwnAmount()
    {
        // Two hits of 10 on you; the teammate is Vulnerable (15 per hit) with 5 block.
        var s = new SimState { Enemies = { Enemy(1, 50, 10, 10) } };
        s.Enemies[0].TeammateHits = new() { new() { 15, 15 } };
        s.Teammates.Add(Mate(block: 5));
        var o = SimEngine.Run(s);
        Assert.Equal(20, o.TakenMax);
        Assert.Equal(25, o.TeammateTakenMax(0)); // 30 - 5 block
    }

    [Fact]
    public void ATeammatesThornsKillingTheAttackerStillLetsThatHitReachYou()
    {
        // Teammate before you in player order with Thorns 5; the enemy has 3 HP and hits 10 x2. Their
        // Thorns kills it during hit 1, which still reaches you (the game checks the attacker only
        // between hits); hit 2 is cancelled.
        var s = new SimState { Enemies = { Enemy(1, 3, 10, 10) } };
        var mate = Mate();
        mate.Thorns = 5;
        s.Teammates.Add(mate);
        s.TeammatesBeforeMe = 1;
        var o = SimEngine.Run(s);
        Assert.Equal(10, o.TakenMax);
        Assert.Equal(10, o.TeammateTakenMax(0));

        // After you in player order: same, you take hit 1 first.
        s.TeammatesBeforeMe = 0;
        o = SimEngine.Run(s);
        Assert.Equal(10, o.TakenMax);
        Assert.Equal(10, o.TeammateTakenMax(0));
    }

    [Fact]
    public void ADeadTeammateTakesNoMoreHits()
    {
        var s = new SimState { Enemies = { Enemy(1, 50, 10, 10, 10) } };
        s.Teammates.Add(Mate(hp: 15));
        var o = SimEngine.Run(s);
        Assert.Equal(15, o.TeammateTakenMax(0)); // 10, then 5 more to 0, then nothing
        Assert.Equal(30, o.TakenMax);
    }

    [Fact]
    public void ATeammatesBeaconOfHopeSharesTheirEndOfTurnBlockWithYou()
    {
        // Their Plating 7 at end of turn: you get half, rounded down (3); Plating 1 gives nothing (0.5).
        var s = new SimState { Enemies = { Enemy(1, 50, 10) } };
        var mate = Mate();
        mate.Plating = 7;
        mate.BeaconOfHope = true;
        s.Teammates.Add(mate);
        Assert.Equal(7, SimEngine.Run(s).TakenMax);

        s.Teammates[0].Plating = 1;
        Assert.Equal(10, SimEngine.Run(s).TakenMax);
    }

    [Fact]
    public void YourBeaconOfHopeAndTheirsPassBlockOnOnce()
    {
        // You gain 10 block with Beacon of Hope: the teammate gets 5; their Beacon passes 2 back to you
        // (you get it, but yours is held while it gives, so nothing goes round again).
        var s = new SimState { Enemies = { Enemy(1, 50, 20) } };
        s.P.BeaconOfHope = true;
        var mate = Mate();
        mate.BeaconOfHope = true;
        s.Teammates.Add(mate);
        var play = new SimPlay { Name = "Defend", BlockGains = 1, BlockAmount = 10 };
        var o = SimEngine.Run(s, play);
        Assert.Equal(20 - 12, o.TakenMax);          // your block: 10 + 2
        Assert.Equal(20 - 5, o.TeammateTakenMax(0)); // theirs: 5
    }

    [Fact]
    public void ATeammatesJuggernautHitsOnTheirBlock()
    {
        // Their Plating 5 sets off their Juggernaut 6 on the only enemy (5 HP): it dies before acting.
        var s = new SimState { Enemies = { Enemy(1, 5, 10) } };
        var mate = Mate();
        mate.Plating = 5;
        mate.Juggernaut = 6;
        s.Teammates.Add(mate);
        var o = SimEngine.Run(s);
        Assert.True(o.DeadInAll(1));
        Assert.Equal(0, o.TakenMax);
    }

    [Fact]
    public void ATeammatesBufferAndTungstenRodWorkLikeYours()
    {
        var s = new SimState { Enemies = { Enemy(1, 50, 10, 10) } };
        var mate = Mate();
        mate.Buffer = 1;
        mate.TungstenRod = 1;
        s.Teammates.Add(mate);
        Assert.Equal(9, SimEngine.Run(s).TeammateTakenMax(0)); // first hit stopped by Buffer, second 10 - 1
    }

    [Fact]
    public void ACardThatChangesIntentsChangesTeammatesHitsToo()
    {
        // A Weak-applying play: hits on you 10 → 7, on the teammate 15 → 11.
        var s = new SimState { Enemies = { Enemy(1, 50, 10) } };
        s.Enemies[0].TeammateHits = new() { new() { 15 } };
        s.Teammates.Add(Mate());
        var play = new SimPlay { Name = "Neutralize", SetHits = { [1] = new() { 7 } }, SetTeammateHits = { [1] = new() { new() { 11 } } } };
        var o = SimEngine.Run(s, play);
        Assert.Equal(7, o.TakenMax);
        Assert.Equal(11, o.TeammateTakenMax(0));
    }
}
