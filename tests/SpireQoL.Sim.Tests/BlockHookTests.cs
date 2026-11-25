using Xunit;

namespace SpireQoL.Sim.Tests;

/// <summary>
/// The block hooks read from the game's code (decision #285): Hook.AfterBlockBroken (HandDrill's Vulnerable on a block its owner or its owner's pet
/// broke, `!target.IsPlayer`; BurrowedPower's stun of its owner, a Tunneler, and the power gone) and Hook.ShouldClearBlock (BarricadePower's and
/// BurrowedPower's `base.Owner != creature`: their owner's block kept as its side's turn starts, Creature.ClearBlock).
/// </summary>
public class BlockHookTests
{
    private static SimEnemy Enemy(int id, int hp, int block = 0, params int[] hits)
        => new() { Id = id, Name = $"E{id}", Hp = hp, Block = block, Hits = hits.ToList(), IntendsToAttack = hits.Length > 0 };

    private static SimPlay Strike(int target, int damage, int hits = 1, bool byOsty = false)
        => new() { Name = "Strike", IsAttack = true, ByOsty = byOsty, HitTarget = SimPlay.Target.Single, TargetId = target, HitDamage = { [target] = damage }, Hits = hits };

    [Fact]
    public void HandDrillPutsVulnerableOnTheEnemyWhoseBlockYouBreak()
    {
        // 5 block, a hit of 6 breaks it: Hand Drill's 2 Vulnerable lands inside the hit (SimModelEffectTests: the rest of the attack hits harder).
        var state = new SimState { Enemies = { Enemy(id: 0, hp: 30, block: 5) } };
        state.You.Relics = state.You.Relics.With(RelicId.HandDrill, 0);
        var after = SimEngine.Run(state, Strike(target: 0, damage: 6)).Branches.Single();
        Assert.Equal(2, after.Enemy(0)!.Powers[PowerId.Vulnerable]);
        // No block broken: no Vulnerable.
        var unbroken = SimEngine.Run(state, Strike(target: 0, damage: 4)).Branches.Single();
        Assert.False(unbroken.Enemy(0)!.Powers.Has(PowerId.Vulnerable));
    }

    [Fact]
    public void HandDrillCountsYourOstysBreakAsYoursAndNotATeammates()
    {
        // `breaker == base.Owner.Creature || breaker?.PetOwner == base.Owner`: your Osty breaking it sets yours off; a teammate's Hand Drill isn't set
        // off by your play's breaks.
        var state = new SimState { Enemies = { Enemy(id: 0, hp: 30, block: 5) } };
        state.You.WithOsty(hp: 10);
        state.You.Relics = state.You.Relics.With(RelicId.HandDrill, 0);
        Assert.True(SimEngine.Run(state, Strike(target: 0, damage: 6, byOsty: true)).Branches.Single().Enemy(0)!.Powers.Has(PowerId.Vulnerable));
        var mates = new SimState { Enemies = { Enemy(id: 0, hp: 30, block: 5) } };
        mates.Teammates.Add(new SimPlayer { Hp = 40, MaxHp = 60, Relics = RelicSet.Empty.With(RelicId.HandDrill, 0) });
        Assert.False(SimEngine.Run(mates, Strike(target: 0, damage: 6)).Branches.Single().Enemy(0)!.Powers.Has(PowerId.Vulnerable));
    }

    [Fact]
    public void BurrowedStunsOnlyATunneler()
    {
        // `target.Monster is Tunneler tunneler`: the one monster the game gives Burrowed. Another monster with it is left as it is.
        foreach (var (monster, stunned) in new[] { ("Tunneler", true), ("Axebot", false) })
        {
            var state = new SimState { Enemies = { Enemy(id: 0, hp: 40, block: 10, hits: 15) } };
            state.Enemies[0].Powers = state.Enemies[0].Powers.With(PowerId.Burrowed, 1);
            state.Enemies[0].Monster = monster;
            var outcome = SimEngine.Run(state, Strike(target: 0, damage: 12));
            Assert.Equal(stunned, outcome.StunnedInAll(enemyId: 0));
            Assert.Equal(!stunned, outcome.Branches.Single().Enemy(0)!.Powers.Has(PowerId.Burrowed));
        }
    }

    [Fact]
    public void BarricadeAndBurrowedKeepTheirOwnersBlockAsItsTurnStarts()
    {
        // ShouldClearBlock: `base.Owner != creature`. Thorns hits back into the block its turn starts with: 4 kept (Barricade, Burrowed) takes 4 of
        // the 6, so 18 left; with none kept (no power, or the power on another enemy) all 6 go through: 14.
        int HpAfterThorns(PowerId? power, bool onAnother)
        {
            var state = new SimState { Enemies = { Enemy(id: 0, hp: 20, block: 4, hits: 3), Enemy(id: 1, hp: 30) } };
            if (power is { } kept) state.Enemies[onAnother ? 1 : 0].Powers = PowerSet.Empty.With(kept, 1);
            state.Enemies[0].Monster = state.Enemies[1].Monster = "Tunneler";
            state.You.Thorns = 6;
            state.You.Hp = 50;
            return SimEngine.Run(state).Branches.Single().Enemy(0)!.Hp;
        }
        Assert.Equal(18, HpAfterThorns(PowerId.Barricade, onAnother: false));
        Assert.Equal(18, HpAfterThorns(PowerId.Burrowed, onAnother: false));
        Assert.Equal(14, HpAfterThorns(null, onAnother: false));
        Assert.Equal(14, HpAfterThorns(PowerId.Barricade, onAnother: true));
    }
}
