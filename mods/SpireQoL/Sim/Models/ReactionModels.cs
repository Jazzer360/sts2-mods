namespace SpireQoL.Sim.Models;

// What reacts to damage and to card plays (decompile v0.113.1): enemy reactions, your on-hit powers, HP-loss modifiers.

// Snecko Skull is read from its code (ReadModel: ModifyPowerAmountGivenAdditive and AfterModifyingPowerAmountGiven, decision #287).

// Hardened Shell is read from its code (ReadModel, decision #278).

// Burrowed is read from its code (ReadModel: AfterBlockBroken and ShouldClearBlock, decision #285).

// Asleep and Slumber are read from their code (ReadModel, decision #307): their wake tab compares the enemy's next move with and without the play.

// Plow is read from its code (ReadModel, decision #275).

// Shriek is read from its code (ReadModel, decision #251).

// The Gambit is read from its code (ReadModel, decision #256).

// Curl Up is read from its code (ReadModel, decision #274).

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
        if ((data & UsedThisTurn) != 0 || (data & FirstHitGotThrough) == 0 || !attack.Props.Has(SimProps.Move) || attack.Card == null) return One(state);
        enemy.Block += enemy.Powers[ThisPower];
        enemy.Powers = enemy.Powers.With(ThisPower, enemy.Powers[ThisPower], data: UsedThisTurn);
        state.LogFired(Name, amount: 0, owner);
        return One(state);
    }

    /// <summary>Its AfterSideTurnEnd acts on the other side's turn ending: never on its owner's, an enemy's.</summary>
    public override bool MayActOnTheEnemiesSide(string hook, int owner) => Overrides(hook) && !Who.IsEnemy(owner);

    /// <summary>AfterSideTurnEnd: the other side's turn ending (side != base.Owner.Side) clears HasGainedBlockThisTurn.</summary>
    public override IEnumerable<SimState> AfterSideTurnEnd(SimState state, int owner)
    {
        if (Who.IsEnemy(owner) == state.EnemiesSideNow || SimEngine.PowersOf(state, owner) is not { } powers || !powers.Has(ThisPower)) return One(state);
        SimEngine.SetPowers(state, owner, powers.With(ThisPower, powers[ThisPower], powers.DataOf(ThisPower) & ~UsedThisTurn));
        return One(state);
    }
}

