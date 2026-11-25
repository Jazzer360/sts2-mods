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
        if (cardEvent != CardEvent.Generated || player != owner) return One(state);
        SimEngine.ApplyPower(state, owner, owner, PowerId.Strength, Amount(state, owner), debuff: false);
        return Fire(state, owner);
    }
}

/// <summary>PillarOfCreationPower.AfterCardGeneratedForCombat: block (unpowered) for each card its owner creates.</summary>
public sealed class PillarOfCreationPower : ProcModel
{
    public override TabKind? Tab => TabKind.Block;
    public override IEnumerable<SimState> AfterCardEvent(SimState state, int owner, CardEvent cardEvent, int player) =>
        cardEvent == CardEvent.Generated && player == owner ? Block(state, owner, Amount(state, owner)) : One(state);
}

/// <summary>AllIsWellPower.AfterCardGeneratedForCombat: block (unpowered) for each Sadness its owner creates.</summary>
public sealed class AllIsWellPower : ProcModel
{
    public override TabKind? Tab => TabKind.Block;
    public override IEnumerable<SimState> AfterCardEvent(SimState state, int owner, CardEvent cardEvent, int player) =>
        cardEvent == CardEvent.Generated && state.EventCard?.Name == CardId.Sadness.Name && player == owner ? Block(state, owner, Amount(state, owner)) : One(state);
}

/// <summary>SmokestackPower.AfterCardGeneratedForCombat: its amount to every hittable enemy for each status its owner creates.</summary>
public sealed class SmokestackPower : ProcModel
{
    public override TabKind? Tab => TabKind.Damage;
    public override IEnumerable<SimState> AfterCardEvent(SimState state, int owner, CardEvent cardEvent, int player) =>
        cardEvent == CardEvent.Generated && state.EventCard?.Type == SimCardType.Status && player == owner ? DamageAll(state, owner, Amount(state, owner)) : One(state);
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
        if (cardEvent != CardEvent.Generated || state.EventCard?.Name != CardId.Sadness.Name || player != owner) return One(state);
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
        if (cardEvent != CardEvent.Generated || player != owner || !Armed(state, owner)) return One(state);
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

// ---- the applier's plays (Sim/Models/ProcPowerModels.cs, deleted 2026-11-24)

/// <summary>StranglePower (on an enemy, Data = its applier + 100): each card the applier plays deals its amount to it (unblockable, unpowered, no dealer).</summary>
public sealed class StranglePower : ProcModel
{
    public override TabKind? Tab => TabKind.Damage;
    public override IEnumerable<SimState> BeforeCardPlayed(SimState state, int owner, SimCardPlay play)
    {
        if (DataCreature.In(Data(state, owner)) == play.Owner) Bind(state, owner, Amount(state, owner));
        return One(state);
    }
    public override IEnumerable<SimState> AfterCardPlayed(SimState state, int owner, SimCardPlay play) =>
        TakeBound(state, owner) is { } bound ? SimEngine.ProcDamageOn(state, owner, Name, owner, bound, SimProps.Unblockable | Unpowered, noDealer: true) : One(state);
}

// ---- the hook's creatures (round four: Sim/Models/TargetModels.cs, StatModels.cs, ReactionModels.cs, deleted 2026-11-24)

/// <summary>TrackingPower.ModifyDamageMultiplicative: 1 + amount/100 on the owner's (or pet's) powered hits from a card on a Weak enemy.</summary>
public sealed class TrackingPower : SimModel
{
    public override decimal ModifyDamageMultiplicative(SimState state, int owner, DamageCtx damage)
    {
        if (!damage.Props.IsPoweredAttack() || damage.Card == null) return 1m;
        if (!(damage.Dealer == owner || (owner == Who.You && damage.Dealer == Who.YourOsty))) return 1m;
        return SimEngine.PowersOf(state, damage.Target).Has(PowerId.Weak) ? 1m + SimEngine.PowerOn(state, owner, ThisPower) / 100m : 1m;
    }
}

/// <summary>ColossusPower.ModifyDamageMultiplicative: powered attacks on its owner from a Vulnerable dealer deal its DamageDecrease (0.5).</summary>
public sealed class ColossusPower : SimModel
{
    public override decimal ModifyDamageMultiplicative(SimState state, int owner, DamageCtx damage) =>
        damage.Target == owner && damage.Props.IsPoweredAttack() && damage.Dealer is { } dealer && SimEngine.PowersOf(state, dealer).Has(PowerId.Vulnerable) ? 0.5m : 1m;
}

/// <summary>SerratedArrowsPower.ModifyDamageMultiplicative: 1 + amount/100 on the owner's (or pet's) powered hits on an Impaled enemy.</summary>
public sealed class SerratedArrowsPower : SimModel
{
    public override decimal ModifyDamageMultiplicative(SimState state, int owner, DamageCtx damage)
    {
        if (!damage.Props.IsPoweredAttack() || !(damage.Dealer == owner || (owner == Who.You && damage.Dealer == Who.YourOsty))) return 1m;
        return damage.Target >= 0 && SimEngine.IsImpaled(state, damage.Target) ? 1m + SimEngine.PowerOn(state, owner, ThisPower) / 100m : 1m;
    }
}

/// <summary>ConcoctPower.AfterDamageGiven: a powered hit of its owner's that gets HP through poisons the target (as Envenom, read from its code).</summary>
public sealed class ConcoctPower : SimModel
{
    public override TabKind? Tab => TabKind.Trigger;
    public override void AfterDamageGiven(SimState state, int owner, DamageEvent hit)
    {
        if (hit.Dealer != owner || !hit.Props.IsPoweredAttack() || hit.Unblocked <= 0) return;
        state.LogFired(Name, amount: 0, owner);
        SimEngine.ApplyPower(state, owner, hit.Target, PowerId.Poison, SimEngine.PowerOn(state, owner, ThisPower), debuff: true);
    }
}

// ---- calls into another model's method (round four: Sim/Models/StatModels.cs, deleted 2026-11-24)

/// <summary>
/// VulnerablePower.ModifyDamageMultiplicative: 1.5 on powered attacks on its owner; the dealer's Paper Phrog adds 0.25, the
/// dealer's Cruelty its amount / 100; Debilitate on the owner doubles the bonus (num + (num - 1)).
/// </summary>
public sealed class VulnerablePower : SimModel
{
    public override decimal ModifyDamageMultiplicative(SimState state, int owner, DamageCtx damage)
    {
        if (damage.Target != owner || !damage.Props.IsPoweredAttack()) return 1m;
        var num = 1.5m;
        if (damage.Dealer is { } dealer)
        {
            var player = dealer == Who.YourOsty ? Who.You : dealer;
            if (Who.IsPlayer(player) && SimEngine.RelicsOf(state, player).Has(RelicId.PaperPhrog)) num += 0.25m;
            if (Who.IsPlayer(player) && SimEngine.PowerOn(state, player, PowerId.Cruelty) is var cruelty and not 0) num += cruelty / 100m;
        }
        if (SimEngine.PowersOf(state, owner).Has(PowerId.Debilitate)) num += num - 1m;
        return num;
    }
}

/// <summary>
/// WeakPower.ModifyDamageMultiplicative: 0.75 on its owner's powered attacks; Paper Krane on the target takes 0.15 more off;
/// Debilitate on the owner doubles the reduction (num - (1 - num)).
/// </summary>
public sealed class WeakPower : SimModel
{
    public override decimal ModifyDamageMultiplicative(SimState state, int owner, DamageCtx damage)
    {
        if (damage.Dealer != owner || !damage.Props.IsPoweredAttack()) return 1m;
        var num = 0.75m;
        if (Who.IsPlayer(damage.Target) && SimEngine.RelicsOf(state, damage.Target).Has(RelicId.PaperKrane)) num -= 0.15m;
        if (SimEngine.PowersOf(state, owner).Has(PowerId.Debilitate)) num -= 1m - num;
        return num;
    }
}

// ---- the fight's log (round four: Sim/Models/ProcPowerModels.cs, deleted 2026-11-24)

/// <summary>
/// MotifPower.AfterCardPlayed: its amount of draws for each melody (Wah, Cho, Ki) the card is the first this turn to play
/// (CardPileCmd.Draw(num * Amount)).
/// </summary>
public sealed class MotifPower : ProcModel
{
    public override TabKind? Tab => TabKind.Draw;
    public override IEnumerable<SimState> AfterCardPlayed(SimState state, int owner, SimCardPlay play) =>
        Mine(play, owner) && Melodies.Tags.Count(simCardTag => Melodies.First(state, play, simCardTag)) is var tagCount and > 0
            ? Cause(Fire(state, owner), CardEvent.Drawn, tagCount * Amount(state, owner), owner)
            : One(state);
}

/// <summary>
/// PaleBlueDotPower.AfterCardPlayed: once a turn (Data 1: already), when its owner has finished 5 card plays this turn, draws next
/// turn.
/// </summary>
public sealed class PaleBlueDotPower : ProcModel
{
    /// <summary>The card plays it waits for (PaleBlueDotPower.cardPlayThresholdValue; the game's AttacksPlayedThisTurn counts every play).</summary>
    private const int PlaysNeeded = 5;
    public override IEnumerable<SimState> AfterCardPlayed(SimState state, int owner, SimCardPlay play)
    {
        if (!Mine(play, owner) || UsedThisTurn(state, owner) || SimEngine.TurnOf(state, owner)[SimKeys.Plays(PlayStage.Finished)] < PlaysNeeded) return One(state);
        MarkUsedThisTurn(state, owner);
        return Fire(state, owner);
    }
}

/// <summary>
/// IterationPower.AfterCardDrawn: the first Status its owner draws this turn (the turn's CardDrawnEntry count, this one included:
/// SimPlayer.StatusDrawn) draws its amount more, in its place among the listeners (before relics and Void's own energy loss). No tab.
/// </summary>
public sealed class IterationPower : SimModel
{
    // Frozen with one change: SimPlayer.StatusDrawn, the count it read, is gone; the turn's Status draws are a question to the fight's log now
    // (the read model's), read here as that count.
    private static int StatusDrawn(SimState state, int owner) =>
        SimModels.Get(PowerId.Iteration.Name) is ReadModel read && read.Spec.Values().OfType<HookHistory>().FirstOrDefault() is { } history ? SimEngine.HistoryCount(state, owner, history.Query) ?? 0 : 0;

    public override IEnumerable<SimState> AfterCardEvent(SimState state, int owner, CardEvent cardEvent, int player)
    {
        if (cardEvent != CardEvent.Drawn || player != owner || state.DrawnCard is not { Status: true } || StatusDrawn(state, owner) > 1) return One(state);
        return SimEngine.ProcCardEvent(state, CardEvent.Drawn, count: SimEngine.PowerOn(state, owner, ThisPower), owner, indirect: true);
    }
}

// ---- outcome tabs from a death (round four: Sim/Models/EnemyModels.cs, ProcRelicModels.cs, deleted 2026-11-24)

/// <summary>
/// CrabRagePower (AfterDeath of an ally on its side): it gains Strength (its amount here) and block (SimEnemy.CrabRageBlock), once.
/// </summary>
public sealed class CrabRagePower : SimModel
{
    public override OutcomeRule? OutcomeTab(SimState start, SimOutcome now, int owner) => Who.IsEnemy(owner) ? OutcomeRules.KillsAnother(owner) : null;

    public override void AfterDeath(SimState state, int owner, int dead)
    {
        // Any ally on its side, whatever its own move is (a Buff intent too: a dev review report, Crusher's 99 block missing).
        if (dead == owner || !Who.IsEnemy(dead) || state.Enemy(owner) is not { } enemy || !enemy.Powers.Has(ThisPower)) return;
        state.LogFired(Name, amount: 0, owner);
        var strength = enemy.Powers[ThisPower];
        enemy.Powers = enemy.Powers.Without(ThisPower).Add(PowerId.Strength, strength);
        // Frozen with one change: SimEnemy.CrabRageBlock, the adapter's read of its Block value, is gone: read here as that value.
        enemy.Block += SimEngine.Var(state, owner, Name, "Block", fallback: 99);
    }
}

/// <summary>
/// GremlinHorn.AfterDeath: an enemy's death gives its owner its Energy (PlayerCmd.GainEnergy), then draws its Cards (CardPileCmd.Draw),
/// queued to run once the hit or action that killed it is over (SimEngine.Raise). No tab of its own here: its tab is judged by the
/// play's outcome (a kill in every branch: its OutcomeTab); this model makes the energy and the draw happen in the simulation
/// (the play check compares the energy after each play: a dev-mode report, Unleash and Debilitate killing a segment).
/// </summary>
public sealed class GremlinHorn : SimModel
{
    public override OutcomeRule? OutcomeTab(SimState start, SimOutcome now, int owner) => owner == Who.You ? OutcomeRules.KillsOne : null;

    public override void AfterDeath(SimState state, int owner, int dead)
    {
        if (dead < 0 || !Who.IsPlayer(owner)) return;
        SimEngine.Raise(state, CardEvent.EnergyGained, owner, SimEngine.Var(state, owner, Name, "Energy", fallback: 1));
        SimEngine.Raise(state, CardEvent.Drawn, owner, SimEngine.Var(state, owner, Name, "Cards", fallback: 1));
    }
}

// ---- stars (round four: Sim/Models/EndOfTurnModels.cs, ProcPowerModels.cs, deleted 2026-11-24)

/// <summary>LunarPastry.AfterSideTurnEnd: its owner gains stars (Black Hole reacts: AfterStarsGained).</summary>
public sealed class LunarPastry : EndOfTurnRelic
{
    public override IEnumerable<SimState> AfterSideTurnEnd(SimState state, int owner) =>
        state.TakesPart(owner) ? SimEngine.ProcCardEvent(state, CardEvent.StarsGained, count: 1, owner, indirect: false) : One(state);
}

/// <summary>TheSealedThronePower.BeforeCardPlayed: each card its owner plays gains them stars (Black Hole reacts).</summary>
public sealed class TheSealedThronePower : ProcModel
{
    public override IEnumerable<SimState> BeforeCardPlayed(SimState state, int owner, SimCardPlay play) =>
        Mine(play, owner) && Amount(state, owner) > 0 ? Cause(Fire(state, owner), CardEvent.StarsGained, count: 1, owner) : One(state);
}
/// <summary>ChildOfTheStarsPower.AfterStarsSpent: its amount of block per star spent (one gain).</summary>
public sealed class ChildOfTheStarsPower : ProcModel
{
    public override TabKind? Tab => TabKind.Block;
    public override IEnumerable<SimState> AfterCardEvent(SimState state, int owner, CardEvent cardEvent, int player) =>
        cardEvent == CardEvent.StarsSpent && player == owner && state.CurrentPlay?.Card.Stars is > 0 and var stars
            ? Block(state, owner, Amount(state, owner) * stars) : One(state);
}

/// <summary>GalacticDust.AfterStarsSpent: block for every Stars value of stars spent (Amount: StarsSpent so far, kept below it).</summary>
public sealed class GalacticDust : RelicProc
{
    public override TabKind? Tab => TabKind.Block;
    public override IEnumerable<SimState> AfterCardEvent(SimState state, int owner, CardEvent cardEvent, int player)
    {
        var stars = state.CurrentPlay?.Card.Stars ?? 0;
        if (cardEvent != CardEvent.StarsSpent || player != owner || stars <= 0) return One(state);
        var per = Var(state, owner, "Stars", fallback: 10);
        if (per <= 0) return One(state);
        var spent = Amount(state, owner) + stars;
        Set(state, owner, spent % per, Data(state, owner));
        return spent >= per ? Block(state, owner, spent / per * Var(state, owner, "Block", fallback: 0), Unpowered) : One(state);
    }
}

// ---- the turn number (round four: Sim/Models/EndOfTurnModels.cs, deleted 2026-11-24)

/// <summary>StoneCalendar.BeforeSideTurnEnd: its damage (amount) to every hittable enemy on its DamageTurn (Data: turns until then).</summary>
public sealed class StoneCalendar : EndOfTurnRelic
{
    public override IEnumerable<SimState> BeforeSideTurnEnd(SimState state, int owner) =>
        state.TakesPart(owner) && Data(state, owner) == 0 && Amount(state, owner) > 0 ? DamageAll(state, owner, Amount(state, owner)) : One(state);
}

// ---- own plain fields and data, copies, AfterApplied (round five, deleted 2026-11-24)

/// <summary>
/// ConductorsBaton.AfterCardPlayed: once a turn (RelicUse.UsedThisTurn), the play that completes Wah, Cho and Ki applies
/// ConductorsBatonPower (1) to its owner.
/// </summary>
public sealed class ConductorsBaton : RelicProc
{
    public override IEnumerable<SimState> AfterCardPlayed(SimState state, int owner, SimCardPlay play)
    {
        if (!Mine(play, owner) || UsedThisTurn(state, owner)) return One(state);
        var (before, after) = Melodies.Finished(state, play);
        if (before >= 3 || after < 3) return One(state);
        MarkUsedThisTurn(state, owner);
        SimEngine.ApplyPower(state, owner, owner, PowerId.ConductorsBaton, amount: 1, debuff: false);
        return Fire(state, owner);
    }
}

/// <summary>
/// TheBombPower.BeforeSideTurnEnd: ticks down (amount: turns left); on its last its Damage to every hittable enemy, then it goes.
/// Each Bomb played is a copy of its own (instanced) with its own turns and Damage, each ticking on its own: the copies listed in
/// SimPlayer.Counters (Left: turns, Amount: Damage), or with none listed its Powers entry (amount: turns, Data: Damage).
/// </summary>
public sealed class TheBombPower : EndOfTurnModel
{
    public override bool Instanced => true;

    public override IEnumerable<SimState> BeforeSideTurnEnd(SimState state, int owner)
    {
        if (!state.TakesPart(owner)) return One(state);
        var listed = CounterCopies.Listed(state, owner, ThisPower);
        var copies = listed ?? new List<CounterCopy> { new(ThisPower, Left: Amount(state, owner), Amount: Data(state, owner)) };
        var ticking = copies.Where(copy => copy.Left > 1).Select(copy => copy with { Left = copy.Left - 1 }).ToList();
        var powers = SimEngine.PowersOf(state, owner);
        SimEngine.SetPowers(state, owner, ticking.Count == 0 ? powers.Without(ThisPower) : powers.With(ThisPower, ticking.Sum(copy => copy.Left), ticking[^1].Amount));
        if (listed != null) CounterCopies.SetListed(state, owner, ThisPower, ticking);
        // Each copy on its last turn goes off in the order the game holds them.
        IEnumerable<SimState> states = One(state);
        foreach (var copy in copies.Where(copy => copy.Left <= 1 && copy.Amount > 0))
            states = states.SelectMany(branch => DamageAll(branch, owner, copy.Amount)).ToList();
        return states;
    }

    /// <summary>
    /// A Bomb played (TheBomb.OnPlay: its Turns, then SetDamage) lands beside the ones before it. Its Damage is set after it lands,
    /// outside what the play carries: 0 here, as it can't go off before its turns (3) run out, past the turn simulated.
    /// </summary>
    public override void AfterApplied(SimState state, int owner, int applier, int amount)
    {
        if (state.Player(owner) is null) return;
        var before = CounterCopies.Listed(state, owner, ThisPower)
                     ?? (Amount(state, owner) > amount ? new List<CounterCopy> { new(ThisPower, Left: Amount(state, owner) - amount, Amount: Data(state, owner)) } : null);
        if (before == null) return; // the first: its entry is the copy
        CounterCopies.SetListed(state, owner, ThisPower, before.Append(new CounterCopy(ThisPower, Left: amount, Amount: 0)));
    }
}

/// <summary>
/// EchoFormPower.ModifyCardPlayCount: a card its owner plays is played once more while fewer than its amount of their card plays this
/// turn were the first of their series (the fight's log, decision #191: CardPlayStartedEntry, IsFirstInSeries). Its tag is the
/// double-play tag (CardPlays), not a proc: it logs no firing.
/// </summary>
public sealed class EchoFormPower : SimModel
{
    /// <summary>EchoFormPower.ModifyCardPlayCount's question: CardPlayStartedEntry, this turn, its owner's, IsFirstInSeries.</summary>
    public static readonly HistoryQuery FirstPlaysStarted =
        new(HistoryKind.CardPlayStarted, HistoryScope.ThisTurn, HistoryWhose.Owner) { Details = HistoryDetail.FirstInSeries };

    public override IReadOnlyList<HistoryQuery> History => new[] { FirstPlaysStarted };

    public override int ModifyCardPlayCount(SimState state, int owner, SimCardPlay play, int count)
    {
        if (play.Owner != owner) return count;
        if (SimEngine.HistoryCount(state, owner, FirstPlaysStarted) is not { } started)
        {
            state.Unmodelled ??= SimEngine.HistoryUnknown;
            return count;
        }
        return started >= SimEngine.PowerOn(state, owner, ThisPower) ? count : count + 1;
    }
}

/// <summary>AutomationPower.AfterCardDrawn: each copy gives its amount of energy for every 10 cards drawn (AutomationPower._baseCardsLeft).</summary>
public sealed class AutomationPower : EventCountdownPower
{
    public override TabKind? Tab => TabKind.Energy;
    protected override CardEvent Event => CardEvent.Drawn;
    protected override int Period => 10;
    protected override IEnumerable<SimState> OnZero(SimState state, int owner, int amount) => Cause(Fire(state, owner), CardEvent.EnergyGained, amount, owner);
}

/// <summary>CacophonyPower.AfterCardDrawn: each copy deals its amount to a random hittable enemy for every 33 cards drawn (its Cards reset).</summary>
public sealed class CacophonyPower : EventCountdownPower
{
    public override TabKind? Tab => TabKind.Damage;
    protected override CardEvent Event => CardEvent.Drawn;
    protected override int Period => 33;
    protected override IEnumerable<SimState> OnZero(SimState state, int owner, int amount) => DamageRandom(state, owner, amount);
}

/// <summary>
/// PanachePower.AfterCardPlayed: every card its owner plays counts its CardsLeft down (Data; 0 = just applied: the card that
/// applied it doesn't count); at 0, its amount to every hittable enemy (unpowered), and the count starts over at 5.
/// </summary>
public sealed class PanachePower : CounterPower
{
    /// <summary>Where its count starts (PanachePower._baseCardsLeft).</summary>
    private const int CardsLeftAtStart = 5;
    public override TabKind? Tab => TabKind.Damage;
    public override int StartCount => 0;
    public override IEnumerable<SimState> AfterCardPlayed(SimState state, int owner, SimCardPlay play)
    {
        if (!Mine(play, owner)) return One(state);
        var damage = new List<int>();
        var next = Copies(state, owner).Select(copy =>
        {
            if (copy.Left <= 0) return copy with { Left = CardsLeftAtStart }; // just applied: this card doesn't count
            if (copy.Left > 1) return copy with { Left = copy.Left - 1 };
            damage.Add(copy.Amount);
            return copy with { Left = CardsLeftAtStart };
        }).ToList();
        SetCopies(state, owner, next);
        IEnumerable<SimState> states = One(state);
        foreach (var amount in damage) states = states.SelectMany(branch => DamageAll(branch, owner, amount)).ToList();
        return states;
    }
}

/// <summary>OrbitPower.AfterEnergySpent: each copy gives its amount of energy for every 4 energy spent (OrbitPower._energyIncrement).</summary>
public sealed class OrbitPower : EventCountdownPower
{
    public override TabKind? Tab => TabKind.Energy;
    protected override CardEvent Event => CardEvent.EnergySpent;
    protected override int Period => 4;
    protected override IEnumerable<SimState> OnZero(SimState state, int owner, int amount) => Cause(Fire(state, owner), CardEvent.EnergyGained, amount, owner);
}

/// <summary>
/// MonologuePower: each card its owner plays gives Strength (its Strength value, DynamicVars.Strength kept as "StrengthPower", bound
/// at the play's start). Each Monologue played is a copy of its own (instanced), each binding and giving its own Strength: two
/// copies give 2 a card, so it binds the total over its copies.
/// </summary>
public sealed class MonologuePower : BoundPower
{
    public override bool Instanced => true;
    protected override int BoundAmount(SimState state, int owner) => VarTotal(state, owner, "StrengthPower", fallback: 1);
    protected override IEnumerable<SimState> Act(SimState state, int owner, SimCardPlay play, int amount)
    {
        SimEngine.ApplyPower(state, owner, owner, PowerId.Strength, amount, debuff: false);
        return Fire(state, owner);
    }
}

/// <summary>A power counting card events down (its DisplayAmount, Data) that fires when the count runs out.</summary>
public abstract class EventCountdownPower : CounterPower
{
    protected abstract CardEvent Event { get; }
    /// <summary>Where a copy's count starts, and starts again once it gets there.</summary>
    protected abstract int Period { get; }
    public override int StartCount => Period;
    /// <summary>A copy got there: what it gives, its own amount.</summary>
    protected abstract IEnumerable<SimState> OnZero(SimState state, int owner, int amount);
    public override IEnumerable<SimState> AfterCardEvent(SimState state, int owner, CardEvent cardEvent, int player)
    {
        if (cardEvent != Event || player != owner) return One(state);
        var triggered = new List<int>();
        var next = Copies(state, owner).Select(copy =>
        {
            if (copy.Left <= 0) return copy; // its count isn't known
            if (copy.Left > 1) return copy with { Left = copy.Left - 1 };
            triggered.Add(copy.Amount);
            return copy with { Left = Period };
        }).ToList();
        SetCopies(state, owner, next);
        // Each copy that got there acts on its own, in the order the game holds them.
        IEnumerable<SimState> states = One(state);
        foreach (var amount in triggered) states = states.SelectMany(branch => OnZero(branch, owner, amount)).ToList();
        return states;
    }
}

/// <summary>
/// A power each application of which is a copy of its own (PowerInstanceType.Instanced), counting on its own: SimPlayer.Counters
/// holds the copies, or, with none listed, its entry in Powers is the one copy (amount, Data: what's left). SimReaders.Counters
/// reads each copy from the game; a copy applied during a simulation starts from StartCount (SimEngine.ApplyPower).
/// </summary>
public abstract class CounterPower : ProcModel
{
    public override bool Instanced => true;

    /// <summary>A new copy's count (Panache: 0, just applied: the card that applied it doesn't count).</summary>
    public abstract int StartCount { get; }

    protected IReadOnlyList<CounterCopy> Copies(SimState state, int owner) =>
        CounterCopies.Listed(state, owner, ThisPower) ?? new List<CounterCopy> { new(ThisPower, Data(state, owner), Amount(state, owner)) };

    protected void SetCopies(SimState state, int owner, IReadOnlyList<CounterCopy> copies)
    {
        if (CounterCopies.Listed(state, owner, ThisPower) != null)
        {
            CounterCopies.SetListed(state, owner, ThisPower, copies);
            return;
        }
        Set(state, owner, copies[0].Amount, copies[0].Left);
    }

}

/// <summary>One copy of a counter power (it was SimPlayer.Counters'): what it still has to count, and what it gives when it gets there.</summary>
public sealed record CounterCopy(PowerId Power, int Left, int Amount);

/// <summary>
/// The copies of an instanced power the frozen counter models list beside its Powers entry, each with its own count and amount; none
/// listed: its Powers entry is the one copy. The simulator keeps a read power's copies in its owner's fields (decision #196), so these
/// frozen models keep their list in the owner's fields too ("Power#count", "Power#i.Left", "Power#i.Amount"), which the parity test leaves
/// out of the branch key as it leaves out every model's fields. (They were SimPlayer.Counters, which only these models used.)
/// </summary>
internal static class CounterCopies
{
    private static string Count(PowerId power) => power.Name + "#count";
    private static string Left(PowerId power, int index) => $"{power.Name}#{index}.Left";
    private static string Amount(PowerId power, int index) => $"{power.Name}#{index}.Amount";

    public static List<CounterCopy>? Listed(SimState state, int owner, PowerId power)
    {
        if (state.Player(owner) is not { } player || !player.Fields.Has(Count(power))) return null;
        return Enumerable.Range(0, player.Fields[Count(power)]).Select(index => new CounterCopy(power, player.Fields[Left(power, index)], player.Fields[Amount(power, index)])).ToList();
    }

    /// <summary>Its copies now (none: it's gone), in place of the ones listed.</summary>
    public static void SetListed(SimState state, int owner, PowerId power, IEnumerable<CounterCopy> copies)
    {
        if (state.Player(owner) is null) return;
        var list = copies.ToList();
        var fields = SimEngine.FieldsOf(state, owner);
        foreach (var key in fields.Items.Select(item => item.Name).Where(name => name.StartsWith(power.Name + "#", StringComparison.Ordinal)).ToList()) fields = fields.Without(key);
        if (list.Count > 0)
        {
            fields = fields.With(Count(power), list.Count);
            for (var index = 0; index < list.Count; index++) fields = fields.With(Left(power, index), list[index].Left).With(Amount(power, index), list[index].Amount);
        }
        SimEngine.SetFields(state, owner, fields);
    }
}

// ---- the enemies' turn start (round six, deleted 2026-11-24)

/// <summary>
/// ConductorsBatonPower.ModifyDamageMultiplicative: its DamageDecrease (0.75) on powered attacks on its owner; it goes at the
/// start of the owner's next turn (AfterSideTurnStart), so it lasts through the enemy turn.
/// </summary>
public sealed class ConductorsBatonPower : SimModel
{
    public override decimal ModifyDamageMultiplicative(SimState state, int owner, DamageCtx damage) =>
        damage.Target == owner && damage.Props.IsPoweredAttack() ? 0.75m : 1m;
}

/// <summary>LullabyPower.AfterCardPlayed: once a turn (Data 1: already), the play that completes Wah, Cho and Ki gains its amount of block.</summary>
public sealed class LullabyPower : ProcModel
{
    public override TabKind? Tab => TabKind.Block;
    public override IEnumerable<SimState> AfterCardPlayed(SimState state, int owner, SimCardPlay play)
    {
        if (!Mine(play, owner) || UsedThisTurn(state, owner)) return One(state);
        var (before, after) = Melodies.Finished(state, play);
        if (before >= 3 || after < 3) return One(state);
        MarkUsedThisTurn(state, owner);
        return Block(state, owner, Amount(state, owner));
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

// ---- the card's facts (round seven: Sim/Models/ProcPowerModels.cs, deleted 2026-11-24)

/// <summary>PagestormPower.AfterCardDrawn: each Ethereal card its owner draws draws its amount more, in its place among the listeners. No tab.</summary>
public sealed class PagestormPower : SimModel
{
    public override IEnumerable<SimState> AfterCardEvent(SimState state, int owner, CardEvent cardEvent, int player) =>
        cardEvent == CardEvent.Drawn && player == owner && state.DrawnCard is { Ethereal: true }
            ? SimEngine.ProcCardEvent(state, CardEvent.Drawn, count: SimEngine.PowerOn(state, owner, ThisPower), owner, indirect: true)
            : One(state);
}

// ---- a power applied by its applier (round seven: Sim/Models/ProcPowerModels.cs, deleted 2026-11-24)

/// <summary>OblivionPower (on an enemy, Data = its applier + 100): each card the applier plays applies Doom to it.</summary>
public sealed class OblivionPower : ProcModel
{
    public override IEnumerable<SimState> BeforeCardPlayed(SimState state, int owner, SimCardPlay play)
    {
        if (DataCreature.In(Data(state, owner)) == play.Owner) Bind(state, owner, Amount(state, owner));
        return One(state);
    }
    public override IEnumerable<SimState> AfterCardPlayed(SimState state, int owner, SimCardPlay play) =>
        TakeBound(state, owner) is not null ? Fire(state, owner) : One(state);
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

// ---- the hit's result (round seven: Sim/Models/StatModels.cs, deleted 2026-11-24)

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

/// <summary>ReaperFormPower.AfterDamageGiven: its owner's powered hits that get HP through apply Doom.</summary>
public sealed class ReaperFormPower : ProcModel
{
    public override void AfterDamageGiven(SimState state, int owner, DamageEvent hit)
    {
        if (hit.Dealer == owner && hit.Props.IsPoweredAttack() && hit.Unblocked > 0 && hit.Target >= 0) state.LogFired(Name, amount: 0, owner);
    }
}

// ---- orbs (round seven: Sim/Models/ProcRelicModels.cs, deleted 2026-11-24)

/// <summary>Metronome.AfterOrbChanneled: damage to every hittable enemy once its OrbCount orbs are channeled this combat (Amount: OrbsChanneled).</summary>
public sealed class Metronome : RelicProc
{
    public override TabKind? Tab => TabKind.Damage;
    public override IEnumerable<SimState> AfterOrbChanneled(SimState state, int owner, int player)
    {
        if (player != owner) return One(state);
        var needed = Var(state, owner, "OrbCount", fallback: 7);
        var count = Amount(state, owner);
        if (count >= needed) return One(state);
        Set(state, owner, count + 1, Data(state, owner));
        return count + 1 == needed ? RelicDamageAll(state, owner) : One(state);
    }
}

// ---- the card's facts: an Ethereal exhaust at the turn's end (Sim/Models/ProcPowerModels.cs, deleted 2026-11-24)

/// <summary>DarkEmbracePower.AfterCardExhausted: for each card its owner exhausts, draws its amount (CardPileCmd.Draw(Amount)).</summary>
public sealed class DarkEmbracePower : ProcModel
{
    public override TabKind? Tab => TabKind.Draw;
    public override IEnumerable<SimState> AfterCardEvent(SimState state, int owner, CardEvent cardEvent, int player) =>
        cardEvent == CardEvent.Exhausted && player == owner ? Cause(Fire(state, owner), CardEvent.Drawn, count: Amount(state, owner), owner) : One(state);
}

// ---- the HP-loss pipeline (Sim/Models/PlayerModels.cs and ReactionModels.cs, deleted 2026-11-24)

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

/// <summary>The Boot (relic, amount = DamageMinimum): its owner's (or Osty's) powered hits that lose another creature 1+ HP lose it at least that. Its tab: a play with a hit it raises (one that gets past block for less than the minimum: 6 into 5 block loses the enemy 5, not 1).</summary>
public sealed class TheBoot : SimModel
{
    public override TabKind? Tab => TabKind.Trigger;

    public override decimal ModifyHpLostAfterOstyLate(SimState state, int owner, DamageCtx damage, decimal amount)
    {
        if (!(damage.Dealer == owner || (owner == Who.You && damage.Dealer == Who.YourOsty)) || damage.Target == owner) return amount;
        if (!damage.Props.IsPoweredAttack() || amount < 1m) return amount;
        var min = SimEngine.RelicsOf(state, owner)[ThisRelic];
        if (amount >= min) return amount;
        state.LogFired(Name, amount: 0, owner);
        return min;
    }
}

/// <summary>
/// JossPaper.AfterCardExhausted: its owner's exhausts count toward its draws (Amount: CardsExhausted), a draw for each ExhaustAmount; an Ethereal
/// card its owner's turn end exhausts (causedByEthereal: SimState.EtherealNow) counts in its EtherealCount (Data) instead, added as the turn ends
/// (AfterSideTurnEnd), then the draws if the count is met: after the cards in hand acted. Frozen as it was when its code was read (decision #214: a let
/// keeps CardsExhausted before the store that takes the draws off).
/// </summary>
public sealed class JossPaper : RelicProc
{
    public override TabKind? Tab => TabKind.Draw;
    public override IEnumerable<SimState> AfterCardEvent(SimState state, int owner, CardEvent cardEvent, int player)
    {
        if (cardEvent != CardEvent.Exhausted || player != owner) return One(state);
        if (state.EtherealNow)
        {
            Set(state, owner, Amount(state, owner), Data(state, owner) + 1);
            return One(state);
        }
        return DrawIfMet(state, owner, Amount(state, owner) + 1, Data(state, owner));
    }

    public override IEnumerable<SimState> AfterSideTurnEnd(SimState state, int owner) =>
        state.TakesPart(owner) && Data(state, owner) > 0 ? DrawIfMet(state, owner, Amount(state, owner) + Data(state, owner), ethereal: 0) : One(state);

    /// <summary>DrawIfThresholdMet: at ExhaustAmount or more, the count goes to its remainder and a card is drawn for each ExhaustAmount.</summary>
    private IEnumerable<SimState> DrawIfMet(SimState state, int owner, int count, int ethereal)
    {
        var needed = Math.Max(1, Var(state, owner, "ExhaustAmount", fallback: 5));
        if (count < needed) { Set(state, owner, count, ethereal); return One(state); }
        Set(state, owner, count % needed, ethereal);
        return Cause(Fire(state, owner), CardEvent.Drawn, count: count / needed, owner);
    }
}


/// <summary>HauntPower.AfterCardPlayed: each Soul its owner plays deals its amount to a random hittable enemy (unblockable, unpowered, no dealer).</summary>
public sealed class HauntPower : ProcModel
{
    public override TabKind? Tab => TabKind.Damage;
    public override IEnumerable<SimState> AfterCardPlayed(SimState state, int owner, SimCardPlay play) =>
        Mine(play, owner) && play.Card.Name == CardId.Soul.Name
            ? SimEngine.ProcDamageRandom(state, owner, Name, Amount(state, owner), SimProps.Unblockable | Unpowered, noDealer: true)
            : One(state);
}


/// <summary>
/// CentennialPuzzle.AfterDamageReceived: the first time this combat its owner takes damage past their block (UnblockedDamage &gt; 0),
/// draws its Cards (3), one at a time, queued until the hit is over (SimEngine.Raise). Used up for the combat (UsedThisCombat: the
/// relic's amount here). A draw tab: the cards it really draws (decision #132).
/// </summary>
public sealed class CentennialPuzzle : RelicProc
{
    public override TabKind? Tab => TabKind.Draw;
    public override void AfterDamageReceived(SimState state, int owner, DamageEvent hit)
    {
        if (hit.Target != owner || hit.Unblocked <= 0 || !Who.IsPlayer(owner) || Amount(state, owner) != 0) return;
        Set(state, owner, amount: 1, Data(state, owner));
        state.LogFired(Name, amount: 0, owner);
        SimEngine.Raise(state, CardEvent.Drawn, owner, SimEngine.Var(state, owner, Name, "Cards", fallback: 3), Name);
    }
}


// ---- a card changed in its play (Sim/Models/ProcRelicModels.cs and ProcPowerModels.cs, deleted 2026-11-24)

/// <summary>
/// RazorTooth.AfterCardPlayed: an Attack or Skill you play that can be upgraded is upgraded (CardCmd.Upgrade): a replay's later plays are the
/// upgraded card's (SimEngine.UpgradeCardInPlay), which can't be upgraded again.
/// </summary>
public sealed class RazorTooth : RelicProc
{
    public override bool UpgradesCardInPlay => true;
    public override IEnumerable<SimState> AfterCardPlayed(SimState state, int owner, SimCardPlay play)
    {
        if (!Mine(play, owner) || play.Card.Type is not (SimCardType.Attack or SimCardType.Skill) || !play.Card.Upgradable) return One(state);
        SimEngine.UpgradeCardInPlay(state);
        return Fire(state, owner);
    }
}

/// <summary>MasterPlannerPower.AfterCardPlayed: each Skill its owner plays gains Sly.</summary>
public sealed class MasterPlannerPower : ProcModel
{
    public override IEnumerable<SimState> AfterCardPlayed(SimState state, int owner, SimCardPlay play) =>
        Mine(play, owner) && play.Card.Type == SimCardType.Skill ? Fire(state, owner) : One(state);
}

// ---- a command on the player who played (Sim/Models/ProcPowerModels.cs, deleted 2026-11-24)

/// <summary>GalvanicPower.AfterCardPlayed: a Galvanized card deals its amount to the player who played it (unpowered Move, no dealer).</summary>
public sealed class GalvanicPower : ProcModel
{
    public override TabKind? Tab => TabKind.Damage;
    public override bool Harmful => true;
    public override IEnumerable<SimState> AfterCardPlayed(SimState state, int owner, SimCardPlay play) =>
        play.Card.Affliction == SimAffliction.Galvanized && Who.IsPlayer(play.Owner)
            ? SimEngine.ProcDamagePlayer(state, owner, Name, play.Owner, Amount(state, owner), Unpowered | SimProps.Move, noDealer: true)
            : One(state);
}

// ---- a power on the player who played (Sim/Models/EnemyModels.cs, deleted 2026-11-24)

/// <summary>VitalSparkPower.AfterCardPlayed: playing a Tainted card gives its player Tainted (its amount: more damage from powered attacks).</summary>
public sealed class VitalSparkPower : SimModel
{
    public override TabKind? Tab => TabKind.Trigger;
    public override bool Harmful => true;
    public override IEnumerable<SimState> AfterCardPlayed(SimState state, int owner, SimCardPlay play)
    {
        if (play.Card.Affliction != SimAffliction.Tainted) return One(state);
        state.LogFired(Name, amount: 0, owner);
        return SimEngine.ApplyAndTell(state, owner, play.Owner, PowerId.Tainted, SimEngine.PowerOn(state, owner, ThisPower), debuff: false);
    }
}

// ---- a card in the hand made free (Sim/Models/ProcRelicModels.cs, deleted 2026-11-24)

/// <summary>
/// MummifiedHand.AfterCardPlayed: each Power you play makes a random other card in your hand free this turn (it prefers one that
/// still costs something; with none, nothing changes).
/// </summary>
public sealed class MummifiedHand : RelicProc
{
    public override IEnumerable<SimState> AfterCardPlayed(SimState state, int owner, SimCardPlay play) =>
        Mine(play, owner) && play.Card.Type == SimCardType.Power && play.Card.OthersInHandCost ? Fire(state, owner) : One(state);
}

// ---- a copy of the card played made (Sim/Models/ProcRelicModels.cs, deleted 2026-11-24)

/// <summary>MusicBox: once a turn, the first Attack you play (bound at BeforeCardPlayed) is copied into your hand, Ethereal.</summary>
public sealed class MusicBox : RelicProc
{
    public override IEnumerable<SimState> BeforeCardPlayed(SimState state, int owner, SimCardPlay play)
    {
        if (Mine(play, owner) && Armed(state, owner) && play.Card.Type == SimCardType.Attack && !state.Bound.Has(SimKeys.Owned(Name, owner))) Bind(state, owner, amount: 1);
        return One(state);
    }
    public override IEnumerable<SimState> AfterCardPlayed(SimState state, int owner, SimCardPlay play)
    {
        if (!Mine(play, owner) || TakeBound(state, owner) is null) return One(state);
        Disarm(state, owner);
        return MakeInHand(Fire(state, owner), play.Card, count: 1, owner, ethereal: true); // a clone of the Attack played
    }
}

// ---- a flag of the props (Sim/Models/BlockModels.cs, deleted 2026-11-24)

/// <summary>NoBlockPower.ModifyBlockMultiplicative: its owner gains no block from cards (anything not Unpowered).</summary>
public sealed class NoBlockPower : SimModel
{
    public override decimal ModifyBlockMultiplicative(SimState state, int owner, BlockCtx block) =>
        block.Target == owner && !block.Props.HasFlag(SimProps.Unpowered) && block.Card != null ? 0m : 1m;
}

// ---- an attack's hooks (Sim/Models/StatModels.cs and ProcRelicModels.cs, deleted 2026-11-24)

/// <summary>
/// Powers bound to one attack (BeforeAttack binds the first attack that qualifies, AfterAttack spends them). Data is the bound
/// attack's id and the amount when it started: (id &lt;&lt; 16) | amount. (Sim/Models/StatModels.cs' AttackBoundPower, as the frozen models had it.)
/// </summary>
public abstract class AttackBoundPower : SimModel
{
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

/// <summary>BoneFlute.AfterAttack: block for each attack your Osty makes.</summary>
public sealed class BoneFlute : RelicProc
{
    public override TabKind? Tab => TabKind.Block;
    public override IEnumerable<SimState> AfterAttack(SimState state, int owner, AttackInfo attack) =>
        owner == Who.You && attack.Dealer == Who.YourOsty && state.You.OstyHp > 0 ? RelicBlock(state, owner) : One(state);
}

// ---- the damage cap and AfterModifyingDamageAmount (Sim/Models/TargetModels.cs and StatModels.cs, deleted 2026-11-24)

/// <summary>HardToKillPower.ModifyDamageCap: at most its amount per damage to its owner; it flashes when it caps.</summary>
public sealed class HardToKillPower : SimModel
{
    public override bool FlashesOnDamageModified => true;
    public override decimal ModifyDamageCap(SimState state, int owner, DamageCtx damage) => damage.Target == owner ? SimEngine.PowerOn(state, owner, ThisPower) : decimal.MaxValue;
}

/// <summary>IntangiblePower.ModifyDamageCap: at most 1 per damage to its owner (its HP-loss cap is in the HP-loss pipeline).</summary>
public sealed class IntangiblePower : SimModel
{
    public override bool FlashesOnDamageModified => true;
    public override decimal ModifyDamageCap(SimState state, int owner, DamageCtx damage) => damage.Target == owner ? 1m : decimal.MaxValue;
    /// <summary>ModifyHpLostAfterOsty: at most 1 HP per damage to its owner (it flashes when it changes one).</summary>
    public override decimal ModifyHpLostAfterOsty(SimState state, int owner, DamageCtx damage, decimal amount) =>
        damage.Target != owner || amount < 1m ? amount : 1m;
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

// ---- multiplayer scaling never called (Sim/Models/PlayerModels.cs, deleted 2026-11-24)

/// <summary>
/// BufferPower.ModifyHpLostAfterOstyLate: HP its owner would lose becomes 0 (beta: not from Brightest Flame); when that changes the amount, it
/// loses a stack (AfterModifyingHpLostAfterOsty: Decrement) instead of flashing. (Main's way, MainRule.BufferStopsBrightestFlame, went with it:
/// each branch's own code is read now; the parity board plays beta's, as the installed game is.)
/// </summary>
public sealed class BufferPower : SimModel
{
    public override decimal ModifyHpLostAfterOstyLate(SimState state, int owner, DamageCtx damage, decimal amount) =>
        damage.Target != owner || damage.Card?.Name == CardId.BrightestFlame.Name ? amount : 0m;
    public override void AfterModifyingHpLost(SimState state, int owner, bool afterOsty)
    {
        var powers = SimEngine.PowersOf(state, owner);
        SimEngine.SetPowers(state, owner, powers[ThisPower] <= 1 ? powers.Without(ThisPower) : powers.With(ThisPower, powers[ThisPower] - 1));
        state.LogFired(Name, amount: 0, owner);
    }
}

// ---- where the damage's card is (Sim/Models/StatModels.cs, deleted 2026-11-24)

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

// ---- a card's own method (Sim/Models/StatModels.cs, deleted 2026-11-24)

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

// ---- an arrow's owner (round fourteen: Sim/Models/ProcPowerModels.cs, ProcRelicModels.cs, deleted 2026-11-24)

/// <summary>YahPower.AfterArrowDislodged: energy the first time each turn (Data 1: already) one of your arrows comes loose.</summary>
public sealed class YahPower : ProcModel
{
    public override TabKind? Tab => TabKind.Energy;
    public override IEnumerable<SimState> AfterCardEvent(SimState state, int owner, CardEvent cardEvent, int player)
    {
        if (cardEvent != CardEvent.ArrowDislodged || player != owner || UsedThisTurn(state, owner)) return One(state);
        MarkUsedThisTurn(state, owner);
        return Cause(Fire(state, owner), CardEvent.EnergyGained, Amount(state, owner), owner);
    }
}

/// <summary>Twine.AfterArrowDislodged: block for each of your arrows that comes loose.</summary>
public sealed class Twine : RelicProc
{
    public override TabKind? Tab => TabKind.Block;
    public override IEnumerable<SimState> AfterCardEvent(SimState state, int owner, CardEvent cardEvent, int player) =>
        cardEvent == CardEvent.ArrowDislodged && player == owner ? RelicBlock(state, owner) : One(state);
}

// ---- a hit's reactions (Sim/Models/EnemyModels.cs, deleted 2026-11-24)

/// <summary>
/// MonarchsGazePower.AfterDamageGiven: each powered hit its owner deals takes its amount of Strength from the target for the turn
/// (MonarchsGazeStrengthDownPower: a temporary Strength loss; Artifact can take it).
/// </summary>
public sealed class MonarchsGazePower : SimModel
{
    public override TabKind? Tab => TabKind.Trigger;
    public override void AfterDamageGiven(SimState state, int owner, DamageEvent hit)
    {
        if (hit.Dealer != owner || !hit.Props.IsPoweredAttack() || hit.Target < 0) return;
        state.LogFired(Name, amount: 0, owner);
        SimEngine.ApplyInHit(state, owner, hit.Target, PowerId.Strength, -SimEngine.PowerOn(state, owner, ThisPower), debuff: true);
    }
}

// ---- reactions to losing HP (Sim/Models/OutcomeModels.cs and ReactionModels.cs, deleted 2026-11-24; Rupture and their base with OutcomeRules.YouLoseHp
// in round sixteen)

/// <summary>The outcome rules only the frozen models still use (Sim/Models/OutcomeModels.cs' OutcomeRules, as they were).</summary>
public static class FrozenRules
{
    /// <summary>You lose HP during the play (its own HP cost, Thorns hitting back), in every branch of an aim: a bonus.</summary>
    public static readonly OutcomeRule YouLoseHp = new(Harmful: false, outcome => outcome.PlayHpLossesMin > 0);
}

/// <summary>A relic or power of yours that reacts to HP you lose (AfterDamageReceived with UnblockedDamage &gt; 0, after Tungsten Rod).</summary>
public abstract class ReactsToYourHpLoss : SimModel
{
    public override OutcomeRule? OutcomeTab(SimState start, SimOutcome now, int owner) => owner == Who.You ? FrozenRules.YouLoseHp : null;
}

/// <summary>RupturePower: Strength when you lose HP on your turn.</summary>
public sealed class RupturePower : ReactsToYourHpLoss { }

/// <summary>SelfFormingClay: block next turn.</summary>
public sealed class SelfFormingClay : ReactsToYourHpLoss { }

/// <summary>EmotionChip: the front orb's passive next turn.</summary>
public sealed class EmotionChip : ReactsToYourHpLoss { }

/// <summary>
/// DemonTongue.AfterDamageReceived: once a turn (RelicUse.UsedThisTurn), on its owner's side's turn, heals the HP they just lost
/// (the HP-loss modifiers already applied).
/// </summary>
public sealed class DemonTongue : SimModel
{
    public override OutcomeRule? OutcomeTab(SimState start, SimOutcome now, int owner) =>
        owner == Who.You && !((RelicUse)SimEngine.RelicsOf(start, owner).DataOf(ThisRelic)).HasFlag(RelicUse.UsedThisTurn) ? FrozenRules.YouLoseHp : null;
    public override void AfterDamageReceived(SimState state, int owner, DamageEvent hit)
    {
        if (hit.Target != owner || hit.Unblocked <= 0 || state.EnemyTurnNow || state.Player(owner) is not { Died: false, Hp: < int.MaxValue }) return;
        var relics = SimEngine.RelicsOf(state, owner);
        if (((RelicUse)relics.DataOf(ThisRelic)).HasFlag(RelicUse.UsedThisTurn)) return;
        SimEngine.SetRelics(state, owner, relics.With(ThisRelic, relics[ThisRelic], relics.DataOf(ThisRelic) | (int)RelicUse.UsedThisTurn));
        var player = state.MutablePlayer(owner);
        player.Hp = (int)Math.Min(player.MaxHp, (long)player.Hp + hit.Unblocked);
        state.LogFired(Name, hit.Unblocked, owner);
    }
}

/// <summary>SlipperyPower: at most 1 HP per damage to its owner (AfterOsty); each that loses HP flashes and takes a charge.</summary>
public sealed class SlipperyPower : SimModel
{
    public override OutcomeRule? OutcomeTab(SimState start, SimOutcome now, int owner) =>
        Who.IsEnemy(owner) && !now.InSome(branch => Triggered(branch, owner)) ? new(Harmful: true, outcome => outcome.InSome(branch => Triggered(branch, owner)), branch => Triggered(branch, owner)) : null;

    /// <summary>Its enemy's flag that a hit took HP through it (SimEnemy.SlipperyTriggered, the engine's until it went: kept in its owner's fields now).</summary>
    private const string TriggeredField = "SlipperyPower.Triggered";

    private static bool Triggered(SimState branch, int owner) => SimEngine.FieldsOf(branch, owner).Has(TriggeredField);

    public override decimal ModifyHpLostAfterOsty(SimState state, int owner, DamageCtx damage, decimal amount) =>
        damage.Target != owner || amount < 1m ? amount : 1m;
    public override void AfterModifyingHpLost(SimState state, int owner, bool afterOsty) { } // it flashes when the damage lands
    public override void AfterDamageReceived(SimState state, int owner, DamageEvent hit)
    {
        if (hit.Target != owner || hit.Unblocked < 1) return;
        state.LogFired(Name, amount: 0, owner);
        var powers = SimEngine.PowersOf(state, owner);
        SimEngine.SetPowers(state, owner, powers[ThisPower] <= 1 ? powers.Without(ThisPower) : powers.With(ThisPower, powers[ThisPower] - 1));
        if (state.Enemy(owner) != null) SimEngine.SetField(state, owner, TriggeredField, 1);
    }
}

// ---- a bonus a play gives up or sets up (Sim/Models/ProcRelicModels.cs and EndOfTurnModels.cs, deleted 2026-11-24)

/// <summary>ArtOfWar / RippleBasin.AfterCardPlayed: the turn's first Attack gives up their end-of-turn bonus (bad for you).</summary>
public abstract class FirstAttackGivesUp : RelicProc
{
    public override bool Harmful => true;
    /// <summary>The play that gives up the bonus clears the relic's Active status (AfterCardPlayed: Status = Normal); it doesn't flash.</summary>
    public override ActedSeen Seen => ActedSeen.StatusCleared;
    public override IEnumerable<SimState> BeforeCardPlayed(SimState state, int owner, SimCardPlay play) =>
        Mine(play, owner) && play.Card.Type == SimCardType.Attack && SimEngine.TurnOf(state, owner)[SimKeys.Plays(PlayStage.Started, SimCardType.Attack)] == 0 ? Fire(state, owner) : One(state);
}

public sealed class ArtOfWar : FirstAttackGivesUp { }
/// <summary>RippleBasin: also BeforeSideTurnEnd: its block (amount) when its owner played no Attack this turn.</summary>
public sealed class RippleBasin : FirstAttackGivesUp
{
    public override IEnumerable<SimState> BeforeSideTurnEnd(SimState state, int owner) =>
        // Its owner's Attacks this turn (SimPlayer.AttacksThisTurn, the engine's count until it went: the turn's count of Attack plays now).
        state.TakesPart(owner) && SimEngine.TurnOf(state, owner)[SimKeys.Plays(PlayStage.Started, SimCardType.Attack)] == 0 && Amount(state, owner) > 0 ? Block(state, owner, Amount(state, owner)) : One(state);
}

/// <inheritdoc cref="SpireQoL.Sim.Models.EndOfTurnModel"/>
public abstract class EndOfTurnRelic : RelicProc
{
    public override TabKind? Tab => null;
}

/// <summary>ParryingShield.AfterSideTurnEnd: its damage (amount) to a random hittable enemy when its owner has at least its Block (Data).</summary>
public sealed class ParryingShield : EndOfTurnRelic
{
    /// <summary>Plain when this play makes it fire for sure and ending the turn now might not; crossed when it would for sure now and the play can stop it.</summary>
    public override OutcomeRule? OutcomeTab(SimState start, SimOutcome now, int owner) =>
        owner != Who.You ? null
        : !now.InEvery(Fired) ? new(Harmful: false, outcome => outcome.InEvery(Fired), InPlay: false)
        : new(Harmful: true, outcome => !outcome.InEvery(Fired), branch => !Fired(branch), InPlay: false);

    /// <summary>It fired as your turn ended (SimState.ParryingShieldFired, the engine's until it went: kept in your fields now).</summary>
    private const string FiredField = "ParryingShield.Fired";

    private static bool Fired(SimState branch) => SimEngine.FieldsOf(branch, Who.You).Has(FiredField);

    public override IEnumerable<SimState> AfterSideTurnEnd(SimState state, int owner)
    {
        if (!state.TakesPart(owner) || Amount(state, owner) <= 0 || state.Player(owner) is not { } player || player.Block < Data(state, owner)) return One(state);
        if (owner == Who.You) SimEngine.SetField(state, Who.You, FiredField, 1);
        return DamageRandom(state, owner, Amount(state, owner));
    }
}

// ---- a hit's reaction before it lands (round sixteen: Sim/Models/ReactionModels.cs, deleted 2026-11-24). SimEngine.LandOnPlayer, its
// landing, went with it: the same landing is SimEngine.Lands with no dealer and Unpowered, as LandOnPlayer's LandBlockable called it.

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
        else if (Who.IsPlayer(dealer) && hit > 0) SimEngine.Lands(state, name, dealer, hit, SimProps.Unpowered, dealer: null, card: null);
    }
}

// ---- arrows (round sixteen: Sim/Models/ProcPowerModels.cs and StatModels.cs, deleted 2026-11-24)

/// <summary>ExplosiveArrowsPower.AfterArrowImpaled: its owner's arrow impales an enemy: its amount to every hittable enemy (unpowered).</summary>
public sealed class ExplosiveArrowsPower : ProcModel
{
    public override TabKind? Tab => TabKind.Damage;
    public override IEnumerable<SimState> AfterArrowImpaled(SimState state, int owner, int target, int shooter) =>
        shooter == owner ? DamageAll(state, owner, Amount(state, owner)) : One(state);
}

/// <summary>CoveringFirePower.AfterArrowShot: each shot its owner tries from a card (arrow or not) gives every other living player its amount of block (unpowered). No tab.</summary>
public sealed class CoveringFirePower : ProcModel
{
    public override TabKind? Tab => null; // no tab: the block is your teammates' (decision #25)
    public override IEnumerable<SimState> AfterArrowShot(SimState state, int owner, int shooter)
    {
        if (shooter != owner || state.CurrentPlay == null) return One(state);
        IEnumerable<SimState> states = One(state);
        var amount = Amount(state, owner);
        for (var teammateIndex = 0; teammateIndex < state.Teammates.Count; teammateIndex++)
        {
            var mate = Who.Teammate(teammateIndex);
            if (mate == owner) continue;
            states = states.SelectMany(branch => SimEngine.ProcBlockFor(branch, owner, Name, mate, amount, Unpowered)).ToList();
        }
        if (owner != Who.You) states = states.SelectMany(branch => SimEngine.ProcBlockFor(branch, owner, Name, Who.You, amount, Unpowered)).ToList();
        return states;
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

// ---- a damage term that changes the play (Sim/Models/ProcPowerModels.cs, deleted 2026-11-24)

/// <summary>
/// ConquerorPower (on an enemy): Sovereign Blade deals it double (ModifyDamageMultiplicative: `cardSource is SovereignBlade`, a powered attack,
/// on its owner: 2), and its tab shows on a Sovereign Blade played (its code reads whole: the tab is what keeps this model).
/// </summary>
public sealed class ConquerorPower : ProcModel
{
    public override IEnumerable<SimState> AfterCardPlayed(SimState state, int owner, SimCardPlay play) =>
        owner >= 0 && play.Card.Name == CardId.SovereignBlade.Name && play.PlayIndex == 0 ? Fire(state, owner) : One(state);
    public override decimal ModifyDamageMultiplicative(SimState state, int owner, DamageCtx damage) =>
        damage.Card?.Name == CardId.SovereignBlade.Name && damage.Props.IsPoweredAttack() && damage.Target == owner ? 2m : 1m;
}

// ---- the stars a play spent, a play's last (round seventeen: Sim/Models/ProcPowerModels.cs, deleted 2026-11-24)

/// <summary>
/// BlackHolePower: its amount to every hittable enemy (unpowered) each time its owner gains stars (AfterStarsGained), and after
/// the last play of a card that spent stars (AfterCardPlayed, IsLastInSeries).
/// </summary>
public sealed class BlackHolePower : ProcModel
{
    public override TabKind? Tab => TabKind.Damage;
    public override IEnumerable<SimState> AfterCardEvent(SimState state, int owner, CardEvent cardEvent, int player) =>
        cardEvent == CardEvent.StarsGained && player == owner ? DamageAll(state, owner, Amount(state, owner)) : One(state);
    public override IEnumerable<SimState> AfterCardPlayed(SimState state, int owner, SimCardPlay play) =>
        Mine(play, owner) && play.Card.Stars > 0 && play.PlayIndex == play.PlayCount - 1 ? DamageAll(state, owner, Amount(state, owner)) : One(state);
}

// ---- a stun of its owner (round seventeen: Sim/Models/ReactionModels.cs, TargetModels.cs and EnemyModels.cs, deleted 2026-11-24)

/// <summary>The outcome rules the frozen stun models use (Sim/Models/OutcomeModels.cs' OutcomeRules.Stuns and KillsAnother, as they were).</summary>
public static class FrozenStunRules
{
    public static OutcomeRule? Stuns(SimOutcome now, int enemy) =>
        Who.IsEnemy(enemy) && !now.StunnedInAll(enemy) ? new(Harmful: false, outcome => outcome.StunnedInAll(enemy)) : null;
    public static OutcomeRule KillsAnother(int enemy) => new(Harmful: true, outcome => outcome.KilledByPlayInAny.Any(killed => killed != enemy));
}

/// <summary>ShriekPower.AfterDamageReceived: HP through that leaves its owner at or below its amount stuns it; the power goes.</summary>
public sealed class ShriekPower : SimModel
{
    public override OutcomeRule? OutcomeTab(SimState start, SimOutcome now, int owner) => FrozenStunRules.Stuns(now, owner);

    public override void AfterDamageReceived(SimState state, int owner, DamageEvent hit)
    {
        if (hit.Target != owner || hit.Unblocked <= 0 || state.Enemy(owner) is not { Alive: true } enemy || enemy.Hp > enemy.Powers[ThisPower]) return;
        state.LogFired(Name, amount: 0, owner);
        enemy.Powers = enemy.Powers.Without(ThisPower);
        SimEngine.Stun(enemy);
    }
}

/// <summary>
/// FlutterPower: powered attacks on its owner deal its DamageDecrease% (Data; 50 by default); each that gets HP through takes a
/// stack (AfterDamageReceived), and the last stuns it.
/// </summary>
public sealed class FlutterPower : SimModel
{
    public override OutcomeRule? OutcomeTab(SimState start, SimOutcome now, int owner) => FrozenStunRules.Stuns(now, owner);

    public override decimal ModifyDamageMultiplicative(SimState state, int owner, DamageCtx damage)
    {
        if (damage.Target != owner || !damage.Props.IsPoweredAttack()) return 1m;
        var pct = SimEngine.PowersOf(state, owner).DataOf(ThisPower);
        return (pct == 0 ? 50 : pct) / 100m;
    }
    public override void AfterDamageReceived(SimState state, int owner, DamageEvent hit)
    {
        if (hit.Target != owner || hit.Unblocked == 0 || !hit.Props.IsPoweredAttack() || owner < 0) return;
        var powers = SimEngine.PowersOf(state, owner);
        if (!powers.Has(ThisPower)) return;
        var left = powers[ThisPower] - 1;
        SimEngine.SetPowers(state, owner, left <= 0 ? powers.Without(ThisPower) : powers.With(ThisPower, left));
        if (left <= 0 && state.Enemy(owner) is { Alive: true } enemy)
        {
            enemy.Stunned = true;
            enemy.StunnedThisTurn = true;
            state.LogFired(Name, amount: 0, owner);
        }
    }
}

/// <summary>RavenousPower (AfterDeath of an ally): it's stunned.</summary>
public sealed class RavenousPower : SimModel
{
    public override OutcomeRule? OutcomeTab(SimState start, SimOutcome now, int owner) => Who.IsEnemy(owner) ? FrozenStunRules.KillsAnother(owner) : null;

    public override void AfterDeath(SimState state, int owner, int dead)
    {
        if (dead == owner || dead < 0 || state.Enemy(owner) is not { } enemy || enemy.Stunned) return;
        state.LogFired(Name, amount: 0, owner);
        SimEngine.Stun(enemy);
    }
}

// ---- a copy of a card event's card (round seventeen: Sim/Models/ProcRelicModels.cs, deleted 2026-11-24)

/// <summary>BurningSticks.AfterCardExhausted: once a combat, a Skill you exhaust is copied into your hand.</summary>
public sealed class BurningSticks : RelicProc
{
    public override IEnumerable<SimState> AfterCardEvent(SimState state, int owner, CardEvent cardEvent, int player)
    {
        if (cardEvent != CardEvent.Exhausted || player != owner || !Armed(state, owner) || state.EventCard?.Type != SimCardType.Skill) return One(state);
        Disarm(state, owner);
        return MakeInHand(Fire(state, owner), state.EventCard, count: 1, owner); // a clone of the card exhausted
    }
}

// ---- the first play with a melody this turn (round seventeen: Sim/Models/ProcPowerModels.cs, deleted 2026-11-24)

/// <summary>ViolentChordPower.AfterCardPlayed: the first Wah each turn deals its amount to every hittable enemy (unpowered).</summary>
public sealed class ViolentChordPower : ProcModel
{
    public override TabKind? Tab => TabKind.Damage;
    public override IEnumerable<SimState> AfterCardPlayed(SimState state, int owner, SimCardPlay play) =>
        Mine(play, owner) && Melodies.First(state, play, SimCardTag.Wah) ? DamageAll(state, owner, Amount(state, owner)) : One(state);
}

/// <summary>SongPathPower.AfterCardPlayed: the first Cho each turn gains its amount of block (unpowered).</summary>
public sealed class SongPathPower : ProcModel
{
    public override TabKind? Tab => TabKind.Block;
    public override IEnumerable<SimState> AfterCardPlayed(SimState state, int owner, SimCardPlay play) =>
        Mine(play, owner) && Melodies.First(state, play, SimCardTag.Cho) ? Block(state, owner, Amount(state, owner)) : One(state);
}

// ---- a kill of its owner (round eighteen: Sim/Models/ReactionModels.cs, deleted 2026-11-24)

/// <summary>TheGambitPower.AfterDamageReceived: a powered attack that gets HP through kills its owner.</summary>
public sealed class TheGambitPower : SimModel
{
    public override OutcomeRule? OutcomeTab(SimState start, SimOutcome now, int owner) =>
        Who.IsEnemy(owner) && !now.KilledByItsPowerInAll(owner) ? new(Harmful: false, outcome => outcome.KilledByItsPowerInAll(owner)) : null;

    public override void AfterDamageReceived(SimState state, int owner, DamageEvent hit)
    {
        if (hit.Target != owner || !hit.Props.IsPoweredAttack() || hit.Unblocked <= 0 || state.Enemy(owner) is not { } enemy) return;
        enemy.Powers = enemy.Powers.Without(ThisPower);
        state.LogFired(Name, amount: 0, owner);
        enemy.KilledByItsPower = true;
        enemy.Hp = 0;
    }
}

// ---- cards made into a draw pile (round eighteen: Sim/Models/ProcPowerModels.cs and ProcRelicModels.cs, deleted 2026-11-24)

/// <summary>
/// PersonalHivePower.AfterDamageReceived: each powered attack hit on it from a player puts its amount of Dazed into their draw pile at
/// random spots (bad for you), where a later draw can find them. Its tab counts them (decision #161): the hits of the play times its amount.
/// </summary>
public sealed class PersonalHivePower : ProcModel
{
    public override TabKind? Tab => TabKind.CardsMade;
    public override bool Harmful => true;
    public override void AfterDamageReceived(SimState state, int owner, DamageEvent hit)
    {
        if (hit.Target != owner || hit.Dealer is not { } dealer || !Who.IsPlayer(dealer) || !hit.Props.IsPoweredAttack()) return;
        var amount = Amount(state, owner);
        state.LogFired(Name, amount, owner);
        // Each Dazed goes into the attacker's draw pile at a random spot (CardPilePosition.Random), once the hit is over (no created-card hook told).
        for (var i = 0; i < amount; i++) state.PendingIntoDraw = state.PendingIntoDraw.Add(new PendingMade(dealer, CardId.Dazed.Name, Who.None, Made: null));
    }
}

/// <summary>BiiigHug.AfterShuffle: a Soot into your draw pile at a random spot on every shuffle (bad for you), where a later draw can find it.</summary>
public sealed class BiiigHug : RelicProc
{
    public override bool Harmful => true;
    public override IEnumerable<SimState> AfterCardEvent(SimState state, int owner, CardEvent cardEvent, int player) =>
        cardEvent == CardEvent.Shuffled && player == owner ? Fire(state, owner).SelectMany(branch => SimEngine.IntoDrawPile(branch, owner, CardId.Soot.Name)).ToList() : One(state);
}
// ---- summons (round nineteen: Sim/Models/ProcPowerModels.cs, deleted 2026-11-25)

/// <summary>DevourLifePower.AfterCardPlayed: each Soul its owner plays summons its amount (Osty).</summary>
public sealed class DevourLifePower : ProcModel
{
    public override TabKind? Tab => TabKind.Summon;
    public override IEnumerable<SimState> AfterCardPlayed(SimState state, int owner, SimCardPlay play) =>
        Mine(play, owner) && play.Card.Name == CardId.Soul.Name ? Fire(state, owner, Amount(state, owner)) : One(state);
}

/// <summary>
/// SicEmPower (yours, on an enemy; InstancedPerApplier).AfterDamageGiven: each time its applier's Osty damages the enemy that has it,
/// its applier summons its amount (OstyCmd.Summon). Owner: the enemy. Only yours is read (SimReaders.Powers: a teammate's instance
/// reacts to their Osty, whose hits aren't simulated in your turn).
/// </summary>
public sealed class SicEmPower : ProcModel
{
    public override TabKind? Tab => TabKind.Summon;
    public override void AfterDamageGiven(SimState state, int owner, DamageEvent hit)
    {
        if (hit.Dealer == Who.YourOsty && hit.Target == owner && hit.Total > 0 && Amount(state, owner) > 0) state.LogFired(Name, Amount(state, owner), owner);
    }
}

// ---- round twenty-one: what a model keeps in its own data, a card or a play (Sim/Models/ProcPowerModels.cs, BlockModels.cs), deleted 2026-11-25

/// <summary>SerpentFormPower: damage (unpowered) to a random hittable enemy for each card its owner plays.</summary>
public sealed class SerpentFormPower : BoundPower
{
    public override TabKind? Tab => TabKind.Damage;
    protected override IEnumerable<SimState> Act(SimState state, int owner, SimCardPlay play, int amount) => amount > 0 ? DamageRandom(state, owner, amount) : One(state);
}

/// <summary>CalamityPower: each Attack its owner plays adds its amount of random Attacks to their hand (cards created).</summary>
public sealed class CalamityPower : BoundPower
{
    protected override bool Binds(SimCardPlay play) => play.Card.Type == SimCardType.Attack;
    protected override IEnumerable<SimState> Act(SimState state, int owner, SimCardPlay play, int amount) => MakeInHand(Fire(state, owner), card: null, amount, owner); // random Attacks: no class to tell
}

/// <summary>
/// JugglingPower: the third Attack its owner plays each turn (its own count, Data) is copied into their hand, its amount of times
/// (BeforeCardPlayed counts and binds the third; AfterCardPlayed adds the copies). (Main's way, MainRule.JugglingCopiesBeforeThePlay, went with
/// it: each branch's own code is read now; the parity board plays beta's, as the installed game is.)
/// </summary>
public sealed class JugglingPower : ProcModel
{
    public override IEnumerable<SimState> BeforeCardPlayed(SimState state, int owner, SimCardPlay play)
    {
        if (!Mine(play, owner) || play.Card.Type != SimCardType.Attack) return One(state);
        var count = Data(state, owner) + 1;
        Set(state, owner, Amount(state, owner), count);
        if (count != 3) return One(state);
        Bind(state, owner, Amount(state, owner));
        return One(state);
    }
    public override IEnumerable<SimState> AfterCardPlayed(SimState state, int owner, SimCardPlay play) =>
        Mine(play, owner) && TakeBound(state, owner) is { } bound ? MakeInHand(Fire(state, owner), play.Card, bound, owner) : One(state);
}
