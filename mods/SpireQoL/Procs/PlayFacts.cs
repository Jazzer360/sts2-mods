using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Orbs;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.Models.Relics;
using SpireQoL.Sim;
using Sts2Mods.Core.Game;

namespace SpireQoL.Procs;

/// <summary>
/// What playing one card in hand right now would do, as far as procs care: how many times it
/// plays, what it spends, and what it exhausts, discards, draws, debuffs and creates. Built once per
/// card per frame and read by every proc rule (Combat/CardPredictions) and the Shuffle Warning.
///
/// Counts come from the game's own numbers where it has them (play count hook, energy/star cost
/// with modifiers, DynamicVars) and from per-card tables (decompile v0.111.0) where the effect is
/// coded in the card's OnPlay. Choices the player makes mid-play (Purity, Hidden Daggers) count as
/// the most the card can do.
/// </summary>
public sealed class PlayFacts
{
    public required CardModel Card { get; init; }
    public required Player Me { get; init; }
    public required ICombatState Combat { get; init; }

    /// <summary>Times the card plays (Echo Form, Burst, Throwing Axe...). Per-play procs fire this many times.</summary>
    public int Plays { get; init; } = 1;

    /// <summary>Energy spent to play it (X-cost: all your energy). The game's CardEnergyCost.GetAmountToSpend.</summary>
    public int Energy { get; init; }

    /// <summary>
    /// Auto-played by an open hand choice (Decisions, Decisions): no energy is spent (CardCmd.AutoPlay:
    /// EnergySpent 0; the card's cost still counts as its value), and it isn't a card you played.
    /// </summary>
    public bool AutoPlayed { get; init; }

    /// <summary>Energy the play spends (0 when auto-played).</summary>
    public int EnergySpent => AutoPlayed ? 0 : Energy;

    /// <summary>Stars spent to play it (0 if it has no star cost).</summary>
    public int Stars { get; init; }

    /// <summary>The card itself exhausts after it's played (Exhaust keyword, including added Exhaust).</summary>
    public bool ExhaustsSelf { get; init; }

    /// <summary>Other cards it exhausts (True Grit, Fiend Fire, Second Wind...).</summary>
    public int ExhaustsOthers { get; init; }

    /// <summary>Cards it discards (Prepared, Survivor, Storm of Steel...).</summary>
    public int Discards { get; init; }

    /// <summary>Cards of yours, other than this one, its play takes out of your hand: what it discards and exhausts from there (LeavingHand).</summary>
    public int LeavesHand { get; init; }

    /// <summary>It takes your whole hand (Fiend Fire, Storm of Steel, Calculated Gamble...): everything in it goes, whatever it was.</summary>
    public bool TakesWholeHand { get; init; }

    /// <summary>
    /// It discards from your hand without a choice (its whole hand) while a Sly card is there (CardModel.IsSlyThisTurn): the Sly card plays
    /// itself (CardCmd.Discard). A discard you pick isn't counted: a pick is a card, never a particular one (decision #92).
    /// </summary>
    public bool MayDiscardSly { get; init; }

    /// <summary>
    /// Cards the play draws: its own draws plus the draws it sets off (ChainedDraws), limited by the
    /// room in your hand (draws stop at a full hand), and 0 while No Draw or Fiddle blocks draws.
    /// </summary>
    public int Draws { get; private set; }

    /// <summary>Cards the card itself draws, across plays.</summary>
    public int OwnDraws { get; init; }

    /// <summary>
    /// The most it can draw, when that depends on what it draws (Pillage draws again while it draws an Attack: up to a full hand;
    /// decision #118). Otherwise <see cref="Draws"/>: the play check's upper bound on draws.
    /// </summary>
    public int DrawsAtMost { get; init; }

    /// <summary>
    /// A card that acts on what it draws (decision #119): each tally its draw can give (SimState.DrawnTally) and its share of the
    /// outcomes, from the simulation its chance tab came from; null otherwise. The play check holds what happened to it.
    /// </summary>
    public SortedDictionary<int, double>? Drawn { get; set; }

    private PlayChain.DrawInputs? _drawInputs;

    /// <summary>
    /// No Draw (or Fiddle) stops draws the play would make (its own, or ones it sets off): the No Draw tab (decision #171). Worked out
    /// on the same count with nothing blocked.
    /// </summary>
    public bool DrawsStopped
    {
        get
        {
            if (!DrawsBlocked || _drawInputs is not { } drawInputs) return false;
            drawInputs.Blocked = false;
            try { return PlayChain.Resolve(drawInputs).Draws > 0; }
            finally { drawInputs.Blocked = true; }
        }
    }

    /// <summary>
    /// The simulation found the play losing you HP in some outcome (an enemy's Galvanic on a Galvanized card, Thorns hitting back, a
    /// Corrupted card), which sets off Centennial Puzzle while the card's own count (LosesHp: only its HP cost) doesn't have it: Draws,
    /// ChainedDraws and Shuffles worked out again with its draws (the Shuffle Warning, the checks' draw budget). Once; nothing when
    /// the count already has them or Centennial Puzzle is used up.
    /// </summary>
    internal void CountSimulatedHpLoss()
    {
        if (_drawInputs is not { LosesHp: false, CentennialPuzzle: > 0 } drawInputs) return;
        drawInputs.LosesHp = true;
        var result = PlayChain.Resolve(drawInputs);
        ChainedDraws = result.Chained;
        Draws = result.Draws;
        Shuffles = Shuffles || result.Shuffles;
    }

    /// <summary>
    /// The play's draws (Draws) had the card itself drawn <paramref name="own"/> instead, across plays: a card whose draws depend on
    /// its target (Ki Hop, Wrest) at the target it was played at, still stopping at a full hand. Null when not worked out here.
    /// </summary>
    public int? DrawsWithOwn(int own)
    {
        if (_drawInputs is not { } drawInputs) return null;
        return PlayChain.Resolve(drawInputs.WithOwnDraws(own)).Draws;
    }

    /// <summary>Of those, the card's enchantment's (Swift): drawn after the card's own play.</summary>
    public int EnchantmentDraws { get; init; }

    /// <summary>
    /// Draws your powers and relics add because of this play (CardPileCmd.Draw in their hooks):
    /// Dark Embrace per card exhausted, Joss Paper on reaching its exhaust count, Game Piece on a
    /// Power, Iron Club on completing its count, Vicious per Vulnerable you apply, Centennial Puzzle
    /// on first HP loss, Motif on your first Wah, Cho and Ki each turn. Draws that depend on which card is drawn (Pagestorm, Iteration) aren't
    /// predictable (the draw order is hidden) and aren't counted.
    /// </summary>
    public int ChainedDraws { get; private set; }

    /// <summary>Every draw is blocked this turn (No Draw, Fiddle).</summary>
    public bool DrawsBlocked { get; private set; }

    /// <summary>
    /// The play's draws run past the draw pile while the discard pile has cards, so the discard pile
    /// is shuffled in mid-play (CardPileCmd.ShuffleIfNecessary before each draw).
    /// </summary>
    public bool Shuffles { get; private set; }

    /// <summary>Its own play shuffles your discard pile in (Reboot): the card text says so, so no Shuffle Warning tab.</summary>
    public bool ShufflesItself { get; private set; }

    /// <summary>
    /// The card's owner; null for the game's template card (ModelDb, read by the coverage scan), whose Owner
    /// throws (CardModel.Owner asserts a mutable model).
    /// </summary>
    private static Player? OwnerOf(CardModel card) => card.IsCanonical ? null : card.Owner;

    /// <summary>
    /// The play shuffles your discard pile into your draw pile before its draws, read from its program (decision #182): on every play
    /// (CardEffects.ShufflesDiscardPileIn: Reboot), or when the draw pile is short (CardEffects.ShufflesWhenShort: Ki Chant).
    /// </summary>
    internal static bool ShufflesFirst(CardModel card)
    {
        if (Programs.CardEffects.ShufflesDiscardPileIn(card.GetType())) return true;
        if (!Programs.CardEffects.ShufflesWhenShort(card.GetType()) || OwnerOf(card) is not { } me) return false;
        // KiChant.OnPlay: an empty draw pile shuffles if it must; a short one shuffles when the discard pile has cards.
        var draw = Sts2State.DrawPileCount(me);
        return draw < Sts2State.Var(card, "Cards", fallback: 3) && Sts2State.DiscardPileCount(me) > 0;
    }

    /// <summary>
    /// What the outcome simulator was given for this card on that frame (set by Proc Indicators): the
    /// start state, its enemy ids, and the play per target id (-1 for untargeted cards). The audit
    /// replays it against the state the play actually left.
    /// </summary>
    public SimInputs? Sim { get; set; }

    /// <summary>Separate times it gains you block (most block cards: 1; Second Wind: per card exhausted).</summary>
    public int BlockGains { get; init; }

    /// <summary>Cards it creates (Shivs, Souls, clones, statuses...), and how many of those are statuses.</summary>
    public int Generated { get; private set; }
    /// <summary>The ones of those that go into your hand (MadeIntoHand): the rest go to your draw or discard pile.</summary>
    public int GeneratedInHand { get; private set; }
    public int GeneratedStatuses { get; private set; }
    /// <summary>Of those, Sadness cards you create (Fertilizer, All Is Well react: AfterCardGeneratedForCombat).</summary>
    public int SadnessCreated { get; private set; }
    /// <summary>
    /// Cards it certainly transforms: each transformed card is a created card (CardCmd.Transform calls
    /// AfterCardGeneratedForCombat when the new card lands in a combat pile), so created-card procs react. None when you choose how
    /// many it transforms (Guards: 0 or more, read from its pick's selector).
    /// </summary>
    public int TransformsCertain => TransformsChosen(Card) ? 0 : Transforms;
    /// <summary>
    /// Cards outside your hand this play brings back (Make It So, Right Hand Hand, Whistling Arrows: ReturnToHand), in your
    /// hand after it; their tabs.
    /// </summary>
    public IReadOnlyList<CardModel> CardsBack { get; init; } = Array.Empty<CardModel>();
    public int ReturnsToHand => CardsBack.Count;
    /// <summary>
    /// Cards the play itself moves into your hand from a pile (CardPileCmd.Add to PileType.Hand: Hologram, Wish, Dredge...,
    /// read from its program: CardEffects.TakesIntoHand), as many as the piles hold and your hand has room for; in your hand after it.
    /// </summary>
    public int TakenToHand { get; init; }
    /// <summary>The play moves cards into your hand and you choose how many (Neow's Fury: 0 or more): your hand after it is your later decision.</summary>
    public bool HandTakeChosen { get; init; }
    /// <summary>Energy the play certainly gives back (Sown, on the card's first play: Sown.OnPlay while Normal).</summary>
    public int EnergyBack { get; init; }

    /// <summary>
    /// Most cards it can transform (Begone, Charge, Guards...). Each new card is a created card (CardCmd.Transform
    /// calls AfterCardGeneratedForCombat when it lands in a combat pile), but it doesn't change your hand size, so it's
    /// kept out of Generated: created-card procs count TransformsCertain, the audit's budget this.
    /// </summary>
    public int Transforms { get; init; }

    /// <summary>Debuff powers it applies to enemies (from its PowerVar&lt;T&gt; vars), e.g. Weak, Vulnerable, Doom.</summary>
    public IReadOnlyList<Type> EnemyDebuffs { get; init; } = Array.Empty<Type>();

    /// <summary>Temporary debuffs it applies to enemies (Dark Shackles, Piercing Wail: Strength back at the end of the turn).</summary>
    public IReadOnlyList<Type> EnemyTemporaryDebuffs { get; init; } = Array.Empty<Type>();

    /// <summary>Enemies its debuffs can land on: every enemy for all-enemy cards, else each possible target.</summary>
    public bool HitsAllEnemies { get; init; }

    /// <summary>Orbs it channels (for Metronome).</summary>
    public int Channels { get; init; }

    /// <summary>Times it gains you stars (for Black Hole).</summary>
    public int StarGains { get; init; }

    /// <summary>Times Osty attacks because of it (for Bone Flute).</summary>
    public int OstyAttacks { get; init; }

    /// <summary>
    /// Osty makes the card's attack (DamageCmd.Attack(...).FromOsty: Bone Shards, Poke, Squeeze...): its hits are Osty's, through the
    /// game's damage math with Osty as the dealer, and only while Osty is alive (Osty.CheckMissingWithAnim).
    /// </summary>
    public bool ByOsty { get; init; }

    /// <summary>It makes you lose HP (HpLoss value), for Rupture.</summary>
    public bool LosesHp { get; init; }

    /// <summary>HP it makes you lose, across plays.</summary>
    public int HpLoss { get; init; }

    public int TotalExhausts => (ExhaustsSelf ? 1 : 0) + ExhaustsOthers;

    // ------------------------------------------------------------------ attacks on enemies

    /// <summary>
    /// Hits of an attack card across plays, not counting target-dependent hits (see HitsOn).
    /// From HitsPerPlay: hard-coded multi-hits, X costs and calculated hits, else Repeat, else 1.
    /// </summary>
    public int AttackHits { get; init; }

    /// <summary>Cards in hand besides this one (Fiend Fire hits once per card it exhausts).</summary>
    private int OtherHandCards { get; init; }

    /// <summary>Hits on this enemy across plays (Dismantle hits Vulnerable enemies twice).</summary>
    public int HitsOn(Creature enemy)
        => AttackHits == 0 ? 0 : HitsPerPlay(Card, Combat, enemy) * Math.Max(1, Plays);

    /// <summary>
    /// Whether this card can hit <paramref name="enemy"/> with a powered attack (your attack, not
    /// Osty's): all-enemy attacks hit everyone; single-target and random ones can hit any enemy.
    /// </summary>
    public bool CanHit(Creature enemy) => AttackHits > 0 && enemy.IsAlive && enemy.IsHittable;

    /// <summary>
    /// Damage per hit on <paramref name="enemy"/>, through the game's own damage math
    /// (Hook.ModifyDamage with this card as the source: Strength, Vulnerable, caps...).
    /// </summary>
    public int DamagePerHit(Creature enemy) => Math.Max(0, (int)RawDamagePerHit(enemy));

    /// <summary>The game's damage for one hit before it's rounded down (Hook.ModifyDamage).</summary>
    private decimal RawDamagePerHit(Creature enemy)
        => RawDamagePerHit(enemy, MegaCrit.Sts2.Core.Hooks.ModifyDamageHookType.All, out _);

    /// <summary>The game's damage for one hit through the given damage hooks, and the card's damage props.</summary>
    private decimal RawDamagePerHit(Creature enemy, MegaCrit.Sts2.Core.Hooks.ModifyDamageHookType hooks, out MegaCrit.Sts2.Core.ValueProps.ValueProp props, decimal baseDelta = 0m)
    {
        props = MegaCrit.Sts2.Core.ValueProps.ValueProp.Move;
        if (AttackHits == 0) return 0;
        try
        {
            decimal raw;
            // The card's own damage props, as its preview uses them (DamageVar.Props): an arrow attack's
            // FromArrow is what Aim, Heavy Shot's doubled Aim, Dislike You and Quietus check (v0.113.0).
            if (Card.DynamicVars.TryGetValue("CalculatedDamage", out var dynamicVar) && dynamicVar is CalculatedVar calc)
            {
                raw = calc.Calculate(enemy);
                if (dynamicVar is CalculatedDamageVar cdv) props = cdv.Props;
            }
            else if (Card.DynamicVars.TryGetValue("Damage", out var otherDynamicVar) || Card.DynamicVars.TryGetValue("OstyDamage", out otherDynamicVar))
            {
                raw = otherDynamicVar.BaseValue;
                if (otherDynamicVar is DamageVar damageVar) props = damageVar.Props;
            }
            else return 0;
            var dealer = ByOsty && Me.Osty is { } osty ? osty : Me.Creature;
            var damage = MegaCrit.Sts2.Core.Hooks.Hook.ModifyDamage(Me.RunState, Combat, enemy, dealer, raw + baseDelta, props,
                Card, null, hooks, CardPreviewMode.None, out _);
            return Math.Max(0m, damage);
        }
        catch
        {
            return 0;
        }
    }

    /// <summary>
    /// Total damage this card deals to the enemy across all its hits (before block), at the damage its first hit has now: an
    /// estimate (the simulator works out each hit as it lands).
    /// </summary>
    public int TotalDamage(Creature enemy) => DamagePerHit(enemy) * HitsOn(enemy);

    // ------------------------------------------------------------------ orbs evoked by the play

    /// <summary>
    /// Damage each enemy takes from orbs this play evokes, when the target is certain: Glass hits
    /// every enemy, Dark the lowest-HP one, Lightning a random one (counted only with one enemy).
    /// </summary>
    public IReadOnlyDictionary<Creature, int> OrbDamage { get; init; } = new Dictionary<Creature, int>();

    /// <summary>Total orb evoke damage to enemies, including random Lightning (for the audit's range).</summary>
    public int OrbDamageMin { get; init; }
    public int OrbDamageMax { get; init; }

    /// <summary>Lightning orbs this play evokes (Thunder hits each one's target again).</summary>
    public int LightningEvokes { get; init; }

    /// <summary>Block you gain from Frost orbs this play evokes (EvokeVal, Unpowered).</summary>
    public int OrbBlock { get; init; }

    /// <summary>
    /// The play's outcome depends on a roll with more than one possible target: Lightning evokes or
    /// a random-target attack with several enemies, or (set by Proc Indicators) a random-target proc.
    /// The audit doesn't report what a random kill or hit could have set off.
    /// </summary>
    public bool RandomOutcome { get; set; }

    /// <summary>
    /// Its simulation was still running when the frame stopped waiting (the 250 ms cap), so its
    /// lethal, Imbalanced and wake tabs may be missing. The audit doesn't report those.
    /// </summary>
    public bool SimPending { get; set; }
    /// <summary>
    /// Its tabs came from a simulation stopped at a cap (decision #107): what's shown carries the ?, and an outcome it never reached
    /// may be what happens. The audit doesn't report the simulator's tabs then; it counts the skip.
    /// </summary>
    public bool SimIncomplete { get; set; }

    /// <summary>
    /// Per aim (the simulation key, SimInputs.PlayByTarget): each enemy's chance of dying before it acts after this play and the end
    /// of turn (decision #87): the lethal tab's inputs, checked against the board after the play (ProcAudit.LethalIssues).
    /// </summary>
    public IReadOnlyDictionary<int, IReadOnlyDictionary<int, SimChance>>? DeathByAim { get; set; }

    /// <summary>
    /// Per aim: the whole outcome of playing it there and ending the turn (Play Preview shows it while the card is held and aimed:
    /// Incoming Damage, the enemies' intents and their death odds after the play).
    /// </summary>
    public IReadOnlyDictionary<int, SimOutcome>? OutcomeByAim { get; set; }

    /// <summary>
    /// Per aim: the block the play gives you from evoking Frost orbs, least and most over its outcomes (the simulator's, every orb it
    /// evokes, the ones it channels included): the play audit's block check.
    /// </summary>
    public IReadOnlyDictionary<int, (int Min, int Max)>? FrostBlockByAim { get; set; }

    /// <summary>
    /// Per aim: the tabbed sources (proc models) the play sets off in every outcome, and in some. The audit holds the first to their
    /// flash (didn't fire), and excuses a proc a roll decided only when it's in the second.
    /// </summary>
    public IReadOnlyDictionary<int, IReadOnlyCollection<Type>>? FiresEveryByAim { get; set; }
    public IReadOnlyDictionary<int, IReadOnlyCollection<Type>>? FiresAnyByAim { get; set; }
    /// <summary>What its simulations didn't model (decision #92, rule 2): gaps the play audit counts.</summary>
    public IReadOnlyList<string> Unmodelled { get; set; } = Array.Empty<string>();

    /// <summary>
    /// This play followed by the plays of a card it auto-plays (Decisions, Decisions' pick, whose facts
    /// were read during the choice): the counts the audit checks added together, and the pick's
    /// simulation (its start state already includes everything this play did before the choice).
    /// </summary>
    internal PlayFacts Then(PlayFacts next)
    {
        var orbDamage = new Dictionary<Creature, int>(next.OrbDamage);
        foreach (var (creature, d) in OrbDamage) orbDamage[creature] = (orbDamage.TryGetValue(creature, out var already) ? already : 0) + d;
        return new PlayFacts
        {
            Card = Card, Me = Me, Combat = Combat,
            Plays = Plays, Energy = Energy, Stars = Stars,
            ExhaustsSelf = ExhaustsSelf,
            ExhaustsOthers = ExhaustsOthers + next.TotalExhausts,
            Discards = Discards + next.Discards,
            LeavesHand = LeavesHand + next.LeavesHand,
            TakesWholeHand = TakesWholeHand,
            MayDiscardSly = MayDiscardSly || next.MayDiscardSly,
            OwnDraws = OwnDraws + next.OwnDraws,
            DrawsAtMost = DrawsAtMost + next.DrawsAtMost,
            EnchantmentDraws = EnchantmentDraws + next.EnchantmentDraws,
            BlockGains = BlockGains + next.BlockGains,
            Transforms = Transforms + next.Transforms,
            EnemyDebuffs = EnemyDebuffs.Concat(next.EnemyDebuffs).Distinct().ToList(),
            EnemyTemporaryDebuffs = EnemyTemporaryDebuffs.Concat(next.EnemyTemporaryDebuffs).Distinct().ToList(),
            HitsAllEnemies = HitsAllEnemies || next.HitsAllEnemies,
            Channels = Channels + next.Channels,
            StarGains = StarGains + next.StarGains,
            OstyAttacks = OstyAttacks + next.OstyAttacks,
            LosesHp = LosesHp || next.LosesHp,
            HpLoss = HpLoss + next.HpLoss,
            AttackHits = AttackHits + next.AttackHits,
            OrbDamage = orbDamage,
            OrbDamageMin = OrbDamageMin + next.OrbDamageMin,
            OrbDamageMax = OrbDamageMax + next.OrbDamageMax,
            LightningEvokes = LightningEvokes + next.LightningEvokes,
            OrbBlock = OrbBlock + next.OrbBlock,
            Draws = Draws + next.Draws,
            ChainedDraws = ChainedDraws + next.ChainedDraws,
            DrawsBlocked = DrawsBlocked || next.DrawsBlocked,
            Shuffles = Shuffles || next.Shuffles,
            ShufflesItself = ShufflesItself || next.ShufflesItself,
            Generated = Generated + next.Generated,
            GeneratedInHand = GeneratedInHand + next.GeneratedInHand,
            GeneratedStatuses = GeneratedStatuses + next.GeneratedStatuses,
            SadnessCreated = SadnessCreated + next.SadnessCreated,
            CardsBack = CardsBack.Concat(next.CardsBack).ToList(),
            TakenToHand = TakenToHand + next.TakenToHand,
            HandTakeChosen = HandTakeChosen || next.HandTakeChosen,
            EnergyBack = EnergyBack + next.EnergyBack,
            Sim = next.Sim,
            DeathByAim = next.DeathByAim,
            FrostBlockByAim = next.FrostBlockByAim,
            FiresEveryByAim = next.FiresEveryByAim,
            FiresAnyByAim = next.FiresAnyByAim,
            RandomOutcome = RandomOutcome || next.RandomOutcome,
            SimPending = SimPending || next.SimPending,
            SimIncomplete = SimIncomplete || next.SimIncomplete,
            Unmodelled = Unmodelled.Concat(next.Unmodelled).Distinct().ToList(),
        };
    }

    // ------------------------------------------------------------------ building

    public static PlayFacts For(CardModel card, Player me, ICombatState combat, int plays)
    {
        var hand = Sts2State.Hand(me);
        var others = hand.Where(otherCard => !ReferenceEquals(otherCard, card)).ToList();
        var draws = CardsDrawnTotal(card, hand.Count, plays);
        var swiftDraws = 0;
        // Swift (enchantment): draws its amount when the card is played, once (Swift.AfterCardPlayed disables it).
        if (card.Enchantment is MegaCrit.Sts2.Core.Models.Enchantments.Swift { Status: MegaCrit.Sts2.Core.Entities.Enchantments.EnchantmentStatus.Normal } swift)
            swiftDraws = swift.Amount;
        var drawPile = Sts2State.DrawPileCount(me);
        var name = card.GetType().Name;
        var evoked = EvokedOrbs(card, me, ChannelCount(card) * Math.Max(1, plays), Math.Max(1, plays));
        var orbs = OrbDamageFor(evoked, me, combat);
        var generated = GeneratedCount(card, name, others, me, out var statuses) * Math.Max(1, plays) + ForgedBlade(card.GetType(), me);
        // Cards the play puts into your hand (Bundle of Joy's four): they fill it before anything comes back.
        var createdInHand = MadeIntoHand(card, generated);

        var facts = new PlayFacts
        {
            Card = card,
            Me = me,
            Combat = combat,
            Plays = Math.Max(1, plays),
            Energy = SafeEnergy(card),
            AutoPlayed = CardPlays.ChoiceAutoPlayed(card),
            Stars = SafeStars(card),
            // Exhaust keyword (including added Exhaust), or Corruption sending a played skill to the
            // exhaust pile (CorruptionPower.ModifyCardPlayResultLocation).
            ExhaustsSelf = card.Type != CardType.Power
                           && (card.Keywords.Contains(CardKeyword.Exhaust)
                               || (card.Type == CardType.Skill && me.Creature.Powers.Any(power => power is CorruptionPower))),
            ExhaustsOthers = ExhaustOthers(card, others, drawPile, plays),
            // A play that looks at your draw pile (Ki Chant) discards its share each play; the others depend on the hand.
            Discards = DiscardCount(card, others, draws, plays),
            LeavesHand = LeavingHand(card, others, drawPile, draws, plays),
            TakesWholeHand = Programs.CardEffects.TakesWholeHand(card.GetType()),
            // A discard of your hand without a pick (Storm of Steel, Calculated Gamble) with a Sly card there.
            MayDiscardSly = Programs.CardEffects.Discards(card.GetType()).Any(discard => discard.Way == Programs.CardEffects.TakeWay.Each)
                            && DiscardedFromHand(card, others, draws) > 0 && others.Any(other => other.IsSlyThisTurn),
            OwnDraws = draws + swiftDraws,
            DrawsAtMost = SpireQoL.Sim.SimAdapter.DrawnCardReaders.GetValueOrDefault(name) is { DrawsAgain: true }
                ? Math.Max(draws, CardPile.MaxCardsInHand - (hand.Count - 1)) + swiftDraws : 0,
            EnchantmentDraws = swiftDraws,
            BlockGains = BlockGainCount(card) * Math.Max(1, plays),
            Generated = generated,
            GeneratedInHand = createdInHand,
            GeneratedStatuses = statuses * Math.Max(1, plays),
            SadnessCreated = Programs.CardEffects.MakesSadness(card.GetType()) ? statuses * Math.Max(1, plays) : 0,
            CardsBack = ReturnToHand.Returning(card, me, plays, Math.Min(CardPile.MaxCardsInHand, hand.Count - 1 + createdInHand) + draws + swiftDraws,
                Fool.Shoots(card, me) ? Math.Max(1, plays) : 0),
            TakenToHand = TakenToHandCount(card, Math.Max(1, plays),
                room: CardPile.MaxCardsInHand - Math.Min(CardPile.MaxCardsInHand, hand.Count - 1 + createdInHand + draws + swiftDraws)),
            HandTakeChosen = HandTakeChosenOf(card, hand.Count),
            EnergyBack = card.Enchantment is MegaCrit.Sts2.Core.Models.Enchantments.Sown { Status: MegaCrit.Sts2.Core.Entities.Enchantments.EnchantmentStatus.Normal } sown ? sown.Amount : 0,
            Transforms = TransformCount(card, others, drawPile) * Math.Max(1, plays),
            // A shot with no arrow to shoot does nothing at all (its debuffs neither: Hobbling Shot).
            EnemyDebuffs = Fool.IsShot(card) && !Fool.Shoots(card, me) ? Array.Empty<Type>() : EnemyDebuffTypes(card),
            EnemyTemporaryDebuffs = Fool.IsShot(card) && !Fool.Shoots(card, me) ? Array.Empty<Type>() : EnemyDebuffTypes(card, temporary: true),
            HitsAllEnemies = card.TargetType == TargetType.AllEnemies,
            Channels = ChannelCount(card) * Math.Max(1, plays),
            StarGains = StarGainsOf(card) * Math.Max(1, plays),
            OstyAttacks = IsOstyAttack(card) ? Math.Max(1, plays) : 0,
            LosesHp = card.DynamicVars.ContainsKey("HpLoss"),
            HpLoss = Sts2State.Var(card, "HpLoss") * Math.Max(1, plays),
            OtherHandCards = others.Count,
            ByOsty = IsOstyAttack(card),
            AttackHits = (IsOstyAttack(card) ? me.IsOstyAlive && IsAttackOnEnemies(card) : IsPlayerAttack(card)) ? HitsPerPlay(card, combat, target: null) * Math.Max(1, plays) : 0,
            OrbDamage = orbs.Exact,
            OrbDamageMin = orbs.Min,
            OrbDamageMax = orbs.Max,
            LightningEvokes = evoked.Count(orb => orb is LightningOrb),
            OrbBlock = OrbBlockFor(evoked, me, combat),
            RandomOutcome = combat.HittableEnemies.Count(creature => creature.IsAlive) > 1
                            && (evoked.Any(orb => orb is LightningOrb) || card.TargetType == TargetType.RandomEnemy),
        };
        // Exhausts from your draw pile (Cleanse's pick, read from its program) leave your hand's room alone.
        facts.ResolveDraws(hand.Count, drawPile, Programs.CardEffects.ExhaustsOfOthers(card.GetType()).Any(exhaust => exhaust.FromDrawPile));
        return facts;
    }

    /// <summary>
    /// How many of the <paramref name="generated"/> cards a play makes go into your hand: the share of its make calls that name your
    /// hand (CardCode.MadeInto, the pile each passes, read from its code: Anger's copy into your discard pile, Severance's one of
    /// three into your hand), exact when each call makes as many (they do). Made some other way (a forge): into your hand.
    /// </summary>
    internal static int MadeIntoHand(CardModel card, int generated)
    {
        var piles = CardCode.MadeInto(card.GetType());
        if (piles.Count == 0 || generated <= 0) return generated;
        var hand = piles.Count(pile => pile is null or PileType.Hand);
        return hand == piles.Count ? generated : generated * hand / piles.Count;
    }

    /// <summary>
    /// Total draws (own + chained), the hand limit, draw blockers, and whether they shuffle: the
    /// game's numbers for PlayChain.Resolve (pure, tested offline). Sources read here: Dark Embrace,
    /// Vicious, Joss Paper, Game Piece, Iron Club, Centennial Puzzle, Motif; No Draw and Fiddle block draws.
    /// </summary>
    private void ResolveDraws(int handCount, int drawPile, bool fromDrawPile)
    {
        var me = Me;
        var drawInputs = new PlayChain.DrawInputs
        {
            OwnDraws = OwnDraws - EnchantmentDraws,
            EnchantmentDraws = EnchantmentDraws,
            // Cards the card itself puts into your hand, before its enchantment draws.
            AddedToHandBeforeEnchantment = EnchantmentDraws > 0 ? GeneratedInHand : 0,
            Plays = Math.Max(1, Plays),
            IsPower = Card.Type == CardType.Power,
            Exhausts = TotalExhausts,
            ExhaustedFromHand = fromDrawPile ? 0 : ExhaustsOthers,
            ExhaustedFromDrawPile = fromDrawPile ? ExhaustsOthers : 0,
            HandCount = handCount,
            DrawPile = drawPile,
            DiscardPile = Sts2State.DiscardPileCount(me),
            // Discards before its draws, in its code's order (decision #182: its program; Acrobatics draws first). A pick whose card is
            // either exhausted or discarded (The Hermit: a Curse is exhausted) discards what its exhausts (in ExhaustedFromHand already)
            // leave of it.
            DiscardedBeforeDraw = !Programs.CardPrograms.DiscardsBeforeDraws(Card.GetType()) ? 0
                : SharedPickSize(Card, OtherHandCards) is { } shared ? Math.Max(0, shared - ExhaustsOthers) : Discards,
            ShufflesFirst = ShufflesFirst(Card),
            HandShuffledIn = Programs.CardEffects.ShufflesDiscardPileIn(Card.GetType()),
            // Calamity adds random attacks to your hand after each attack you play, before your relics draw.
            AddedToHandAfterPlay = Card.Type == CardType.Attack ? (me.Creature.GetPower<CalamityPower>()?.Amount ?? 0) * Math.Max(1, Plays) : 0,
            // Motif draws its amount per melody (Wah, Cho, Ki) this card plays first this turn, once (a replay's
            // later plays aren't first).
            PowerDrawsAfterPlay = (me.Creature.Powers.FirstOrDefault(power => power.GetType().Name == nameof(MotifPower))?.Amount ?? 0)
                                  * Fool.FirstMelodies(Card, me).Count(),
            VulnerableLandings = DebuffsLandingTotal(type => type == typeof(VulnerablePower)),
            LosesHp = HpLoss > 0,
            Blocked = me.Creature.HasPower<NoDrawPower>() || me.Relics.Any(relic => relic is Fiddle),
            DarkEmbrace = me.Creature.GetPower<DarkEmbracePower>()?.Amount ?? 0,
            Vicious = me.Creature.GetPower<ViciousPower>()?.Amount ?? 0,
        };
        foreach (var relic in me.Relics)
        {
            switch (relic)
            {
                case JossPaper:
                    drawInputs.JossPaperCount = Sts2State.Private(relic, "CardsExhausted", fallback: 0);
                    drawInputs.JossPaperEvery = Sts2State.Var(relic, "ExhaustAmount", fallback: 5);
                    break;
                case GamePiece: drawInputs.GamePiece += Sts2State.Var(relic, "Cards", fallback: 1); break;
                case IronClub:
                    drawInputs.IronClubEvery = Sts2State.Var(relic, "Cards", fallback: 4);
                    drawInputs.IronClubCount = relic.DisplayAmount; // CardsPlayed % Cards (reads Cards while it animates: % again)
                    break;
                case CentennialPuzzle when !Sts2State.Private(relic, "UsedThisCombat", fallback: true): drawInputs.CentennialPuzzle += Sts2State.Var(relic, "Cards", fallback: 3); break;
            }
        }
        if (Programs.CardEffects.ShufflesWhenShort(Card.GetType()))
        {
            // Ki Chant takes the top cards it looks at off the draw pile: one to your hand, the rest discarded.
            var looked = LooksAt(Card);
            var available = drawPile + (drawInputs.ShufflesFirst ? drawInputs.DiscardPile : 0);
            drawInputs.TakenFromDrawPile = Math.Min(looked * Math.Max(1, Plays), available);
            drawInputs.AddedToHandDuringPlay = looked > 0 ? Math.Min(Math.Max(1, Plays), drawInputs.TakenFromDrawPile) : 0;
        }
        _drawInputs = drawInputs;
        var result = PlayChain.Resolve(drawInputs);
        DrawsBlocked = drawInputs.Blocked;
        ChainedDraws = result.Chained;
        Draws = result.Draws;
        // Ki Chant replayed: a later play can find the draw pile short and shuffle (its own text, like its first play's).
        var laterShuffle = Programs.CardEffects.ShufflesWhenShort(Card.GetType())
                           && PlayChain.ShortPileShufflesOnLaterPlay(drawPile, drawInputs.DiscardPile, Sts2State.Var(Card, "Cards", fallback: 3), Math.Max(1, Plays));
        Shuffles = result.Shuffles || laterShuffle;
        ShufflesItself = drawInputs.ShufflesFirst || laterShuffle;
        // Biiig Hug puts a Soot (a status) into your draw pile on the shuffle: a created status.
        if (Shuffles && me.Relics.Any(relic => relic is BiiigHug))
        {
            Generated += 1;
            GeneratedStatuses += 1;
        }
    }

    /// <summary>
    /// Existing orbs this play evokes, in order: evoke cards (Dualcast and Quadcast evoke the front
    /// orb several times, Multicast X times, Shatter every orb twice), and channelling into full
    /// slots (each channel past capacity evokes the front orb first; OrbCmd.Channel). Orbs the play
    /// channels and then evokes itself aren't counted (their type may be random).
    /// </summary>
    private static List<OrbModel> EvokedOrbs(CardModel card, Player me, int channels, int plays)
    {
        var result = new List<OrbModel>();
        var orbQueue = me.PlayerCombatState?.OrbQueue;
        if (orbQueue == null) return result;
        var queue = orbQueue.Orbs.Cast<OrbModel?>().ToList();
        var cap = orbQueue.Capacity;
        var plan = OrbsOf(card);
        // A new orb pushed out by a later channel (Voltaic's Lightning filling every slot) evokes too: one of yours
        // of the same kind stands in for it when its evoke doesn't depend on the orb itself (Lightning, Frost).
        var kind = plan.ChannelKind;
        OrbModel? template = kind switch
        {
            SpireQoL.Sim.OrbKind.Lightning => orbQueue.Orbs.OfType<LightningOrb>().FirstOrDefault(),
            SpireQoL.Sim.OrbKind.Frost => orbQueue.Orbs.OfType<FrostOrb>().FirstOrDefault(),
            _ => null,
        };
        void EvokeFront(int times, bool remove)
        {
            if (queue.Count == 0) return;
            for (var time = 0; time < times; time++)
                if (queue[0] is { } orb) result.Add(orb);
            if (remove) queue.RemoveAt(0);
        }
        for (var playIndex = 0; playIndex < plays; playIndex++)
        {
            // Its own evokes (read from its program: OrbsOf): every orb that many times (Shatter), or the front orb (Dualcast, Quadcast, Multicast).
            if (plan.EvokesEachOrb > 0)
                while (queue.Count > 0) EvokeFront(plan.EvokesEachOrb, remove: true);
            else if (plan.EvokesFront > 0) EvokeFront(plan.EvokesFront, remove: true);
            // Slots the card adds before it channels (The Magician: OrbCmd.AddSlots, up to 10).
            var added = plan.Slots;
            if (added > 0) cap = Math.Min(10, cap + added);
            for (var channelIndex = 0; channelIndex < channels / Math.Max(1, plays); channelIndex++)
            {
                if (cap <= 0) break;
                if (queue.Count >= cap) EvokeFront(times: 1, remove: true);
                queue.Add(template); // the new orb (null: its evoke isn't counted)
            }
        }
        return result;
    }

    private static int OrbBlockFor(List<OrbModel> evoked, Player me, ICombatState combat)
    {
        var total = 0;
        foreach (var orb in evoked)
        {
            if (orb is not FrostOrb || orb.EvokeVal <= 0) continue;
            try
            {
                total += Math.Max(0, (int)MegaCrit.Sts2.Core.Hooks.Hook.ModifyBlock(combat, me.Creature, orb.EvokeVal, MegaCrit.Sts2.Core.ValueProps.ValueProp.Unpowered, null, null, out _));
            }
            catch
            {
                total += (int)orb.EvokeVal;
            }
        }
        return total;
    }

    private static (Dictionary<Creature, int> Exact, int Min, int Max) OrbDamageFor(List<OrbModel> evoked, Player me, ICombatState combat)
    {
        var none = (new Dictionary<Creature, int>(), 0, 0);
        if (evoked.Count == 0) return none;
        var enemies = combat.HittableEnemies.Where(creature => creature.IsAlive).ToList();
        if (enemies.Count == 0) return none;
        var exact = new Dictionary<Creature, int>();
        int min = 0, max = 0;
        int Mod(Creature creature, decimal value)
        {
            try
            {
                return Math.Max(0, (int)MegaCrit.Sts2.Core.Hooks.Hook.ModifyDamage(me.RunState, combat, creature, me.Creature, value,
                    MegaCrit.Sts2.Core.ValueProps.ValueProp.Unpowered, null, null, MegaCrit.Sts2.Core.Hooks.ModifyDamageHookType.All, CardPreviewMode.None, out _));
            }
            catch
            {
                return (int)value;
            }
        }
        void Add(Creature creature, int damage) => exact[creature] = (exact.TryGetValue(creature, out var already) ? already : 0) + damage;
        foreach (var orb in evoked)
        {
            var evokeVal = orb.EvokeVal;
            if (evokeVal <= 0) continue;
            switch (orb)
            {
                case GlassOrb:
                    foreach (var creature in enemies)
                    {
                        var mod = Mod(creature, evokeVal);
                        Add(creature, mod);
                        min += mod;
                        max += mod;
                    }
                    break;
                case DarkOrb:
                {
                    var weakest = enemies.MinBy(creature => creature.CurrentHp)!;
                    var mod = Mod(weakest, evokeVal);
                    Add(weakest, mod);
                    min += mod;
                    max += mod;
                    break;
                }
                case LightningOrb:
                    if (enemies.Count == 1)
                    {
                        var mod = Mod(enemies[0], evokeVal);
                        Add(enemies[0], mod);
                        min += mod;
                        max += mod;
                    }
                    else
                    {
                        min += enemies.Min(creature => Mod(creature, evokeVal));
                        max += enemies.Max(creature => Mod(creature, evokeVal));
                    }
                    break;
            }
        }
        return (exact, min, max);
    }

    private static int SafeEnergy(CardModel card)
    {
        try { return card.EnergyCost.GetAmountToSpend(); } catch { return 0; }
    }

    private static int SafeStars(CardModel card)
    {
        try
        {
            if (!card.HasStarCostX && card.CurrentStarCost < 0) return 0;
            return Math.Max(0, card.GetStarCostWithModifiers());
        }
        catch { return 0; }
    }

    // ------------------------------------------------------------------ draws

    /// <summary>
    /// How many cards one play of this card draws right now (0 if it doesn't draw), read from its program (decision #182): each draw
    /// for you its code makes (CardPileCmd.Draw to its owner, CardCmd.DiscardAndDraw), on the condition its code puts it on, evaluated
    /// against the board (ProgramEvaluator: Impatience's hand, Restlessness alone in your hand, FTL's plays this turn, Fetch's Osty,
    /// Mad Science's rider), its count from its own argument (Cards, Scrawl's room in your hand, Calculated Gamble's hand, a count the
    /// game calculates) and its loops' rounds. A draw whose condition asks the target (Ki Hop, Wrest) counts what the worst target it
    /// could be aimed at would draw. <paramref name="handCount"/> isn't read: the hand is the board's.
    /// </summary>
    public static int CardsDrawn(CardModel card, int handCount)
    {
        if (!Programs.CardEffects.DrawsDependOnTarget(card.GetType())) return DrawsOnPlay(card, target: null, playsBefore: 0);
        var targets = Targets(card);
        if (targets == null) return DrawsOnPlay(card, target: null, playsBefore: 0); // no fight to aim at (the game's template card)
        return targets.Count > 0 ? targets.Min(target => DrawsOnPlay(card, target, playsBefore: 0)) : 0;
    }

    /// <summary>
    /// Draws of one play of <paramref name="card"/> aimed at <paramref name="target"/>, after <paramref name="playsBefore"/> of its replay's
    /// plays: each draw its condition lets through, its count times its loops' rounds. A count that can't be read is its Cards value, or
    /// one card when it has none (Dagger Throw, Escape Plan).
    /// </summary>
    private static int DrawsOnPlay(CardModel card, Creature? target, int playsBefore)
    {
        var program = Programs.CardPrograms.Of(card.GetType());
        var draws = Programs.CardEffects.DrawsForYou(program);
        if (draws.Count == 0) return 0;
        var board = new Programs.ProgramBoard(card, target, playsBefore);
        var total = 0;
        foreach (var draw in draws)
        {
            if (Programs.ProgramEvaluator.Holds(draw.Condition, board, program) == false) continue;
            var count = Programs.ProgramEvaluator.Number(draw.Count, board, program) is { } read ? (int)read
                : card.DynamicVars.ContainsKey("Cards") ? Sts2State.Var(card, "Cards") : 1;
            foreach (var loop in draw.Loops)
                count *= card.DynamicVars.ContainsKey(loop.Over) ? Sts2State.Var(card, loop.Over) : 1;
            total += Math.Max(0, count);
        }
        return total;
    }

    /// <summary>The enemies the card could be aimed at; null when it isn't in a fight (the game's template card).</summary>
    private static List<Creature>? Targets(CardModel card)
    {
        try { return card.CombatState?.HittableEnemies.Where(creature => creature.IsAlive).ToList(); }
        catch { return null; }
    }

    /// <summary>
    /// Draws of all <paramref name="plays"/> plays aimed at <paramref name="target"/>, for a card whose draws depend on its target
    /// (CardEffects.DrawsDependOnTarget), from the board before the first play; null for other cards. Each play's condition is
    /// evaluated after the plays before it: Ki Hop draws on each play that starts with the target Impaled, its own hit loosening the
    /// last arrow (SimEngine.PlaysStartingImpaled); Wrest pulls every arrow of yours out on its first play, so only that one draws. The
    /// audit takes it with the play's actual target as the play starts; the prediction counts what the worst target would draw
    /// (CardsDrawnTotal).
    /// </summary>
    public static int? TargetDraws(CardModel card, Creature target, int plays)
    {
        if (!Programs.CardEffects.DrawsDependOnTarget(card.GetType())) return null;
        var total = 0;
        for (var playIndex = 0; playIndex < Math.Max(1, plays); playIndex++) total += DrawsOnPlay(card, target, playIndex);
        return total;
    }

    /// <summary>
    /// How many cards all <paramref name="plays"/> plays of this card draw right now, each play's conditions after the plays before it
    /// (FTL draws while fewer than PlayMax plays have finished this turn, a replay's earlier plays included; Fetch only on its first).
    /// A card whose draws depend on its target counts what the worst target it could be aimed at would draw.
    /// </summary>
    public static int CardsDrawnTotal(CardModel card, int handCount, int plays)
    {
        plays = Math.Max(1, plays);
        if (!Programs.CardEffects.DrawsDependOnTarget(card.GetType()))
        {
            var total = 0;
            for (var playIndex = 0; playIndex < plays; playIndex++) total += DrawsOnPlay(card, target: null, playIndex);
            return total;
        }
        var targets = Targets(card);
        if (targets == null) return DrawsOnPlay(card, target: null, playsBefore: 0) * plays;
        return targets.Count > 0 ? targets.Min(target => TargetDraws(card, target, plays) ?? 0) : 0;
    }

    /// <summary>How many of <paramref name="plays"/> plays in a row start with fewer than <paramref name="max"/> plays finished, <paramref name="finishedBefore"/> before the first.</summary>
    internal static int PlaysUnder(int finishedBefore, int max, int plays) => Math.Clamp(max - finishedBefore, 0, Math.Max(1, plays));

    // ------------------------------------------------------------------ cards taken: exhausted, discarded, transformed, moved into your hand

    /// <summary>
    /// Other cards the play exhausts, read from its program (decision #182: CardEffects.ExhaustsOfOthers), counted on the board
    /// (Taken): a pick takes one card a play (a pick's first: Brand, Burning Pact) or the selector's most (Purity), from your hand or your
    /// draw pile (Cleanse), of the cards its test on the picked card lets through (The Hermit: a Curse); a roll takes one card a play from
    /// what it rolls over (Thrash: your Attacks); a loop takes each card of what it goes over once (Fiend Fire your hand, Second Wind its
    /// non-Attacks, Growth its Sadness, Flak Cannon your statuses: a later play finds them gone). Only the exhausts whose condition holds
    /// (True Grit's pick when upgraded, its roll when not). A collection that can't be told on the board (the game's template card) is
    /// <paramref name="others"/>.
    /// </summary>
    internal static int ExhaustOthers(CardModel card, List<CardModel> others, int drawPile, int plays) =>
        Taken(card, Programs.CardEffects.ExhaustsOfOthers(card.GetType()), others, drawPile, draws: 0, plays, fromHandOnly: false, pickEachPlay: true);

    /// <summary>Of those, the ones that leave your hand: not Cleanse's (your draw pile); of a collection from every pile, the ones in your hand.</summary>
    private static int ExhaustedFromHand(CardModel card, List<CardModel> others, int drawPile, int plays) =>
        Taken(card, Programs.CardEffects.ExhaustsOfOthers(card.GetType()), others, drawPile, draws: 0, plays, fromHandOnly: true, pickEachPlay: true);

    /// <summary>
    /// Cards the play discards, read from its program (CardEffects.Discards: CardCmd.Discard and DiscardAndDraw), counted on the board
    /// (Taken): a pick from your hand, after the draws its code makes first (Acrobatics, Prepared; Survivor, Hidden Daggers before any),
    /// of the cards its test lets through (The Hermit: not a Curse); your whole hand (Storm of Steel, Calculated Gamble); the cards it drew
    /// that its filter keeps (Scrape: at most <paramref name="draws"/>); the cards it looked at on top of your draw pile, less the one taken
    /// (Ki Chant, each play). A pick's later plays aren't counted yet (TRACKER).
    /// </summary>
    internal static int DiscardCount(CardModel card, List<CardModel> others, int draws, int plays = 1) =>
        Taken(card, Programs.CardEffects.Discards(card.GetType()), others, DrawPileOf(card), draws, plays, fromHandOnly: false, pickEachPlay: false);

    /// <summary>Of those, the ones from your hand: not the cards it drew (Scrape's: the simulator's), nor the ones it looked at (Ki Chant's).</summary>
    private static int DiscardedFromHand(CardModel card, List<CardModel> others, int draws) =>
        Taken(card, Programs.CardEffects.Discards(card.GetType()), others, DrawPileOf(card), draws, plays: 1, fromHandOnly: true, pickEachPlay: false);

    /// <summary>
    /// Most cards a play transforms, read from its program (CardEffects.Transforms: CardCmd.Transform, TransformTo), counted on the board
    /// (Taken) among the cards CardCmd.Transform can take (IsTransformable): a pick from your hand (Begone one, Guards as many as you
    /// pick) or your draw pile (Charge, Seance), each of a collection (Compact your statuses, Primal Force your Attacks), the first of one
    /// (Punch of Joy's Sadness).
    /// </summary>
    internal static int TransformCount(CardModel card, List<CardModel> others, int drawPile) =>
        Taken(card, Programs.CardEffects.Transforms(card.GetType()), others, drawPile, draws: 0, plays: 1, fromHandOnly: false, pickEachPlay: false, Transformable);

    /// <summary>The play transforms only cards you choose how many of (Guards: 0 or more): none certainly.</summary>
    private static bool TransformsChosen(CardModel card) =>
        Programs.CardEffects.Transforms(card.GetType()) is { Count: > 0 } transforms && transforms.All(transform => transform.ChoosesHowMany);

    private static bool Transformable(CardModel card)
    {
        try { return card.IsTransformable; } catch { return true; }
    }

    /// <summary>
    /// How many cards a pick takes that its play then exhausts or discards, by a test on the card (The Hermit: a Curse is exhausted,
    /// anything else discarded): the selector's most of <paramref name="otherCards"/>. Null when no pick is shared that way.
    /// </summary>
    private static int? SharedPickSize(CardModel card, int otherCards)
    {
        var type = card.GetType();
        var exhausts = Programs.CardEffects.ExhaustsOfOthers(type);
        if (Programs.CardEffects.Discards(type).FirstOrDefault(discard => discard.Pick != null && exhausts.Any(exhaust => ReferenceEquals(exhaust.Pick, discard.Pick))) is not { } shared)
            return null;
        var perPick = Programs.ProgramEvaluator.Number(shared.PerPick, new Programs.ProgramBoard(card), Programs.CardPrograms.Of(type)) is { } most ? (int)most : 1;
        return Math.Min(perPick, otherCards);
    }

    /// <summary>
    /// How many cards the play moves into your hand from your piles (CardEffects.TakesIntoHandOf: CardPileCmd.Add to the hand), counted
    /// on the board (Taken: a pick a play, a roll's count, each card of a collection), as many as <paramref name="room"/> leaves in your
    /// hand (a card added to a full hand goes to the discard pile instead: CardPileCmd.Add). None when you choose how many (Neow's Fury:
    /// HandTakeChosen). Which cards is a pick or a roll; how many is in the piles now.
    /// </summary>
    internal static int TakenToHandCount(CardModel card, int plays, int room)
    {
        var takes = Programs.CardEffects.TakesIntoHandOf(card.GetType());
        if (takes.Count == 0 || room <= 0 || takes.Any(take => take.ChoosesHowMany)) return 0;
        return Math.Min(room, Taken(card, takes, new List<CardModel>(), DrawPileOf(card), draws: 0, plays, fromHandOnly: false, pickEachPlay: true));
    }

    /// <summary>
    /// The play takes cards into your hand and you choose how many (a selector whose least and most differ: Neow's Fury), with cards to
    /// take and room in your hand (<paramref name="handCount"/> with the card played): your hand after it is your later decision.
    /// </summary>
    private static bool HandTakeChosenOf(CardModel card, int handCount)
    {
        var chosen = Programs.CardEffects.TakesIntoHandOf(card.GetType()).Where(take => take.ChoosesHowMany).ToList();
        if (chosen.Count == 0 || handCount - 1 >= CardPile.MaxCardsInHand) return false;
        return Taken(card, chosen, new List<CardModel>(), DrawPileOf(card), draws: 0, plays: 1, fromHandOnly: false, pickEachPlay: true) > 0;
    }

    /// <summary>Cards in your draw pile; 0 for the game's template card.</summary>
    private static int DrawPileOf(CardModel card) => OwnerOf(card) is { } me ? Sts2State.DrawPileCount(me) : 0;

    /// <summary>
    /// How many cards the play's <paramref name="takes"/> take on the board, each whose condition holds:
    /// <list type="bullet">
    /// <item>a pick: the selector's most (its first card: one) of the cards in its pile that its filter and its test on the card let
    /// through, your hand with the draws its code makes before it, a draw pile with the discard pile its play shuffles in first; on each
    /// play when <paramref name="pickEachPlay"/>;</item>
    /// <item>a roll: its count (one, or TakeRandom's) a play, of its collection;</item>
    /// <item>each card of a collection once (a later play finds them gone), one for its first card;</item>
    /// <item>the cards the play drew (<paramref name="draws"/>), and the cards looked at on top of your draw pile less the ones picked, a play.</item>
    /// </list>
    /// <paramref name="fromHandOnly"/>: only the cards that leave your hand. <paramref name="eligible"/>: the cards the command itself can
    /// take. A collection that can't be told on the board (the game's template card) is <paramref name="others"/>.
    /// </summary>
    private static int Taken(CardModel card, IReadOnlyList<Programs.CardEffects.CardsTaken> takes, List<CardModel> others, int drawPile, int draws, int plays,
        bool fromHandOnly, bool pickEachPlay, Func<CardModel, bool>? eligible = null)
    {
        if (takes.Count == 0) return 0;
        var playCount = Math.Max(1, plays);
        var program = Programs.CardPrograms.Of(card.GetType());
        var board = new Programs.ProgramBoard(card);
        bool Eligible(object? item) => eligible == null || item is not CardModel cardItem || eligible(cardItem);
        int Number(Programs.ProgramValue? value, int fallback) => value != null && Programs.ProgramEvaluator.Number(value, board, program) is { } read ? (int)read : fallback;
        var total = 0;
        foreach (var take in takes)
        {
            if (Programs.ProgramEvaluator.Holds(take.PlayTest, board, program) == false) continue;
            switch (take.Way)
            {
                case Programs.CardEffects.TakeWay.Picked:
                {
                    var pile = take.PickPile ?? PileType.Hand;
                    if (fromHandOnly && pile != PileType.Hand) break;
                    var pool = pile == PileType.Hand
                        ? others.Count(other => Eligible(other) && Programs.ProgramEvaluator.HoldsFor(take.ItemTest, other, board, program) != false) + DrawsBefore(card, program, take, draws)
                        : PileCount(card, pile, take, board, program, Eligible, otherwise: pile == PileType.Draw ? drawPile : others.Count);
                    total += Math.Min(pool, Number(take.PerPick, fallback: 1) * (pickEachPlay ? playCount : 1));
                    break;
                }
                case Programs.CardEffects.TakeWay.Random:
                    total += Math.Min(Number(take.RollCount, fallback: 1) * playCount, Collection(take, others, board, program, fromHandOnly, Eligible));
                    break;
                case Programs.CardEffects.TakeWay.Drawn:
                    if (!fromHandOnly) total += draws;
                    break;
                case Programs.CardEffects.TakeWay.LookedAt:
                    if (!fromHandOnly) total += Math.Max(0, LooksAt(card) - Number(take.PerPick, fallback: 1)) * playCount;
                    break;
                default:
                    var each = Collection(take, others, board, program, fromHandOnly, Eligible);
                    total += take.Single ? Math.Min(1, each) : each;
                    break;
            }
        }
        return total;
    }

    /// <summary>The cards a pick from your hand also has to pick from: the ones the play draws before it (Acrobatics), when it draws first.</summary>
    private static int DrawsBefore(CardModel card, Programs.CardProgram program, Programs.CardEffects.CardsTaken take, int draws)
    {
        if (draws <= 0 || take.Pick == null) return 0;
        foreach (var step in program.All())
        {
            if (ReferenceEquals(step, take.Pick)) return 0;
            if (step is Programs.CommandStep command && Programs.CardEffects.DrawsForYou(card.GetType()).Any(draw => ReferenceEquals(draw.Command, command))) return draws;
        }
        return 0;
    }

    /// <summary>
    /// The cards of yours in <paramref name="pile"/> a pick can take: its filter's and its test's, read on the board; a draw pile with the
    /// discard pile the play shuffles in first when it's short (Ki Chant). <paramref name="otherwise"/> when the pile can't be read.
    /// </summary>
    private static int PileCount(CardModel card, PileType pile, Programs.CardEffects.CardsTaken take, Programs.ProgramBoard board, Programs.CardProgram program,
        Func<object?, bool> eligible, int otherwise)
    {
        if (OwnerOf(card) is not { } me) return otherwise;
        var cards = PileCards(me, pile);
        if (pile == PileType.Draw && ShufflesFirst(card)) cards.AddRange(PileCards(me, PileType.Discard));
        return cards.Count(other => eligible(other)
                                    && (take.PickFilter is not { } filter || Programs.ProgramEvaluator.Passes(filter, other, board, program) != false)
                                    && Programs.ProgramEvaluator.HoldsFor(take.ItemTest, other, board, program) != false);
    }

    /// <summary>
    /// How many cards the take's collection holds on the board (only your hand's, when asked), those its test on each card lets through;
    /// <paramref name="others"/> when it can't be told.
    /// </summary>
    private static int Collection(Programs.CardEffects.CardsTaken take, List<CardModel> others, Programs.ProgramBoard board, Programs.CardProgram program, bool fromHandOnly,
        Func<object?, bool> eligible)
    {
        if (take.Items == null || Programs.ProgramEvaluator.Items(take.Items, board, program) is not { } items) return others.Count;
        return items.Count(item => !ReferenceEquals(item, board.Card) && (!fromHandOnly || item is CardModel { Pile.Type: PileType.Hand }) && eligible(item)
                                   && (take.ItemTest.IsAlways || Programs.ProgramEvaluator.HoldsFor(take.ItemTest, item, board, program) != false));
    }

    /// <summary>
    /// The top cards of your draw pile the play looks at (a LookedAt take's count: Take(drawPile, Cards), Ki Chant), as many as the pile
    /// holds after the shuffle its play makes first when it's short.
    /// </summary>
    private static int LooksAt(CardModel card)
    {
        var take = Programs.CardEffects.Discards(card.GetType()).FirstOrDefault(discard => discard.Way == Programs.CardEffects.TakeWay.LookedAt);
        var count = take?.Looks is { } looks && Programs.ProgramEvaluator.Number(looks, new Programs.ProgramBoard(card), Programs.CardPrograms.Of(card.GetType())) is { } read
            ? (int)read : Sts2State.Var(card, "Cards", fallback: 3);
        if (OwnerOf(card) is not { } me) return count; // the game's template card (the scan)
        var available = Sts2State.DrawPileCount(me) + (ShufflesFirst(card) ? Sts2State.DiscardPileCount(me) : 0);
        return Math.Min(count, available);
    }
    /// <summary>
    /// How a card that exhausts others takes them (ExhaustFromHandAction: what a card that acts when exhausted, Drum of Battle, does
    /// then), read from its program: a pick is Chosen (from your draw pile for Cleanse), a roll Random, a loop over a collection All;
    /// a filter by type (Second Wind's non-Attacks, Thrash's Attacks, The Hermit's Curse) is its Only or Except. A filter that isn't a
    /// type (Growth's Sadness, Florescence's Shoot cards, Flak Cannon's statuses outside the exhaust pile) takes no card in your hand
    /// that acts when exhausted unless one passes it, evaluated on the board: then none is followed. Null for a card that exhausts no
    /// other card.
    /// </summary>
    internal static ExhaustFromHandAction? ExhaustPickOf(CardModel card)
    {
        var program = Programs.CardPrograms.Of(card.GetType());
        var board = new Programs.ProgramBoard(card);
        foreach (var exhaust in Programs.CardEffects.ExhaustsOfOthers(card.GetType()))
        {
            if (Programs.ProgramEvaluator.Holds(exhaust.PlayTest, board, program) == false) continue;
            var pick = exhaust.Way switch
            {
                Programs.CardEffects.TakeWay.Picked => ExhaustPick.Chosen,
                Programs.CardEffects.TakeWay.Random => ExhaustPick.Random,
                _ => ExhaustPick.All,
            };
            var filter = exhaust.Way == Programs.CardEffects.TakeWay.Picked ? exhaust.ItemTest : exhaust.Filter?.Body ?? Programs.ProgramCondition.Always;
            if (filter.IsAlways) return new ExhaustFromHandAction(pick) { FromDrawPile = exhaust.FromDrawPile };
            if (TypeTest(filter) is var (type, holds))
                return holds ? new ExhaustFromHandAction(pick) { Only = type, FromDrawPile = exhaust.FromDrawPile } : new ExhaustFromHandAction(pick) { Except = type, FromDrawPile = exhaust.FromDrawPile };
            return null;
        }
        return null;
    }

    /// <summary>
    /// The card exhausts others with a filter that isn't a type (ExhaustPickOf: none followed) and one of <paramref name="candidates"/>
    /// that acts when exhausted (CardEnergy.WhenExhausted: Drum of Battle) passes it, or might: the card scan's check that such a filter
    /// takes no card whose exhaust does something.
    /// </summary>
    internal static bool ExhaustFilterTakesActingCard(CardModel card, IEnumerable<CardModel> candidates)
    {
        var program = Programs.CardPrograms.Of(card.GetType());
        var board = new Programs.ProgramBoard(card);
        var acting = candidates.Where(candidate => SpireQoL.Sim.CardEnergy.WhenExhausted(candidate) > 0).ToList();
        foreach (var exhaust in Programs.CardEffects.ExhaustsOfOthers(card.GetType()))
        {
            var filter = exhaust.Way == Programs.CardEffects.TakeWay.Picked ? exhaust.ItemTest : exhaust.Filter?.Body ?? Programs.ProgramCondition.Always;
            if (filter.IsAlways || TypeTest(filter) != null) continue;
            if (exhaust.Way == Programs.CardEffects.TakeWay.Picked
                    ? acting.Any(candidate => Programs.ProgramEvaluator.HoldsFor(filter, candidate, board, program) != false)
                    : acting.Any(candidate => Programs.ProgramEvaluator.Passes(exhaust.Filter!, candidate, board, program) != false))
                return true;
        }
        return false;
    }

    /// <summary>A filter that's one test of a card's type (c.Type == Attack, each.Type != Curse): the type, and whether it's kept or left.</summary>
    private static (SimCardType Type, bool Kept)? TypeTest(Programs.ProgramCondition filter)
    {
        if (filter.Terms is not [[{ Test: Programs.CompareValue { Op: Programs.Comparison.Equal or Programs.Comparison.NotEqual, Left: Programs.QueryValue { Name: "Type" }, Right: Programs.ConstantValue constant } compare } literal]])
            return null;
        if (!Enum.TryParse<SimCardType>(((CardType)constant.Value).ToString(), out var type) || type == SimCardType.None) return null;
        return (type, (compare.Op == Programs.Comparison.Equal) == literal.Holds);
    }

    /// <summary>
    /// Cards that hit their target, then deal that hit's damage (overkill included: DamageResult.TotalDamage + OverkillDamage) to
    /// every other hittable enemy, with only the damage caps applied (ModifyDamageHookType.Cap): RainShot.OnPlay, Omnislice.OnPlay.
    /// The card scan holds every card reading a hit's overkill to this list or OverkillReaders.
    /// </summary>
    internal static readonly HashSet<string> SplashCards = new() { nameof(RainShot), nameof(Omnislice) };

    /// <summary>Cards that read a hit's overkill for something else, and what.</summary>
    internal static readonly Dictionary<string, string> OverkillReaders = new()
    {
        [nameof(Fisticuffs)] = "block equal to the damage it dealt (SimAdapter: Fisticuffs)",
        [nameof(BlossomShot)] = "Stretch equal to the damage it dealt (AttackAction.StretchFromDamage): for block later in the turn (I Am Invincible at its end)",
    };

    private static List<CardModel> PileCards(Player me, PileType pile)
    {
        try { return pile.GetPile(me).Cards.ToList(); } catch { return new List<CardModel>(); }
    }

    /// <summary>
    /// Cards of yours, other than this one, its play takes out of your hand: what it discards from there (not the cards it looked at on
    /// your draw pile, Ki Chant's; nor the ones it drew, Scrape's, the simulator's) and what it exhausts from there (not Cleanse's, from
    /// the draw pile; of Florescence's and Flak Cannon's, from every pile, the ones in your hand).
    /// </summary>
    internal static int LeavingHand(CardModel card, List<CardModel> others, int drawPile, int draws, int plays)
    {
        var exhausts = ExhaustedFromHand(card, others, drawPile, plays);
        var discards = DiscardedFromHand(card, others, draws);
        // Put back on your draw pile after the draws, from the cards in hand then (you choose which).
        var putBack = Programs.CardEffects.PutsBackFromHand(card.GetType())
            ? Math.Min(card.GetType().Name == nameof(ThinkingAhead) ? 1 : Sts2State.Var(card, "PutBack", fallback: 1), others.Count + draws) * Math.Max(1, plays) : 0;
        return exhausts + discards + putBack;
    }

    // ------------------------------------------------------------------ block

    /// <summary>
    /// Separate times one play gains you block, read from its program (decision #182: each CreatureCmd.GainBlock, CardEffects.Find), on
    /// the board: each gain on its condition (Mad Science's rider), times its loops' rounds (Second Wind your non-Attacks, Cho Limbs 2,
    /// Evil Eye twice after an exhaust this turn, Death's Door one more Repeat after Doom, Cho Firm twice with an arrow to shoot); a loop
    /// over every player (Rally) gains yours once. None when the game says the card gains no block now (CardModel.GainsBlock: Sovereign
    /// Blade without Parry, Mad Science not a Skill, a Power whose Block is for later); one when it does and its program shows no gain.
    /// The game's template card (no owner: the coverage scan) counts every gain its code makes.
    /// </summary>
    internal static int BlockGainCount(CardModel card)
    {
        if (OwnerOf(card) != null && !card.GainsBlock) return 0;
        var type = card.GetType();
        var program = Programs.CardPrograms.Of(type);
        var board = new Programs.ProgramBoard(card);
        var gains = Programs.CardEffects.BlockGains(type);
        if (gains.Count == 0) return OwnerOf(card) != null ? 1 : 0;
        var count = 0;
        foreach (var gain in gains)
        {
            if (Programs.ProgramEvaluator.Holds(gain.Condition, board, program) == false) continue;
            // The loop over the players it gains each one's block in: yours once.
            count += gain.Loops.Aggregate(1, (times, loop) => times * (Programs.CardEffects.GainsForEachOfLoop(gain.Command) ? 1 : Rounds(loop, board, program)));
        }
        return count;
    }

    /// <summary>
    /// Hits per play of an attack card, read from its program (decision #182): its attack (the play's first DamageCmd.Attack) on its
    /// condition (Pact's End's exhausts, Nail's Impaled target, Fetch's and Flatten's Osty, Mad Science's type), its hit count's value
    /// (WithHitCount's argument: a card value, a constant on main, a card's X, Heavenly Drill's doubled X, Dismantle's Vulnerable target,
    /// Spite's HP lost this turn, Wah Blast's melodies, Fiend Fire's hand, a calculated count; one with none) times its loops' rounds,
    /// then the game's hit-count modifiers. With no target (null) a test of the target holds when it does for some enemy you could aim
    /// at, and a count that asks the target is its least.
    /// </summary>
    internal static int HitsPerPlay(CardModel card, ICombatState combat, Creature? target)
    {
        var hits = BaseHitsPerPlay(card, target);
        // Then the game's hit-count modifiers (Thumbscrew: +1 on your multi-hit attacks), the way its card
        // preview applies them (HitCountVar.GetPreviewValue; the attack itself in AttackCommand.Execute).
        if (hits <= 0 || card.Type != CardType.Attack) return hits;
        try
        {
            return Math.Max(0, (int)MegaCrit.Sts2.Core.Hooks.Hook.ModifyAttackHitCount(combat,
                MegaCrit.Sts2.Core.Commands.DamageCmd.Attack(0m).FromCard(card, null), hits));
        }
        catch
        {
            return hits;
        }
    }

    private static int BaseHitsPerPlay(CardModel card, Creature? target)
    {
        // A Shoot card with no arrow to shoot does nothing at all (ArrowCmd.ShootArrowAtTarget returns before its attack; the card
        // glows red).
        if (Fool.IsShot(card) && OwnerOf(card) is { } owner && !Fool.Shoots(card, owner)) return 0;
        var program = Programs.CardPrograms.Of(card.GetType());
        if (Programs.CardEffects.Attacks(card.GetType()) is not [var attack, ..]) return Math.Max(1, Sts2State.Var(card, "Repeat", fallback: 1));
        var board = new Programs.ProgramBoard(card, target);
        if (target == null && Programs.ProgramEvaluator.AsksTarget(attack.Condition))
        {
            if (Targets(card) is { } enemies && !enemies.Any(enemy => Programs.ProgramEvaluator.Holds(attack.Condition, board with { Target = enemy }, program) != false)) return 0;
        }
        else if (Programs.ProgramEvaluator.Holds(attack.Condition, board, program) == false) return 0;
        var hits = attack.Command.HitCountValue is not { } count ? 1
            : Programs.ProgramEvaluator.Number(count, board, program) is { } read ? (int)read
            : Least(count, board, program) ?? Math.Max(1, Sts2State.Var(card, "HitCount", Sts2State.Var(card, "Repeat", fallback: 1)));
        return Math.Max(0, hits * attack.Loops.Aggregate(1, (times, loop) => times * Rounds(loop, board, program)));
    }

    /// <summary>The least a value two ways leave can be (Dismantle with no target: one hit), when each way's value can be told.</summary>
    private static int? Least(Programs.ProgramValue value, Programs.ProgramBoard board, Programs.CardProgram program)
    {
        while (value is Programs.StoredValue stored) value = stored.Value;
        if (value is not Programs.WaysValue ways) return null;
        var each = ways.Ways.Select(way => Programs.ProgramEvaluator.Number(way.Value, board, program)).ToList();
        return each.All(number => number != null) && each.Count > 0 ? (int)each.Min()!.Value : null;
    }

    private static int XValue(ICombatState combat, CardModel card, int spent)
    {
        try { return Math.Max(0, MegaCrit.Sts2.Core.Hooks.Hook.ModifyXValue(combat, card, spent)); } catch { return Math.Max(0, spent); }
    }

    /// <summary>An attack card aimed at enemies (Osty's attacks use OstyDamage or CalculatedDamage).</summary>
    private static bool IsAttackOnEnemies(CardModel card)
        => card.Type == CardType.Attack && card.TargetType is TargetType.AnyEnemy or TargetType.AllEnemies or TargetType.RandomEnemy;

    private static bool IsPlayerAttack(CardModel card)
        => card.Type == CardType.Attack
           && (card.DynamicVars.ContainsKey("Damage") || card.DynamicVars.ContainsKey("CalculatedDamage"))
           && card.TargetType is TargetType.AnyEnemy or TargetType.AllEnemies or TargetType.RandomEnemy;

    // ------------------------------------------------------------------ orbs, stars, Osty

    /// <summary>Orbs one play channels (Metronome), read from its program (OrbsOf).</summary>
    internal static int ChannelCount(CardModel card) => OrbsOf(card).Channels.Sum(channel => channel.Times);

    /// <summary>
    /// What one play does to your orbs, read from its program (decision #182: CardEffects.OrbChannels, OrbEvokes, OrbSlotChanges) and
    /// worked out on the board: the slots it adds or removes (their count argument: Capacitor's Repeat, Bulk Up's OrbSlots), how many
    /// times it evokes your front orb (each OrbCmd.EvokeNext times its loops' rounds: Dualcast twice, Quadcast its Repeat, Multicast its
    /// X, one more upgraded) or every orb (a loop over your orbs: Shatter twice each), and each channel, in order, with its orb (its
    /// class, or random: Chaos) and how many times (its loops' rounds: Glacier 2, Chill your enemies, Tempest its X, Voltaic its count),
    /// on its condition (Spinner+ only). An evoke with no orb to evoke does nothing (as OrbCmd.EvokeNext), so its test isn't asked.
    /// </summary>
    internal static OrbPlan OrbsOf(CardModel card)
    {
        var type = card.GetType();
        var program = Programs.CardPrograms.Of(type);
        var board = new Programs.ProgramBoard(card);
        int Number(Programs.ProgramValue value, int fallback) => Programs.ProgramEvaluator.Number(value, board, program) is { } read ? (int)read : fallback;
        int Times(Programs.CardEffects.Found found) => found.Loops.Aggregate(1, (times, loop) => times * Rounds(loop, board, program));
        var slots = 0;
        foreach (var change in Programs.CardEffects.OrbSlotChanges(type))
            if (Programs.ProgramEvaluator.Holds(change.Condition, board, program) != false && change.Command.Values.Count > 1)
                slots += (change.Command.Command.Name == "RemoveSlots" ? -1 : 1) * Number(change.Command.Values[1], fallback: 1) * Times(change);
        var eachOrb = Programs.CardEffects.EvokesPerOrbOfAll(type);
        var front = eachOrb > 0 ? 0 : Programs.CardEffects.OrbEvokes(type).Sum(Times);
        var channels = new List<(SpireQoL.Sim.OrbKind Kind, int Times)>();
        foreach (var channel in Programs.CardEffects.OrbChannels(type))
        {
            if (Programs.CardEffects.OrbOf(channel.Command) is not { } kind || Programs.ProgramEvaluator.Holds(channel.Condition, board, program) == false) continue;
            var times = Times(channel);
            if (times > 0) channels.Add((kind, times));
        }
        return new OrbPlan(slots, front, eachOrb, channels);
    }

    /// <summary>One play's orbs (OrbsOf).</summary>
    internal sealed record OrbPlan(int Slots, int EvokesFront, int EvokesEachOrb, IReadOnlyList<(SpireQoL.Sim.OrbKind Kind, int Times)> Channels)
    {
        /// <summary>The kind every channel makes, when it's one kind (null: mixed, random or none).</summary>
        public SpireQoL.Sim.OrbKind? ChannelKind => Channels.Select(channel => channel.Kind).Distinct().ToList() is [var only] && only != SpireQoL.Sim.OrbKind.Random ? only : null;
    }

    /// <summary>
    /// How many rounds a loop of the play goes on the board: its foreach's collection (Chill: the enemies you can hit), its counter's
    /// bound (i &lt; Repeat, i &lt; 2, i &lt; your X), or the card value it's over; one when none can be told (the game's template card).
    /// </summary>
    internal static int Rounds(Programs.LoopStep loop, Programs.ProgramBoard board, Programs.CardProgram program)
    {
        if (loop.Items != null) return Programs.ProgramEvaluator.Items(loop.Items, board, program) is { } items ? items.Count : 1;
        if (loop.While is { Terms: [[{ Holds: true, Test: Programs.CompareValue { Op: Programs.Comparison.Less or Programs.Comparison.LessOrEqual } bound }]] }
            && Programs.ProgramEvaluator.Number(bound.Right, board, program) is { } most)
            return Math.Max(0, (int)most + (bound.Op == Programs.Comparison.LessOrEqual ? 1 : 0));
        return board.Card.DynamicVars.ContainsKey(loop.Over) ? Sts2State.Var(board.Card, loop.Over) : 1;
    }

    /// <summary>
    /// The star gains of one play of the card: each PlayerCmd.GainStars in its play, on every way through it, to its owner, read from
    /// its own code (CardEnergy.CallsOf). A gain that waits on its attack's kill is the attack's instead (CardEffects.GainsStarsOnKill: AttackAction.KillRewards).
    /// </summary>
    internal static int StarGainsOf(CardModel card) =>
        SpireQoL.Sim.CardEnergy.CallsOf(card.GetType(), "GainStars").Count(call => call is { Method: "OnPlay", Certain: true, ToOwner: true });

    /// <summary>
    /// A card that makes Osty attack: the game's own tag on it (CardTag.OstyAttack, on every card whose OnPlay attacks FromOsty). A list
    /// of names missed Fetch, so its hit, Bone Flute's block and the enemy HP after it weren't predicted (a dev-mode report, 1.21.2).
    /// </summary>
    internal static bool IsOstyAttack(CardModel card)
    {
        try { return card.Tags.Contains(MegaCrit.Sts2.Core.Entities.Cards.CardTag.OstyAttack); }
        catch { return false; }
    }

    // ------------------------------------------------------------------ generated cards

    // A card that makes Sadness (CardEffects.MakesSadness, from its program): its created statuses are all Sadness (GeneratedCount),
    // so that count is the Sadness made.

    internal static int GeneratedCount(CardModel card, string name, List<CardModel> others, Player me, out int statuses)
    {
        statuses = 0;
        int Var(string key, int fallback = 1) => Sts2State.Var(card, key, fallback);
        switch (name)
        {
            // Statuses.
            case nameof(BoostAway): case nameof(CollisionCourse): case nameof(GunkUp): case nameof(Overclock): case nameof(Turbo):
                statuses = 1; return 1;
            case nameof(FightThrough):
                statuses = 2; return 2;
            case nameof(CrashLanding):
            {
                var room = Math.Max(0, CardPile.MaxCardsInHand - others.Count);
                statuses = room; return room;
            }
            // Shivs.
            case nameof(BladeDance): case nameof(BladeOfInk): case nameof(CloakAndDagger): case nameof(UpMySleeve): return Var("Cards");
            case nameof(FanOfKnives): case nameof(HiddenDaggers): return Var("Shivs");
            case nameof(LeadingStrike): return Var("Shivs");
            case nameof(StormOfSteel): return others.Count;
            // Souls.
            case nameof(CaptureSpirit): case nameof(GraveWarden): case nameof(Reave): return Var("Cards");
            // Souls into every living teammate's draw pile, created by you (GlimpseBeyond.OnPlay: AddGeneratedCardsToCombat with
            // creator = you): your created-card procs, once per Soul. The template card (no owner) counts one teammate.
            case nameof(GlimpseBeyond):
                return Var("Cards") * (me == null ? 1 : me.Creature?.CombatState?.Players.Count(player => player != me && player.Creature is { IsAlive: true }) ?? 0);
            case nameof(Severance): return 3;
            case nameof(Dirge): return me?.PlayerCombatState?.Energy ?? 1; // X souls
            // Other cards.
            case nameof(BundleOfJoy): case nameof(JackOfAllTrades): case nameof(Jackpot): case nameof(Metamorphosis): case nameof(DualWield): return Var("Cards");
            case nameof(HeirloomHammer): return Var("Repeat");
            case nameof(Stoke): return others.Count;
            case nameof(Abundance): case nameof(AdaptiveStrike): case nameof(Anger): case nameof(Discovery): case nameof(Distraction):
            case nameof(InfernalBlade): case nameof(Largesse): case nameof(ManifestAuthority): case nameof(Outrage): case nameof(Quasar):
            case nameof(Splash): case nameof(Undeath): case nameof(WhiteNoise):
                return 1;
            // v0.113.0 (names as strings): the Fool's Sadness (a status) and Joy, The Shot, tarot and event cards.
            case nameof(BlotOutTheSky): case nameof(Florescence): return 1;
            case nameof(MourningBlow): case nameof(Vitriol): case nameof(Rummage): statuses = 1; return 1; // Mourning Blow turns into a Sadness
            case nameof(Jest): case nameof(SorrowfulStrike): statuses = Var("Cards"); return statuses;
            case nameof(BrightenUp): statuses = Var("Sadness", fallback: 2); return statuses + Var("Joy", fallback: 2);
            case nameof(SelfDeprecate):
            {
                // Sadness for you, Joy for every other living player (created by you).
                var mates = me?.Creature?.CombatState?.Players.Count(player => player != me && player.Creature is { IsAlive: true }) ?? 0;
                statuses = Var("Sadness", fallback: 1);
                return statuses + Var("Joy", fallback: 2) * mates;
            }
            case nameof(TheHierophant): return Var("Cards", fallback: 3);
            case nameof(TheLovers): return Math.Max(0, CardPile.MaxCardsInHand - others.Count);
            // Shivs until your hand is full (TheMoon.OnPlay: MaxCardsInHand - hand, once it has left your hand).
            case nameof(TheMoon): return Math.Max(0, CardPile.MaxCardsInHand - others.Count);
            case nameof(TheSun): statuses = 1; return 1; // a Soot into your discard pile
            case nameof(MightyShield): case nameof(MightyWeapon): statuses = Var("Cards"); return statuses; // Dazed into your draw pile
        }
        return 0;
    }

    /// <summary>
    /// A card that forges (CardEffects.Forges: ForgeCmd.Forge in its program). Forging creates a Sovereign Blade in your hand when you have none outside your exhaust pile
    /// (ForgeCmd.Forge, through AddGeneratedCardToCombat); later forges only add damage. Once per play
    /// chain: a replay finds the blade the first play made.
    /// </summary>
    private static int ForgedBlade(Type cardType, Player me)
    {
        if (!Programs.CardEffects.Forges(cardType)) return 0;
        try
        {
            var hasBlade = me.PlayerCombatState?.AllCards.Any(card => card is SovereignBlade && !card.IsDupe && card.Pile?.Type != PileType.Exhaust) ?? true;
            return hasBlade ? 0 : 1;
        }
        catch
        {
            return 0;
        }
    }

    // ------------------------------------------------------------------ debuffs

    private static readonly Dictionary<Type, bool> DebuffCache = new();

    private static IReadOnlyList<Type> EnemyDebuffTypes(CardModel card, bool temporary = false)
    {
        if (card.TargetType is not (TargetType.AnyEnemy or TargetType.AllEnemies or TargetType.RandomEnemy)) return Array.Empty<Type>();
        List<Type>? list = null;
        foreach (var dynamicVar in card.DynamicVars.Values)
        {
            var type = dynamicVar.GetType();
            if (!type.IsGenericType || type.GetGenericTypeDefinition() != typeof(PowerVar<>)) continue;
            if (dynamicVar.BaseValue <= 0) continue;
            var powerType = type.GetGenericArguments()[0];
            if (!(temporary ? IsTemporaryDebuff(powerType) : IsDebuff(powerType))) continue;
            (list ??= new()).Add(powerType);
        }
        // Debuffs its code applies with a plain value (Uppercut, Putrefy, Shockwave use "Power").
        foreach (var applied in Diagnostics.CardCoverage.PowersApplied(card.GetType()))
            if ((temporary ? IsTemporaryDebuff(applied) : IsDebuff(applied)) && (list == null || !list.Contains(applied))) (list ??= new()).Add(applied);
        return list ?? (IReadOnlyList<Type>)Array.Empty<Type>();
    }

    public static bool IsDebuff(Type powerType) => !typeof(ITemporaryPower).IsAssignableFrom(powerType) && IsDebuffType(powerType);

    public static bool IsTemporaryDebuff(Type powerType) => typeof(ITemporaryPower).IsAssignableFrom(powerType) && IsDebuffType(powerType);

    /// <summary>
    /// The power is a debuff by type (PowerModel.Type), temporary ones included (Dark Shackles):
    /// what Artifact blocks (ArtifactPower.TryModifyPowerAmountReceived).
    /// </summary>
    public static bool IsDebuffType(Type powerType)
    {
        if (DebuffCache.TryGetValue(powerType, out var isDebuff)) return isDebuff;
        isDebuff = false;
        try
        {
            var canonical = typeof(ModelDb).GetMethod(nameof(ModelDb.Power))!.MakeGenericMethod(powerType).Invoke(null, null) as PowerModel;
            isDebuff = canonical?.Type == PowerType.Debuff;
        }
        catch
        {
            // unknown: not a debuff
        }
        DebuffCache[powerType] = isDebuff;
        return isDebuff;
    }

    /// <summary>
    /// Debuffs landing across the card's targets: summed over every enemy for all-enemy cards; for a
    /// single target, the best case over the enemies you could pick.
    /// </summary>
    public int DebuffsLandingTotal(Func<Type, bool>? filter = null)
    {
        var enemies = Combat.HittableEnemies;
        if (enemies.Count == 0) return 0;
        var perPlay = filter == null ? EnemyDebuffs.Count : EnemyDebuffs.Count(filter);
        if (perPlay == 0) return 0;
        int Landing(Creature creature)
        {
            var total = perPlay * Plays;
            var artifact = creature.Powers.OfType<ArtifactPower>().Sum(artifactPower => artifactPower.Amount);
            return Math.Max(0, total - artifact);
        }
        return HitsAllEnemies ? enemies.Sum(Landing) : enemies.Max(Landing);
    }
}

/// <summary>The outcome simulator's inputs for one card on one frame (see PlayFacts.Sim).</summary>
public sealed class SimInputs
{
    public required SimState Start { get; init; }
    /// <summary>
    /// The same board with teammates' hands (decision #25: the checks use everything that isn't a roll), when Start leaves them
    /// out (Count teammates' hands off); null when they're the same.
    /// </summary>
    public SimState? FullStart { get; init; }

    /// <summary>
    /// Whether a teammate's hand changes the play itself (its branches, before your end of turn): then the tabs, worked out
    /// without it, can't be held to what happened, and the play's checks that rest on them aren't reported (counted instead).
    /// </summary>
    public bool HandChangesThePlay(SimPlay play)
    {
        if (FullStart == null) return false;
        SimEngine.ApplyPlayTo(Start, play, out var shown, out var shownCapped);
        SimEngine.ApplyPlayTo(FullStart, play, out var full, out var fullCapped);
        if (shownCapped || fullCapped) return true;
        // The shown run's teammates have their hand cleared: compare everything else.
        static IEnumerable<long> Keys(IEnumerable<SimState> states) => states.Select(branch => branch.WithoutTeammateHands().Key()).Distinct().OrderBy(key => key);
        return !Keys(shown).SequenceEqual(Keys(full));
    }
    public required IReadOnlyDictionary<Creature, int> Ids { get; init; }
    /// <summary>The teammates behind Start.Teammates, in order: a play aimed at one is keyed Who.Teammate(index).</summary>
    public IReadOnlyList<Creature> Allies { get; init; } = Array.Empty<Creature>();

    /// <summary>The simulation key for a play aimed at this creature: an enemy id, a teammate (Who.Teammate), or no one (Who.None).</summary>
    private int KeyFor(Creature? target) =>
        target == null ? Who.None
        : Ids.TryGetValue(target, out var id) ? id
        : Allies.ToList().IndexOf(target) is var teammateIndex and >= 0 ? Who.Teammate(teammateIndex) : Who.None;
    /// <summary>Each aim's play (an enemy id, a teammate, or Who.None for a card simulated once): looked up through AimFor.</summary>
    public Dictionary<int, SimPlay> PlayByTarget { get; } = new();

    /// <summary>
    /// The aim a play at this target was simulated under: its own, or no one's (a card simulated once, aimed at no one); null when
    /// neither was simulated. The one lookup of a played card's aim (decision #142): every check reads its play and outcomes by it.
    /// </summary>
    public int? AimFor(Creature? target) =>
        KeyFor(target) is var key && PlayByTarget.ContainsKey(key) ? key : PlayByTarget.ContainsKey(Who.None) ? Who.None : null;
}
