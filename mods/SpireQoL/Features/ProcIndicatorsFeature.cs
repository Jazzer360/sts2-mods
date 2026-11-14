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
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.ValueProps;
using SpireQoL.Cards;
using SpireQoL.Combat;
using SpireQoL.Procs;
using SpireQoL.Sim;
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

    public ProcIndicatorsFeature(QolContext ctx)
    {
        _ctx = ctx;
        _instance = this;
    }

    private static ProcIndicatorsFeature? _instance;

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
        IReadOnlyDictionary<Creature, List<int>>? OnEnemies = null, bool Random = false,
        IReadOnlyList<(IReadOnlyDictionary<Creature, int> PerEnemy, int Times)>? RandomHits = null)
    {
        public static Hit Trig(bool harmful = false) => new(Kind.Trigger, 0, harmful);
    }

    private readonly HashSet<string> _warned = new();

    // Procs that other procs can set off; evaluated in a second pass with these extras.
    // Waking reads the first pass's proc damage on each enemy (Kusarigama etc.), so it runs second.
    // (Juggernaut is the simulator's: it counts every block gain the play makes, see FinishSim.)
    private static readonly HashSet<string> SecondOrder = new() { nameof(Metronome), nameof(BlackHolePower) };
    private int _xChannels, _xStarGains;

    internal static bool CanEverPlayNow(CardModel card)
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
        var sources = Sources(me, cs);

        var pending = new List<(CardModel Card, Prepared P, CardDecor D)>();
        foreach (var card in hand)
        {
            _ctx.Decor.SeenInHand(card);
            // Nothing happens on play for a card that can't be played (Unplayable statuses and
            // curses, blocked cards, cards whose own condition isn't met). Cards you just can't
            // afford yet still show, since energy can come mid-turn.
            if (!CanEverPlayNow(card)) continue;
            if (Prepare(card, sources, record: true) is not { } p) continue;
            pending.Add((card, p, _ctx.Decor.For(card)));
        }

        // Every card's simulations (one per target) run in parallel across CPU cores; the frame waits
        // for them, so the tabs appear on the frame they belong to. The wait is capped so a pathological
        // state can't freeze the game: anything still running then shows a frame or two later (SimPending).
        var running = pending.Select(x => x.P.Sim).Where(t => t is { IsCompleted: false }).Cast<Task>().ToArray();
        if (running.Length > 0)
        {
            try { Task.WaitAll(running, SimWaitCapMs); }
            catch { /* faulted tasks are reported per card below */ }
        }

        foreach (var (card, p, d) in pending)
            foreach (var t in Finish(card, p))
                d.AddTab(t.Key, t.Icon, crossed: t.Crossed, number: t.Number, kind: t.Kind, amount: t.Amount, auditMin: t.AuditMin, auditMax: t.AuditMax);
    }

    /// <summary>A card's facts, its merged proc hits, and its running simulations.</summary>
    private sealed record Prepared(PlayFacts F, Dictionary<string, (AbstractModel Src, Hit Hit)> Merged, Task<SimOutcome[]>? Sim);

    /// <summary>Your powers, your relics, and powers on every living enemy: the proc sources.</summary>
    private static List<AbstractModel> Sources(Player me, ICombatState cs)
    {
        var sources = new List<AbstractModel>();
        sources.AddRange(me.Creature.Powers);
        sources.AddRange(me.Relics); // each relic's tab has its own switch (Tabs "proc:<Relic>")
        // All living enemies: an untargetable one's powers still fire.
        foreach (var enemy in cs.Enemies.Where(e => e.IsAlive)) sources.AddRange(enemy.Powers);
        return sources;
    }

    /// <summary>
    /// One card's prediction, up to its simulations (started, not awaited): the same code for the tabs on
    /// screen and for the audit's prediction at resolution (PredictNow). record: keep its facts for the
    /// audit and the play queue (the frame loop does; PredictNow doesn't).
    /// </summary>
    private Prepared? Prepare(CardModel card, List<AbstractModel> sources, bool record)
    {
        PlayFacts f;
        try
        {
            f = PlayFacts.For(card, _me, _cs, CardIndicatorsFeature.PlayCount(card));
        }
        catch (Exception ex)
        {
            if (_warned.Add(card.GetType().Name))
            {
                ModEntry.Log.Warn($"proc facts failed for {card.GetType().Name}: {ex.Message}");
                Sts2Mods.Core.Diagnostics.ErrorReporter.Instance?.Exception(ex, "play-facts:" + card.GetType().Name);
            }
            return null;
        }

        if (record)
        {
            _facts[card] = f;
            _lastFacts[card] = f;
            if (_lastFacts.Count > 300) _lastFacts.Clear();
        }
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
                        RandomHits = prev.Hit.RandomHits == null ? h.RandomHits : h.RandomHits == null ? prev.Hit.RandomHits : prev.Hit.RandomHits.Concat(h.RandomHits).ToList(),
                    })
                    : (src, h);
            }
        }

        // First-order procs, then the procs they set off (procs of procs): block gains feed
        // Juggernaut, Storm's lightning feeds Metronome, The Sealed Throne's stars feed Black Hole.
        _xChannels = _xStarGains = 0;
        Run(secondOrder: false);
        foreach (var (src, h) in merged.Values)
        {
            if (src is PowerModel { } sp && sp.GetType().Name == nameof(StormPower)) _xChannels += sp.Amount * f.Plays;
            if (src is PowerModel { } tp && tp.GetType().Name == nameof(TheSealedThronePower) && tp.Amount > 0) _xStarGains += f.Plays;
        }
        Run(secondOrder: true);

        if (merged.Values.Any(m => m.Hit.Random)) f.RandomOutcome = true;

        Task<SimOutcome[]>? sim = null;
        try
        {
            sim = StartSim(card, f, merged);
        }
        catch (Exception ex)
        {
            SimFailed(card, ex);
        }
        return new Prepared(f, merged, sim);
    }

    /// <summary>A prepared card's tabs, once its simulations are done (simulator tabs included).</summary>
    private List<TabSpec> Finish(CardModel card, Prepared p)
    {
        var (f, merged, sim) = p;
        var lethal = false;
        try
        {
            lethal = sim != null && FinishSim(f, merged, sim);
        }
        catch (Exception ex)
        {
            SimFailed(card, ex);
        }
        var tabs = new List<TabSpec>();
        foreach (var (key, (src, h)) in merged)
        {
            var icon = src switch
            {
                RelicModel r => GameIcons.Relic(r),
                PowerModel pw => GameIcons.Power(pw),
                _ => null,
            };
            int? number = h.Kind != Kind.Trigger && _ctx.Settings.ProcNumbers ? h.Amount : null;
            var kind = h.Kind switch { Kind.Block => "block", Kind.Damage => "damage", Kind.Summon => "summon", _ => null };
            tabs.Add(new TabSpec(key, icon, h.Harmful, number, kind, h.Amount, h.Min, h.Max));
        }
        if (lethal) tabs.Add(new TabSpec("lethal", GameIcons.Skull()));
        return tabs;
    }

    /// <summary>
    /// The audit's prediction for a card as its play begins to resolve (decision #28): from the board right
    /// now, with nothing pending ahead of it, through the same code as the tabs, simulations run inline.
    /// Null when it can't be built (outside your turn, the card's facts failed).
    /// </summary>
    public static (List<TabSpec> Tabs, PlayFacts Facts)? PredictNow(CardModel card)
    {
        var self = _instance;
        if (self == null) return null;
        var me = Sts2State.Me;
        var cs = Sts2State.Combat;
        if (me == null || cs == null) return null;
        var live = TurnEndModel.ComputeLive();
        if (live == null) return null;
        var (savedMe, savedCs, savedTe, savedFrame) = (self._me, self._cs, self._turnEnd, self._turnEndFrame);
        self._me = me;
        self._cs = cs;
        self._indirect = self._ctx.Settings.ProcIndirect;
        self._turnEnd = live;
        self._turnEndFrame = self._ctx.Decor.Frame;
        self._noCache = true;
        try
        {
            if (self.Prepare(card, Sources(me, cs), record: false) is not { } p) return null;
            if (p.Sim is { IsCompleted: false } t)
            {
                try { t.Wait(SimWaitCapMs); } catch { /* reported by Finish */ }
            }
            return (self.Finish(card, p), p.F);
        }
        finally
        {
            self._noCache = false;
            (self._me, self._cs, self._turnEnd, self._turnEndFrame) = (savedMe, savedCs, savedTe, savedFrame);
        }
    }

    private void SimFailed(CardModel card, Exception ex)
    {
        if (!_warned.Add("sim:" + card.GetType().Name)) return;
        ModEntry.Log.Warn($"outcome simulation failed for {card.GetType().Name}: {ex.Message}");
        Sts2Mods.Core.Diagnostics.ErrorReporter.Instance?.Exception(ex, "sim:" + card.GetType().Name);
    }

    // ------------------------------------------------------------------ outcome simulation

    // Simulations for this game state, per card and target (the state rarely changes between frames).
    private long _simKey;

    // Card simulations run in parallel on worker threads (the simulator is plain C#, no game types):
    // the frame reads the game and builds every card's inputs, starts all their simulations at once,
    // and waits for them (capped at SimWaitCapMs). Results are cached until the state changes.
    // One simulation's work is bounded (SimEngine.StepCopyBudget per step) and counted by SimPerformanceTests.
    private const int SimWaitCapMs = 250;
    private readonly Dictionary<string, Task<SimOutcome[]>> _simCache = new();

    /// <summary>
    /// Runs the outcome simulator for this card (once per possible target for single-target cards)
    /// against "end your turn now", and adds what only the simulation can tell:
    ///  - Imbalanced (good for you, so only when guaranteed): its first hit fully blocked in every branch.
    ///  - Asleep / Slumber (bad, so whenever possible): enough damage instances through its block by the
    ///    end of your turn in some branch, when that isn't possible without this card.
    /// Returns whether the card is lethal: some aim leaves an enemy dead before it acts in every branch,
    /// which ending the turn now doesn't.
    /// </summary>
    private Task<SimOutcome[]>? StartSim(CardModel card, PlayFacts f, Dictionary<string, (AbstractModel Src, Hit Hit)> merged)
    {
        var te = TurnEnd();
        if (te == null) return null;

        // Damage and block procs the rules predicted (the simulator models Juggernaut itself).
        var procs = new List<ProcHit>();
        var procBlock = new List<int>();
        var toYou = new List<(string, int)>();
        foreach (var (src, h) in merged.Values)
        {
            // Damage procs on you (Galvanic): the simulator lands them once the card resolves. Enemy Thorns it
            // models itself, before each hit.
            if (h.Harmful && h.Kind == Kind.Damage && h.Amount > 0 && src.GetType().Name != nameof(ThornsPower))
                toYou.Add((src.GetType().Name, h.Amount));
            if (src.GetType().Name == nameof(JuggernautPower) || h.Harmful) continue;
            if (h.Kind == Kind.Damage)
            {
                if (h.OnEnemies != null)
                {
                    foreach (var (e, amounts) in h.OnEnemies)
                    {
                        if (!te.Ids.TryGetValue(e, out var id)) continue;
                        foreach (var amount in amounts) procs.Add(new ProcHit { Spread = SimPlay.Target.Single, TargetId = id, Damage = { [id] = amount } });
                    }
                }
                if (h.RandomHits != null)
                {
                    foreach (var (perEnemy, times) in h.RandomHits)
                    {
                        var map = new Dictionary<int, int>();
                        foreach (var (e, amount) in perEnemy)
                            if (te.Ids.TryGetValue(e, out var id)) map[id] = amount;
                        procs.Add(new ProcHit { Spread = SimPlay.Target.Random, Damage = map, Times = times });
                    }
                }
            }
            else if (h.Kind == Kind.Block && h.Count > 0)
            {
                for (var i = 0; i < h.Count; i++) procBlock.Add(h.Amount / h.Count);
            }
        }

        var targets = card.TargetType == TargetType.AnyEnemy
            ? te.Ids.Where(kv => kv.Key.IsAlive && kv.Key.IsHittable).Select(kv => kv.Value).ToList()
            : new List<int> { -1 };
        if (targets.Count == 0) targets.Add(-1);

        var stateKey = te.StateKey;
        if (!_noCache && stateKey != _simKey)
        {
            _simKey = stateKey;
            _simCache.Clear(); // running tasks finish on their own; their results are dropped
        }
        var cacheKey = $"{card.GetHashCode()}|{f.Plays}|{f.Energy}|{f.AttackHits}|{f.BlockGains}|{f.Draws}|{f.Generated}|" +
                       string.Join(",", procs.Select(p => $"{p.Spread}{p.TargetId}x{p.Times}:{string.Join("/", p.Damage.Values)}")) + "|" + string.Join(",", procBlock) + "|" + string.Join(",", toYou.Select(x => x.Item2));
        // The plays are built here (they read the game); only the simulation runs off the main thread.
        var inputs = new SimInputs { Start = te.State, Ids = te.Ids };
        foreach (var t in targets)
        {
            var play = SimAdapter.Play(card, f, _me, _cs, te.Ids, t, procs, procBlock);
            play.DamageToYou.AddRange(toYou);
            inputs.PlayByTarget[t] = play;
        }
        f.Sim = inputs;
        if (_noCache || !_simCache.TryGetValue(cacheKey, out var task))
        {
            var start = te.State;
            task = Task.WhenAll(targets.Select(t => inputs.PlayByTarget[t]).Select(p => Task.Run(() => SimEngine.Run(start, p))));
            if (!_noCache) _simCache[cacheKey] = task;
        }
        return task;
    }

    /// <summary>The play makes this enemy's attack stronger (Enrage's Strength: SimPlay.SetHits above its current hits).</summary>
    private static bool IntentRaised(PlayFacts f, int id)
    {
        if (f.Sim?.Start.Enemy(id) is not { } e) return false;
        var now = e.Hits.Sum();
        return f.Sim.PlayByTarget.Values.Any(p => p.SetHits.TryGetValue(id, out var h) && h.Sum() > now);
    }

    /// <summary>Reads a card's finished simulations into its tabs (see StartSim); returns whether it's lethal.</summary>
    private bool FinishSim(PlayFacts f, Dictionary<string, (AbstractModel Src, Hit Hit)> merged, Task<SimOutcome[]> task)
    {
        var te = TurnEnd();
        if (te == null) return false;
        if (!task.IsCompleted)
        {
            f.SimPending = true;
            return false;
        }
        if (task.IsFaulted) throw task.Exception!.GetBaseException();
        var outcomes = task.Result;
        var baseline = te.Outcome;

        foreach (var (enemy, id) in te.Ids)
        {
            if (!enemy.IsAlive) continue;
            foreach (var p in enemy.Powers)
            {
                var name = p.GetType().Name;
                Hit? hit = name switch
                {
                    nameof(ImbalancedPower) when outcomes.Any(o => o.FirstHitBlockedInAll(id)) && !baseline.FirstHitBlockedInAll(id) => Hit.Trig(),
                    nameof(AsleepPower) when outcomes.Any(o => o.UnblockedMax(id) >= 1) && baseline.UnblockedMax(id) < 1 => Hit.Trig(harmful: true),
                    nameof(SlumberPower) when outcomes.Any(o => o.UnblockedMax(id) >= p.Amount) && baseline.UnblockedMax(id) < p.Amount => Hit.Trig(harmful: true),
                    // Its death sets something off (warnings: in any branch, for any aim).
                    nameof(InfestedPower) or nameof(SteamEruptionPower) or nameof(SurprisePower)
                        when outcomes.Any(o => o.KilledByPlayInAny.Contains(id)) => Hit.Trig(harmful: true),
                    // Another enemy's death makes it stronger.
                    nameof(CrabRagePower) or nameof(RavenousPower)
                        when outcomes.Any(o => o.KilledByPlayInAny.Any(k => k != id)) => Hit.Trig(harmful: true),
                    // It gains block because of this play (Curl Up after a card's attack hits it, Skittish
                    // when one gets through), in some branch.
                    nameof(CurlUpPower) or nameof(SkittishPower) or nameof(SneakyPower)
                        when outcomes.Any(o => o.BlockReactionInAny.Contains(id)) => Hit.Trig(harmful: true),
                    // Its attack gets stronger (Enrage's Strength on a Skill).
                    nameof(EnragePower) when IntentRaised(f, id) => Hit.Trig(harmful: true),
                    // Stunned by your turn (block broken, enough hits through, HP threshold), in every
                    // branch for some aim, and not already without this card.
                    nameof(BurrowedPower) or nameof(FlutterPower) or nameof(PlowPower) or nameof(ShriekPower)
                        when outcomes.Any(o => o.StunnedInAll(id)) && !baseline.StunnedInAll(id) => Hit.Trig(),
                    nameof(TheGambitPower) when outcomes.Any(o => o.GambitInAll(id)) && !baseline.GambitInAll(id) => Hit.Trig(),
                    nameof(SlipperyPower) when outcomes.Any(o => o.SlipperyInAny(id)) && !baseline.SlipperyInAny(id) => Hit.Trig(harmful: true),
                    _ => null,
                };
                if (hit is { } h) merged["proc:" + name] = (p, h);
            }
        }
        // Juggernaut: a random hit per block gain the play makes (its own block, block procs, Frost
        // evokes), as counted by the simulator; the amounts and audit range from the damage rules.
        if (_me.Creature.GetPower<JuggernautPower>() is { Amount: > 0 } jug)
        {
            var gains = outcomes.Max(o => o.PlayBlockGainsMax);
            if (gains > 0)
            {
                _curFacts = f;
                merged["proc:" + nameof(JuggernautPower)] = (jug, Damage(jug.Amount, gains, Spread.Random));
            }
        }

        // Hand Drill (Vulnerable on an enemy whose block you break): good, so every branch for some aim.
        foreach (var r in _me.Relics.OfType<HandDrill>())
        {
            if (outcomes.Any(o => o.PlayBreaksBlockInEveryBranch)) merged["proc:" + nameof(HandDrill)] = (r, Hit.Trig());
        }

        // Parrying Shield (end of turn, block at least its threshold): plain when this play makes it fire in
        // every branch and it wouldn't for sure now; crossed when it would for sure now and this play can stop
        // it (less block: Orichalcum no longer arming, Thorns, a Burn landing first...).
        foreach (var r in _me.Relics.OfType<ParryingShield>())
        {
            if (outcomes.Any(o => o.ParryingShieldInAll) && !baseline.ParryingShieldInAll) merged["proc:" + nameof(ParryingShield)] = (r, Hit.Trig());
            else if (baseline.ParryingShieldInAll && outcomes.Any(o => !o.ParryingShieldInAll)) merged["proc:" + nameof(ParryingShield)] = (r, Hit.Trig(harmful: true));
        }

        // Gremlin Horn (energy and a draw when an enemy dies): good, so only when some aim kills an
        // enemy in every branch of the play.
        foreach (var r in _me.Relics.OfType<GremlinHorn>())
        {
            if (outcomes.Any(o => o.PlayKillsInEveryBranch)) merged["proc:" + nameof(GremlinHorn)] = (r, Hit.Trig());
        }

        return outcomes.Any(o => te.Ids.Values.Any(id => o.DeadInAll(id) && !baseline.DeadInAll(id)));
    }

    // ------------------------------------------------------------------ rules

    private PlayFacts? _curFacts;
    /// <summary>
    /// The card's block is above 0 after the game's block math (No Block, Panic Button's, makes it 0; so can negative
    /// Dexterity or Frail): Pael's Legion and Vambrace only take a play whose block lands (AfterModifyingBlockAmount:
    /// modifiedAmount above 0), so their tabs need it. Cards without a block value: Entrench gains your current block
    /// (none: nothing lands), the rest (Fisticuffs) land unless the game's math zeroes any block from the card.
    /// </summary>
    private bool BlockLands(CardModel card)
    {
        if (card.DynamicVars.ContainsKey("Block") || card.DynamicVars.ContainsKey("CalculatedBlock")) return SimAdapter.CardBlock(card, _cs, _me) > 0;
        if (card is Entrench && _me.Creature.Block <= 0) return false;
        try
        {
            return Hook.ModifyBlock(_cs, _me.Creature, 10m, ValueProp.Move, card, null, out _) > 0;
        }
        catch
        {
            return true;
        }
    }

    /// <summary>Your attack and skill plays started this turn (each play of a replay counts), as Nostalgia counts them.</summary>
    private int AttacksAndSkillsStartedThisTurn()
    {
        var cs = Sts2State.Combat;
        if (cs == null) return 0;
        try
        {
            return CombatManager.Instance.History.CardPlaysStarted
                .Count(e => e.HappenedThisTurn(cs) && e.CardPlay.Player == _me && e.CardPlay.Card.Type is CardType.Attack or CardType.Skill);
        }
        catch
        {
            return 0;
        }
    }


    private Hit? Evaluate(AbstractModel src, PlayFacts f)
    {
        _curFacts = f;
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
                    case nameof(AfterimagePower) when mine: return BlockN(a, plays);
                    case nameof(RagePower) when mine && type == CardType.Attack: return BlockN(a, plays);
                    case nameof(SpiritOfAshPower) when mine && card.Keywords.Contains(CardKeyword.Ethereal): return BlockN(a, plays);
                    case nameof(DanseMacabrePower) when mine && f.Energy >= Sts2State.Var(p, "Energy", 2): return BlockN(a, plays);
                    case nameof(GravityPower) when mine: return Damage(a, plays, Spread.All);
                    case nameof(SerpentFormPower) when mine: return Damage(a, plays, Spread.Random);
                    // Each Lightning evoke's target again (ThunderPower.AfterOrbEvoked): random target, and
                    // nothing if the evoke killed it, so the audit range starts at 0.
                    case nameof(ThunderPower) when mine && f.LightningEvokes > 0: return Damage(a, f.LightningEvokes, Spread.Random) with { Min = 0 };
                    case nameof(PanachePower) when mine && plays >= Sts2State.Var(p, "CardsLeft", 5): return Damage(a, 1, Spread.All);
                    // Second order: stars gained also set off Black Hole (The Sealed Throne gains
                    // stars on every card).
                    case nameof(BlackHolePower) when mine:
                    {
                        var times = (f.Stars > 0 ? 1 : 0) + f.StarGains + _xStarGains;
                        return times > 0 ? Damage(a, times, Spread.All) : null;
                    }
                    case nameof(RupturePower) when mine && f.LosesHp: return Hit.Trig();
                    // Losing HP on your turn: damage to every enemy (InfernoPower.AfterDamageReceived), once per play.
                    case nameof(InfernoPower) when mine && f.LosesHp: return Damage(a, plays, Spread.All);
                    case nameof(DevourLifePower) when mine && card is Soul: return new Hit(Kind.Summon, a * plays);
                    case nameof(HauntPower) when mine && card is Soul: return Damage(a, plays, Spread.Random, props: ValueProp.Unblockable | ValueProp.Unpowered);
                    case nameof(StormPower) when mine && type == CardType.Power: return Hit.Trig();
                    case nameof(SubroutinePower) when mine && type == CardType.Power: return Hit.Trig();
                    case nameof(CalamityPower) when mine && type == CardType.Attack: return Hit.Trig();
                    case nameof(JugglingPower) when mine && type == CardType.Attack:
                    {
                        var attacks = Sts2State.CountPlayedThisTurn(_me, CardType.Attack);
                        return attacks < 3 && attacks + plays >= 3 ? Hit.Trig() : null;
                    }
                    case nameof(MonologuePower) when mine: return Hit.Trig();
                    case nameof(MasterPlannerPower) when mine && type == CardType.Skill: return Hit.Trig();
                    // An Attack that spends 0 energy returns to your hand while Feral has charges left
                    // this turn (DisplayAmount; FeralPower.ModifyCardPlayResultLocation). Not copies.
                    case nameof(FeralPower) when mine && type == CardType.Attack && f.Energy == 0 && !card.IsDupe && p.DisplayAmount > 0:
                        return Hit.Trig();
                    // Your first Amount attacks and skills each turn go on top of your draw pile instead of your discard pile
                    // (NostalgiaPower.ModifyCardPlayResultLocation; it counts plays started before this one).
                    case "NostalgiaPower" when mine && type is (CardType.Attack or CardType.Skill) && !f.ExhaustsSelf
                                               && AttacksAndSkillsStartedThisTurn() < a:
                        return Hit.Trig();
                    case nameof(TheSealedThronePower) when mine: return Hit.Trig();
                    case nameof(TenderPower) when mine: return Hit.Trig(harmful: true);
                    case nameof(SmoggyPower) when mine && type == CardType.Skill: return Hit.Trig(harmful: true);
                    case nameof(PaleBlueDotPower) when mine:
                    {
                        if (Sts2State.Private(p, "alreadyActivatedThisTurn", false)) return null;
                        var played = Sts2State.CountPlayedThisTurn(_me);
                        return played < 5 && played + plays >= 5 ? Hit.Trig() : null;
                    }

                    // ---- the Fool (v0.113.0): melodies (card tags Wah, Cho, Ki) and arrows; names as strings (Procs/Fool.cs)
                    // The first Wah each turn: damage to every enemy (ViolentChordPower.AfterCardPlayed, MelodyCmd.IsFirstPlayThisTurn).
                    case "ViolentChordPower" when mine && Fool.FirstMelodies(card, _me).Contains(Fool.Wah): return Damage(a, 1, Spread.All);
                    // The first Cho each turn: block (SongPathPower).
                    case "SongPathPower" when mine && Fool.FirstMelodies(card, _me).Contains(Fool.Cho): return BlockN(a, 1);
                    // Once a turn, when you've played a Wah, a Cho and a Ki (LullabyPower: the card that completes the set).
                    case "LullabyPower" when mine && !Sts2State.Private(p, "_proccdThisTurn", true)
                                                  && Fool.MelodiesThisTurn(_me) < 3 && Fool.MelodiesAfter(card, _me) == 3:
                        return BlockN(a, 1);
                    // The first Wah, Cho and Ki each turn: draws (MotifPower) / plays the top of your draw pile (LostLutePower).
                    case "MotifPower" when mine && Fool.FirstMelodies(card, _me).Any(): return Hit.Trig();
                    case "LostLutePower" when mine && Fool.FirstMelodies(card, _me).Any(): return Hit.Trig();
                    // A shot that has an arrow to shoot impales its target: damage to every enemy (ExplosiveArrowsPower.AfterArrowImpaled).
                    case "ExplosiveArrowsPower" when mine && Fool.IsShot(card) && Fool.ArrowsAvailable(_me) > 0: return Damage(a, 1, Spread.All);
                    // Energy the first time each turn one of your arrows comes loose (YahPower.AfterArrowDislodged): some aim does it.
                    case "YahPower" when mine && Fool.Dislodged(f, _me, _cs).Max > 0
                                         && Sts2State.Private<object?>(p, "_internalData", null) is { } yah
                                         && !Sts2State.Private(yah, "triggeredThisTurn", true):
                        return Hit.Trig();

                    // Spent by your next attack: Vigor (all of it, VigorPower.AfterAttack: Akabeko's on turn 1) and a stack of Gigantification;
                    // the damage the card shows already has them.
                    case nameof(VigorPower) when mine && f.SpendsAttackBonus(vigor: true): return Hit.Trig();
                    case nameof(GigantificationPower) when mine && f.SpendsAttackBonus(vigor: false): return Hit.Trig();
                    // A block card spends your Stretch (StretchPower.AfterCardPlayed); its block already shows the bonus.
                    case "StretchPower" when mine && Fool.SpendsStretch(card, f.BlockGains): return Hit.Trig();

                    // ---- Sovereign Blade's buffs (the card text lists them, but it's easy to miss)
                    case nameof(SeekingEdgePower) when mine && card is SovereignBlade: return Hit.Trig(); // hits all enemies
                    case nameof(ParryPower) when mine && card is SovereignBlade: return Hit.Trig();       // gains block
                    case nameof(SwordSagePower) when mine && card is SovereignBlade: return Hit.Trig();   // replays
                    case nameof(ConquerorPower) when onEnemy && card is SovereignBlade: return Hit.Trig(); // more damage to that enemy

                    // ---- your powers: exhaust / discard / draw / stars / energy / debuffs / created cards
                    case nameof(FeelNoPainPower) when mine && exhausts > 0: return BlockN(a, exhausts);
                    case nameof(DarkEmbracePower) when mine && exhausts > 0: return Hit.Trig();
                    case nameof(ChildOfTheStarsPower) when mine && f.Stars > 0: return BlockN(a * f.Stars, 1);
                    case nameof(OrbitPower) when mine && f.EnergySpent > 0 && f.EnergySpent >= p.DisplayAmount: return Hit.Trig(); // OrbitPower: EnergySpent
                    case nameof(AutomationPower) when mine && draws > 0 && draws >= p.DisplayAmount: return Hit.Trig();
                    case nameof(CacophonyPower) when mine && draws > 0 && draws >= p.DisplayAmount: return Damage(a, 1, Spread.Random);
                    case nameof(SpeedsterPower) when mine && draws > 0: return Damage(a, draws, Spread.All);
                    case nameof(CorrosiveWavePower) when mine && draws > 0: return Hit.Trig();
                    case nameof(StratagemPower) when mine && shuffles: return Hit.Trig();
                    case nameof(SleightOfFleshPower) when mine && f.DebuffsLandingTotal() > 0:
                        return f.HitsAllEnemies
                            ? Damage(a, Math.Max(1, f.DebuffsLandingTotal() / Math.Max(1, _cs.HittableEnemies.Count)), Spread.All)
                            : Damage(a, f.DebuffsLandingTotal(), Spread.Random);
                    case nameof(ViciousPower) when mine && f.DebuffsLandingTotal(t => t == typeof(VulnerablePower)) > 0: return Hit.Trig();
                    case nameof(ShroudPower) when mine: return BlockN(a, f.DebuffsLandingTotal(t => t == typeof(DoomPower)));
                    // Juggernaut (every block gain, including procs and Frost evokes): from the simulator, see FinishSim.
                    case nameof(EnvenomPower) when mine && type == CardType.Attack: return Hit.Trig();
                    case nameof(ConcoctPower) when mine && type == CardType.Attack: return Hit.Trig();
                    case nameof(ReaperFormPower) when mine && type == CardType.Attack: return Hit.Trig();
                    case nameof(MonarchsGazePower) when mine && type == CardType.Attack: return Hit.Trig();
                    case nameof(ArsenalPower) when mine && generated > 0: return Hit.Trig();
                    case nameof(PillarOfCreationPower) when mine && generated > 0: return BlockN(a, generated);
                    case nameof(SmokestackPower) when mine && statuses > 0: return Damage(a, statuses, Spread.All);
                    case nameof(TrashToTreasurePower) when mine && statuses > 0: return Hit.Trig();

                    // ---- powers on enemies: your plays
                    // Enrage (Strength on Skills) and Sneaky (block on Attacks): from the simulator, see FinishSim.
                    case nameof(ImitationLearningPower) when onEnemy && type == CardType.Power: return Hit.Trig(harmful: true);
                    case nameof(WitheringPresencePower) when onEnemy && plays >= p.DisplayAmount: return Hit.Trig(harmful: true);
                    case nameof(GalvanicPower) when card.Affliction is Galvanized:
                        return Damage(a, plays, Spread.Me, props: ValueProp.Unpowered | ValueProp.Move, harmful: true);
                    case nameof(VitalSparkPower) when card.Affliction is Tainted: return Hit.Trig(harmful: true);
                    case nameof(StranglePower) when onEnemy && p.Applier?.Player == _me:
                        return Damage(a, plays, Spread.Specific, target: e, props: ValueProp.Unblockable | ValueProp.Unpowered);
                    case nameof(OblivionPower) when onEnemy && p.Applier?.Player == _me: return Hit.Trig();
                    case nameof(SlowPower) when onEnemy: return Hit.Trig();

                    // ---- powers on enemies: being hit by your attack
                    case nameof(ThornsPower) when onEnemy && f.CanHit(e):
                    {
                        var thorns = Damage(a, f.HitsOn(e), Spread.Me, dealer: e, harmful: true);
                        // A single-target or random attack might hit an enemy without Thorns instead.
                        var mayMiss = !f.HitsAllEnemies && _cs.HittableEnemies.Any(o => o != e && !o.HasPower<ThornsPower>());
                        return mayMiss ? thorns with { Min = 0 } : thorns;
                    }
                    // Curl Up and Skittish (it gains block): from the simulator's branches, see FinishSim.
                    case nameof(PersonalHivePower) when onEnemy && f.CanHit(e): return Hit.Trig(harmful: true);    // Dazed into your draw pile
                    // Each damage instance that gets through its block counts (Asleep wakes on the first,
                    // Slumber after Amount of them). Judged by the end of your turn: this play's hits,
                    // procs and orb evokes strip block that your end-of-turn damage then gets through.
                    // Burrowed, Flutter, Plow, Shriek (stuns it), The Gambit (it dies), Slippery (HP loss
                    // capped at 1): from the simulator, see FinishSim.

                    // ---- powers on enemies: killing one
                    // Killing one (Infested, Steam Eruption, Surprise, Crab Rage, Ravenous): from the
                    // simulator's branches, see FinishSim.
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
                var armed = RelicCountersFeature.IsArmed(r);
                switch (name)
                {
                    // ---- uses or breaks a relic (formerly Relic Reminders)
                    case nameof(PenNib) when r.Status == RelicStatus.Active && type == CardType.Attack: return Hit.Trig();
                    case nameof(ArtOfWar) or nameof(RippleBasin) when type == CardType.Attack && Sts2State.CountPlayedThisTurn(_me, CardType.Attack) == 0:
                        return Hit.Trig(harmful: true);
                    case nameof(PaelsTears):
                    {
                        // Ending the turn with energy left gives energy next turn: cards that would
                        // spend your last energy (X-cost cards spend it all) give that up.
                        var energy = _me.PlayerCombatState?.Energy ?? 0;
                        return energy > 0 && f.EnergySpent >= energy ? Hit.Trig(harmful: true) : null;
                    }
                    // Ending a turn without playing a card gives an extra turn (once per combat):
                    // while armed and nothing has been played, every card gives that up.
                    case nameof(PaelsEye) when armed && !f.AutoPlayed && Sts2State.CountPlayedThisTurn(_me) == 0: return Hit.Trig(harmful: true); // auto-plays don't count
                    case nameof(Vambrace) when armed && f.BlockGains > 0 && BlockLands(card): return Hit.Trig();
                    // Off cooldown, the next card that gains block gets double block, then it rests 2 turns.
                    case nameof(PaelsLegion) when armed && f.BlockGains > 0 && BlockLands(card): return Hit.Trig();
                    case nameof(BurningSticks) when armed && type == CardType.Skill && f.ExhaustsSelf: return Hit.Trig();
                    // It arms on a debuff to an enemy without Artifact (checked before Artifact uses a
                    // charge), so an enemy whose charges cover every debuff this play applies doesn't count.
                    // Temporary debuffs count too (Dark Shackles: UnsettlingLamp.BeforePowerAmountChanged checks the power's type).
                    case nameof(UnsettlingLamp) when armed && f.EnemyDebuffs.Count + f.EnemyTemporaryDebuffs.Count > 0
                                               && _cs.HittableEnemies.Any(e => (e.GetPower<ArtifactPower>()?.Amount ?? 0) < (f.EnemyDebuffs.Count + f.EnemyTemporaryDebuffs.Count) * Math.Max(1, plays)):
                        return Hit.Trig();
                    case nameof(MusicBox) when armed && type == CardType.Attack: return Hit.Trig();

                    // ---- on play
                    case nameof(IntimidatingHelmet) when f.Energy >= N("Energy", 2): return RelicBlock(r, plays);
                    case nameof(DaughterOfTheWind) when type == CardType.Attack: return RelicBlock(r, plays);
                    case nameof(Permafrost) when type == CardType.Power && armed: return RelicBlock(r, 1);
                    case nameof(LostWisp) when type == CardType.Power: return RelicDamage(r, plays, Spread.All);
                    case nameof(GamePiece) when type == CardType.Power: return Hit.Trig();
                    // A random other hand card becomes free this turn. It prefers cards that still
                    // cost energy or stars, but picks one anyway when none do (and nothing changes).
                    case nameof(MummifiedHand) when type == CardType.Power:
                        return Sts2State.Hand(_me).Any(c => c != card && c.CostsEnergyOrStars(includeGlobalModifiers: true)) ? Hit.Trig() : null;
                    case nameof(HelicalDart) when card.Tags.Contains(CardTag.Shiv): return Hit.Trig();
                    case nameof(IvoryTile) when f.Energy >= N("EnergyThreshold", 3): return Hit.Trig();
                    case nameof(ChemicalX) when card.EnergyCost.CostsX || card.HasStarCostX: return Hit.Trig();
                    case nameof(RazorTooth) when (type == CardType.Attack || type == CardType.Skill) && card.IsUpgradable: return Hit.Trig();
                    case nameof(RainbowRing):
                    {
                        if (Sts2State.Private(r, "ActivationCountThisTurn", 0) >= 1) return null;
                        var played = Sts2State.CardsPlayedThisTurn(_me).Select(c => c.Type).Append(type).ToHashSet();
                        var before = Sts2State.CardsPlayedThisTurn(_me).Select(c => c.Type).ToHashSet();
                        bool All(HashSet<CardType> s) => s.Contains(CardType.Attack) && s.Contains(CardType.Skill) && s.Contains(CardType.Power);
                        return !All(before) && All(played) ? Hit.Trig() : null;
                    }

                    // Block per arrow of yours this play dislodges (Twine.AfterArrowDislodged): its block when every aim
                    // dislodges one (the fewest), else a plain tab when some aim does.
                    case "Twine":
                    {
                        var (min, max) = Fool.Dislodged(f, _me, _cs);
                        return min > 0 ? RelicBlock(r, min) : max > 0 ? Hit.Trig() : null;
                    }
                    // The card that brings you to three melodies this turn (Wah, Cho, Ki): Conductor's Baton, so powered
                    // attacks on you deal 75% until your next turn (ConductorsBaton.AfterCardPlayed, once a turn).
                    case "ConductorsBaton" when !Sts2State.Private(r, "_triggeredThisTurn", true)
                                                && Fool.MelodiesThisTurn(_me) < 3 && Fool.MelodiesAfter(card, _me) >= 3:
                        return Hit.Trig();

                    // ---- counters completed by this card
                    case nameof(Kunai) or nameof(Shuriken) or nameof(Nunchaku) when Completes(type == CardType.Attack): return Hit.Trig();
                    case nameof(OrnamentalFan) when Completes(type == CardType.Attack): return RelicBlock(r, 1);
                    case nameof(Kusarigama) when Completes(type == CardType.Attack): return RelicDamage(r, 1, Spread.Random);
                    case nameof(LetterOpener) when Completes(type == CardType.Skill): return RelicDamage(r, 1, Spread.All);
                    case nameof(TuningFork) when Completes(type == CardType.Skill): return RelicBlock(r, 1);
                    case nameof(IronClub) when Completes(true): return Hit.Trig();

                    // ---- exhaust / discard / stars / created cards / shuffle / empty hand
                    case nameof(CharonsAshes) when exhausts > 0: return RelicDamage(r, exhausts, Spread.All);
                    case nameof(ForgottenSoul) when exhausts > 0: return RelicDamage(r, exhausts, Spread.Random);
                    case nameof(JossPaper) when exhausts > 0 && Sts2State.Private(r, "CardsExhausted", 0) + exhausts >= N("ExhaustAmount", 5): return Hit.Trig();
                    case nameof(Tingsha) when discards > 0: return RelicDamage(r, discards, Spread.Random);
                    case nameof(ToughBandages) when discards > 0: return RelicBlock(r, discards);
                    case nameof(MiniRegent) when f.Stars > 0 && !Sts2State.Private(r, "UsedThisTurn", true): return Hit.Trig();
                    case nameof(GalacticDust) when f.Stars > 0:
                    {
                        var per = N("Stars", 10);
                        var times = per > 0 ? (Sts2State.Private(r, "StarsSpent", 0) + f.Stars) / per : 0;
                        return times > 0 ? RelicBlock(r, times) : null;
                    }
                    case nameof(Regalite) when generated > 0 && !Sts2State.Private(r, "UsedThisTurn", true): return RelicBlock(r, 1);
                    case nameof(TheAbacus) when shuffles: return RelicBlock(r, 1);
                    // A Soot (Unplayable status) into your draw pile on every shuffle (BiiigHug.AfterShuffle).
                    case nameof(BiiigHug) when shuffles: return Hit.Trig(harmful: true);
                    case nameof(Metronome):
                    {
                        var n = N("OrbCount", 7);
                        var channeled = Sts2State.Private(r, "OrbsChanneled", n);
                        var channels = f.Channels + _xChannels;
                        return channels > 0 && channeled < n && channeled + channels >= n ? RelicDamage(r, 1, Spread.All) : null;
                    }
                    case nameof(BoneFlute) when f.OstyAttacks > 0 && _me.IsOstyAlive: return RelicBlock(r, f.OstyAttacks);
                    case nameof(UnceasingTop) when Sts2State.Hand(_me).Count == 1 && draws == 0 && generated == 0: return Hit.Trig();

                    // ---- your attack: breaking block, killing
                    // Hand Drill (breaking an enemy's block): from the simulator, see FinishSim.
                    // Gremlin Horn (an enemy dies): from the simulator's branches, see FinishSim.

                    // ---- losing HP from the card (your turn)
                    case nameof(CentennialPuzzle) when f.HpLoss > 0 && !Sts2State.Private(r, "UsedThisCombat", true): return Hit.Trig();
                    case nameof(SelfFormingClay) when f.HpLoss > 0: return Hit.Trig();
                    case nameof(DemonTongue) when f.HpLoss > 0 && !Sts2State.Private(r, "_triggeredThisTurn", true): return Hit.Trig();
                    case nameof(EmotionChip) when f.HpLoss > 0: return Hit.Trig();
                    case nameof(RedSkull) when f.HpLoss > 0:
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
    /// <summary>PredictNow is running: the simulation cache (keyed to this frame's state) is left alone.</summary>
    private bool _noCache;

    private TurnEndModel? _turnEnd;
    private long _turnEndFrame = -1;

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

        // Random and all-enemy procs land after the card: enemies it kills are gone by then (a play that
        // kills every enemy, e.g. one that revives, leaves the proc nothing to hit).
        var enemies = spread is Spread.Random or Spread.All && _curFacts is { } cf
            ? _cs.HittableEnemies.Where(e => !cf.Kills(e)).ToList()
            : _cs.HittableEnemies.ToList();
        // A single-target card kills at most the enemy you aim it at: an all-enemy proc after it still hits
        // every other enemy. Each one is in the proc (the simulator plays each aim and skips the dead), and the
        // audit range runs from leaving out every enemy the card could kill to none of them.
        var aimed = spread == Spread.All && _curFacts?.Card.TargetType == TargetType.AnyEnemy;
        var reached = aimed ? _cs.HittableEnemies.ToList() : enemies;
        if (spread is Spread.Random or Spread.All && reached.Count == 0) return new Hit(Kind.Damage, 0, harmful);
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
                min = enemies.Sum(Mod) * times;
                max = reached.Sum(Mod) * times;
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
            case Spread.All: foreach (var e in reached) On(e); break;
            case Spread.Random when enemies.Count == 1: On(enemies[0]); break;
        }
        var random = spread == Spread.Random && enemies.Count > 1;
        var randomHits = random
            ? new List<(IReadOnlyDictionary<Creature, int>, int)> { (enemies.ToDictionary(e => e, e => Mod(e)), times) }
            : null;
        return new Hit(Kind.Damage, shown, harmful, Count: times, Min: min, Max: max, OnEnemies: on,
            Random: random, RandomHits: randomHits);
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
}
