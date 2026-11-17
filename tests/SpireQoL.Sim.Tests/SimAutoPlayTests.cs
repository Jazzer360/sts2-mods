using SpireQoL.Sim;
using Xunit;

namespace SpireQoL.Sim.Tests;

/// <summary>
/// The auto-play phase of your end of turn (decompile v0.113.1: CombatManager.EndPlayerTurnPhaseOneInternal sets
/// PlayerTurnPhase.AutoPostPlay and runs Hook.AfterAutoPostPlayPhaseEntered before BeforeSideTurnEnd). Stampede
/// auto-plays a random Attack from your hand, Simpleton a random card among your cheapest (each its Amount times),
/// I Am Invincible plays from the top of your draw pile, Howl from Beyond from your exhaust pile. CardCmd.AutoPlay
/// aims a card that needs an enemy at a random hittable one. Random picks are branches.
/// </summary>
public class SimAutoPlayTests
{
    private static SimEnemy Enemy(int id, int hp, params int[] hits) => new() { Id = id, Name = $"E{id}", Hp = hp, Hits = hits.ToList() };

    private static SimPlay Strike(int target, int damage, int hand = 0)
        => new() { Name = "Strike", IsAttack = true, HitTarget = SimPlay.Target.Single, TargetId = target, HitDamage = { [target] = damage }, Hits = 1, HandCard = hand, HandDelta = -1 };

    private static SimPlay AllEnemies(string name, int damage, params int[] ids)
    {
        var p = new SimPlay { Name = name, IsAttack = true, HitTarget = SimPlay.Target.All, Hits = 1, HandDelta = -1 };
        foreach (var id in ids) p.HitDamage[id] = damage;
        return p;
    }

    /// <summary>A Strike in hand: one play per enemy it can be aimed at.</summary>
    private static SimHandCard StrikeCard(int id, int damage, int cost, params int[] targets)
        => new() { Id = id, Name = "Strike", IsAttack = true, Cost = cost, Plays = targets.Select(t => Strike(t, damage, id)).ToList() };

    private static SimHandCard Defend(int id, int block, int cost)
        => new() { Id = id, Name = "Defend", Cost = cost, Plays = { new SimPlay { Name = "Defend", BlockAmount = block, BlockGains = 1, HandCard = id, HandDelta = -1 } } };

    [Fact]
    public void HowlFromBeyondInYourExhaustPileHitsEveryEnemy()
    {
        var s = new SimState { Enemies = { Enemy(1, 10, 5), Enemy(2, 12, 5) } };
        s.P.ExhaustAutoPlays.Add(AllEnemies("HowlFromBeyond", 10, 1, 2));
        var o = SimEngine.Run(s);
        Assert.True(o.DeadInAll(1));
        Assert.False(o.DeadInAny(2));
        Assert.Equal(5, o.TakenMax);
        Assert.All(o.Branches, b => Assert.Equal(1, b.AutoPlayed));
    }

    [Fact]
    public void EachHowlCopyPlays()
    {
        var s = new SimState { Enemies = { Enemy(1, 20, 5) } };
        s.P.ExhaustAutoPlays.Add(AllEnemies("HowlFromBeyond", 10, 1));
        s.P.ExhaustAutoPlays.Add(AllEnemies("HowlFromBeyond", 10, 1));
        Assert.True(SimEngine.Run(s).DeadInAll(1));
    }

    [Fact]
    public void AnAutoPlayedAttackCountsAgainstRippleBasin()
    {
        // Ripple Basin (BeforeSideTurnEnd) checks for an attack played this turn: the auto-play phase comes first.
        var s = new SimState { Enemies = { Enemy(1, 50, 10) } };
        s.P.RippleBasinBlock = 4;
        Assert.Equal(6, SimEngine.Run(s).TakenMax);
        s.P.ExhaustAutoPlays.Add(AllEnemies("HowlFromBeyond", 3, 1));
        Assert.Equal(10, SimEngine.Run(s).TakenMax);
    }

    [Fact]
    public void IAmInvincibleOnTopGainsItsBlockBeforeOrichalcumLooks()
    {
        // Its block comes in the auto-play phase, so Orichalcum (no block at BeforeSideTurnEnd) doesn't arm.
        var s = new SimState { Enemies = { Enemy(1, 50, 10) } };
        s.P.OrichalcumBlock = 6;
        Assert.Equal(4, SimEngine.Run(s).TakenMax);
        s.P.InvincibleBlock = 8;
        s.P.InvincibleDrawPile = 2;
        Assert.Equal(2, SimEngine.Run(s).TakenMax);
    }

    [Fact]
    public void DrawingTheWholePileLeavesTheTopUnknown()
    {
        var s = new SimState { Enemies = { Enemy(1, 50, 10), Enemy(2, 50) } };
        s.P.InvincibleBlock = 8;
        s.P.InvincibleDrawPile = 2;
        var draws = new SimPlay { Name = "Acrobatics", Draws = 1, HandDelta = 0 };
        Assert.Equal(2, SimEngine.Run(s, draws).TakenMax); // one left: still on top
        draws.Draws = 2;
        Assert.Equal(10, SimEngine.Run(s, draws).TakenMax); // the discard pile shuffles in: unknown
    }

    [Fact]
    public void StampedeAimsAtARandomEnemy()
    {
        // One Attack in hand, two enemies at 5: it kills one of them, at random.
        var s = new SimState { Enemies = { Enemy(1, 5, 4), Enemy(2, 5, 4) } };
        s.P.Stampede = 1;
        s.P.AutoHand = new() { StrikeCard(11, 6, 1, 1, 2) };
        var o = SimEngine.Run(s);
        Assert.True(o.DeadInAny(1) && o.DeadInAny(2));
        Assert.False(o.DeadInAll(1) || o.DeadInAll(2));
        Assert.Equal(4, o.TakenMax);
        Assert.Equal(0.5, o.DeadChance(1), 3);
    }

    [Fact]
    public void StampedePicksOnlyAttacks()
    {
        var s = new SimState { Enemies = { Enemy(1, 5, 4) } };
        s.P.Stampede = 1;
        s.P.AutoHand = new() { Defend(12, 5, 1) };
        var o = SimEngine.Run(s);
        Assert.Equal(4, o.TakenMax); // the Defend isn't played
        Assert.All(o.Branches, b => Assert.Equal(0, b.AutoPlayed));
    }

    [Fact]
    public void StampedeTwiceTakesTwoDifferentCards()
    {
        // Two Strikes: the second pick is the card left, so both always play.
        var s = new SimState { Enemies = { Enemy(1, 12, 4) } };
        s.P.Stampede = 2;
        s.P.AutoHand = new() { StrikeCard(11, 6, 1, 1), StrikeCard(13, 6, 1, 1) };
        Assert.True(SimEngine.Run(s).DeadInAll(1));
    }

    [Fact]
    public void TheCardYouPlayIsNoLongerThereToPick()
    {
        // Playing the only Attack yourself: Stampede has nothing left, so it isn't played twice.
        var s = new SimState { Enemies = { Enemy(1, 12, 4) } };
        s.P.Stampede = 1;
        s.P.AutoHand = new() { StrikeCard(11, 6, 1, 1) };
        Assert.True(SimEngine.Run(s).DeadInAny(1) == false);
        var o = SimEngine.Run(s, Strike(1, 6, 11));
        Assert.False(o.DeadInAny(1));
        Assert.All(o.Branches, b => Assert.Equal(6, b.Enemy(1)!.HpAtTurnEnd));
    }

    [Fact]
    public void APlayThatChangesYourHandEndsWhatsPredicted()
    {
        var s = new SimState { Enemies = { Enemy(1, 6, 4) } };
        s.P.Stampede = 1;
        s.P.AutoHand = new() { StrikeCard(11, 6, 1, 1) };
        var o = SimEngine.Run(s, new SimPlay { Name = "Acrobatics", Draws = 2, HandDelta = 1 });
        Assert.All(o.Branches, b => Assert.True(b.AutoPlaysUnknown));
        Assert.False(o.DeadInAny(1));
    }

    [Fact]
    public void ACardThatCantBeSimulatedEndsWhatsPredicted()
    {
        var s = new SimState { Enemies = { Enemy(1, 6, 4) } };
        s.P.Stampede = 2;
        s.P.AutoHand = new() { new SimHandCard { Id = 21, Name = "Pommel Strike", IsAttack = true, Cost = 1 } };
        var o = SimEngine.Run(s);
        Assert.All(o.Branches, b => Assert.True(b.AutoPlaysUnknown));
        Assert.All(o.Branches, b => Assert.Equal(1, b.AutoPlayed));
    }

    [Fact]
    public void SimpletonPlaysYourCheapestCard()
    {
        // The 0-cost Strike, not the 1-cost Defend.
        var s = new SimState { Enemies = { Enemy(1, 6, 4) } };
        s.P.Simpleton = 1;
        s.P.AutoHand = new() { Defend(12, 5, 1), StrikeCard(11, 6, 0, 1) };
        var o = SimEngine.Run(s);
        Assert.True(o.DeadInAll(1));
        Assert.Equal(0, o.TakenMax);
    }

    [Fact]
    public void SimpletonPicksAtRandomAmongTies()
    {
        // Strike or Defend, both 1: a kill in one branch, 5 block in the other (4 taken at worst, 0 at best).
        var s = new SimState { Enemies = { Enemy(1, 6, 4) } };
        s.P.Simpleton = 1;
        s.P.AutoHand = new() { Defend(12, 5, 1), StrikeCard(11, 6, 1, 1) };
        var o = SimEngine.Run(s);
        Assert.True(o.DeadInAny(1));
        Assert.False(o.DeadInAll(1));
        Assert.Equal(0, o.TakenMax);
    }

    [Fact]
    public void NoTargetLeftMeansItIsntPlayed()
    {
        // Your card killed the only enemy the Strike could aim at: it goes unplayed.
        var s = new SimState { Enemies = { Enemy(1, 5), Enemy(2, 30, 4) } };
        s.P.Stampede = 1;
        s.P.AutoHand = new() { StrikeCard(11, 6, 1, 1) };
        var o = SimEngine.Run(s, Strike(1, 5));
        Assert.All(o.Branches, b => Assert.Equal(30, b.Enemy(2)!.HpAtTurnEnd));
    }

    /// <summary>Teammate 0's Howl from Beyond: their card (the play's owner), with its damage as final numbers.</summary>
    private static SimPlay TeammatesHowl(int damage, params int[] ids)
    {
        var p = AllEnemies("HowlFromBeyond", damage, ids);
        p.HandDelta = 0; // from their exhaust pile
        p.AutoPlay = true;
        p.Card = new SimCardPlay(new SimCard("HowlFromBeyond", SimCardType.Attack), Who.Teammate(0), -1, 0, 1, true);
        return p;
    }

    [Fact]
    public void ATeammatesHowlFromBeyondKillsBeforeTheEnemyActs()
    {
        // Hook.AfterAutoPostPlayPhaseEntered runs for every player ending the turn: their Howl from Beyond in their exhaust
        // pile (you can see it) plays too. It isn't your auto-play (the outcome audit counts yours).
        var s = new SimState { Enemies = { Enemy(1, 10, 5) } };
        var mate = new SimPlayer { Hp = 50 };
        mate.ExhaustAutoPlays.Add(TeammatesHowl(10, 1));
        s.Teammates.Add(mate);
        var o = SimEngine.Run(s);
        Assert.True(o.DeadInAll(1));
        Assert.Equal(0, o.TakenMax);
        Assert.All(o.Branches, b => Assert.Equal(0, b.AutoPlayed));
    }

    [Fact]
    public void ATeammatesAutoPlayedAttackIsTheirsNotYours()
    {
        // Their Howl counts as their Attack this turn: your Ripple Basin still gives its block, and your hand keeps its size.
        var s = new SimState { Enemies = { Enemy(1, 50, 10) } };
        s.P.RippleBasinBlock = 4;
        s.P.HandCount = 3;
        var mate = new SimPlayer { Hp = 50 };
        mate.ExhaustAutoPlays.Add(TeammatesHowl(3, 1));
        s.Teammates.Add(mate);
        var o = SimEngine.Run(s);
        Assert.Equal(6, o.TakenMax);
        Assert.All(o.Branches, b => Assert.Equal(3, b.P.HandCount));
        Assert.All(o.Branches, b => Assert.Equal(1, b.Teammates[0].AttacksThisTurn));
    }

    [Fact]
    public void ATeammatesIAmInvincibleBlocksForThemNotYou()
    {
        var s = new SimState { Enemies = { Enemy(1, 50, 10) } };
        var mate = new SimPlayer { Hp = 50, InvincibleBlock = 8, InvincibleDrawPile = 1 };
        s.Teammates.Add(mate);
        var o = SimEngine.Run(s);
        Assert.Equal(10, o.TakenMax);
        Assert.Equal(2, o.TeammateTakenMax(0));
    }

    [Fact]
    public void YourHowlFromBeyondDoesntLeaveYourHand()
    {
        // It plays from your exhaust pile: Cloak Clasp (block per card in hand, BeforeSideTurnEnd, after the auto-plays) counts
        // the hand as it was. The adapter builds it with HandDelta 0 (SimAdapter.Play: the card isn't in your hand).
        var s = new SimState { Enemies = { Enemy(1, 50, 10) } };
        s.P.HandCount = 3;
        s.P.CloakClaspPerCard = 1;
        var howl = AllEnemies("HowlFromBeyond", 3, 1);
        howl.HandDelta = 0;
        howl.AutoPlay = true;
        s.P.ExhaustAutoPlays.Add(howl);
        Assert.Equal(7, SimEngine.Run(s).TakenMax);
    }

    /// <summary>
    /// A hand card's play as the adapter builds it (SimAdapter.AutoPlay): a card play and its attack, aimed at the play's
    /// TargetId (decision #84: the one place a play's target is kept).
    /// </summary>
    private static SimPlay AdapterStrike(int target, int damage, int hand, int draws = 0) => new()
    {
        Name = "Strike", IsAttack = true, AutoPlay = true, HandCard = hand, HandDelta = -1 + draws, Draws = draws, TargetId = target,
        Card = new SimCardPlay(new SimCard("Strike", SimCardType.Attack), Who.You, target, 0, 1, true),
        Body = { new AttackAction(1, SimPlay.Target.Single, new DamageSpec(0m, SimProps.Move) { BaseOn = new Dictionary<int, decimal> { [target] = damage }, Fixed = true }) },
    };

    [Fact]
    public void APlaysTargetIsItsTargetIdEvenWhenItsCardPlaySaysOtherwise()
    {
        // One place for the target (decision #84): the card play takes the play's TargetId when the script is built.
        var s = new SimState { Enemies = { Enemy(1, 5), Enemy(2, 5) } };
        var play = AdapterStrike(2, 6, 0);
        play.Card = play.Card! with { Target = 1 };
        var o = SimEngine.Run(s, play);
        Assert.True(o.DeadInAll(2));
        Assert.False(o.DeadInAny(1));
    }

    [Fact]
    public void StampedeAimsTheAdaptersPlaysAtEachEnemy()
    {
        // In game (two Exoskeletons at 10 and 6, Strike+ and Perfected Strike in hand, Stampede 1): the plays' target is on
        // the card play, so each one is a possible target. Either card kills whichever it hits: one dies, 50% each.
        var s = new SimState { Enemies = { Enemy(1, 10, 3, 3, 3, 3), Enemy(2, 6) } };
        s.P.Stampede = 1;
        s.P.AutoHand = new()
        {
            new SimHandCard { Id = 11, Name = "Strike", IsAttack = true, Cost = 1, Plays = { AdapterStrike(1, 13, 11), AdapterStrike(2, 13, 11) } },
            new SimHandCard { Id = 13, Name = "PerfectedStrike", IsAttack = true, Cost = 2, Plays = { AdapterStrike(1, 34, 13), AdapterStrike(2, 34, 13) } },
        };
        var o = SimEngine.Run(s);
        Assert.Equal(0.5, o.DeadChance(1), 3);
        Assert.Equal(0.5, o.DeadChance(2), 3);
        Assert.All(o.Branches, b => Assert.Equal(1, b.AutoPlayed));
    }

    [Fact]
    public void AStampedePickThatDrawsStillHits()
    {
        // In game (one Exoskeleton at 7, Strike+ and Pommel Strike in hand, Stampede 1): Pommel Strike draws a card, but its hit
        // lands first, so the enemy dies either way. Only later picks, from the changed hand, aren't predicted.
        var s = new SimState { Enemies = { Enemy(1, 7, 11) } };
        s.P.Stampede = 2;
        s.P.AutoHand = new()
        {
            new SimHandCard { Id = 11, Name = "Strike", IsAttack = true, Cost = 1, Plays = { AdapterStrike(1, 9, 11) } },
            new SimHandCard { Id = 15, Name = "PommelStrike", IsAttack = true, Cost = 1, Plays = { AdapterStrike(1, 9, 15, draws: 1) } },
        };
        var o = SimEngine.Run(s);
        Assert.True(o.DeadInAll(1));
        Assert.Contains(o.Branches, b => b.AutoPlaysUnknown); // Pommel Strike first: the second pick is from a changed hand
    }
}
