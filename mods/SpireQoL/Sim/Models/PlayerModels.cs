namespace SpireQoL.Sim.Models;

// What happens to HP a player loses, and what reacts to it (decompile v0.113.1).

/// <summary>Tungsten Rod (relic, amount = HpLossReduction): ModifyHpLostAfterOsty takes that much off HP its owner loses; it flashes when it does.</summary>
public sealed class TungstenRod : SimModel
{
    public override string Name => "TungstenRod";
    public override decimal ModifyHpLostAfterOsty(SimState s, int owner, DamageCtx d, decimal amount) =>
        d.Target != owner ? amount : Math.Max(0m, amount - SimEngine.RelicsOf(s, owner)[Name]);
}

/// <summary>
/// Beating Remnant (relic: amount = MaxHpLoss, Data = HP lost since its owner's turn started): ModifyHpLostAfterOsty caps HP lost
/// at what's left; AfterDamageReceived counts the HP actually lost (a hit Buffer stops counts nothing).
/// </summary>
public sealed class BeatingRemnant : SimModel
{
    public override string Name => "BeatingRemnant";
    public override decimal ModifyHpLostAfterOsty(SimState s, int owner, DamageCtx d, decimal amount)
    {
        if (d.Target != owner) return amount;
        var r = SimEngine.RelicsOf(s, owner);
        return Math.Min(amount, r[Name] - r.DataOf(Name));
    }
    public override void AfterDamageReceived(SimState s, int owner, DamageEvent e)
    {
        if (e.Target != owner || e.Unblocked == 0) return;
        var r = SimEngine.RelicsOf(s, owner);
        SimEngine.SetRelics(s, owner, r.With(Name, r[Name], r.DataOf(Name) + e.Unblocked));
    }
}

/// <summary>
/// BufferPower.ModifyHpLostAfterOstyLate: HP its owner would lose becomes 0 (not from Brightest Flame); when that changes the
/// amount, it loses a stack (AfterModifyingHpLostAfterOsty: Decrement) instead of flashing.
/// </summary>
public sealed class BufferPower : SimModel
{
    public override string Name => "BufferPower";
    public override decimal ModifyHpLostAfterOstyLate(SimState s, int owner, DamageCtx d, decimal amount) =>
        d.Target != owner || d.Card?.Name == "BrightestFlame" ? amount : 0m;
    public override void AfterModifyingHpLost(SimState s, int owner)
    {
        var p = SimEngine.PowersOf(s, owner);
        SimEngine.SetPowers(s, owner, p[Name] <= 1 ? p.Without(Name) : p.With(Name, p[Name] - 1));
        s.LogFired(Name);
    }
}

/// <summary>
/// InfernoPower.AfterDamageReceived: each time its owner loses HP while it's their side's turn, every hittable enemy takes its
/// amount (Unpowered: their block first).
/// </summary>
public sealed class InfernoPower : SimModel
{
    public override string Name => "InfernoPower";
    public override void AfterDamageReceived(SimState s, int owner, DamageEvent e)
    {
        if (e.Target != owner || e.Unblocked <= 0 || s.EnemyTurnNow) return;
        var amount = SimEngine.PowerOn(s, owner, Name);
        if (amount <= 0) return;
        s.LogFired(Name, amount);
        if (s.Log) s.EndOfTurnDamage.Add(("Inferno (all)", amount));
        SimEngine.DamageAllEnemies(s, amount, owner);
    }
}
