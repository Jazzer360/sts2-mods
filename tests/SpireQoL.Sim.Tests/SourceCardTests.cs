using Xunit;
using static SpireQoL.Sim.Tests.ScriptBuilder;

namespace SpireQoL.Sim.Tests;

/// <summary>
/// What a damage hook asks of the card its damage comes from (decompile v0.113.1). ConquerorPower.ModifyDamageMultiplicative: `cardSource is
/// SovereignBlade`, a powered attack, on its owner: 2. The simulator modelled Conqueror (its tab on a Sovereign Blade played), so the
/// adapter captured no term for it, and nothing doubled the blade's damage.
/// </summary>
public class SourceCardTests
{
    private static SimState Conquered()
    {
        var state = new SimState { Enemies = { Enemy(id: 0, hp: 100) } };
        state.Enemies[0].Powers = state.Enemies[0].Powers.With(PowerId.Conqueror, amount: 1);
        return state;
    }

    [Fact]
    public void ConquerorDoublesTheSovereignBladesAttackOnItsOwner()
    {
        var blade = Assert.Single(After(Conquered(), Play(Card(CardId.SovereignBlade.Name, SimCardType.Attack), target: 0, plays: 1, Attack(baseDamage: 10))));
        Assert.Equal(80, blade.Enemy(id: 0)!.Hp);
        var strike = Assert.Single(After(Conquered(), Play(Card("Strike", SimCardType.Attack), target: 0, plays: 1, Attack(baseDamage: 10))));
        Assert.Equal(90, strike.Enemy(id: 0)!.Hp);
    }
}
