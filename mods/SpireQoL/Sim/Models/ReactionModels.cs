namespace SpireQoL.Sim.Models;

// What reacts to damage and to card plays (decompile v0.113.1): enemy reactions, your on-hit powers, HP-loss modifiers.

/// <summary>
/// ThornsPower.BeforeDamageReceived: a powered attack on its owner (with a dealer) hits the dealer back for its amount
/// (Unpowered | SkipHurtAnim, through the damage math), before the hit lands. You and your teammates take it on block; Osty
/// on his HP. (Your Thorns against an enemy's attack is the enemy turn's.)
/// </summary>
public sealed class ThornsPower : SimModel
{
    public override TabKind? Tab => TabKind.Damage;
    public override bool Harmful => true;
    public override void BeforeDamageReceived(SimState s, int owner, DamageCtx d, int amount)
    {
        if (d.Target != owner || owner < 0 || d.Dealer is not { } dealer || !d.Props.IsPoweredAttack()) return;
        var thorns = SimEngine.PowerOn(s, owner, ThisPower);
        if (thorns <= 0) return;
        var name = (s.Enemy(owner)?.Name ?? "Enemy") + " Thorns";
        var hit = SimEngine.CalcDamage(s, thorns, new DamageCtx(owner, dealer == Who.YourOsty ? Who.You : dealer, SimProps.Unpowered | SimProps.SkipHurtAnim, null, null));
        s.LogFired(Name, hit, owner);
        if (dealer == Who.YourOsty)
        {
            SimEngine.OstyLoses(s, Who.You, hit);
        }
        else if (Who.IsPlayer(dealer)) SimEngine.LandOnPlayer(s, name, hit, dealer);
    }
}

/// <summary>EnvenomPower / ConcoctPower.AfterDamageGiven: a powered hit of its owner's that gets HP through poisons the target.</summary>
public sealed class EnvenomPower : SimModel
{
    public override TabKind? Tab => TabKind.Trigger;
    public override void AfterDamageGiven(SimState s, int owner, DamageEvent e)
    {
        if (e.Dealer != owner || !e.Props.IsPoweredAttack() || e.Unblocked <= 0) return;
        s.LogFired(Name, 0, owner);
        SimEngine.ApplyPower(s, owner, e.Target, PowerId.Poison, SimEngine.PowerOn(s, owner, ThisPower), debuff: true);
    }
}

public sealed class ConcoctPower : SimModel
{
    public override TabKind? Tab => TabKind.Trigger;
    public override void AfterDamageGiven(SimState s, int owner, DamageEvent e)
    {
        if (e.Dealer != owner || !e.Props.IsPoweredAttack() || e.Unblocked <= 0) return;
        s.LogFired(Name, 0, owner);
        SimEngine.ApplyPower(s, owner, e.Target, PowerId.Poison, SimEngine.PowerOn(s, owner, ThisPower), debuff: true);
    }
}

/// <summary>Snecko Skull (relic, amount = its Poison var): poison its owner gives gets that much more (ModifyPowerAmountGivenAdditive).</summary>
public sealed class SneckoSkull : SimModel
{
    public override int ModifyPowerAmountGiven(SimState s, int owner, PowerId power, int amount, int? applier, int target, bool debuff)
    {
        if (power != PowerId.Poison || applier != owner) return amount;
        s.LogFired(Name, 0, owner);
        return amount + SimEngine.RelicsOf(s, owner)[ThisRelic];
    }
}

/// <summary>The Boot (relic, amount = DamageMinimum): its owner's (or Osty's) powered hits that lose another creature 1+ HP lose it at least that.</summary>
public sealed class TheBoot : SimModel
{
    public override decimal ModifyHpLostAfterOstyLate(SimState s, int owner, DamageCtx d, decimal amount)
    {
        if (!(d.Dealer == owner || (owner == Who.You && d.Dealer == Who.YourOsty)) || d.Target == owner) return amount;
        if (!d.Props.IsPoweredAttack() || amount < 1m) return amount;
        var min = SimEngine.RelicsOf(s, owner)[ThisRelic];
        return amount >= min ? amount : min;
    }
}

/// <summary>HardenedShellPower: at most what's left this turn (Data) of HP lost (BeforeOstyLate); HP it loses uses that up (AfterDamageReceived).</summary>
public sealed class HardenedShellPower : SimModel
{
    public override decimal ModifyHpLostBeforeOstyLate(SimState s, int owner, DamageCtx d, decimal amount) =>
        d.Target != owner || amount == 0m ? amount : Math.Min(amount, Math.Max(0, SimEngine.PowersOf(s, owner).DataOf(ThisPower)));
    public override void AfterDamageReceived(SimState s, int owner, DamageEvent e)
    {
        if (e.Target != owner || e.Unblocked == 0) return;
        var p = SimEngine.PowersOf(s, owner);
        SimEngine.SetPowers(s, owner, p.With(ThisPower, p[ThisPower], Math.Max(0, p.DataOf(ThisPower) - e.Unblocked)));
    }
}

/// <summary>SlipperyPower: at most 1 HP per damage to its owner (AfterOsty); each that loses HP flashes and takes a charge.</summary>
public sealed class SlipperyPower : SimModel
{
    public override OutcomeRule? OutcomeTab(SimState start, SimOutcome now, int owner) =>
        Who.IsEnemy(owner) && !now.SlipperyInAny(owner) ? new(true, o => o.SlipperyInAny(owner)) : null;

    public override decimal ModifyHpLostAfterOsty(SimState s, int owner, DamageCtx d, decimal amount) =>
        d.Target != owner || amount < 1m ? amount : 1m;
    public override void AfterModifyingHpLost(SimState s, int owner) { } // it flashes when the damage lands
    public override void AfterDamageReceived(SimState s, int owner, DamageEvent e)
    {
        if (e.Target != owner || e.Unblocked < 1) return;
        s.LogFired(Name, 0, owner);
        var p = SimEngine.PowersOf(s, owner);
        SimEngine.SetPowers(s, owner, p[ThisPower] <= 1 ? p.Without(ThisPower) : p.With(ThisPower, p[ThisPower] - 1));
        if (s.Enemy(owner) is { } en) en.SlipperyTriggered = true;
    }
}

/// <summary>BurrowedPower.AfterBlockBroken: breaking its owner's block stuns it, and the power goes (with its block-keeping).</summary>
public sealed class BurrowedPower : SimModel
{
    public override OutcomeRule? OutcomeTab(SimState start, SimOutcome now, int owner) => OutcomeRules.Stuns(now, owner);

    public override void AfterBlockBroken(SimState s, int owner, int target)
    {
        if (target != owner || s.Enemy(owner) is not { } e) return;
        SimEngine.Stun(e);
        e.Powers = e.Powers.Without(ThisPower);
        s.LogFired(Name, 0, owner);
    }
}

/// <summary>AsleepPower.AfterDamageReceived: HP through its owner's block (any source) wakes it: Plating goes, it's stunned, the power goes.</summary>
public sealed class AsleepPower : SimModel
{
    public override OutcomeRule? OutcomeTab(SimState start, SimOutcome now, int owner) =>
        Who.IsEnemy(owner) && now.UnblockedMax(owner) < 1 ? new(true, o => o.UnblockedMax(owner) >= 1) : null;

    public override void AfterDamageReceived(SimState s, int owner, DamageEvent e)
    {
        if (e.Target != owner || e.Unblocked == 0 || s.Enemy(owner) is not { } en) return;
        en.Powers = en.Powers.Without(PowerId.Plating).Without(ThisPower);
        SimEngine.Stun(en);
        s.LogFired(Name, 0, owner);
    }
}

/// <summary>SlumberPower.AfterDamageReceived: each damage that gets HP through takes a stack; the last stuns it.</summary>
public sealed class SlumberPower : SimModel
{
    public override OutcomeRule? OutcomeTab(SimState start, SimOutcome now, int owner) =>
        Who.IsEnemy(owner) && SimEngine.PowerOn(start, owner, ThisPower) is var stacks && now.UnblockedMax(owner) < stacks
            ? new(true, o => o.UnblockedMax(owner) >= stacks) : null;

    public override void AfterDamageReceived(SimState s, int owner, DamageEvent e)
    {
        if (e.Target != owner || e.Unblocked == 0 || s.Enemy(owner) is not { } en) return;
        var left = en.Powers[ThisPower] - 1;
        en.Powers = left <= 0 ? en.Powers.Without(ThisPower) : en.Powers.With(ThisPower, left);
        if (left <= 0)
        {
            SimEngine.Stun(en);
            s.LogFired(Name, 0, owner);
        }
    }
}

/// <summary>
/// PlowPower.AfterDamageReceived: HP through that leaves its owner at or below its amount: it flashes, loses its Strength,
/// is stunned, and the power goes.
/// </summary>
public sealed class PlowPower : SimModel
{
    public override OutcomeRule? OutcomeTab(SimState start, SimOutcome now, int owner) => OutcomeRules.Stuns(now, owner);

    public override void AfterDamageReceived(SimState s, int owner, DamageEvent e)
    {
        if (e.Target != owner || e.Unblocked <= 0 || s.Enemy(owner) is not { Alive: true } en || en.Hp > en.Powers[ThisPower]) return;
        s.LogFired(Name, 0, owner);
        en.Powers = en.Powers.Without(PowerId.Strength).Without(ThisPower);
        SimEngine.Stun(en);
    }
}

/// <summary>ShriekPower.AfterDamageReceived: HP through that leaves its owner at or below its amount stuns it; the power goes.</summary>
public sealed class ShriekPower : SimModel
{
    public override OutcomeRule? OutcomeTab(SimState start, SimOutcome now, int owner) => OutcomeRules.Stuns(now, owner);

    public override void AfterDamageReceived(SimState s, int owner, DamageEvent e)
    {
        if (e.Target != owner || e.Unblocked <= 0 || s.Enemy(owner) is not { Alive: true } en || en.Hp > en.Powers[ThisPower]) return;
        s.LogFired(Name, 0, owner);
        en.Powers = en.Powers.Without(ThisPower);
        SimEngine.Stun(en);
    }
}

/// <summary>TheGambitPower.AfterDamageReceived: a powered attack that gets HP through kills its owner.</summary>
public sealed class TheGambitPower : SimModel
{
    public override OutcomeRule? OutcomeTab(SimState start, SimOutcome now, int owner) =>
        Who.IsEnemy(owner) && !now.GambitInAll(owner) ? new(false, o => o.GambitInAll(owner)) : null;

    public override void AfterDamageReceived(SimState s, int owner, DamageEvent e)
    {
        if (e.Target != owner || !e.Props.IsPoweredAttack() || e.Unblocked <= 0 || s.Enemy(owner) is not { } en) return;
        en.Powers = en.Powers.Without(ThisPower);
        s.LogFired(Name, 0, owner);
        en.GambitTriggered = true;
        en.Hp = 0;
    }
}

/// <summary>
/// CurlUpPower: a powered hit from a card on its owner binds that card (Data 1: the play in progress); AfterCardPlayed of that
/// play gives it its amount of block (unpowered) and the power goes.
/// </summary>
public sealed class CurlUpPower : SimModel
{
    public override TabKind? Tab => TabKind.Trigger;
    public override bool Harmful => true;
    public override void AfterDamageReceived(SimState s, int owner, DamageEvent e)
    {
        if (e.Target != owner || !e.Props.IsPoweredAttack() || e.Card == null || s.CurrentPlay == null) return;
        var p = SimEngine.PowersOf(s, owner);
        if (p.Has(ThisPower)) SimEngine.SetPowers(s, owner, p.With(ThisPower, p[ThisPower], 1));
    }
    public override IEnumerable<SimState> AfterCardPlayed(SimState s, int owner, SimCardPlay play)
    {
        if (s.Enemy(owner) is not { Alive: true } en || en.Powers.DataOf(ThisPower) != 1) return One(s);
        en.Block += en.Powers[ThisPower];
        en.Powers = en.Powers.Without(ThisPower);
        en.ReactedWithBlock = true;
        s.LogFired(Name, 0, owner);
        return One(s);
    }
}

/// <summary>
/// SkittishPower: once per turn (Data bit 1), after an attack from a card (a Move) whose first hit on its owner got HP through
/// (bit 4; bit 2 marks the first hit seen this attack), it gains its amount of block (AfterAttack).
/// </summary>
public sealed class SkittishPower : SimModel
{
    public override TabKind? Tab => TabKind.Trigger;
    public override bool Harmful => true;
    public override IEnumerable<SimState> BeforeAttack(SimState s, int owner, AttackInfo attack)
    {
        var p = SimEngine.PowersOf(s, owner);
        if (p.Has(ThisPower)) SimEngine.SetPowers(s, owner, p.With(ThisPower, p[ThisPower], p.DataOf(ThisPower) & 1));
        return One(s);
    }
    public override void AfterDamageReceived(SimState s, int owner, DamageEvent e)
    {
        if (e.Target != owner || s.CurrentAttack == 0) return;
        var p = SimEngine.PowersOf(s, owner);
        var data = p.DataOf(ThisPower);
        if (!p.Has(ThisPower) || (data & 2) != 0) return;
        SimEngine.SetPowers(s, owner, p.With(ThisPower, p[ThisPower], data | 2 | (e.Unblocked != 0 ? 4 : 0)));
    }
    public override IEnumerable<SimState> AfterAttack(SimState s, int owner, AttackInfo attack)
    {
        if (s.Enemy(owner) is not { Alive: true } en) return One(s);
        var data = en.Powers.DataOf(ThisPower);
        if ((data & 1) != 0 || (data & 4) == 0 || !attack.Props.HasFlag(SimProps.Move) || attack.Card == null) return One(s);
        en.Block += en.Powers[ThisPower];
        en.Powers = en.Powers.With(ThisPower, en.Powers[ThisPower], 1);
        en.ReactedWithBlock = true;
        s.LogFired(Name, 0, owner);
        return One(s);
    }
}

/// <summary>SneakyPower.AfterCardPlayed: every Attack play by someone else gives its owner its amount of block.</summary>
public sealed class SneakyPower : SimModel
{
    public override TabKind? Tab => TabKind.Trigger;
    public override bool Harmful => true;
    public override IEnumerable<SimState> AfterCardPlayed(SimState s, int owner, SimCardPlay play)
    {
        if (play.Card.Type != SimCardType.Attack || play.Owner == owner || s.Enemy(owner) is not { Alive: true } en) return One(s);
        en.Block += en.Powers[ThisPower];
        en.ReactedWithBlock = true;
        s.LogFired(Name, 0, owner);
        return One(s);
    }
}
