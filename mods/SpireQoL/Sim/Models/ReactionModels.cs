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
/// SuckPower (decompile v0.114.0: Fossil Stalker's): after its owner's own attack on the other side (AfterAttack, a powered attack: a move's), Strength of
/// its amount for each of the attack's hits that got HP through on anyone it hit (each hit's results over every player; a hit on a pet leaves out its
/// owner's result). A dev review: Lash's two hits got through, Fossil Stalker gained 6 Strength, and its next Latch hit for 18 where 12 was projected.
/// Its Data while the attack runs: the hit being followed (index + 1, bits 16 to 23), the hits counted (bits 24 and up), and that hit's players: those it
/// got HP through to (bits 0 to 7: you 0, teammate k k + 1), those whose pet it landed on (bits 8 to 15). Cleared as the attack ends.
/// </summary>
public sealed class SuckPower : SimModel
{
    private const int HitShift = 16, CountShift = 24, PetShift = 8, PlayersMask = 0xFF, HitMask = 0xFF << HitShift, OneHit = 1;
    /// <summary>A pet's own result through (its owner's place among the players' bits, never masked by a pet's hit).</summary>
    private const int PetThrough = 1 << 7;

    private static int PlayerBit(int player) => 1 << (player == Who.You ? 0 : 1 + Who.TeammateIndex(player));

    /// <summary>A hit's results through to anyone, its pets' owners left out (a pet's own result counts, in its owner's place).</summary>
    private static bool HitGotThrough(int data) => (data & PlayersMask & ~((data >> PetShift) & PlayersMask)) != 0;

    /// <summary>The followed hit closed: counted when it got through, its players cleared.</summary>
    private static int Closed(int data) =>
        ((data >> CountShift) + (HitGotThrough(data) ? OneHit : 0)) << CountShift | (data & HitMask);

    public override IEnumerable<SimState> BeforeAttack(SimState state, int owner, AttackInfo attack)
    {
        var powers = SimEngine.PowersOf(state, owner);
        if (attack.Dealer == owner && powers.Has(ThisPower)) SimEngine.SetPowers(state, owner, powers.With(ThisPower, powers[ThisPower], data: 0));
        return One(state);
    }

    public override void AfterDamageReceived(SimState state, int owner, DamageEvent hit)
    {
        if (hit.Dealer != owner || state.CurrentAttack == 0 || !Who.IsEnemy(owner) || Who.IsEnemy(hit.Target)) return;
        var powers = SimEngine.PowersOf(state, owner);
        if (!powers.Has(ThisPower)) return;
        var data = powers.DataOf(ThisPower);
        if ((data & HitMask) >> HitShift != state.CurrentHit + OneHit) data = Closed(data) & ~HitMask | (state.CurrentHit + OneHit) << HitShift;
        if (Who.IsPet(hit.Target))
        {
            var petOwner = Who.PetOwnerOf(hit.Target);
            if (petOwner != Who.None) data |= PlayerBit(petOwner) << PetShift;
            // The pet's own result: through when it lost HP, standing in for its owner's.
            if (hit.Unblocked > 0 && petOwner != Who.None) data |= PetThrough;
        }
        else if (hit.Unblocked > 0 && Who.IsPlayer(hit.Target)) data |= PlayerBit(hit.Target);
        SimEngine.SetPowers(state, owner, powers.With(ThisPower, powers[ThisPower], data));
    }

    public override IEnumerable<SimState> AfterAttack(SimState state, int owner, AttackInfo attack)
    {
        if (attack.Dealer != owner || !attack.Props.Has(SimProps.Move) || state.Enemy(owner) is not { } enemy || !enemy.Powers.Has(ThisPower)) return One(state);
        var data = enemy.Powers.DataOf(ThisPower);
        var hits = data == 0 ? 0 : Closed(data) >> CountShift;
        enemy.Powers = enemy.Powers.With(ThisPower, enemy.Powers[ThisPower], data: 0);
        if (hits == 0) return One(state);
        state.LogFired(Name, amount: 0, owner);
        return SimEngine.ApplyAndTell(state, owner, owner, PowerId.Strength, enemy.Powers[ThisPower] * hits, debuff: false);
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

