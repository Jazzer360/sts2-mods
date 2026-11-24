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
        var player = new SimPlayer { OrbCapacity = 3 };
        player.Orbs.AddRange(orbs);
        return player;
    }

    private static SimOrb Lightning(int passive = 3, int evoke = 8) => new() { Kind = OrbKind.Lightning, Passive = passive, Evoke = evoke };

    [Fact]
    public void ATeammatesLightningKillsBeforeTheEnemyActs()
    {
        // One enemy at 3 HP attacking for 10: your end of turn does nothing, a teammate's Lightning
        // passive (3) kills it, so it never attacks you.
        var state = new SimState { Enemies = { Enemy(id: 1, hp: 3, hits: 10) } };
        state.Teammates.Add(Defect(Lightning()));
        var outcome = SimEngine.Run(state);
        Assert.True(outcome.DeadInAll(enemyId: 1));
        Assert.Equal(0, outcome.TakenMax);
    }

    [Fact]
    public void TeammatesOrbsRunInPlayerOrderAroundYours()
    {
        // A teammate before you in player order: their Lightning (random target, two enemies) runs
        // before your Lightning, so your orb can only hit what's still alive.
        var state = new SimState { Enemies = { Enemy(id: 1, hp: 3, hits: 5), Enemy(id: 2, hp: 3, hits: 5) } };
        state.You.OrbCapacity = 3;
        state.You.Orbs.Add(Lightning());
        state.Teammates.Add(Defect(Lightning()));
        state.TeammatesBeforeMe = 1;
        var outcome = SimEngine.Run(state);
        // Both Lightnings land on different enemies whichever goes first: both die in every branch.
        Assert.True(outcome.DeadInAll(enemyId: 1));
        Assert.True(outcome.DeadInAll(enemyId: 2));
    }

    [Fact]
    public void TeammatesFrostDoesNotBlockForYou()
    {
        var state = new SimState { Enemies = { Enemy(id: 1, hp: 50, hits: 10) } };
        state.Teammates.Add(Defect(new SimOrb { Kind = OrbKind.Frost, Passive = 2, Evoke = 5 }));
        var outcome = SimEngine.Run(state);
        Assert.Equal(10, outcome.TakenMax);
    }

    [Fact]
    public void TeammatesBeforeSideTurnEndDamageAndConsumingShadow()
    {
        // The Bomb (all enemies) and Consuming Shadow evoking their Glass orb (all enemies) at end of turn.
        var state = new SimState { Enemies = { Enemy(id: 1, hp: 30, hits: 5), Enemy(id: 2, hp: 30, hits: 5) } };
        var player = Defect(new SimOrb { Kind = OrbKind.Glass, Passive = 0, Evoke = 0, GlassBase = 0 });
        player.Powers = player.Powers.With(PowerId.TheBomb, amount: 1);
        player.Fields = player.Fields.With($"{PowerId.TheBomb.Name}@1", amount: 1).With($"{PowerId.TheBomb.Name}@1.Var.Damage", amount: 20);
        player.Orbs[0] = new SimOrb { Kind = OrbKind.Glass, Passive = 4, Evoke = 8, GlassBase = 4 };
        player.ConsumingShadow = 1;
        state.Teammates.Add(player);
        var outcome = SimEngine.Run(state);
        // 20 (The Bomb) + 4 (Glass passive, then base 3: evoke 6) + 6 (Consuming Shadow evokes the Glass) = 30.
        Assert.True(outcome.DeadInAll(enemyId: 1));
        Assert.True(outcome.DeadInAll(enemyId: 2));
    }

    [Fact]
    public void ATeammatesHandIsUnknownSoItsEffectsDontCount()
    {
        // Screaming Flagon needs an empty hand; a teammate's hand isn't known (HandCount -1): no damage.
        var state = new SimState { Enemies = { Enemy(id: 1, hp: 5, hits: 10) } };
        var player = Defect();
        player.HandCount = -1;
        state.Teammates.Add(player);
        state.WithRelic(Who.Teammate(0), RelicId.ScreamingFlagon, "Damage", value: 20);
        Assert.False(SimEngine.Run(state).DeadInAll(enemyId: 1));
    }

    // ---- teammates as players (AttackCommand: each hit, CreatureCmd.Damage over every living player in order)

    private static SimPlayer Mate(int hp = 50, int block = 0) => new() { Hp = hp, Block = block, HandCount = 0 };

    [Fact]
    public void EachHitLandsOnEveryPlayerWithTheirOwnAmount()
    {
        // Two hits of 10 on you; the teammate is Vulnerable (15 per hit) with 5 block.
        var state = new SimState { Enemies = { Enemy(id: 1, hp: 50, hits: [10, 10]) } };
        state.Enemies[0].TeammateHits = new() { new() { 15, 15 } };
        state.Teammates.Add(Mate(block: 5));
        var outcome = SimEngine.Run(state);
        Assert.Equal(20, outcome.TakenMax);
        Assert.Equal(25, outcome.TeammateTakenMax(teammateIndex: 0)); // 30 - 5 block
    }

    [Fact]
    public void ATeammatesThornsKillingTheAttackerStillLetsThatHitReachYou()
    {
        // Teammate before you in player order with Thorns 5; the enemy has 3 HP and hits 10 x2. Their
        // Thorns kills it during hit 1, which still reaches you (the game checks the attacker only
        // between hits); hit 2 is cancelled.
        var state = new SimState { Enemies = { Enemy(id: 1, hp: 3, hits: [10, 10]) } };
        var mate = Mate();
        mate.Thorns = 5;
        state.Teammates.Add(mate);
        state.TeammatesBeforeMe = 1;
        var outcome = SimEngine.Run(state);
        Assert.Equal(10, outcome.TakenMax);
        Assert.Equal(10, outcome.TeammateTakenMax(teammateIndex: 0));

        // After you in player order: same, you take hit 1 first.
        state.TeammatesBeforeMe = 0;
        outcome = SimEngine.Run(state);
        Assert.Equal(10, outcome.TakenMax);
        Assert.Equal(10, outcome.TeammateTakenMax(teammateIndex: 0));
    }

    [Fact]
    public void ADeadTeammateTakesNoMoreHits()
    {
        var state = new SimState { Enemies = { Enemy(id: 1, hp: 50, hits: [10, 10, 10]) } };
        state.Teammates.Add(Mate(hp: 15));
        var outcome = SimEngine.Run(state);
        Assert.Equal(20, outcome.TeammateTakenMax(teammateIndex: 0)); // 10, then 10 more (5 of it overkill, counted like yours: decision #93), then nothing
        Assert.True(outcome.Worst.Teammates[0].Died);
        Assert.Equal(30, outcome.TakenMax);
    }

    [Fact]
    public void ATeammatesBeaconOfHopeSharesTheirEndOfTurnBlockWithYou()
    {
        // Their Plating 7 at end of turn: you get half, rounded down (3); Plating 1 gives nothing (0.5).
        var state = new SimState { Enemies = { Enemy(id: 1, hp: 50, hits: 10) } };
        var mate = Mate();
        mate.Plating = 7;
        mate.BeaconOfHope = true;
        state.Teammates.Add(mate);
        Assert.Equal(7, SimEngine.Run(state).TakenMax);

        state.Teammates[0].Plating = 1;
        Assert.Equal(10, SimEngine.Run(state).TakenMax);
    }

    [Fact]
    public void YourBeaconOfHopeAndTheirsPassBlockOnOnce()
    {
        // You gain 10 block with Beacon of Hope: the teammate gets 5; their Beacon passes 2 back to you
        // (you get it, but yours is held while it gives, so nothing goes round again).
        var state = new SimState { Enemies = { Enemy(id: 1, hp: 50, hits: 20) } };
        state.You.BeaconOfHope = true;
        var mate = Mate();
        mate.BeaconOfHope = true;
        state.Teammates.Add(mate);
        var play = new SimPlay { Name = "Defend", BlockGains = 1, BlockAmount = 10 };
        var outcome = SimEngine.Run(state, play);
        Assert.Equal(20 - 12, outcome.TakenMax);          // your block: 10 + 2
        Assert.Equal(20 - 5, outcome.TeammateTakenMax(teammateIndex: 0)); // theirs: 5
    }

    [Fact]
    public void ATeammatesJuggernautHitsOnTheirBlock()
    {
        // Their Plating 5 sets off their Juggernaut 6 on the only enemy (5 HP): it dies before acting.
        var state = new SimState { Enemies = { Enemy(id: 1, hp: 5, hits: 10) } };
        var mate = Mate();
        mate.Plating = 5;
        mate.Juggernaut = 6;
        state.Teammates.Add(mate);
        var outcome = SimEngine.Run(state);
        Assert.True(outcome.DeadInAll(enemyId: 1));
        Assert.Equal(0, outcome.TakenMax);
    }

    [Fact]
    public void ATeammatesBufferAndTungstenRodWorkLikeYours()
    {
        var state = new SimState { Enemies = { Enemy(id: 1, hp: 50, hits: [10, 10]) } };
        var mate = Mate();
        mate.Buffer = 1;
        mate.TungstenRod = 1;
        state.Teammates.Add(mate);
        Assert.Equal(9, SimEngine.Run(state).TeammateTakenMax(teammateIndex: 0)); // first hit stopped by Buffer, second 10 - 1
    }

    [Fact]
    public void ACardThatChangesIntentsChangesTeammatesHitsToo()
    {
        // A Weak-applying play: hits on you 10 → 7, on the teammate (an unmodelled ×1.5 on them) 15 → 11.
        var state = new SimState { Enemies = { Enemy(id: 1, hp: 50, hits: 10) } };
        state.Enemies[0].TeammateHits = new() { new() { 15 } };
        state.Enemies[0].Intent = new List<IntentHit> { new(Base: 10) { Captured = new Dictionary<int, CapturedTerms> { [Who.Teammate(teammateIndex: 0)] = new() { Mul = 1.5m } } } };
        state.Teammates.Add(Mate());
        var play = ScriptBuilder.Play(ScriptBuilder.Card("Neutralize", SimCardType.Skill), target: 1, plays: 1, new ApplyPowerAction(PowerId.Weak, Amount: 1) { Debuff = true });
        var outcome = SimEngine.Run(state, play);
        Assert.Equal(7, outcome.TakenMax);
        Assert.Equal(11, outcome.TeammateTakenMax(teammateIndex: 0));
    }
    // ---- Cards aimed at a teammate (TargetType AnyAlly): one simulation per teammate, what lands on them goes to them.

    [Fact]
    public void AllyBlockGoesToTheTargetedTeammateNotYou()
    {
        // Lift (8 block) on the teammate: the enemy hits each of you for 10. They lose 2, you lose 10.
        var state = new SimState { Enemies = { Enemy(id: 1, hp: 50, hits: 10) } };
        state.Enemies[0].TeammateHits = new() { new() { 10 } };
        state.Teammates.Add(Mate());
        var play = new SimPlay { BlockGains = 1, BlockAmount = 8, BlockToAlly = true, AllyTarget = 0 };
        var outcome = SimEngine.Run(state, play);
        Assert.Equal(10, outcome.TakenMax);
        Assert.Equal(2, outcome.TeammateTakenMax(teammateIndex: 0));
    }

    [Fact]
    public void AllyBlockWithNoTeammateToAimAtGoesToNoOne()
    {
        // No living teammate to target: the block must never land on you instead.
        var state = new SimState { Enemies = { Enemy(id: 1, hp: 50, hits: 10) } };
        var play = new SimPlay { BlockGains = 1, BlockAmount = 8, BlockToAlly = true, AllyTarget = -1 };
        Assert.Equal(10, SimEngine.Run(state, play).TakenMax);
    }

    [Fact]
    public void AllyChannelGoesIntoTheTeammatesOrbsAndEvokesTheirFrontOrb()
    {
        // Ignition on a teammate with one full slot of Frost (evoke 5): their Frost evokes for 5 block on them,
        // then the Plasma takes the slot. Your orbs are untouched.
        var state = new SimState { Enemies = { Enemy(id: 1, hp: 50) } };
        var mate = new SimPlayer { Hp = 60, OrbCapacity = 1 };
        mate.Orbs.Add(new SimOrb { Kind = OrbKind.Frost, Passive = 2, Evoke = 5 });
        state.Teammates.Add(mate);
        state.You.OrbCapacity = 3;
        var play = new SimPlay { ChannelToAlly = true, AllyTarget = 0 };
        play.OrbOps.Add(new OrbOp { Op = OrbOp.Kind.Channel, Orb = new SimOrb { Kind = OrbKind.Plasma, Passive = 1, Evoke = 2 } });
        SimEngine.ApplyPlayTo(state, play, out var branches, out _);
        var after = Assert.Single(branches);
        Assert.Equal(5, after.Teammates[0].Block);
        Assert.Equal(OrbKind.Plasma, Assert.Single(after.Teammates[0].Orbs).Kind);
        Assert.Empty(after.You.Orbs);
        Assert.Single(state.Teammates[0].Orbs); // the start state is untouched
        Assert.Equal(OrbKind.Frost, state.Teammates[0].Orbs[0].Kind);
    }

    [Fact]
    public void AllyChannelEvokingTheirLightningHitsAnEnemy()
    {
        // The teammate's full slot holds a Lightning (evoke 8): channelling into it evokes it at the enemy (6 HP).
        var state = new SimState { Enemies = { Enemy(id: 1, hp: 6, hits: 10) } };
        var mate = new SimPlayer { Hp = 60, OrbCapacity = 1 };
        mate.Orbs.Add(Lightning());
        state.Teammates.Add(mate);
        var play = new SimPlay { ChannelToAlly = true, AllyTarget = 0 };
        play.OrbOps.Add(new OrbOp { Op = OrbOp.Kind.Channel, Orb = new SimOrb { Kind = OrbKind.Plasma, Passive = 1, Evoke = 2 } });
        var outcome = SimEngine.Run(state, play);
        Assert.True(outcome.DeadInAll(enemyId: 1));
        Assert.Equal(0, outcome.TakenMax);
    }
    [Fact]
    public void InterceptMovesTheAttacksOntoYou()
    {
        // Intercept on the teammate: Covered (CoveredPower.AfterApplied gives you Intercept) zeroes the powered attacks on them, your
        // InterceptPower doubles those on you (covered players + 1). The enemy hits 10 each.
        var state = new SimState { Enemies = { Enemy(id: 1, hp: 50, hits: 10) } };
        state.Enemies[0].TeammateHits = new() { new() { 10 } };
        state.Enemies[0].Intent = new List<IntentHit> { new(Base: 10) };
        state.Teammates.Add(Mate());
        var play = ScriptBuilder.Play(ScriptBuilder.Card("Intercept", SimCardType.Skill), Who.Teammate(teammateIndex: 0), plays: 1,
            new ApplyPowerAction(PowerId.Covered, Amount: 1) { To = ApplyPowerAction.Where.Creature, Creature = Who.Teammate(teammateIndex: 0) });
        var outcome = SimEngine.Run(state, play);
        Assert.Equal(20, outcome.TakenMax);
        Assert.Equal(0, outcome.TeammateTakenMax(teammateIndex: 0));
        Assert.Equal(new[] { 10 }, state.Enemies[0].Hits); // the start state is untouched
    }

    [Fact]
    public void ATeammatesWuppyTakesTheHitsAimedAtThemFirst()
    {
        // WuppysLoyaltyPower.ModifyPreBlockDamageTarget on a teammate's pet, as on yours: 7 into their Wuppy at 5 HP, the 2 left
        // over goes on to their 1 block and them. Their Thorns still hits back (BeforeDamageReceived: they're still the target).
        var state = new SimState { Enemies = { Enemy(id: 1, hp: 50, hits: 7) } };
        var mate = new SimPlayer { Hp = 30, Block = 1, WuppyHp = 5 };
        mate.Thorns = 3;
        state.Teammates.Add(mate);
        var outcome = SimEngine.Run(state);
        Assert.Equal(1, outcome.TeammateTakenMax(teammateIndex: 0));
        Assert.Equal(0, outcome.Worst.Teammates[0].WuppyHp);
        Assert.Equal(2, outcome.Worst.Teammates[0].Incoming); // what reached them past Wuppy
        Assert.Equal(47, outcome.Worst.Enemy(id: 1)!.Hp);
    }

    [Fact]
    public void ATeammatesWuppyThatTakesTheWholeHitLeavesThemUntouched()
    {
        var state = new SimState { Enemies = { Enemy(id: 1, hp: 50, hits: 4) } };
        state.Teammates.Add(new SimPlayer { Hp = 30, Block = 3, WuppyHp = 10 });
        var outcome = SimEngine.Run(state);
        Assert.Equal(0, outcome.TeammateTakenMax(teammateIndex: 0));
        Assert.Equal(3, outcome.Worst.Teammates[0].Block);
        Assert.Equal(6, outcome.Worst.Teammates[0].WuppyHp);
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
        var state = new SimState { Enemies = { new SimEnemy { Id = 0, Name = "E0", Hp = 50, Powers = PowerSet.Empty.With(PowerId.Thorns, amount: 3) } } };
        state.Teammates.Add(new SimPlayer { Hp = 40, Block = 1 });
        SimEngine.Damage(state, state.Enemies[0], amount: 6, Who.Teammate(teammateIndex: 0), SimProps.Move);
        Assert.Equal(0, state.Teammates[0].Block);
        Assert.Equal(2, state.Teammates[0].Taken);
        Assert.Equal(0, state.You.Taken);
        Assert.Equal(44, state.Enemies[0].Hp);
    }

    [Fact]
    public void PlayersComeInPlayerOrderWithYouAtYourPlace()
    {
        Assert.Equal(new[] { Who.You }, Who.InPlayerOrder(teammates: 0, teammatesBeforeYou: 0));
        Assert.Equal(new[] { Who.Teammate(teammateIndex: 0), Who.You, Who.Teammate(teammateIndex: 1) }, Who.InPlayerOrder(teammates: 2, teammatesBeforeYou: 1));
        Assert.Equal(new[] { Who.Teammate(teammateIndex: 0), Who.Teammate(teammateIndex: 1), Who.Teammate(teammateIndex: 2), Who.Teammate(teammateIndex: 3), Who.You }, Who.InPlayerOrder(teammates: 4, teammatesBeforeYou: 4));
        Assert.Same(Who.InPlayerOrder(teammates: 3, teammatesBeforeYou: 2), Who.InPlayerOrder(teammates: 3, teammatesBeforeYou: 2)); // made once
    }

    [Fact]
    public void EveryIdHasOneKind()
    {
        Assert.Equal(CreatureKind.Enemy, Who.KindOf(id: 0));
        Assert.Equal(CreatureKind.You, Who.KindOf(Who.You));
        Assert.Equal(CreatureKind.Teammate, Who.KindOf(Who.Teammate(teammateIndex: 0)));
        Assert.Equal(CreatureKind.YourOsty, Who.KindOf(Who.YourOsty));
        Assert.Equal(CreatureKind.None, Who.KindOf(Who.None)); // no one: never read as a teammate or you
        Assert.Equal(-1, Who.TeammateIndex(Who.None));
        Assert.Equal(-1, Who.TeammateIndex(id: 0));
        Assert.Equal(2, Who.TeammateIndex(Who.Teammate(teammateIndex: 2)));
        var state = new SimState();
        state.Teammates.Add(new SimPlayer());
        Assert.Same(state.You, state.Player(Who.You));
        Assert.Same(state.Teammates[0], state.Player(Who.Teammate(teammateIndex: 0)));
        Assert.Null(state.Player(id: 0));
        Assert.Null(state.Player(Who.Teammate(teammateIndex: 1)));
    }
}
