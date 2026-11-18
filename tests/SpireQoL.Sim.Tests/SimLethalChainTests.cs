using SpireQoL.Sim;
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

    private static void Agrees(SimState s, SimPlay play)
    {
        var card = SimEngine.Run(s, play);
        SimEngine.ApplyPlayTo(s, play, out var boards, out var truncated);
        Assert.False(truncated);
        foreach (var e in s.Enemies)
        {
            var c = card.DeadChanceRange(e.Id);
            foreach (var board in boards)
            {
                var d = SimEngine.Run(board).DeadChanceRange(e.Id);
                if (c.Certain) Assert.True(d.Certain, $"{e.Name}: certain after the card, {d.Text()} from its board");
                if (!c.Possible) Assert.False(d.Possible, $"{e.Name}: never after the card, {d.Text()} from its board");
            }
        }
    }

    [Fact]
    public void AStrikeThatLeavesOneForTheLightningOrb()
    {
        // 9 HP: the Strike's 6 leaves 3, and the end-of-turn Lightning orb (3) finishes it.
        var s = new SimState { Enemies = { E(1, 9, 10) } };
        s.P.OrbCapacity = 3;
        s.P.Orbs.Add(Lightning());
        Agrees(s, Strike(1, 6));
        Assert.True(SimEngine.Run(s, Strike(1, 6)).DeadChanceRange(1).Certain);
    }

    [Fact]
    public void ARandomOrbWithTwoEnemies()
    {
        // Two enemies at 3 after the Strike: the orb picks one, so neither is certain; each board agrees with the card.
        var s = new SimState { Enemies = { E(1, 9, 10), E(2, 3, 10) } };
        s.P.OrbCapacity = 3;
        s.P.Orbs.Add(Lightning());
        Agrees(s, Strike(1, 6));
    }

    [Fact]
    public void PoisonThatKillsAtTheEnemyTurn()
    {
        var s = new SimState { Enemies = { E(1, 10, 10) } };
        s.Enemies[0].Poison = 5;
        Agrees(s, Strike(1, 6));
        Assert.True(SimEngine.Run(s, Strike(1, 6)).DeadChanceRange(1).Certain);
        Assert.False(SimEngine.Run(s).DeadChanceRange(1).Possible);
    }
}
