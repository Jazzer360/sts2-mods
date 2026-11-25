using Xunit;

namespace SpireQoL.Sim.Tests;

/// <summary>
/// Plating read from its code (decision #289): BeforeSideTurnEndEarly's block for its owner as their side's turn ends (participants.Contains(base.Owner));
/// AfterApplied on an enemy sets its Decrement to the players' count (base.Owner.CombatState.RunState.Players.Count); AfterSideTurnStart takes that
/// many stacks off an enemy's as the enemies' turn starts, but on the fight's first round (combatState.RoundNumber != 1), and a stack off a player's
/// as their next turn starts, past the turns a simulation covers.
/// </summary>
public class PlatingTests
{
    private static SimState Board(int round, int teammates = 0)
    {
        var state = new SimState { TurnNumber = round, RoundNumber = round, Enemies = { new SimEnemy { Id = 0, Name = "E0", Hp = 40, Hits = new List<int> { 5 }, IntendsToAttack = true } } };
        state.You.Hp = 50;
        for (var i = 0; i < teammates; i++) state.Teammates.Add(new SimPlayer { Hp = 40, MaxHp = 60 });
        return state;
    }

    [Fact]
    public void ItsOwnersBlockAsTheirTurnEnds()
    {
        var state = Board(round: 2);
        state.You.Plating = 4;
        Assert.IsType<ReadModel>(SimModels.Get(PowerId.Plating.Name));
        var branch = Assert.Single(SimEngine.Run(state).Branches);
        Assert.Equal(4, branch.BlockAtEnemyTurn);
        // A player's stacks come off as their next turn starts, which no simulation reaches.
        Assert.Equal(4, branch.You.Plating);
    }

    [Fact]
    public void AnEnemysStacksComeDownAsTheEnemiesTurnStartsButOnTheFirstRound()
    {
        int PlatingLeft(int round)
        {
            var state = Board(round);
            state.Enemies[0].Powers = state.Enemies[0].Powers.With(PowerId.Plating, 5);
            return Assert.Single(SimEngine.Run(state).Branches).Enemy(0)!.Powers[PowerId.Plating];
        }
        Assert.Equal(5, PlatingLeft(round: 1));
        Assert.Equal(4, PlatingLeft(round: 2));
        // An enemy's Plating gives it no block as the players' turn ends: its participants are the players.
        var state = Board(round: 2);
        state.Enemies[0].Powers = state.Enemies[0].Powers.With(PowerId.Plating, 5);
        Assert.Equal(0, Assert.Single(SimEngine.Run(state).Branches).Enemy(0)!.Block);
    }

    [Fact]
    public void AnEnemysPlatingLandingInAFightOfTwoComesDownByTwo()
    {
        // AfterApplied: base.DynamicVars["Decrement"].BaseValue = base.Owner.CombatState.RunState.Players.Count, on an enemy. PowerCmd.Apply scales
        // a new one on an enemy for the players too (PowerRule.ScaledForPlayers: (players - 1) × 2 + 1 times its amount, read from its code).
        var state = Board(round: 2, teammates: 1);
        SimEngine.ApplyPower(state, applier: 0, target: 0, PowerId.Plating, amount: 2, debuff: false);
        var landed = state.Enemy(0)!.Powers[PowerId.Plating];
        Assert.Equal(6, landed);
        Assert.Equal(landed - 2, Assert.Single(SimEngine.Run(state).Branches).Enemy(0)!.Powers[PowerId.Plating]);
    }
}
