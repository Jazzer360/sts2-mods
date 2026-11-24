namespace SpireQoL.Sim.Models;

// What happens to HP a player loses, and what reacts to it (decompile v0.113.1).

/// <summary>
/// BufferPower.ModifyHpLostAfterOstyLate: HP its owner would lose becomes 0 (beta: not from Brightest Flame; main: that too, MainRule
/// .BufferStopsBrightestFlame); when that changes the amount, it loses a stack (AfterModifyingHpLostAfterOsty: Decrement) instead of flashing.
/// </summary>
public sealed class BufferPower : SimModel
{
    public override decimal ModifyHpLostAfterOstyLate(SimState state, int owner, DamageCtx damage, decimal amount) =>
        damage.Target != owner || damage.Card?.Name == CardId.BrightestFlame.Name && !state.MainRules.HasFlag(MainRule.BufferStopsBrightestFlame) ? amount : 0m;
    public override void AfterModifyingHpLost(SimState state, int owner, bool afterOsty)
    {
        var powers = SimEngine.PowersOf(state, owner);
        SimEngine.SetPowers(state, owner, powers[ThisPower] <= 1 ? powers.Without(ThisPower) : powers.With(ThisPower, powers[ThisPower] - 1));
        state.LogFired(Name, amount: 0, owner);
    }
}
