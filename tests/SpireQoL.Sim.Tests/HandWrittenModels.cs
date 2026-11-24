// The hand-written models the read models replaced (decision #195), kept as they were when they were deleted from Sim/Models, so the parity
// test (ReadModelParityTests) holds each read model to what the model it replaced did, on every scenario, for good. Not registered: SimModels
// knows only the simulator's own models. Each is the deleted class, unchanged but for its namespace.
using SpireQoL.Sim.Models;

namespace SpireQoL.Sim.Tests.HandWritten;

// ---- the damage and block math (Sim/Models/StatModels.cs, deleted 2026-11-24)

/// <summary>StrengthPower.ModifyDamageAdditive: its amount on the owner's powered attacks (negative allowed).</summary>
public sealed class StrengthPower : SimModel
{
    public override decimal ModifyDamageAdditive(SimState state, int owner, DamageCtx damage) =>
        damage.Dealer == owner && damage.Props.IsPoweredAttack() ? SimEngine.PowerOn(state, owner, ThisPower) : 0m;
}

/// <summary>
/// DexterityPower.ModifyBlockAdditive: its amount on powered block: from a card, the card's owner must be its owner; without a
/// card, its owner must be the one gaining it.
/// </summary>
public sealed class DexterityPower : SimModel
{
    public override decimal ModifyBlockAdditive(SimState state, int owner, BlockCtx block)
    {
        if (block.Play != null ? block.Play.Owner != owner : block.Target != owner) return 0m;
        return block.Props.IsPoweredBlock() ? SimEngine.PowerOn(state, owner, ThisPower) : 0m;
    }
}

/// <summary>FrailPower.ModifyBlockMultiplicative: 0.75 on its owner's powered block.</summary>
public sealed class FrailPower : SimModel
{
    public override decimal ModifyBlockMultiplicative(SimState state, int owner, BlockCtx block) =>
        block.Target == owner && block.Props.IsPoweredBlock() ? 0.75m : 1m;
}

/// <summary>DoubleDamagePower.ModifyDamageMultiplicative: 2 on the owner's (or their pet's) powered attacks from a card.</summary>
public sealed class DoubleDamagePower : SimModel
{
    public override decimal ModifyDamageMultiplicative(SimState state, int owner, DamageCtx damage) =>
        (damage.Dealer == owner || (owner == Who.You && damage.Dealer == Who.YourOsty)) && damage.Props.IsPoweredAttack() && damage.Card != null ? 2m : 1m;
}

// ---- powers acting on plays and card events (Sim/Models/ProcPowerModels.cs, deleted 2026-11-24)

/// <summary>RagePower.AfterCardPlayed: block (unpowered) for each Attack its owner plays.</summary>
public sealed class RagePower : ProcModel
{
    public override TabKind? Tab => TabKind.Block;
    public override IEnumerable<SimState> AfterCardPlayed(SimState state, int owner, SimCardPlay play) =>
        Mine(play, owner) && play.Card.Type == SimCardType.Attack ? Block(state, owner, Amount(state, owner)) : One(state);
}

/// <summary>SpiritOfAshPower.BeforeCardPlayed: block (unpowered) for each Ethereal card its owner plays.</summary>
public sealed class SpiritOfAshPower : ProcModel
{
    public override TabKind? Tab => TabKind.Block;
    public override IEnumerable<SimState> BeforeCardPlayed(SimState state, int owner, SimCardPlay play) =>
        Mine(play, owner) && play.Card.Ethereal ? Block(state, owner, Amount(state, owner)) : One(state);
}

/// <summary>DanseMacabrePower.BeforeCardPlayed: block (unpowered) for each card its owner plays costing at least its Energy value.</summary>
public sealed class DanseMacabrePower : ProcModel
{
    public override TabKind? Tab => TabKind.Block;
    public override IEnumerable<SimState> BeforeCardPlayed(SimState state, int owner, SimCardPlay play) =>
        Mine(play, owner) && play.Card.Energy >= Var(state, owner, "Energy", fallback: 2) ? Block(state, owner, Amount(state, owner)) : One(state);
}

/// <summary>FeelNoPainPower.AfterCardExhausted: block (unpowered) for each card its owner exhausts.</summary>
public sealed class FeelNoPainPower : ProcModel
{
    public override TabKind? Tab => TabKind.Block;
    public override IEnumerable<SimState> AfterCardEvent(SimState state, int owner, CardEvent cardEvent, int player) =>
        cardEvent == CardEvent.Exhausted && player == owner ? Block(state, owner, Amount(state, owner)) : One(state);
}

/// <summary>SpeedsterPower.AfterCardDrawn: its amount to every hittable enemy for each card its owner draws.</summary>
public sealed class SpeedsterPower : ProcModel
{
    public override TabKind? Tab => TabKind.Damage;
    public override IEnumerable<SimState> AfterCardEvent(SimState state, int owner, CardEvent cardEvent, int player) =>
        cardEvent == CardEvent.Drawn && player == owner ? DamageAll(state, owner, Amount(state, owner)) : One(state);
}

/// <summary>CorrosiveWavePower.AfterCardDrawn: its amount of poison on every hittable enemy for each card its owner draws.</summary>
public sealed class CorrosiveWavePower : ProcModel
{
    public override IEnumerable<SimState> AfterCardEvent(SimState state, int owner, CardEvent cardEvent, int player)
    {
        if (cardEvent != CardEvent.Drawn || player != owner) return One(state);
        foreach (var enemy in state.Targetable.ToList()) SimEngine.ApplyPower(state, owner, enemy.Id, PowerId.Poison, Amount(state, owner), debuff: true);
        return Fire(state, owner);
    }
}

/// <summary>ArsenalPower.AfterCardGeneratedForCombat: its amount of Strength for each card its owner creates.</summary>
public sealed class ArsenalPower : ProcModel
{
    public override IEnumerable<SimState> AfterCardEvent(SimState state, int owner, CardEvent cardEvent, int player)
    {
        if (!Generated(cardEvent) || player != owner) return One(state);
        SimEngine.ApplyPower(state, owner, owner, PowerId.Strength, Amount(state, owner), debuff: false);
        return Fire(state, owner);
    }
}

/// <summary>PillarOfCreationPower.AfterCardGeneratedForCombat: block (unpowered) for each card its owner creates.</summary>
public sealed class PillarOfCreationPower : ProcModel
{
    public override TabKind? Tab => TabKind.Block;
    public override IEnumerable<SimState> AfterCardEvent(SimState state, int owner, CardEvent cardEvent, int player) =>
        Generated(cardEvent) && player == owner ? Block(state, owner, Amount(state, owner)) : One(state);
}

/// <summary>AllIsWellPower.AfterCardGeneratedForCombat: block (unpowered) for each Sadness its owner creates.</summary>
public sealed class AllIsWellPower : ProcModel
{
    public override TabKind? Tab => TabKind.Block;
    public override IEnumerable<SimState> AfterCardEvent(SimState state, int owner, CardEvent cardEvent, int player) =>
        cardEvent == CardEvent.GeneratedSadness && player == owner ? Block(state, owner, Amount(state, owner)) : One(state);
}

/// <summary>SmokestackPower.AfterCardGeneratedForCombat: its amount to every hittable enemy for each status its owner creates.</summary>
public sealed class SmokestackPower : ProcModel
{
    public override TabKind? Tab => TabKind.Damage;
    public override IEnumerable<SimState> AfterCardEvent(SimState state, int owner, CardEvent cardEvent, int player) =>
        cardEvent == CardEvent.GeneratedStatus && player == owner ? DamageAll(state, owner, Amount(state, owner)) : One(state);
}

/// <summary>SleightOfFleshPower.AfterPowerAmountChanged: each debuff its owner lands on an enemy deals that enemy its amount (unpowered).</summary>
public sealed class SleightOfFleshPower : ProcModel
{
    public override TabKind? Tab => TabKind.Damage;
    public override IEnumerable<SimState> AfterPowerApplied(SimState state, int owner, PowerId power, int amount, int applier, int target, bool debuff) =>
        debuff && applier == owner && target >= 0 ? SimEngine.ProcDamageOn(state, owner, Name, target, Amount(state, owner), Unpowered) : One(state);
}

/// <summary>ViciousPower.AfterPowerAmountChanged: for each Vulnerable its owner lands on an enemy, draws its amount (CardPileCmd.Draw(Amount)).</summary>
public sealed class ViciousPower : ProcModel
{
    public override TabKind? Tab => TabKind.Draw;
    public override IEnumerable<SimState> AfterPowerApplied(SimState state, int owner, PowerId power, int amount, int applier, int target, bool debuff) =>
        power == PowerId.Vulnerable && applier == owner && target >= 0 ? Cause(Fire(state, owner), CardEvent.Drawn, count: Amount(state, owner), owner) : One(state);
}

/// <summary>ShroudPower.AfterPowerAmountChanged: block (unpowered) for each Doom its owner lands on an enemy.</summary>
public sealed class ShroudPower : ProcModel
{
    public override TabKind? Tab => TabKind.Block;
    public override IEnumerable<SimState> AfterPowerApplied(SimState state, int owner, PowerId power, int amount, int applier, int target, bool debuff) =>
        power == PowerId.Doom && applier == owner && target >= 0 ? Block(state, owner, Amount(state, owner)) : One(state);
}

// ---- relics acting on plays and card events (Sim/Models/ProcRelicModels.cs, deleted 2026-11-24)

/// <summary>DaughterOfTheWind.AfterCardPlayed: block for each Attack you play.</summary>
public sealed class DaughterOfTheWind : RelicProc
{
    public override TabKind? Tab => TabKind.Block;
    public override IEnumerable<SimState> AfterCardPlayed(SimState state, int owner, SimCardPlay play) =>
        Mine(play, owner) && play.Card.Type == SimCardType.Attack ? RelicBlock(state, owner) : One(state);
}

/// <summary>LostWisp.AfterCardPlayed: damage to every hittable enemy for each Power you play.</summary>
public sealed class LostWisp : RelicProc
{
    public override TabKind? Tab => TabKind.Damage;
    public override IEnumerable<SimState> AfterCardPlayed(SimState state, int owner, SimCardPlay play) =>
        Mine(play, owner) && play.Card.Type == SimCardType.Power ? RelicDamageAll(state, owner) : One(state);
}

/// <summary>GamePiece.AfterCardPlayed: its Cards value of draws for each Power you play (CardPileCmd.Draw(DynamicVars.Cards)).</summary>
public sealed class GamePiece : RelicProc
{
    public override TabKind? Tab => TabKind.Draw;
    public override IEnumerable<SimState> AfterCardPlayed(SimState state, int owner, SimCardPlay play) =>
        Mine(play, owner) && play.Card.Type == SimCardType.Power ? Cause(Fire(state, owner), CardEvent.Drawn, count: Var(state, owner, "Cards", fallback: 1), owner) : One(state);
}

/// <summary>CharonsAshes.AfterCardExhausted: damage to every hittable enemy for each card you exhaust.</summary>
public sealed class CharonsAshes : RelicProc
{
    public override TabKind? Tab => TabKind.Damage;
    public override IEnumerable<SimState> AfterCardEvent(SimState state, int owner, CardEvent cardEvent, int player) =>
        cardEvent == CardEvent.Exhausted && player == owner ? DamageAll(state, owner, Amount(state, owner), PropsOf(state, owner, "Damage")) : One(state);
}

/// <summary>ToughBandages.AfterCardDiscarded: block for each card you discard.</summary>
public sealed class ToughBandages : RelicProc
{
    public override TabKind? Tab => TabKind.Block;
    public override IEnumerable<SimState> AfterCardEvent(SimState state, int owner, CardEvent cardEvent, int player) =>
        cardEvent == CardEvent.Discarded && player == owner ? RelicBlock(state, owner) : One(state);
}

/// <summary>Fertilizer.AfterCardGeneratedForCombat: Strength (its StrengthPower value) when you create a Sadness.</summary>
public sealed class Fertilizer : RelicProc
{
    public override IEnumerable<SimState> AfterCardEvent(SimState state, int owner, CardEvent cardEvent, int player)
    {
        if (cardEvent != CardEvent.GeneratedSadness || player != owner) return One(state);
        SimEngine.ApplyPower(state, owner, owner, PowerId.Strength, Var(state, owner, "StrengthPower", fallback: 1), debuff: false);
        return Fire(state, owner);
    }
}
