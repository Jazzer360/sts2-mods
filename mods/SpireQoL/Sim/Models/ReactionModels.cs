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
    public override void BeforeDamageReceived(SimState state, int owner, DamageCtx damage, int amount)
    {
        // Main's Thorns answers Omnislice's unpowered splash too (cardSource is Omnislice: MainRule.SplashIsUnpowered).
        var answers = damage.Props.IsPoweredAttack() || state.MainRules.HasFlag(MainRule.SplashIsUnpowered) && damage.Card?.Name == CardId.Omnislice.Name;
        if (damage.Target != owner || owner < 0 || damage.Dealer is not { } dealer || !answers) return;
        var thorns = SimEngine.PowerOn(state, owner, ThisPower);
        if (thorns <= 0) return;
        var name = (state.Enemy(owner)?.Name ?? "Enemy") + " Thorns";
        var hit = SimEngine.CalcDamage(state, thorns, new DamageCtx(owner, dealer == Who.YourOsty ? Who.You : dealer, SimProps.Unpowered | SimProps.SkipHurtAnim, Card: null, Play: null));
        state.LogFired(Name, hit, owner);
        if (dealer == Who.YourOsty)
        {
            SimEngine.OstyLoses(state, Who.You, hit);
        }
        else if (Who.IsPlayer(dealer)) SimEngine.LandOnPlayer(state, name, hit, dealer);
    }
}

/// <summary>Snecko Skull (relic, amount = its Poison var): poison its owner gives gets that much more (ModifyPowerAmountGivenAdditive).</summary>
public sealed class SneckoSkull : SimModel
{
    public override int ModifyPowerAmountGiven(SimState state, int owner, PowerId power, int amount, int? applier, int target, bool debuff)
    {
        if (power != PowerId.Poison || applier != owner) return amount;
        state.LogFired(Name, amount: 0, owner);
        return amount + SimEngine.RelicsOf(state, owner)[ThisRelic];
    }
}

/// <summary>HardenedShellPower: at most what's left this turn (Data) of HP lost (BeforeOstyLate); HP it loses uses that up (AfterDamageReceived).</summary>
public sealed class HardenedShellPower : SimModel
{
    public override decimal ModifyHpLostBeforeOstyLate(SimState state, int owner, DamageCtx damage, decimal amount) =>
        damage.Target != owner || amount == 0m ? amount : Math.Min(amount, Math.Max(0, SimEngine.PowersOf(state, owner).DataOf(ThisPower)));
    public override void AfterDamageReceived(SimState state, int owner, DamageEvent hit)
    {
        if (hit.Target != owner || hit.Unblocked == 0) return;
        var powers = SimEngine.PowersOf(state, owner);
        SimEngine.SetPowers(state, owner, powers.With(ThisPower, powers[ThisPower], Math.Max(0, powers.DataOf(ThisPower) - hit.Unblocked)));
    }
    /// <summary>BeforeSideTurnStart: what it took this turn starts over as any side's turn starts (no test of the side), before any AfterSideTurnStart.</summary>
    public override IEnumerable<SimState> BeforeSideTurnStart(SimState state, int owner)
    {
        var powers = SimEngine.PowersOf(state, owner);
        if (powers.Has(ThisPower)) SimEngine.SetPowers(state, owner, powers.With(ThisPower, powers[ThisPower], powers[ThisPower]));
        return One(state);
    }
}

/// <summary>SlipperyPower: at most 1 HP per damage to its owner (AfterOsty); each that loses HP flashes and takes a charge.</summary>
public sealed class SlipperyPower : SimModel
{
    public override OutcomeRule? OutcomeTab(SimState start, SimOutcome now, int owner) =>
        Who.IsEnemy(owner) && !now.SlipperyInAny(owner) ? new(Harmful: true, outcome => outcome.SlipperyInAny(owner), branch => branch.Enemy(owner) is { SlipperyTriggered: true }) : null;

    public override decimal ModifyHpLostAfterOsty(SimState state, int owner, DamageCtx damage, decimal amount) =>
        damage.Target != owner || amount < 1m ? amount : 1m;
    public override void AfterModifyingHpLost(SimState state, int owner, bool afterOsty) { } // it flashes when the damage lands
    public override void AfterDamageReceived(SimState state, int owner, DamageEvent hit)
    {
        if (hit.Target != owner || hit.Unblocked < 1) return;
        state.LogFired(Name, amount: 0, owner);
        var powers = SimEngine.PowersOf(state, owner);
        SimEngine.SetPowers(state, owner, powers[ThisPower] <= 1 ? powers.Without(ThisPower) : powers.With(ThisPower, powers[ThisPower] - 1));
        if (state.Enemy(owner) is { } enemy) enemy.SlipperyTriggered = true;
    }
}

/// <summary>BurrowedPower.AfterBlockBroken: breaking its owner's block stuns it, and the power goes (with its block-keeping).</summary>
public sealed class BurrowedPower : SimModel
{
    public override OutcomeRule? OutcomeTab(SimState start, SimOutcome now, int owner) => OutcomeRules.Stuns(now, owner);

    public override void AfterBlockBroken(SimState state, int owner, int target, int? breaker)
    {
        if (target != owner || state.Enemy(owner) is not { } enemy) return;
        SimEngine.Stun(enemy);
        enemy.Powers = enemy.Powers.Without(ThisPower);
        state.LogFired(Name, amount: 0, owner);
    }
}

/// <summary>AsleepPower.AfterDamageReceived: HP through its owner's block (any source) wakes it: Plating goes, it's stunned, the power goes.</summary>
public sealed class AsleepPower : SimModel
{
    public override OutcomeRule? OutcomeTab(SimState start, SimOutcome now, int owner) =>
        Who.IsEnemy(owner) && now.UnblockedMax(owner) < 1 ? new(Harmful: true, outcome => outcome.UnblockedMax(owner) >= 1, branch => (branch.Enemy(owner)?.UnblockedInstances ?? 0) >= 1) : null;

    public override void AfterDamageReceived(SimState state, int owner, DamageEvent hit)
    {
        if (hit.Target != owner || hit.Unblocked == 0 || state.Enemy(owner) is not { } enemy) return;
        enemy.Powers = enemy.Powers.Without(PowerId.Plating).Without(ThisPower);
        SimEngine.Stun(enemy);
        state.LogFired(Name, amount: 0, owner);
    }
}

/// <summary>SlumberPower.AfterDamageReceived: each damage that gets HP through takes a stack; the last stuns it.</summary>
public sealed class SlumberPower : SimModel
{
    public override OutcomeRule? OutcomeTab(SimState start, SimOutcome now, int owner) =>
        Who.IsEnemy(owner) && SimEngine.PowerOn(start, owner, ThisPower) is var stacks && now.UnblockedMax(owner) < stacks
            ? new(Harmful: true, outcome => outcome.UnblockedMax(owner) >= stacks, branch => (branch.Enemy(owner)?.UnblockedInstances ?? 0) >= stacks) : null;

    public override void AfterDamageReceived(SimState state, int owner, DamageEvent hit)
    {
        if (hit.Target != owner || hit.Unblocked == 0 || state.Enemy(owner) is not { } enemy) return;
        var left = enemy.Powers[ThisPower] - 1;
        enemy.Powers = left <= 0 ? enemy.Powers.Without(ThisPower) : enemy.Powers.With(ThisPower, left);
        if (left <= 0)
        {
            SimEngine.Stun(enemy);
            state.LogFired(Name, amount: 0, owner);
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

    public override void AfterDamageReceived(SimState state, int owner, DamageEvent hit)
    {
        if (hit.Target != owner || hit.Unblocked <= 0 || state.Enemy(owner) is not { Alive: true } enemy || enemy.Hp > enemy.Powers[ThisPower]) return;
        state.LogFired(Name, amount: 0, owner);
        enemy.Powers = enemy.Powers.Without(PowerId.Strength).Without(ThisPower);
        SimEngine.Stun(enemy);
    }
}

/// <summary>ShriekPower.AfterDamageReceived: HP through that leaves its owner at or below its amount stuns it; the power goes.</summary>
public sealed class ShriekPower : SimModel
{
    public override OutcomeRule? OutcomeTab(SimState start, SimOutcome now, int owner) => OutcomeRules.Stuns(now, owner);

    public override void AfterDamageReceived(SimState state, int owner, DamageEvent hit)
    {
        if (hit.Target != owner || hit.Unblocked <= 0 || state.Enemy(owner) is not { Alive: true } enemy || enemy.Hp > enemy.Powers[ThisPower]) return;
        state.LogFired(Name, amount: 0, owner);
        enemy.Powers = enemy.Powers.Without(ThisPower);
        SimEngine.Stun(enemy);
    }
}

/// <summary>TheGambitPower.AfterDamageReceived: a powered attack that gets HP through kills its owner.</summary>
public sealed class TheGambitPower : SimModel
{
    public override OutcomeRule? OutcomeTab(SimState start, SimOutcome now, int owner) =>
        Who.IsEnemy(owner) && !now.GambitInAll(owner) ? new(Harmful: false, outcome => outcome.GambitInAll(owner)) : null;

    public override void AfterDamageReceived(SimState state, int owner, DamageEvent hit)
    {
        if (hit.Target != owner || !hit.Props.IsPoweredAttack() || hit.Unblocked <= 0 || state.Enemy(owner) is not { } enemy) return;
        enemy.Powers = enemy.Powers.Without(ThisPower);
        state.LogFired(Name, amount: 0, owner);
        enemy.GambitTriggered = true;
        enemy.Hp = 0;
    }
}

/// <summary>
/// CurlUpPower: a powered hit from a card on its owner binds that card (Data 1: the play in progress); AfterCardPlayed of that
/// play gives it its amount of block (unpowered) and the power goes.
/// </summary>
public sealed class CurlUpPower : SimModel
{
    /// <summary>Its Data: a card in play hit it (the one whose play gives it block).</summary>
    private const int BoundToPlay = 1;
    public override TabKind? Tab => TabKind.Trigger;
    public override bool Harmful => true;
    public override void AfterDamageReceived(SimState state, int owner, DamageEvent hit)
    {
        if (hit.Target != owner || !hit.Props.IsPoweredAttack() || hit.Card == null || state.CurrentPlay == null) return;
        var powers = SimEngine.PowersOf(state, owner);
        if (powers.Has(ThisPower)) SimEngine.SetPowers(state, owner, powers.With(ThisPower, powers[ThisPower], data: BoundToPlay));
    }
    public override IEnumerable<SimState> AfterCardPlayed(SimState state, int owner, SimCardPlay play)
    {
        if (state.Enemy(owner) is not { } enemy || enemy.Powers.DataOf(ThisPower) != BoundToPlay) return One(state);
        enemy.Block += enemy.Powers[ThisPower];
        enemy.Powers = enemy.Powers.Without(ThisPower);
        enemy.ReactedWithBlock = true;
        state.LogFired(Name, amount: 0, owner);
        return One(state);
    }
}

/// <summary>
/// SkittishPower: once per turn (Data bit 1), after an attack from a card (a Move) whose first hit on its owner got HP through
/// (bit 4; bit 2 marks the first hit seen this attack), it gains its amount of block (AfterAttack).
/// </summary>
public sealed class SkittishPower : SimModel
{
    /// <summary>Its Data: block gained this turn (HasGainedBlockThisTurn); this attack's first hit was seen, and it got HP through.</summary>
    public const int UsedThisTurn = 1;
    private const int FirstHitSeen = 2, FirstHitGotThrough = 4;
    public override TabKind? Tab => TabKind.Trigger;
    public override bool Harmful => true;
    public override IEnumerable<SimState> BeforeAttack(SimState state, int owner, AttackInfo attack)
    {
        var powers = SimEngine.PowersOf(state, owner);
        if (powers.Has(ThisPower)) SimEngine.SetPowers(state, owner, powers.With(ThisPower, powers[ThisPower], powers.DataOf(ThisPower) & UsedThisTurn));
        return One(state);
    }
    public override void AfterDamageReceived(SimState state, int owner, DamageEvent hit)
    {
        if (hit.Target != owner || state.CurrentAttack == 0) return;
        var powers = SimEngine.PowersOf(state, owner);
        var data = powers.DataOf(ThisPower);
        if (!powers.Has(ThisPower) || (data & FirstHitSeen) != 0) return;
        SimEngine.SetPowers(state, owner, powers.With(ThisPower, powers[ThisPower], data | FirstHitSeen | (hit.Unblocked != 0 ? FirstHitGotThrough : 0)));
    }
    public override IEnumerable<SimState> AfterAttack(SimState state, int owner, AttackInfo attack)
    {
        if (state.Enemy(owner) is not { } enemy) return One(state);
        var data = enemy.Powers.DataOf(ThisPower);
        if ((data & UsedThisTurn) != 0 || (data & FirstHitGotThrough) == 0 || !attack.Props.HasFlag(SimProps.Move) || attack.Card == null) return One(state);
        enemy.Block += enemy.Powers[ThisPower];
        enemy.Powers = enemy.Powers.With(ThisPower, enemy.Powers[ThisPower], data: UsedThisTurn);
        enemy.ReactedWithBlock = true;
        state.LogFired(Name, amount: 0, owner);
        return One(state);
    }
}

