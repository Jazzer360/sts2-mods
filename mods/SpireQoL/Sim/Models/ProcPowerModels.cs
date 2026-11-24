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
        if (state.MainRules.HasFlag(MainRule.JugglingCopiesBeforeThePlay)) return MakeInHand(Fire(state, owner), play.Card, Amount(state, owner), owner);
        Bind(state, owner, Amount(state, owner));
        return One(state);
    }
    public override IEnumerable<SimState> AfterCardPlayed(SimState state, int owner, SimCardPlay play) =>
        Mine(play, owner) && TakeBound(state, owner) is { } bound ? MakeInHand(Fire(state, owner), play.Card, bound, owner) : One(state);
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

/// <summary>SmoggyPower.AfterCardPlayed: each Skill its owner plays afflicts their other Skills with Smog (bad for you).</summary>
public sealed class SmoggyPower : ProcModel
{
    public override bool Harmful => true;
    public override IEnumerable<SimState> AfterCardPlayed(SimState state, int owner, SimCardPlay play) =>
        Mine(play, owner) && play.Card.Type == SimCardType.Skill ? Fire(state, owner) : One(state);
}

/// <summary>
/// FeralPower (Data: its zeroCostAttacksPlayed). ModifyCardPlayResultLocation, decided once before a card play's plays: an Attack its
/// owner plays for 0 energy (not a copy) goes back to the top of their hand while fewer than its amount came back this turn;
/// AfterModifyingCardPlayResultLocation counts it and flashes. A new Feral starts its count at the 0-cost Attacks its owner started this
/// turn (AfterApplied: the fight's log, decision #191); more Feral on it only raises its amount (PowerCmd.Apply).
/// </summary>
public sealed class FeralPower : ProcModel
{
    /// <summary>FeralPower.AfterApplied's question: CardPlayStartedEntry, this turn, its owner's, an Attack, Resources.EnergyValue == 0.</summary>
    public static readonly HistoryQuery FreeAttacksStarted =
        new(HistoryKind.CardPlayStarted, HistoryScope.ThisTurn, HistoryWhose.Owner) { Details = HistoryDetail.ZeroEnergy, CardType = SimCardType.Attack };

    public override IReadOnlyList<HistoryQuery> History => new[] { FreeAttacksStarted };

    public override void AfterApplied(SimState state, int owner, int applier, int amount)
    {
        if (SimEngine.HistoryCount(state, owner, FreeAttacksStarted) is { } started) Set(state, owner, Amount(state, owner), started);
        else state.Unmodelled ??= SimEngine.HistoryUnknown;
    }

    public override SimResultLocation ModifyCardPlayResultLocation(SimState state, int owner, SimCardPlay play, SimResultLocation location)
    {
        if (!Mine(play, owner) || play.Card.Type != SimCardType.Attack || play.Card.Energy > 0 || play.Card.Dupe) return location;
        return Data(state, owner) >= Amount(state, owner) ? location : SimResultLocation.HandTop;
    }

    public override void AfterModifyingCardPlayResultLocation(SimState state, int owner, SimCardPlay play, SimResultLocation location)
    {
        Set(state, owner, Amount(state, owner), Data(state, owner) + 1);
        Fire(state, owner);
    }
}

/// <summary>
/// TagTeamPower on an enemy, one copy per applier as the game's (PowerInstanceType.Instanced): Data holds each applier's amount, 4 bits each
/// (you, each teammate, no one: DataFor), the power's amount their total. ModifyCardPlayCount: an Attack aimed at this enemy or at every
/// enemy (TargetType AnyEnemy at it, AllEnemies) plays each other applier's amount more times (card.Owner.Creature == base.Applier: not its
/// own); AfterModifyingCardPlayCount removes each copy that did (PowerCmd.Remove(this)). Its tag is the double-play tag (CardPlays).
/// </summary>
public sealed class TagTeamPower : SimModel
{
    public override bool Instanced => true;

    private const int Bits = 4;
    private const int Most = (1 << Bits) - 1;
    private const int Slots = 8;

    /// <summary>The applier's slot in Data: you 0, each teammate 1 to 6, anyone else (no one) 7.</summary>
    private static int SlotOf(int applier) => applier == Who.You ? 0 : Who.TeammateIndex(applier) is var teammateIndex and >= 0 and < Slots - 2 ? teammateIndex + 1 : Slots - 1;

    private static int AmountIn(int data, int slot) => (data >> (Bits * slot)) & Most;

    /// <summary>The Data of a copy applied by <paramref name="applier"/> with this amount (several copies' Data or together).</summary>
    public static int DataFor(int applier, int amount) => Math.Min(Most, Math.Max(0, amount)) << (Bits * SlotOf(applier));

    /// <summary>A new copy is its applier's (the game's PowerCmd.Apply of an instanced power sets its Applier).</summary>
    public override void AfterApplied(SimState state, int owner, int applier, int amount)
    {
        var powers = SimEngine.PowersOf(state, owner);
        var data = powers.DataOf(ThisPower);
        var slot = SlotOf(applier);
        var now = Math.Min(Most, AmountIn(data, slot) + amount);
        SimEngine.SetPowers(state, owner, powers.With(ThisPower, powers[ThisPower], data & ~(Most << (Bits * slot)) | now << (Bits * slot)));
    }

    /// <summary>The amounts of the copies another player than <paramref name="player"/> applied.</summary>
    private static int OthersAmount(int data, int player)
    {
        var own = SlotOf(player);
        var total = 0;
        for (var slot = 0; slot < Slots; slot++)
            if (slot != own) total += AmountIn(data, slot);
        return total;
    }

    public override int ModifyCardPlayCount(SimState state, int owner, SimCardPlay play, int count)
    {
        var powers = SimEngine.PowersOf(state, owner);
        if (play.Card.Type != SimCardType.Attack) return count;
        if (play.Card.Targets == SimPlay.Target.Single ? play.Target != owner : play.Card.Targets != SimPlay.Target.All) return count;
        return count + OthersAmount(powers.DataOf(ThisPower), play.Owner);
    }

    public override void AfterModifyingCardPlayCount(SimState state, int owner, SimCardPlay play)
    {
        var powers = SimEngine.PowersOf(state, owner);
        var slot = SlotOf(play.Owner);
        var kept = AmountIn(powers.DataOf(ThisPower), slot);
        SimEngine.SetPowers(state, owner, kept > 0 ? powers.With(ThisPower, kept, kept << (Bits * slot)) : powers.Without(ThisPower));
    }
}

/// <summary>
/// CorruptionPower.ModifyCardPlayResultLocation, decided once before a card play's plays: its owner's Skill goes to the exhaust pile
/// whatever its own pile was (its own exhaust then sets off what reacts to exhausts: SimEngine.RunCardPlay).
/// </summary>
public sealed class CorruptionPower : SimModel
{
    public override SimResultLocation ModifyCardPlayResultLocation(SimState state, int owner, SimCardPlay play, SimResultLocation location) =>
        play.Owner == owner && play.Card.Type == SimCardType.Skill ? SimResultLocation.Exhaust : location;
}

/// <summary>
/// ReboundPower.ModifyCardPlayResultLocation, decided once before a card play's plays: its owner's card bound for their discard pile goes on
/// top of their draw pile; AfterModifyingCardPlayResultLocation uses a stack (PowerCmd.Decrement). Where the card lands isn't followed
/// further, as Nostalgia's.
/// </summary>
public sealed class ReboundPower : SimModel
{
    public override SimResultLocation ModifyCardPlayResultLocation(SimState state, int owner, SimCardPlay play, SimResultLocation location) =>
        play.Owner == owner && location == SimResultLocation.Default ? SimResultLocation.DrawTop : location;

    public override void AfterModifyingCardPlayResultLocation(SimState state, int owner, SimCardPlay play, SimResultLocation location)
    {
        if (play.Owner == owner) SimEngine.DecrementPower(state, owner, ThisPower);
    }
}

/// <summary>
/// NostalgiaPower.ModifyCardPlayResultLocation, decided once before a card play's plays: its owner's Attack or Skill bound for their
/// discard pile (not exhausting, not sent elsewhere) goes on top of their draw pile while fewer than its amount of their Attack and Skill
/// plays started this turn; AfterModifyingCardPlayResultLocation flashes. Where the card lands isn't followed further (its tag).
/// </summary>
public sealed class NostalgiaPower : ProcModel
{
    public override SimResultLocation ModifyCardPlayResultLocation(SimState state, int owner, SimCardPlay play, SimResultLocation location)
    {
        if (!Mine(play, owner) || play.Card.Type is not (SimCardType.Attack or SimCardType.Skill) || play.Card.ExhaustsSelf || location != SimResultLocation.Default) return location;
        var turn = SimEngine.TurnOf(state, owner);
        var started = turn[SimKeys.Plays(PlayStage.Started, SimCardType.Attack)] + turn[SimKeys.Plays(PlayStage.Started, SimCardType.Skill)];
        return started < Amount(state, owner) ? SimResultLocation.DrawTop : location;
    }

    public override void AfterModifyingCardPlayResultLocation(SimState state, int owner, SimCardPlay play, SimResultLocation location) => Fire(state, owner);
}

/// <summary>ImitationLearningPower (a teammate's, Data = the player it copies + 100): a Power card that player plays is copied and played for its owner.</summary>
public sealed class ImitationLearningPower : ProcModel
{
    /// <summary>One copy per player it copies; the adapter reads you into Data when any copy copies you (SimReaders.Read).</summary>
    public override bool Instanced => true;
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

// ---- card events: exhausts, draws, discards, stars, energy, shuffles, created cards

/// <summary>StratagemPower.AfterShuffle: a pick from the draw pile on each shuffle.</summary>
public sealed class StratagemPower : ProcModel
{
    public override IEnumerable<SimState> AfterCardEvent(SimState state, int owner, CardEvent cardEvent, int player) =>
        cardEvent == CardEvent.Shuffled && player == owner ? Fire(state, owner) : One(state);
}

/// <summary>TrashToTreasurePower.AfterCardGeneratedForCombat: its amount of random orbs for each status its owner creates.</summary>
public sealed class TrashToTreasurePower : ProcModel
{
    public override IEnumerable<SimState> AfterCardEvent(SimState state, int owner, CardEvent cardEvent, int player) =>
        cardEvent == CardEvent.Generated && state.EventCard?.Type == SimCardType.Status && player == owner ? Channel(Fire(state, owner), owner, OrbKind.Random, Amount(state, owner)) : One(state);
}

// ---- enemy powers acting on your plays

/// <summary>
/// WitheringPresencePower (on an enemy): each card its target plays counts its CardsLeft down (Data); at 0 a Wither (a Status) goes
/// into their hand (CardPileCmd.AddToCombatAndPreview: bad for you) and the count starts over at 6.
/// </summary>
public sealed class WitheringPresencePower : ProcModel
{
    /// <summary>One copy per player (Aeonglass.AfterAddedToRoom), each counting its Target's cards: only yours is read (SimReaders.Powers).</summary>
    public override bool Instanced => true;
    public override bool Harmful => true;
    private static readonly SimCard Wither = new(CardId.Wither.Name, SimCardType.Status);
    public override IEnumerable<SimState> AfterCardPlayed(SimState state, int owner, SimCardPlay play)
    {
        if (owner < 0 || play.Owner != Who.You) return One(state);
        var left = Data(state, owner);
        if (left <= 0) return One(state);
        left--;
        Set(state, owner, Amount(state, owner), left <= 0 ? 6 : left);
        return left <= 0 ? MakeInHand(Fire(state, owner), Wither, count: 1, play.Owner) : One(state);
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
