namespace SpireQoL.Sim.Models;

// What happens to HP a player loses, and what reacts to it (decompile v0.113.1).

/// <summary>Tungsten Rod (relic, amount = HpLossReduction): ModifyHpLostAfterOsty takes that much off HP its owner loses; it flashes when it does.</summary>
public sealed class TungstenRod : SimModel
{
    public override TabKind? Tab => TabKind.Trigger;
    public override decimal ModifyHpLostAfterOsty(SimState state, int owner, DamageCtx damage, decimal amount) =>
        damage.Target != owner ? amount : Math.Max(0m, amount - SimEngine.RelicsOf(state, owner)[ThisRelic]);
}

/// <summary>
/// Beating Remnant (relic: amount = MaxHpLoss, Data = HP lost since its owner's turn started): ModifyHpLostAfterOsty caps HP lost
/// at what's left; AfterDamageReceived counts the HP actually lost (a hit Buffer stops counts nothing).
/// </summary>
public sealed class BeatingRemnant : SimModel
{
    public override decimal ModifyHpLostAfterOsty(SimState state, int owner, DamageCtx damage, decimal amount)
    {
        if (damage.Target != owner) return amount;
        var relics = SimEngine.RelicsOf(state, owner);
        return Math.Min(amount, relics[ThisRelic] - relics.DataOf(ThisRelic));
    }
    public override void AfterDamageReceived(SimState state, int owner, DamageEvent hit)
    {
        if (hit.Target != owner || hit.Unblocked == 0) return;
        var relics = SimEngine.RelicsOf(state, owner);
        SimEngine.SetRelics(state, owner, relics.With(ThisRelic, relics[ThisRelic], relics.DataOf(ThisRelic) + hit.Unblocked));
    }
}

/// <summary>
/// BufferPower.ModifyHpLostAfterOstyLate: HP its owner would lose becomes 0 (not from Brightest Flame); when that changes the
/// amount, it loses a stack (AfterModifyingHpLostAfterOsty: Decrement) instead of flashing.
/// </summary>
public sealed class BufferPower : SimModel
{
    public override decimal ModifyHpLostAfterOstyLate(SimState state, int owner, DamageCtx damage, decimal amount) =>
        damage.Target != owner || damage.Card?.Name == CardId.BrightestFlame.Name ? amount : 0m;
    public override void AfterModifyingHpLost(SimState state, int owner)
    {
        var powers = SimEngine.PowersOf(state, owner);
        SimEngine.SetPowers(state, owner, powers[ThisPower] <= 1 ? powers.Without(ThisPower) : powers.With(ThisPower, powers[ThisPower] - 1));
        state.LogFired(Name, amount: 0, owner);
    }
}

/// <summary>
/// InfernoPower.AfterDamageReceived: each time its owner loses HP while it's their side's turn, every hittable enemy takes its
/// amount (Unpowered: their block first).
/// </summary>
public sealed class InfernoPower : SimModel
{
    public override TabKind? Tab => TabKind.Damage;
    public override void AfterDamageReceived(SimState state, int owner, DamageEvent hit)
    {
        if (hit.Target != owner || hit.Unblocked <= 0 || state.EnemyTurnNow) return;
        var amount = SimEngine.PowerOn(state, owner, ThisPower);
        if (amount <= 0) return;
        state.LogFired(Name, amount * state.Targetable.Count(), owner);
        if (state.Log) state.EndOfTurnDamage.Add(("Inferno (all)", amount));
        SimEngine.DamageAllEnemies(state, amount, owner);
    }
}
