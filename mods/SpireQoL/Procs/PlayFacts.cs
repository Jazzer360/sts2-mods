using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
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

    public int TotalExhausts => (ExhaustsSelf ? 1 : 0) + ExhaustsOthers;

    // ------------------------------------------------------------------ building

    public static PlayFacts For(CardModel card, Player me, ICombatState cs, int plays)
    {
        var hand = Sts2State.Hand(me);
        var others = hand.Where(c => !ReferenceEquals(c, card)).ToList();
        var draws = CardsDrawn(card, hand.Count) * Math.Max(1, plays);
        var drawPile = Sts2State.DrawPileCount(me);
        var name = card.GetType().Name;

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
        };
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
