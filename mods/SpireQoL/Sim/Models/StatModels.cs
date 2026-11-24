namespace SpireQoL.Sim.Models;

// The damage and block sources that can change during your turn (docs/SIMULATOR.md "Coverage"), each from its
// decompiled body (v0.113.1). Amounts live on the owner's PowerSet; Data holds what a model keeps between hooks.

// Strength, Dexterity, Frail and Double Damage are read from their code (ReadModel, decision #195).

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
    protected abstract bool Qualifies(SimState state, int owner, AttackInfo attack);

    public override IEnumerable<SimState> BeforeAttack(SimState state, int owner, AttackInfo attack)
    {
        var powers = SimEngine.PowersOf(state, owner);
        if (!powers.Has(ThisPower) || powers.DataOf(ThisPower) != 0 || !Qualifies(state, owner, attack)) return One(state);
        SimEngine.SetPowers(state, owner, powers.With(ThisPower, powers[ThisPower], (attack.Id << 16) | Math.Clamp(powers[ThisPower], 0, 0xFFFF)));
        return One(state);
    }

    /// <summary>It applies to this damage: not bound, or bound to the attack in progress.</summary>
    protected static bool AppliesNow(SimState state, int owner, PowerId name)
    {
        var bound = BoundAttack(SimEngine.PowersOf(state, owner).DataOf(name));
        return bound == 0 || bound == state.CurrentAttack;
    }
}

/// <summary>VigorPower: its amount on the owner's powered attack from a card; the attack that started with it spends that much (AfterAttack).</summary>
public sealed class VigorPower : AttackBoundPower
{
    public override TabKind? Tab => TabKind.Trigger;
    protected override bool Qualifies(SimState state, int owner, AttackInfo attack) => attack.Dealer == owner && attack.Props.IsPoweredAttack();
    public override decimal ModifyDamageAdditive(SimState state, int owner, DamageCtx damage) =>
        damage.Dealer == owner && damage.Props.IsPoweredAttack() && AppliesNow(state, owner, ThisPower) ? SimEngine.PowerOn(state, owner, ThisPower) : 0m;
    public override IEnumerable<SimState> AfterAttack(SimState state, int owner, AttackInfo attack)
    {
        var powers = SimEngine.PowersOf(state, owner);
        var data = powers.DataOf(ThisPower);
        if (!powers.Has(ThisPower) || BoundAttack(data) != attack.Id) return One(state);
        var left = powers[ThisPower] - AmountAtStart(data);
        SimEngine.SetPowers(state, owner, left == 0 ? powers.Without(ThisPower) : powers.With(ThisPower, left, data: 0));
        state.LogFired(Name, amount: 0, owner);
        return One(state);
    }
}

/// <summary>
/// AimPower: like Vigor, on the owner's powered arrow attacks (FromArrow); Heavy Shot multiplies it by its AimMultiplier.
/// </summary>
public sealed class AimPower : AttackBoundPower
{
    protected override bool Qualifies(SimState state, int owner, AttackInfo attack) =>
        attack.Dealer == owner && attack.Props.IsPoweredAttack() && attack.Props.HasFlag(SimProps.FromArrow);
    public override decimal ModifyDamageAdditive(SimState state, int owner, DamageCtx damage)
    {
        if (damage.Dealer != owner || !damage.Props.IsPoweredAttack() || !damage.Props.HasFlag(SimProps.FromArrow) || !AppliesNow(state, owner, ThisPower)) return 0m;
        decimal num = SimEngine.PowerOn(state, owner, ThisPower);
        if (damage.Card is { } heavy && heavy.Name == CardId.HeavyShot.Name && heavy.Vars.TryGetValue("AimMultiplier", out var mul)) num *= mul;
        return num;
    }
    public override IEnumerable<SimState> AfterAttack(SimState state, int owner, AttackInfo attack)
    {
        var powers = SimEngine.PowersOf(state, owner);
        var data = powers.DataOf(ThisPower);
        if (!powers.Has(ThisPower) || BoundAttack(data) != attack.Id) return One(state);
        var left = powers[ThisPower] - AmountAtStart(data);
        SimEngine.SetPowers(state, owner, left == 0 ? powers.Without(ThisPower) : powers.With(ThisPower, left, data: 0));
        state.LogFired(Name, amount: 0, owner);
        return One(state);
    }
}

/// <summary>QuietusPower: multiplies the owner's powered arrow attack by its amount; the attack bound to it removes it.</summary>
public sealed class QuietusPower : AttackBoundPower
{
    protected override bool Qualifies(SimState state, int owner, AttackInfo attack) =>
        attack.Dealer == owner && attack.Props.IsPoweredAttack() && attack.Props.HasFlag(SimProps.FromArrow);
    public override decimal ModifyDamageMultiplicative(SimState state, int owner, DamageCtx damage) =>
        damage.Dealer == owner && damage.Props.IsPoweredAttack() && damage.Props.HasFlag(SimProps.FromArrow) && AppliesNow(state, owner, ThisPower)
            ? SimEngine.PowerOn(state, owner, ThisPower) : 1m;
    public override IEnumerable<SimState> AfterAttack(SimState state, int owner, AttackInfo attack)
    {
        var powers = SimEngine.PowersOf(state, owner);
        if (!powers.Has(ThisPower) || BoundAttack(powers.DataOf(ThisPower)) != attack.Id) return One(state);
        SimEngine.SetPowers(state, owner, powers.Without(ThisPower));
        state.LogFired(Name, amount: 0, owner);
        return One(state);
    }
}

/// <summary>
/// GigantificationPower: triples the owner's powered attacks from an Attack card; the first such attack takes a stack
/// (AfterAttack: Decrement).
/// </summary>
public sealed class GigantificationPower : AttackBoundPower
{
    public override TabKind? Tab => TabKind.Trigger;
    protected override bool Qualifies(SimState state, int owner, AttackInfo attack) =>
        attack.Card is { Type: SimCardType.Attack } && state.CurrentPlay?.Owner == owner && attack.Props.IsPoweredAttack();
    public override decimal ModifyDamageMultiplicative(SimState state, int owner, DamageCtx damage) =>
        damage.Card != null && damage.Play?.Owner == owner && damage.Props.IsPoweredAttack() && AppliesNow(state, owner, ThisPower) ? 3m : 1m;
    public override IEnumerable<SimState> AfterAttack(SimState state, int owner, AttackInfo attack)
    {
        var powers = SimEngine.PowersOf(state, owner);
        if (!powers.Has(ThisPower) || BoundAttack(powers.DataOf(ThisPower)) != attack.Id) return One(state);
        var left = powers[ThisPower] - 1;
        SimEngine.SetPowers(state, owner, left <= 0 ? powers.Without(ThisPower) : powers.With(ThisPower, left, data: 0));
        state.LogFired(Name, amount: 0, owner);
        return One(state);
    }
}

/// <summary>
/// SlowPower: powered attacks on its owner deal 10% more per card played this turn (SlowAmount, Data: AfterCardPlayed adds 1
/// for every card played; reset when its side's turn starts). It flashes when it changes damage.
/// </summary>
public sealed class SlowPower : SimModel
{
    public override TabKind? Tab => TabKind.Trigger;
    public override bool FlashesOnDamageModified => true;
    public override decimal ModifyDamageMultiplicative(SimState state, int owner, DamageCtx damage) =>
        damage.Target == owner && damage.Props.IsPoweredAttack() ? 1m + 0.1m * SimEngine.PowersOf(state, owner).DataOf(ThisPower) : 1m;
    public override IEnumerable<SimState> AfterCardPlayed(SimState state, int owner, SimCardPlay play)
    {
        var powers = SimEngine.PowersOf(state, owner);
        if (!powers.Has(ThisPower)) return One(state);
        SimEngine.SetPowers(state, owner, powers.With(ThisPower, powers[ThisPower], powers.DataOf(ThisPower) + 1));
        state.LogFired(Name, amount: 0, owner);
        return One(state);
    }
}

/// <summary>TenderPower.AfterCardPlayed: each card its owner plays takes 1 Strength and 1 Dexterity (Artifact can take them).</summary>
public sealed class TenderPower : SimModel
{
    public override TabKind? Tab => TabKind.Trigger;
    public override bool Harmful => true;
    public override IEnumerable<SimState> AfterCardPlayed(SimState state, int owner, SimCardPlay play)
    {
        if (play.Owner != owner) return One(state);
        state.LogFired(Name, amount: 0, owner);
        SimEngine.ApplyPower(state, owner, owner, PowerId.Strength, amount: -1, debuff: true);
        SimEngine.ApplyPower(state, owner, owner, PowerId.Dexterity, amount: -1, debuff: true);
        return One(state);
    }
}

/// <summary>
/// LethalityPower.ModifyDamageMultiplicative: 1 + amount/100 on the owner's first Attack play each turn (a replay's later
/// plays don't get it; attack plays started this turn: the owner's Turn "Started:Attack").
/// </summary>
public sealed class LethalityPower : SimModel
{
    public override decimal ModifyDamageMultiplicative(SimState state, int owner, DamageCtx damage)
    {
        if (!damage.Props.IsPoweredAttack() || damage.Card == null || damage.Play?.Owner != owner) return 1m;
        if (damage.Play.PlayIndex > 0) return 1m;
        var started = SimEngine.TurnOf(state, owner)[SimKeys.Plays(PlayStage.Started, SimCardType.Attack)];
        return started > 1 ? 1m : 1m + SimEngine.PowerOn(state, owner, ThisPower) / 100m;
    }
}

/// <summary>PhantomBladesPower.ModifyDamageAdditive: its amount on the owner's first Shiv each turn (no Shiv play finished yet).</summary>
public sealed class PhantomBladesPower : SimModel
{
    public override decimal ModifyDamageAdditive(SimState state, int owner, DamageCtx damage)
    {
        if (!damage.Props.IsPoweredAttack() || damage.Card == null || !damage.Card.Is(SimCardTag.Shiv) || damage.Dealer != owner) return 0m;
        var player = state.Player(owner);
        return player != null && player.Turn[SimKeys.Plays(PlayStage.Finished, SimCardTag.Shiv)] > 0 ? 0m : SimEngine.PowerOn(state, owner, ThisPower);
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
    /// <summary>Its Data: the card the game is playing right now is the doubled one (AttackToDouble), or a play simulated here is.</summary>
    public const int GameCardBound = 1, SimulatedPlayBound = 2;
    public override TabKind? Tab => TabKind.Trigger;
    public override decimal ModifyDamageMultiplicative(SimState state, int owner, DamageCtx damage)
    {
        if (!damage.Props.IsPoweredAttack() || damage.Card == null) return 1m;
        if (!(damage.Dealer == owner || (owner == Who.You && damage.Dealer == Who.YourOsty))) return 1m;
        var relics = SimEngine.RelicsOf(state, owner);
        if (relics.DataOf(ThisRelic) == SimulatedPlayBound) return damage.Play != null ? 2m : 1m;
        if (relics.DataOf(ThisRelic) == GameCardBound) return 1m; // the game's card mid-play is the doubled one
        return damage.Play == null && relics[ThisRelic] == 9 ? 2m : 1m; // not being played (a preview): the next attack is the 10th
    }
    public override IEnumerable<SimState> BeforeCardPlayed(SimState state, int owner, SimCardPlay play)
    {
        if (play.Card.Type != SimCardType.Attack || play.Owner != owner) return One(state);
        var relics = SimEngine.RelicsOf(state, owner);
        var count = (relics[ThisRelic] + 1) % 10;
        SimEngine.SetRelics(state, owner, relics.With(ThisRelic, count, count == 0 ? SimulatedPlayBound : relics.DataOf(ThisRelic)));
        if (count == 0) state.LogFired(Name, amount: 0, owner);
        return One(state);
    }
    public override IEnumerable<SimState> AfterCardPlayed(SimState state, int owner, SimCardPlay play)
    {
        var relics = SimEngine.RelicsOf(state, owner);
        if (relics.DataOf(ThisRelic) == SimulatedPlayBound && play.Owner == owner) SimEngine.SetRelics(state, owner, relics.With(ThisRelic, relics[ThisRelic], data: 0));
        return One(state);
    }
}

/// <summary>
/// Mighty Wings (relic): Amount is its Strength, Data its damage threshold. AfterDamageGiven: a powered hit by its owner (or
/// their Osty) whose TotalDamage is at least the threshold flashes and gives the owner that much Strength.
/// </summary>
public sealed class MightyWings : SimModel
{
    public override TabKind? Tab => TabKind.Trigger;
    public override void AfterDamageGiven(SimState state, int owner, DamageEvent hit)
    {
        if (!hit.Props.IsPoweredAttack() || !(hit.Dealer == owner || (owner == Who.You && hit.Dealer == Who.YourOsty))) return;
        var relics = SimEngine.RelicsOf(state, owner);
        if (hit.Total < relics.DataOf(ThisRelic)) return;
        state.LogFired(Name, amount: 0, owner);
        SimEngine.ApplyPower(state, owner, owner, PowerId.Strength, relics[ThisRelic], debuff: false);
    }
}
