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
        var play = new SimPlay { Name = name, IsAttack = true, HitTarget = SimPlay.Target.All, Hits = 1, HandDelta = -1 };
        foreach (var id in ids) play.HitDamage[id] = damage;
        return play;
    }

    /// <summary>A Strike in hand: one play per enemy it can be aimed at.</summary>
    private static SimHandCard StrikeCard(int id, int damage, int cost, params int[] targets)
        => new() { Id = id, Name = "Strike", IsAttack = true, Cost = cost, Plays = targets.Select(target => Strike(target, damage, id)).ToList() };

    private static SimHandCard Defend(int id, int block, int cost)
        => new() { Id = id, Name = "Defend", Cost = cost, Plays = { new SimPlay { Name = "Defend", BlockAmount = block, BlockGains = 1, HandCard = id, HandDelta = -1 } } };

    [Fact]
    public void BlockAProcGivesAnAutoPlayIsEndOfTurnBlock()
    {
        // Issues #370, #392: Stampede plays a Strike, Daughter of the Wind gives 1 block. It lowers what you lose and shows in the
        // breakdown's end-of-turn block (what the forecast check compares with the block the game gave).
        var state = new SimState { Enemies = { Enemy(id: 1, hp: 50, hits: 10) }, Log = true };
        state.You.Relics = state.You.Relics.With(RelicId.DaughterOfTheWind, amount: 1, data: 0);
        state.Vars["-1:DaughterOfTheWind:Block"] = 1;
        state.You.Stampede = 1;
        state.You.AutoHand = new() { StrikeCard(id: 11, damage: 6, cost: 1, targets: 1) };
        var branch = SimEngine.Run(state).Worst;
        Assert.Equal(9, branch.Taken);
        Assert.Equal(1, branch.EndOfTurnBlock.Sum(nameAmount => nameAmount.Amount));
    }

    [Fact]
    public void HowlFromBeyondInYourExhaustPileHitsEveryEnemy()
    {
        var state = new SimState { Enemies = { Enemy(id: 1, hp: 10, hits: 5), Enemy(id: 2, hp: 12, hits: 5) } };
        state.You.ExhaustAutoPlays.Add(AllEnemies("HowlFromBeyond", damage: 10, ids: [1, 2]));
        var outcome = SimEngine.Run(state);
        Assert.True(outcome.DeadInAll(enemyId: 1));
        Assert.False(outcome.DeadInAny(enemyId: 2));
        Assert.Equal(5, outcome.TakenMax);
        Assert.All(outcome.Branches, branch => Assert.Equal(1, branch.AutoPlayed));
    }

    [Fact]
    public void EachHowlCopyPlays()
    {
        var state = new SimState { Enemies = { Enemy(id: 1, hp: 20, hits: 5) } };
        state.You.ExhaustAutoPlays.Add(AllEnemies("HowlFromBeyond", damage: 10, ids: 1));
        state.You.ExhaustAutoPlays.Add(AllEnemies("HowlFromBeyond", damage: 10, ids: 1));
        Assert.True(SimEngine.Run(state).DeadInAll(enemyId: 1));
    }

    [Fact]
    public void AnAutoPlayedAttackCountsAgainstRippleBasin()
    {
        // Ripple Basin (BeforeSideTurnEnd) checks for an attack played this turn: the auto-play phase comes first.
        var state = new SimState { Enemies = { Enemy(id: 1, hp: 50, hits: 10) } };
        state.WithRelic(Who.You, RelicId.RippleBasin, "Block", value: 4);
        Assert.Equal(6, SimEngine.Run(state).TakenMax);
        state.You.ExhaustAutoPlays.Add(AllEnemies("HowlFromBeyond", damage: 3, ids: 1));
        Assert.Equal(10, SimEngine.Run(state).TakenMax);
    }

    [Fact]
    public void IAmInvincibleOnTopGainsItsBlockBeforeOrichalcumLooks()
    {
        // Its block comes in the auto-play phase, so Orichalcum (no block at BeforeSideTurnEnd) doesn't arm.
        var state = new SimState { Enemies = { Enemy(id: 1, hp: 50, hits: 10) } };
        state.WithRelic(Who.You, RelicId.Orichalcum, "Block", value: 6);
        Assert.Equal(4, SimEngine.Run(state).TakenMax);
        state.You.InvincibleBlock = 8;
        state.You.InvincibleDrawPile = 2;
        Assert.Equal(2, SimEngine.Run(state).TakenMax);
    }

    [Fact]
    public void DrawingTheWholePileLeavesTheTopUnknown()
    {
        var state = new SimState { Enemies = { Enemy(id: 1, hp: 50, hits: 10), Enemy(id: 2, hp: 50) } };
        state.You.InvincibleBlock = 8;
        state.You.InvincibleDrawPile = 2;
        var draws = new SimPlay { Name = "Acrobatics", Draws = 1, HandDelta = 0 };
        Assert.Equal(2, SimEngine.Run(state, draws).TakenMax); // one left: still on top
        draws.Draws = 2;
        Assert.Equal(10, SimEngine.Run(state, draws).TakenMax); // the discard pile shuffles in: unknown
    }

    [Fact]
    public void StampedeAimsAtARandomEnemy()
    {
        // One Attack in hand, two enemies at 5: it kills one of them, at random.
        var state = new SimState { Enemies = { Enemy(id: 1, hp: 5, hits: 4), Enemy(id: 2, hp: 5, hits: 4) } };
        state.You.Stampede = 1;
        state.You.AutoHand = new() { StrikeCard(id: 11, damage: 6, cost: 1, targets: [1, 2]) };
        var outcome = SimEngine.Run(state);
        Assert.True(outcome.DeadInAny(enemyId: 1) && outcome.DeadInAny(enemyId: 2));
        Assert.False(outcome.DeadInAll(enemyId: 1) || outcome.DeadInAll(enemyId: 2));
        Assert.Equal(4, outcome.TakenMax);
        Assert.Equal(0.5, outcome.DeadChance(enemyId: 1), 3);
    }

    [Fact]
    public void ATeammatesStampedePlaysAnAttackFromTheirHand()
    {
        // Decision #25: the checks use a teammate's hand. Their Stampede plays their Strike (built as theirs: SimAdapter.AutoPlays
        // with them as the owner) at the enemy; what's shown leaves their hand out (WithoutTeammateHands).
        var state = new SimState { Enemies = { Enemy(id: 1, hp: 5, hits: 4) } };
        var strike = Strike(target: 1, damage: 6, hand: 21);
        strike.Card = new SimCardPlay(new SimCard("Strike", SimCardType.Attack), Who.Teammate(teammateIndex: 0), 1, PlayIndex: 0, PlayCount: 1, AutoPlay: true);
        state.Teammates.Add(new SimPlayer { Hp = 30, Stampede = 1, AutoHand = new() { new SimHandCard { Id = 21, Name = "Strike", IsAttack = true, Cost = 1, Plays = { strike } } } });
        Assert.True(SimEngine.Run(state).DeadInAll(enemyId: 1));
        Assert.False(SimEngine.Run(state.WithoutTeammateHands()).DeadInAny(enemyId: 1));
    }

    [Fact]
    public void StampedePicksOnlyAttacks()
    {
        var state = new SimState { Enemies = { Enemy(id: 1, hp: 5, hits: 4) } };
        state.You.Stampede = 1;
        state.You.AutoHand = new() { Defend(id: 12, block: 5, cost: 1) };
        var outcome = SimEngine.Run(state);
        Assert.Equal(4, outcome.TakenMax); // the Defend isn't played
        Assert.All(outcome.Branches, branch => Assert.Equal(0, branch.AutoPlayed));
    }

    [Fact]
    public void StampedeTwiceTakesTwoDifferentCards()
    {
        // Two Strikes: the second pick is the card left, so both always play.
        var state = new SimState { Enemies = { Enemy(id: 1, hp: 12, hits: 4) } };
        state.You.Stampede = 2;
        state.You.AutoHand = new() { StrikeCard(id: 11, damage: 6, cost: 1, targets: 1), StrikeCard(id: 13, damage: 6, cost: 1, targets: 1) };
        Assert.True(SimEngine.Run(state).DeadInAll(enemyId: 1));
    }

    [Fact]
    public void TheCardYouPlayIsNoLongerThereToPick()
    {
        // Playing the only Attack yourself: Stampede has nothing left, so it isn't played twice.
        var state = new SimState { Enemies = { Enemy(id: 1, hp: 12, hits: 4) } };
        state.You.Stampede = 1;
        state.You.AutoHand = new() { StrikeCard(id: 11, damage: 6, cost: 1, targets: 1) };
        Assert.True(SimEngine.Run(state).DeadInAny(enemyId: 1) == false);
        var outcome = SimEngine.Run(state, Strike(target: 1, damage: 6, hand: 11));
        Assert.False(outcome.DeadInAny(enemyId: 1));
        Assert.All(outcome.Branches, branch => Assert.Equal(6, branch.Enemy(id: 1)!.HpAtTurnEnd));
    }

    [Fact]
    public void APlayThatChangesYourHandEndsWhatsPredicted()
    {
        var state = new SimState { Enemies = { Enemy(id: 1, hp: 6, hits: 4) } };
        state.You.Stampede = 1;
        state.You.AutoHand = new() { StrikeCard(id: 11, damage: 6, cost: 1, targets: 1) };
        var outcome = SimEngine.Run(state, new SimPlay { Name = "Acrobatics", Draws = 2, HandDelta = 1 });
        Assert.All(outcome.Branches, branch => Assert.True(branch.AutoPlaysUnknown));
        Assert.False(outcome.DeadInAny(enemyId: 1));
    }

    [Fact]
    public void ACardThatCantBeSimulatedEndsWhatsPredicted()
    {
        var state = new SimState { Enemies = { Enemy(id: 1, hp: 6, hits: 4) } };
        state.You.Stampede = 2;
        state.You.AutoHand = new() { new SimHandCard { Id = 21, Name = "Pommel Strike", IsAttack = true, Cost = 1 } };
        var outcome = SimEngine.Run(state);
        Assert.All(outcome.Branches, branch => Assert.True(branch.AutoPlaysUnknown));
        Assert.All(outcome.Branches, branch => Assert.Equal(1, branch.AutoPlayed));
    }

    [Fact]
    public void SimpletonPlaysYourCheapestCard()
    {
        // The 0-cost Strike, not the 1-cost Defend.
        var state = new SimState { Enemies = { Enemy(id: 1, hp: 6, hits: 4) } };
        state.You.Simpleton = 1;
        state.You.AutoHand = new() { Defend(id: 12, block: 5, cost: 1), StrikeCard(id: 11, damage: 6, cost: 0, targets: 1) };
        var outcome = SimEngine.Run(state);
        Assert.True(outcome.DeadInAll(enemyId: 1));
        Assert.Equal(0, outcome.TakenMax);
    }

    [Fact]
    public void APlayThatGivesSimpletonPlaysYourCheapestCardAsTheTurnEnds()
    {
        // A dev review report: playing Simpleton (PowerCmd.Apply<SimpletonPower> on you) showed the turn's end without its auto-play. The power the
        // play gives is the power SimpletonPower.AfterAutoPostPlayPhaseEntered reads as this turn ends: the 0-cost Strike kills the enemy.
        var state = new SimState { Enemies = { Enemy(id: 1, hp: 6, hits: 4) } };
        state.You.AutoHand = new() { Defend(id: 12, block: 5, cost: 1), StrikeCard(id: 11, damage: 6, cost: 0, targets: 1) };
        var simpleton = ScriptBuilder.Play(ScriptBuilder.Card("Simpleton", SimCardType.Power), target: Who.None, plays: 1,
            new ApplyPowerAction(PowerId.Simpleton, Amount: 1) { To = ApplyPowerAction.Where.You });
        Assert.False(SimEngine.Run(state).DeadInAny(enemyId: 1));
        var outcome = SimEngine.Run(state, simpleton);
        Assert.True(outcome.DeadInAll(enemyId: 1));
        Assert.Equal(0, outcome.TakenMax);
    }

    [Fact]
    public void AnAutoPlayDueFromAHandNotReadIsUnknown()
    {
        // Simpleton on you with no hand read for it (a card made during the turn gave it): unknown, never no auto-play.
        var state = new SimState { Enemies = { Enemy(id: 1, hp: 6, hits: 4) } };
        state.You.Simpleton = 1;
        Assert.All(SimEngine.Run(state).Branches, branch => Assert.NotNull(branch.Unmodelled));
    }

    [Fact]
    public void SimpletonPicksAtRandomAmongTies()
    {
        // Strike or Defend, both 1: a kill in one branch, 5 block in the other (4 taken at worst, 0 at best).
        var state = new SimState { Enemies = { Enemy(id: 1, hp: 6, hits: 4) } };
        state.You.Simpleton = 1;
        state.You.AutoHand = new() { Defend(id: 12, block: 5, cost: 1), StrikeCard(id: 11, damage: 6, cost: 1, targets: 1) };
        var outcome = SimEngine.Run(state);
        Assert.True(outcome.DeadInAny(enemyId: 1));
        Assert.False(outcome.DeadInAll(enemyId: 1));
        Assert.Equal(0, outcome.TakenMax);
    }

    [Fact]
    public void NoTargetLeftMeansItIsntPlayed()
    {
        // Your card killed the only enemy the Strike could aim at: it goes unplayed.
        var state = new SimState { Enemies = { Enemy(id: 1, hp: 5), Enemy(id: 2, hp: 30, hits: 4) } };
        state.You.Stampede = 1;
        state.You.AutoHand = new() { StrikeCard(id: 11, damage: 6, cost: 1, targets: 1) };
        var outcome = SimEngine.Run(state, Strike(target: 1, damage: 5));
        Assert.All(outcome.Branches, branch => Assert.Equal(30, branch.Enemy(id: 2)!.HpAtTurnEnd));
    }

    /// <summary>Teammate 0's Howl from Beyond: their card (the play's owner), with its damage as final numbers.</summary>
    private static SimPlay TeammatesHowl(int damage, params int[] ids)
    {
        var play = AllEnemies("HowlFromBeyond", damage, ids);
        play.HandDelta = 0; // from their exhaust pile
        play.AutoPlay = true;
        play.Card = new SimCardPlay(new SimCard("HowlFromBeyond", SimCardType.Attack), Who.Teammate(teammateIndex: 0), Target: Who.None, PlayIndex: 0, PlayCount: 1, AutoPlay: true);
        return play;
    }

    [Fact]
    public void ATeammatesHowlFromBeyondKillsBeforeTheEnemyActs()
    {
        // Hook.AfterAutoPostPlayPhaseEntered runs for every player ending the turn: their Howl from Beyond in their exhaust
        // pile (you can see it) plays too. It isn't your auto-play (the outcome audit counts yours).
        var state = new SimState { Enemies = { Enemy(id: 1, hp: 10, hits: 5) } };
        var mate = new SimPlayer { Hp = 50 };
        mate.ExhaustAutoPlays.Add(TeammatesHowl(damage: 10, ids: 1));
        state.Teammates.Add(mate);
        var outcome = SimEngine.Run(state);
        Assert.True(outcome.DeadInAll(enemyId: 1));
        Assert.Equal(0, outcome.TakenMax);
        Assert.All(outcome.Branches, branch => Assert.Equal(0, branch.AutoPlayed));
    }

    [Fact]
    public void ATeammatesAutoPlayedAttackIsTheirsNotYours()
    {
        // Their Howl is their Attack this turn, not yours: your Ripple Basin still gives its block, and your hand keeps its size.
        var state = new SimState { Enemies = { Enemy(id: 1, hp: 50, hits: 10) } };
        state.WithRelic(Who.You, RelicId.RippleBasin, "Block", value: 4);
        state.You.HandCount = 3;
        var mate = new SimPlayer { Hp = 50 };
        mate.ExhaustAutoPlays.Add(TeammatesHowl(damage: 3, ids: 1));
        state.Teammates.Add(mate);
        var outcome = SimEngine.Run(state);
        Assert.Equal(6, outcome.TakenMax);
        Assert.All(outcome.Branches, branch => Assert.Equal(3, branch.You.HandCount));
    }

    [Fact]
    public void ATeammatesIAmInvincibleBlocksForThemNotYou()
    {
        var state = new SimState { Enemies = { Enemy(id: 1, hp: 50, hits: 10) } };
        var mate = new SimPlayer { Hp = 50, InvincibleBlock = 8, InvincibleDrawPile = 1 };
        state.Teammates.Add(mate);
        var outcome = SimEngine.Run(state);
        Assert.Equal(10, outcome.TakenMax);
        Assert.Equal(2, outcome.TeammateTakenMax(teammateIndex: 0));
    }

    [Fact]
    public void YourHowlFromBeyondDoesntLeaveYourHand()
    {
        // It plays from your exhaust pile: Cloak Clasp (block per card in hand, BeforeSideTurnEnd, after the auto-plays) counts
        // the hand as it was. The adapter builds it with HandDelta 0 (SimAdapter.Play: the card isn't in your hand).
        var state = new SimState { Enemies = { Enemy(id: 1, hp: 50, hits: 10) } };
        state.You.HandCount = 3;
        state.WithRelic(Who.You, RelicId.CloakClasp, "Block", value: 1);
        var howl = AllEnemies("HowlFromBeyond", damage: 3, ids: 1);
        howl.HandDelta = 0;
        howl.AutoPlay = true;
        state.You.ExhaustAutoPlays.Add(howl);
        Assert.Equal(7, SimEngine.Run(state).TakenMax);
    }

    /// <summary>
    /// A hand card's play as the adapter builds it (SimAdapter.AutoPlay): a card play and its attack, aimed at the play's
    /// TargetId (decision #84: the one place a play's target is kept).
    /// </summary>
    private static SimPlay AdapterStrike(int target, int damage, int hand, int draws = 0) => new()
    {
        Name = "Strike", IsAttack = true, AutoPlay = true, HandCard = hand, HandDelta = -1 + draws, Draws = draws, TargetId = target,
        Card = new SimCardPlay(new SimCard("Strike", SimCardType.Attack), Who.You, target, PlayIndex: 0, PlayCount: 1, AutoPlay: true),
        Body = { new AttackAction(Hits: 1, SimPlay.Target.Single, new DamageSpec(Base: 0m, SimProps.Move) { BaseOn = new Dictionary<int, decimal> { [target] = damage }, Fixed = true }) },
    };

    [Fact]
    public void APlaysTargetIsItsTargetIdEvenWhenItsCardPlaySaysOtherwise()
    {
        // One place for the target (decision #84): the card play takes the play's TargetId when the script is built.
        var state = new SimState { Enemies = { Enemy(id: 1, hp: 5), Enemy(id: 2, hp: 5) } };
        var play = AdapterStrike(target: 2, damage: 6, hand: 0);
        play.Card = play.Card! with { Target = 1 };
        var outcome = SimEngine.Run(state, play);
        Assert.True(outcome.DeadInAll(enemyId: 2));
        Assert.False(outcome.DeadInAny(enemyId: 1));
    }

    [Fact]
    public void StampedeAimsTheAdaptersPlaysAtEachEnemy()
    {
        // In game (two Exoskeletons at 10 and 6, Strike+ and Perfected Strike in hand, Stampede 1): the plays' target is on
        // the card play, so each one is a possible target. Either card kills whichever it hits: one dies, 50% each.
        var state = new SimState { Enemies = { Enemy(id: 1, hp: 10, hits: [3, 3, 3, 3]), Enemy(id: 2, hp: 6) } };
        state.You.Stampede = 1;
        state.You.AutoHand = new()
        {
            new SimHandCard { Id = 11, Name = "Strike", IsAttack = true, Cost = 1, Plays = { AdapterStrike(target: 1, damage: 13, hand: 11), AdapterStrike(target: 2, damage: 13, hand: 11) } },
            new SimHandCard { Id = 13, Name = "PerfectedStrike", IsAttack = true, Cost = 2, Plays = { AdapterStrike(target: 1, damage: 34, hand: 13), AdapterStrike(target: 2, damage: 34, hand: 13) } },
        };
        var outcome = SimEngine.Run(state);
        Assert.Equal(0.5, outcome.DeadChance(enemyId: 1), 3);
        Assert.Equal(0.5, outcome.DeadChance(enemyId: 2), 3);
        Assert.All(outcome.Branches, branch => Assert.Equal(1, branch.AutoPlayed));
    }

    [Fact]
    public void AStampedePickThatDrawsStillHits()
    {
        // In game (one Exoskeleton at 7, Strike+ and Pommel Strike in hand, Stampede 1): Pommel Strike draws a card, but its hit
        // lands first, so the enemy dies either way. Only later picks, from the changed hand, aren't predicted.
        var state = new SimState { Enemies = { Enemy(id: 1, hp: 7, hits: 11) } };
        state.You.Stampede = 2;
        state.You.AutoHand = new()
        {
            new SimHandCard { Id = 11, Name = "Strike", IsAttack = true, Cost = 1, Plays = { AdapterStrike(target: 1, damage: 9, hand: 11) } },
            new SimHandCard { Id = 15, Name = "PommelStrike", IsAttack = true, Cost = 1, Plays = { AdapterStrike(target: 1, damage: 9, hand: 15, draws: 1) } },
        };
        var outcome = SimEngine.Run(state);
        Assert.True(outcome.DeadInAll(enemyId: 1));
        Assert.Contains(outcome.Branches, branch => branch.AutoPlaysUnknown); // Pommel Strike first: the second pick is from a changed hand
    }

    [Fact]
    public void EachBranchRecordsItsPicks()
    {
        // The forecast check keeps the branches whose picks are the game's (decision #86): one per card and target.
        var state = new SimState { Enemies = { Enemy(id: 1, hp: 50, hits: 4), Enemy(id: 2, hp: 50, hits: 4) } };
        state.You.Stampede = 1;
        var strike = new SimHandCard { Id = 11, Name = "Strike", IsAttack = true, Cost = 1, Plays = { AdapterStrike(target: 1, damage: 6, hand: 11), AdapterStrike(target: 2, damage: 6, hand: 11) } };
        var pommelStrike = new SimHandCard { Id = 15, Name = "PommelStrike", IsAttack = true, Cost = 1, Plays = { AdapterStrike(target: 1, damage: 9, hand: 15, draws: 1), AdapterStrike(target: 2, damage: 9, hand: 15, draws: 1) } };
        state.You.AutoHand = new()
        {
            strike,
            pommelStrike,
        };
        var outcome = SimEngine.Run(state);
        var picks = outcome.Branches.Select(branch => Assert.Single(branch.AutoPicks)).ToHashSet();
        var expected = new[] { new SimPick(strike.Key, Target: 1), new SimPick(strike.Key, Target: 2), new SimPick(pommelStrike.Key, Target: 1), new SimPick(pommelStrike.Key, Target: 2) };
        Assert.True(picks.SetEquals(expected), string.Join(", ", picks));
        // Only Pommel Strike's branches changed the hand: a turn where Strike was picked can still be checked.
        Assert.All(outcome.Branches, branch => Assert.Equal(branch.AutoPicks[0].Card == pommelStrike.Key, branch.AutoPlaysUnknown));
    }

    [Fact]
    public void IdenticalCopiesPickedAreOneBranch()
    {
        // Two copies of one Strike: Stampede picking either plays the same in every way the simulator follows, so it's one branch
        // (decision #140), and the check matches the game's pick by what the card is (SimHandCard.Key).
        var state = new SimState { Enemies = { Enemy(id: 1, hp: 50, hits: 4) } };
        state.You.Stampede = 1;
        var strike = new SimHandCard { Id = 11, Name = "Strike", IsAttack = true, Cost = 1, Plays = { AdapterStrike(target: 1, damage: 6, hand: 11) } };
        state.You.AutoHand = new() { strike, new SimHandCard { Id = 12, Name = "Strike", IsAttack = true, Cost = 1, Plays = { AdapterStrike(target: 1, damage: 6, hand: 12) } } };
        var branch = Assert.Single(SimEngine.Run(state).Branches);
        Assert.Equal(1.0, branch.Weight, precision: 9);
        Assert.Equal(new SimPick(strike.Key, Target: 1), Assert.Single(branch.AutoPicks));
    }

    [Fact]
    public void CopiesThatDifferInAnythingStayApart()
    {
        // The same hit either way, but one copy costs 0: nothing in this turn's outcome shows it, but it's part of the card, so the
        // picks stay apart and the check knows which one the game played.
        var state = new SimState { Enemies = { Enemy(id: 1, hp: 50, hits: 4) } };
        state.You.Stampede = 1;
        state.You.AutoHand = new()
        {
            new SimHandCard { Id = 11, Name = "Strike", IsAttack = true, Cost = 1, Plays = { AdapterStrike(target: 1, damage: 6, hand: 11) } },
            new SimHandCard { Id = 12, Name = "Strike", IsAttack = true, Cost = 0, Plays = { AdapterStrike(target: 1, damage: 6, hand: 12) } },
        };
        Assert.Equal(2, SimEngine.Run(state).Branches.Count);
    }

    [Fact]
    public void TwoPicksOfThreeCopiesAreOneBranch()
    {
        // Stampede twice from three copies: which two it took doesn't matter, the hand left holds one copy either way.
        var state = new SimState { Enemies = { Enemy(id: 1, hp: 50, hits: 4) } };
        state.You.Stampede = 2;
        state.You.AutoHand = new(Enumerable.Range(11, 3).Select(id => new SimHandCard { Id = id, Name = "Strike", IsAttack = true, Cost = 1, Plays = { AdapterStrike(target: 1, damage: 6, hand: id) } }));
        var branch = Assert.Single(SimEngine.Run(state).Branches);
        Assert.Equal(2, branch.AutoPicks.Count);
        Assert.Equal(38, branch.Enemies[0].Hp);
    }
}
