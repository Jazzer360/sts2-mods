using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
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

    /// <summary>Cards the card itself draws, across plays.</summary>
    public int OwnDraws { get; init; }

    /// <summary>
    /// The most it can draw, when that depends on what it draws (Pillage draws again while it draws an Attack: up to a full hand;
    /// decision #118). Otherwise 0: the play check's upper bound on draws is its simulation's.
    /// </summary>
    public int DrawsAtMost { get; init; }

    /// <summary>
    /// A card that acts on what it draws (decision #119): each tally its draw can give (SimState.DrawnTally) and its share of the
    /// outcomes, from the simulation its chance tab came from; null otherwise. The play check holds what happened to it.
    /// </summary>
    public SortedDictionary<int, double>? Drawn { get; set; }

    /// <summary>Of those, the card's enchantment's (Swift): drawn after the card's own play.</summary>
    public int EnchantmentDraws { get; init; }

    /// <summary>Every draw is blocked this turn (No Draw, Fiddle).</summary>
    public bool DrawsBlocked { get; init; }

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
    public int TransformsCertain => Programs.BoardCounts.TransformsChosen(Card) ? 0 : Transforms;
    /// <summary>
    /// Cards outside your hand this play brings back (Make It So, Right Hand Hand, Whistling Arrows: ReturnToHand), in your
    /// hand after it; their tabs.
    /// </summary>
    public IReadOnlyList<CardModel> CardsBack { get; init; } = Array.Empty<CardModel>();
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


    /// <summary>Hits on this enemy across plays (Dismantle hits Vulnerable enemies twice).</summary>
    public int HitsOn(Creature enemy)
        => AttackHits == 0 ? 0 : Programs.BoardCounts.HitsPerPlay(Card, Combat, enemy);

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
            DrawsBlocked = DrawsBlocked || next.DrawsBlocked,
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
        var draws = Programs.BoardCounts.CardsDrawn(card, hand.Count);
        // Swift (enchantment): draws its amount when the card is played, once (Swift.AfterCardPlayed disables it).
        var swiftDraws = SpireQoL.Sim.SimAdapter.SwiftDraws(card);
        var drawPile = Sts2State.DrawPileCount(me);
        var evoked = EvokedOrbs(card, me);
        var orbs = OrbDamageFor(evoked, me, combat);
        // The cards it makes, read from its program's makes (MadeCards), the ones for each player of your side that you make included.
        var made = Programs.MadeCards.Summary(card);
        // Cards the play puts into your hand (Bundle of Joy's four): they fill it before anything comes back.
        var createdInHand = made.IntoYourHand;

        var facts = new PlayFacts
        {
            Card = card,
            Me = me,
            Combat = combat,
            Plays = Math.Max(1, plays),
            Energy = CardPlays.EnergyOf(card),
            AutoPlayed = CardPlays.ChoiceAutoPlayed(card),
            Stars = CardPlays.StarsOf(card),
            ExhaustsSelf = SpireQoL.Sim.SimAdapter.ExhaustsAfterPlay(card, me),
            ExhaustsOthers = Programs.BoardCounts.ExhaustOthers(card, others, drawPile),
            // A play that looks at your draw pile (Ki Chant) discards its share each play; the others depend on the hand.
            Discards = Programs.BoardCounts.DiscardCount(card, others, draws),
            LeavesHand = Programs.BoardCounts.LeavingHand(card, others, drawPile, draws),
            TakesWholeHand = Programs.CardEffects.TakesWholeHand(card.GetType()),
            // A discard of your hand without a pick (Storm of Steel, Calculated Gamble) with a Sly card there.
            MayDiscardSly = Programs.BoardCounts.DiscardsSlyWithoutPick(card),
            OwnDraws = draws + swiftDraws,
            DrawsAtMost = SpireQoL.Sim.SimAdapter.DrawnCardReaderOf(card.GetType()) is { DrawsAgain: true }
                ? Math.Max(draws, CardPile.MaxCardsInHand - (hand.Count - 1)) + swiftDraws : 0,
            EnchantmentDraws = swiftDraws,
            BlockGains = Programs.BoardCounts.BlockGainCount(card),
            Generated = made.Made,
            GeneratedInHand = createdInHand,
            GeneratedStatuses = made.Statuses + made.Sadness,
            SadnessCreated = made.Sadness,
            CardsBack = ReturnToHand.Back(card, me, plays),
            TakenToHand = Programs.BoardCounts.TakenToHandCount(card),
            HandTakeChosen = Programs.BoardCounts.HandTakeChosenOf(card, hand.Count),
            EnergyBack = SpireQoL.Sim.SimAdapter.SownEnergy(card),
            Transforms = Programs.BoardCounts.TransformCount(card, others, drawPile),
            // A shot with no arrow to shoot does nothing at all (its debuffs neither: Hobbling Shot).
            EnemyDebuffs = Fool.IsShot(card) && !Fool.Shoots(card, me) ? Array.Empty<Type>() : EnemyDebuffTypes(card),
            EnemyTemporaryDebuffs = Fool.IsShot(card) && !Fool.Shoots(card, me) ? Array.Empty<Type>() : EnemyDebuffTypes(card, temporary: true),
            HitsAllEnemies = card.TargetType == TargetType.AllEnemies,
            Channels = Programs.BoardCounts.ChannelCount(card),
            StarGains = Programs.BoardCounts.StarGainsOf(card),
            OstyAttacks = Programs.BoardCounts.IsOstyAttack(card) ? 1 : 0,
            LosesHp = card.DynamicVars.ContainsKey("HpLoss"),
            HpLoss = Sts2State.Var(card, "HpLoss"),
            ByOsty = Programs.BoardCounts.IsOstyAttack(card),
            AttackHits = (Programs.BoardCounts.IsOstyAttack(card) ? me.IsOstyAlive && Programs.BoardCounts.IsAttackOnEnemies(card) : Programs.BoardCounts.IsPlayerAttack(card)) ? Programs.BoardCounts.HitsPerPlay(card, combat, target: null) : 0,
            OrbDamage = orbs.Exact,
            OrbDamageMin = orbs.Min,
            OrbDamageMax = orbs.Max,
            LightningEvokes = evoked.Count(orb => orb is LightningOrb),
            OrbBlock = OrbBlockFor(evoked, me, combat),
            RandomOutcome = combat.HittableEnemies.Count(creature => creature.IsAlive) > 1
                            && (evoked.Any(orb => orb is LightningOrb) || card.TargetType == TargetType.RandomEnemy),
            // Every draw is stopped this turn (NoDrawPower.ShouldDraw, Fiddle's): what the play check excuses.
            DrawsBlocked = me.Creature.HasPower<NoDrawPower>() || me.Relics.Any(relic => relic is Fiddle),
        };
        return facts;
    }


    /// <summary>
    /// Existing orbs this play evokes, in order: evoke cards (Dualcast and Quadcast evoke the front
    /// orb several times, Multicast X times, Shatter every orb twice), and channelling into full
    /// slots (each channel past capacity evokes the front orb first; OrbCmd.Channel). Orbs the play
    /// channels and then evokes itself aren't counted (their type may be random).
    /// </summary>
    private static List<OrbModel> EvokedOrbs(CardModel card, Player me)
    {
        var result = new List<OrbModel>();
        var orbQueue = me.PlayerCombatState?.OrbQueue;
        if (orbQueue == null) return result;
        var queue = orbQueue.Orbs.Cast<OrbModel?>().ToList();
        var cap = orbQueue.Capacity;
        var plan = Programs.BoardCounts.OrbsOf(card);
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
        // One play (decision #189): a replay's later plays evoke what the simulator finds then.
        {
            // Its own evokes (read from its program: OrbsOf): every orb that many times (Shatter), or the front orb (Dualcast, Quadcast, Multicast).
            if (plan.EvokesEachOrb > 0)
                while (queue.Count > 0) EvokeFront(plan.EvokesEachOrb, remove: true);
            else if (plan.EvokesFront > 0) EvokeFront(plan.EvokesFront, remove: true);
            // Slots the card adds before it channels (The Magician: OrbCmd.AddSlots, up to 10).
            var added = plan.Slots;
            if (added > 0) cap = Math.Min(10, cap + added);
            for (var channelIndex = 0; channelIndex < Programs.BoardCounts.ChannelCount(card); channelIndex++)
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


    // ------------------------------------------------------------------ debuffs


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
            if (!(temporary ? SpireQoL.Sim.SimReaders.IsTemporaryDebuff(powerType) : SpireQoL.Sim.SimReaders.IsDebuff(powerType))) continue;
            (list ??= new()).Add(powerType);
        }
        // Debuffs its code applies with a plain value (Uppercut, Putrefy, Shockwave use "Power").
        foreach (var applied in Diagnostics.CardCoverage.PowersApplied(card.GetType()))
            if ((temporary ? SpireQoL.Sim.SimReaders.IsTemporaryDebuff(applied) : SpireQoL.Sim.SimReaders.IsDebuff(applied)) && (list == null || !list.Contains(applied))) (list ??= new()).Add(applied);
        return list ?? (IReadOnlyList<Type>)Array.Empty<Type>();
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
