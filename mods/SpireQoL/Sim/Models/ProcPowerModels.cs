namespace SpireQoL.Sim.Models;

// Your powers that act on your plays and card events (decompile v0.113.1). Amounts bound at BeforeCardPlayed act at
// AfterCardPlayed, as the game keeps them (amountsForPlayedCards), so a power the play applies doesn't fire on that play.

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

/// <summary>SerpentFormPower: damage (unpowered) to a random hittable enemy for each card its owner plays.</summary>
public sealed class SerpentFormPower : BoundPower
{
    public override TabKind? Tab => TabKind.Damage;
    protected override IEnumerable<SimState> Act(SimState state, int owner, SimCardPlay play, int amount) => amount > 0 ? DamageRandom(state, owner, amount) : One(state);
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

/// <summary>CalamityPower: each Attack its owner plays adds its amount of random Attacks to their hand (cards created).</summary>
public sealed class CalamityPower : BoundPower
{
    protected override bool Binds(SimCardPlay play) => play.Card.Type == SimCardType.Attack;
    protected override IEnumerable<SimState> Act(SimState state, int owner, SimCardPlay play, int amount) => MakeInHand(Fire(state, owner), CardEvent.Generated, amount, owner);
}

/// <summary>
/// MonologuePower: each card its owner plays gives Strength (its Strength value, DynamicVars.Strength kept as "StrengthPower", bound
/// at the play's start).
/// </summary>
public sealed class MonologuePower : BoundPower
{
    protected override int BoundAmount(SimState state, int owner) => Var(state, owner, "StrengthPower", fallback: 1);
    protected override IEnumerable<SimState> Act(SimState state, int owner, SimCardPlay play, int amount)
    {
        SimEngine.ApplyPower(state, owner, owner, PowerId.Strength, amount, debuff: false);
        return Fire(state, owner);
    }
}

/// <summary>
/// JugglingPower: the third Attack its owner plays each turn (its own count, Data) is copied into their hand, its amount of times
/// (BeforeCardPlayed counts and binds the third; AfterCardPlayed adds the copies). Main (MainRule.JugglingCopiesBeforeThePlay) adds them as
/// it counts, before the card resolves.
/// </summary>
public sealed class JugglingPower : ProcModel
{
    public override IEnumerable<SimState> BeforeCardPlayed(SimState state, int owner, SimCardPlay play)
    {
        if (!Mine(play, owner) || play.Card.Type != SimCardType.Attack) return One(state);
        var count = Data(state, owner) + 1;
        Set(state, owner, Amount(state, owner), count);
        if (count != 3) return One(state);
        if (state.MainRules.HasFlag(MainRule.JugglingCopiesBeforeThePlay)) return MakeInHand(Fire(state, owner), CardEvent.Generated, Amount(state, owner), owner);
        Bind(state, owner, Amount(state, owner));
        return One(state);
    }
    public override IEnumerable<SimState> AfterCardPlayed(SimState state, int owner, SimCardPlay play) =>
        Mine(play, owner) && TakeBound(state, owner) is { } bound ? MakeInHand(Fire(state, owner), CardEvent.Generated, bound, owner) : One(state);
}

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

/// <summary>TheSealedThronePower.BeforeCardPlayed: each card its owner plays gains them stars (Black Hole reacts).</summary>
public sealed class TheSealedThronePower : ProcModel
{
    public override IEnumerable<SimState> BeforeCardPlayed(SimState state, int owner, SimCardPlay play) =>
        Mine(play, owner) && Amount(state, owner) > 0 ? Cause(Fire(state, owner), CardEvent.StarsGained, count: 1, owner) : One(state);
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

/// <summary>HauntPower.AfterCardPlayed: each Soul its owner plays deals its amount to a random hittable enemy (unblockable, unpowered, no dealer).</summary>
public sealed class HauntPower : ProcModel
{
    public override TabKind? Tab => TabKind.Damage;
    public override IEnumerable<SimState> AfterCardPlayed(SimState state, int owner, SimCardPlay play) =>
        Mine(play, owner) && play.Card.Name == CardId.Soul.Name
            ? SimEngine.ProcDamageRandom(state, owner, Name, Amount(state, owner), SimProps.Unblockable | Unpowered, noDealer: true)
            : One(state);
}

/// <summary>MasterPlannerPower.AfterCardPlayed: each Skill its owner plays gains Sly.</summary>
public sealed class MasterPlannerPower : ProcModel
{
    public override IEnumerable<SimState> AfterCardPlayed(SimState state, int owner, SimCardPlay play) =>
        Mine(play, owner) && play.Card.Type == SimCardType.Skill ? Fire(state, owner) : One(state);
}

/// <summary>SmoggyPower.AfterCardPlayed: each Skill its owner plays afflicts their other Skills with Smog (bad for you).</summary>
public sealed class SmoggyPower : ProcModel
{
    public override bool Harmful => true;
    public override IEnumerable<SimState> AfterCardPlayed(SimState state, int owner, SimCardPlay play) =>
        Mine(play, owner) && play.Card.Type == SimCardType.Skill ? Fire(state, owner) : One(state);
}

/// <summary>
/// FeralPower.ModifyCardPlayResultLocation: an Attack its owner plays for 0 energy (not a copy) goes back to their hand while it
/// has charges left this turn (Data: its DisplayAmount): it keeps its slot, where the card would have left the hand at CardResolved.
/// </summary>
public sealed class FeralPower : ProcModel
{
    public override IEnumerable<SimState> AfterCardPlayed(SimState state, int owner, SimCardPlay play)
    {
        if (!Mine(play, owner) || play.Card.Type != SimCardType.Attack || play.Card.Energy != 0 || play.Card.Dupe) return One(state);
        var left = Data(state, owner);
        if (left <= 0) return One(state);
        Set(state, owner, Amount(state, owner), left - 1);
        SimEngine.AddToHand(state, owner, count: 1);
        return Fire(state, owner);
    }
}

/// <summary>
/// NostalgiaPower.ModifyCardPlayResultLocation: its owner's first Amount attacks and skills each turn (plays started before this
/// one) go on top of their draw pile, unless they exhaust.
/// </summary>
public sealed class NostalgiaPower : ProcModel
{
    public override IEnumerable<SimState> AfterCardPlayed(SimState state, int owner, SimCardPlay play)
    {
        if (!Mine(play, owner) || play.Card.Type is not (SimCardType.Attack or SimCardType.Skill) || play.Card.ExhaustsSelf) return One(state);
        var turn = SimEngine.TurnOf(state, owner);
        var before = turn[SimKeys.Plays(PlayStage.Started, SimCardType.Attack)] + turn[SimKeys.Plays(PlayStage.Started, SimCardType.Skill)] - 1;
        return before < Amount(state, owner) ? Fire(state, owner) : One(state);
    }
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

/// <summary>ImitationLearningPower (a teammate's, Data = the player it copies + 100): a Power card that player plays is copied and played for its owner.</summary>
public sealed class ImitationLearningPower : ProcModel
{
    public override IEnumerable<SimState> AfterCardPlayed(SimState state, int owner, SimCardPlay play) =>
        play.Owner != owner && DataCreature.In(Data(state, owner)) == play.Owner && play.Card.Type == SimCardType.Power ? Fire(state, owner) : One(state);
}

// ---- the Fool (v0.113.0): melodies (card tags Wah, Cho, Ki) and arrows

/// <summary>The melodies (Wah, Cho, Ki) and what a play does to them, from its owner's turn counters.</summary>
internal static class Melodies
{
    public static readonly SimCardTag[] Tags = { SimCardTag.Wah, SimCardTag.Cho, SimCardTag.Ki };

    /// <summary>The play is the first this turn with that tag (MelodyCmd.IsFirstPlayThisTurn: the only play started with it).</summary>
    public static bool First(SimState state, SimCardPlay play, SimCardTag tag) => play.Card.Is(tag) && SimEngine.TurnOf(state, play.Owner)[SimKeys.Plays(PlayStage.Started, tag)] == 1;

    public static bool AnyFirst(SimState state, SimCardPlay play) => Tags.Any(simCardTag => First(state, play, simCardTag));

    /// <summary>Melodies finished this turn, with this play (AfterCardPlayed) and without it.</summary>
    public static (int Before, int After) Finished(SimState state, SimCardPlay play)
    {
        var turn = SimEngine.TurnOf(state, play.Owner);
        var after = Tags.Count(simCardTag => turn[SimKeys.Plays(PlayStage.Finished, simCardTag)] > 0);
        var before = Tags.Count(simCardTag => turn[SimKeys.Plays(PlayStage.Finished, simCardTag)] - (play.Card.Is(simCardTag) ? 1 : 0) > 0);
        return (before, after);
    }
}

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
/// LostLutePower.AfterCardPlayed: each melody (Wah, Cho, Ki) the card is the first this turn to play auto-plays its amount of cards
/// from the top of your draw pile (CardPileCmd.AutoPlayFromDrawPile(num * Amount, Top)).
/// </summary>
public sealed class LostLutePower : ProcModel
{
    public override IEnumerable<SimState> AfterCardPlayed(SimState state, int owner, SimCardPlay play) =>
        Mine(play, owner) && Melodies.Tags.Count(simCardTag => Melodies.First(state, play, simCardTag)) is var tagCount and > 0
            ? Fire(state, owner).SelectMany(branch => SimEngine.AutoPlayFromDraw(branch, owner, tagCount * Amount(branch, owner), forceExhaust: false)).ToList()
            : One(state);
}

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

/// <summary>
/// ImpaledPower (yours, on an enemy: Amount = your arrows in it, Data = its HitCounter): every third powered hit of yours knocks an
/// arrow out (AfterDamageReceived: the counter starts over at 3), and its death lets all of them go (AfterDeath). Each comes back to
/// you and sets off AfterArrowDislodged, once the hit is over.
/// </summary>
public sealed class ImpaledPower : SimModel
{
    /// <summary>The hits each arrow takes to come loose (ImpaledPower's HitCounter, CanonicalVars).</summary>
    private const int HitsPerArrow = 3;
    public override void AfterDamageReceived(SimState state, int owner, DamageEvent hit)
    {
        if (hit.Target != owner || hit.Dealer != Who.You || !hit.Props.IsPoweredAttack()) return;
        var powers = SimEngine.PowersOf(state, owner);
        if (powers[ThisPower] <= 0) return;
        var counter = powers.DataOf(ThisPower) - 1;
        if (counter > 0) { SimEngine.SetPowers(state, owner, powers.With(ThisPower, powers[ThisPower], counter)); return; }
        SimEngine.SetPowers(state, owner, powers.With(ThisPower, powers[ThisPower], data: HitsPerArrow));
        SimEngine.TakeArrows(state, owner, count: 1);
        SimEngine.Raise(state, CardEvent.ArrowDislodged, Who.You);
    }
    public override void AfterDeath(SimState state, int owner, int dead)
    {
        var arrows = SimEngine.PowerOn(state, owner, ThisPower);
        if (dead != owner || arrows <= 0) return;
        SimEngine.TakeArrows(state, owner, arrows);
        SimEngine.Raise(state, CardEvent.ArrowDislodged, Who.You, arrows);
    }
}

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

// ---- Sovereign Blade's buffs (the card text lists them, but it's easy to miss)

/// <summary>A buff that changes what Sovereign Blade does (Seeking Edge: every enemy; Parry: block; Sword Sage: replays).</summary>
public abstract class SovereignBladeBuff : ProcModel
{
    public override IEnumerable<SimState> AfterCardPlayed(SimState state, int owner, SimCardPlay play) =>
        Mine(play, owner) && play.Card.Name == CardId.SovereignBlade.Name && play.PlayIndex == 0 ? Fire(state, owner) : One(state);
}

public sealed class SeekingEdgePower : SovereignBladeBuff { }
public sealed class ParryPower : SovereignBladeBuff { }
public sealed class SwordSagePower : SovereignBladeBuff { }

/// <summary>ConquerorPower (on an enemy): Sovereign Blade deals it more damage.</summary>
public sealed class ConquerorPower : ProcModel
{
    public override IEnumerable<SimState> AfterCardPlayed(SimState state, int owner, SimCardPlay play) =>
        owner >= 0 && play.Card.Name == CardId.SovereignBlade.Name && play.PlayIndex == 0 ? Fire(state, owner) : One(state);
}

// ---- card events: exhausts, draws, discards, stars, energy, shuffles, created cards

/// <summary>FeelNoPainPower.AfterCardExhausted: block (unpowered) for each card its owner exhausts.</summary>
public sealed class FeelNoPainPower : ProcModel
{
    public override TabKind? Tab => TabKind.Block;
    public override IEnumerable<SimState> AfterCardEvent(SimState state, int owner, CardEvent cardEvent, int player) =>
        cardEvent == CardEvent.Exhausted && player == owner ? Block(state, owner, Amount(state, owner)) : One(state);
}

/// <summary>DarkEmbracePower.AfterCardExhausted: for each card its owner exhausts, draws its amount (CardPileCmd.Draw(Amount)).</summary>
public sealed class DarkEmbracePower : ProcModel
{
    public override TabKind? Tab => TabKind.Draw;
    public override IEnumerable<SimState> AfterCardEvent(SimState state, int owner, CardEvent cardEvent, int player) =>
        cardEvent == CardEvent.Exhausted && player == owner ? Cause(Fire(state, owner), CardEvent.Drawn, count: Amount(state, owner), owner) : One(state);
}

/// <summary>ChildOfTheStarsPower.AfterStarsSpent: its amount of block per star spent (one gain).</summary>
public sealed class ChildOfTheStarsPower : ProcModel
{
    public override TabKind? Tab => TabKind.Block;
    public override IEnumerable<SimState> AfterCardEvent(SimState state, int owner, CardEvent cardEvent, int player) =>
        cardEvent == CardEvent.StarsSpent && player == owner && state.CurrentPlay?.Card.Stars is > 0 and var stars
            ? Block(state, owner, Amount(state, owner) * stars) : One(state);
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
    /// <summary>A new copy's count (Panache: 0, just applied: the card that applied it doesn't count).</summary>
    public abstract int StartCount { get; }

    protected IReadOnlyList<CounterCopy> Copies(SimState state, int owner)
    {
        if (state.Player(owner) is { } player && player.Counters.Any(copy => copy.Power == ThisPower)) return player.Counters.Where(copy => copy.Power == ThisPower).ToList();
        return new[] { new CounterCopy(ThisPower, Data(state, owner), Amount(state, owner)) };
    }

    protected void SetCopies(SimState state, int owner, IReadOnlyList<CounterCopy> copies)
    {
        if (state.Player(owner) is { } player && player.Counters.Any(copy => copy.Power == ThisPower))
        {
            state.MutablePlayer(owner).Counters = player.Counters.Where(copy => copy.Power != ThisPower).Concat(copies).ToList();
            return;
        }
        Set(state, owner, copies[0].Amount, copies[0].Left);
    }

    /// <summary>
    /// A copy applied (an Orbit card played): its own count from StartCount. Its entry in Powers already holds every copy's amount
    /// added (ApplyPower); a first copy is that entry, a later one goes into Counters beside the ones before it.
    /// </summary>
    internal void NewCopy(SimState state, int target, int amount, bool had)
    {
        if (!had)
        {
            Set(state, target, Amount(state, target), StartCount);
            return;
        }
        if (state.Player(target) is not { } player) return;
        var before = player.Counters.Any(copy => copy.Power == ThisPower)
            ? player.Counters.Where(copy => copy.Power == ThisPower).ToList()
            : new List<CounterCopy> { new(ThisPower, Data(state, target), Amount(state, target) - amount) };
        state.MutablePlayer(target).Counters = player.Counters.Where(copy => copy.Power != ThisPower).Concat(before).Append(new CounterCopy(ThisPower, StartCount, amount)).ToList();
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
/// IterationPower.AfterCardDrawn: the first Status its owner draws this turn (the turn's CardDrawnEntry count, this one included:
/// SimPlayer.StatusDrawn) draws its amount more, in its place among the listeners (before relics and Void's own energy loss). No tab.
/// </summary>
public sealed class IterationPower : SimModel
{
    public override IEnumerable<SimState> AfterCardEvent(SimState state, int owner, CardEvent cardEvent, int player)
    {
        if (cardEvent != CardEvent.Drawn || player != owner || state.DrawnCard is not { Status: true } || state.Player(owner) is not { StatusDrawn: <= 1 }) return One(state);
        return SimEngine.ProcCardEvent(state, CardEvent.Drawn, count: SimEngine.PowerOn(state, owner, ThisPower), owner, indirect: true);
    }
}

/// <summary>PagestormPower.AfterCardDrawn: each Ethereal card its owner draws draws its amount more, in its place among the listeners. No tab.</summary>
public sealed class PagestormPower : SimModel
{
    public override IEnumerable<SimState> AfterCardEvent(SimState state, int owner, CardEvent cardEvent, int player) =>
        cardEvent == CardEvent.Drawn && player == owner && state.DrawnCard is { Ethereal: true }
            ? SimEngine.ProcCardEvent(state, CardEvent.Drawn, count: SimEngine.PowerOn(state, owner, ThisPower), owner, indirect: true)
            : One(state);
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

/// <summary>StratagemPower.AfterShuffle: a pick from the draw pile on each shuffle.</summary>
public sealed class StratagemPower : ProcModel
{
    public override IEnumerable<SimState> AfterCardEvent(SimState state, int owner, CardEvent cardEvent, int player) =>
        cardEvent == CardEvent.Shuffled && player == owner ? Fire(state, owner) : One(state);
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

/// <summary>TrashToTreasurePower.AfterCardGeneratedForCombat: its amount of random orbs for each status its owner creates.</summary>
public sealed class TrashToTreasurePower : ProcModel
{
    public override IEnumerable<SimState> AfterCardEvent(SimState state, int owner, CardEvent cardEvent, int player) =>
        cardEvent == CardEvent.GeneratedStatus && player == owner ? Channel(Fire(state, owner), owner, OrbKind.Random, Amount(state, owner)) : One(state);
}

// ---- powers landing

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

/// <summary>ReaperFormPower.AfterDamageGiven: its owner's powered hits that get HP through apply Doom.</summary>
public sealed class ReaperFormPower : ProcModel
{
    public override void AfterDamageGiven(SimState state, int owner, DamageEvent hit)
    {
        if (hit.Dealer == owner && hit.Props.IsPoweredAttack() && hit.Unblocked > 0 && hit.Target >= 0) state.LogFired(Name, amount: 0, owner);
    }
}

// ---- enemy powers acting on your plays

/// <summary>
/// WitheringPresencePower (on an enemy): each card its target plays counts its CardsLeft down (Data); at 0 a Wither (a Status) goes
/// into their hand (CardPileCmd.AddToCombatAndPreview: bad for you) and the count starts over at 6.
/// </summary>
public sealed class WitheringPresencePower : ProcModel
{
    public override bool Harmful => true;
    public override IEnumerable<SimState> AfterCardPlayed(SimState state, int owner, SimCardPlay play)
    {
        if (owner < 0 || play.Owner != Who.You) return One(state);
        var left = Data(state, owner);
        if (left <= 0) return One(state);
        left--;
        Set(state, owner, Amount(state, owner), left <= 0 ? 6 : left);
        return left <= 0 ? MakeInHand(Fire(state, owner), CardEvent.GeneratedStatus, count: 1, play.Owner) : One(state);
    }
}

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
        // Each Dazed goes into the attacker's draw pile at a random spot (CardPilePosition.Random), once the hit is over.
        for (var i = 0; i < amount; i++) state.PendingIntoDraw = state.PendingIntoDraw.Add((dealer, CardId.Dazed.Name));
    }
}

/// <summary>
/// NecroMasteryPower.AfterCurrentHpChanged: when its owner's Osty loses HP (a hit for them, Thorns on his attack, his death), every
/// hittable enemy takes that much times its amount (unblockable, unpowered).
/// </summary>
public sealed class NecroMasteryPower : ProcModel
{
    public override TabKind? Tab => TabKind.Damage;
    public override void AfterOstyHpLost(SimState state, int owner, int player, int lost)
    {
        if (player != owner || lost <= 0) return;
        SimEngine.ProcDamageAll(state, owner, Name, lost * Amount(state, owner), SimProps.Unblockable | Unpowered);
    }
}

/// <summary>
/// Teammates' Impaled on an enemy ("ImpaledPowerOther": Data holds each teammate's arrows in it, 4 bits per teammate index). Your
/// hits don't count it down (ImpaledPower.AfterDamageReceived: only its applier's hits), but the enemy's death lets every arrow go
/// (AfterDeath): back to each teammate, each through AfterArrowDislodged (their Twine, their Yah).
/// </summary>
public sealed class TeammatesImpaledPower : SimModel
{
    public override string Name => "ImpaledPowerOther";
    public override void AfterDeath(SimState state, int owner, int dead)
    {
        if (dead != owner) return;
        var powers = SimEngine.PowersOf(state, owner);
        var bits = powers.DataOf(ThisPower);
        SimEngine.SetPowers(state, owner, powers.Without(ThisPower));
        for (var teammateIndex = 0; teammateIndex < state.Teammates.Count && bits != 0; teammateIndex++, bits >>= 4)
        {
            var arrows = bits & 15;
            if (arrows == 0 || state.Teammates[teammateIndex].Hp <= 0) continue;
            state.MutableTeammate(teammateIndex).Arrows += arrows;
            SimEngine.Raise(state, CardEvent.ArrowDislodged, Who.Teammate(teammateIndex), arrows);
        }
    }
}
