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

    /// <summary>Stars spent to play it (0 if it has no star cost).</summary>
    public int Stars { get; init; }

    /// <summary>The card itself exhausts after it's played (Exhaust keyword, including added Exhaust).</summary>
    public bool ExhaustsSelf { get; init; }

    /// <summary>Other cards it exhausts (True Grit, Fiend Fire, Second Wind...).</summary>
    public int ExhaustsOthers { get; init; }

    /// <summary>Cards it discards (Prepared, Survivor, Storm of Steel...).</summary>
    public int Discards { get; init; }

    /// <summary>Cards it draws.</summary>
    public int Draws { get; init; }

    /// <summary>Its draw runs past the draw pile, so the discard pile is shuffled in mid-play.</summary>
    public bool Shuffles { get; init; }

    /// <summary>Separate times it gains you block (most block cards: 1; Second Wind: per card exhausted).</summary>
    public int BlockGains { get; init; }

    /// <summary>Cards it creates (Shivs, Souls, clones, statuses...), and how many of those are statuses.</summary>
    public int Generated { get; init; }
    public int GeneratedStatuses { get; init; }

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
    public int DamagePerHit(Creature enemy)
    {
        if (AttackHits == 0) return 0;
        try
        {
            decimal raw;
            if (Card.DynamicVars.TryGetValue("CalculatedDamage", out var cv) && cv is CalculatedVar calc) raw = calc.Calculate(enemy);
            else if (Card.DynamicVars.TryGetValue("Damage", out var dv)) raw = dv.BaseValue;
            else return 0;
            var v = MegaCrit.Sts2.Core.Hooks.Hook.ModifyDamage(Me.RunState, Combat, enemy, Me.Creature, raw, MegaCrit.Sts2.Core.ValueProps.ValueProp.Move,
                Card, null, MegaCrit.Sts2.Core.Hooks.ModifyDamageHookType.All, CardPreviewMode.None, out _);
            return Math.Max(0, (int)v);
        }
        catch
        {
            return 0;
        }
    }

    /// <summary>Total damage this card deals to the enemy across all its hits (before block).</summary>
    public int TotalDamage(Creature enemy) => DamagePerHit(enemy) * HitsOn(enemy);

    /// <summary>Damage that gets past the enemy's current block.</summary>
    public int Unblocked(Creature enemy) => Math.Max(0, TotalDamage(enemy) - enemy.Block);

    /// <summary>Breaks the enemy's block (it has some, and the attack deals at least that much).</summary>
    public bool BreaksBlock(Creature enemy) => CanHit(enemy) && enemy.Block > 0 && TotalDamage(enemy) >= enemy.Block;

    /// <summary>Kills the enemy (damage ≥ block + HP; ignores per-turn caps like Hardened Shell).</summary>
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

    /// <summary>Block you gain from Frost orbs this play evokes (EvokeVal, Unpowered).</summary>
    public int OrbBlock { get; init; }

    /// <summary>Block gains from those Frost evokes (each sets off Juggernaut).</summary>
    public int OrbBlockGains { get; init; }

    /// <summary>
    /// The play's outcome depends on a roll with more than one possible target: Lightning evokes or
    /// a random-target attack with several enemies, or (set by Proc Indicators) a random-target proc.
    /// The audit doesn't report what a random kill or hit could have set off.
    /// </summary>
    public bool RandomOutcome { get; set; }

    /// <summary>Damage that gets past the enemy's block, counting orbs this play evokes.</summary>
    public int UnblockedWithOrbs(Creature enemy)
        => Math.Max(0, (CanHit(enemy) ? TotalDamage(enemy) : 0) + OrbDamageOn(enemy) - enemy.Block);

    public int OrbDamageOn(Creature enemy) => OrbDamage.TryGetValue(enemy, out var d) ? d : 0;

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

        return new PlayFacts
        {
            Card = card,
            Me = me,
            Combat = cs,
            Plays = Math.Max(1, plays),
            Energy = SafeEnergy(card),
            Stars = SafeStars(card),
            // Exhaust keyword (including added Exhaust), or Corruption sending a played skill to the
            // exhaust pile (CorruptionPower.ModifyCardPlayResultLocation).
            ExhaustsSelf = card.Type != CardType.Power
                           && (card.Keywords.Contains(CardKeyword.Exhaust)
                               || (card.Type == CardType.Skill && me.Creature.Powers.Any(p => p is CorruptionPower))),
            ExhaustsOthers = ExhaustOthers(card, name, others, drawPile, plays),
            Discards = DiscardCount(card, name, others, draws),
            Draws = draws,
            Shuffles = draws > drawPile && Sts2State.DiscardPileCount(me) > 0,
            BlockGains = BlockGainCount(card, name, others) * Math.Max(1, plays),
            Generated = GeneratedCount(card, name, others, me, out var statuses) * Math.Max(1, plays),
            GeneratedStatuses = statuses * Math.Max(1, plays),
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
            OrbBlock = OrbBlockFor(evoked, me, cs),
            OrbBlockGains = evoked.Count(o => o is FrostOrb && o.EvokeVal > 0),
            RandomOutcome = cs.HittableEnemies.Count(e => e.IsAlive) > 1
                            && (evoked.Any(o => o is LightningOrb) || card.TargetType == TargetType.RandomEnemy),
        };
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
                case "Dualcast": EvokeFront(2, true); break;
                case "Quadcast": EvokeFront(Math.Max(1, Sts2State.Var(card, "Repeat", 4)), true); break;
                case "MultiCast": EvokeFront(XValue(cs, card, SafeEnergy(card)) + (card.IsUpgraded ? 1 : 0), true); break;
                case "Shatter":
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
        "Acrobatics", "Adrenaline", "Backflip", "BattleTrance", "BigBang", "BrightestFlame", "BurningPact",
        "Constellation", "Coolheaded", "DecisionsDecisions", "DrumOfBattle", "Expertise", "Fetch", "Finesse",
        "FlashOfSteel", "Ftl", "Glimmer", "Glow", "HuddleUp", "Impatience", "MasterOfStrategy", "MinionStrike",
        "Neurosurge", "Offering", "Overclock", "Parse", "PhotonCut", "PommelStrike", "Prepared", "Prophesize",
        "Reboot", "Reflex", "RocketPunch", "Scourge", "Scrape", "ShrugItOff", "Skim", "Slimed", "Soul",
        "SpoilsOfBattle", "SweepingBeam", "ThinkingAhead",
    };

    // Cards that always draw exactly one (Pillage draws at least one).
    private static readonly HashSet<string> DrawOne = new() { "DaggerThrow", "EscapePlan", "Pillage" };

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
            // Draws one per distinct orb type channeled.
            case CompileDriver when c.DynamicVars.TryGetValue("CalculatedCards", out var v) && v is CalculatedVar cv:
                return (int)cv.Calculate(null);
            // Only draws when it's the only card in hand.
            case Restlessness: return handCount == 1 ? Sts2State.Var(c, "Cards") : 0;
            // Mad Science with the Wisdom rider (Tinker Time).
            case MadScience m when m.TinkerTimeRider.ToString() == "Wisdom": return Sts2State.Var(c, "WisdomCards");
        }
        return 0;
    }

    // ------------------------------------------------------------------ exhaust / discard

    private static int ExhaustOthers(CardModel card, string name, List<CardModel> others, int drawPile, int plays)
    {
        var n = others.Count;
        var p = Math.Max(1, plays);
        return name switch
        {
            // One chosen or random card from hand, per play.
            "Brand" or "BurningPact" or "TrueGrit" or "Scavenge" or "Cinder" => Math.Min(p, n),
            "Thrash" => Math.Min(p, others.Count(c => c.Type == CardType.Attack)),
            "Cleanse" => Math.Min(p, drawPile), // from the draw pile
            // The whole rest of the hand (a second play finds the hand empty).
            "FiendFire" or "Stoke" => n,
            "SecondWind" => others.Count(c => c.Type != CardType.Attack),
            // Up to Cards chosen cards: counted as the most it can exhaust.
            "Purity" => Math.Min(n, Sts2State.Var(card, "Cards") * p),
            // Every status card in your piles.
            "FlakCannon" => card.DynamicVars.TryGetValue("CalculatedHits", out var v) && v is CalculatedVar cv ? (int)cv.Calculate(null) : 0,
            _ => 0,
        };
    }

    private static int DiscardCount(CardModel card, string name, List<CardModel> others, int draws)
    {
        var n = others.Count;
        return name switch
        {
            // Discard after drawing, so the drawn cards are there to discard.
            "Acrobatics" or "DaggerThrow" => Math.Min(1, n + draws),
            "Prepared" => Math.Min(Sts2State.Var(card, "Cards"), n + draws),
            "Survivor" => Math.Min(1, n),
            "HiddenDaggers" => Math.Min(Sts2State.Var(card, "Cards"), n),
            "CalculatedGamble" or "ShadowStep" or "StormOfSteel" => n,
            _ => 0,
        };
    }

    // ------------------------------------------------------------------ block

    private static int BlockGainCount(CardModel card, string name, List<CardModel> others)
    {
        if (name == "SecondWind") return others.Count(c => c.Type != CardType.Attack);
        return card.DynamicVars.ContainsKey("Block") || card.DynamicVars.ContainsKey("CalculatedBlock") ? 1 : 0;
    }

    /// <summary>An attack you deal yourself (a Damage value; Osty attacks use OstyDamage instead).</summary>
    /// <summary>
    /// Hits per play of an attack card, matching each card's OnPlay (decompile v0.111.0): cards
    /// that pass a fixed number, their X value or a calculation to WithHitCount instead of
    /// using their Repeat value. Unknown target (null): target-dependent cards count their minimum.
    /// </summary>
    internal static int HitsPerPlay(CardModel card, string name, ICombatState cs, Creature? target, int energy, int stars, int otherCards)
    {
        try
        {
            switch (name)
            {
                case "AstralPulse" or "DaggerSpray" or "Maul" or "Refract" or "RipAndTear" or "Thrash" or "TwinStrike" or "Uproar":
                    return 2;
                case "Eradicate" or "Skewer" or "Volley" or "Whirlwind":
                    return XValue(cs, card, energy);
                case "HeavenlyDrill":
                {
                    var x = XValue(cs, card, energy);
                    return x >= Sts2State.Var(card, "Energy") ? x * 2 : x;
                }
                case "Stardust":
                    return XValue(cs, card, stars);
                case "Dismantle":
                    return target != null && target.HasPower<VulnerablePower>() ? 2 : 1;
                case "Spite": // the Repeat value only after you lost HP this turn (when the card glows gold)
                    return Sts2State.Private(card, "ShouldGlowGoldInternal", false) ? Sts2State.Var(card, "Repeat", 2) : 1;
                case "FiendFire":
                    return otherCards;
                case "MadScience":
                    return card is MadScience ms && ms.TinkerTimeRider == MegaCrit.Sts2.Core.Models.Events.TinkerTime.RiderEffect.Violence
                        ? Sts2State.Var(card, "ViolenceHits", 1)
                        : 1;
            }
            if (card.DynamicVars.TryGetValue("CalculatedHits", out var v) && v is CalculatedVar calc)
                return Math.Max(0, (int)calc.Calculate(target));
        }
        catch
        {
            // fall through to the Repeat value
        }
        return Math.Max(1, Sts2State.Var(card, "Repeat", 1));
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

    private static int ChannelCount(CardModel card, string name, Player me) => name switch
    {
        "BallLightning" or "Chill" or "ColdSnap" or "Coolheaded" or "Fusion" or "Glasswork" or "Ignition"
            or "Null" or "ShadowShield" or "Spinner" or "Zap" or "Darkness" => 1,
        "Glacier" => 2,
        "MeteorStrike" or "Rainbow" => 3,
        "Chaos" or "ConsumingShadow" or "Hibernate" or "IceLance" or "Refract" => Sts2State.Var(card, "Repeat", 1),
        "Tempest" => me.PlayerCombatState?.Energy ?? 0, // X lightning
        _ => 0,
    };

    // Cards that gain you stars when played (PlayerCmd.GainStars in OnPlay).
    private static readonly HashSet<string> StarGainCards = new()
    {
        "BigBang", "GatherLight", "Glow", "HiddenCache", "KnockoutBlow", "RoyalGamble", "ShiningStrike", "SolarStrike", "Venerate",
    };

    // Cards that make Osty attack.
    private static readonly HashSet<string> OstyAttackCards = new()
    {
        "BoneShards", "Flatten", "HighFive", "Poke", "Protector", "Rattle", "RightHandHand", "SicEm", "Snap", "Squeeze", "SweepingGaze", "Unleash",
    };

    // ------------------------------------------------------------------ generated cards

    private static int GeneratedCount(CardModel card, string name, List<CardModel> others, Player me, out int statuses)
    {
        statuses = 0;
        int Var(string key, int fallback = 1) => Sts2State.Var(card, key, fallback);
        switch (name)
        {
            // Statuses.
            case "BoostAway": case "CollisionCourse": case "GunkUp": case "Overclock": case "Turbo":
                statuses = 1; return 1;
            case "FightThrough":
                statuses = 2; return 2;
            case "CrashLanding":
            {
                var n = Math.Max(0, CardPile.MaxCardsInHand - others.Count);
                statuses = n; return n;
            }
            // Shivs.
            case "BladeDance": case "BladeOfInk": case "CloakAndDagger": case "UpMySleeve": return Var("Cards");
            case "FanOfKnives": case "HiddenDaggers": return Var("Shivs");
            case "LeadingStrike": case "BladeSymphony": return 1;
            case "StormOfSteel": return others.Count;
            // Souls.
            case "CaptureSpirit": case "GlimpseBeyond": case "GraveWarden": case "Reave": return Var("Cards");
            case "Severance": return 3;
            case "Dirge": return me.PlayerCombatState?.Energy ?? 0; // X souls
            // Other cards.
            case "BundleOfJoy": case "JackOfAllTrades": case "Jackpot": case "Metamorphosis": case "DualWield": return Var("Cards");
            case "HeirloomHammer": return Var("Repeat");
            case "Stoke": return others.Count;
            case "Abundance": case "AdaptiveStrike": case "Anger": case "Discovery": case "Distraction":
            case "InfernalBlade": case "Largesse": case "ManifestAuthority": case "Outrage": case "Quasar":
            case "Splash": case "Undeath": case "WhiteNoise":
                return 1;
        }
        return 0;
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
        return list ?? (IReadOnlyList<Type>)Array.Empty<Type>();
    }

    public static bool IsDebuff(Type powerType)
    {
        if (DebuffCache.TryGetValue(powerType, out var d)) return d;
        d = false;
        try
        {
            var canonical = typeof(ModelDb).GetMethod(nameof(ModelDb.Power))!.MakeGenericMethod(powerType).Invoke(null, null) as PowerModel;
            d = canonical?.Type == PowerType.Debuff && !typeof(ITemporaryPower).IsAssignableFrom(powerType);
        }
        catch
        {
            // unknown: not a debuff
        }
        DebuffCache[powerType] = d;
        return d;
    }

    /// <summary>Debuff applications that actually land on an enemy: Artifact cancels one each.</summary>
    public int DebuffsLanding(Creature enemy)
    {
        var n = EnemyDebuffs.Count * Plays;
        if (n == 0) return 0;
        var artifact = enemy.Powers.OfType<ArtifactPower>().Sum(p => p.Amount);
        return Math.Max(0, n - artifact);
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
