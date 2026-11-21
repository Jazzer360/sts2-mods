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

    // Cards whose play shuffles your discard pile into your draw pile (CardPileCmd.Shuffle in OnPlay; decompile v0.111.0).
    internal static readonly HashSet<string> ShuffleCards = new() { nameof(Reboot) };

    /// <summary>Cards that shuffle your discard pile in first only when your draw pile is short (Ki Chant, v0.113.0).</summary>
    internal static readonly HashSet<string> ShuffleWhenShortCards = new() { "KiChant" };

    /// <summary>The play shuffles your discard pile into your draw pile before its draws (Reboot; Ki Chant when the draw pile is short).</summary>
    internal static bool ShufflesFirst(CardModel card)
    {
        var name = card.GetType().Name;
        if (ShuffleCards.Contains(name)) return true;
        if (!ShuffleWhenShortCards.Contains(name) || OwnerOf(card) is not { } me) return false;
        // KiChant.OnPlay: an empty draw pile shuffles if it must; a short one shuffles when the discard pile has cards.
        var draw = Sts2State.DrawPileCount(me);
        return draw < Sts2State.Var(card, "Cards", 3) && Sts2State.DiscardPileCount(me) > 0;
    }

    /// <summary>Cards Ki Chant looks at: its Cards from the top of your draw pile, after a shuffle when it's short.</summary>
    private static int KiChantLooksAt(CardModel card)
    {
        var count = Sts2State.Var(card, "Cards", 3);
        if (OwnerOf(card) is not { } me) return count; // the game's template card (the scan)
        var available = Sts2State.DrawPileCount(me) + (ShufflesFirst(card) ? Sts2State.DiscardPileCount(me) : 0);
        return Math.Min(count, available);
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
    public int GeneratedStatuses { get; private set; }
    /// <summary>Of those, Sadness cards you create (Fertilizer, All Is Well react: AfterCardGeneratedForCombat).</summary>
    public int SadnessCreated { get; private set; }
    /// <summary>
    /// Cards it certainly transforms: each transformed card is a created card (CardCmd.Transform calls
    /// AfterCardGeneratedForCombat when the new card lands in a combat pile), so created-card procs react. Not Guards:
    /// you pick how many (0 or more).
    /// </summary>
    public int TransformsCertain => Card.GetType().Name == nameof(Guards) ? 0 : Transforms;
    /// <summary>
    /// Cards outside your hand this play brings back (Make It So, Right Hand Hand, Whistling Arrows: ReturnToHand), in your
    /// hand after it; their tabs.
    /// </summary>
    public IReadOnlyList<CardModel> CardsBack { get; init; } = Array.Empty<CardModel>();
    public int ReturnsToHand => CardsBack.Count;
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
        => AttackHits == 0 ? 0 : HitsPerPlay(Card, Card.GetType().Name, Combat, enemy, Energy, Stars, OtherHandCards) * Math.Max(1, Plays);

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
            GeneratedStatuses = GeneratedStatuses + next.GeneratedStatuses,
            SadnessCreated = SadnessCreated + next.SadnessCreated,
            CardsBack = CardsBack.Concat(next.CardsBack).ToList(),
            EnergyBack = EnergyBack + next.EnergyBack,
            Sim = next.Sim,
            DeathByAim = next.DeathByAim,
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
        var evoked = EvokedOrbs(card, name, me, combat, ChannelCount(card, name, me) * Math.Max(1, plays), Math.Max(1, plays));
        var orbs = OrbDamageFor(evoked, me, combat);
        var generated = GeneratedCount(card, name, others, me, out var statuses) * Math.Max(1, plays) + ForgedBlade(name, me);
        // Cards the play puts into your hand (Bundle of Joy's four): they fill it before anything comes back.
        var createdInHand = GeneratedToPileCards.Contains(name) ? 0 : generated;

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
            ExhaustsOthers = ExhaustOthers(card, name, others, drawPile, plays),
            // Ki Chant looks at your draw pile, so each play discards its share; the others depend on the hand.
            Discards = DiscardCount(card, name, others, draws) * (name == "KiChant" ? Math.Max(1, plays) : 1),
            OwnDraws = draws + swiftDraws,
            DrawsAtMost = SpireQoL.Sim.SimAdapter.DrawnCardReaders.GetValueOrDefault(name) is { DrawsAgain: true }
                ? Math.Max(draws, CardPile.MaxCardsInHand - (hand.Count - 1)) + swiftDraws : 0,
            EnchantmentDraws = swiftDraws,
            BlockGains = BlockGainCount(card, name, others) * Math.Max(1, plays),
            Generated = generated,
            GeneratedStatuses = statuses * Math.Max(1, plays),
            SadnessCreated = SadnessCards.Contains(name) ? statuses * Math.Max(1, plays) : 0,
            CardsBack = ReturnToHand.Returning(card, me, plays, Math.Min(CardPile.MaxCardsInHand, hand.Count - 1 + createdInHand) + draws + swiftDraws,
                Fool.Shoots(card, me) ? Math.Max(1, plays) : 0),
            EnergyBack = card.Enchantment is MegaCrit.Sts2.Core.Models.Enchantments.Sown { Status: MegaCrit.Sts2.Core.Entities.Enchantments.EnchantmentStatus.Normal } sown ? sown.Amount : 0,
            Transforms = TransformCount(card, name, others, drawPile) * Math.Max(1, plays),
            // A shot with no arrow to shoot does nothing at all (its debuffs neither: Hobbling Shot).
            EnemyDebuffs = Fool.IsShot(card) && !Fool.Shoots(card, me) ? Array.Empty<Type>() : EnemyDebuffTypes(card),
            EnemyTemporaryDebuffs = Fool.IsShot(card) && !Fool.Shoots(card, me) ? Array.Empty<Type>() : EnemyDebuffTypes(card, temporary: true),
            HitsAllEnemies = card.TargetType == TargetType.AllEnemies,
            Channels = ChannelCount(card, name, me) * Math.Max(1, plays),
            StarGains = StarGainCards.Contains(name) ? Math.Max(1, plays) : 0,
            OstyAttacks = IsOstyAttack(card) ? Math.Max(1, plays) : 0,
            LosesHp = card.DynamicVars.ContainsKey("HpLoss"),
            HpLoss = Sts2State.Var(card, "HpLoss") * Math.Max(1, plays),
            OtherHandCards = others.Count,
            ByOsty = IsOstyAttack(card),
            AttackHits = (IsOstyAttack(card) ? me.IsOstyAlive && IsAttackOnEnemies(card) : IsPlayerAttack(card)) ? HitsPerPlay(card, name, combat, null, SafeEnergy(card), SafeStars(card), others.Count) * Math.Max(1, plays) : 0,
            OrbDamage = orbs.Exact,
            OrbDamageMin = orbs.Min,
            OrbDamageMax = orbs.Max,
            LightningEvokes = evoked.Count(orb => orb is LightningOrb),
            OrbBlock = OrbBlockFor(evoked, me, combat),
            RandomOutcome = combat.HittableEnemies.Count(creature => creature.IsAlive) > 1
                            && (evoked.Any(orb => orb is LightningOrb) || card.TargetType == TargetType.RandomEnemy),
        };
        facts.ResolveDraws(hand.Count, drawPile, name == nameof(Cleanse));
        return facts;
    }

    /// <summary>
    /// Cards that discard your hand, then draw (CardCmd.DiscardAndDraw): the hand limit counts the hand after the
    /// discard. The card scan flags a new one.
    /// </summary>
    internal static readonly HashSet<string> DiscardThenDrawCards = new() { nameof(CalculatedGamble), "WheelOfFortune", "TheHermit" };

    /// <summary>
    /// Cards whose created cards go to your draw or discard pile, not your hand (AddGeneratedCardToCombat with
    /// PileType.Draw or Discard; decompile v0.113.0). Every other card's created cards are counted as going to your hand.
    /// </summary>
    internal static readonly HashSet<string> GeneratedToPileCards = new()
    {
        nameof(AdaptiveStrike), nameof(Anger), nameof(BoostAway), nameof(CaptureSpirit), nameof(Dirge), nameof(FightThrough),
        nameof(GlimpseBeyond), nameof(GraveWarden), nameof(GunkUp), nameof(Metamorphosis), nameof(MightyShield), nameof(MightyWeapon),
        nameof(Outrage), nameof(Overclock), nameof(Reave), nameof(Turbo), nameof(Undeath),
    };

    /// <summary>Cards that draw, then discard (Acrobatics: the hand limit counts the hand before the discard).</summary>
    internal static readonly HashSet<string> DrawThenDiscardCards = new() { nameof(Acrobatics), nameof(DaggerThrow), nameof(Prepared), nameof(Scrape) };

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
            AddedToHandBeforeEnchantment = EnchantmentDraws > 0 && !GeneratedToPileCards.Contains(Card.GetType().Name) ? Generated : 0,
            Plays = Math.Max(1, Plays),
            IsPower = Card.Type == CardType.Power,
            Exhausts = TotalExhausts,
            ExhaustedFromHand = fromDrawPile ? 0 : ExhaustsOthers,
            ExhaustedFromDrawPile = fromDrawPile ? ExhaustsOthers : 0,
            HandCount = handCount,
            DrawPile = drawPile,
            DiscardPile = Sts2State.DiscardPileCount(me),
            // The Hermit discards one card or, if it's a Curse, exhausts it (counted in ExhaustedFromHand already).
            DiscardedBeforeDraw = !DiscardThenDrawCards.Contains(Card.GetType().Name) ? 0
                : Card.GetType().Name == "TheHermit" ? Math.Max(0, Math.Min(1, OtherHandCards) - ExhaustsOthers) : Discards,
            ShufflesFirst = ShufflesFirst(Card),
            HandShuffledIn = ShuffleCards.Contains(Card.GetType().Name),
            // Calamity adds random attacks to your hand after each attack you play, before your relics draw.
            AddedToHandAfterPlay = Card.Type == CardType.Attack ? (me.Creature.GetPower<CalamityPower>()?.Amount ?? 0) * Math.Max(1, Plays) : 0,
            // Motif draws its amount per melody (Wah, Cho, Ki) this card plays first this turn, once (a replay's
            // later plays aren't first).
            PowerDrawsAfterPlay = (me.Creature.Powers.FirstOrDefault(power => power.GetType().Name == "MotifPower")?.Amount ?? 0)
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
                    drawInputs.JossPaperCount = Sts2State.Private(relic, "CardsExhausted", 0);
                    drawInputs.JossPaperEvery = Sts2State.Var(relic, "ExhaustAmount", 5);
                    break;
                case GamePiece: drawInputs.GamePiece += Sts2State.Var(relic, "Cards", 1); break;
                case IronClub:
                    drawInputs.IronClubEvery = Sts2State.Var(relic, "Cards", 4);
                    drawInputs.IronClubCount = relic.DisplayAmount; // CardsPlayed % Cards (reads Cards while it animates: % again)
                    break;
                case CentennialPuzzle when !Sts2State.Private(relic, "UsedThisCombat", true): drawInputs.CentennialPuzzle += Sts2State.Var(relic, "Cards", 3); break;
            }
        }
        if (ShuffleWhenShortCards.Contains(Card.GetType().Name))
        {
            // Ki Chant takes the top cards it looks at off the draw pile: one to your hand, the rest discarded.
            var looked = KiChantLooksAt(Card);
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
        var laterShuffle = ShuffleWhenShortCards.Contains(Card.GetType().Name)
                           && PlayChain.ShortPileShufflesOnLaterPlay(drawPile, drawInputs.DiscardPile, Sts2State.Var(Card, "Cards", 3), Math.Max(1, Plays));
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
    private static List<OrbModel> EvokedOrbs(CardModel card, string name, Player me, ICombatState combat, int channels, int plays)
    {
        var result = new List<OrbModel>();
        var orbQueue = me.PlayerCombatState?.OrbQueue;
        if (orbQueue == null) return result;
        var queue = orbQueue.Orbs.Cast<OrbModel?>().ToList();
        var cap = orbQueue.Capacity;
        // A new orb pushed out by a later channel (Voltaic's Lightning filling every slot) evokes too: one of yours
        // of the same kind stands in for it when its evoke doesn't depend on the orb itself (Lightning, Frost).
        var kind = SpireQoL.Sim.SimAdapter.ChannelKind(name);
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
            switch (name)
            {
                case nameof(Dualcast): EvokeFront(2, true); break;
                case nameof(Quadcast): EvokeFront(Math.Max(1, Sts2State.Var(card, "Repeat", 4)), true); break;
                case nameof(MultiCast): EvokeFront(XValue(combat, card, SafeEnergy(card)) + (card.IsUpgraded ? 1 : 0), true); break;
                case nameof(Shatter):
                    while (queue.Count > 0) EvokeFront(2, true);
                    break;
            }
            // Slots the card adds before it channels (The Magician: OrbCmd.AddSlots, up to 10).
            var added = SpireQoL.Sim.SimAdapter.SlotChange(card, name);
            if (added > 0) cap = Math.Min(10, cap + added);
            for (var channelIndex = 0; channelIndex < channels / Math.Max(1, plays); channelIndex++)
            {
                if (cap <= 0) break;
                if (queue.Count >= cap) EvokeFront(1, true);
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

    // Cards that draw DynamicVars["Cards"] cards (every CardPileCmd.Draw caller in the decompile).
    private static readonly HashSet<string> DrawCards = new()
    {
        nameof(Acrobatics), nameof(Adrenaline), nameof(Backflip), nameof(BattleTrance), nameof(BigBang), nameof(BrightestFlame), nameof(BurningPact),
        nameof(Coolheaded), nameof(DecisionsDecisions), nameof(DrumOfBattle), nameof(Expertise), nameof(Fetch), nameof(Finesse),
        nameof(FlashOfSteel), nameof(Glimmer), nameof(Glow), nameof(Impatience), nameof(MasterOfStrategy), nameof(MinionStrike),
        nameof(Modded), nameof(Neurosurge), nameof(Offering), nameof(Overclock), nameof(Parse), nameof(PhotonCut), nameof(PommelStrike), nameof(Prepared), nameof(Prophesize),
        nameof(Reboot), nameof(Reflex), nameof(RocketPunch), nameof(Scourge), nameof(Scrape), nameof(ShrugItOff), nameof(Skim), nameof(Slimed), nameof(Soul),
        nameof(SpoilsOfBattle), nameof(SweepingBeam), nameof(ThinkingAhead),
        // v0.113.0 (names as strings: these cards don't exist before it)
        "DeepStretch", "Encore", "Joy", "Rummage", "WahChoKi", "TheHighPriestess", "TheSun", "TheHermit", "WheelOfFortune",
    };

    // Cards that always draw exactly one (Pillage draws at least one).
    private static readonly HashSet<string> DrawOne = new() { nameof(DaggerThrow), nameof(EscapePlan), nameof(Pillage) };

    /// <summary>How many cards one play of this card draws right now (0 if it doesn't draw).</summary>
    public static int CardsDrawn(CardModel card, int handCount)
    {
        var name = card.GetType().Name;
        // A card's condition first: Impatience and Fetch are in DrawCards too (for the coverage scan) but don't always draw.
        switch (card)
        {
            // Draws up to a full hand; Scrawl itself has left the hand by then.
            case Scrawl: return Math.Max(0, CardPile.MaxCardsInHand - (handCount - 1));
            // Discards the rest of your hand, then draws that many (CardCmd.DiscardAndDraw).
            case CalculatedGamble: return Math.Max(0, handCount - 1);
            // Draws one per distinct orb type channeled.
            case CompileDriver when card.DynamicVars.TryGetValue("CalculatedCards", out var dynamicVar) && dynamicVar is CalculatedVar calculatedVar:
                return (int)calculatedVar.Calculate(null);
            // Draws only while you've finished fewer than PlayMax plays this turn (Ftl.CanDrawCard; the
            // game's template card, with no owner, counts as drawing).
            case Ftl: return FtlCanDraw(card) ? Sts2State.Var(card, "Cards") : 0;
            // Draws only when no Attack is in your hand (Impatience.OnPlay; it's a Skill itself).
            case Impatience: return HandHasAttack(card) ? 0 : Sts2State.Var(card, "Cards");
            // Draws only while Osty is alive (Osty.CheckMissingWithAnim) and on this copy's first finished play this turn
            // (Fetch.HasBeenPlayedThisTurn); CardsDrawnTotal counts it once for a replay.
            case Fetch: return FetchCanDraw(card) ? Sts2State.Var(card, "Cards") : 0;
            // Only draws when it's the only card in hand.
            case Restlessness: return handCount == 1 ? Sts2State.Var(card, "Cards") : 0;
            // Draws one per melody played this turn (Steady Cadence).
            case var _ when card.GetType().Name == "SteadyCadence" && card.DynamicVars.TryGetValue("CalculatedCards", out var dynamicVar) && dynamicVar is CalculatedVar scv:
                return (int)scv.Calculate(null);
            // Draws only when its target is Impaled (Ki Hop: anyone's arrow; Wrest: yours): counted when every
            // enemy you could aim at is, so whatever you aim at draws.
            case var _ when card.GetType().Name == "KiHop":
                return AllTargets(card, Fool.IsImpaled) ? Sts2State.Var(card, "Cards") : 0;
            case var _ when card.GetType().Name == "Wrest":
                return OwnerOf(card) is { } player && AllTargets(card, creature => Fool.ImpaledBy(creature, player.Creature) != null) ? Sts2State.Var(card, "Cards") : 0;
            // Mad Science with the Wisdom rider (Tinker Time).
            case MadScience madScience when madScience.TinkerTimeRider.ToString() == "Wisdom": return Sts2State.Var(card, "WisdomCards");
        }
        if (DrawCards.Contains(name)) return Sts2State.Var(card, "Cards");
        if (DrawOne.Contains(name)) return 1;
        return 0;
    }

    /// <summary>
    /// Cards whose draws depend on their target (TargetDraws). The game's template card has no fight to aim at,
    /// so the coverage scan counts these as drawing by this list.
    /// </summary>
    internal static readonly HashSet<string> TargetDrawCards = new() { "KiHop", "Wrest" };

    /// <summary>Cards aimed at a teammate that make them draw, not you (Constellation: CardPileCmd.Draw for the target's player).</summary>
    internal static readonly HashSet<string> AllyDrawCards = new() { nameof(Constellation) };

    /// <summary>
    /// Draws of all <paramref name="plays"/> plays aimed at <paramref name="target"/>, for cards whose draws depend on their
    /// target, from the board before the first play. Null for other cards. Ki Hop draws on each play that starts with the target
    /// Impaled, and its own hit can loosen the last arrow (SimEngine.PlaysStartingImpaled); Wrest pulls all your arrows out on
    /// its first play, so only that one draws. The audit takes it with the play's actual target as the play starts; the
    /// prediction counts what the worst target would draw (CardsDrawnTotal).
    /// </summary>
    public static int? TargetDraws(CardModel card, Creature target, int plays)
    {
        plays = Math.Max(1, plays);
        switch (card.GetType().Name)
        {
            case "KiHop":
            {
                var cards = Sts2State.Var(card, "Cards");
                if (OwnerOf(card) is not { } owner) return Fool.IsImpaled(target) ? cards * plays : 0;
                var yours = Fool.ImpaledBy(target, owner.Creature);
                var teammate = target.Powers.Any(power => power.GetType().Name == "ImpaledPower" && power.Applier != owner.Creature);
                // One hit a play (KiHop.OnPlay: DamageCmd.Attack with no hit count).
                return cards * SimEngine.PlaysStartingImpaled(teammate, yours == null ? 0 : Fool.ArrowsIn(yours),
                    yours == null ? 3 : Fool.HitCounter(yours), 1, plays);
            }
            case "Wrest":
                return OwnerOf(card) is { } player && Fool.ImpaledBy(target, player.Creature) != null ? Sts2State.Var(card, "Cards") : 0;
            default:
                return null;
        }
    }

    /// <summary>
    /// How many cards all <paramref name="plays"/> plays of this card draw right now. A card whose draws depend on its target
    /// counts what the worst target it could be aimed at would draw.
    /// </summary>
    public static int CardsDrawnTotal(CardModel card, int handCount, int plays)
    {
        plays = Math.Max(1, plays);
        if (card is Fetch) return CardsDrawn(card, handCount); // only the first play draws (its replay has finished a play)
        if (!TargetDrawCards.Contains(card.GetType().Name)) return CardsDrawn(card, handCount) * plays;
        try
        {
            var targets = card.CombatState?.HittableEnemies.Where(creature => creature.IsAlive).ToList();
            return targets is { Count: > 0 } ? targets.Min(creature => TargetDraws(card, creature, plays) ?? 0) : 0;
        }
        catch
        {
            return 0;
        }
    }

    /// <summary>Every enemy the card could target matches (none to target: false).</summary>
    private static bool AllTargets(CardModel card, Func<Creature, bool> match)
    {
        try
        {
            var targets = card.CombatState?.HittableEnemies.Where(creature => creature.IsAlive).ToList();
            return targets is { Count: > 0 } && targets.All(match);
        }
        catch
        {
            return false;
        }
    }

    private static bool HandHasAttack(CardModel card)
    {
        try { return OwnerOf(card) is { } me && PileType.Hand.GetPile(me).Cards.Any(otherCard => !ReferenceEquals(otherCard, card) && otherCard.Type == CardType.Attack); }
        catch { return false; }
    }

    private static bool FetchCanDraw(CardModel card)
    {
        try
        {
            if (OwnerOf(card) is not { } me) return true; // the game's template card counts as drawing
            if (!me.IsOstyAlive) return false;
            var combat = card.CombatState;
            return combat == null || !CombatManager.Instance.History.CardPlaysFinished.Any(cardPlayFinishedEntry => ReferenceEquals(cardPlayFinishedEntry.CardPlay.Card, card) && cardPlayFinishedEntry.HappenedThisTurn(combat));
        }
        catch { return true; }
    }

    /// <summary>
    /// Drawing cards whose draw has a condition, which the game shows by glowing them gold (ShouldGlowGoldInternal), all
    /// checked in CardsDrawn or TargetDraws. The card scan flags a drawing card that glows gold and isn't here.
    /// </summary>
    internal static readonly HashSet<string> ConditionalDrawCards = new()
        { nameof(Impatience), nameof(Fetch), nameof(Ftl), nameof(Restlessness), "KiHop", "Wrest" };

    private static bool FtlCanDraw(CardModel card)
    {
        try { return card.Owner == null || Sts2State.CountPlayedThisTurn(card.Owner) < Sts2State.Var(card, "PlayMax", 3); }
        catch { return true; }
    }

    // ------------------------------------------------------------------ exhaust / discard

    internal static int ExhaustOthers(CardModel card, string name, List<CardModel> others, int drawPile, int plays)
    {
        var otherCount = others.Count;
        var playCount = Math.Max(1, plays);
        return name switch
        {
            // One chosen or random card from hand, per play.
            nameof(Brand) or nameof(BurningPact) or nameof(TrueGrit) or nameof(Scavenge) or nameof(Cinder) => Math.Min(playCount, otherCount),
            nameof(Thrash) => Math.Min(playCount, others.Count(other => other.Type == CardType.Attack)),
            nameof(Cleanse) => Math.Min(playCount, drawPile), // from the draw pile
            // The whole rest of the hand (a second play finds the hand empty).
            nameof(FiendFire) or nameof(Stoke) => otherCount,
            nameof(SecondWind) => others.Count(other => other.Type != CardType.Attack),
            // v0.113.0: The Devil exhausts your hand; Growth every Sadness in it; Florescence every Shoot card
            // you have outside your exhaust pile (any pile).
            "TheDevil" => otherCount,
            "Growth" => others.Count(other => other.GetType().Name == "Sadness"),
            "Florescence" => OwnerOf(card)?.PlayerCombatState is { } pcs
                ? pcs.AllCards.Count(combatCard => combatCard != card && Fool.IsShot(combatCard) && combatCard.Pile?.Type != PileType.Exhaust)
                : others.Count(Fool.IsShot),
            // Up to Cards chosen cards: counted as the most it can exhaust.
            nameof(Purity) => Math.Min(otherCount, Sts2State.Var(card, "Cards") * playCount),
            // The Hermit: the card you pick is exhausted instead of discarded when it's a Curse (counted as the most it can do).
            "TheHermit" => Math.Min(playCount, others.Count(other => other.Type == CardType.Curse)),
            // Every status card in your piles.
            nameof(FlakCannon) => card.DynamicVars.TryGetValue("CalculatedHits", out var dynamicVar) && dynamicVar is CalculatedVar calculatedVar ? (int)calculatedVar.Calculate(null) : 0,
            _ => 0,
        };
    }

    internal static int DiscardCount(CardModel card, string name, List<CardModel> others, int draws)
    {
        var otherCount = others.Count;
        return name switch
        {
            // Discard after drawing, so the drawn cards are there to discard.
            nameof(Acrobatics) or nameof(DaggerThrow) => Math.Min(1, otherCount + draws),
            nameof(Prepared) => Math.Min(Sts2State.Var(card, "Cards"), otherCount + draws),
            nameof(Survivor) => Math.Min(1, otherCount),
            nameof(HiddenDaggers) => Math.Min(Sts2State.Var(card, "Cards"), otherCount),
            nameof(CalculatedGamble) or nameof(ShadowStep) or nameof(StormOfSteel) or "WheelOfFortune" => otherCount,
            // Discards one card you choose (a Curse is exhausted instead).
            "TheHermit" => Math.Min(1, otherCount),
            // Looks at the top Cards of your draw pile (shuffling first when it's short), takes one, discards the rest.
            "KiChant" => Math.Max(0, KiChantLooksAt(card) - 1),
            // Discards the cards it drew that cost energy: at most all of them (which ones is a draw).
            nameof(Scrape) => draws,
            _ => 0,
        };
    }

    // ------------------------------------------------------------------ block

    /// <summary>
    /// Cards whose OnPlay can gain block more than once (a loop, or two GainBlock calls), each counted by a rule in BlockGainCount;
    /// the card scan (CardCoverage) flags any other card that does.
    /// </summary>
    internal static readonly HashSet<string> RepeatedBlockCards = new()
    {
        nameof(SecondWind), "ChoLimbs", "ChoFirm", nameof(EvilEye), nameof(DeathsDoor),
        // Its loop is over the living players: each gains block once, you included (once), so the default count holds.
        nameof(Rally),
    };

    internal static int BlockGainCount(CardModel card, string name, List<CardModel> others)
    {
        if (name == nameof(SecondWind)) return others.Count(other => other.Type != CardType.Attack);
        // Twice when its owner exhausted a card this turn (EvilEye.WasCardExhaustedThisTurn, the game's own answer, read by name).
        if (name == nameof(EvilEye)) return OwnerOf(card) != null && Sts2State.Private(card, "WasCardExhaustedThisTurn", false) ? 2 : 1;
        // 1, plus its Repeat when its owner applied Doom this turn (DeathsDoor.WasDoomAppliedThisTurn, read by name).
        if (name == nameof(DeathsDoor))
            return 1 + (OwnerOf(card) != null && Sts2State.Private(card, "WasDoomAppliedThisTurn", false) ? Sts2State.Var(card, "Repeat", 0) : 0);
        // Block equal to the damage it dealt / to your current block: no Block value.
        if (name is nameof(Fisticuffs) or nameof(Entrench)) return 1;
        // Gains its Block twice (ChoLimbs); Cho Firm a second time while you have an arrow to shoot.
        if (name == "ChoLimbs") return 2;
        if (name == "ChoFirm") return OwnerOf(card) is { } player && Fool.ArrowsAvailable(player) > 0 ? 2 : 1;
        // The game's own answer (CardModel.GainsBlock, v0.113.0): Sovereign Blade only while you have Parry, Mad Science only as a
        // Skill, and no block on play from a Power whose Block value is for later (Pillar of Creation, Shroud). Read by name, so
        // a game without it falls back to the card having a Block value. PlayFactsTests checks it against every card.
        // The game's template card (no owner, for the coverage scan) counts by its value: it can gain block.
        var hasValue = card.DynamicVars.ContainsKey("Block") || card.DynamicVars.ContainsKey("CalculatedBlock");
        if (OwnerOf(card) == null) return hasValue ? 1 : 0;
        return Sts2State.Private(card, "GainsBlock", hasValue) ? 1 : 0;
    }

    /// <summary>
    /// Hits per play of an attack card, matching each card's OnPlay: from v0.113.0 a card's hit count is its
    /// HitCount value (CalculatedHits when it's computed); before, Repeat, or a fixed number in OnPlay. The
    /// names are read as strings so both game versions work. Cards that use their X value or a condition
    /// are listed. Unknown target (null): target-dependent cards count their minimum.
    /// </summary>
    internal static int HitsPerPlay(CardModel card, string name, ICombatState combat, Creature? target, int energy, int stars, int otherCards)
    {
        var hits = BaseHitsPerPlay(card, name, combat, target, energy, stars, otherCards);
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

    /// <summary>
    /// Attacks with a Repeat value that isn't their hit count: one attack, then Repeat of something else (Ice Lance
    /// channels Repeat Frost, Heirloom Hammer makes Repeat copies; decompile v0.113.1). The card scan flags a new one.
    /// </summary>
    internal static readonly HashSet<string> SingleHitRepeatCards = new() { nameof(IceLance), nameof(HeirloomHammer) };

    /// <summary>X-cost attacks that hit X times (WithHitCount(X)); Repeat Shot is a shot (v0.113.0). The card scan flags a new one.</summary>
    internal static readonly HashSet<string> XHitCards = new() { nameof(Eradicate), nameof(Skewer), nameof(Volley), nameof(Whirlwind), "RepeatShot" };

    private static int BaseHitsPerPlay(CardModel card, string name, ICombatState combat, Creature? target, int energy, int stars, int otherCards)
    {
        // The card's hit count (v0.113.0: HitCount; v0.111.0: Repeat, or the fixed 2 in OnPlay).
        int Hits(int fixedBefore) => card.DynamicVars.ContainsKey("HitCount") ? Sts2State.Var(card, "HitCount", fixedBefore)
            : fixedBefore > 0 ? fixedBefore : Sts2State.Var(card, "Repeat", 1);
        try
        {
            // A Shoot card with no arrow to shoot does nothing at all (ArrowCmd.ShootArrowAtTarget returns before
            // its attack; the card glows red).
            if (Fool.IsShot(card) && OwnerOf(card) is { } owner && !Fool.Shoots(card, owner)) return 0;
            switch (name)
            {
                // Nail attacks only an Impaled enemy (any player's arrow). Unknown target: it can hit if some enemy is.
                case "Nail":
                    return target != null ? (Fool.IsImpaled(target) ? 1 : 0) : (combat.HittableEnemies.Any(Fool.IsImpaled) ? 1 : 0);
                // Twice once you've played a Cho or a Ki this turn (WahBlast.HitsTwice: the card glows gold).
                case "WahBlast":
                    return Sts2State.Private(card, "ShouldGlowGoldInternal", false) ? 2 : 1;
                // v0.111.0 passed a fixed 2 to WithHitCount (Refract's Repeat is its orb count, not hits).
                case nameof(AstralPulse) or nameof(DaggerSpray) or nameof(Maul) or nameof(Refract) or nameof(RipAndTear) or nameof(Thrash) or nameof(TwinStrike) or nameof(Uproar):
                    return Hits(2);
                case var _ when XHitCards.Contains(name):
                    return XValue(combat, card, energy);
                case var _ when SingleHitRepeatCards.Contains(name):
                    return 1;
                case nameof(HeavenlyDrill):
                {
                    var xValue = XValue(combat, card, energy);
                    return xValue >= Sts2State.Var(card, "Energy") ? xValue * 2 : xValue;
                }
                case nameof(Stardust):
                    return XValue(combat, card, stars);
                case nameof(Dismantle):
                    return target != null && target.HasPower<VulnerablePower>() ? Hits(2) : 1;
                case nameof(Spite): // its hit count only after you lost HP this turn (when the card glows gold)
                    return Sts2State.Private(card, "ShouldGlowGoldInternal", false) ? Hits(0) : 1;
                case nameof(FiendFire):
                    return otherCards;
                case nameof(MadScience):
                    return card is MadScience madScience && madScience.TinkerTimeRider == MegaCrit.Sts2.Core.Models.Events.TinkerTime.RiderEffect.Violence
                        ? Sts2State.Var(card, "ViolenceHits", 1)
                        : 1;
            }
            if (card.DynamicVars.TryGetValue("CalculatedHits", out var dynamicVar) && dynamicVar is CalculatedVar calc)
                return Math.Max(0, (int)calc.Calculate(target));
        }
        catch
        {
            // fall through to the hit count
        }
        return Math.Max(1, Hits(0));
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

    internal static int ChannelCount(CardModel card, string name, Player me) => name switch
    {
        nameof(BallLightning) or nameof(ColdSnap) or nameof(Coolheaded) or nameof(Fusion) or nameof(Glasswork) or nameof(Ignition)
            or nameof(Null) or nameof(ShadowShield) or nameof(Zap) or nameof(Darkness) => 1,
        nameof(Spinner) => card.IsUpgraded ? 1 : 0, // only Spinner+ channels on play; Spinner's power channels at turn start
        nameof(Chill) => ChillChannels(card), // one Frost per hittable enemy
        nameof(Glacier) => 2,
        nameof(MeteorStrike) or nameof(Rainbow) => 3,
        nameof(Chaos) or nameof(ConsumingShadow) or nameof(Hibernate) or nameof(IceLance) or nameof(Refract) or "TheMagician" => Sts2State.Var(card, "Repeat", 1),
        nameof(Tempest) => me?.PlayerCombatState?.Energy ?? 1, // X lightning
        // One Lightning per Lightning channeled this combat (its calculation reads the history).
        nameof(Voltaic) => card.DynamicVars.TryGetValue("CalculatedChannels", out var dynamicVar) && dynamicVar is CalculatedVar calc ? (int)calc.Calculate(null) : 0,
        _ => 0,
    };

    private static int ChillChannels(CardModel card)
    {
        try { return card.CombatState is { } combat ? combat.HittableEnemies.Count : 1; } catch { return 1; }
    }

    // Cards that gain you stars when played (PlayerCmd.GainStars in OnPlay).
    internal static readonly HashSet<string> StarGainCards = new()
    {
        nameof(BigBang), nameof(GatherLight), nameof(Glow), nameof(HiddenCache), nameof(KnockoutBlow), nameof(RoyalGamble), nameof(ShiningStrike), nameof(SolarStrike), nameof(Venerate),
    };

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

    /// <summary>
    /// Cards that create Sadness when played (CreateCard&lt;Sadness&gt; in OnPlay, or Mourning Blow turning into one after
    /// it; v0.113.1): their created statuses are all Sadness (GeneratedCount), so that count is the Sadness made.
    /// </summary>
    internal static readonly HashSet<string> SadnessCards = new() { "BrightenUp", "Jest", "MourningBlow", "Rummage", "SelfDeprecate", "SorrowfulStrike", "Vitriol" };


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
            case nameof(LeadingStrike): return 1;
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
            case "BlotOutTheSky": case "Florescence": return 1;
            case "MourningBlow": case "Vitriol": case "Rummage": statuses = 1; return 1; // Mourning Blow turns into a Sadness
            case "Jest": case "SorrowfulStrike": statuses = Var("Cards"); return statuses;
            case "BrightenUp": statuses = Var("Sadness", 2); return statuses + Var("Joy", 2);
            case "SelfDeprecate":
            {
                // Sadness for you, Joy for every other living player (created by you).
                var mates = me?.Creature?.CombatState?.Players.Count(player => player != me && player.Creature is { IsAlive: true }) ?? 0;
                statuses = Var("Sadness", 1);
                return statuses + Var("Joy", 2) * mates;
            }
            case "TheHierophant": return Var("Cards", 3);
            case "TheLovers": return Math.Max(0, CardPile.MaxCardsInHand - others.Count);
            // Shivs until your hand is full (TheMoon.OnPlay: MaxCardsInHand - hand, once it has left your hand).
            case "TheMoon": return Math.Max(0, CardPile.MaxCardsInHand - others.Count);
            case "TheSun": statuses = 1; return 1; // a Soot into your discard pile
            case "MightyShield": case "MightyWeapon": statuses = Var("Cards"); return statuses; // Dazed into your draw pile
        }
        return 0;
    }

    // Cards that Forge when played (ForgeCmd.Forge in OnPlay; decompile v0.111.0).
    internal static readonly HashSet<string> ForgeCards = new()
    {
        nameof(BeatIntoShape), nameof(BigBang), nameof(Bulwark), nameof(Conqueror), nameof(RefineBlade),
        nameof(SeekingEdge), nameof(SpoilsOfBattle), nameof(SummonForth), nameof(TheSmith), nameof(WroughtInWar),
    };

    /// <summary>
    /// Forging creates a Sovereign Blade in your hand when you have none outside your exhaust pile
    /// (ForgeCmd.Forge, through AddGeneratedCardToCombat); later forges only add damage. Once per play
    /// chain: a replay finds the blade the first play made.
    /// </summary>
    private static int ForgedBlade(string name, Player me)
    {
        if (!ForgeCards.Contains(name)) return 0;
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

    /// <summary>Most cards a play transforms (CardCmd.Transform / TransformTo in OnPlay; decompile v0.111.0).</summary>
    internal static int TransformCount(CardModel card, string name, List<CardModel> others, int drawPile)
    {
        int Var(string key, int fallback) => Sts2State.Var(card, key, fallback);
        int Transformable(Func<CardModel, bool> filter)
        {
            try { return others.Count(other => other.IsTransformable && filter(other)); } catch { return others.Count(filter); }
        }
        return name switch
        {
            nameof(Begone) => Math.Min(1, Transformable(_ => true)),                        // 1 card in hand you pick
            nameof(Guards) => Transformable(_ => true),                                     // any cards in hand you pick
            nameof(Charge) => Math.Min(Var("Cards", 2), drawPile),                          // cards from your draw pile
            nameof(Seance) => Math.Min(Var("Cards", 1), drawPile),                          // into Souls
            nameof(Compact) => Transformable(otherCard => otherCard.Type == CardType.Status),               // every status in hand
            nameof(PrimalForce) => Transformable(otherCard => otherCard.Type == CardType.Attack),           // every attack in hand
            "PunchOfJoy" => Math.Min(1, others.Count(other => other.GetType().Name == "Sadness")),  // a Sadness in hand into Joy (v0.113.0)
            _ => 0,
        };
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

    /// <summary>The simulation key for a play aimed at this creature: an enemy id, a teammate's AllyKey, or -1.</summary>
    public int KeyFor(Creature? target) =>
        target == null ? -1
        : Ids.TryGetValue(target, out var id) ? id
        : Allies.ToList().IndexOf(target) is var teammateIndex and >= 0 ? Who.Teammate(teammateIndex) : -1;
    public Dictionary<int, SimPlay> PlayByTarget { get; } = new();
}
