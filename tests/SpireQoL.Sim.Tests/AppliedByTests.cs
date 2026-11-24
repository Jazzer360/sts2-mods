using Xunit;
using static SpireQoL.Sim.Tests.ScriptBuilder;

namespace SpireQoL.Sim.Tests;

/// <summary>
/// A power a read model applies as someone other than its owner (decision #204, decompile v0.113.1): PowerCmd.Apply's applier is its owner, the
/// applier the power keeps (base.Applier), or no one (null). OblivionPower.AfterCardPlayed applies DoomPower to its owner as base.Applier, so your
/// Shroud (AfterPowerAmountChanged: applier == its owner, a Doom) gains its block for it; TenderPower's Strength and Dexterity are its applier's.
/// </summary>
public class AppliedByTests
{
    [Fact]
    public void OblivionsDoomIsItsAppliersAndSetsOffTheirShroud()
    {
        var state = new SimState { Enemies = { Enemy(id: 0, hp: 50) } };
        state.Enemies[0].Powers = state.Enemies[0].Powers.With(PowerId.Oblivion, amount: 3, data: DataCreature.Of(Who.You));
        state.You.Powers = state.You.Powers.With(PowerId.Shroud, amount: 2);
        var after = Assert.Single(After(state, Play(Card("Defend", SimCardType.Skill), target: Who.None, plays: 1)));
        Assert.Equal(3, after.Enemy(id: 0)!.Powers[PowerId.Doom]);
        Assert.Equal(2, after.You.Block);
    }

    [Fact]
    public void OblivionActsOnlyOnItsAppliersCards()
    {
        // A teammate's Oblivion (its applier) on the enemy: your card sets it off no Doom.
        var state = new SimState { Enemies = { Enemy(id: 0, hp: 50) } };
        state.Teammates.Add(new SimPlayer { Hp = 50 });
        state.Enemies[0].Powers = state.Enemies[0].Powers.With(PowerId.Oblivion, amount: 3, data: DataCreature.Of(Who.Teammate(teammateIndex: 0)));
        var after = Assert.Single(After(state, Play(Card("Defend", SimCardType.Skill), target: Who.None, plays: 1)));
        Assert.Equal(0, after.Enemy(id: 0)!.Powers[PowerId.Doom]);
    }

    [Fact]
    public void TendersDebuffsHurtYouAndAreItsAppliers()
    {
        // TenderPower.AfterCardPlayed: Strength and Dexterity -1 on you for each card you play: a warning (its debuffs hurt you).
        var state = new SimState { Enemies = { Enemy(id: 0, hp: 50) } };
        state.You.Powers = state.You.Powers.With(PowerId.Tender, amount: 1, data: DataCreature.Of(0));
        var after = Assert.Single(After(state, Play(Card("Defend", SimCardType.Skill), target: Who.None, plays: 1)));
        Assert.Equal(-1, after.You.Powers[PowerId.Strength]);
        Assert.Equal(-1, after.You.Powers[PowerId.Dexterity]);
        Assert.True(SimModels.Get(PowerId.Tender.Name)!.HarmfulFor(Who.You));
    }
}
