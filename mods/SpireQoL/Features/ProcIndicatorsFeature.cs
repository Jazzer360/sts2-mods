using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Relics;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Afflictions;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.ValueProps;
using SpireQoL.Cards;
using SpireQoL.Combat;
using SpireQoL.Procs;
using Sts2Mods.Core.Features;
using Sts2Mods.Core.Game;
using Sts2Mods.Core.Ui;

namespace SpireQoL.Features;

/// <summary>
/// "Does something happen the moment I play this card that the card doesn't say?" One tab per
/// power or relic that playing the card would set off, with that source's icon. Spec:
/// docs/FEATURES.md#proc-indicators. (Relic Reminders were merged in here in 0.21.0.)
///
///  - Trigger: plain icon ("this sets off that").
///  - Block / damage / summon: icon plus the amount (QolSettings.ProcNumbers).
///  - Harmful (helps the enemy, hurts you, or breaks one of your relics): crossed.
///
/// Every rule mirrors the source's own hook in the decompile (v0.111.0). Facts about the card
/// (play count, cost, exhausts, discards, draws, debuffs, created cards, how hard it hits each
/// enemy) come from PlayFacts. Each damage tab also records the exact damage range it predicts
/// (per enemy, with each enemy's own modifiers) so ProcAudit can check it against real plays.
/// </summary>
public sealed class ProcIndicatorsFeature : IFeature, IFrameFeature
{
    private readonly QolContext _ctx;

    public ProcIndicatorsFeature(QolContext ctx) => _ctx = ctx;

    public string Id => "proc-indicators";
    public string DisplayName => "Proc Indicators";
    public bool EnabledByDefault => true;

    public void Apply(Harmony harmony)
    {
        // Frame-driven; no patches.
    }

    public void Attach(OverlayHost host)
    {
    }

    private enum Kind { Trigger, Block, Damage, Summon }

    /// <summary>Who a damage proc hits.</summary>
    private enum Spread { All, Random, Specific, Me }

    /// <param name="Count">Separate instances (block gains for Juggernaut).</param>
    /// <param name="Min">Damage procs: least total damage actually dealt (all targets, their modifiers).</param>
    /// <param name="Max">Damage procs: most total damage actually dealt.</param>
    private readonly record struct Hit(Kind Kind, int Amount, bool Harmful = false, int Count = 1, int Min = 0, int Max = 0,
        IReadOnlyDictionary<Creature, List<int>>? OnEnemies = null, bool Random = false)
    {
        public static Hit Trig(bool harmful = false) => new(Kind.Trigger, 0, harmful);
    }

    private readonly HashSet<string> _warned = new();

    // Procs that other procs can set off; evaluated in a second pass with these extras.
    // Waking reads the first pass's proc damage on each enemy (Kusarigama etc.), so it runs second.
    private static readonly HashSet<string> SecondOrder = new() { "JuggernautPower", "Metronome", "BlackHolePower", "AsleepPower", "SlumberPower" };
    private int _xBlockGains, _xChannels, _xStarGains;
    private readonly Dictionary<Creature, List<int>> _xEnemyHits = new();

    private static bool CanEverPlayNow(CardModel card)
    {
        try
        {
            if (card.CanPlay(out var reason, out _)) return true;
            const UnplayableReason never = UnplayableReason.HasUnplayableKeyword | UnplayableReason.BlockedByHook
                                           | UnplayableReason.BlockedByCardLogic | UnplayableReason.NoLivingAllies;
            return (reason & never) == 0;
        }
        catch
        {
            return true;
        }
    }

    // Per-frame context.
    private Player _me = null!;
    private ICombatState _cs = null!;
    private bool _indirect;

    /// <summary>Each hand card's PlayFacts on the previous frame (what ProcAudit expects a play to do).</summary>
    /// <summary>Each card's facts from the last frame it was in hand (kept after it leaves; ProcAudit).</summary>
    public static IReadOnlyDictionary<CardModel, PlayFacts> LastFacts => _lastFacts;

    private static readonly Dictionary<CardModel, PlayFacts> _lastFacts = new(ReferenceEqualityComparer.Instance);

    public static IReadOnlyDictionary<CardModel, PlayFacts> PreviousFacts { get; private set; }
        = new Dictionary<CardModel, PlayFacts>(ReferenceEqualityComparer.Instance);

    private Dictionary<CardModel, PlayFacts> _facts = new(ReferenceEqualityComparer.Instance);

    public void OnFrame(OverlayHost host)
    {
        // Keep last frame's facts for the audit, even on frames where nothing is computed (the
        // hand just emptied, the turn is ending).
        if (_facts.Count > 0 || PreviousFacts.Count > 0)
        {
            PreviousFacts = _facts;
            _facts = new Dictionary<CardModel, PlayFacts>(ReferenceEqualityComparer.Instance);
        }

        var me = Sts2State.Me;
        var cs = Sts2State.Combat;
        if (me == null || cs == null || !Sts2State.IsPlayerTurn) return;
        var hand = Sts2State.Hand(me);
        if (hand.Count == 0) return;

        _me = me;
        _cs = cs;
        _indirect = _ctx.Settings.ProcIndirect;

        // Sources: your powers, your relics, and powers on enemies (theirs, or ones you applied).
        var sources = new List<AbstractModel>();
        sources.AddRange(me.Creature.Powers);
        sources.AddRange(me.Relics); // each relic's tab has its own switch (Tabs "proc:<Relic>")
        // All living enemies: an untargetable one's powers still fire.
        foreach (var enemy in cs.Enemies.Where(e => e.IsAlive)) sources.AddRange(enemy.Powers);

        foreach (var card in hand)
        {
            _ctx.Decor.SeenInHand(card);
            // Nothing happens on play for a card that can't be played (Unplayable statuses and
            // curses, blocked cards, cards whose own condition isn't met). Cards you just can't
            // afford yet still show, since energy can come mid-turn.
            if (!CanEverPlayNow(card)) continue;

            PlayFacts f;
            try
            {
                f = PlayFacts.For(card, me, cs, CardIndicatorsFeature.PlayCount(card));
            }
            catch (Exception ex)
            {
                if (_warned.Add(card.GetType().Name))
                {
                    ModEntry.Log.Warn($"proc facts failed for {card.GetType().Name}: {ex.Message}");
                    Sts2Mods.Core.Diagnostics.ErrorReporter.Instance?.Exception(ex, "play-facts:" + card.GetType().Name);
                }
                continue;
            }

            _facts[card] = f;
            _lastFacts[card] = f;
            if (_lastFacts.Count > 300) _lastFacts.Clear();
            var d = _ctx.Decor.For(card);
            // Several enemies can carry the same power: one tab, amounts summed.
            var merged = new Dictionary<string, (AbstractModel Src, Hit Hit)>();
            void Run(bool secondOrder)
            {
                foreach (var src in sources)
                {
                    if (SecondOrder.Contains(src.GetType().Name) != secondOrder) continue;
                    Hit? hit;
                    try { hit = Evaluate(src, f); }
                    catch { hit = null; }
                    if (hit is not { } h) continue;
                    if (h.Kind != Kind.Trigger && h.Amount <= 0) continue;
                    var key = "proc:" + src.GetType().Name;
                    merged[key] = merged.TryGetValue(key, out var prev)
                        ? (prev.Src, prev.Hit with
                        {
                            Amount = prev.Hit.Amount + h.Amount, Count = prev.Hit.Count + h.Count,
                            Min = prev.Hit.Min + h.Min, Max = prev.Hit.Max + h.Max,
                            OnEnemies = MergeHits(prev.Hit.OnEnemies, h.OnEnemies),
                            Random = prev.Hit.Random || h.Random,
                        })
                        : (src, h);
                }
            }

            // First-order procs, then the procs they set off (procs of procs): block gains feed
            // Juggernaut, Storm's lightning feeds Metronome, The Sealed Throne's stars feed Black Hole.
            _xBlockGains = _xChannels = _xStarGains = 0;
            _xEnemyHits.Clear();
            Run(secondOrder: false);
            _xBlockGains = merged.Values.Where(m => m.Hit.Kind == Kind.Block).Sum(m => m.Hit.Count);
            foreach (var (_, h) in merged.Values)
            {
                if (h.Kind != Kind.Damage || h.Harmful || h.OnEnemies == null) continue;
                foreach (var (e, hits) in h.OnEnemies)
                {
                    if (!_xEnemyHits.TryGetValue(e, out var l)) _xEnemyHits[e] = l = new List<int>();
                    l.AddRange(hits);
                }
            }
            foreach (var (src, h) in merged.Values)
            {
                if (src is PowerModel { } sp && sp.GetType().Name == "StormPower") _xChannels += sp.Amount * f.Plays;
                if (src is PowerModel { } tp && tp.GetType().Name == "TheSealedThronePower" && tp.Amount > 0) _xStarGains += f.Plays;
            }
            Run(secondOrder: true);

            if (merged.Values.Any(m => m.Hit.Random)) f.RandomOutcome = true;

            foreach (var (key, (src, h)) in merged)
            {
                var icon = src switch
                {
                    RelicModel r => GameIcons.Relic(r),
                    PowerModel p => GameIcons.Power(p),
                    _ => null,
                };
                int? number = h.Kind != Kind.Trigger && _ctx.Settings.ProcNumbers ? h.Amount : null;
                var kind = h.Kind switch { Kind.Block => "block", Kind.Damage => "damage", Kind.Summon => "summon", _ => null };
                d.AddTab(key, icon, crossed: h.Harmful, number: number, kind: kind, amount: h.Amount, auditMin: h.Min, auditMax: h.Max);
            }
        }
    }

    // ------------------------------------------------------------------ rules

    private Hit? Evaluate(AbstractModel src, PlayFacts f)
    {
        var card = f.Card;
        var type = card.Type;
        var plays = f.Plays;
        var exhausts = (f.ExhaustsSelf ? 1 : 0) + (_indirect ? f.ExhaustsOthers : 0);
        var discards = _indirect ? f.Discards : 0;
        var draws = _indirect ? f.Draws : 0;
        var shuffles = _indirect && f.Shuffles;
        var generated = _indirect ? f.Generated : 0;
        var statuses = _indirect ? f.GeneratedStatuses : 0;

        switch (src)
        {
            case PowerModel p:
            {
                var a = p.Amount;
                var mine = p.Owner == _me.Creature;
                var onEnemy = p.Owner.IsEnemy;
                var e = p.Owner; // for powers on an enemy: that enemy
                switch (p.GetType().Name)
                {
                    // ---- your powers: on play
                    case "AfterimagePower" when mine: return BlockN(a, plays);
                    case "RagePower" when mine && type == CardType.Attack: return BlockN(a, plays);
                    case "SpiritOfAshPower" when mine && card.Keywords.Contains(CardKeyword.Ethereal): return BlockN(a, plays);
                    case "DanseMacabrePower" when mine && f.Energy >= Sts2State.Var(p, "Energy", 2): return BlockN(a, plays);
                    case "GravityPower" when mine: return Damage(a, plays, Spread.All);
                    case "SerpentFormPower" when mine: return Damage(a, plays, Spread.Random);
                    case "PanachePower" when mine && plays >= Sts2State.Var(p, "CardsLeft", 5): return Damage(a, 1, Spread.All);
                    // Second order: stars gained also set off Black Hole (The Sealed Throne gains
                    // stars on every card).
                    case "BlackHolePower" when mine:
                    {
                        var times = (f.Stars > 0 ? 1 : 0) + f.StarGains + _xStarGains;
                        return times > 0 ? Damage(a, times, Spread.All) : null;
                    }
                    case "RupturePower" when mine && f.LosesHp: return Hit.Trig();
                    case "DevourLifePower" when mine && card is Soul: return new Hit(Kind.Summon, a * plays);
                    case "HauntPower" when mine && card is Soul: return Damage(a, plays, Spread.Random, props: ValueProp.Unblockable | ValueProp.Unpowered);
                    case "StormPower" when mine && type == CardType.Power: return Hit.Trig();
                    case "SubroutinePower" when mine && type == CardType.Power: return Hit.Trig();
                    case "CalamityPower" when mine && type == CardType.Attack: return Hit.Trig();
                    case "JugglingPower" when mine && type == CardType.Attack:
                    {
                        var attacks = Sts2State.CountPlayedThisTurn(_me, CardType.Attack);
                        return attacks < 3 && attacks + plays >= 3 ? Hit.Trig() : null;
                    }
                    case "MonologuePower" when mine: return Hit.Trig();
                    case "MasterPlannerPower" when mine && type == CardType.Skill: return Hit.Trig();
                    // An Attack that spends 0 energy returns to your hand while Feral has charges left
                    // this turn (DisplayAmount; FeralPower.ModifyCardPlayResultLocation). Not copies.
                    case "FeralPower" when mine && type == CardType.Attack && f.Energy == 0 && !card.IsDupe && p.DisplayAmount > 0:
                        return Hit.Trig();
                    case "TheSealedThronePower" when mine: return Hit.Trig();
                    case "TenderPower" when mine: return Hit.Trig(harmful: true);
                    case "SmoggyPower" when mine && type == CardType.Skill: return Hit.Trig(harmful: true);
                    case "PaleBlueDotPower" when mine:
                    {
                        if (Sts2State.Private(p, "alreadyActivatedThisTurn", false)) return null;
                        var played = Sts2State.CountPlayedThisTurn(_me);
                        return played < 5 && played + plays >= 5 ? Hit.Trig() : null;
                    }

                    // ---- Sovereign Blade's buffs (the card text lists them, but it's easy to miss)
                    case "SeekingEdgePower" when mine && card is SovereignBlade: return Hit.Trig(); // hits all enemies
                    case "ParryPower" when mine && card is SovereignBlade: return Hit.Trig();       // gains block
                    case "SwordSagePower" when mine && card is SovereignBlade: return Hit.Trig();   // replays
                    case "ConquerorPower" when onEnemy && card is SovereignBlade: return Hit.Trig(); // more damage to that enemy

                    // ---- your powers: exhaust / discard / draw / stars / energy / debuffs / created cards
                    case "FeelNoPainPower" when mine && exhausts > 0: return BlockN(a, exhausts);
                    case "DarkEmbracePower" when mine && exhausts > 0: return Hit.Trig();
                    case "ChildOfTheStarsPower" when mine && f.Stars > 0: return BlockN(a * f.Stars, 1);
                    case "OrbitPower" when mine && f.Energy > 0 && f.Energy >= p.DisplayAmount: return Hit.Trig();
                    case "AutomationPower" when mine && draws > 0 && draws >= p.DisplayAmount: return Hit.Trig();
                    case "CacophonyPower" when mine && draws > 0 && draws >= p.DisplayAmount: return Damage(a, 1, Spread.Random);
                    case "SpeedsterPower" when mine && draws > 0: return Damage(a, draws, Spread.All);
                    case "CorrosiveWavePower" when mine && draws > 0: return Hit.Trig();
                    case "StratagemPower" when mine && shuffles: return Hit.Trig();
                    case "SleightOfFleshPower" when mine && f.DebuffsLandingTotal() > 0:
                        return f.HitsAllEnemies
                            ? Damage(a, Math.Max(1, f.DebuffsLandingTotal() / Math.Max(1, _cs.HittableEnemies.Count)), Spread.All)
                            : Damage(a, f.DebuffsLandingTotal(), Spread.Random);
                    case "ViciousPower" when mine && f.DebuffsLandingTotal(t => t == typeof(VulnerablePower)) > 0: return Hit.Trig();
                    case "ShroudPower" when mine: return BlockN(a, f.DebuffsLandingTotal(t => t == typeof(DoomPower)));
                    // Every block gain sets it off, including block from other procs (Afterimage...).
                    // Any block gain, including Frost orbs the card evokes.
                    case "JuggernautPower" when mine && f.BlockGains + f.OrbBlockGains + _xBlockGains > 0:
                        return Damage(a, f.BlockGains + f.OrbBlockGains + _xBlockGains, Spread.Random);
                    case "EnvenomPower" when mine && type == CardType.Attack: return Hit.Trig();
                    case "ConcoctPower" when mine && type == CardType.Attack: return Hit.Trig();
                    case "ReaperFormPower" when mine && type == CardType.Attack: return Hit.Trig();
                    case "MonarchsGazePower" when mine && type == CardType.Attack: return Hit.Trig();
                    case "ArsenalPower" when mine && generated > 0: return Hit.Trig();
                    case "PillarOfCreationPower" when mine && generated > 0: return BlockN(a, generated);
                    case "SmokestackPower" when mine && statuses > 0: return Damage(a, statuses, Spread.All);
                    case "TrashToTreasurePower" when mine && statuses > 0: return Hit.Trig();

                    // ---- powers on enemies: your plays
                    case "EnragePower" when onEnemy && type == CardType.Skill: return Hit.Trig(harmful: true);
                    case "SneakyPower" when onEnemy && type == CardType.Attack: return Hit.Trig(harmful: true);
                    case "ImitationLearningPower" when onEnemy && type == CardType.Power: return Hit.Trig(harmful: true);
                    // Fully blocking its hit stuns it (Bowlbug: knocks it off balance). Plain tab on the
                    // card whose own block takes your end-of-turn block over that line.
                    case "ImbalancedPower" when onEnemy && CrossesFullBlock(e, card, f): return Hit.Trig();
                    case "SkittishPower" when onEnemy && type == CardType.Attack && !Sts2State.Private(p, "HasGainedBlockThisTurn", true):
                        return Hit.Trig(harmful: true);
                    case "WitheringPresencePower" when onEnemy && plays >= p.DisplayAmount: return Hit.Trig(harmful: true);
                    case "GalvanicPower" when card.Affliction is Galvanized:
                        return Damage(a, plays, Spread.Me, props: ValueProp.Unpowered | ValueProp.Move, harmful: true);
                    case "VitalSparkPower" when card.Affliction is Tainted: return Hit.Trig(harmful: true);
                    case "StranglePower" when onEnemy && p.Applier?.Player == _me:
                        return Damage(a, plays, Spread.Specific, target: e, props: ValueProp.Unblockable | ValueProp.Unpowered);
                    case "OblivionPower" when onEnemy && p.Applier?.Player == _me: return Hit.Trig();
                    case "SlowPower" when onEnemy: return Hit.Trig();

                    // ---- powers on enemies: being hit by your attack
                    case "ThornsPower" when onEnemy && f.CanHit(e):
                    {
                        var thorns = Damage(a, f.HitsOn(e), Spread.Me, dealer: e, harmful: true);
                        // A single-target or random attack might hit an enemy without Thorns instead.
                        var mayMiss = !f.HitsAllEnemies && _cs.HittableEnemies.Any(o => o != e && !o.HasPower<ThornsPower>());
                        return mayMiss ? thorns with { Min = 0 } : thorns;
                    }
                    case "CurlUpPower" when onEnemy && f.CanHit(e): return Hit.Trig(harmful: true);          // it gains block
                    case "PersonalHivePower" when onEnemy && f.CanHit(e): return Hit.Trig(harmful: true);    // Dazed into your draw pile
                    // Each damage instance that gets through its block counts (Asleep wakes on the first,
                    // Slumber after Amount of them). Judged by the end of your turn: this play's hits,
                    // procs and orb evokes strip block that your end-of-turn damage then gets through.
                    case "AsleepPower" when onEnemy && WakesBy(e, f, 1): return Hit.Trig(harmful: true);
                    case "SlumberPower" when onEnemy && WakesBy(e, f, a): return Hit.Trig(harmful: true);
                    case "BurrowedPower" when onEnemy && f.BreaksBlock(e): return Hit.Trig();                // stuns it
                    case "FlutterPower" when onEnemy && f.Unblocked(e) > 0 && a <= f.HitsOn(e): return Hit.Trig(); // stuns it
                    case "PlowPower" when onEnemy && f.Unblocked(e) > 0 && e.CurrentHp - f.Unblocked(e) <= a: return Hit.Trig();
                    case "ShriekPower" when onEnemy && f.UnblockedWithOrbs(e) > 0 && e.CurrentHp - f.UnblockedWithOrbs(e) <= a: return Hit.Trig();
                    case "TheGambitPower" when onEnemy && f.Unblocked(e) > 0: return Hit.Trig();             // it dies
                    case "SlipperyPower" when onEnemy && f.Unblocked(e) > 0: return Hit.Trig(harmful: true); // HP loss capped at 1

                    // ---- powers on enemies: killing one
                    case "InfestedPower" or "SteamEruptionPower" or "SurprisePower" when onEnemy && f.Kills(e): return Hit.Trig(harmful: true);
                    case "CrabRagePower" or "RavenousPower" when onEnemy && _cs.HittableEnemies.Any(o => o != e && f.Kills(o)): return Hit.Trig(harmful: true);
                }
                return null;
            }

            case RelicModel r:
            {
                var name = r.GetType().Name;
                int N(string key, int fallback) => Sts2State.Var(r, key, fallback);
                // Counter relics fire on the card that completes the count (the tray shows progress).
                // DisplayAmount is the count mod N, except it reads N during the relic's activation
                // animation (right after it fired), hence the % N.
                bool Completes(bool matches)
                {
                    var n = N("Cards", 0);
                    return matches && n > 0 && r.DisplayAmount % n + plays >= n;
                }
                var armed = RelicCountersFeature.IsArmed(r, _me);
                switch (name)
                {
                    // ---- uses or breaks a relic (formerly Relic Reminders)
                    case "PenNib" when r.Status == RelicStatus.Active && type == CardType.Attack: return Hit.Trig();
                    case "ArtOfWar" or "RippleBasin" when type == CardType.Attack && Sts2State.CountPlayedThisTurn(_me, CardType.Attack) == 0:
                        return Hit.Trig(harmful: true);
                    case "PaelsTears":
                    {
                        // Ending the turn with energy left gives energy next turn: cards that would
                        // spend your last energy (X-cost cards spend it all) give that up.
                        var energy = _me.PlayerCombatState?.Energy ?? 0;
                        return energy > 0 && f.Energy >= energy ? Hit.Trig(harmful: true) : null;
                    }
                    // Ending a turn without playing a card gives an extra turn (once per combat):
                    // while armed and nothing has been played, every card gives that up.
                    case "PaelsEye" when armed && Sts2State.CountPlayedThisTurn(_me) == 0: return Hit.Trig(harmful: true);
                    case "Vambrace" when armed && f.BlockGains > 0: return Hit.Trig();
                    // Off cooldown, the next card that gains block gets double block, then it rests 2 turns.
                    case "PaelsLegion" when armed && f.BlockGains > 0: return Hit.Trig();
                    case "BurningSticks" when armed && type == CardType.Skill && f.ExhaustsSelf: return Hit.Trig();
                    // It arms on a debuff to an enemy without Artifact (checked before Artifact uses a
                    // charge), so an enemy whose charges cover every debuff this play applies doesn't count.
                    case "UnsettlingLamp" when armed && f.EnemyDebuffs.Count > 0
                                               && _cs.HittableEnemies.Any(e => (e.GetPower<ArtifactPower>()?.Amount ?? 0) < f.EnemyDebuffs.Count * Math.Max(1, plays)):
                        return Hit.Trig();
                    case "MusicBox" when armed && type == CardType.Attack: return Hit.Trig();

                    // ---- on play
                    case "IntimidatingHelmet" when f.Energy >= N("Energy", 2): return RelicBlock(r, plays);
                    case "DaughterOfTheWind" when type == CardType.Attack: return RelicBlock(r, plays);
                    case "Permafrost" when type == CardType.Power && armed: return RelicBlock(r, 1);
                    case "LostWisp" when type == CardType.Power: return RelicDamage(r, plays, Spread.All);
                    case "GamePiece" when type == CardType.Power: return Hit.Trig();
                    // A random other hand card becomes free this turn. It prefers cards that still
                    // cost energy or stars, but picks one anyway when none do (and nothing changes).
                    case "MummifiedHand" when type == CardType.Power:
                        return Sts2State.Hand(_me).Any(c => c != card && c.CostsEnergyOrStars(includeGlobalModifiers: true)) ? Hit.Trig() : null;
                    case "HelicalDart" when card.Tags.Contains(CardTag.Shiv): return Hit.Trig();
                    case "IvoryTile" when f.Energy >= N("EnergyThreshold", 3): return Hit.Trig();
                    case "ChemicalX" when card.EnergyCost.CostsX || card.HasStarCostX: return Hit.Trig();
                    case "RazorTooth" when (type == CardType.Attack || type == CardType.Skill) && card.IsUpgradable: return Hit.Trig();
                    case "RainbowRing":
                    {
                        if (Sts2State.Private(r, "ActivationCountThisTurn", 0) >= 1) return null;
                        var played = Sts2State.CardsPlayedThisTurn(_me).Select(c => c.Type).Append(type).ToHashSet();
                        var before = Sts2State.CardsPlayedThisTurn(_me).Select(c => c.Type).ToHashSet();
                        bool All(HashSet<CardType> s) => s.Contains(CardType.Attack) && s.Contains(CardType.Skill) && s.Contains(CardType.Power);
                        return !All(before) && All(played) ? Hit.Trig() : null;
                    }

                    // ---- counters completed by this card
                    case "Kunai" or "Shuriken" or "Nunchaku" when Completes(type == CardType.Attack): return Hit.Trig();
                    case "OrnamentalFan" when Completes(type == CardType.Attack): return RelicBlock(r, 1);
                    case "Kusarigama" when Completes(type == CardType.Attack): return RelicDamage(r, 1, Spread.Random);
                    case "LetterOpener" when Completes(type == CardType.Skill): return RelicDamage(r, 1, Spread.All);
                    case "TuningFork" when Completes(type == CardType.Skill): return RelicBlock(r, 1);
                    case "IronClub" when Completes(true): return Hit.Trig();

                    // ---- exhaust / discard / stars / created cards / shuffle / empty hand
                    case "CharonsAshes" when exhausts > 0: return RelicDamage(r, exhausts, Spread.All);
                    case "ForgottenSoul" when exhausts > 0: return RelicDamage(r, exhausts, Spread.Random);
                    case "JossPaper" when exhausts > 0 && Sts2State.Private(r, "CardsExhausted", 0) + exhausts >= N("ExhaustAmount", 5): return Hit.Trig();
                    case "Tingsha" when discards > 0: return RelicDamage(r, discards, Spread.Random);
                    case "ToughBandages" when discards > 0: return RelicBlock(r, discards);
                    case "MiniRegent" when f.Stars > 0 && !Sts2State.Private(r, "UsedThisTurn", true): return Hit.Trig();
                    case "GalacticDust" when f.Stars > 0:
                    {
                        var per = N("Stars", 10);
                        var times = per > 0 ? (Sts2State.Private(r, "StarsSpent", 0) + f.Stars) / per : 0;
                        return times > 0 ? RelicBlock(r, times) : null;
                    }
                    case "Regalite" when generated > 0 && !Sts2State.Private(r, "UsedThisTurn", true): return RelicBlock(r, 1);
                    case "TheAbacus" when shuffles: return RelicBlock(r, 1);
                    case "Metronome":
                    {
                        var n = N("OrbCount", 7);
                        var channeled = Sts2State.Private(r, "OrbsChanneled", n);
                        var channels = f.Channels + _xChannels;
                        return channels > 0 && channeled < n && channeled + channels >= n ? RelicDamage(r, 1, Spread.All) : null;
                    }
                    case "BoneFlute" when f.OstyAttacks > 0 && _me.IsOstyAlive: return RelicBlock(r, f.OstyAttacks);
                    case "UnceasingTop" when Sts2State.Hand(_me).Count == 1 && draws == 0 && generated == 0: return Hit.Trig();

                    // ---- your attack: breaking block, killing
                    case "HandDrill" when _cs.HittableEnemies.Any(f.BreaksBlock): return Hit.Trig();
                    case "GremlinHorn" when _cs.HittableEnemies.Any(f.Kills): return Hit.Trig();

                    // ---- losing HP from the card (your turn)
                    case "CentennialPuzzle" when f.HpLoss > 0 && !Sts2State.Private(r, "UsedThisCombat", true): return Hit.Trig();
                    case "SelfFormingClay" when f.HpLoss > 0: return Hit.Trig();
                    case "DemonTongue" when f.HpLoss > 0 && !Sts2State.Private(r, "_triggeredThisTurn", true): return Hit.Trig();
                    case "EmotionChip" when f.HpLoss > 0: return Hit.Trig();
                    case "RedSkull" when f.HpLoss > 0:
                    {
                        var c = _me.Creature;
                        var threshold = c.MaxHp * Sts2State.Var(r, "HpThreshold", 50) / 100m;
                        return c.CurrentHp > threshold && c.CurrentHp - f.HpLoss <= threshold ? Hit.Trig() : null;
                    }
                }
                return null;
            }
        }
        return null;
    }

    // ------------------------------------------------------------------ amounts

    /// <summary>Block you'd actually gain: the game's own block math (Dexterity/Frail only apply to powered block).</summary>
    private TurnEndModel? _turnEnd;
    private long _turnEndFrame = -1;

    /// <summary>
    /// This card's own block (its Block value, through the game's block math, times its plays)
    /// takes your expected end-of-turn block from below the enemy's FullBlockNeeded to at or above it.
    /// </summary>
    /// <summary>The Incoming Damage simulation for this frame (computed once, shared by rules).</summary>
    private TurnEndModel? TurnEnd()
    {
        if (_turnEndFrame != _ctx.Decor.Frame)
        {
            _turnEnd = TurnEndModel.Compute();
            _turnEndFrame = _ctx.Decor.Frame;
        }
        return _turnEnd;
    }

    private bool CrossesFullBlock(Creature enemy, CardModel card, PlayFacts f)
    {
        var te = TurnEnd();
        if (te == null || !te.FullBlockNeeded.TryGetValue(enemy, out var needed)) return false;
        var have = te.ExpectedBlock;
        if (have >= needed) return false;
        return have + CardBlock(card) + f.OrbBlock >= needed; // its own block, plus Frost orbs it evokes
    }

    private int CardBlock(CardModel card)
    {
        try
        {
            decimal raw;
            if (card.DynamicVars.TryGetValue("CalculatedBlock", out var cv) && cv is CalculatedVar calc) raw = calc.Calculate(null);
            else if (card.DynamicVars.TryGetValue("Block", out var bv)) raw = bv.BaseValue;
            else return 0;
            if (raw <= 0) return 0;
            var each = Math.Max(0, (int)Hook.ModifyBlock(_cs, _me.Creature, raw, ValueProp.Move, card, null, out _));
            return each * Math.Max(1, CardIndicatorsFeature.PlayCount(card));
        }
        catch
        {
            return 0;
        }
    }

    private int BlockOf(int raw, ValueProp props = ValueProp.Unpowered)
    {
        if (raw <= 0) return 0;
        try
        {
            return Math.Max(0, (int)Hook.ModifyBlock(_cs, _me.Creature, raw, props, null, null, out _));
        }
        catch
        {
            return raw;
        }
    }

    /// <summary><paramref name="times"/> separate block gains of <paramref name="each"/>.</summary>
    private Hit BlockN(int each, int times, ValueProp props = ValueProp.Unpowered) => times <= 0 || each <= 0
        ? new Hit(Kind.Block, 0, Count: 0)
        : new Hit(Kind.Block, BlockOf(each, props) * times, Count: times);

    private Hit RelicBlock(RelicModel r, int times)
    {
        var props = r.DynamicVars.TryGetValue("Block", out var v) && v is BlockVar bv ? bv.Props : ValueProp.Unpowered;
        return BlockN(Sts2State.Var(r, "Block"), times, props);
    }

    private Hit RelicDamage(RelicModel r, int times, Spread spread)
    {
        var props = r.DynamicVars.TryGetValue("Damage", out var v) && v is DamageVar dv ? dv.Props : ValueProp.Unpowered;
        return Damage(Sts2State.Var(r, "Damage"), times, spread, props: props);
    }

    /// <summary>
    /// <paramref name="times"/> damage instances of <paramref name="each"/> raw damage.
    /// The tab's number: per enemy hit, dealer side only (before each enemy's own modifiers),
    /// except for one known target (that enemy, or you) where it's exact. The audit range (Min/Max)
    /// applies every target's own modifiers: All = sum over enemies; Random = between the
    /// least- and most-damaged enemy; Specific/Me = that creature.
    /// </summary>
    private Hit Damage(int each, int times, Spread spread, Creature? target = null, Creature? dealer = null,
        ValueProp props = ValueProp.Unpowered, bool harmful = false)
    {
        if (each <= 0 || times <= 0) return new Hit(Kind.Damage, 0, harmful);
        var from = dealer ?? (spread == Spread.Me ? null : _me.Creature);
        int Mod(Creature? t)
        {
            try
            {
                return Math.Max(0, (int)Hook.ModifyDamage(_me.RunState, _cs, t, from, each, props, null, null, ModifyDamageHookType.All, CardPreviewMode.None, out _));
            }
            catch
            {
                return each;
            }
        }

        var enemies = _cs.HittableEnemies;
        int shown, min, max;
        switch (spread)
        {
            case Spread.Specific when target != null:
                shown = min = max = Mod(target) * times;
                break;
            case Spread.Me:
                shown = min = max = Mod(_me.Creature) * times;
                break;
            case Spread.All:
                shown = Mod(null) * times;
                min = max = enemies.Sum(Mod) * times;
                break;
            default: // Random (or no enemies)
                shown = Mod(null) * times;
                min = enemies.Count > 0 ? enemies.Min(Mod) * times : 0;
                max = enemies.Count > 0 ? enemies.Max(Mod) * times : 0;
                break;
        }
        Dictionary<Creature, List<int>>? on = null;
        void On(Creature t)
        {
            on ??= new Dictionary<Creature, List<int>>();
            var l = on.TryGetValue(t, out var x) ? x : on[t] = new List<int>();
            for (var i = 0; i < times; i++) l.Add(Mod(t));
        }
        switch (spread)
        {
            case Spread.Specific when target != null: On(target); break;
            case Spread.All: foreach (var e in enemies) On(e); break;
            case Spread.Random when enemies.Count == 1: On(enemies[0]); break;
        }
        return new Hit(Kind.Damage, shown, harmful, Count: times, Min: min, Max: max, OnEnemies: on,
            Random: spread == Spread.Random && enemies.Count > 1);
    }

    private static IReadOnlyDictionary<Creature, List<int>>? MergeHits(IReadOnlyDictionary<Creature, List<int>>? a, IReadOnlyDictionary<Creature, List<int>>? b)
    {
        if (a == null) return b;
        if (b == null) return a;
        var m = a.ToDictionary(kv => kv.Key, kv => kv.Value.ToList());
        foreach (var (e, l) in b)
        {
            if (!m.TryGetValue(e, out var x)) m[e] = x = new List<int>();
            x.AddRange(l);
        }
        return m;
    }

    /// <summary>
    /// Playing this card makes at least <paramref name="needed"/> damage instances get through the
    /// enemy's block by the end of your turn, when your end-of-turn damage alone wouldn't. In order:
    /// the card's own hits, orbs it evokes, the procs it sets off, then your end-of-turn damage.
    /// The enemy's block stays up until its own turn starts.
    /// </summary>
    private bool WakesBy(Creature enemy, PlayFacts f, int needed)
    {
        if (needed <= 0 || !enemy.IsAlive) return false;
        var play = new List<int>();
        if (f.CanHit(enemy))
        {
            var per = f.DamagePerHit(enemy);
            for (var i = 0; i < f.HitsOn(enemy); i++) play.Add(per);
        }
        if (f.OrbDamageOn(enemy) is var orb and > 0) play.Add(orb);
        if (_xEnemyHits.TryGetValue(enemy, out var procs)) play.AddRange(procs);
        if (play.Count == 0) return false;

        var eot = TurnEnd() is { } te && te.EndOfTurnHitsOn.TryGetValue(enemy, out var l) ? l : new List<int>();

        int Through(IEnumerable<int> hits)
        {
            var block = enemy.Block;
            var n = 0;
            foreach (var h in hits)
            {
                if (h <= 0) continue;
                if (h > block) n++;
                block = Math.Max(0, block - h);
            }
            return n;
        }
        return Through(play.Concat(eot)) >= needed && Through(eot) < needed;
    }
}
