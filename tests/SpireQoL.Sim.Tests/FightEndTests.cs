using Xunit;
using static SpireQoL.Sim.Tests.ScriptBuilder;

namespace SpireQoL.Sim.Tests;

/// <summary>
/// The fight's end inside a turn (decision #301): once no primary enemy is alive and nothing holds the end back (CombatManager.IsCombatEnding),
/// the game's commands return at their start, its combat hooks reach no one, a card's later plays don't happen (OnPlayWrapper's loop breaks) and no
/// end of turn follows.
/// </summary>
public class FightEndTests
{
    private static SimState WithBurn(SimState state)
    {
        state.You.TurnEndCards.Add(new TurnEndCard(CardId.Burn.Name, "Burn", Amount: 2) { Source = new SimCard(CardId.Burn.Name, SimCardType.Status) });
        return state;
    }

    [Fact]
    public void NoEndOfTurnFollowsAPlayThatWinsTheFight()
    {
        // Plating's block and a Burn's damage come as your turn ends: after a play that kills the last enemy there is none.
        var won = WithBurn(new SimState { Enemies = { Enemy(id: 0, hp: 5) } });
        won.You.Plating = 4;
        var after = Assert.Single(SimEngine.Run(won, Play(Card("Strike"), target: 0, plays: 1, Attack(baseDamage: 6))).Branches);
        Assert.Equal(0, after.Taken);
        Assert.Equal(0, after.BlockAtEnemyTurn);
        // With an enemy left, the turn ends as before: 4 block, the Burn's 2 into it.
        var goesOn = WithBurn(new SimState { Enemies = { Enemy(id: 0, hp: 5), Enemy(id: 1, hp: 30) } });
        goesOn.You.Plating = 4;
        var later = Assert.Single(SimEngine.Run(goesOn, Play(Card("Strike"), target: 0, plays: 1, Attack(baseDamage: 6))).Branches);
        Assert.Equal(2, later.BlockAtEnemyTurn);
    }

    [Fact]
    public void AReplaysLaterPlaysDontHappenOnceTheFightIsWon()
    {
        // Played twice, each play 6 damage then 3 block: the first kills the last enemy, so its block (CreatureCmd.GainBlock returns once the
        // fight is ending) and the second play never come.
        var state = new SimState { Enemies = { Enemy(id: 0, hp: 5) } };
        var after = Assert.Single(SimEngine.Run(state, Play(Card("Strike"), target: 0, plays: 2,
            Attack(baseDamage: 6), new GainBlockAction(Times: 1, Base: 3, SimProps.Move) { Fixed = true })).Branches);
        Assert.Equal(0, after.BlockAtEnemyTurn);
        // While another enemy keeps the fight going, both plays block.
        var twoEnemies = new SimState { Enemies = { Enemy(id: 0, hp: 5), Enemy(id: 1, hp: 30) } };
        var both = Assert.Single(SimEngine.Run(twoEnemies, Play(Card("Strike"), target: 0, plays: 2,
            Attack(baseDamage: 6), new GainBlockAction(Times: 1, Base: 3, SimProps.Move) { Fixed = true })).Branches);
        Assert.Equal(6, both.BlockAtEnemyTurn);
    }

    /// <summary>
    /// Right Hand Hand comes back to your hand after a play of 2 energy or more (its AfterCardPlayedLate: CardPileCmd.Add to your hand). Hook.
    /// AfterCardPlayed still reaches it once the play has won the fight, but CardPileCmd.Add returns at its start then (IsOverOrEnding): the
    /// card stays in your discard pile (2.0.0's audit: Squeeze killing the last Wriggler).
    /// </summary>
    [Fact]
    public void ACardBroughtBackToYourHandStaysOutOnceThePlayWinsTheFight()
    {
        static int HandAfter(params SimEnemy[] enemies)
        {
            var state = new SimState();
            state.Enemies.AddRange(enemies);
            state.You.HandCount = 5;
            var play = new SimPlay { Name = "Squeeze", IsAttack = true, HandCard = 7, HandDelta = -1, BroughtBack = 1, TargetId = 0,
                Card = new SimCardPlay(new SimCard("Squeeze", SimCardType.Attack), Who.You, Target: 0, PlayIndex: 0, PlayCount: 1, AutoPlay: false) };
            play.Body.Add(Attack(baseDamage: 30));
            SimEngine.ApplyPlayTo(state, play, out var branches, out _);
            return Assert.Single(branches).You.HandCount;
        }
        Assert.Equal(4, HandAfter(Enemy(id: 0, hp: 19)));
        // With another enemy left, it comes back: the hand is as it was.
        Assert.Equal(5, HandAfter(Enemy(id: 0, hp: 19), Enemy(id: 1, hp: 30)));
    }

    [Fact]
    public void AKillAtTheTurnsEndStopsWhatsLeftOfIt()
    {
        // A Lightning orb's passive kills the last enemy at your turn's end: the Burn in your hand, which acts after the orbs, never hits.
        var state = WithBurn(new SimState { Enemies = { Enemy(id: 0, hp: 3) } });
        state.You.OrbCapacity = 1;
        state.You.Orbs.Add(new SimOrb { Kind = OrbKind.Lightning, Passive = 3, Evoke = 8 });
        var outcome = SimEngine.Run(state);
        Assert.True(outcome.DeadInAll(enemyId: 0));
        Assert.Equal(0, outcome.TakenMax);
    }

    [Fact]
    public void ABoardWithNoEnemyIsNoFightThatEnded()
    {
        // The simulator's tests of a player alone: no enemy at all is a fight that never began, not one that's over.
        var state = new SimState();
        state.You.Block = 5;
        var after = Assert.Single(SimEngine.Run(state, Play(Card("Entrench", SimCardType.Skill), target: Who.None, plays: 1,
            new GainBlockAction(Times: 1, Base: 0, SimProps.Unpowered | SimProps.Move) { BaseFrom = SimBlockBase.OwnBlock })).Branches);
        Assert.Equal(10, after.You.Block);
    }
}
