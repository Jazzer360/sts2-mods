namespace SpireQoL.Sim.Models;

// Relics that act on your plays and card events (decompile v0.113.1). Amount: the relic's counter; Data bit 1: armed (unused
// this combat or turn, RelicCountersFeature.IsArmed), unless a model says otherwise.

// ---- giving up a relic's bonus (crossed tabs)

/// <summary>ArtOfWar / RippleBasin.AfterCardPlayed: the turn's first Attack gives up their end-of-turn bonus (bad for you).</summary>
public abstract class FirstAttackGivesUp : RelicProc
{
    public override bool Harmful => true;
    public override IEnumerable<SimState> BeforeCardPlayed(SimState s, int owner, SimCardPlay play) =>
        Mine(play, owner) && play.Card.Type == SimCardType.Attack && SimEngine.TurnOf(s, owner)["Started:Attack"] == 0 ? Fire(s, owner) : One(s);
}

public sealed class ArtOfWar : FirstAttackGivesUp { public override string Name => "ArtOfWar"; }
/// <summary>RippleBasin: also BeforeSideTurnEnd: its block (amount) when its owner played no Attack this turn.</summary>
public sealed class RippleBasin : FirstAttackGivesUp
{
    public override string Name => "RippleBasin";
    public override IEnumerable<SimState> BeforeSideTurnEnd(SimState s, int owner) =>
        SimEngine.TakesPart(s, owner) && SimEngine.PlayerOf(s, owner) is { AttacksThisTurn: 0 } && Amount(s, owner) > 0 ? Block(s, owner, Amount(s, owner)) : One(s);
}

/// <summary>PaelsTears (BeforeSideTurnEnd: energy left gives energy next turn): spending your last energy gives that up (bad for you).</summary>
public sealed class PaelsTears : RelicProc
{
    public override string Name => "PaelsTears";
    public override bool Harmful => true;
    public override IEnumerable<SimState> AfterCardEvent(SimState s, int owner, CardEvent ev, int player) =>
        ev == CardEvent.EnergySpent && player == owner && SimEngine.PlayerOf(s, owner) is { Energy: <= 0 } ? Fire(s, owner) : One(s);
}

/// <summary>PaelsEye.BeforeCardPlayed: while armed, the turn's first card you play (not an auto-play) gives up its extra turn (bad for you).</summary>
public sealed class PaelsEye : RelicProc
{
    public override string Name => "PaelsEye";
    public override bool Harmful => true;
    /// <summary>
    /// BeforeSideTurnEndEarly: still armed (no card played this turn: SimPlayer.PaelsEyeExhaust), its owner's whole hand is
    /// exhausted (Feel No Pain, Charon's Ashes... react to each card); nothing is left to act in hand or exhaust as Ethereal.
    /// </summary>
    public override IEnumerable<SimState> BeforeSideTurnEndEarly(SimState s, int owner)
    {
        if (!SimEngine.TakesPart(s, owner) || SimEngine.PlayerOf(s, owner) is not { PaelsEyeExhaust: true, HandCount: > 0 } p) return One(s);
        var cards = p.HandCount;
        return SimEngine.ProcCardEvent(s, CardEvent.Exhausted, cards, owner, indirect: false).Select(x =>
        {
            var px = owner == Who.You ? x.P : x.MutableTeammate(Who.TeammateIndex(owner));
            px.HandCount = 0;
            px.Fading = 0;
            px.HandBlockable = new();
            px.HandUnblockable = new();
            px.RegretCards = 0;
            return x;
        }).ToList();
    }
    public override IEnumerable<SimState> BeforeCardPlayed(SimState s, int owner, SimCardPlay play)
    {
        if (!Mine(play, owner) || play.AutoPlay || !Armed(s, owner) || SimEngine.TurnOf(s, owner)["Started"] != 0) return One(s);
        Disarm(s, owner);
        return Fire(s, owner);
    }
}

/// <summary>BiiigHug.AfterShuffle: a Soot into your draw pile on every shuffle (bad for you).</summary>
public sealed class BiiigHug : RelicProc
{
    public override string Name => "BiiigHug";
    public override bool Harmful => true;
    public override IEnumerable<SimState> AfterCardEvent(SimState s, int owner, CardEvent ev, int player) =>
        ev == CardEvent.Shuffled && player == owner ? Fire(s, owner) : One(s);
}

// ---- used up by a play

/// <summary>BurningSticks.AfterCardExhausted: once a combat, a Skill you exhaust is copied into your hand.</summary>
public sealed class BurningSticks : RelicProc
{
    public override string Name => "BurningSticks";
    public override IEnumerable<SimState> AfterCardEvent(SimState s, int owner, CardEvent ev, int player)
    {
        if (ev != CardEvent.Exhausted || player != owner || !Armed(s, owner) || s.EventCard?.Type != SimCardType.Skill) return One(s);
        Disarm(s, owner);
        return Then(Fire(s, owner), CardEvent.Generated, 1, owner);
    }
}

/// <summary>MusicBox: once a turn, the first Attack you play (bound at BeforeCardPlayed) is copied into your hand, Ethereal.</summary>
public sealed class MusicBox : RelicProc
{
    public override string Name => "MusicBox";
    public override IEnumerable<SimState> BeforeCardPlayed(SimState s, int owner, SimCardPlay play)
    {
        if (Mine(play, owner) && Armed(s, owner) && play.Card.Type == SimCardType.Attack && !s.Bound.Has($"{Name}@{owner}")) Bind(s, owner, 1);
        return One(s);
    }
    public override IEnumerable<SimState> AfterCardPlayed(SimState s, int owner, SimCardPlay play)
    {
        if (!Mine(play, owner) || TakeBound(s, owner) is null) return One(s);
        Disarm(s, owner);
        return Then(Fire(s, owner), CardEvent.Generated, 1, owner);
    }
}

/// <summary>
/// UnsettlingLamp: while armed, the first card of yours that applies a debuff to an enemy without Artifact (BeforePowerAmountChanged)
/// has its debuffs doubled (ModifyPowerAmountGivenMultiplicative); AfterCardPlayed of that card uses it up. Data bit 2: bound.
/// </summary>
public sealed class UnsettlingLamp : RelicProc
{
    public override string Name => "UnsettlingLamp";
    private const int BoundBit = 2;
    public override int ModifyPowerAmountGiven(SimState s, int owner, string power, int amount, int? applier, int target, bool debuff)
    {
        if (!debuff || applier != owner || target < 0 || s.CurrentPlay is not { } play || play.Owner != owner) return amount;
        var data = Data(s, owner);
        if ((data & BoundBit) == 0)
        {
            if (!Armed(s, owner) || SimEngine.PowerOn(s, target, "ArtifactPower") > 0) return amount;
            Set(s, owner, Amount(s, owner), data | BoundBit);
        }
        return amount * 2;
    }
    public override IEnumerable<SimState> AfterCardPlayed(SimState s, int owner, SimCardPlay play)
    {
        if (!Mine(play, owner) || (Data(s, owner) & BoundBit) == 0) return One(s);
        Set(s, owner, Amount(s, owner), 0);
        return Fire(s, owner);
    }
}

// ---- on play

/// <summary>IntimidatingHelmet.BeforeCardPlayed: block for each card you play costing at least its Energy value.</summary>
public sealed class IntimidatingHelmet : RelicProc
{
    public override string Name => "IntimidatingHelmet";
    public override TabKind? Tab => TabKind.Block;
    public override IEnumerable<SimState> BeforeCardPlayed(SimState s, int owner, SimCardPlay play) =>
        Mine(play, owner) && play.Card.Energy >= Var(s, owner, "Energy", 2) ? RelicBlock(s, owner) : One(s);
}

/// <summary>DaughterOfTheWind.AfterCardPlayed: block for each Attack you play.</summary>
public sealed class DaughterOfTheWind : RelicProc
{
    public override string Name => "DaughterOfTheWind";
    public override TabKind? Tab => TabKind.Block;
    public override IEnumerable<SimState> AfterCardPlayed(SimState s, int owner, SimCardPlay play) =>
        Mine(play, owner) && play.Card.Type == SimCardType.Attack ? RelicBlock(s, owner) : One(s);
}

/// <summary>Permafrost.AfterCardPlayed: block for the first Power you play each combat.</summary>
public sealed class Permafrost : RelicProc
{
    public override string Name => "Permafrost";
    public override TabKind? Tab => TabKind.Block;
    public override IEnumerable<SimState> AfterCardPlayed(SimState s, int owner, SimCardPlay play)
    {
        if (!Mine(play, owner) || play.Card.Type != SimCardType.Power || !Armed(s, owner)) return One(s);
        Disarm(s, owner);
        return RelicBlock(s, owner);
    }
}

/// <summary>LostWisp.AfterCardPlayed: damage to every hittable enemy for each Power you play.</summary>
public sealed class LostWisp : RelicProc
{
    public override string Name => "LostWisp";
    public override TabKind? Tab => TabKind.Damage;
    public override IEnumerable<SimState> AfterCardPlayed(SimState s, int owner, SimCardPlay play) =>
        Mine(play, owner) && play.Card.Type == SimCardType.Power ? RelicDamageAll(s, owner) : One(s);
}

/// <summary>GamePiece.AfterCardPlayed: a draw for each Power you play.</summary>
public sealed class GamePiece : RelicProc
{
    public override string Name => "GamePiece";
    public override IEnumerable<SimState> AfterCardPlayed(SimState s, int owner, SimCardPlay play) =>
        Mine(play, owner) && play.Card.Type == SimCardType.Power ? Then(Fire(s, owner), CardEvent.Drawn, 1, owner) : One(s);
}

/// <summary>
/// MummifiedHand.AfterCardPlayed: each Power you play makes a random other card in your hand free this turn (it prefers one that
/// still costs something; with none, nothing changes).
/// </summary>
public sealed class MummifiedHand : RelicProc
{
    public override string Name => "MummifiedHand";
    public override IEnumerable<SimState> AfterCardPlayed(SimState s, int owner, SimCardPlay play) =>
        Mine(play, owner) && play.Card.Type == SimCardType.Power && play.Card.OthersInHandCost ? Fire(s, owner) : One(s);
}

/// <summary>HelicalDart.AfterCardPlayed: each Shiv you play gives Dexterity this turn.</summary>
public sealed class HelicalDart : RelicProc
{
    public override string Name => "HelicalDart";
    public override IEnumerable<SimState> AfterCardPlayed(SimState s, int owner, SimCardPlay play) =>
        Mine(play, owner) && play.Card.Is("Shiv") ? Fire(s, owner) : One(s);
}

/// <summary>IvoryTile.AfterCardPlayed: energy for each card you play costing at least its EnergyThreshold.</summary>
public sealed class IvoryTile : RelicProc
{
    public override string Name => "IvoryTile";
    public override IEnumerable<SimState> AfterCardPlayed(SimState s, int owner, SimCardPlay play) =>
        Mine(play, owner) && play.Card.Energy >= Var(s, owner, "EnergyThreshold", 3) ? Fire(s, owner) : One(s);
}

/// <summary>ChemicalX.ModifyXValue: X-cost cards (energy or stars) get more.</summary>
public sealed class ChemicalX : RelicProc
{
    public override string Name => "ChemicalX";
    public override IEnumerable<SimState> BeforeCardPlayed(SimState s, int owner, SimCardPlay play) =>
        Mine(play, owner) && play.PlayIndex == 0 && (play.Card.CostsX || play.Card.StarCostX) ? Fire(s, owner) : One(s);
}

/// <summary>RazorTooth.AfterCardPlayed: an Attack or Skill you play that can be upgraded is upgraded.</summary>
public sealed class RazorTooth : RelicProc
{
    public override string Name => "RazorTooth";
    public override IEnumerable<SimState> AfterCardPlayed(SimState s, int owner, SimCardPlay play) =>
        Mine(play, owner) && play.PlayIndex == 0 && play.Card.Type is SimCardType.Attack or SimCardType.Skill && play.Card.Upgradable ? Fire(s, owner) : One(s);
}

/// <summary>RainbowRing.AfterCardPlayed: once a turn (Data 2: done), the play that completes an Attack, a Skill and a Power this turn.</summary>
public sealed class RainbowRing : RelicProc
{
    public override string Name => "RainbowRing";
    public override IEnumerable<SimState> AfterCardPlayed(SimState s, int owner, SimCardPlay play)
    {
        if (!Mine(play, owner) || (Data(s, owner) & 2) != 0) return One(s);
        var turn = SimEngine.TurnOf(s, owner);
        int Of(SimCardType t) => turn[$"Finished:{t}"] - (play.Card.Type == t ? 1 : 0);
        var all = turn["Finished:Attack"] > 0 && turn["Finished:Skill"] > 0 && turn["Finished:Power"] > 0;
        var before = Of(SimCardType.Attack) > 0 && Of(SimCardType.Skill) > 0 && Of(SimCardType.Power) > 0;
        if (!all || before) return One(s);
        Set(s, owner, Amount(s, owner), Data(s, owner) | 2);
        return Fire(s, owner);
    }
}

/// <summary>
/// ConductorsBaton.AfterCardPlayed: once a turn (Data 2: done), the play that completes Wah, Cho and Ki applies
/// ConductorsBatonPower (1) to its owner.
/// </summary>
public sealed class ConductorsBaton : RelicProc
{
    public override string Name => "ConductorsBaton";
    public override IEnumerable<SimState> AfterCardPlayed(SimState s, int owner, SimCardPlay play)
    {
        if (!Mine(play, owner) || (Data(s, owner) & 2) != 0) return One(s);
        var (before, after) = Melodies.Finished(s, play);
        if (before >= 3 || after < 3) return One(s);
        Set(s, owner, Amount(s, owner), Data(s, owner) | 2);
        SimEngine.ApplyPower(s, owner, owner, "ConductorsBatonPower", 1, debuff: false);
        return Fire(s, owner);
    }
}

/// <summary>
/// ConductorsBatonPower.ModifyDamageMultiplicative: its DamageDecrease (0.75) on powered attacks on its owner; it goes at the
/// start of the owner's next turn (AfterSideTurnStart), so it lasts through the enemy turn.
/// </summary>
public sealed class ConductorsBatonPower : SimModel
{
    public override string Name => "ConductorsBatonPower";
    public override decimal ModifyDamageMultiplicative(SimState s, int owner, DamageCtx d) =>
        d.Target == owner && d.Props.IsPoweredAttack() ? 0.75m : 1m;
}

/// <summary>Twine.AfterArrowDislodged: block for each of your arrows that comes loose.</summary>
public sealed class Twine : RelicProc
{
    public override string Name => "Twine";
    public override TabKind? Tab => TabKind.Block;
    public override IEnumerable<SimState> AfterCardEvent(SimState s, int owner, CardEvent ev, int player) =>
        ev == CardEvent.ArrowDislodged && player == owner ? RelicBlock(s, owner) : One(s);
}

/// <summary>UnceasingTop.AfterHandEmptied: a draw when your last card leaves your hand (a play that draws or creates nothing).</summary>
public sealed class UnceasingTop : RelicProc
{
    public override string Name => "UnceasingTop";
    public override IEnumerable<SimState> AfterCardPlayed(SimState s, int owner, SimCardPlay play) =>
        Mine(play, owner) && play.PlayIndex == 0 && SimEngine.PlayerOf(s, owner) is { HandCount: 1 }
        && s.PlayEvents[nameof(CardEvent.Drawn)] == 0 && s.PlayEvents[nameof(CardEvent.Generated)] + s.PlayEvents[nameof(CardEvent.GeneratedStatus)] + s.PlayEvents[nameof(CardEvent.GeneratedSadness)] == 0
            ? Fire(s, owner) : One(s);
}

// ---- counters

public sealed class Kunai : CounterRelic
{
    public override string Name => "Kunai";
    protected override bool Counts(SimCard card) => card.Type == SimCardType.Attack;
    protected override IEnumerable<SimState> OnComplete(SimState s, int owner)
    {
        SimEngine.ApplyPower(s, owner, owner, "DexterityPower", Var(s, owner, "Dexterity", 1), debuff: false);
        return Fire(s, owner);
    }
}

public sealed class Shuriken : CounterRelic
{
    public override string Name => "Shuriken";
    protected override bool Counts(SimCard card) => card.Type == SimCardType.Attack;
    protected override IEnumerable<SimState> OnComplete(SimState s, int owner)
    {
        SimEngine.ApplyPower(s, owner, owner, "StrengthPower", Var(s, owner, "Strength", 1), debuff: false);
        return Fire(s, owner);
    }
}

public sealed class Nunchaku : CounterRelic
{
    public override string Name => "Nunchaku";
    protected override bool Counts(SimCard card) => card.Type == SimCardType.Attack;
    protected override IEnumerable<SimState> OnComplete(SimState s, int owner) => Fire(s, owner);
}

public sealed class OrnamentalFan : CounterRelic
{
    public override string Name => "OrnamentalFan";
    public override TabKind? Tab => TabKind.Block;
    protected override bool Counts(SimCard card) => card.Type == SimCardType.Attack;
    protected override IEnumerable<SimState> OnComplete(SimState s, int owner) => RelicBlock(s, owner);
}

public sealed class Kusarigama : CounterRelic
{
    public override string Name => "Kusarigama";
    public override TabKind? Tab => TabKind.Damage;
    protected override bool Counts(SimCard card) => card.Type == SimCardType.Attack;
    protected override IEnumerable<SimState> OnComplete(SimState s, int owner) => RelicDamageRandom(s, owner);
}

public sealed class LetterOpener : CounterRelic
{
    public override string Name => "LetterOpener";
    public override TabKind? Tab => TabKind.Damage;
    protected override bool Counts(SimCard card) => card.Type == SimCardType.Skill;
    protected override IEnumerable<SimState> OnComplete(SimState s, int owner) => RelicDamageAll(s, owner);
}

public sealed class TuningFork : CounterRelic
{
    public override string Name => "TuningFork";
    public override TabKind? Tab => TabKind.Block;
    protected override bool Counts(SimCard card) => card.Type == SimCardType.Skill;
    protected override IEnumerable<SimState> OnComplete(SimState s, int owner) => RelicBlock(s, owner);
}

public sealed class IronClub : CounterRelic
{
    public override string Name => "IronClub";
    protected override bool Counts(SimCard card) => true;
    protected override IEnumerable<SimState> OnComplete(SimState s, int owner) => Then(Fire(s, owner), CardEvent.Drawn, 1, owner);
}

// ---- card events

/// <summary>CharonsAshes.AfterCardExhausted: damage to every hittable enemy for each card you exhaust.</summary>
public sealed class CharonsAshes : RelicProc
{
    public override string Name => "CharonsAshes";
    public override TabKind? Tab => TabKind.Damage;
    public override IEnumerable<SimState> AfterCardEvent(SimState s, int owner, CardEvent ev, int player) =>
        ev == CardEvent.Exhausted && player == owner ? DamageAll(s, owner, Amount(s, owner), PropsOf(s, owner, "Damage")) : One(s);
}

/// <summary>ForgottenSoul.AfterCardExhausted: damage to a random hittable enemy for each card you exhaust.</summary>
public sealed class ForgottenSoul : RelicProc
{
    public override string Name => "ForgottenSoul";
    public override TabKind? Tab => TabKind.Damage;
    public override IEnumerable<SimState> AfterCardEvent(SimState s, int owner, CardEvent ev, int player) =>
        ev == CardEvent.Exhausted && player == owner ? DamageRandom(s, owner, Amount(s, owner), PropsOf(s, owner, "Damage")) : One(s);
}

/// <summary>JossPaper.AfterCardExhausted: a draw once its ExhaustAmount cards are exhausted (Amount: CardsExhausted).</summary>
public sealed class JossPaper : RelicProc
{
    public override string Name => "JossPaper";
    public override IEnumerable<SimState> AfterCardEvent(SimState s, int owner, CardEvent ev, int player)
    {
        if (ev != CardEvent.Exhausted || player != owner) return One(s);
        var n = Var(s, owner, "ExhaustAmount", 5);
        var count = Amount(s, owner) + 1;
        if (count < n) { Set(s, owner, count, Data(s, owner)); return One(s); }
        Set(s, owner, count - n, Data(s, owner));
        return Then(Fire(s, owner), CardEvent.Drawn, 1, owner);
    }
}

/// <summary>Tingsha.AfterCardDiscarded: damage to a random hittable enemy for each card you discard.</summary>
public sealed class Tingsha : RelicProc
{
    public override string Name => "Tingsha";
    public override TabKind? Tab => TabKind.Damage;
    public override IEnumerable<SimState> AfterCardEvent(SimState s, int owner, CardEvent ev, int player) =>
        ev == CardEvent.Discarded && player == owner ? RelicDamageRandom(s, owner) : One(s);
}

/// <summary>ToughBandages.AfterCardDiscarded: block for each card you discard.</summary>
public sealed class ToughBandages : RelicProc
{
    public override string Name => "ToughBandages";
    public override TabKind? Tab => TabKind.Block;
    public override IEnumerable<SimState> AfterCardEvent(SimState s, int owner, CardEvent ev, int player) =>
        ev == CardEvent.Discarded && player == owner ? RelicBlock(s, owner) : One(s);
}

/// <summary>MiniRegent.AfterStarsSpent: once a turn (armed), Strength when you spend stars.</summary>
public sealed class MiniRegent : RelicProc
{
    public override string Name => "MiniRegent";
    public override IEnumerable<SimState> AfterCardEvent(SimState s, int owner, CardEvent ev, int player)
    {
        if (ev != CardEvent.StarsSpent || player != owner || !Armed(s, owner)) return One(s);
        Disarm(s, owner);
        return Fire(s, owner);
    }
}

/// <summary>GalacticDust.AfterStarsSpent: block for every Stars value of stars spent (Amount: StarsSpent so far, kept below it).</summary>
public sealed class GalacticDust : RelicProc
{
    public override string Name => "GalacticDust";
    public override TabKind? Tab => TabKind.Block;
    public override IEnumerable<SimState> AfterCardEvent(SimState s, int owner, CardEvent ev, int player)
    {
        var stars = s.CurrentPlay?.Card.Stars ?? 0;
        if (ev != CardEvent.StarsSpent || player != owner || stars <= 0) return One(s);
        var per = Var(s, owner, "Stars", 10);
        if (per <= 0) return One(s);
        var spent = Amount(s, owner) + stars;
        Set(s, owner, spent % per, Data(s, owner));
        return spent >= per ? Block(s, owner, spent / per * Var(s, owner, "Block", 0), Unpowered) : One(s);
    }
}

/// <summary>Regalite.AfterCardGeneratedForCombat: once a turn (armed), block when you create a card.</summary>
public sealed class Regalite : RelicProc
{
    public override string Name => "Regalite";
    public override TabKind? Tab => TabKind.Block;
    public override IEnumerable<SimState> AfterCardEvent(SimState s, int owner, CardEvent ev, int player)
    {
        if (!Generated(ev) || player != owner || !Armed(s, owner)) return One(s);
        Disarm(s, owner);
        return RelicBlock(s, owner);
    }
}

/// <summary>TheAbacus.AfterShuffle: block on every shuffle.</summary>
public sealed class TheAbacus : RelicProc
{
    public override string Name => "TheAbacus";
    public override TabKind? Tab => TabKind.Block;
    public override IEnumerable<SimState> AfterCardEvent(SimState s, int owner, CardEvent ev, int player) =>
        ev == CardEvent.Shuffled && player == owner ? RelicBlock(s, owner) : One(s);
}

/// <summary>WhittlingKnife.AfterShuffle: Aim on every shuffle.</summary>
public sealed class WhittlingKnife : RelicProc
{
    public override string Name => "WhittlingKnife";
    public override IEnumerable<SimState> AfterCardEvent(SimState s, int owner, CardEvent ev, int player) =>
        ev == CardEvent.Shuffled && player == owner ? Fire(s, owner) : One(s);
}

/// <summary>Fertilizer.AfterCardGeneratedForCombat: Strength when you create a Sadness.</summary>
public sealed class Fertilizer : RelicProc
{
    public override string Name => "Fertilizer";
    public override IEnumerable<SimState> AfterCardEvent(SimState s, int owner, CardEvent ev, int player) =>
        ev == CardEvent.GeneratedSadness && player == owner ? Fire(s, owner) : One(s);
}

/// <summary>Metronome.AfterOrbChanneled: damage to every hittable enemy once its OrbCount orbs are channeled this combat (Amount: OrbsChanneled).</summary>
public sealed class Metronome : RelicProc
{
    public override string Name => "Metronome";
    public override TabKind? Tab => TabKind.Damage;
    public override IEnumerable<SimState> AfterOrbChanneled(SimState s, int owner, int player)
    {
        if (player != owner) return One(s);
        var n = Var(s, owner, "OrbCount", 7);
        var count = Amount(s, owner);
        if (count >= n) return One(s);
        Set(s, owner, count + 1, Data(s, owner));
        return count + 1 == n ? RelicDamageAll(s, owner) : One(s);
    }
}

/// <summary>BoneFlute.AfterAttack: block for each attack your Osty makes.</summary>
public sealed class BoneFlute : RelicProc
{
    public override string Name => "BoneFlute";
    public override TabKind? Tab => TabKind.Block;
    public override IEnumerable<SimState> AfterAttack(SimState s, int owner, AttackInfo attack) =>
        owner == Who.You && attack.Dealer == Who.YourOsty && s.P.OstyHp > 0 ? RelicBlock(s, owner) : One(s);
}
