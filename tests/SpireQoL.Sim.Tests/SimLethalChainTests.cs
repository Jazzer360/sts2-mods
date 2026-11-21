using Xunit;

namespace SpireQoL.Sim.Tests;

/// <summary>
/// Decision #95: the lethal tab is checked by running the end of turn from the board the play left. That holds only if a card's
/// simulation (its play, then the end of turn) agrees with the end of turn run from its play's result: an enemy dead in every
/// outcome of the card stays certain to die from each board the play can leave, and one alive in every outcome stays alive.
/// </summary>
public class SimLethalChainTests
{
    private static SimEnemy E(int id, int hp, params int[] hits) => new() { Id = id, Name = $"E{id}", Hp = hp, Hits = hits.ToList() };
    private static SimOrb Lightning() => new() { Kind = OrbKind.Lightning, Passive = 3, Evoke = 8 };

    private static SimPlay Strike(int target, int damage) => new()
    {
        Name = "Strike", IsAttack = true, HitTarget = SimPlay.Target.Single, TargetId = target, HitDamage = { [target] = damage }, Hits = 1, HandCard = 7, HandDelta = -1,
    };

    private static void Agrees(SimState state, SimPlay play)
    {
        var card = SimEngine.Run(state, play);
        SimEngine.ApplyPlayTo(state, play, out var boards, out var truncated);
        Assert.False(truncated);
        foreach (var enemy in state.Enemies)
        {
            var chance = card.DeadChanceRange(enemy.Id);
            foreach (var board in boards)
            {
                var simChance = SimEngine.Run(board).DeadChanceRange(enemy.Id);
                if (chance.Certain) Assert.True(simChance.Certain, $"{enemy.Name}: certain after the card, {simChance.Text()} from its board");
                if (!chance.Possible) Assert.False(simChance.Possible, $"{enemy.Name}: never after the card, {simChance.Text()} from its board");
            }
        }
    }

    [Fact]
    public void AStrikeThatLeavesOneForTheLightningOrb()
    {
        // 9 HP: the Strike's 6 leaves 3, and the end-of-turn Lightning orb (3) finishes it.
        var state = new SimState { Enemies = { E(1, 9, 10) } };
        state.You.OrbCapacity = 3;
        state.You.Orbs.Add(Lightning());
        Agrees(state, Strike(1, 6));
        Assert.True(SimEngine.Run(state, Strike(1, 6)).DeadChanceRange(1).Certain);
    }

    [Fact]
    public void ARandomOrbWithTwoEnemies()
    {
        // Two enemies at 3 after the Strike: the orb picks one, so neither is certain; each board agrees with the card.
        var state = new SimState { Enemies = { E(1, 9, 10), E(2, 3, 10) } };
        state.You.OrbCapacity = 3;
        state.You.Orbs.Add(Lightning());
        Agrees(state, Strike(1, 6));
    }

    [Fact]
    public void PoisonThatKillsAtTheEnemyTurn()
    {
        var state = new SimState { Enemies = { E(1, 10, 10) } };
        state.Enemies[0].Poison = 5;
        Agrees(state, Strike(1, 6));
        Assert.True(SimEngine.Run(state, Strike(1, 6)).DeadChanceRange(1).Certain);
        Assert.False(SimEngine.Run(state).DeadChanceRange(1).Possible);
    }
}
