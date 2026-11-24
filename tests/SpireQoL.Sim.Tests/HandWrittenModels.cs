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

// ---- relics that count what you play or act once a turn, and more relics on plays and card events (Sim/Models/ProcRelicModels.cs and
// ProcModel.cs' CounterRelic, deleted 2026-11-24)

/// <summary>
/// A counter relic (Kunai, Shuriken, Ornamental Fan...): every card of its kind its owner plays counts (AfterCardPlayed); the one
/// that brings the count to its Cards value fires it, and the count starts over. Amount: the count (the tray's number).
/// </summary>
public abstract class CounterRelic : RelicProc
{
    protected abstract bool Counts(SimCard card);
    protected abstract IEnumerable<SimState> OnComplete(SimState state, int owner);

    public override IEnumerable<SimState> AfterCardPlayed(SimState state, int owner, SimCardPlay play)
    {
        if (!Mine(play, owner) || !Counts(play.Card)) return One(state);
        var cardsNeeded = Var(state, owner, "Cards", fallback: 0);
        if (cardsNeeded <= 0) return One(state);
        var count = Amount(state, owner) % cardsNeeded + 1;
        Set(state, owner, count % cardsNeeded, Data(state, owner));
        if (count < cardsNeeded) return One(state);
        return OnComplete(state, owner);
    }
}

/// <summary>IntimidatingHelmet.BeforeCardPlayed: block for each card you play costing at least its Energy value.</summary>
public sealed class IntimidatingHelmet : RelicProc
{
    public override TabKind? Tab => TabKind.Block;
    public override IEnumerable<SimState> BeforeCardPlayed(SimState state, int owner, SimCardPlay play) =>
        Mine(play, owner) && play.Card.Energy >= Var(state, owner, "Energy", fallback: 2) ? RelicBlock(state, owner) : One(state);
}

/// <summary>
/// HelicalDart.AfterCardPlayed: each Shiv you play gives Dexterity this turn (HelicalDartPower, a TemporaryDexterityPower: the
/// Dexterity now, taken back as the turn ends).
/// </summary>
public sealed class HelicalDart : RelicProc
{
    public override IEnumerable<SimState> AfterCardPlayed(SimState state, int owner, SimCardPlay play)
    {
        if (!Mine(play, owner) || !play.Card.Is(SimCardTag.Shiv)) return One(state);
        SimEngine.ApplyPower(state, owner, owner, PowerId.Dexterity, Var(state, owner, "DexterityPower", fallback: 1), debuff: false);
        return Fire(state, owner);
    }
}

/// <summary>IvoryTile.AfterCardPlayed: energy for each card you play costing at least its EnergyThreshold.</summary>
public sealed class IvoryTile : RelicProc
{
    public override TabKind? Tab => TabKind.Energy;
    public override IEnumerable<SimState> AfterCardPlayed(SimState state, int owner, SimCardPlay play) =>
        Mine(play, owner) && play.Card.Energy >= Var(state, owner, "EnergyThreshold", fallback: 3)
            ? Cause(Fire(state, owner), CardEvent.EnergyGained, Var(state, owner, "Energy", fallback: 1), owner) : One(state);
}

/// <summary>RainbowRing.AfterCardPlayed: once a turn (RelicUse.UsedThisTurn), the play that completes an Attack, a Skill and a Power this turn: Strength and Dexterity.</summary>
public sealed class RainbowRing : RelicProc
{
    public override IEnumerable<SimState> AfterCardPlayed(SimState state, int owner, SimCardPlay play)
    {
        if (!Mine(play, owner) || UsedThisTurn(state, owner)) return One(state);
        var turn = SimEngine.TurnOf(state, owner);
        int Of(SimCardType simCardType) => turn[SimKeys.Plays(PlayStage.Finished, simCardType)] - (play.Card.Type == simCardType ? 1 : 0);
        var all = new[] { SimCardType.Attack, SimCardType.Skill, SimCardType.Power }.All(simCardType => turn[SimKeys.Plays(PlayStage.Finished, simCardType)] > 0);
        var before = Of(SimCardType.Attack) > 0 && Of(SimCardType.Skill) > 0 && Of(SimCardType.Power) > 0;
        if (!all || before) return One(state);
        MarkUsedThisTurn(state, owner);
        SimEngine.ApplyPower(state, owner, owner, PowerId.Strength, Var(state, owner, "StrengthPower", fallback: 1), debuff: false);
        SimEngine.ApplyPower(state, owner, owner, PowerId.Dexterity, Var(state, owner, "DexterityPower", fallback: 1), debuff: false);
        return Fire(state, owner);
    }
}

/// <summary>Kunai: its Dexterity value (DynamicVars.Dexterity, kept as "DexterityPower") every Cards Attacks.</summary>
public sealed class Kunai : CounterRelic
{
    protected override bool Counts(SimCard card) => card.Type == SimCardType.Attack;
    protected override IEnumerable<SimState> OnComplete(SimState state, int owner)
    {
        SimEngine.ApplyPower(state, owner, owner, PowerId.Dexterity, Var(state, owner, "DexterityPower", fallback: 1), debuff: false);
        return Fire(state, owner);
    }
}

/// <summary>Shuriken: its Strength value (DynamicVars.Strength, kept as "StrengthPower") every Cards Attacks.</summary>
public sealed class Shuriken : CounterRelic
{
    protected override bool Counts(SimCard card) => card.Type == SimCardType.Attack;
    protected override IEnumerable<SimState> OnComplete(SimState state, int owner)
    {
        SimEngine.ApplyPower(state, owner, owner, PowerId.Strength, Var(state, owner, "StrengthPower", fallback: 1), debuff: false);
        return Fire(state, owner);
    }
}

public sealed class Nunchaku : CounterRelic
{
    public override TabKind? Tab => TabKind.Energy;
    protected override bool Counts(SimCard card) => card.Type == SimCardType.Attack;
    protected override IEnumerable<SimState> OnComplete(SimState state, int owner) => Cause(Fire(state, owner), CardEvent.EnergyGained, Var(state, owner, "Energy", fallback: 1), owner);
}

public sealed class OrnamentalFan : CounterRelic
{
    public override TabKind? Tab => TabKind.Block;
    protected override bool Counts(SimCard card) => card.Type == SimCardType.Attack;
    protected override IEnumerable<SimState> OnComplete(SimState state, int owner) => RelicBlock(state, owner);
}

public sealed class Kusarigama : CounterRelic
{
    public override TabKind? Tab => TabKind.Damage;
    protected override bool Counts(SimCard card) => card.Type == SimCardType.Attack;
    protected override IEnumerable<SimState> OnComplete(SimState state, int owner) => RelicDamageRandom(state, owner);
}

public sealed class LetterOpener : CounterRelic
{
    public override TabKind? Tab => TabKind.Damage;
    protected override bool Counts(SimCard card) => card.Type == SimCardType.Skill;
    protected override IEnumerable<SimState> OnComplete(SimState state, int owner) => RelicDamageAll(state, owner);
}

public sealed class TuningFork : CounterRelic
{
    public override TabKind? Tab => TabKind.Block;
    protected override bool Counts(SimCard card) => card.Type == SimCardType.Skill;
    protected override IEnumerable<SimState> OnComplete(SimState state, int owner) => RelicBlock(state, owner);
}

public sealed class IronClub : CounterRelic
{
    public override TabKind? Tab => TabKind.Draw;
    protected override bool Counts(SimCard card) => true;
    protected override IEnumerable<SimState> OnComplete(SimState state, int owner) => Cause(Fire(state, owner), CardEvent.Drawn, count: 1, owner);
}

/// <summary>ForgottenSoul.AfterCardExhausted: damage to a random hittable enemy for each card you exhaust.</summary>
public sealed class ForgottenSoul : RelicProc
{
    public override TabKind? Tab => TabKind.Damage;
    public override IEnumerable<SimState> AfterCardEvent(SimState state, int owner, CardEvent cardEvent, int player) =>
        cardEvent == CardEvent.Exhausted && player == owner ? DamageRandom(state, owner, Amount(state, owner), PropsOf(state, owner, "Damage")) : One(state);
}

/// <summary>Tingsha.AfterCardDiscarded: damage to a random hittable enemy for each card you discard.</summary>
public sealed class Tingsha : RelicProc
{
    public override TabKind? Tab => TabKind.Damage;
    public override IEnumerable<SimState> AfterCardEvent(SimState state, int owner, CardEvent cardEvent, int player) =>
        cardEvent == CardEvent.Discarded && player == owner ? RelicDamageRandom(state, owner) : One(state);
}

/// <summary>MiniRegent.AfterStarsSpent: once a turn (armed), Strength when you spend stars.</summary>
public sealed class MiniRegent : RelicProc
{
    public override IEnumerable<SimState> AfterCardEvent(SimState state, int owner, CardEvent cardEvent, int player)
    {
        if (cardEvent != CardEvent.StarsSpent || player != owner || !Armed(state, owner)) return One(state);
        Disarm(state, owner);
        SimEngine.ApplyPower(state, owner, owner, PowerId.Strength, Var(state, owner, "StrengthPower", fallback: 1), debuff: false);
        return Fire(state, owner);
    }
}

/// <summary>TheAbacus.AfterShuffle: block on every shuffle.</summary>
public sealed class TheAbacus : RelicProc
{
    public override TabKind? Tab => TabKind.Block;
    public override IEnumerable<SimState> AfterCardEvent(SimState state, int owner, CardEvent cardEvent, int player) =>
        cardEvent == CardEvent.Shuffled && player == owner ? RelicBlock(state, owner) : One(state);
}

/// <summary>WhittlingKnife.AfterShuffle: Aim (its AimPower value) on every shuffle.</summary>
public sealed class WhittlingKnife : RelicProc
{
    public override IEnumerable<SimState> AfterCardEvent(SimState state, int owner, CardEvent cardEvent, int player)
    {
        if (cardEvent != CardEvent.Shuffled || player != owner) return One(state);
        SimEngine.ApplyPower(state, owner, owner, PowerId.Aim, Var(state, owner, "AimPower", fallback: 5), debuff: false);
        return Fire(state, owner);
    }
}

// ---- once a combat, once a turn (Sim/Models/ProcRelicModels.cs), and Tainted (Sim/Models/TargetModels.cs), deleted 2026-11-24

/// <summary>Permafrost.AfterCardPlayed: block for the first Power you play each combat.</summary>
public sealed class Permafrost : RelicProc
{
    public override TabKind? Tab => TabKind.Block;
    public override IEnumerable<SimState> AfterCardPlayed(SimState state, int owner, SimCardPlay play)
    {
        if (!Mine(play, owner) || play.Card.Type != SimCardType.Power || !Armed(state, owner)) return One(state);
        Disarm(state, owner);
        return RelicBlock(state, owner);
    }
}

/// <summary>Regalite.AfterCardGeneratedForCombat: once a turn (armed), block when you create a card.</summary>
public sealed class Regalite : RelicProc
{
    public override TabKind? Tab => TabKind.Block;
    public override IEnumerable<SimState> AfterCardEvent(SimState state, int owner, CardEvent cardEvent, int player)
    {
        if (!Generated(cardEvent) || player != owner || !Armed(state, owner)) return One(state);
        Disarm(state, owner);
        return RelicBlock(state, owner);
    }
}

/// <summary>TaintedPower.ModifyDamageAdditive: its amount on powered attacks on its owner (Vital Spark gives it for Tainted cards).</summary>
public sealed class TaintedPower : SimModel
{
    public override decimal ModifyDamageAdditive(SimState state, int owner, DamageCtx damage) =>
        damage.Target == owner && damage.Props.IsPoweredAttack() ? SimEngine.PowerOn(state, owner, ThisPower) : 0m;
}

// ---- powers bound to a play (Sim/Models/ProcPowerModels.cs), deleted 2026-11-24: the amount bound at BeforeCardPlayed acts at AfterCardPlayed

/// <summary>A power that binds its owner's play at BeforeCardPlayed (when <see cref="Binds"/>) and acts on it at AfterCardPlayed.</summary>
public abstract class BoundPower : ProcModel
{
    protected virtual bool Binds(SimCardPlay play) => true;
    protected virtual int BoundAmount(SimState state, int owner) => Amount(state, owner);
    protected abstract IEnumerable<SimState> Act(SimState state, int owner, SimCardPlay play, int amount);

    public override IEnumerable<SimState> BeforeCardPlayed(SimState state, int owner, SimCardPlay play)
    {
        if (Mine(play, owner) && Binds(play)) Bind(state, owner, BoundAmount(state, owner));
        return One(state);
    }

    public override IEnumerable<SimState> AfterCardPlayed(SimState state, int owner, SimCardPlay play) =>
        Mine(play, owner) && TakeBound(state, owner) is { } bound ? Act(state, owner, play, bound) : One(state);
}

/// <summary>AfterimagePower: block (unpowered) for each card its owner plays.</summary>
public sealed class AfterimagePower : BoundPower
{
    public override TabKind? Tab => TabKind.Block;
    protected override IEnumerable<SimState> Act(SimState state, int owner, SimCardPlay play, int amount) => amount > 0 ? Block(state, owner, amount) : One(state);
}

/// <summary>GravityPower: damage (unpowered) to every hittable enemy for each card its owner plays.</summary>
public sealed class GravityPower : BoundPower
{
    public override TabKind? Tab => TabKind.Damage;
    protected override IEnumerable<SimState> Act(SimState state, int owner, SimCardPlay play, int amount) => amount > 0 ? DamageAll(state, owner, amount) : One(state);
}

/// <summary>StormPower: each Power card its owner plays channels its amount of Lightning (bound at BeforeCardPlayed).</summary>
public sealed class StormPower : BoundPower
{
    protected override bool Binds(SimCardPlay play) => play.Card.Type == SimCardType.Power;
    protected override IEnumerable<SimState> Act(SimState state, int owner, SimCardPlay play, int amount) => Channel(Fire(state, owner), owner, OrbKind.Lightning, amount);
}

/// <summary>SubroutinePower: each Power card its owner plays gives its amount of energy (bound as the play begins).</summary>
public sealed class SubroutinePower : BoundPower
{
    public override TabKind? Tab => TabKind.Energy;
    protected override bool Binds(SimCardPlay play) => play.Card.Type == SimCardType.Power;
    protected override IEnumerable<SimState> Act(SimState state, int owner, SimCardPlay play, int amount) => Cause(Fire(state, owner), CardEvent.EnergyGained, amount, owner);
}

// ---- end-of-turn relics (Sim/Models/EndOfTurnModels.cs), deleted 2026-11-24: no tab on cards, their amount their value (SimReaders.RelicAmountVars)

/// <summary>
/// Orichalcum (and its fake): BeforeSideTurnEndVeryEarly arms it when its owner has no block (RelicUse.Armed); BeforeSideTurnEnd then
/// gives its block (amount, unpowered).
/// </summary>
public abstract class OrichalcumBase : RelicProc
{
    public override TabKind? Tab => null;
    public override IEnumerable<SimState> BeforeSideTurnEndVeryEarly(SimState state, int owner)
    {
        if (state.TakesPart(owner) && state.Player(owner) is { Block: <= 0 }) Set(state, owner, Amount(state, owner), (int)RelicUse.Armed); // armed: it gives its block
        return One(state);
    }
    public override IEnumerable<SimState> BeforeSideTurnEnd(SimState state, int owner)
    {
        if (!Armed(state, owner)) return One(state);
        Disarm(state, owner);
        return Block(state, owner, Amount(state, owner));
    }
}

public sealed class Orichalcum : OrichalcumBase { }
public sealed class FakeOrichalcum : OrichalcumBase { }

/// <summary>ScreamingFlagon.BeforeSideTurnEnd: its damage (amount) to every hittable enemy when its owner's hand is empty.</summary>
public sealed class ScreamingFlagon : RelicProc
{
    public override TabKind? Tab => null;
    public override IEnumerable<SimState> BeforeSideTurnEnd(SimState state, int owner) =>
        state.TakesPart(owner) && state.Player(owner) is { HandCount: 0 } ? DamageAll(state, owner, Amount(state, owner)) : One(state);
}

// ---- a card play's count (Sim/Models/ProcPowerModels.cs, ProcRelicModels.cs), deleted 2026-11-24: decided as each card play starts (decision #192)

/// <summary>
/// A power that plays its owner's cards of a kind once more, a stack used each time (ModifyCardPlayCount: +1; AfterModifyingCardPlayCount:
/// PowerCmd.Decrement, gone at 0), decided as each card play starts (decision #192): Burst, Duplication, One-Two Punch, Signal Boost, Ki
/// Verse. Its tag is the double-play tag (CardPlays), not a proc: it logs no firing. Removed at its owner's turn's end, after the plays.
/// </summary>
public abstract class ExtraPlayPower : SimModel
{
    /// <summary>The card is of the kind it replays (its ModifyCardPlayCount's test of the card).</summary>
    protected abstract bool Replays(SimCard card);

    public override int ModifyCardPlayCount(SimState state, int owner, SimCardPlay play, int count) =>
        play.Owner == owner && Replays(play.Card) && SimEngine.PowerOn(state, owner, ThisPower) > 0 ? count + 1 : count;

    public override void AfterModifyingCardPlayCount(SimState state, int owner, SimCardPlay play) => SimEngine.DecrementPower(state, owner, ThisPower);
}

/// <summary>BurstPower: its owner's Skills.</summary>
public sealed class BurstPower : ExtraPlayPower
{
    protected override bool Replays(SimCard card) => card.Type == SimCardType.Skill;
}

/// <summary>DuplicationPower: any card of its owner's.</summary>
public sealed class DuplicationPower : ExtraPlayPower
{
    protected override bool Replays(SimCard card) => true;
}

/// <summary>OneTwoPunchPower: its owner's Attacks.</summary>
public sealed class OneTwoPunchPower : ExtraPlayPower
{
    protected override bool Replays(SimCard card) => card.Type == SimCardType.Attack;
}

/// <summary>SignalBoostPower: its owner's Powers.</summary>
public sealed class SignalBoostPower : ExtraPlayPower
{
    protected override bool Replays(SimCard card) => card.Type == SimCardType.Power;
}

/// <summary>KiVersePower: its owner's melodies (cards tagged Wah, Cho or Ki).</summary>
public sealed class KiVersePower : ExtraPlayPower
{
    protected override bool Replays(SimCard card) => card.Is(SimCardTag.Wah) || card.Is(SimCardTag.Cho) || card.Is(SimCardTag.Ki);
}

/// <summary>
/// ThrowingAxe (Data: RelicUse.Armed while unused this fight). ModifyCardPlayCount: its owner's first card play of the fight plays once more;
/// AfterModifyingCardPlayCount uses it up (UsedThisCombat). Decided as each card play starts (decision #192). Its tag is the double-play tag
/// (CardPlays), not a proc: it logs no firing.
/// </summary>
public sealed class ThrowingAxe : SimModel
{
    private static bool Armed(SimState state, int owner) => ((RelicUse)SimEngine.RelicsOf(state, owner).DataOf(RelicId.ThrowingAxe)).HasFlag(RelicUse.Armed);

    public override int ModifyCardPlayCount(SimState state, int owner, SimCardPlay play, int count) =>
        Armed(state, owner) && play.Owner == owner ? count + 1 : count;

    public override void AfterModifyingCardPlayCount(SimState state, int owner, SimCardPlay play)
    {
        var relics = SimEngine.RelicsOf(state, owner);
        SimEngine.SetRelics(state, owner, relics.With(RelicId.ThrowingAxe, relics[RelicId.ThrowingAxe], 0));
    }
}

// ---- a hit's reaction (Sim/Models/ReactionModels.cs), deleted 2026-11-24: inside the damage

/// <summary>EnvenomPower.AfterDamageGiven: a powered hit of its owner's that gets HP through poisons the target.</summary>
public sealed class EnvenomPower : SimModel
{
    public override TabKind? Tab => TabKind.Trigger;
    public override void AfterDamageGiven(SimState state, int owner, DamageEvent hit)
    {
        if (hit.Dealer != owner || !hit.Props.IsPoweredAttack() || hit.Unblocked <= 0) return;
        state.LogFired(Name, amount: 0, owner);
        SimEngine.ApplyPower(state, owner, hit.Target, PowerId.Poison, SimEngine.PowerOn(state, owner, ThisPower), debuff: true);
    }
}

// ---- an enemy reacting to your play (Sim/Models/EnemyModels.cs), deleted 2026-11-24: a crossed tab

/// <summary>EnragePower.AfterCardPlayed: every Skill played gives its owner its amount of Strength.</summary>
public sealed class EnragePower : SimModel
{
    public override TabKind? Tab => TabKind.Trigger;
    public override bool Harmful => true;
    public override IEnumerable<SimState> AfterCardPlayed(SimState state, int owner, SimCardPlay play)
    {
        if (play.Card.Type != SimCardType.Skill || state.Enemy(owner) is not { Alive: true }) return One(state);
        state.LogFired(Name, amount: 0, owner);
        SimEngine.ApplyPower(state, owner, owner, PowerId.Strength, SimEngine.PowerOn(state, owner, ThisPower), debuff: false);
        return One(state);
    }
}

// ---- the turn's end: damage to its owner, a heal (Sim/Models/EndOfTurnModels.cs, deleted 2026-11-24)

/// <summary>
/// RegenPower.BeforeSideTurnEndEarly: its owner heals its amount (CreatureCmd.Heal: up to max HP, no modifiers) and it goes down by
/// 1. On a player that's the end of their turn, before the enemies act; an enemy's heals after its own attacks (not simulated past).
/// </summary>
public sealed class RegenPower : EndOfTurnModel
{
    public override IEnumerable<SimState> BeforeSideTurnEndEarly(SimState state, int owner)
    {
        var amount = Amount(state, owner);
        if (!state.TakesPart(owner) || state.Player(owner) is not { Died: false, Hp: < int.MaxValue } || amount <= 0) return One(state);
        var player = state.MutablePlayer(owner);
        player.Hp = (int)Math.Min(player.MaxHp, (long)player.Hp + amount);
        var powers = SimEngine.PowersOf(state, owner);
        SimEngine.SetPowers(state, owner, amount > 1 ? powers.With(ThisPower, amount - 1, powers.DataOf(ThisPower)) : powers.Without(ThisPower));
        return One(state);
    }
}

/// <summary>DemisePower.AfterSideTurnEnd: its amount to its owner, unblockable (no dealer).</summary>
public sealed class DemisePower : TurnEndDebuff
{
    public override IEnumerable<SimState> AfterSideTurnEnd(SimState state, int owner) =>
        state.TakesPart(owner) ? Hurt(state, owner, Amount(state, owner), SimProps.Unblockable | Unpowered, selfDealt: false) : One(state);
}

/// <summary>DisintegrationPower.AfterSideTurnEndLate: its amount to its owner (unpowered, dealt by its owner).</summary>
public sealed class DisintegrationPower : TurnEndDebuff
{
    public override IEnumerable<SimState> AfterSideTurnEndLate(SimState state, int owner) =>
        state.TakesPart(owner) ? Hurt(state, owner, Amount(state, owner), Unpowered, selfDealt: true) : One(state);
}

// ---- an enemy's block (Sim/Models/ReactionModels.cs, deleted 2026-11-24)

/// <summary>SneakyPower.AfterCardPlayed: every Attack play by someone else gives its owner its amount of block.</summary>
public sealed class SneakyPower : SimModel
{
    public override TabKind? Tab => TabKind.Trigger;
    public override bool Harmful => true;
    public override IEnumerable<SimState> AfterCardPlayed(SimState state, int owner, SimCardPlay play)
    {
        if (play.Card.Type != SimCardType.Attack || play.Owner == owner || state.Enemy(owner) is not { } enemy) return One(state);
        enemy.Block += enemy.Powers[ThisPower];
        enemy.ReactedWithBlock = true;
        state.LogFired(Name, amount: 0, owner);
        return One(state);
    }
}

// ---- the turn's end: a count of the owner's Frost orbs, the cards in its hand (Sim/Models/EndOfTurnModels.cs, deleted 2026-11-24)

/// <summary>HailstormPower.BeforeSideTurnEnd: its amount to every hittable enemy when its owner has its FrostOrbs (Data) Frost orbs.</summary>
public sealed class HailstormPower : EndOfTurnModel
{
    public override IEnumerable<SimState> BeforeSideTurnEnd(SimState state, int owner)
    {
        if (!state.TakesPart(owner) || state.Player(owner) is not { } player || Amount(state, owner) <= 0) return One(state);
        return player.Orbs.Count(orb => orb.Kind == OrbKind.Frost) >= Data(state, owner) ? DamageAll(state, owner, Amount(state, owner)) : One(state);
    }
}

/// <summary>CloakClasp.BeforeSideTurnEnd: its block (amount) per card in its owner's hand (unpowered).</summary>
public sealed class CloakClasp : EndOfTurnRelic
{
    public override IEnumerable<SimState> BeforeSideTurnEnd(SimState state, int owner)
    {
        if (!state.TakesPart(owner) || state.Player(owner) is not { HandCount: > 0 } player) return One(state);
        return SimEngine.ProcBlock(state, owner, Name, player.HandCount * Amount(state, owner), Unpowered, label: $"{SimEngine.Title(state, owner, Name)} ×{player.HandCount}");
    }
}

/// <summary>ObsidianVice.BeforeSideTurnEnd: its damage (amount) per card in its owner's hand, to one random hittable enemy (unpowered).</summary>
public sealed class ObsidianVice : EndOfTurnRelic
{
    public override IEnumerable<SimState> BeforeSideTurnEnd(SimState state, int owner)
    {
        if (!state.TakesPart(owner) || state.Player(owner) is not { HandCount: > 0 } player || Amount(state, owner) <= 0) return One(state);
        return SimEngine.ProcDamageRandom(state, owner, Name, Amount(state, owner) * player.HandCount, Unpowered, label: $"{SimEngine.Title(state, owner, Name)} ×{player.HandCount}");
    }
}

// ---- a debuff gone with its applier (Sim/Models/EndOfTurnModels.cs, deleted 2026-11-24)

/// <summary>ConstrictPower.AfterSideTurnEnd: its amount to its owner (unpowered, dealt by its owner); it goes when its applier dies (AfterDeath).</summary>
public sealed class ConstrictPower : TurnEndDebuff
{
    public override IEnumerable<SimState> AfterSideTurnEnd(SimState state, int owner) =>
        state.TakesPart(owner) && !ApplierDead(state, owner, ThisPower) ? Hurt(state, owner, Amount(state, owner), Unpowered, selfDealt: true) : One(state);
}
