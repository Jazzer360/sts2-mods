namespace SpireQoL.Sim.Models;

// The damage and block sources that can change during your turn (docs/SIMULATOR.md "Coverage"), each from its
// decompiled body (v0.113.1). Amounts live on the owner's PowerSet; Data holds what a model keeps between hooks.

/// <summary>StrengthPower.ModifyDamageAdditive: its amount on the owner's powered attacks (negative allowed).</summary>
public sealed class StrengthPower : SimModel
{
    public override decimal ModifyDamageAdditive(SimState s, int owner, DamageCtx d) =>
        d.Dealer == owner && d.Props.IsPoweredAttack() ? SimEngine.PowerOn(s, owner, ThisPower) : 0m;
}

/// <summary>
/// DexterityPower.ModifyBlockAdditive: its amount on powered block: from a card, the card's owner must be its owner; without a
/// card, its owner must be the one gaining it.
/// </summary>
public sealed class DexterityPower : SimModel
{
    public override decimal ModifyBlockAdditive(SimState s, int owner, BlockCtx b)
    {
        if (b.Play != null ? b.Play.Owner != owner : b.Target != owner) return 0m;
        return b.Props.IsPoweredBlock() ? SimEngine.PowerOn(s, owner, ThisPower) : 0m;
    }
}

/// <summary>FrailPower.ModifyBlockMultiplicative: 0.75 on its owner's powered block.</summary>
public sealed class FrailPower : SimModel
{
    public override decimal ModifyBlockMultiplicative(SimState s, int owner, BlockCtx b) =>
        b.Target == owner && b.Props.IsPoweredBlock() ? 0.75m : 1m;
}

/// <summary>
/// VulnerablePower.ModifyDamageMultiplicative: 1.5 on powered attacks on its owner; the dealer's Paper Phrog adds 0.25, the
/// dealer's Cruelty its amount / 100; Debilitate on the owner doubles the bonus (num + (num - 1)).
/// </summary>
public sealed class VulnerablePower : SimModel
{
    public override decimal ModifyDamageMultiplicative(SimState s, int owner, DamageCtx d)
    {
        if (d.Target != owner || !d.Props.IsPoweredAttack()) return 1m;
        var num = 1.5m;
        if (d.Dealer is { } dealer)
        {
            var player = dealer == Who.YourOsty ? Who.You : dealer;
            if (Who.IsPlayer(player) && SimEngine.RelicsOf(s, player).Has(RelicId.PaperPhrog)) num += 0.25m;
            if (Who.IsPlayer(player) && SimEngine.PowerOn(s, player, PowerId.Cruelty) is var cruelty and not 0) num += cruelty / 100m;
        }
        if (SimEngine.PowersOf(s, owner).Has(PowerId.Debilitate)) num += num - 1m;
        return num;
    }
}

/// <summary>
/// WeakPower.ModifyDamageMultiplicative: 0.75 on its owner's powered attacks; Paper Krane on the target takes 0.15 more off;
/// Debilitate on the owner doubles the reduction (num - (1 - num)).
/// </summary>
public sealed class WeakPower : SimModel
{
    public override decimal ModifyDamageMultiplicative(SimState s, int owner, DamageCtx d)
    {
        if (d.Dealer != owner || !d.Props.IsPoweredAttack()) return 1m;
        var num = 0.75m;
        if (Who.IsPlayer(d.Target) && SimEngine.RelicsOf(s, d.Target).Has(RelicId.PaperKrane)) num -= 0.15m;
        if (SimEngine.PowersOf(s, owner).Has(PowerId.Debilitate)) num -= 1m - num;
        return num;
    }
}

/// <summary>
/// Powers bound to one attack (BeforeAttack binds the first attack that qualifies, AfterAttack spends them). Data is the bound
/// attack's id and the amount when it started: (id &lt;&lt; 16) | amount.
/// </summary>
public abstract class AttackBoundPower : SimModel
{
    /// <summary>
    /// Bound to an attack outside the simulation: one under way when the state was read (the game's commandToModify is set), so
    /// it adds nothing to any simulated attack, and no simulated attack spends it.
    /// </summary>
    public const int BoundOutside = 0x7FFF << 16;

    protected static int BoundAttack(int data) => data >> 16;
    protected static int AmountAtStart(int data) => data & 0xFFFF;

    /// <summary>The attack qualifies: its dealer, props and card (the game's BeforeAttack checks).</summary>
    protected abstract bool Qualifies(SimState s, int owner, AttackInfo a);

    public override IEnumerable<SimState> BeforeAttack(SimState s, int owner, AttackInfo attack)
    {
        var powers = SimEngine.PowersOf(s, owner);
        if (!powers.Has(ThisPower) || powers.DataOf(ThisPower) != 0 || !Qualifies(s, owner, attack)) return One(s);
        SimEngine.SetPowers(s, owner, powers.With(ThisPower, powers[ThisPower], (attack.Id << 16) | Math.Clamp(powers[ThisPower], 0, 0xFFFF)));
        return One(s);
    }

    /// <summary>It applies to this damage: not bound, or bound to the attack in progress.</summary>
    protected static bool AppliesNow(SimState s, int owner, PowerId name)
    {
        var bound = BoundAttack(SimEngine.PowersOf(s, owner).DataOf(name));
        return bound == 0 || bound == s.CurrentAttack;
    }
}

/// <summary>VigorPower: its amount on the owner's powered attack from a card; the attack that started with it spends that much (AfterAttack).</summary>
public sealed class VigorPower : AttackBoundPower
{
    public override TabKind? Tab => TabKind.Trigger;
    protected override bool Qualifies(SimState s, int owner, AttackInfo a) => a.Dealer == owner && a.Props.IsPoweredAttack();
    public override decimal ModifyDamageAdditive(SimState s, int owner, DamageCtx d) =>
        d.Dealer == owner && d.Props.IsPoweredAttack() && AppliesNow(s, owner, ThisPower) ? SimEngine.PowerOn(s, owner, ThisPower) : 0m;
    public override IEnumerable<SimState> AfterAttack(SimState s, int owner, AttackInfo attack)
    {
        var powers = SimEngine.PowersOf(s, owner);
        var data = powers.DataOf(ThisPower);
        if (!powers.Has(ThisPower) || BoundAttack(data) != attack.Id) return One(s);
        var left = powers[ThisPower] - AmountAtStart(data);
        SimEngine.SetPowers(s, owner, left == 0 ? powers.Without(ThisPower) : powers.With(ThisPower, left, 0));
        s.LogFired(Name, 0, owner);
        return One(s);
    }
}

/// <summary>
/// AimPower: like Vigor, on the owner's powered arrow attacks (FromArrow); Heavy Shot multiplies it by its AimMultiplier.
/// </summary>
public sealed class AimPower : AttackBoundPower
{
    protected override bool Qualifies(SimState s, int owner, AttackInfo a) =>
        a.Dealer == owner && a.Props.IsPoweredAttack() && a.Props.HasFlag(SimProps.FromArrow);
    public override decimal ModifyDamageAdditive(SimState s, int owner, DamageCtx d)
    {
        if (d.Dealer != owner || !d.Props.IsPoweredAttack() || !d.Props.HasFlag(SimProps.FromArrow) || !AppliesNow(s, owner, ThisPower)) return 0m;
        decimal num = SimEngine.PowerOn(s, owner, ThisPower);
        if (d.Card is { Name: "HeavyShot" } heavy && heavy.Vars.TryGetValue("AimMultiplier", out var mul)) num *= mul;
        return num;
    }
    public override IEnumerable<SimState> AfterAttack(SimState s, int owner, AttackInfo attack)
    {
        var powers = SimEngine.PowersOf(s, owner);
        var data = powers.DataOf(ThisPower);
        if (!powers.Has(ThisPower) || BoundAttack(data) != attack.Id) return One(s);
        var left = powers[ThisPower] - AmountAtStart(data);
        SimEngine.SetPowers(s, owner, left == 0 ? powers.Without(ThisPower) : powers.With(ThisPower, left, 0));
        s.LogFired(Name, 0, owner);
        return One(s);
    }
}

/// <summary>QuietusPower: multiplies the owner's powered arrow attack by its amount; the attack bound to it removes it.</summary>
public sealed class QuietusPower : AttackBoundPower
{
    protected override bool Qualifies(SimState s, int owner, AttackInfo a) =>
        a.Dealer == owner && a.Props.IsPoweredAttack() && a.Props.HasFlag(SimProps.FromArrow);
    public override decimal ModifyDamageMultiplicative(SimState s, int owner, DamageCtx d) =>
        d.Dealer == owner && d.Props.IsPoweredAttack() && d.Props.HasFlag(SimProps.FromArrow) && AppliesNow(s, owner, ThisPower)
            ? SimEngine.PowerOn(s, owner, ThisPower) : 1m;
    public override IEnumerable<SimState> AfterAttack(SimState s, int owner, AttackInfo attack)
    {
        var powers = SimEngine.PowersOf(s, owner);
        if (!powers.Has(ThisPower) || BoundAttack(powers.DataOf(ThisPower)) != attack.Id) return One(s);
        SimEngine.SetPowers(s, owner, powers.Without(ThisPower));
        s.LogFired(Name, 0, owner);
        return One(s);
    }
}

/// <summary>
/// GigantificationPower: triples the owner's powered attacks from an Attack card; the first such attack takes a stack
/// (AfterAttack: Decrement).
/// </summary>
public sealed class GigantificationPower : AttackBoundPower
{
    public override TabKind? Tab => TabKind.Trigger;
    protected override bool Qualifies(SimState s, int owner, AttackInfo a) =>
        a.Card is { Type: SimCardType.Attack } && s.CurrentPlay?.Owner == owner && a.Props.IsPoweredAttack();
    public override decimal ModifyDamageMultiplicative(SimState s, int owner, DamageCtx d) =>
        d.Card != null && d.Play?.Owner == owner && d.Props.IsPoweredAttack() && AppliesNow(s, owner, ThisPower) ? 3m : 1m;
    public override IEnumerable<SimState> AfterAttack(SimState s, int owner, AttackInfo attack)
    {
        var powers = SimEngine.PowersOf(s, owner);
        if (!powers.Has(ThisPower) || BoundAttack(powers.DataOf(ThisPower)) != attack.Id) return One(s);
        var left = powers[ThisPower] - 1;
        SimEngine.SetPowers(s, owner, left <= 0 ? powers.Without(ThisPower) : powers.With(ThisPower, left, 0));
        s.LogFired(Name, 0, owner);
        return One(s);
    }
}

/// <summary>DoubleDamagePower.ModifyDamageMultiplicative: 2 on the owner's (or their pet's) powered attacks from a card.</summary>
public sealed class DoubleDamagePower : SimModel
{
    public override decimal ModifyDamageMultiplicative(SimState s, int owner, DamageCtx d) =>
        (d.Dealer == owner || (owner == Who.You && d.Dealer == Who.YourOsty)) && d.Props.IsPoweredAttack() && d.Card != null ? 2m : 1m;
}

/// <summary>
/// SlowPower: powered attacks on its owner deal 10% more per card played this turn (SlowAmount, Data: AfterCardPlayed adds 1
/// for every card played; reset when its side's turn starts). It flashes when it changes damage.
/// </summary>
public sealed class SlowPower : SimModel
{
    public override TabKind? Tab => TabKind.Trigger;
    public override bool FlashesOnDamageModified => true;
    public override decimal ModifyDamageMultiplicative(SimState s, int owner, DamageCtx d) =>
        d.Target == owner && d.Props.IsPoweredAttack() ? 1m + 0.1m * SimEngine.PowersOf(s, owner).DataOf(ThisPower) : 1m;
    public override IEnumerable<SimState> AfterCardPlayed(SimState s, int owner, SimCardPlay play)
    {
        var powers = SimEngine.PowersOf(s, owner);
        if (!powers.Has(ThisPower)) return One(s);
        SimEngine.SetPowers(s, owner, powers.With(ThisPower, powers[ThisPower], powers.DataOf(ThisPower) + 1));
        s.LogFired(Name, 0, owner);
        return One(s);
    }
}

/// <summary>TenderPower.AfterCardPlayed: each card its owner plays takes 1 Strength and 1 Dexterity (Artifact can take them).</summary>
public sealed class TenderPower : SimModel
{
    public override TabKind? Tab => TabKind.Trigger;
    public override bool Harmful => true;
    public override IEnumerable<SimState> AfterCardPlayed(SimState s, int owner, SimCardPlay play)
    {
        if (play.Owner != owner) return One(s);
        s.LogFired(Name, 0, owner);
        SimEngine.ApplyPower(s, owner, owner, PowerId.Strength, -1, debuff: true);
        SimEngine.ApplyPower(s, owner, owner, PowerId.Dexterity, -1, debuff: true);
        return One(s);
    }
}

/// <summary>
/// LethalityPower.ModifyDamageMultiplicative: 1 + amount/100 on the owner's first Attack play each turn (a replay's later
/// plays don't get it; attack plays started this turn: the owner's Turn "Started:Attack").
/// </summary>
public sealed class LethalityPower : SimModel
{
    public override decimal ModifyDamageMultiplicative(SimState s, int owner, DamageCtx d)
    {
        if (!d.Props.IsPoweredAttack() || d.Card == null || d.Play?.Owner != owner) return 1m;
        if (d.Play.PlayIndex > 0) return 1m;
        var started = SimEngine.TurnOf(s, owner)[SimKeys.Plays(PlayStage.Started, SimCardType.Attack)];
        return started > 1 ? 1m : 1m + SimEngine.PowerOn(s, owner, ThisPower) / 100m;
    }
}

/// <summary>PhantomBladesPower.ModifyDamageAdditive: its amount on the owner's first Shiv each turn (no Shiv play finished yet).</summary>
public sealed class PhantomBladesPower : SimModel
{
    public override decimal ModifyDamageAdditive(SimState s, int owner, DamageCtx d)
    {
        if (!d.Props.IsPoweredAttack() || d.Card == null || !d.Card.Is(SimCardTag.Shiv) || d.Dealer != owner) return 0m;
        var p = s.Player(owner);
        return p != null && p.Turn[SimKeys.Plays(PlayStage.Finished, SimCardTag.Shiv)] > 0 ? 0m : SimEngine.PowerOn(s, owner, ThisPower);
    }
}

/// <summary>SerratedArrowsPower.ModifyDamageMultiplicative: 1 + amount/100 on the owner's (or pet's) powered hits on an Impaled enemy.</summary>
public sealed class SerratedArrowsPower : SimModel
{
    public override decimal ModifyDamageMultiplicative(SimState s, int owner, DamageCtx d)
    {
        if (!d.Props.IsPoweredAttack() || !(d.Dealer == owner || (owner == Who.You && d.Dealer == Who.YourOsty))) return 1m;
        return d.Target >= 0 && SimEngine.IsImpaled(s, d.Target) ? 1m + SimEngine.PowerOn(s, owner, ThisPower) / 100m : 1m;
    }
}

/// <summary>
/// Pen Nib (relic): Amount is AttacksPlayed (0-9). Data 1 while a card the game is playing right now is bound to be doubled
/// (AttackToDouble, read mid-play: no card simulated here is that card, so none is doubled), 2 while a play simulated here is.
/// BeforeCardPlayed counts your Attack plays (each play of a replay); the one that brings it to 0 binds itself (and flashes);
/// its powered hits from that play deal double (ModifyDamageMultiplicative); AfterCardPlayed of that play unbinds.
/// </summary>
public sealed class PenNib : SimModel
{
    public override TabKind? Tab => TabKind.Trigger;
    public override decimal ModifyDamageMultiplicative(SimState s, int owner, DamageCtx d)
    {
        if (!d.Props.IsPoweredAttack() || d.Card == null) return 1m;
        if (!(d.Dealer == owner || (owner == Who.You && d.Dealer == Who.YourOsty))) return 1m;
        var r = SimEngine.RelicsOf(s, owner);
        if (r.DataOf(ThisRelic) == 2) return d.Play != null ? 2m : 1m;
        if (r.DataOf(ThisRelic) == 1) return 1m; // the game's card mid-play is the doubled one
        return d.Play == null && r[ThisRelic] == 9 ? 2m : 1m; // not being played (a preview): the next attack is the 10th
    }
    public override IEnumerable<SimState> BeforeCardPlayed(SimState s, int owner, SimCardPlay play)
    {
        if (play.Card.Type != SimCardType.Attack || play.Owner != owner) return One(s);
        var r = SimEngine.RelicsOf(s, owner);
        var count = (r[ThisRelic] + 1) % 10;
        SimEngine.SetRelics(s, owner, r.With(ThisRelic, count, count == 0 ? 2 : r.DataOf(ThisRelic)));
        if (count == 0) s.LogFired(Name, 0, owner);
        return One(s);
    }
    public override IEnumerable<SimState> AfterCardPlayed(SimState s, int owner, SimCardPlay play)
    {
        var r = SimEngine.RelicsOf(s, owner);
        if (r.DataOf(ThisRelic) == 2 && play.Owner == owner) SimEngine.SetRelics(s, owner, r.With(ThisRelic, r[ThisRelic], 0));
        return One(s);
    }
}

/// <summary>
/// Mighty Wings (relic): Amount is its Strength, Data its damage threshold. AfterDamageGiven: a powered hit by its owner (or
/// their Osty) whose TotalDamage is at least the threshold flashes and gives the owner that much Strength.
/// </summary>
public sealed class MightyWings : SimModel
{
    public override TabKind? Tab => TabKind.Trigger;
    public override void AfterDamageGiven(SimState s, int owner, DamageEvent e)
    {
        if (!e.Props.IsPoweredAttack() || !(e.Dealer == owner || (owner == Who.You && e.Dealer == Who.YourOsty))) return;
        var r = SimEngine.RelicsOf(s, owner);
        if (e.Total < r.DataOf(ThisRelic)) return;
        s.LogFired(Name, 0, owner);
        SimEngine.ApplyPower(s, owner, owner, PowerId.Strength, r[ThisRelic], debuff: false);
    }
}
