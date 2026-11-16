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
/// "Does something happen the moment I play this card that the card doesn't say?" One tab per power or relic that playing the
/// card sets off, with that source's icon. Spec: docs/FEATURES.md#proc-indicators.
///
///  - Trigger: plain icon ("this sets off that").
///  - Block / damage / summon: icon plus the amount (QolSettings.ProcNumbers).
///  - Harmful (helps the enemy, hurts you, or breaks one of your relics): crossed.
///
/// Every tab comes from the outcome simulator (decision #48, docs/SIMULATOR_V2.md): the card's play is simulated once per target,
/// and the models that fired during it (the play log) are its tabs, with the block or damage they caused. A good effect needs
/// every branch of some aim; a warning, one branch of any aim. Effects judged by the end of the turn (kills, stuns, waking an
/// enemy) compare the play's outcome with ending the turn now. The audit (ProcAudit) checks the tabs against real plays.
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

    private readonly HashSet<string> _warned = new();

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
                d.AddTab(t.Key, t.Icon, crossed: t.Crossed, number: t.Number, kind: t.Kind, amount: t.Amount, auditMin: t.AuditMin, auditMax: t.AuditMax, card: t.Card);
    }

    /// <summary>A card's facts, the proc sources this frame, and its running simulations.</summary>
    private sealed record Prepared(PlayFacts F, List<AbstractModel> Sources, Task<SimOutcome[]>? Sim);

    /// <summary>The player whose Power cards a teammate's Imitation Learning copies (ImitationLearningPower.PlayerTarget).</summary>
    internal static Player? CopiesFrom(ImitationLearningPower p)
    {
        try { return p.PlayerTarget; } catch { return null; }
    }

    /// <summary>Your powers, your relics, and powers on every living enemy: the proc sources (each with a tab when its model fires).</summary>
    private static List<AbstractModel> Sources(Player me, ICombatState cs)
    {
        var sources = new List<AbstractModel>();
        sources.AddRange(me.Creature.Powers);
        sources.AddRange(me.Relics); // each relic's tab has its own switch (Tabs "proc:<Relic>")
        // All living enemies: an untargetable one's powers still fire.
        foreach (var enemy in cs.Enemies.Where(e => e.IsAlive)) sources.AddRange(enemy.Powers);
        // Teammates' powers that act on your plays (only these: the other rules read your powers and enemies').
        foreach (var mate in SimAdapter.Teammates(me, cs))
            sources.AddRange(mate.Creature.Powers.OfType<ImitationLearningPower>().Where(p => CopiesFrom(p) == me));
        return sources;
    }

    /// <summary>
    /// One card's prediction, up to its simulations (started, not awaited): the same code for the tabs on screen and for the
    /// audit's prediction at resolution (PredictNow). record: keep its facts for the audit and the play queue.
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
        Task<SimOutcome[]>? sim = null;
        try
        {
            sim = StartSim(card, f);
        }
        catch (Exception ex)
        {
            SimFailed(card, ex);
        }
        return new Prepared(f, sources, sim);
    }

    /// <summary>A prepared card's tabs, once its simulations are done.</summary>
    private List<TabSpec> Finish(CardModel card, Prepared p)
    {
        var tabs = new Dictionary<string, TabSpec>();
        var lethal = false;
        try
        {
            lethal = p.Sim != null && FinishSim(p.F, p.Sources, p.Sim, tabs);
        }
        catch (Exception ex)
        {
            SimFailed(card, ex);
        }
        var list = tabs.Values.ToList();
        if (lethal) list.Add(new TabSpec("lethal", GameIcons.Skull()));
        // Cards this play brings back to your hand (Make It So, Right Hand Hand, Whistling Arrows): plain, one per card name,
        // with the returning card as the icon.
        foreach (var back in p.F.CardsBack.GroupBy(c => c.GetType().Name))
            list.Add(new TabSpec("return:" + back.Key, null, Card: back.First()));
        // Free Card, Free Attack, Free Skill: the energy this play saves (or "?" when Snecko Eye hides the cost); crossed when
        // the card already costs 0 or is X-cost, so playing it throws the free card away.
        foreach (var use in FreeCards.Uses(card, _me))
        {
            var numbers = _ctx.Settings.ProcNumbers;
            list.Add(new TabSpec(FreeCards.Prefix + use.Power.GetType().Name, GameIcons.Power(use.Power), Crossed: use.Wasted,
                Number: numbers ? use.Saved : null, Kind: "energy", Amount: use.Saved ?? 0, NumberText: numbers && use.Hidden ? "?" : null));
        }
        return list;
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

    // Card simulations run in parallel on worker threads (the simulator is plain C#, no game types): the frame reads the game and
    // builds every card's inputs, starts all their simulations at once, and waits for them (capped at SimWaitCapMs). Results are
    // cached until the state changes. One simulation's work is bounded (SimEngine.StepCopyBudget per step) and counted by
    // SimPerformanceTests.
    private const int SimWaitCapMs = 250;
    private readonly Dictionary<string, Task<SimOutcome[]>> _simCache = new();

    /// <summary>Runs the outcome simulator for this card (once per possible target for single-target cards) against "end your turn now".</summary>
    private Task<SimOutcome[]>? StartSim(CardModel card, PlayFacts f)
    {
        var te = TurnEnd();
        if (te == null) return null;

        // A card aimed at any enemy, or at any teammate (AnyAlly: one simulation per living teammate, SimAdapter.AllyKey),
        // is simulated once per target; a tab that needs some aim shows when one target gives it.
        var targets = card.TargetType switch
        {
            TargetType.AnyEnemy => te.Ids.Where(kv => kv.Key.IsAlive && kv.Key.IsHittable).Select(kv => kv.Value).ToList(),
            TargetType.AnyAlly => Enumerable.Range(0, te.Teammates.Count).Select(SimAdapter.AllyKey).ToList(),
            _ => new List<int> { -1 },
        };
        if (targets.Count == 0) targets.Add(-1);

        var stateKey = te.StateKey;
        if (!_noCache && stateKey != _simKey)
        {
            _simKey = stateKey;
            _simCache.Clear(); // running tasks finish on their own; their results are dropped
        }
        var cacheKey = $"{card.GetHashCode()}|{f.Plays}|{f.Energy}|{f.AttackHits}|{f.BlockGains}|{f.Draws}|{f.Generated}";
        // The plays are built here (they read the game); only the simulation runs off the main thread.
        var inputs = new SimInputs { Start = te.State, Ids = te.Ids, Allies = te.Teammates.Select(p => p.Creature).ToList() };
        foreach (var t in targets) inputs.PlayByTarget[t] = SimAdapter.Play(card, f, _me, _cs, te.Ids, t);
        f.Sim = inputs;
        if (_noCache || !_simCache.TryGetValue(cacheKey, out var task))
        {
            var start = te.State;
            task = Task.WhenAll(targets.Select(t => inputs.PlayByTarget[t]).Select(p => Task.Run(() => SimEngine.Run(start, p))));
            if (!_noCache) _simCache[cacheKey] = task;
        }
        return task;
    }

    /// <summary>
    /// Reads a card's finished simulations into its tabs; returns whether it's lethal (some aim leaves an enemy dead before it
    /// acts in every branch, which ending the turn now doesn't).
    /// </summary>
    private bool FinishSim(PlayFacts f, List<AbstractModel> sources, Task<SimOutcome[]> task, Dictionary<string, TabSpec> tabs)
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
        if (outcomes.Any(o => o.Truncated || o.Branches.Count > 1)) f.RandomOutcome = true;
        if (!_noCache) Diagnostics.DamageFormulaAudit.Check(f.Card, f, te.Ids, outcomes);

        // ---- the play log: each source whose model fired during the play
        foreach (var src in sources)
        {
            int? enemyId = null;
            if (src is PowerModel { Owner.IsEnemy: true } ep)
            {
                if (!te.Ids.TryGetValue(ep.Owner, out var eid)) continue;
                enemyId = eid;
            }
            LogTab(src, enemyId, outcomes, tabs);
        }
        // Effects the engine logs itself (Juggernaut on every block gain, Thunder on Lightning evokes): logged under their power's
        // name.
        foreach (var src in _me.Creature.Powers.Where(x => x.GetType().Name is nameof(JuggernautPower) or nameof(ThunderPower)))
            LogTab(src, null, outcomes, tabs, TabKind.Damage);

        // ---- end-of-turn comparisons: what the play changes by the time the enemies act
        void Add(AbstractModel src, bool harmful) => tabs["proc:" + src.GetType().Name] = new TabSpec("proc:" + src.GetType().Name, IconOf(src), harmful);
        foreach (var (enemy, id) in te.Ids)
        {
            if (!enemy.IsAlive) continue;
            foreach (var p in enemy.Powers)
            {
                bool? hit = p.GetType().Name switch
                {
                    nameof(ImbalancedPower) when outcomes.Any(o => o.FirstHitBlockedInAll(id)) && !baseline.FirstHitBlockedInAll(id) => false,
                    nameof(AsleepPower) when outcomes.Any(o => o.UnblockedMax(id) >= 1) && baseline.UnblockedMax(id) < 1 => true,
                    nameof(SlumberPower) when outcomes.Any(o => o.UnblockedMax(id) >= p.Amount) && baseline.UnblockedMax(id) < p.Amount => true,
                    // Its death sets something off (warnings: in any branch, for any aim).
                    nameof(InfestedPower) or nameof(SteamEruptionPower) or nameof(SurprisePower) or nameof(ExplosivePower)
                        when outcomes.Any(o => o.KilledByPlayInAny.Contains(id)) => true,
                    // Stock: a new Axebot takes its place (it attacks this enemy turn) while stock is left.
                    nameof(StockPower) when p.Amount > 0 && outcomes.Any(o => o.KilledByPlayInAny.Contains(id)) => true,
                    // Another enemy's death makes it stronger.
                    nameof(CrabRagePower) or nameof(RavenousPower) when outcomes.Any(o => o.KilledByPlayInAny.Any(k => k != id)) => true,
                    // Stunned by your turn (block broken, enough hits through, HP threshold), in every branch for some aim, and not
                    // already without this card.
                    nameof(BurrowedPower) or nameof(FlutterPower) or nameof(PlowPower) or nameof(ShriekPower)
                        when outcomes.Any(o => o.StunnedInAll(id)) && !baseline.StunnedInAll(id) => false,
                    nameof(TheGambitPower) when outcomes.Any(o => o.GambitInAll(id)) && !baseline.GambitInAll(id) => false,
                    nameof(SlipperyPower) when outcomes.Any(o => o.SlipperyInAny(id)) && !baseline.SlipperyInAny(id) => true,
                    _ => null,
                };
                if (hit is { } harmful) Add(p, harmful);
            }
        }

        foreach (var r in _me.Relics)
        {
            switch (r)
            {
                // Hand Drill (Vulnerable on an enemy whose block you break): good, so every branch for some aim.
                case HandDrill when outcomes.Any(o => o.PlayBreaksBlockInEveryBranch): Add(r, false); break;
                // Parrying Shield (end of turn, block at least its threshold): plain when this play makes it fire in every branch and
                // it wouldn't for sure now; crossed when it would for sure now and this play can stop it.
                case ParryingShield when outcomes.Any(o => o.ParryingShieldInAll) && !baseline.ParryingShieldInAll: Add(r, false); break;
                case ParryingShield when baseline.ParryingShieldInAll && outcomes.Any(o => !o.ParryingShieldInAll): Add(r, true); break;
                // Gremlin Horn (energy and a draw when an enemy dies): some aim kills an enemy in every branch.
                case GremlinHorn when outcomes.Any(o => o.PlayKillsInEveryBranch): Add(r, false); break;
            }
        }
        // Compost (yours or a teammate's: energy for every player on your side when an enemy dies): every branch for some aim.
        if (outcomes.Any(o => o.PlayKillsInEveryBranch))
        {
            var compost = _cs.Players.Select(p => p.Creature).Where(c => c is { IsAlive: true }).SelectMany(c => c.Powers).FirstOrDefault(p => p is CompostPower);
            if (compost != null) Add(compost, false);
        }

        // HP you lose during the play (the card's own HP cost, enemy Thorns before each hit), through your block, Tungsten Rod,
        // Beating Remnant and Buffer: good, so every branch for some aim.
        var losses = outcomes.Select(o => o.PlayHpLossesMin).DefaultIfEmpty().Max();
        var lostHp = outcomes.Select(o => o.PlayHpLostMin).DefaultIfEmpty().Max();
        foreach (var r in _me.Relics)
        {
            // Keys by nameof, so the settings check (CatalogCheck reads this class's string literals) sees each rule.
            var key = r switch
            {
                // AfterDamageReceived with UnblockedDamage > 0: HP actually lost, after Tungsten Rod.
                SelfFormingClay when losses > 0 => nameof(SelfFormingClay),
                EmotionChip when losses > 0 => nameof(EmotionChip),
                CentennialPuzzle when losses > 0 && !Sts2State.Private(r, "UsedThisCombat", true) => nameof(CentennialPuzzle),
                DemonTongue when losses > 0 && !Sts2State.Private(r, "_triggeredThisTurn", true) => nameof(DemonTongue),
                // AfterCurrentHpChanged: Strength once your HP is at or below its threshold.
                RedSkull when lostHp > 0 && _me.Creature is var c
                    && c.CurrentHp > c.MaxHp * Sts2State.Var(r, "HpThreshold", 50) / 100m
                    && c.CurrentHp - lostHp <= c.MaxHp * Sts2State.Var(r, "HpThreshold", 50) / 100m => nameof(RedSkull),
                _ => null,
            };
            if (key != null) Add(r, false);
        }
        // Rupture (Strength when you lose HP on your turn).
        if (losses > 0 && _me.Creature.Powers.FirstOrDefault(p => p is RupturePower) is { } rupture) Add(rupture, false);

        return outcomes.Any(o => te.Ids.Values.Any(id => o.DeadInAll(id) && !baseline.DeadInAll(id)));
    }

    /// <summary>The sources whose tabs come from the end-of-turn comparisons by type (the settings check reads these names).</summary>
    internal static readonly string[] OutcomeTabs =
    {
        nameof(HandDrill), nameof(ParryingShield), nameof(GremlinHorn), nameof(CompostPower), nameof(RupturePower),
    };

    private static Godot.Texture2D? IconOf(AbstractModel src) => src switch
    {
        RelicModel r => GameIcons.Relic(r),
        PowerModel pw => GameIcons.Power(pw),
        _ => null,
    };

    /// <summary>
    /// A source's tab from the play log (docs/SIMULATOR_V2.md "Tabs from the log"): its model fired during the play, in every
    /// branch of some aim (good) or in some branch of any aim (harmful). Indirect firings (from draws, exhausts, discards, created
    /// cards, shuffles) count only with the Indirect procs setting. Block, damage and summon tabs carry the amount the model logged
    /// (the most any aim guarantees) and the audit's range (the least any aim guarantees to the most any branch gives). Several
    /// enemies with the same power share one tab, their amounts added.
    /// </summary>
    private void LogTab(AbstractModel src, int? enemyId, SimOutcome[] outcomes, Dictionary<string, TabSpec> tabs, TabKind? kindOverride = null)
    {
        if (src is PowerModel && SimAdapter.IsTemporaryThorns(src.GetType())) return; // its Thorns has the ThornsPower tab
        var name = SimReaders.ModelName(src);
        var model = SimModels.Get(name);
        if ((kindOverride ?? model?.Tab) is not { } kind) return;
        var harmful = model?.Harmful ?? false;
        var key = enemyId is { } id ? $"{name}@{id}" : name;
        bool Fired(PowerSet log) => log.Has(key) && (_indirect || log[key] > log["~" + key]);
        var shown = harmful ? outcomes.Any(o => Fired(o.PlayFiredAny)) : outcomes.Any(o => Fired(o.PlayFiredEvery));
        if (!shown) return;
        var amount = harmful
            ? outcomes.Max(o => o.PlayFiredAny.DataOf(key))
            : outcomes.Where(o => Fired(o.PlayFiredEvery)).Max(o => o.PlayFiredEvery.DataOf(key));
        var min = outcomes.Min(o => o.PlayFiredEvery.DataOf(key));
        var max = outcomes.Max(o => o.PlayFiredAny.DataOf(key));
        if (kind != TabKind.Trigger && max <= 0) return;
        var tabKey = "proc:" + src.GetType().Name;
        if (tabs.TryGetValue(tabKey, out var prev))
        {
            amount += prev.Amount;
            min += prev.AuditMin;
            max += prev.AuditMax;
        }
        int? number = kind != TabKind.Trigger && _ctx.Settings.ProcNumbers ? amount : null;
        var kindName = kind switch { TabKind.Block => "block", TabKind.Damage => "damage", TabKind.Summon => "summon", _ => null };
        tabs[tabKey] = new TabSpec(tabKey, IconOf(src), harmful, number, kindName, amount, min, max);
    }


    /// <summary>PredictNow is running: the simulation cache (keyed to this frame's state) is left alone.</summary>
    private bool _noCache;

    private TurnEndModel? _turnEnd;
    private long _turnEndFrame = -1;

    /// <summary>The Incoming Damage simulation for this frame (computed once, shared by every card).</summary>
    private TurnEndModel? TurnEnd()
    {
        if (_turnEndFrame != _ctx.Decor.Frame)
        {
            _turnEnd = TurnEndModel.Compute();
            _turnEndFrame = _ctx.Decor.Frame;
        }
        return _turnEnd;
    }
}
