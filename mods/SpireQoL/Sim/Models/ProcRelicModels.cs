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
    public override IEnumerable<SimState> BeforeCardPlayed(SimState state, int owner, SimCardPlay play) =>
        Mine(play, owner) && play.Card.Type == SimCardType.Attack && SimEngine.TurnOf(state, owner)[SimKeys.Plays(PlayStage.Started, SimCardType.Attack)] == 0 ? Fire(state, owner) : One(state);
}

public sealed class ArtOfWar : FirstAttackGivesUp { }
/// <summary>RippleBasin: also BeforeSideTurnEnd: its block (amount) when its owner played no Attack this turn.</summary>
public sealed class RippleBasin : FirstAttackGivesUp
{
    public override IEnumerable<SimState> BeforeSideTurnEnd(SimState state, int owner) =>
        state.TakesPart(owner) && state.Player(owner) is { AttacksThisTurn: 0 } && Amount(state, owner) > 0 ? Block(state, owner, Amount(state, owner)) : One(state);
}

/// <summary>
/// PaelsTears (BeforeSideTurnEnd: any energy left gives energy next turn): a play that spends energy and leaves you with none gives
/// that up (bad for you). Judged once the play has resolved (AfterPlayResolved), so energy the play gets back first (Orbit, Ivory
/// Tile, Subroutine, the card's own) keeps the bonus and shows no warning. Data: energy was spent during this play (1), not the armed bit.
/// </summary>
public sealed class PaelsTears : RelicProc
{
    /// <summary>Its Data: energy was spent during the play under way (none: 0).</summary>
    private const int SpentThisPlay = 1;
    public override bool Harmful => true;
    public override ActedSeen Seen => ActedSeen.NoEnergyLeft;
    public override IEnumerable<SimState> AfterCardEvent(SimState state, int owner, CardEvent cardEvent, int player)
    {
        if (cardEvent == CardEvent.EnergySpent && player == owner && Data(state, owner) == 0) Set(state, owner, Amount(state, owner), data: SpentThisPlay);
        return One(state);
    }
    public override IEnumerable<SimState> AfterPlayResolved(SimState state, int owner, int player)
    {
        if (player != owner || Data(state, owner) == 0) return One(state);
        Set(state, owner, Amount(state, owner), data: 0);
        return state.Player(owner) is { Energy: <= 0 } ? Fire(state, owner) : One(state);
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
    public override IEnumerable<SimState> AfterCardPlayed(SimState state, int owner, SimCardPlay play)
    {
        if (!Mine(play, owner)) return One(state);
        var count = Amount(state, owner) + 1;
        Set(state, owner, count, Data(state, owner));
        return count == Data(state, owner) + 1 ? Fire(state, owner) : One(state);
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
    public override IEnumerable<SimState> BeforeSideTurnEndEarly(SimState state, int owner)
    {
        if (!state.TakesPart(owner) || state.Player(owner) is not { PaelsEyeExhaust: true, HandCount: > 0 } player) return One(state);
        var cards = player.HandCount;
        return SimEngine.ProcCardEvent(state, CardEvent.Exhausted, cards, owner, indirect: false).Select(branch =>
        {
            var simPlayer = branch.MutablePlayer(owner);
            simPlayer.HandCount = 0;
            simPlayer.Fading = 0;
            simPlayer.TurnEndCards = new();
            return branch;
        }).ToList();
    }
    public override IEnumerable<SimState> BeforeCardPlayed(SimState state, int owner, SimCardPlay play)
    {
        if (!Mine(play, owner) || play.AutoPlay || !Armed(state, owner) || SimEngine.TurnOf(state, owner)[SimKeys.Plays(PlayStage.Started)] != 0) return One(state);
        Disarm(state, owner);
        return Fire(state, owner);
    }
}

/// <summary>BiiigHug.AfterShuffle: a Soot into your draw pile at a random spot on every shuffle (bad for you), where a later draw can find it.</summary>
public sealed class BiiigHug : RelicProc
{
    public override bool Harmful => true;
    public override IEnumerable<SimState> AfterCardEvent(SimState state, int owner, CardEvent cardEvent, int player) =>
        cardEvent == CardEvent.Shuffled && player == owner ? Fire(state, owner).SelectMany(branch => SimEngine.IntoDrawPile(branch, owner, CardId.Soot.Name)).ToList() : One(state);
}

// ---- used up by a play

/// <summary>BurningSticks.AfterCardExhausted: once a combat, a Skill you exhaust is copied into your hand.</summary>
public sealed class BurningSticks : RelicProc
{
    public override IEnumerable<SimState> AfterCardEvent(SimState state, int owner, CardEvent cardEvent, int player)
    {
        if (cardEvent != CardEvent.Exhausted || player != owner || !Armed(state, owner) || state.EventCard?.Type != SimCardType.Skill) return One(state);
        Disarm(state, owner);
        return MakeInHand(Fire(state, owner), CardEvent.Generated, count: 1, owner);
    }
}

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
        return MakeInHand(Fire(state, owner), CardEvent.Generated, count: 1, owner, ethereal: true);
    }
}

/// <summary>
/// UnsettlingLamp: while armed, the first card of yours that applies a debuff to an enemy without Artifact (BeforePowerAmountChanged)
/// has its debuffs doubled (ModifyPowerAmountGivenMultiplicative); AfterCardPlayed of that card uses it up. Data bit 2: bound.
/// </summary>
public sealed class UnsettlingLamp : RelicProc
{
    private const int BoundBit = 2;
    public override int ModifyPowerAmountGiven(SimState state, int owner, PowerId power, int amount, int? applier, int target, bool debuff)
    {
        if (!debuff || applier != owner || target < 0 || state.CurrentPlay is not { } play || play.Owner != owner) return amount;
        var data = Data(state, owner);
        if ((data & BoundBit) == 0)
        {
            if (!Armed(state, owner) || SimEngine.PowerOn(state, target, PowerId.Artifact) > 0) return amount;
            Set(state, owner, Amount(state, owner), data | BoundBit);
        }
        return amount * 2;
    }
    public override IEnumerable<SimState> AfterCardPlayed(SimState state, int owner, SimCardPlay play)
    {
        if (!Mine(play, owner) || (Data(state, owner) & BoundBit) == 0) return One(state);
        Set(state, owner, Amount(state, owner), data: 0);
        return Fire(state, owner);
    }
}

// ---- on play

/// <summary>IntimidatingHelmet.BeforeCardPlayed: block for each card you play costing at least its Energy value.</summary>
public sealed class IntimidatingHelmet : RelicProc
{
    public override TabKind? Tab => TabKind.Block;
    public override IEnumerable<SimState> BeforeCardPlayed(SimState state, int owner, SimCardPlay play) =>
        Mine(play, owner) && play.Card.Energy >= Var(state, owner, "Energy", fallback: 2) ? RelicBlock(state, owner) : One(state);
}

/// <summary>DaughterOfTheWind.AfterCardPlayed: block for each Attack you play.</summary>
public sealed class DaughterOfTheWind : RelicProc
{
    public override TabKind? Tab => TabKind.Block;
    public override IEnumerable<SimState> AfterCardPlayed(SimState state, int owner, SimCardPlay play) =>
        Mine(play, owner) && play.Card.Type == SimCardType.Attack ? RelicBlock(state, owner) : One(state);
}

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

/// <summary>
/// MummifiedHand.AfterCardPlayed: each Power you play makes a random other card in your hand free this turn (it prefers one that
/// still costs something; with none, nothing changes).
/// </summary>
public sealed class MummifiedHand : RelicProc
{
    public override IEnumerable<SimState> AfterCardPlayed(SimState state, int owner, SimCardPlay play) =>
        Mine(play, owner) && play.Card.Type == SimCardType.Power && play.Card.OthersInHandCost ? Fire(state, owner) : One(state);
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

/// <summary>ChemicalX.ModifyXValue: X-cost cards (energy or stars) get more.</summary>
public sealed class ChemicalX : RelicProc
{
    public override IEnumerable<SimState> BeforeCardPlayed(SimState state, int owner, SimCardPlay play) =>
        Mine(play, owner) && play.PlayIndex == 0 && (play.Card.CostsX || play.Card.StarCostX) ? Fire(state, owner) : One(state);
}

/// <summary>RazorTooth.AfterCardPlayed: an Attack or Skill you play that can be upgraded is upgraded.</summary>
public sealed class RazorTooth : RelicProc
{
    public override IEnumerable<SimState> AfterCardPlayed(SimState state, int owner, SimCardPlay play) =>
        Mine(play, owner) && play.PlayIndex == 0 && play.Card.Type is SimCardType.Attack or SimCardType.Skill && play.Card.Upgradable ? Fire(state, owner) : One(state);
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
/// ConductorsBatonPower.ModifyDamageMultiplicative: its DamageDecrease (0.75) on powered attacks on its owner; it goes at the
/// start of the owner's next turn (AfterSideTurnStart), so it lasts through the enemy turn.
/// </summary>
public sealed class ConductorsBatonPower : SimModel
{
    public override decimal ModifyDamageMultiplicative(SimState state, int owner, DamageCtx damage) =>
        damage.Target == owner && damage.Props.IsPoweredAttack() ? 0.75m : 1m;
}

/// <summary>Twine.AfterArrowDislodged: block for each of your arrows that comes loose.</summary>
public sealed class Twine : RelicProc
{
    public override TabKind? Tab => TabKind.Block;
    public override IEnumerable<SimState> AfterCardEvent(SimState state, int owner, CardEvent cardEvent, int player) =>
        cardEvent == CardEvent.ArrowDislodged && player == owner ? RelicBlock(state, owner) : One(state);
}

/// <summary>
/// UnceasingTop.AfterHandEmptied: a draw (CardPileCmd.Draw) when a play leaves your hand empty. The game checks once the card has
/// resolved (CardModel: CombatManager.CheckForEmptyHand after it reaches its pile) and only while no other card's effect is running,
/// in your turn's play and auto-play phases (IsValidPhase: Stampede's plays at the end of your turn count): your hand holds nothing and
/// no card of yours is still being played. The card it draws is drawn, with what reacts to draws.
/// </summary>
public sealed class UnceasingTop : RelicProc
{
    public override IEnumerable<SimState> AfterPlayResolved(SimState state, int owner, int player) =>
        player == owner && !state.EnemyTurnNow && state.Player(owner) is { HandCount: 0, PlayingFromHand: 0 }
            ? Cause(Fire(state, owner), CardEvent.Drawn, count: 1, owner)
            : One(state);
}

// ---- counters

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

// ---- card events

/// <summary>CharonsAshes.AfterCardExhausted: damage to every hittable enemy for each card you exhaust.</summary>
public sealed class CharonsAshes : RelicProc
{
    public override TabKind? Tab => TabKind.Damage;
    public override IEnumerable<SimState> AfterCardEvent(SimState state, int owner, CardEvent cardEvent, int player) =>
        cardEvent == CardEvent.Exhausted && player == owner ? DamageAll(state, owner, Amount(state, owner), PropsOf(state, owner, "Damage")) : One(state);
}

/// <summary>ForgottenSoul.AfterCardExhausted: damage to a random hittable enemy for each card you exhaust.</summary>
public sealed class ForgottenSoul : RelicProc
{
    public override TabKind? Tab => TabKind.Damage;
    public override IEnumerable<SimState> AfterCardEvent(SimState state, int owner, CardEvent cardEvent, int player) =>
        cardEvent == CardEvent.Exhausted && player == owner ? DamageRandom(state, owner, Amount(state, owner), PropsOf(state, owner, "Damage")) : One(state);
}

/// <summary>JossPaper.AfterCardExhausted: a draw once its ExhaustAmount cards are exhausted (Amount: CardsExhausted).</summary>
public sealed class JossPaper : RelicProc
{
    public override TabKind? Tab => TabKind.Draw;
    public override IEnumerable<SimState> AfterCardEvent(SimState state, int owner, CardEvent cardEvent, int player)
    {
        if (cardEvent != CardEvent.Exhausted || player != owner) return One(state);
        var needed = Var(state, owner, "ExhaustAmount", fallback: 5);
        var count = Amount(state, owner) + 1;
        if (count < needed) { Set(state, owner, count, Data(state, owner)); return One(state); }
        Set(state, owner, count - needed, Data(state, owner));
        return Cause(Fire(state, owner), CardEvent.Drawn, count: 1, owner);
    }
}

/// <summary>Tingsha.AfterCardDiscarded: damage to a random hittable enemy for each card you discard.</summary>
public sealed class Tingsha : RelicProc
{
    public override TabKind? Tab => TabKind.Damage;
    public override IEnumerable<SimState> AfterCardEvent(SimState state, int owner, CardEvent cardEvent, int player) =>
        cardEvent == CardEvent.Discarded && player == owner ? RelicDamageRandom(state, owner) : One(state);
}

/// <summary>ToughBandages.AfterCardDiscarded: block for each card you discard.</summary>
public sealed class ToughBandages : RelicProc
{
    public override TabKind? Tab => TabKind.Block;
    public override IEnumerable<SimState> AfterCardEvent(SimState state, int owner, CardEvent cardEvent, int player) =>
        cardEvent == CardEvent.Discarded && player == owner ? RelicBlock(state, owner) : One(state);
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

/// <summary>BoneFlute.AfterAttack: block for each attack your Osty makes.</summary>
public sealed class BoneFlute : RelicProc
{
    public override TabKind? Tab => TabKind.Block;
    public override IEnumerable<SimState> AfterAttack(SimState state, int owner, AttackInfo attack) =>
        owner == Who.You && attack.Dealer == Who.YourOsty && state.You.OstyHp > 0 ? RelicBlock(state, owner) : One(state);
}
