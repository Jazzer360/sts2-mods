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
/// card per frame and read by every proc rule (ProcIndicatorsFeature) and the Shuffle Warning.
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
    /// Draws your powers and relics add because of this play (CardPileCmd.Draw in their hooks):
    /// Dark Embrace per card exhausted, Joss Paper on reaching its exhaust count, Game Piece on a
    /// Power, Iron Club on completing its count, Vicious per Vulnerable you apply, Centennial Puzzle
    /// on first HP loss. Draws that depend on which card is drawn (Pagestorm, Iteration) aren't
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

    // Cards whose play shuffles your discard pile into your draw pile (CardPileCmd.Shuffle in OnPlay; decompile v0.111.0).
    internal static readonly HashSet<string> ShuffleCards = new() { nameof(Reboot) };

    /// <summary>Cards that shuffle your discard pile in first only when your draw pile is short (Ki Chant, v0.113.0).</summary>
    internal static readonly HashSet<string> ShuffleWhenShortCards = new() { "KiChant" };

    /// <summary>The play shuffles your discard pile into your draw pile before its draws (Reboot; Ki Chant when the draw pile is short).</summary>
    internal static bool ShufflesFirst(CardModel card)
    {
        var name = card.GetType().Name;
        if (ShuffleCards.Contains(name)) return true;
        if (!ShuffleWhenShortCards.Contains(name) || card.Owner is not { } me) return false;
        // KiChant.OnPlay: an empty draw pile shuffles if it must; a short one shuffles when the discard pile has cards.
        var draw = Sts2State.DrawPileCount(me);
        return draw < Sts2State.Var(card, "Cards", 3) && Sts2State.DiscardPileCount(me) > 0;
    }

    /// <summary>Cards Ki Chant looks at: its Cards from the top of your draw pile, after a shuffle when it's short.</summary>
    private static int KiChantLooksAt(CardModel card)
    {
        var count = Sts2State.Var(card, "Cards", 3);
        if (card.Owner is not { } me) return count; // the game's template card (the scan)
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

    /// <summary>
    /// Most cards it can transform (Begone, Charge, Guards...). The game's history logs each new card
    /// as created, but nothing reacts to it as one (CardCmd.Transform doesn't call
    /// AfterCardGeneratedForCombat), so it's kept out of Generated. The audit only.
    /// </summary>
    public int Transforms { get; init; }

    /// <summary>Debuff powers it applies to enemies (from its PowerVar&lt;T&gt; vars), e.g. Weak, Vulnerable, Doom.</summary>
    public IReadOnlyList<Type> EnemyDebuffs { get; init; } = Array.Empty<Type>();

    /// <summary>Enemies its debuffs can land on: every enemy for all-enemy cards, else each possible target.</summary>
    public bool HitsAllEnemies { get; init; }

    /// <summary>Orbs it channels (for Metronome).</summary>
    public int Channels { get; init; }

    /// <summary>Times it gains you stars (for Black Hole).</summary>
    public int StarGains { get; init; }

    /// <summary>Times Osty attacks because of it (for Bone Flute).</summary>
    public int OstyAttacks { get; init; }

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
    {
        if (AttackHits == 0) return 0;
        try
        {
            decimal raw;
            // The card's own damage props, as its preview uses them (DamageVar.Props): an arrow attack's
            // FromArrow is what Aim, Heavy Shot's doubled Aim, Dislike You and Quietus check (v0.113.0).
            var props = MegaCrit.Sts2.Core.ValueProps.ValueProp.Move;
            if (Card.DynamicVars.TryGetValue("CalculatedDamage", out var cv) && cv is CalculatedVar calc)
            {
                raw = calc.Calculate(enemy);
                if (cv is CalculatedDamageVar cdv) props = cdv.Props;
            }
            else if (Card.DynamicVars.TryGetValue("Damage", out var dv))
            {
                raw = dv.BaseValue;
                if (dv is DamageVar d) props = d.Props;
            }
            else return 0;
            var v = MegaCrit.Sts2.Core.Hooks.Hook.ModifyDamage(Me.RunState, Combat, enemy, Me.Creature, raw, props,
                Card, null, MegaCrit.Sts2.Core.Hooks.ModifyDamageHookType.All, CardPreviewMode.None, out _);
            return Math.Max(0m, v);
        }
        catch
        {
            return 0;
        }
    }

    /// <summary>Total damage this card deals to the enemy across all its hits (before block).</summary>
    public int TotalDamage(Creature enemy)
    {
        var hits = HitsOn(enemy);
        if (LaterHitDamage(enemy) is not { } later) return DamagePerHit(enemy) * hits;
        return DamagePerHit(enemy) * Math.Min(hits, later.After) + later.Damage * Math.Max(0, hits - later.After);
    }

    /// <summary>
    /// Hits that get weaker partway through the play: an enemy only your arrows Impale loses Impaled with its last
    /// arrow (ImpaledPower.AfterDamageReceived: the counter's hit, then one arrow per 3 more hits), and with it the
    /// multipliers that need it: Serrated Arrows (SerratedArrowsPower, your powered hits on an Impaled enemy) and
    /// Wah Hammer's ×2. After = the hits before that (the freeing hit included), Damage = each later hit (the
    /// game's damage for this enemy without those multipliers). Null when nothing changes.
    /// </summary>
    public (int After, int Damage)? LaterHitDamage(Creature enemy)
    {
        try
        {
            if (Fool.ImpaledBy(enemy, Me.Creature) is not { } imp) return null;
            if (enemy.Powers.Count(p => p.GetType().Name == "ImpaledPower") > 1) return null; // another player's arrows keep it Impaled
            var factor = 1m;
            foreach (var p in Me.Creature.Powers.Where(p => p.GetType().Name == "SerratedArrowsPower")) factor *= 1m + p.Amount / 100m;
            if (Card.GetType().Name == "WahHammer") factor *= 2m;
            if (factor == 1m) return null;
            var arrows = Fool.ArrowsIn(imp);
            if (arrows <= 0) return null;
            var after = Fool.HitCounter(imp) + 3 * (arrows - 1);
            if (HitsOn(enemy) <= after) return null;
            return (after, Math.Max(0, (int)(RawDamagePerHit(enemy) / factor)));
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// The card alone kills the enemy (its hits and orb evokes ≥ block + HP; ignores caps). Only a
    /// pre-simulation estimate for where its random and all-enemy procs can land (the procs feed the
    /// simulator, so they can't wait for it); kill tabs come from the simulator.
    /// </summary>
    public bool Kills(Creature enemy)
        => enemy.IsAlive && enemy.IsHittable
           && (CanHit(enemy) ? TotalDamage(enemy) : 0) + OrbDamageOn(enemy) > 0
           && (CanHit(enemy) ? TotalDamage(enemy) : 0) + OrbDamageOn(enemy) >= enemy.Block + enemy.CurrentHp;

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

    public int OrbDamageOn(Creature enemy) => OrbDamage.TryGetValue(enemy, out var d) ? d : 0;

    /// <summary>
    /// This play followed by the plays of a card it auto-plays (Decisions, Decisions' pick, whose facts
    /// were read during the choice): the counts the audit checks added together, and the pick's
    /// simulation (its start state already includes everything this play did before the choice).
    /// </summary>
    internal PlayFacts Then(PlayFacts next)
    {
        var orbDamage = new Dictionary<Creature, int>(next.OrbDamage);
        foreach (var (e, d) in OrbDamage) orbDamage[e] = (orbDamage.TryGetValue(e, out var x) ? x : 0) + d;
        return new PlayFacts
        {
            Card = Card, Me = Me, Combat = Combat,
            Plays = Plays, Energy = Energy, Stars = Stars,
            ExhaustsSelf = ExhaustsSelf,
            ExhaustsOthers = ExhaustsOthers + next.TotalExhausts,
            Discards = Discards + next.Discards,
            OwnDraws = OwnDraws + next.OwnDraws,
            BlockGains = BlockGains + next.BlockGains,
            Transforms = Transforms + next.Transforms,
            EnemyDebuffs = EnemyDebuffs.Concat(next.EnemyDebuffs).Distinct().ToList(),
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
            Sim = next.Sim,
            RandomOutcome = RandomOutcome || next.RandomOutcome,
            SimPending = SimPending || next.SimPending,
        };
    }

    // ------------------------------------------------------------------ building

    public static PlayFacts For(CardModel card, Player me, ICombatState cs, int plays)
    {
        var hand = Sts2State.Hand(me);
        var others = hand.Where(c => !ReferenceEquals(c, card)).ToList();
        var draws = CardsDrawn(card, hand.Count) * Math.Max(1, plays);
        var drawPile = Sts2State.DrawPileCount(me);
        var name = card.GetType().Name;
        var evoked = EvokedOrbs(card, name, me, cs, ChannelCount(card, name, me) * Math.Max(1, plays), Math.Max(1, plays));
        var orbs = OrbDamageFor(evoked, me, cs);

        var facts = new PlayFacts
        {
            Card = card,
            Me = me,
            Combat = cs,
            Plays = Math.Max(1, plays),
            Energy = SafeEnergy(card),
            AutoPlayed = Features.CardIndicatorsFeature.ChoiceAutoPlayed(card),
            Stars = SafeStars(card),
            // Exhaust keyword (including added Exhaust), or Corruption sending a played skill to the
            // exhaust pile (CorruptionPower.ModifyCardPlayResultLocation).
            ExhaustsSelf = card.Type != CardType.Power
                           && (card.Keywords.Contains(CardKeyword.Exhaust)
                               || (card.Type == CardType.Skill && me.Creature.Powers.Any(p => p is CorruptionPower))),
            ExhaustsOthers = ExhaustOthers(card, name, others, drawPile, plays),
            Discards = DiscardCount(card, name, others, draws),
            OwnDraws = draws,
            BlockGains = BlockGainCount(card, name, others) * Math.Max(1, plays),
            Generated = GeneratedCount(card, name, others, me, out var statuses) * Math.Max(1, plays) + ForgedBlade(name, me),
            GeneratedStatuses = statuses * Math.Max(1, plays),
            Transforms = TransformCount(card, name, others, drawPile) * Math.Max(1, plays),
            EnemyDebuffs = EnemyDebuffTypes(card),
            HitsAllEnemies = card.TargetType == TargetType.AllEnemies,
            Channels = ChannelCount(card, name, me) * Math.Max(1, plays),
            StarGains = StarGainCards.Contains(name) ? Math.Max(1, plays) : 0,
            OstyAttacks = OstyAttackCards.Contains(name) ? Math.Max(1, plays) : 0,
            LosesHp = card.DynamicVars.ContainsKey("HpLoss"),
            HpLoss = Sts2State.Var(card, "HpLoss") * Math.Max(1, plays),
            OtherHandCards = others.Count,
            AttackHits = IsPlayerAttack(card) ? HitsPerPlay(card, name, cs, null, SafeEnergy(card), SafeStars(card), others.Count) * Math.Max(1, plays) : 0,
            OrbDamage = orbs.Exact,
            OrbDamageMin = orbs.Min,
            OrbDamageMax = orbs.Max,
            LightningEvokes = evoked.Count(o => o is LightningOrb),
            OrbBlock = OrbBlockFor(evoked, me, cs),
            RandomOutcome = cs.HittableEnemies.Count(e => e.IsAlive) > 1
                            && (evoked.Any(o => o is LightningOrb) || card.TargetType == TargetType.RandomEnemy),
        };
        facts.ResolveDraws(hand.Count, drawPile, name == nameof(Cleanse));
        return facts;
    }

    /// <summary>
    /// Total draws (own + chained), the hand limit, draw blockers, and whether they shuffle: the
    /// game's numbers for PlayChain.Resolve (pure, tested offline). Sources read here: Dark Embrace,
    /// Vicious, Joss Paper, Game Piece, Iron Club, Centennial Puzzle; No Draw and Fiddle block draws.
    /// </summary>
    private void ResolveDraws(int handCount, int drawPile, bool fromDrawPile)
    {
        var me = Me;
        var i = new PlayChain.DrawInputs
        {
            OwnDraws = OwnDraws,
            Plays = Math.Max(1, Plays),
            IsPower = Card.Type == CardType.Power,
            Exhausts = TotalExhausts,
            ExhaustedFromHand = fromDrawPile ? 0 : ExhaustsOthers,
            ExhaustedFromDrawPile = fromDrawPile ? ExhaustsOthers : 0,
            HandCount = handCount,
            DrawPile = drawPile,
            DiscardPile = Sts2State.DiscardPileCount(me),
            DiscardedBeforeDraw = Card is CalculatedGamble ? Discards : 0,
            ShufflesFirst = ShufflesFirst(Card),
            // Calamity adds random attacks to your hand after each attack you play, before your relics draw.
            AddedToHandAfterPlay = Card.Type == CardType.Attack ? (me.Creature.GetPower<CalamityPower>()?.Amount ?? 0) * Math.Max(1, Plays) : 0,
            VulnerableLandings = DebuffsLandingTotal(t => t == typeof(VulnerablePower)),
            LosesHp = HpLoss > 0,
            Blocked = me.Creature.HasPower<NoDrawPower>() || me.Relics.Any(r => r is Fiddle),
            DarkEmbrace = me.Creature.GetPower<DarkEmbracePower>()?.Amount ?? 0,
            Vicious = me.Creature.GetPower<ViciousPower>()?.Amount ?? 0,
        };
        foreach (var r in me.Relics)
        {
            switch (r)
            {
                case JossPaper:
                    i.JossPaperCount = Sts2State.Private(r, "CardsExhausted", 0);
                    i.JossPaperEvery = Sts2State.Var(r, "ExhaustAmount", 5);
                    break;
                case GamePiece: i.GamePiece += Sts2State.Var(r, "Cards", 1); break;
                case IronClub:
                    i.IronClubEvery = Sts2State.Var(r, "Cards", 4);
                    i.IronClubCount = r.DisplayAmount; // CardsPlayed % Cards (reads Cards while it animates: % again)
                    break;
                case CentennialPuzzle when !Sts2State.Private(r, "UsedThisCombat", true): i.CentennialPuzzle += Sts2State.Var(r, "Cards", 3); break;
            }
        }
        var result = PlayChain.Resolve(i);
        DrawsBlocked = i.Blocked;
        ChainedDraws = result.Chained;
        Draws = result.Draws;
        Shuffles = result.Shuffles;
        ShufflesItself = i.ShufflesFirst;
        // Biiig Hug puts a Soot (a status) into your draw pile on the shuffle: a created status.
        if (Shuffles && me.Relics.Any(r => r is BiiigHug))
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
    private static List<OrbModel> EvokedOrbs(CardModel card, string name, Player me, ICombatState cs, int channels, int plays)
    {
        var result = new List<OrbModel>();
        var q = me.PlayerCombatState?.OrbQueue;
        if (q == null) return result;
        var queue = q.Orbs.Cast<OrbModel?>().ToList();
        var cap = q.Capacity;
        void EvokeFront(int times, bool remove)
        {
            if (queue.Count == 0) return;
            for (var t = 0; t < times; t++)
                if (queue[0] is { } o) result.Add(o);
            if (remove) queue.RemoveAt(0);
        }
        for (var p = 0; p < plays; p++)
        {
            switch (name)
            {
                case nameof(Dualcast): EvokeFront(2, true); break;
                case nameof(Quadcast): EvokeFront(Math.Max(1, Sts2State.Var(card, "Repeat", 4)), true); break;
                case nameof(MultiCast): EvokeFront(XValue(cs, card, SafeEnergy(card)) + (card.IsUpgraded ? 1 : 0), true); break;
                case nameof(Shatter):
                    while (queue.Count > 0) EvokeFront(2, true);
                    break;
            }
            for (var c = 0; c < channels / Math.Max(1, plays); c++)
            {
                if (cap <= 0) break;
                if (queue.Count >= cap) EvokeFront(1, true);
                queue.Add(null); // the new orb
            }
        }
        return result;
    }

    private static int OrbBlockFor(List<OrbModel> evoked, Player me, ICombatState cs)
    {
        var total = 0;
        foreach (var orb in evoked)
        {
            if (orb is not FrostOrb || orb.EvokeVal <= 0) continue;
            try
            {
                total += Math.Max(0, (int)MegaCrit.Sts2.Core.Hooks.Hook.ModifyBlock(cs, me.Creature, orb.EvokeVal, MegaCrit.Sts2.Core.ValueProps.ValueProp.Unpowered, null, null, out _));
            }
            catch
            {
                total += (int)orb.EvokeVal;
            }
        }
        return total;
    }

    private static (Dictionary<Creature, int> Exact, int Min, int Max) OrbDamageFor(List<OrbModel> evoked, Player me, ICombatState cs)
    {
        var none = (new Dictionary<Creature, int>(), 0, 0);
        if (evoked.Count == 0) return none;
        var enemies = cs.HittableEnemies.Where(e => e.IsAlive).ToList();
        if (enemies.Count == 0) return none;
        var exact = new Dictionary<Creature, int>();
        int min = 0, max = 0;
        int Mod(Creature t, decimal v)
        {
            try
            {
                return Math.Max(0, (int)MegaCrit.Sts2.Core.Hooks.Hook.ModifyDamage(me.RunState, cs, t, me.Creature, v,
                    MegaCrit.Sts2.Core.ValueProps.ValueProp.Unpowered, null, null, MegaCrit.Sts2.Core.Hooks.ModifyDamageHookType.All, CardPreviewMode.None, out _));
            }
            catch
            {
                return (int)v;
            }
        }
        void Add(Creature e, int d) => exact[e] = (exact.TryGetValue(e, out var x) ? x : 0) + d;
        foreach (var orb in evoked)
        {
            var v = orb.EvokeVal;
            if (v <= 0) continue;
            switch (orb)
            {
                case GlassOrb:
                    foreach (var e in enemies)
                    {
                        var d = Mod(e, v);
                        Add(e, d);
                        min += d;
                        max += d;
                    }
                    break;
                case DarkOrb:
                {
                    var weakest = enemies.MinBy(e => e.CurrentHp)!;
                    var d = Mod(weakest, v);
                    Add(weakest, d);
                    min += d;
                    max += d;
                    break;
                }
                case LightningOrb:
                    if (enemies.Count == 1)
                    {
                        var d = Mod(enemies[0], v);
                        Add(enemies[0], d);
                        min += d;
                        max += d;
                    }
                    else
                    {
                        min += enemies.Min(e => Mod(e, v));
                        max += enemies.Max(e => Mod(e, v));
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
        nameof(Constellation), nameof(Coolheaded), nameof(DecisionsDecisions), nameof(DrumOfBattle), nameof(Expertise), nameof(Fetch), nameof(Finesse),
        nameof(FlashOfSteel), nameof(Glimmer), nameof(Glow), nameof(HuddleUp), nameof(Impatience), nameof(MasterOfStrategy), nameof(MinionStrike),
        nameof(Modded), nameof(Neurosurge), nameof(Offering), nameof(Overclock), nameof(Parse), nameof(PhotonCut), nameof(PommelStrike), nameof(Prepared), nameof(Prophesize),
        nameof(Reboot), nameof(Reflex), nameof(RocketPunch), nameof(Scourge), nameof(Scrape), nameof(ShrugItOff), nameof(Skim), nameof(Slimed), nameof(Soul),
        nameof(SpoilsOfBattle), nameof(SweepingBeam), nameof(ThinkingAhead),
        // v0.113.0 (names as strings: these cards don't exist before it)
        "DeepStretch", "Encore", "Joy", "Rummage", "WahChoKi", "TheHighPriestess", "TheSun", "TheHermit", "WheelOfFortune",
    };

    // Cards that always draw exactly one (Pillage draws at least one).
    private static readonly HashSet<string> DrawOne = new() { nameof(DaggerThrow), nameof(EscapePlan), nameof(Pillage) };

    /// <summary>How many cards one play of this card draws right now (0 if it doesn't draw).</summary>
    public static int CardsDrawn(CardModel c, int handCount)
    {
        var name = c.GetType().Name;
        if (DrawCards.Contains(name)) return Sts2State.Var(c, "Cards");
        if (DrawOne.Contains(name)) return 1;
        switch (c)
        {
            // Draws up to a full hand; Scrawl itself has left the hand by then.
            case Scrawl: return Math.Max(0, CardPile.MaxCardsInHand - (handCount - 1));
            // Discards the rest of your hand, then draws that many (CardCmd.DiscardAndDraw).
            case CalculatedGamble: return Math.Max(0, handCount - 1);
            // Draws one per distinct orb type channeled.
            case CompileDriver when c.DynamicVars.TryGetValue("CalculatedCards", out var v) && v is CalculatedVar cv:
                return (int)cv.Calculate(null);
            // Draws only while you've finished fewer than PlayMax plays this turn (Ftl.CanDrawCard; the
            // game's template card, with no owner, counts as drawing).
            case Ftl: return FtlCanDraw(c) ? Sts2State.Var(c, "Cards") : 0;
            // Only draws when it's the only card in hand.
            case Restlessness: return handCount == 1 ? Sts2State.Var(c, "Cards") : 0;
            // Draws one per melody played this turn (Steady Cadence).
            case { } when c.GetType().Name == "SteadyCadence" && c.DynamicVars.TryGetValue("CalculatedCards", out var sv) && sv is CalculatedVar scv:
                return (int)scv.Calculate(null);
            // Draws only when its target is Impaled (Ki Hop: anyone's arrow; Wrest: yours): counted when every
            // enemy you could aim at is, so whatever you aim at draws.
            case { } when c.GetType().Name == "KiHop":
                return AllTargets(c, Fool.IsImpaled) ? Sts2State.Var(c, "Cards") : 0;
            case { } when c.GetType().Name == "Wrest":
                return c.Owner is { } wo && AllTargets(c, e => Fool.ImpaledBy(e, wo.Creature) != null) ? Sts2State.Var(c, "Cards") : 0;
            // Mad Science with the Wisdom rider (Tinker Time).
            case MadScience m when m.TinkerTimeRider.ToString() == "Wisdom": return Sts2State.Var(c, "WisdomCards");
        }
        return 0;
    }

    /// <summary>
    /// Draws of one play aimed at <paramref name="target"/>, for cards whose draws depend on their target (Ki Hop:
    /// an Impaled enemy; Wrest: one your arrow Impales). Null for other cards. The audit uses it with the play's
    /// actual target; before the play, CardsDrawn counts them only when every possible target qualifies.
    /// </summary>
    public static int? TargetDraws(CardModel c, Creature target) => c.GetType().Name switch
    {
        "KiHop" => Fool.IsImpaled(target) ? Sts2State.Var(c, "Cards") : 0,
        "Wrest" => c.Owner is { } o && Fool.ImpaledBy(target, o.Creature) != null ? Sts2State.Var(c, "Cards") : 0,
        _ => null,
    };

    /// <summary>Every enemy the card could target matches (none to target: false).</summary>
    private static bool AllTargets(CardModel c, Func<Creature, bool> match)
    {
        try
        {
            var targets = c.CombatState?.HittableEnemies.Where(e => e.IsAlive).ToList();
            return targets is { Count: > 0 } && targets.All(match);
        }
        catch
        {
            return false;
        }
    }

    private static bool FtlCanDraw(CardModel c)
    {
        try { return c.Owner == null || Sts2State.CountPlayedThisTurn(c.Owner) < Sts2State.Var(c, "PlayMax", 3); }
        catch { return true; }
    }

    // ------------------------------------------------------------------ exhaust / discard

    internal static int ExhaustOthers(CardModel card, string name, List<CardModel> others, int drawPile, int plays)
    {
        var n = others.Count;
        var p = Math.Max(1, plays);
        return name switch
        {
            // One chosen or random card from hand, per play.
            nameof(Brand) or nameof(BurningPact) or nameof(TrueGrit) or nameof(Scavenge) or nameof(Cinder) => Math.Min(p, n),
            nameof(Thrash) => Math.Min(p, others.Count(c => c.Type == CardType.Attack)),
            nameof(Cleanse) => Math.Min(p, drawPile), // from the draw pile
            // The whole rest of the hand (a second play finds the hand empty).
            nameof(FiendFire) or nameof(Stoke) => n,
            nameof(SecondWind) => others.Count(c => c.Type != CardType.Attack),
            // v0.113.0: The Devil exhausts your hand; Growth every Sadness in it; Florescence every Shoot card
            // you have outside your exhaust pile (any pile).
            "TheDevil" => n,
            "Growth" => others.Count(c => c.GetType().Name == "Sadness"),
            "Florescence" => card.Owner?.PlayerCombatState is { } pcs
                ? pcs.AllCards.Count(c => c != card && Fool.IsShot(c) && c.Pile?.Type != PileType.Exhaust)
                : others.Count(Fool.IsShot),
            // Up to Cards chosen cards: counted as the most it can exhaust.
            nameof(Purity) => Math.Min(n, Sts2State.Var(card, "Cards") * p),
            // Every status card in your piles.
            nameof(FlakCannon) => card.DynamicVars.TryGetValue("CalculatedHits", out var v) && v is CalculatedVar cv ? (int)cv.Calculate(null) : 0,
            _ => 0,
        };
    }

    internal static int DiscardCount(CardModel card, string name, List<CardModel> others, int draws)
    {
        var n = others.Count;
        return name switch
        {
            // Discard after drawing, so the drawn cards are there to discard.
            nameof(Acrobatics) or nameof(DaggerThrow) => Math.Min(1, n + draws),
            nameof(Prepared) => Math.Min(Sts2State.Var(card, "Cards"), n + draws),
            nameof(Survivor) => Math.Min(1, n),
            nameof(HiddenDaggers) => Math.Min(Sts2State.Var(card, "Cards"), n),
            nameof(CalculatedGamble) or nameof(ShadowStep) or nameof(StormOfSteel) or "WheelOfFortune" => n,
            // Discards one card you choose (a Curse is exhausted instead).
            "TheHermit" => Math.Min(1, n),
            // Looks at the top Cards of your draw pile (shuffling first when it's short), takes one, discards the rest.
            "KiChant" => Math.Max(0, KiChantLooksAt(card) - 1),
            // Discards the cards it drew that cost energy: at most all of them (which ones is a draw).
            nameof(Scrape) => draws,
            _ => 0,
        };
    }

    // ------------------------------------------------------------------ block

    internal static int BlockGainCount(CardModel card, string name, List<CardModel> others)
    {
        if (name == nameof(SecondWind)) return others.Count(c => c.Type != CardType.Attack);
        // Block equal to the damage it dealt / to your current block: no Block value.
        if (name is nameof(Fisticuffs) or nameof(Entrench)) return 1;
        // Gains its Block twice (ChoLimbs); Cho Firm a second time while you have an arrow to shoot.
        if (name == "ChoLimbs") return 2;
        if (name == "ChoFirm") return card.Owner is { } p && Fool.ArrowsAvailable(p) > 0 ? 2 : 1;
        return card.DynamicVars.ContainsKey("Block") || card.DynamicVars.ContainsKey("CalculatedBlock") ? 1 : 0;
    }

    /// <summary>An attack you deal yourself (a Damage value; Osty attacks use OstyDamage instead).</summary>
    /// <summary>
    /// Hits per play of an attack card, matching each card's OnPlay: from v0.113.0 a card's hit count is its
    /// HitCount value (CalculatedHits when it's computed); before, Repeat, or a fixed number in OnPlay. The
    /// names are read as strings so both game versions work. Cards that use their X value or a condition
    /// are listed. Unknown target (null): target-dependent cards count their minimum.
    /// </summary>
    internal static int HitsPerPlay(CardModel card, string name, ICombatState cs, Creature? target, int energy, int stars, int otherCards)
    {
        var hits = BaseHitsPerPlay(card, name, cs, target, energy, stars, otherCards);
        // Then the game's hit-count modifiers (Thumbscrew: +1 on your multi-hit attacks), the way its card
        // preview applies them (HitCountVar.GetPreviewValue; the attack itself in AttackCommand.Execute).
        if (hits <= 0 || card.Type != CardType.Attack) return hits;
        try
        {
            return Math.Max(0, (int)MegaCrit.Sts2.Core.Hooks.Hook.ModifyAttackHitCount(cs,
                MegaCrit.Sts2.Core.Commands.DamageCmd.Attack(0m).FromCard(card, null), hits));
        }
        catch
        {
            return hits;
        }
    }

    private static int BaseHitsPerPlay(CardModel card, string name, ICombatState cs, Creature? target, int energy, int stars, int otherCards)
    {
        // The card's hit count (v0.113.0: HitCount; v0.111.0: Repeat, or the fixed 2 in OnPlay).
        int Hits(int fixedBefore) => card.DynamicVars.ContainsKey("HitCount") ? Sts2State.Var(card, "HitCount", fixedBefore)
            : fixedBefore > 0 ? fixedBefore : Sts2State.Var(card, "Repeat", 1);
        try
        {
            // A Shoot card with no arrow to shoot does nothing at all (ArrowCmd.ShootArrowAtTarget returns before
            // its attack; the card glows red).
            if (Fool.IsShot(card) && card.Owner is { } owner && Fool.ArrowsAvailable(owner) == 0) return 0;
            switch (name)
            {
                // Nail attacks only an Impaled enemy (any player's arrow). Unknown target: it can hit if some enemy is.
                case "Nail":
                    return target != null ? (Fool.IsImpaled(target) ? 1 : 0) : (cs.HittableEnemies.Any(Fool.IsImpaled) ? 1 : 0);
                // Twice once you've played a Cho or a Ki this turn (WahBlast.HitsTwice: the card glows gold).
                case "WahBlast":
                    return Sts2State.Private(card, "ShouldGlowGoldInternal", false) ? 2 : 1;
                // v0.111.0 passed a fixed 2 to WithHitCount (Refract's Repeat is its orb count, not hits).
                case nameof(AstralPulse) or nameof(DaggerSpray) or nameof(Maul) or nameof(Refract) or nameof(RipAndTear) or nameof(Thrash) or nameof(TwinStrike) or nameof(Uproar):
                    return Hits(2);
                case nameof(Eradicate) or nameof(Skewer) or nameof(Volley) or nameof(Whirlwind):
                    return XValue(cs, card, energy);
                case nameof(HeavenlyDrill):
                {
                    var x = XValue(cs, card, energy);
                    return x >= Sts2State.Var(card, "Energy") ? x * 2 : x;
                }
                case nameof(Stardust):
                    return XValue(cs, card, stars);
                case nameof(Dismantle):
                    return target != null && target.HasPower<VulnerablePower>() ? Hits(2) : 1;
                case nameof(Spite): // its hit count only after you lost HP this turn (when the card glows gold)
                    return Sts2State.Private(card, "ShouldGlowGoldInternal", false) ? Hits(0) : 1;
                case nameof(FiendFire):
                    return otherCards;
                case nameof(MadScience):
                    return card is MadScience ms && ms.TinkerTimeRider == MegaCrit.Sts2.Core.Models.Events.TinkerTime.RiderEffect.Violence
                        ? Sts2State.Var(card, "ViolenceHits", 1)
                        : 1;
            }
            if (card.DynamicVars.TryGetValue("CalculatedHits", out var v) && v is CalculatedVar calc)
                return Math.Max(0, (int)calc.Calculate(target));
        }
        catch
        {
            // fall through to the hit count
        }
        return Math.Max(1, Hits(0));
    }

    private static int XValue(ICombatState cs, CardModel card, int spent)
    {
        try { return Math.Max(0, MegaCrit.Sts2.Core.Hooks.Hook.ModifyXValue(cs, card, spent)); } catch { return Math.Max(0, spent); }
    }

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
        nameof(Chaos) or nameof(ConsumingShadow) or nameof(Hibernate) or nameof(IceLance) or nameof(Refract) => Sts2State.Var(card, "Repeat", 1),
        nameof(Tempest) => me?.PlayerCombatState?.Energy ?? 1, // X lightning
        // One Lightning per Lightning channeled this combat (its calculation reads the history).
        nameof(Voltaic) => card.DynamicVars.TryGetValue("CalculatedChannels", out var vc) && vc is CalculatedVar calc ? (int)calc.Calculate(null) : 0,
        _ => 0,
    };

    private static int ChillChannels(CardModel card)
    {
        try { return card.CombatState is { } cs ? cs.HittableEnemies.Count : 1; } catch { return 1; }
    }

    // Cards that gain you stars when played (PlayerCmd.GainStars in OnPlay).
    internal static readonly HashSet<string> StarGainCards = new()
    {
        nameof(BigBang), nameof(GatherLight), nameof(Glow), nameof(HiddenCache), nameof(KnockoutBlow), nameof(RoyalGamble), nameof(ShiningStrike), nameof(SolarStrike), nameof(Venerate),
    };

    // Cards that make Osty attack.
    private static readonly HashSet<string> OstyAttackCards = new()
    {
        nameof(BoneShards), nameof(Flatten), nameof(HighFive), nameof(Poke), nameof(Protector), nameof(Rattle), nameof(RightHandHand), nameof(SicEm), nameof(Snap), nameof(Squeeze), nameof(SweepingGaze), nameof(Unleash),
    };

    // ------------------------------------------------------------------ generated cards

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
                var n = Math.Max(0, CardPile.MaxCardsInHand - others.Count);
                statuses = n; return n;
            }
            // Shivs.
            case nameof(BladeDance): case nameof(BladeOfInk): case nameof(CloakAndDagger): case nameof(UpMySleeve): return Var("Cards");
            case nameof(FanOfKnives): case nameof(HiddenDaggers): return Var("Shivs");
            case nameof(LeadingStrike): case nameof(BladeSymphony): return 1;
            case nameof(StormOfSteel): return others.Count;
            // Souls.
            case nameof(CaptureSpirit): case nameof(GlimpseBeyond): case nameof(GraveWarden): case nameof(Reave): return Var("Cards");
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
                var mates = me?.Creature?.CombatState?.Players.Count(p => p != me && p.Creature is { IsAlive: true }) ?? 0;
                statuses = Var("Sadness", 1);
                return statuses + Var("Joy", 2) * mates;
            }
            case "TheHierophant": return Var("Cards", 3);
            case "TheLovers": return Math.Max(0, CardPile.MaxCardsInHand - others.Count);
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
            var hasBlade = me.PlayerCombatState?.AllCards.Any(c => c is SovereignBlade && !c.IsDupe && c.Pile?.Type != PileType.Exhaust) ?? true;
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
            try { return others.Count(c => c.IsTransformable && filter(c)); } catch { return others.Count(filter); }
        }
        return name switch
        {
            nameof(Begone) => Math.Min(1, Transformable(_ => true)),                        // 1 card in hand you pick
            nameof(Guards) => Transformable(_ => true),                                     // any cards in hand you pick
            nameof(Charge) => Math.Min(Var("Cards", 2), drawPile),                          // cards from your draw pile
            nameof(Seance) => Math.Min(Var("Cards", 1), drawPile),                          // into Souls
            nameof(Compact) => Transformable(c => c.Type == CardType.Status),               // every status in hand
            nameof(PrimalForce) => Transformable(c => c.Type == CardType.Attack),           // every attack in hand
            _ => 0,
        };
    }

    // ------------------------------------------------------------------ debuffs

    private static readonly Dictionary<Type, bool> DebuffCache = new();

    private static IReadOnlyList<Type> EnemyDebuffTypes(CardModel card)
    {
        if (card.TargetType is not (TargetType.AnyEnemy or TargetType.AllEnemies or TargetType.RandomEnemy)) return Array.Empty<Type>();
        List<Type>? list = null;
        foreach (var v in card.DynamicVars.Values)
        {
            var t = v.GetType();
            if (!t.IsGenericType || t.GetGenericTypeDefinition() != typeof(PowerVar<>)) continue;
            if (v.BaseValue <= 0) continue;
            var powerType = t.GetGenericArguments()[0];
            if (!IsDebuff(powerType)) continue;
            (list ??= new()).Add(powerType);
        }
        // Debuffs its code applies with a plain value (Uppercut, Putrefy, Shockwave use "Power").
        foreach (var applied in Diagnostics.CardCoverage.PowersApplied(card.GetType()))
            if (IsDebuff(applied) && (list == null || !list.Contains(applied))) (list ??= new()).Add(applied);
        return list ?? (IReadOnlyList<Type>)Array.Empty<Type>();
    }

    public static bool IsDebuff(Type powerType) => !typeof(ITemporaryPower).IsAssignableFrom(powerType) && IsDebuffType(powerType);

    /// <summary>
    /// The power is a debuff by type (PowerModel.Type), temporary ones included (Dark Shackles):
    /// what Artifact blocks (ArtifactPower.TryModifyPowerAmountReceived).
    /// </summary>
    public static bool IsDebuffType(Type powerType)
    {
        if (DebuffCache.TryGetValue(powerType, out var d)) return d;
        d = false;
        try
        {
            var canonical = typeof(ModelDb).GetMethod(nameof(ModelDb.Power))!.MakeGenericMethod(powerType).Invoke(null, null) as PowerModel;
            d = canonical?.Type == PowerType.Debuff;
        }
        catch
        {
            // unknown: not a debuff
        }
        DebuffCache[powerType] = d;
        return d;
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
        int Landing(Creature e)
        {
            var n = perPlay * Plays;
            var artifact = e.Powers.OfType<ArtifactPower>().Sum(p => p.Amount);
            return Math.Max(0, n - artifact);
        }
        return HitsAllEnemies ? enemies.Sum(Landing) : enemies.Max(Landing);
    }
}

/// <summary>The outcome simulator's inputs for one card on one frame (see PlayFacts.Sim).</summary>
public sealed class SimInputs
{
    public required SimState Start { get; init; }
    public required IReadOnlyDictionary<Creature, int> Ids { get; init; }
    public Dictionary<int, SimPlay> PlayByTarget { get; } = new();
}
