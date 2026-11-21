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
        Assert.Equal(20, o.TeammateTakenMax(0)); // 10, then 10 more (5 of it overkill, counted like yours: decision #93), then nothing
        Assert.True(o.Worst.Teammates[0].Died);
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
        // A Weak-applying play: hits on you 10 → 7, on the teammate (an unmodelled ×1.5 on them) 15 → 11.
        var s = new SimState { Enemies = { Enemy(1, 50, 10) } };
        s.Enemies[0].TeammateHits = new() { new() { 15 } };
        s.Enemies[0].Intent = new List<IntentHit> { new(10) { Captured = new Dictionary<int, CapturedTerms> { [Who.Teammate(0)] = new() { Mul = 1.5m } } } };
        s.Teammates.Add(Mate());
        var play = V2.V2Script.Play(V2.V2Script.Card("Neutralize", SimCardType.Skill), 1, 1, new ApplyPowerAction(PowerId.Weak, 1) { Debuff = true });
        var o = SimEngine.Run(s, play);
        Assert.Equal(7, o.TakenMax);
        Assert.Equal(11, o.TeammateTakenMax(0));
    }
    // ---- Cards aimed at a teammate (TargetType AnyAlly): one simulation per teammate, what lands on them goes to them.

    [Fact]
    public void AllyBlockGoesToTheTargetedTeammateNotYou()
    {
        // Lift (8 block) on the teammate: the enemy hits each of you for 10. They lose 2, you lose 10.
        var s = new SimState { Enemies = { Enemy(1, 50, 10) } };
        s.Enemies[0].TeammateHits = new() { new() { 10 } };
        s.Teammates.Add(Mate());
        var play = new SimPlay { BlockGains = 1, BlockAmount = 8, BlockToAlly = true, AllyTarget = 0 };
        var o = SimEngine.Run(s, play);
        Assert.Equal(10, o.TakenMax);
        Assert.Equal(2, o.TeammateTakenMax(0));
    }

    [Fact]
    public void AllyBlockWithNoTeammateToAimAtGoesToNoOne()
    {
        // No living teammate to target: the block must never land on you instead.
        var s = new SimState { Enemies = { Enemy(1, 50, 10) } };
        var play = new SimPlay { BlockGains = 1, BlockAmount = 8, BlockToAlly = true, AllyTarget = -1 };
        Assert.Equal(10, SimEngine.Run(s, play).TakenMax);
    }

    [Fact]
    public void AllyChannelGoesIntoTheTeammatesOrbsAndEvokesTheirFrontOrb()
    {
        // Ignition on a teammate with one full slot of Frost (evoke 5): their Frost evokes for 5 block on them,
        // then the Plasma takes the slot. Your orbs are untouched.
        var s = new SimState { Enemies = { Enemy(1, 50) } };
        var mate = new SimPlayer { Hp = 60, OrbCapacity = 1 };
        mate.Orbs.Add(new SimOrb { Kind = OrbKind.Frost, Passive = 2, Evoke = 5 });
        s.Teammates.Add(mate);
        s.P.OrbCapacity = 3;
        var play = new SimPlay { ChannelToAlly = true, AllyTarget = 0 };
        play.OrbOps.Add(new OrbOp { Op = OrbOp.Kind.Channel, Orb = new SimOrb { Kind = OrbKind.Plasma, Passive = 1, Evoke = 2 } });
        SimEngine.ApplyPlayTo(s, play, out var branches, out _);
        var after = Assert.Single(branches);
        Assert.Equal(5, after.Teammates[0].Block);
        Assert.Equal(OrbKind.Plasma, Assert.Single(after.Teammates[0].Orbs).Kind);
        Assert.Empty(after.P.Orbs);
        Assert.Single(s.Teammates[0].Orbs); // the start state is untouched
        Assert.Equal(OrbKind.Frost, s.Teammates[0].Orbs[0].Kind);
    }

    [Fact]
    public void AllyChannelEvokingTheirLightningHitsAnEnemy()
    {
        // The teammate's full slot holds a Lightning (evoke 8): channelling into it evokes it at the enemy (6 HP).
        var s = new SimState { Enemies = { Enemy(1, 6, 10) } };
        var mate = new SimPlayer { Hp = 60, OrbCapacity = 1 };
        mate.Orbs.Add(Lightning());
        s.Teammates.Add(mate);
        var play = new SimPlay { ChannelToAlly = true, AllyTarget = 0 };
        play.OrbOps.Add(new OrbOp { Op = OrbOp.Kind.Channel, Orb = new SimOrb { Kind = OrbKind.Plasma, Passive = 1, Evoke = 2 } });
        var o = SimEngine.Run(s, play);
        Assert.True(o.DeadInAll(1));
        Assert.Equal(0, o.TakenMax);
    }
    [Fact]
    public void InterceptMovesTheAttacksOntoYou()
    {
        // Intercept on the teammate: Covered (CoveredPower.AfterApplied gives you Intercept) zeroes the powered attacks on them, your
        // InterceptPower doubles those on you (covered players + 1). The enemy hits 10 each.
        var s = new SimState { Enemies = { Enemy(1, 50, 10) } };
        s.Enemies[0].TeammateHits = new() { new() { 10 } };
        s.Enemies[0].Intent = new List<IntentHit> { new(10) };
        s.Teammates.Add(Mate());
        var play = V2.V2Script.Play(V2.V2Script.Card("Intercept", SimCardType.Skill), Who.Teammate(0), 1,
            new ApplyPowerAction(PowerId.Covered, 1) { To = ApplyPowerAction.Where.Creature, Creature = Who.Teammate(0) });
        var o = SimEngine.Run(s, play);
        Assert.Equal(20, o.TakenMax);
        Assert.Equal(0, o.TeammateTakenMax(0));
        Assert.Equal(new[] { 10 }, s.Enemies[0].Hits); // the start state is untouched
    }

    [Fact]
    public void ATeammatesWuppyTakesTheHitsAimedAtThemFirst()
    {
        // WuppysLoyaltyPower.ModifyPreBlockDamageTarget on a teammate's pet, as on yours: 7 into their Wuppy at 5 HP, the 2 left
        // over goes on to their 1 block and them. Their Thorns still hits back (BeforeDamageReceived: they're still the target).
        var s = new SimState { Enemies = { Enemy(1, 50, 7) } };
        var mate = new SimPlayer { Hp = 30, Block = 1, WuppyHp = 5 };
        mate.Thorns = 3;
        s.Teammates.Add(mate);
        var o = SimEngine.Run(s);
        Assert.Equal(1, o.TeammateTakenMax(0));
        Assert.Equal(0, o.Worst.Teammates[0].WuppyHp);
        Assert.Equal(2, o.Worst.Teammates[0].Incoming); // what reached them past Wuppy
        Assert.Equal(47, o.Worst.Enemy(1)!.Hp);
    }

    [Fact]
    public void ATeammatesWuppyThatTakesTheWholeHitLeavesThemUntouched()
    {
        var s = new SimState { Enemies = { Enemy(1, 50, 4) } };
        s.Teammates.Add(new SimPlayer { Hp = 30, Block = 3, WuppyHp = 10 });
        var o = SimEngine.Run(s);
        Assert.Equal(0, o.TeammateTakenMax(0));
        Assert.Equal(3, o.Worst.Teammates[0].Block);
        Assert.Equal(6, o.Worst.Teammates[0].WuppyHp);
    }
}

/// <summary>
/// One id per creature (Who): an enemy's Thorns hits back at whoever made the attack, you or a teammate. A teammate's index into
/// SimState.Teammates is never an id (teammate 0 is Who.Teammate(0), and 0 is the first enemy).
/// </summary>
public class SimCreatureIdTests
{
    [Fact]
    public void ThornsHitsBackTheTeammateWhoAttacked()
    {
        // A teammate's powered hit on an enemy with 3 Thorns: the teammate takes the 3 (blockable), not you, not the enemy.
        var s = new SimState { Enemies = { new SimEnemy { Id = 0, Name = "E0", Hp = 50, Powers = PowerSet.Empty.With(PowerId.Thorns, 3) } } };
        s.Teammates.Add(new SimPlayer { Hp = 40, Block = 1 });
        SimEngine.Damage(s, s.Enemies[0], 6, Who.Teammate(0), SimProps.Move);
        Assert.Equal(0, s.Teammates[0].Block);
        Assert.Equal(2, s.Teammates[0].Taken);
        Assert.Equal(0, s.P.Taken);
        Assert.Equal(44, s.Enemies[0].Hp);
    }

    [Fact]
    public void PlayersComeInPlayerOrderWithYouAtYourPlace()
    {
        Assert.Equal(new[] { Who.You }, Who.InPlayerOrder(0, 0));
        Assert.Equal(new[] { Who.Teammate(0), Who.You, Who.Teammate(1) }, Who.InPlayerOrder(2, 1));
        Assert.Equal(new[] { Who.Teammate(0), Who.Teammate(1), Who.Teammate(2), Who.Teammate(3), Who.You }, Who.InPlayerOrder(4, 4));
        Assert.Same(Who.InPlayerOrder(3, 2), Who.InPlayerOrder(3, 2)); // made once
    }

    [Fact]
    public void EveryIdHasOneKind()
    {
        Assert.Equal(CreatureKind.Enemy, Who.KindOf(0));
        Assert.Equal(CreatureKind.You, Who.KindOf(Who.You));
        Assert.Equal(CreatureKind.Teammate, Who.KindOf(Who.Teammate(0)));
        Assert.Equal(CreatureKind.YourOsty, Who.KindOf(Who.YourOsty));
        Assert.Equal(-1, Who.TeammateIndex(0));
        Assert.Equal(2, Who.TeammateIndex(Who.Teammate(2)));
        var s = new SimState();
        s.Teammates.Add(new SimPlayer());
        Assert.Same(s.P, s.Player(Who.You));
        Assert.Same(s.Teammates[0], s.Player(Who.Teammate(0)));
        Assert.Null(s.Player(0));
        Assert.Null(s.Player(Who.Teammate(1)));
    }
}
