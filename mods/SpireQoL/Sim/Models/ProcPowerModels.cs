namespace SpireQoL.Sim.Models;

// Your powers that act on your plays and card events (decompile v0.113.1). Amounts bound at BeforeCardPlayed act at
// AfterCardPlayed, as the game keeps them (amountsForPlayedCards), so a power the play applies doesn't fire on that play.

/// <summary>A power that binds its owner's play at BeforeCardPlayed (when <see cref="Binds"/>) and acts on it at AfterCardPlayed.</summary>
public abstract class BoundPower : ProcModel
{
    protected virtual bool Binds(SimCardPlay play) => true;
    protected virtual int BoundAmount(SimState s, int owner) => Amount(s, owner);
    protected abstract IEnumerable<SimState> Act(SimState s, int owner, SimCardPlay play, int amount);

    public override IEnumerable<SimState> BeforeCardPlayed(SimState s, int owner, SimCardPlay play)
    {
        if (Mine(play, owner) && Binds(play)) Bind(s, owner, BoundAmount(s, owner));
        return One(s);
    }

    public override IEnumerable<SimState> AfterCardPlayed(SimState s, int owner, SimCardPlay play) =>
        Mine(play, owner) && TakeBound(s, owner) is { } v ? Act(s, owner, play, v) : One(s);
}

/// <summary>AfterimagePower: block (unpowered) for each card its owner plays.</summary>
public sealed class AfterimagePower : BoundPower
{
    public override TabKind? Tab => TabKind.Block;
    protected override IEnumerable<SimState> Act(SimState s, int owner, SimCardPlay play, int amount) => amount > 0 ? Block(s, owner, amount) : One(s);
}

/// <summary>GravityPower: damage (unpowered) to every hittable enemy for each card its owner plays.</summary>
public sealed class GravityPower : BoundPower
{
    public override TabKind? Tab => TabKind.Damage;
    protected override IEnumerable<SimState> Act(SimState s, int owner, SimCardPlay play, int amount) => amount > 0 ? DamageAll(s, owner, amount) : One(s);
}

/// <summary>SerpentFormPower: damage (unpowered) to a random hittable enemy for each card its owner plays.</summary>
public sealed class SerpentFormPower : BoundPower
{
    public override TabKind? Tab => TabKind.Damage;
    protected override IEnumerable<SimState> Act(SimState s, int owner, SimCardPlay play, int amount) => amount > 0 ? DamageRandom(s, owner, amount) : One(s);
}

/// <summary>StormPower: each Power card its owner plays channels its amount of Lightning (bound at BeforeCardPlayed).</summary>
public sealed class StormPower : BoundPower
{
    protected override bool Binds(SimCardPlay play) => play.Card.Type == SimCardType.Power;
    protected override IEnumerable<SimState> Act(SimState s, int owner, SimCardPlay play, int amount) => Channel(Fire(s, owner), owner, OrbKind.Lightning, amount);
}

/// <summary>SubroutinePower: each Power card its owner plays gives its amount of energy (bound as the play begins).</summary>
public sealed class SubroutinePower : BoundPower
{
    public override TabKind? Tab => TabKind.Energy;
    protected override bool Binds(SimCardPlay play) => play.Card.Type == SimCardType.Power;
    protected override IEnumerable<SimState> Act(SimState s, int owner, SimCardPlay play, int amount) => Cause(Fire(s, owner), CardEvent.EnergyGained, amount, owner);
}

/// <summary>CalamityPower: each Attack its owner plays adds its amount of random Attacks to their hand (cards created).</summary>
public sealed class CalamityPower : BoundPower
{
    protected override bool Binds(SimCardPlay play) => play.Card.Type == SimCardType.Attack;
    protected override IEnumerable<SimState> Act(SimState s, int owner, SimCardPlay play, int amount) => Cause(Fire(s, owner), CardEvent.Generated, amount, owner);
}

/// <summary>MonologuePower: each card its owner plays gives Strength (its Strength value, bound at the play's start).</summary>
public sealed class MonologuePower : BoundPower
{
    protected override int BoundAmount(SimState s, int owner) => Var(s, owner, "Strength", 1);
    protected override IEnumerable<SimState> Act(SimState s, int owner, SimCardPlay play, int amount)
    {
        SimEngine.ApplyPower(s, owner, owner, "StrengthPower", amount, debuff: false);
        return Fire(s, owner);
    }
}

/// <summary>
/// JugglingPower: the third Attack its owner plays each turn (its own count, Data) is copied into their hand, its amount of times
/// (BeforeCardPlayed counts and binds the third; AfterCardPlayed adds the copies).
/// </summary>
public sealed class JugglingPower : ProcModel
{
    public override IEnumerable<SimState> BeforeCardPlayed(SimState s, int owner, SimCardPlay play)
    {
        if (!Mine(play, owner) || play.Card.Type != SimCardType.Attack) return One(s);
        var count = Data(s, owner) + 1;
        Set(s, owner, Amount(s, owner), count);
        if (count == 3) Bind(s, owner, Amount(s, owner));
        return One(s);
    }
    public override IEnumerable<SimState> AfterCardPlayed(SimState s, int owner, SimCardPlay play) =>
        Mine(play, owner) && TakeBound(s, owner) is { } n ? Cause(Fire(s, owner), CardEvent.Generated, n, owner) : One(s);
}

/// <summary>StranglePower (on an enemy, Data = its applier + 100): each card the applier plays deals its amount to it (unblockable, unpowered, no dealer).</summary>
public sealed class StranglePower : ProcModel
{
    public override TabKind? Tab => TabKind.Damage;
    public override IEnumerable<SimState> BeforeCardPlayed(SimState s, int owner, SimCardPlay play)
    {
        if (Data(s, owner) - 100 == play.Owner && Data(s, owner) != 0) Bind(s, owner, Amount(s, owner));
        return One(s);
    }
    public override IEnumerable<SimState> AfterCardPlayed(SimState s, int owner, SimCardPlay play) =>
        TakeBound(s, owner) is { } v ? SimEngine.ProcDamageOn(s, owner, Name, owner, v, SimProps.Unblockable | Unpowered, noDealer: true) : One(s);
}

/// <summary>OblivionPower (on an enemy, Data = its applier + 100): each card the applier plays applies Doom to it.</summary>
public sealed class OblivionPower : ProcModel
{
    public override IEnumerable<SimState> BeforeCardPlayed(SimState s, int owner, SimCardPlay play)
    {
        if (Data(s, owner) - 100 == play.Owner && Data(s, owner) != 0) Bind(s, owner, Amount(s, owner));
        return One(s);
    }
    public override IEnumerable<SimState> AfterCardPlayed(SimState s, int owner, SimCardPlay play) =>
        TakeBound(s, owner) is not null ? Fire(s, owner) : One(s);
}

/// <summary>RagePower.AfterCardPlayed: block (unpowered) for each Attack its owner plays.</summary>
public sealed class RagePower : ProcModel
{
    public override TabKind? Tab => TabKind.Block;
    public override IEnumerable<SimState> AfterCardPlayed(SimState s, int owner, SimCardPlay play) =>
        Mine(play, owner) && play.Card.Type == SimCardType.Attack ? Block(s, owner, Amount(s, owner)) : One(s);
}

/// <summary>SpiritOfAshPower.BeforeCardPlayed: block (unpowered) for each Ethereal card its owner plays.</summary>
public sealed class SpiritOfAshPower : ProcModel
{
    public override TabKind? Tab => TabKind.Block;
    public override IEnumerable<SimState> BeforeCardPlayed(SimState s, int owner, SimCardPlay play) =>
        Mine(play, owner) && play.Card.Ethereal ? Block(s, owner, Amount(s, owner)) : One(s);
}

/// <summary>DanseMacabrePower.BeforeCardPlayed: block (unpowered) for each card its owner plays costing at least its Energy value.</summary>
public sealed class DanseMacabrePower : ProcModel
{
    public override TabKind? Tab => TabKind.Block;
    public override IEnumerable<SimState> BeforeCardPlayed(SimState s, int owner, SimCardPlay play) =>
        Mine(play, owner) && play.Card.Energy >= Var(s, owner, "Energy", 2) ? Block(s, owner, Amount(s, owner)) : One(s);
}

/// <summary>TheSealedThronePower.BeforeCardPlayed: each card its owner plays gains them stars (Black Hole reacts).</summary>
public sealed class TheSealedThronePower : ProcModel
{
    public override IEnumerable<SimState> BeforeCardPlayed(SimState s, int owner, SimCardPlay play) =>
        Mine(play, owner) && Amount(s, owner) > 0 ? Cause(Fire(s, owner), CardEvent.StarsGained, 1, owner) : One(s);
}

/// <summary>
/// PanachePower.AfterCardPlayed: every card its owner plays counts its CardsLeft down (Data; 0 = just applied: the card that
/// applied it doesn't count); at 0, its amount to every hittable enemy (unpowered), and the count starts over at 5.
/// </summary>
public sealed class PanachePower : ProcModel
{
    public override TabKind? Tab => TabKind.Damage;
    public override IEnumerable<SimState> AfterCardPlayed(SimState s, int owner, SimCardPlay play)
    {
        if (!Mine(play, owner)) return One(s);
        var left = Data(s, owner);
        if (left <= 0) { Set(s, owner, Amount(s, owner), 5); return One(s); }
        left--;
        if (left > 0) { Set(s, owner, Amount(s, owner), left); return One(s); }
        Set(s, owner, Amount(s, owner), 5);
        return DamageAll(s, owner, Amount(s, owner));
    }
}

/// <summary>
/// BlackHolePower: its amount to every hittable enemy (unpowered) each time its owner gains stars (AfterStarsGained), and after
/// the last play of a card that spent stars (AfterCardPlayed, IsLastInSeries).
/// </summary>
public sealed class BlackHolePower : ProcModel
{
    public override TabKind? Tab => TabKind.Damage;
    public override IEnumerable<SimState> AfterCardEvent(SimState s, int owner, CardEvent ev, int player) =>
        ev == CardEvent.StarsGained && player == owner ? DamageAll(s, owner, Amount(s, owner)) : One(s);
    public override IEnumerable<SimState> AfterCardPlayed(SimState s, int owner, SimCardPlay play) =>
        Mine(play, owner) && play.Card.Stars > 0 && play.PlayIndex == play.PlayCount - 1 ? DamageAll(s, owner, Amount(s, owner)) : One(s);
}

/// <summary>DevourLifePower.AfterCardPlayed: each Soul its owner plays summons its amount (Osty).</summary>
public sealed class DevourLifePower : ProcModel
{
    public override TabKind? Tab => TabKind.Summon;
    public override IEnumerable<SimState> AfterCardPlayed(SimState s, int owner, SimCardPlay play) =>
        Mine(play, owner) && play.Card.Name == "Soul" ? Fire(s, owner, Amount(s, owner)) : One(s);
}

/// <summary>
/// SicEmPower (yours, on an enemy; InstancedPerApplier).AfterDamageGiven: each time its applier's Osty damages the enemy that has it,
/// its applier summons its amount (OstyCmd.Summon). Owner: the enemy. Taken as yours: a teammate's Sic 'Em on the same enemy isn't
/// told apart (the adapter reads the power by its type).
/// </summary>
public sealed class SicEmPower : ProcModel
{
    public override TabKind? Tab => TabKind.Summon;
    public override void AfterDamageGiven(SimState s, int owner, DamageEvent e)
    {
        if (e.Dealer == Who.YourOsty && e.Target == owner && e.Total > 0 && Amount(s, owner) > 0) s.LogFired(Name, Amount(s, owner), owner);
    }
}

/// <summary>HauntPower.AfterCardPlayed: each Soul its owner plays deals its amount to a random hittable enemy (unblockable, unpowered, no dealer).</summary>
public sealed class HauntPower : ProcModel
{
    public override TabKind? Tab => TabKind.Damage;
    public override IEnumerable<SimState> AfterCardPlayed(SimState s, int owner, SimCardPlay play) =>
        Mine(play, owner) && play.Card.Name == "Soul"
            ? SimEngine.ProcDamageRandom(s, owner, Name, Amount(s, owner), SimProps.Unblockable | Unpowered, noDealer: true)
            : One(s);
}

/// <summary>MasterPlannerPower.AfterCardPlayed: each Skill its owner plays gains Sly.</summary>
public sealed class MasterPlannerPower : ProcModel
{
    public override IEnumerable<SimState> AfterCardPlayed(SimState s, int owner, SimCardPlay play) =>
        Mine(play, owner) && play.Card.Type == SimCardType.Skill ? Fire(s, owner) : One(s);
}

/// <summary>SmoggyPower.AfterCardPlayed: each Skill its owner plays afflicts their other Skills with Smog (bad for you).</summary>
public sealed class SmoggyPower : ProcModel
{
    public override bool Harmful => true;
    public override IEnumerable<SimState> AfterCardPlayed(SimState s, int owner, SimCardPlay play) =>
        Mine(play, owner) && play.Card.Type == SimCardType.Skill ? Fire(s, owner) : One(s);
}

/// <summary>
/// FeralPower.ModifyCardPlayResultLocation: an Attack its owner plays for 0 energy (not a copy) goes back to their hand while it
/// has charges left this turn (Data: its DisplayAmount).
/// </summary>
public sealed class FeralPower : ProcModel
{
    public override IEnumerable<SimState> AfterCardPlayed(SimState s, int owner, SimCardPlay play)
    {
        if (!Mine(play, owner) || play.Card.Type != SimCardType.Attack || play.Card.Energy != 0 || play.Card.Dupe) return One(s);
        var left = Data(s, owner);
        if (left <= 0) return One(s);
        Set(s, owner, Amount(s, owner), left - 1);
        return Fire(s, owner);
    }
}

/// <summary>
/// NostalgiaPower.ModifyCardPlayResultLocation: its owner's first Amount attacks and skills each turn (plays started before this
/// one) go on top of their draw pile, unless they exhaust.
/// </summary>
public sealed class NostalgiaPower : ProcModel
{
    public override IEnumerable<SimState> AfterCardPlayed(SimState s, int owner, SimCardPlay play)
    {
        if (!Mine(play, owner) || play.Card.Type is not (SimCardType.Attack or SimCardType.Skill) || play.Card.ExhaustsSelf) return One(s);
        var turn = SimEngine.TurnOf(s, owner);
        var before = turn[SimKeys.Plays(PlayStage.Started, SimCardType.Attack)] + turn[SimKeys.Plays(PlayStage.Started, SimCardType.Skill)] - 1;
        return before < Amount(s, owner) ? Fire(s, owner) : One(s);
    }
}

/// <summary>
/// PaleBlueDotPower.AfterCardPlayed: once a turn (Data 1: already), when its owner has finished 5 card plays this turn, draws next
/// turn.
/// </summary>
public sealed class PaleBlueDotPower : ProcModel
{
    public override IEnumerable<SimState> AfterCardPlayed(SimState s, int owner, SimCardPlay play)
    {
        if (!Mine(play, owner) || Data(s, owner) != 0 || SimEngine.TurnOf(s, owner)[SimKeys.Plays(PlayStage.Finished)] < 5) return One(s);
        Set(s, owner, Amount(s, owner), 1);
        return Fire(s, owner);
    }
}

/// <summary>ImitationLearningPower (a teammate's, Data = the player it copies + 100): a Power card that player plays is copied and played for its owner.</summary>
public sealed class ImitationLearningPower : ProcModel
{
    public override IEnumerable<SimState> AfterCardPlayed(SimState s, int owner, SimCardPlay play) =>
        play.Owner != owner && Data(s, owner) != 0 && Data(s, owner) - 100 == play.Owner && play.Card.Type == SimCardType.Power ? Fire(s, owner) : One(s);
}

// ---- the Fool (v0.113.0): melodies (card tags Wah, Cho, Ki) and arrows

/// <summary>The melodies (Wah, Cho, Ki) and what a play does to them, from its owner's turn counters.</summary>
internal static class Melodies
{
    public static readonly string[] Tags = { "Wah", "Cho", "Ki" };

    /// <summary>The play is the first this turn with that tag (MelodyCmd.IsFirstPlayThisTurn: the only play started with it).</summary>
    public static bool First(SimState s, SimCardPlay play, string tag) => play.Card.Is(tag) && SimEngine.TurnOf(s, play.Owner)[SimKeys.Plays(PlayStage.Started, tag)] == 1;

    public static bool AnyFirst(SimState s, SimCardPlay play) => Tags.Any(t => First(s, play, t));

    /// <summary>Melodies finished this turn, with this play (AfterCardPlayed) and without it.</summary>
    public static (int Before, int After) Finished(SimState s, SimCardPlay play)
    {
        var turn = SimEngine.TurnOf(s, play.Owner);
        var after = Tags.Count(t => turn[SimKeys.Plays(PlayStage.Finished, t)] > 0);
        var before = Tags.Count(t => turn[SimKeys.Plays(PlayStage.Finished, t)] - (play.Card.Is(t) ? 1 : 0) > 0);
        return (before, after);
    }
}

/// <summary>ViolentChordPower.AfterCardPlayed: the first Wah each turn deals its amount to every hittable enemy (unpowered).</summary>
public sealed class ViolentChordPower : ProcModel
{
    public override TabKind? Tab => TabKind.Damage;
    public override IEnumerable<SimState> AfterCardPlayed(SimState s, int owner, SimCardPlay play) =>
        Mine(play, owner) && Melodies.First(s, play, "Wah") ? DamageAll(s, owner, Amount(s, owner)) : One(s);
}

/// <summary>SongPathPower.AfterCardPlayed: the first Cho each turn gains its amount of block (unpowered).</summary>
public sealed class SongPathPower : ProcModel
{
    public override TabKind? Tab => TabKind.Block;
    public override IEnumerable<SimState> AfterCardPlayed(SimState s, int owner, SimCardPlay play) =>
        Mine(play, owner) && Melodies.First(s, play, "Cho") ? Block(s, owner, Amount(s, owner)) : One(s);
}

/// <summary>LullabyPower.AfterCardPlayed: once a turn (Data 1: already), the play that completes Wah, Cho and Ki gains its amount of block.</summary>
public sealed class LullabyPower : ProcModel
{
    public override TabKind? Tab => TabKind.Block;
    public override IEnumerable<SimState> AfterCardPlayed(SimState s, int owner, SimCardPlay play)
    {
        if (!Mine(play, owner) || Data(s, owner) != 0) return One(s);
        var (before, after) = Melodies.Finished(s, play);
        if (before >= 3 || after < 3) return One(s);
        Set(s, owner, Amount(s, owner), 1);
        return Block(s, owner, Amount(s, owner));
    }
}

/// <summary>
/// MotifPower.AfterCardPlayed: its amount of draws for each melody (Wah, Cho, Ki) the card is the first this turn to play
/// (CardPileCmd.Draw(num * Amount)).
/// </summary>
public sealed class MotifPower : ProcModel
{
    public override TabKind? Tab => TabKind.Draw;
    public override IEnumerable<SimState> AfterCardPlayed(SimState s, int owner, SimCardPlay play) =>
        Mine(play, owner) && Melodies.Tags.Count(t => Melodies.First(s, play, t)) is var n and > 0
            ? Cause(Fire(s, owner), CardEvent.Drawn, n * Amount(s, owner), owner)
            : One(s);
}

/// <summary>LostLutePower.AfterCardPlayed: the first Wah, Cho and Ki each turn play the top card of your draw pile.</summary>
public sealed class LostLutePower : ProcModel
{
    public override IEnumerable<SimState> AfterCardPlayed(SimState s, int owner, SimCardPlay play) =>
        Mine(play, owner) && Melodies.AnyFirst(s, play) ? Fire(s, owner) : One(s);
}

/// <summary>ExplosiveArrowsPower.AfterArrowImpaled: its owner's arrow impales an enemy: its amount to every hittable enemy (unpowered).</summary>
public sealed class ExplosiveArrowsPower : ProcModel
{
    public override TabKind? Tab => TabKind.Damage;
    public override IEnumerable<SimState> AfterArrowImpaled(SimState s, int owner, int target, int shooter) =>
        shooter == owner ? DamageAll(s, owner, Amount(s, owner)) : One(s);
}

/// <summary>CoveringFirePower.AfterArrowShot: each shot its owner tries from a card (arrow or not) gives every other living player its amount of block (unpowered). No tab.</summary>
public sealed class CoveringFirePower : ProcModel
{
    public override TabKind? Tab => null; // no tab: the block is your teammates' (decision #25)
    public override IEnumerable<SimState> AfterArrowShot(SimState s, int owner, int shooter)
    {
        if (shooter != owner || s.CurrentPlay == null) return One(s);
        IEnumerable<SimState> r = One(s);
        var amount = Amount(s, owner);
        for (var k = 0; k < s.Teammates.Count; k++)
        {
            var mate = Who.Teammate(k);
            if (mate == owner) continue;
            r = r.SelectMany(x => SimEngine.ProcBlockFor(x, owner, Name, mate, amount, Unpowered)).ToList();
        }
        if (owner != Who.You) r = r.SelectMany(x => SimEngine.ProcBlockFor(x, owner, Name, Who.You, amount, Unpowered)).ToList();
        return r;
    }
}

/// <summary>
/// ImpaledPower (yours, on an enemy: Amount = your arrows in it, Data = its HitCounter): every third powered hit of yours knocks an
/// arrow out (AfterDamageReceived: the counter starts over at 3), and its death lets all of them go (AfterDeath). Each comes back to
/// you and sets off AfterArrowDislodged, once the hit is over.
/// </summary>
public sealed class ImpaledPower : SimModel
{
    public override void AfterDamageReceived(SimState s, int owner, DamageEvent e)
    {
        if (e.Target != owner || e.Dealer != Who.You || !e.Props.IsPoweredAttack()) return;
        var p = SimEngine.PowersOf(s, owner);
        if (p[Name] <= 0) return;
        var counter = p.DataOf(Name) - 1;
        if (counter > 0) { SimEngine.SetPowers(s, owner, p.With(Name, p[Name], counter)); return; }
        SimEngine.SetPowers(s, owner, p.With(Name, p[Name], 3));
        SimEngine.TakeArrows(s, owner, 1);
        SimEngine.Raise(s, CardEvent.ArrowDislodged, Who.You);
    }
    public override void AfterDeath(SimState s, int owner, int dead)
    {
        var arrows = SimEngine.PowerOn(s, owner, Name);
        if (dead != owner || arrows <= 0) return;
        SimEngine.TakeArrows(s, owner, arrows);
        SimEngine.Raise(s, CardEvent.ArrowDislodged, Who.You, arrows);
    }
}

/// <summary>YahPower.AfterArrowDislodged: energy the first time each turn (Data 1: already) one of your arrows comes loose.</summary>
public sealed class YahPower : ProcModel
{
    public override TabKind? Tab => TabKind.Energy;
    public override IEnumerable<SimState> AfterCardEvent(SimState s, int owner, CardEvent ev, int player)
    {
        if (ev != CardEvent.ArrowDislodged || player != owner || Data(s, owner) != 0) return One(s);
        Set(s, owner, Amount(s, owner), 1);
        return Cause(Fire(s, owner), CardEvent.EnergyGained, Amount(s, owner), owner);
    }
}

// ---- Sovereign Blade's buffs (the card text lists them, but it's easy to miss)

/// <summary>A buff that changes what Sovereign Blade does (Seeking Edge: every enemy; Parry: block; Sword Sage: replays).</summary>
public abstract class SovereignBladeBuff : ProcModel
{
    public override IEnumerable<SimState> AfterCardPlayed(SimState s, int owner, SimCardPlay play) =>
        Mine(play, owner) && play.Card.Name == "SovereignBlade" && play.PlayIndex == 0 ? Fire(s, owner) : One(s);
}

public sealed class SeekingEdgePower : SovereignBladeBuff { }
public sealed class ParryPower : SovereignBladeBuff { }
public sealed class SwordSagePower : SovereignBladeBuff { }

/// <summary>ConquerorPower (on an enemy): Sovereign Blade deals it more damage.</summary>
public sealed class ConquerorPower : ProcModel
{
    public override IEnumerable<SimState> AfterCardPlayed(SimState s, int owner, SimCardPlay play) =>
        owner >= 0 && play.Card.Name == "SovereignBlade" && play.PlayIndex == 0 ? Fire(s, owner) : One(s);
}

// ---- card events: exhausts, draws, discards, stars, energy, shuffles, created cards

/// <summary>FeelNoPainPower.AfterCardExhausted: block (unpowered) for each card its owner exhausts.</summary>
public sealed class FeelNoPainPower : ProcModel
{
    public override TabKind? Tab => TabKind.Block;
    public override IEnumerable<SimState> AfterCardEvent(SimState s, int owner, CardEvent ev, int player) =>
        ev == CardEvent.Exhausted && player == owner ? Block(s, owner, Amount(s, owner)) : One(s);
}

/// <summary>DarkEmbracePower.AfterCardExhausted: a draw for each card its owner exhausts.</summary>
public sealed class DarkEmbracePower : ProcModel
{
    public override TabKind? Tab => TabKind.Draw;
    public override IEnumerable<SimState> AfterCardEvent(SimState s, int owner, CardEvent ev, int player) =>
        ev == CardEvent.Exhausted && player == owner ? Cause(Fire(s, owner), CardEvent.Drawn, 1, owner) : One(s);
}

/// <summary>ChildOfTheStarsPower.AfterStarsSpent: its amount of block per star spent (one gain).</summary>
public sealed class ChildOfTheStarsPower : ProcModel
{
    public override TabKind? Tab => TabKind.Block;
    public override IEnumerable<SimState> AfterCardEvent(SimState s, int owner, CardEvent ev, int player) =>
        ev == CardEvent.StarsSpent && player == owner && s.CurrentPlay?.Card.Stars is > 0 and var stars
            ? Block(s, owner, Amount(s, owner) * stars) : One(s);
}

/// <summary>A power counting card events down (its DisplayAmount, Data) that fires when the count runs out.</summary>
public abstract class EventCountdownPower : ProcModel
{
    protected abstract CardEvent Event { get; }
    protected abstract IEnumerable<SimState> OnZero(SimState s, int owner);
    public override IEnumerable<SimState> AfterCardEvent(SimState s, int owner, CardEvent ev, int player)
    {
        if (ev != Event || player != owner) return One(s);
        var left = Data(s, owner);
        if (left <= 0) return One(s); // the count isn't known (or it's spent): nothing more this play
        Set(s, owner, Amount(s, owner), left - 1);
        return left == 1 ? OnZero(s, owner) : One(s);
    }
}

/// <summary>OrbitPower.AfterEnergySpent: its amount of energy once enough energy is spent (its count, Data).</summary>
public sealed class OrbitPower : EventCountdownPower
{
    public override TabKind? Tab => TabKind.Energy;
    protected override CardEvent Event => CardEvent.EnergySpent;
    protected override IEnumerable<SimState> OnZero(SimState s, int owner) => Cause(Fire(s, owner), CardEvent.EnergyGained, Amount(s, owner), owner);
}

/// <summary>AutomationPower.AfterCardDrawn: energy once enough cards are drawn (its count, Data).</summary>
public sealed class AutomationPower : EventCountdownPower
{
    public override TabKind? Tab => TabKind.Energy;
    protected override CardEvent Event => CardEvent.Drawn;
    protected override IEnumerable<SimState> OnZero(SimState s, int owner) => Cause(Fire(s, owner), CardEvent.EnergyGained, Amount(s, owner), owner);
}

/// <summary>CacophonyPower.AfterCardDrawn: its amount to a random hittable enemy once enough cards are drawn (its count, Data).</summary>
public sealed class CacophonyPower : EventCountdownPower
{
    public override TabKind? Tab => TabKind.Damage;
    protected override CardEvent Event => CardEvent.Drawn;
    protected override IEnumerable<SimState> OnZero(SimState s, int owner) => DamageRandom(s, owner, Amount(s, owner));
}

/// <summary>SpeedsterPower.AfterCardDrawn: its amount to every hittable enemy for each card its owner draws.</summary>
public sealed class SpeedsterPower : ProcModel
{
    public override TabKind? Tab => TabKind.Damage;
    public override IEnumerable<SimState> AfterCardEvent(SimState s, int owner, CardEvent ev, int player) =>
        ev == CardEvent.Drawn && player == owner ? DamageAll(s, owner, Amount(s, owner)) : One(s);
}

/// <summary>CorrosiveWavePower.AfterCardDrawn: poison on every enemy for each card its owner draws.</summary>
public sealed class CorrosiveWavePower : ProcModel
{
    public override IEnumerable<SimState> AfterCardEvent(SimState s, int owner, CardEvent ev, int player) =>
        ev == CardEvent.Drawn && player == owner ? Fire(s, owner) : One(s);
}

/// <summary>StratagemPower.AfterShuffle: a pick from the draw pile on each shuffle.</summary>
public sealed class StratagemPower : ProcModel
{
    public override IEnumerable<SimState> AfterCardEvent(SimState s, int owner, CardEvent ev, int player) =>
        ev == CardEvent.Shuffled && player == owner ? Fire(s, owner) : One(s);
}

/// <summary>ArsenalPower.AfterCardGeneratedForCombat: Strength for each card its owner creates.</summary>
public sealed class ArsenalPower : ProcModel
{
    public override IEnumerable<SimState> AfterCardEvent(SimState s, int owner, CardEvent ev, int player) =>
        Generated(ev) && player == owner ? Fire(s, owner) : One(s);
}

/// <summary>PillarOfCreationPower.AfterCardGeneratedForCombat: block (unpowered) for each card its owner creates.</summary>
public sealed class PillarOfCreationPower : ProcModel
{
    public override TabKind? Tab => TabKind.Block;
    public override IEnumerable<SimState> AfterCardEvent(SimState s, int owner, CardEvent ev, int player) =>
        Generated(ev) && player == owner ? Block(s, owner, Amount(s, owner)) : One(s);
}

/// <summary>AllIsWellPower.AfterCardGeneratedForCombat: block (unpowered) for each Sadness its owner creates.</summary>
public sealed class AllIsWellPower : ProcModel
{
    public override TabKind? Tab => TabKind.Block;
    public override IEnumerable<SimState> AfterCardEvent(SimState s, int owner, CardEvent ev, int player) =>
        ev == CardEvent.GeneratedSadness && player == owner ? Block(s, owner, Amount(s, owner)) : One(s);
}

/// <summary>SmokestackPower.AfterCardGeneratedForCombat: its amount to every hittable enemy for each status its owner creates.</summary>
public sealed class SmokestackPower : ProcModel
{
    public override TabKind? Tab => TabKind.Damage;
    public override IEnumerable<SimState> AfterCardEvent(SimState s, int owner, CardEvent ev, int player) =>
        ev == CardEvent.GeneratedStatus && player == owner ? DamageAll(s, owner, Amount(s, owner)) : One(s);
}

/// <summary>TrashToTreasurePower.AfterCardGeneratedForCombat: its amount of random orbs for each status its owner creates.</summary>
public sealed class TrashToTreasurePower : ProcModel
{
    public override IEnumerable<SimState> AfterCardEvent(SimState s, int owner, CardEvent ev, int player) =>
        ev == CardEvent.GeneratedStatus && player == owner ? Channel(Fire(s, owner), owner, OrbKind.Unknown, Amount(s, owner)) : One(s);
}

// ---- powers landing

/// <summary>SleightOfFleshPower.AfterPowerAmountChanged: each debuff its owner lands on an enemy deals that enemy its amount (unpowered).</summary>
public sealed class SleightOfFleshPower : ProcModel
{
    public override TabKind? Tab => TabKind.Damage;
    public override IEnumerable<SimState> AfterPowerApplied(SimState s, int owner, string power, int amount, int applier, int target, bool debuff) =>
        debuff && applier == owner && target >= 0 ? SimEngine.ProcDamageOn(s, owner, Name, target, Amount(s, owner), Unpowered) : One(s);
}

/// <summary>ViciousPower.AfterPowerAmountChanged: a draw for each Vulnerable its owner lands on an enemy.</summary>
public sealed class ViciousPower : ProcModel
{
    public override TabKind? Tab => TabKind.Draw;
    public override IEnumerable<SimState> AfterPowerApplied(SimState s, int owner, string power, int amount, int applier, int target, bool debuff) =>
        power == "VulnerablePower" && applier == owner && target >= 0 ? Cause(Fire(s, owner), CardEvent.Drawn, 1, owner) : One(s);
}

/// <summary>ShroudPower.AfterPowerAmountChanged: block (unpowered) for each Doom its owner lands on an enemy.</summary>
public sealed class ShroudPower : ProcModel
{
    public override TabKind? Tab => TabKind.Block;
    public override IEnumerable<SimState> AfterPowerApplied(SimState s, int owner, string power, int amount, int applier, int target, bool debuff) =>
        power == "DoomPower" && applier == owner && target >= 0 ? Block(s, owner, Amount(s, owner)) : One(s);
}

/// <summary>ReaperFormPower.AfterDamageGiven: its owner's powered hits that get HP through apply Doom.</summary>
public sealed class ReaperFormPower : ProcModel
{
    public override void AfterDamageGiven(SimState s, int owner, DamageEvent e)
    {
        if (e.Dealer == owner && e.Props.IsPoweredAttack() && e.Unblocked > 0 && e.Target >= 0) s.LogFired(Name, 0, owner);
    }
}

// ---- enemy powers acting on your plays

/// <summary>
/// WitheringPresencePower (on an enemy): each card its target plays counts its CardsLeft down (Data); at 0 a Wither goes into
/// their hand (bad for you) and the count starts over at 6.
/// </summary>
public sealed class WitheringPresencePower : ProcModel
{
    public override bool Harmful => true;
    public override IEnumerable<SimState> AfterCardPlayed(SimState s, int owner, SimCardPlay play)
    {
        if (owner < 0 || play.Owner != Who.You) return One(s);
        var left = Data(s, owner);
        if (left <= 0) return One(s);
        left--;
        Set(s, owner, Amount(s, owner), left <= 0 ? 6 : left);
        return left <= 0 ? Fire(s, owner) : One(s);
    }
}

/// <summary>GalvanicPower.AfterCardPlayed: a Galvanized card deals its amount to the player who played it (unpowered Move, no dealer).</summary>
public sealed class GalvanicPower : ProcModel
{
    public override TabKind? Tab => TabKind.Damage;
    public override bool Harmful => true;
    public override IEnumerable<SimState> AfterCardPlayed(SimState s, int owner, SimCardPlay play) =>
        play.Card.Is("Galvanized") && Who.IsPlayer(play.Owner)
            ? SimEngine.ProcDamagePlayer(s, owner, Name, play.Owner, Amount(s, owner), Unpowered | SimProps.Move, noDealer: true)
            : One(s);
}

/// <summary>PersonalHivePower.AfterDamageReceived: a player's attack on it puts a Dazed into their draw pile (bad for you).</summary>
public sealed class PersonalHivePower : ProcModel
{
    public override bool Harmful => true;
    public override void AfterDamageReceived(SimState s, int owner, DamageEvent e)
    {
        if (e.Target == owner && e.Dealer is { } d && Who.IsPlayer(d) && e.Props.IsPoweredAttack()) s.LogFired(Name, 0, owner);
    }
}

/// <summary>
/// NecroMasteryPower.AfterCurrentHpChanged: when its owner's Osty loses HP (a hit for them, Thorns on his attack, his death), every
/// hittable enemy takes that much times its amount (unblockable, unpowered).
/// </summary>
public sealed class NecroMasteryPower : ProcModel
{
    public override TabKind? Tab => TabKind.Damage;
    public override void AfterOstyHpLost(SimState s, int owner, int player, int lost)
    {
        if (player != owner || lost <= 0) return;
        SimEngine.ProcDamageAll(s, owner, Name, lost * Amount(s, owner), SimProps.Unblockable | Unpowered);
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
    public override void AfterDeath(SimState s, int owner, int dead)
    {
        if (dead != owner) return;
        var p = SimEngine.PowersOf(s, owner);
        var bits = p.DataOf(Name);
        SimEngine.SetPowers(s, owner, p.Without(Name));
        for (var k = 0; k < s.Teammates.Count && bits != 0; k++, bits >>= 4)
        {
            var arrows = bits & 15;
            if (arrows == 0 || s.Teammates[k].Hp <= 0) continue;
            s.MutableTeammate(k).Arrows += arrows;
            SimEngine.Raise(s, CardEvent.ArrowDislodged, Who.Teammate(k), arrows);
        }
    }
}
