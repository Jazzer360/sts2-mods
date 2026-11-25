using Xunit;

namespace SpireQoL.Sim.Tests;

/// <summary>
/// TheGambitPower.AfterDamageReceived, read from its code: `target == base.Owner &amp;&amp; props.IsPoweredAttack() &amp;&amp; result.UnblockedDamage &gt; 0` →
/// PowerCmd.Remove(this), Flash, CreatureCmd.Kill(base.Owner). The Gambit card applies it to its own player (TheGambit.OnPlay), so it's a
/// player's: an enemy's attack that gets HP through their block kills them. CreatureCmd.Kill takes all their HP (LoseHpInternal) and asks
/// Hook.ShouldDie, so a Fairy in a Bottle or Lizard Tail holds the death back as it holds back any other (force is false).
/// </summary>
public class TheGambitTests
{
    private static SimEnemy Enemy(int id, int hp, params int[] hits) => new() { Id = id, Name = $"E{id}", Hp = hp, Hits = hits.ToList() };

    private static SimState Board(params int[] hits)
    {
        var state = new SimState { Enemies = { Enemy(id: 0, hp: 50, hits: hits) } };
        state.You.Hp = 30;
        state.You.MaxHp = 80;
        state.You.Block = 5;
        state.You.Powers = PowerSet.Empty.With(PowerId.TheGambit, amount: 1);
        return state;
    }

    [Fact]
    public void AnAttackThroughYourBlockKillsYou()
    {
        // 8 against 5 block: 3 HP through, so The Gambit kills you (all 27 HP left go: LoseHpInternal of CurrentHp).
        var branch = Assert.Single(SimEngine.Run(Board(hits: 8)).Branches);
        Assert.True(branch.Died);
        Assert.Equal(0, branch.You.Hp);
        Assert.Equal(30, branch.You.Taken);
        Assert.False(branch.You.Powers.Has(PowerId.TheGambit));
    }

    [Fact]
    public void AnAttackYourBlockTakesWholeDoesNothing()
    {
        var branch = Assert.Single(SimEngine.Run(Board(hits: 5)).Branches);
        Assert.False(branch.Died);
        Assert.Equal(30, branch.You.Hp);
        Assert.True(branch.You.Powers.Has(PowerId.TheGambit));
    }

    [Fact]
    public void AFairyInABottleHoldsTheDeathBack()
    {
        // CreatureCmd.Kill(creature) passes force: false, so Hook.ShouldDie asks Fairy in a Bottle (30% of max HP: 24); the later hit lands on that.
        var state = Board(hits: [8, 4]);
        state.You.Fairies = 1;
        state.You.FairyHeal = 24;
        var branch = Assert.Single(SimEngine.Run(state).Branches);
        Assert.False(branch.Died);
        Assert.Equal(1, branch.You.FairiesUsed);
        Assert.Equal(20, branch.You.Hp);
        Assert.False(branch.You.Powers.Has(PowerId.TheGambit)); // it went first (PowerCmd.Remove before the Kill): the second hit is only damage
    }

    [Fact]
    public void ATeammatesGambitKillsThem()
    {
        // An enemy's attack hits every player: 4 through the teammate's 0 block kills them; you have no Gambit and lose 4.
        var state = new SimState { Enemies = { Enemy(id: 0, hp: 50, hits: 4) } };
        state.You.Hp = 30;
        state.Teammates.Add(new SimPlayer { Hp = 20, MaxHp = 70, Powers = PowerSet.Empty.With(PowerId.TheGambit, amount: 1) });
        var branch = Assert.Single(SimEngine.Run(state).Branches);
        Assert.True(branch.Teammates[0].Died);
        Assert.Equal(20, branch.Teammates[0].Taken);
        Assert.False(branch.You.Died);
        Assert.Equal(26, branch.You.Hp);
    }

    [Fact]
    public void AnUnpoweredHitDoesNothing()
    {
        // An HP cost (Unblockable, Unpowered: LoseHpAction's props) isn't a powered attack.
        var state = Board();
        state.You.Block = 0;
        var play = new SimPlay
        {
            Name = "Bloodletting", HandCard = 7, HandDelta = -1,
            Card = new SimCardPlay(new SimCard("Bloodletting", SimCardType.Skill), Who.You, Who.None, PlayIndex: 0, PlayCount: 1, AutoPlay: false),
            Body = { new LoseHpAction(3) },
        };
        var branch = Assert.Single(SimEngine.Run(state, play).Branches);
        Assert.False(branch.Died);
        Assert.Equal(27, branch.You.Hp);
    }
}
