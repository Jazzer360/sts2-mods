using Xunit;

namespace SpireQoL.Sim.Tests;

/// <summary>
/// Your next turn's start in a projection (decision #306: SimEngine.YourTurnStart): CombatManager.StartTurn for the players' side and each player's
/// SetupPlayerTurn, every listener's own code run there in the game's order (Hook.BeforeSideTurnStart, the block cleared, AfterEnergyReset,
/// BeforeHandDraw, AfterPlayerTurnStart, Hook.AfterSideTurnStart), its side the players' and its participants the players and their pets. A listener
/// the projection doesn't follow there leaves what comes after unknown, with why.
/// </summary>
public class YourTurnStartTests
{
    private static SimEnemy Attacker(int id, int hp, int hit) =>
        new() { Id = id, Name = $"E{id}", Hp = hp, Hits = new() { hit }, Intent = new List<IntentHit> { new(Base: hit) }, IntendsToAttack = true };

    private static SimState Fight(params SimEnemy[] enemies)
    {
        var state = new SimState();
        state.You.Hp = 80;
        state.Enemies.AddRange(enemies);
        return state;
    }

    private static ProjectedMove Jab(int damage) => new("JAB")
    {
        Intent = new List<IntentHit> { new(Base: damage) },
        Actions = new SimAction[] { new MoveAttackAction(FirstHit: 0, Hits: 1) },
        OnlyAttacks = true,
    };

    private static IReadOnlyList<ProjectedChoice> JabTurn(int damage) => new[] { new ProjectedChoice(EnemyId: 0, new[] { (Jab(damage), 1.0) }) };

    [Fact]
    public void YourPowerActsOnTheEnemiesAsYourTurnStarts()
    {
        // NoxiousFumesPower.AfterSideTurnStart: participants.Contains(base.Owner), PowerCmd.Apply<PoisonPower> on every enemy. It doesn't act as the
        // enemies' turn starts (you aren't among them); as your next turn starts it gives the enemy 5 Poison, which ticks as their turn starts and
        // kills it (4 HP) before it moves: no hit to show that turn. Without it, the jab lands.
        var state = Fight(Attacker(id: 0, hp: 4, hit: 5));
        var without = SimEngine.Project(state, new[] { JabTurn(damage: 5) });
        Assert.Equal(new[] { 5 }, without.Of(turn: 0, enemyId: 0, move: "JAB")!.Lands);
        state.You.Powers = PowerSet.Empty.With(PowerId.NoxiousFumes, amount: 5);
        var with = SimEngine.Project(state, new[] { JabTurn(damage: 5) });
        var hits = with.Of(turn: 0, enemyId: 0, move: "JAB")!;
        Assert.Null(hits.Lands);
        Assert.Equal("it dies before it attacks", hits.Unknown);
        Assert.Equal(new[] { 5 }, hits.AtYourTurnStart); // its intent read as your turn started, before the poison ticked
    }

    [Fact]
    public void AnEnemysPoisonTicksOnlyAsItsOwnSideStarts()
    {
        // PoisonPower.AfterSideTurnStart: participants.Contains(base.Owner). An enemy's poison ticks as the enemies' turn starts, never as yours:
        // 10 HP and 4 Poison go 10 → 6 → 3 → 1 over three of its turns' starts, so it lives to make both projected moves (ticking at your turn
        // starts too, it would die before the second).
        var state = Fight(Attacker(id: 0, hp: 10, hit: 5));
        state.Enemies[0].Powers = PowerSet.Empty.With(PowerId.Poison, amount: 4);
        var projection = SimEngine.Project(state, new[] { JabTurn(damage: 5), JabTurn(damage: 5) });
        Assert.Equal(new[] { 5 }, projection.Of(turn: 0, enemyId: 0, move: "JAB")!.Lands);
        Assert.Equal(new[] { 5 }, projection.Of(turn: 1, enemyId: 0, move: "JAB")!.Lands);
    }

    [Fact]
    public void YourPoisonTicksAsYourTurnStartsAndCanEndTheFight()
    {
        // PoisonPower on you: participants.Contains(base.Owner) as your side's turn starts, CreatureCmd.Damage of you (Unblockable). 5 Poison on 3 HP
        // kills you as your next turn starts: the fight is over, so no later turn is projected.
        var state = Fight(Attacker(id: 0, hp: 50, hit: 1));
        state.You.Hp = 3;
        state.You.Powers = PowerSet.Empty.With(PowerId.Poison, amount: 5);
        var projection = SimEngine.Project(state, new[] { JabTurn(damage: 1), JabTurn(damage: 1) });
        Assert.Empty(projection.Turns);
        // With 30 HP you live through it, and the next turn is projected.
        state.You.Hp = 30;
        Assert.Equal(new[] { 1 }, SimEngine.Project(state, new[] { JabTurn(damage: 1) }).Of(turn: 0, enemyId: 0, move: "JAB")!.Lands);
    }

    [Fact]
    public void AListenerTheProjectionDoesntFollowThereLeavesTheTurnUnknown()
    {
        // Horn Cleat's AfterBlockCleared (its owner's block on the fight's second round) is a hook no read model runs: on the board as your next turn
        // starts, what follows isn't followed (ReadModels/turn-start-gaps.json), so that turn's numbers are unknown, with why.
        var state = Fight(Attacker(id: 0, hp: 50, hit: 6));
        state.You.Relics = state.You.Relics.With(RelicId.HornCleat, 0);
        var hits = SimEngine.Project(state, new[] { JabTurn(damage: 6) }).Of(turn: 0, enemyId: 0, move: "JAB")!;
        Assert.Null(hits.Lands);
        Assert.Null(hits.AtYourTurnStart);
        Assert.Contains(RelicId.HornCleat.Name, hits.Unknown, StringComparison.Ordinal);
        Assert.Contains("as your turn starts", hits.Unknown, StringComparison.Ordinal);
    }

    [Fact]
    public void AReadBodyThatWaitsOnItsOwnerTakingPartActsOnlyAsYourTurnStarts()
    {
        // Conductor's Baton's power (AfterSideTurnStart: participants.Contains(base.Owner), PowerCmd.Remove(this)) is read whole; as the enemies' turn
        // starts it can't act for you (SimModel.MayActOnTheEnemiesSide), so a card's simulation, which ends with the enemy turn, is as before.
        var model = SimModels.Get(PowerId.ConductorsBaton.Name)!;
        Assert.True(model.Overrides(nameof(SimModel.AfterSideTurnStart)));
        Assert.False(model.MayActOnTheEnemiesSide(nameof(SimModel.AfterSideTurnStart), Who.You));
    }
}
