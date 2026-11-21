namespace SpireQoL.Sim.Models;

// Relics that act on your plays and card events (decompile v0.113.1). Amount: the relic's counter; Data bit 1: armed (unused
// this combat or turn, RelicCountersFeature.IsArmed), unless a model says otherwise.

// ---- giving up a relic's bonus (crossed tabs)

/// <summary>ArtOfWar / RippleBasin.AfterCardPlayed: the turn's first Attack gives up their end-of-turn bonus (bad for you).</summary>
public abstract class FirstAttackGivesUp : RelicProc
{
    public override bool Harmful => true;
    /// <summary>The play that gives up the bonus clears the relic's Active status (AfterCardPlayed: Status = Normal); it doesn't flash.</summary>
    public override ActedSeen Seen => ActedSeen.StatusCleared;
    public override IEnumerable<SimState> BeforeCardPlayed(SimState s, int owner, SimCardPlay play) =>
        Mine(play, owner) && play.Card.Type == SimCardType.Attack && SimEngine.TurnOf(s, owner)[SimKeys.Plays(PlayStage.Started, SimCardType.Attack)] == 0 ? Fire(s, owner) : One(s);
}

public sealed class ArtOfWar : FirstAttackGivesUp { }
/// <summary>RippleBasin: also BeforeSideTurnEnd: its block (amount) when its owner played no Attack this turn.</summary>
public sealed class RippleBasin : FirstAttackGivesUp
{
    public override IEnumerable<SimState> BeforeSideTurnEnd(SimState s, int owner) =>
        s.TakesPart(owner) && s.Player(owner) is { AttacksThisTurn: 0 } && Amount(s, owner) > 0 ? Block(s, owner, Amount(s, owner)) : One(s);
}

/// <summary>
/// PaelsTears (BeforeSideTurnEnd: any energy left gives energy next turn): a play that spends energy and leaves you with none gives
/// that up (bad for you). Judged once the play has resolved (AfterPlayResolved), so energy the play gets back first (Orbit, Ivory
/// Tile, Subroutine, the card's own) keeps the bonus and shows no warning. Data: energy was spent during this play (1), not the armed bit.
/// </summary>
public sealed class PaelsTears : RelicProc
{
    public override bool Harmful => true;
    public override ActedSeen Seen => ActedSeen.NoEnergyLeft;
    public override IEnumerable<SimState> AfterCardEvent(SimState s, int owner, CardEvent ev, int player)
    {
        if (ev == CardEvent.EnergySpent && player == owner && Data(s, owner) == 0) Set(s, owner, Amount(s, owner), 1);
        return One(s);
    }
    public override IEnumerable<SimState> AfterPlayResolved(SimState s, int owner, int player)
    {
        if (player != owner || Data(s, owner) == 0) return One(s);
        Set(s, owner, Amount(s, owner), 0);
        return s.Player(owner) is { Energy: <= 0 } ? Fire(s, owner) : One(s);
    }
}

/// <summary>
/// Pocketwatch (ModifyHandDraw: 3 more cards at the start of your next turn when you played no more than its CardThreshold this
/// turn; AfterCardPlayed counts each card its owner plays, auto-plays and each play of a replay included): the play that takes the
/// count past the threshold gives that up (bad for you). Amount: cards played this turn (its counter); Data: the threshold.
/// </summary>
public sealed class Pocketwatch : RelicProc
{
    public override bool Harmful => true;
    /// <summary>It doesn't flash: its counter (cards this turn) goes past the threshold.</summary>
    public override ActedSeen Seen => ActedSeen.CounterPassedLimit;
    public override string? LimitVar => "CardThreshold";
    public override IEnumerable<SimState> AfterCardPlayed(SimState s, int owner, SimCardPlay play)
    {
        if (!Mine(play, owner)) return One(s);
        var count = Amount(s, owner) + 1;
        Set(s, owner, count, Data(s, owner));
        return count == Data(s, owner) + 1 ? Fire(s, owner) : One(s);
    }
}

/// <summary>
/// GremlinHorn.AfterDeath: an enemy's death gives its owner its Energy (PlayerCmd.GainEnergy), then draws its Cards (CardPileCmd.Draw),
/// queued to run once the hit or action that killed it is over (SimEngine.Raise). No tab of its own here: its tab is judged by the
/// play's outcome (a kill in every branch: ProcIndicatorsFeature); this model makes the energy and the draw happen in the simulation
/// (the play check compares the energy after each play: a dev-mode report, Unleash and Debilitate killing a segment).
/// </summary>
public sealed class GremlinHorn : SimModel
{
    public override void AfterDeath(SimState s, int owner, int dead)
    {
        if (dead < 0 || !Who.IsPlayer(owner)) return;
        SimEngine.Raise(s, CardEvent.EnergyGained, owner, SimEngine.Var(s, owner, Name, "Energy", 1));
        SimEngine.Raise(s, CardEvent.Drawn, owner, SimEngine.Var(s, owner, Name, "Cards", 1));
    }
}

/// <summary>
/// CentennialPuzzle.AfterDamageReceived: the first time this combat its owner takes damage past their block (UnblockedDamage &gt; 0),
/// draws its Cards (3), one at a time, queued until the hit is over (SimEngine.Raise). Used up for the combat (UsedThisCombat: the
/// relic's amount here). A draw tab: the cards it really draws (decision #132).
/// </summary>
public sealed class CentennialPuzzle : RelicProc
{
    public override TabKind? Tab => TabKind.Draw;
    public override void AfterDamageReceived(SimState s, int owner, DamageEvent e)
    {
        if (e.Target != owner || e.Unblocked <= 0 || !Who.IsPlayer(owner) || Amount(s, owner) != 0) return;
        Set(s, owner, 1, Data(s, owner));
        s.LogFired(Name, 0, owner);
        SimEngine.Raise(s, CardEvent.Drawn, owner, SimEngine.Var(s, owner, Name, "Cards", 3), Name);
    }
}

/// <summary>PaelsEye.BeforeCardPlayed: while armed, the turn's first card you play (not an auto-play) gives up its extra turn (bad for you).</summary>
public sealed class PaelsEye : RelicProc
{
    public override bool Harmful => true;
    /// <summary>The turn's first card clears its Active status (BeforeCardPlayed: Status = Normal); it doesn't flash.</summary>
    public override ActedSeen Seen => ActedSeen.StatusCleared;
    /// <summary>
    /// BeforeSideTurnEndEarly: still armed (no card played this turn: SimPlayer.PaelsEyeExhaust), its owner's whole hand is
    /// exhausted (Feel No Pain, Charon's Ashes... react to each card); nothing is left to act in hand or exhaust as Ethereal.
    /// </summary>
    public override IEnumerable<SimState> BeforeSideTurnEndEarly(SimState s, int owner)
    {
        if (!s.TakesPart(owner) || s.Player(owner) is not { PaelsEyeExhaust: true, HandCount: > 0 } p) return One(s);
        var cards = p.HandCount;
        return SimEngine.ProcCardEvent(s, CardEvent.Exhausted, cards, owner, indirect: false).Select(x =>
        {
            var px = owner == Who.You ? x.P : x.MutableTeammate(Who.TeammateIndex(owner));
            px.HandCount = 0;
            px.Fading = 0;
            px.TurnEndCards = new();
            return x;
        }).ToList();
    }
    public override IEnumerable<SimState> BeforeCardPlayed(SimState s, int owner, SimCardPlay play)
    {
        if (!Mine(play, owner) || play.AutoPlay || !Armed(s, owner) || SimEngine.TurnOf(s, owner)[SimKeys.Plays(PlayStage.Started)] != 0) return One(s);
        Disarm(s, owner);
        return Fire(s, owner);
    }
}

/// <summary>BiiigHug.AfterShuffle: a Soot into your draw pile on every shuffle (bad for you).</summary>
public sealed class BiiigHug : RelicProc
{
    public override bool Harmful => true;
    public override IEnumerable<SimState> AfterCardEvent(SimState s, int owner, CardEvent ev, int player) =>
        ev == CardEvent.Shuffled && player == owner ? Fire(s, owner) : One(s);
}

// ---- used up by a play

/// <summary>BurningSticks.AfterCardExhausted: once a combat, a Skill you exhaust is copied into your hand.</summary>
public sealed class BurningSticks : RelicProc
{
    public override IEnumerable<SimState> AfterCardEvent(SimState s, int owner, CardEvent ev, int player)
    {
        if (ev != CardEvent.Exhausted || player != owner || !Armed(s, owner) || s.EventCard?.Type != SimCardType.Skill) return One(s);
        Disarm(s, owner);
        return Cause(Fire(s, owner), CardEvent.Generated, 1, owner);
    }
}

/// <summary>MusicBox: once a turn, the first Attack you play (bound at BeforeCardPlayed) is copied into your hand, Ethereal.</summary>
public sealed class MusicBox : RelicProc
{
    public override IEnumerable<SimState> BeforeCardPlayed(SimState s, int owner, SimCardPlay play)
    {
        if (Mine(play, owner) && Armed(s, owner) && play.Card.Type == SimCardType.Attack && !s.Bound.Has(SimKeys.Owned(Name, owner))) Bind(s, owner, 1);
        return One(s);
    }
    public override IEnumerable<SimState> AfterCardPlayed(SimState s, int owner, SimCardPlay play)
    {
        if (!Mine(play, owner) || TakeBound(s, owner) is null) return One(s);
        Disarm(s, owner);
        return Cause(Fire(s, owner), CardEvent.Generated, 1, owner);
    }
}

/// <summary>
/// UnsettlingLamp: while armed, the first card of yours that applies a debuff to an enemy without Artifact (BeforePowerAmountChanged)
/// has its debuffs doubled (ModifyPowerAmountGivenMultiplicative); AfterCardPlayed of that card uses it up. Data bit 2: bound.
/// </summary>
public sealed class UnsettlingLamp : RelicProc
{
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
    public override TabKind? Tab => TabKind.Block;
    public override IEnumerable<SimState> BeforeCardPlayed(SimState s, int owner, SimCardPlay play) =>
        Mine(play, owner) && play.Card.Energy >= Var(s, owner, "Energy", 2) ? RelicBlock(s, owner) : One(s);
}

/// <summary>DaughterOfTheWind.AfterCardPlayed: block for each Attack you play.</summary>
public sealed class DaughterOfTheWind : RelicProc
{
    public override TabKind? Tab => TabKind.Block;
    public override IEnumerable<SimState> AfterCardPlayed(SimState s, int owner, SimCardPlay play) =>
        Mine(play, owner) && play.Card.Type == SimCardType.Attack ? RelicBlock(s, owner) : One(s);
}

/// <summary>Permafrost.AfterCardPlayed: block for the first Power you play each combat.</summary>
public sealed class Permafrost : RelicProc
{
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
    public override TabKind? Tab => TabKind.Damage;
    public override IEnumerable<SimState> AfterCardPlayed(SimState s, int owner, SimCardPlay play) =>
        Mine(play, owner) && play.Card.Type == SimCardType.Power ? RelicDamageAll(s, owner) : One(s);
}

/// <summary>GamePiece.AfterCardPlayed: a draw for each Power you play.</summary>
public sealed class GamePiece : RelicProc
{
    public override TabKind? Tab => TabKind.Draw;
    public override IEnumerable<SimState> AfterCardPlayed(SimState s, int owner, SimCardPlay play) =>
        Mine(play, owner) && play.Card.Type == SimCardType.Power ? Cause(Fire(s, owner), CardEvent.Drawn, 1, owner) : One(s);
}

/// <summary>
/// MummifiedHand.AfterCardPlayed: each Power you play makes a random other card in your hand free this turn (it prefers one that
/// still costs something; with none, nothing changes).
/// </summary>
public sealed class MummifiedHand : RelicProc
{
    public override IEnumerable<SimState> AfterCardPlayed(SimState s, int owner, SimCardPlay play) =>
        Mine(play, owner) && play.Card.Type == SimCardType.Power && play.Card.OthersInHandCost ? Fire(s, owner) : One(s);
}

/// <summary>HelicalDart.AfterCardPlayed: each Shiv you play gives Dexterity this turn.</summary>
public sealed class HelicalDart : RelicProc
{
    public override IEnumerable<SimState> AfterCardPlayed(SimState s, int owner, SimCardPlay play) =>
        Mine(play, owner) && play.Card.Is("Shiv") ? Fire(s, owner) : One(s);
}

/// <summary>IvoryTile.AfterCardPlayed: energy for each card you play costing at least its EnergyThreshold.</summary>
public sealed class IvoryTile : RelicProc
{
    public override TabKind? Tab => TabKind.Energy;
    public override IEnumerable<SimState> AfterCardPlayed(SimState s, int owner, SimCardPlay play) =>
        Mine(play, owner) && play.Card.Energy >= Var(s, owner, "EnergyThreshold", 3)
            ? Cause(Fire(s, owner), CardEvent.EnergyGained, Var(s, owner, "Energy", 1), owner) : One(s);
}

/// <summary>ChemicalX.ModifyXValue: X-cost cards (energy or stars) get more.</summary>
public sealed class ChemicalX : RelicProc
{
    public override IEnumerable<SimState> BeforeCardPlayed(SimState s, int owner, SimCardPlay play) =>
        Mine(play, owner) && play.PlayIndex == 0 && (play.Card.CostsX || play.Card.StarCostX) ? Fire(s, owner) : One(s);
}

/// <summary>RazorTooth.AfterCardPlayed: an Attack or Skill you play that can be upgraded is upgraded.</summary>
public sealed class RazorTooth : RelicProc
{
    public override IEnumerable<SimState> AfterCardPlayed(SimState s, int owner, SimCardPlay play) =>
        Mine(play, owner) && play.PlayIndex == 0 && play.Card.Type is SimCardType.Attack or SimCardType.Skill && play.Card.Upgradable ? Fire(s, owner) : One(s);
}

/// <summary>RainbowRing.AfterCardPlayed: once a turn (Data 2: done), the play that completes an Attack, a Skill and a Power this turn.</summary>
public sealed class RainbowRing : RelicProc
{
    public override IEnumerable<SimState> AfterCardPlayed(SimState s, int owner, SimCardPlay play)
    {
        if (!Mine(play, owner) || (Data(s, owner) & 2) != 0) return One(s);
        var turn = SimEngine.TurnOf(s, owner);
        int Of(SimCardType t) => turn[SimKeys.Plays(PlayStage.Finished, t)] - (play.Card.Type == t ? 1 : 0);
        var all = new[] { SimCardType.Attack, SimCardType.Skill, SimCardType.Power }.All(t => turn[SimKeys.Plays(PlayStage.Finished, t)] > 0);
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
    public override decimal ModifyDamageMultiplicative(SimState s, int owner, DamageCtx d) =>
        d.Target == owner && d.Props.IsPoweredAttack() ? 0.75m : 1m;
}

/// <summary>Twine.AfterArrowDislodged: block for each of your arrows that comes loose.</summary>
public sealed class Twine : RelicProc
{
    public override TabKind? Tab => TabKind.Block;
    public override IEnumerable<SimState> AfterCardEvent(SimState s, int owner, CardEvent ev, int player) =>
        ev == CardEvent.ArrowDislodged && player == owner ? RelicBlock(s, owner) : One(s);
}

/// <summary>UnceasingTop.AfterHandEmptied: a draw when your last card leaves your hand (a play that draws or creates nothing).</summary>
public sealed class UnceasingTop : RelicProc
{
    public override IEnumerable<SimState> AfterCardPlayed(SimState s, int owner, SimCardPlay play) =>
        Mine(play, owner) && play.PlayIndex == 0 && s.Player(owner) is { HandCount: 1 }
        && s.PlayEvents[CardEvent.Drawn] == 0 && s.PlayEvents[CardEvent.Generated] + s.PlayEvents[CardEvent.GeneratedStatus] + s.PlayEvents[CardEvent.GeneratedSadness] == 0
            ? Fire(s, owner) : One(s);
}

// ---- counters

public sealed class Kunai : CounterRelic
{
    protected override bool Counts(SimCard card) => card.Type == SimCardType.Attack;
    protected override IEnumerable<SimState> OnComplete(SimState s, int owner)
    {
        SimEngine.ApplyPower(s, owner, owner, "DexterityPower", Var(s, owner, "Dexterity", 1), debuff: false);
        return Fire(s, owner);
    }
}

public sealed class Shuriken : CounterRelic
{
    protected override bool Counts(SimCard card) => card.Type == SimCardType.Attack;
    protected override IEnumerable<SimState> OnComplete(SimState s, int owner)
    {
        SimEngine.ApplyPower(s, owner, owner, "StrengthPower", Var(s, owner, "Strength", 1), debuff: false);
        return Fire(s, owner);
    }
}

public sealed class Nunchaku : CounterRelic
{
    public override TabKind? Tab => TabKind.Energy;
    protected override bool Counts(SimCard card) => card.Type == SimCardType.Attack;
    protected override IEnumerable<SimState> OnComplete(SimState s, int owner) => Cause(Fire(s, owner), CardEvent.EnergyGained, Var(s, owner, "Energy", 1), owner);
}

public sealed class OrnamentalFan : CounterRelic
{
    public override TabKind? Tab => TabKind.Block;
    protected override bool Counts(SimCard card) => card.Type == SimCardType.Attack;
    protected override IEnumerable<SimState> OnComplete(SimState s, int owner) => RelicBlock(s, owner);
}

public sealed class Kusarigama : CounterRelic
{
    public override TabKind? Tab => TabKind.Damage;
    protected override bool Counts(SimCard card) => card.Type == SimCardType.Attack;
    protected override IEnumerable<SimState> OnComplete(SimState s, int owner) => RelicDamageRandom(s, owner);
}

public sealed class LetterOpener : CounterRelic
{
    public override TabKind? Tab => TabKind.Damage;
    protected override bool Counts(SimCard card) => card.Type == SimCardType.Skill;
    protected override IEnumerable<SimState> OnComplete(SimState s, int owner) => RelicDamageAll(s, owner);
}

public sealed class TuningFork : CounterRelic
{
    public override TabKind? Tab => TabKind.Block;
    protected override bool Counts(SimCard card) => card.Type == SimCardType.Skill;
    protected override IEnumerable<SimState> OnComplete(SimState s, int owner) => RelicBlock(s, owner);
}

public sealed class IronClub : CounterRelic
{
    public override TabKind? Tab => TabKind.Draw;
    protected override bool Counts(SimCard card) => true;
    protected override IEnumerable<SimState> OnComplete(SimState s, int owner) => Cause(Fire(s, owner), CardEvent.Drawn, 1, owner);
}

// ---- card events

/// <summary>CharonsAshes.AfterCardExhausted: damage to every hittable enemy for each card you exhaust.</summary>
public sealed class CharonsAshes : RelicProc
{
    public override TabKind? Tab => TabKind.Damage;
    public override IEnumerable<SimState> AfterCardEvent(SimState s, int owner, CardEvent ev, int player) =>
        ev == CardEvent.Exhausted && player == owner ? DamageAll(s, owner, Amount(s, owner), PropsOf(s, owner, "Damage")) : One(s);
}

/// <summary>ForgottenSoul.AfterCardExhausted: damage to a random hittable enemy for each card you exhaust.</summary>
public sealed class ForgottenSoul : RelicProc
{
    public override TabKind? Tab => TabKind.Damage;
    public override IEnumerable<SimState> AfterCardEvent(SimState s, int owner, CardEvent ev, int player) =>
        ev == CardEvent.Exhausted && player == owner ? DamageRandom(s, owner, Amount(s, owner), PropsOf(s, owner, "Damage")) : One(s);
}

/// <summary>JossPaper.AfterCardExhausted: a draw once its ExhaustAmount cards are exhausted (Amount: CardsExhausted).</summary>
public sealed class JossPaper : RelicProc
{
    public override TabKind? Tab => TabKind.Draw;
    public override IEnumerable<SimState> AfterCardEvent(SimState s, int owner, CardEvent ev, int player)
    {
        if (ev != CardEvent.Exhausted || player != owner) return One(s);
        var n = Var(s, owner, "ExhaustAmount", 5);
        var count = Amount(s, owner) + 1;
        if (count < n) { Set(s, owner, count, Data(s, owner)); return One(s); }
        Set(s, owner, count - n, Data(s, owner));
        return Cause(Fire(s, owner), CardEvent.Drawn, 1, owner);
    }
}

/// <summary>Tingsha.AfterCardDiscarded: damage to a random hittable enemy for each card you discard.</summary>
public sealed class Tingsha : RelicProc
{
    public override TabKind? Tab => TabKind.Damage;
    public override IEnumerable<SimState> AfterCardEvent(SimState s, int owner, CardEvent ev, int player) =>
        ev == CardEvent.Discarded && player == owner ? RelicDamageRandom(s, owner) : One(s);
}

/// <summary>ToughBandages.AfterCardDiscarded: block for each card you discard.</summary>
public sealed class ToughBandages : RelicProc
{
    public override TabKind? Tab => TabKind.Block;
    public override IEnumerable<SimState> AfterCardEvent(SimState s, int owner, CardEvent ev, int player) =>
        ev == CardEvent.Discarded && player == owner ? RelicBlock(s, owner) : One(s);
}

/// <summary>MiniRegent.AfterStarsSpent: once a turn (armed), Strength when you spend stars.</summary>
public sealed class MiniRegent : RelicProc
{
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
    public override TabKind? Tab => TabKind.Block;
    public override IEnumerable<SimState> AfterCardEvent(SimState s, int owner, CardEvent ev, int player) =>
        ev == CardEvent.Shuffled && player == owner ? RelicBlock(s, owner) : One(s);
}

/// <summary>WhittlingKnife.AfterShuffle: Aim on every shuffle.</summary>
public sealed class WhittlingKnife : RelicProc
{
    public override IEnumerable<SimState> AfterCardEvent(SimState s, int owner, CardEvent ev, int player) =>
        ev == CardEvent.Shuffled && player == owner ? Fire(s, owner) : One(s);
}

/// <summary>Fertilizer.AfterCardGeneratedForCombat: Strength when you create a Sadness.</summary>
public sealed class Fertilizer : RelicProc
{
    public override IEnumerable<SimState> AfterCardEvent(SimState s, int owner, CardEvent ev, int player) =>
        ev == CardEvent.GeneratedSadness && player == owner ? Fire(s, owner) : One(s);
}

/// <summary>Metronome.AfterOrbChanneled: damage to every hittable enemy once its OrbCount orbs are channeled this combat (Amount: OrbsChanneled).</summary>
public sealed class Metronome : RelicProc
{
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
    public override TabKind? Tab => TabKind.Block;
    public override IEnumerable<SimState> AfterAttack(SimState s, int owner, AttackInfo attack) =>
        owner == Who.You && attack.Dealer == Who.YourOsty && s.P.OstyHp > 0 ? RelicBlock(s, owner) : One(s);
}
