using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.Models.Relics;
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
///  - Block / damage / summon / draw / energy: icon plus the amount (QolSettings.ProcNumbers; draws and energy: decision #132).
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
        // Frame-driven. The predictions remember the cards placed on top of your draw pile (observe-only; patched once).
        DrawPileMemory.Patch(harmony);
    }

    public void Attach(OverlayHost host)
    {
    }

    private readonly HashSet<string> _warned = new();

    // Per-frame context.
    private Player _me = null!;
    private ICombatState _cs = null!;
    private bool _indirect;

    /// <summary>The card being finished: for each proc tab, which of its targets (simulations) give it (the aim marker).</summary>
    private readonly Dictionary<string, bool[]> _firesAt = new();
    /// <summary>Per tabbed source this frame's card: whether each aim sets it off in every outcome and in some (PlayFacts.FiresEveryByAim).</summary>
    private readonly Dictionary<string, (Type T, bool[] Every, bool[] Any)> _firesBy = new();

    /// <summary>Each card's facts from the last frame it was in hand (kept after it leaves; ProcAudit).</summary>
    public static IReadOnlyDictionary<CardModel, PlayFacts> LastFacts => _lastFacts;

    private static readonly Dictionary<CardModel, PlayFacts> _lastFacts = new(ReferenceEqualityComparer.Instance);

    public static IReadOnlyDictionary<CardModel, PlayFacts> PreviousFacts { get; private set; }
        = new Dictionary<CardModel, PlayFacts>(ReferenceEqualityComparer.Instance);

    private Dictionary<CardModel, PlayFacts> _facts = new(ReferenceEqualityComparer.Instance);
    /// <summary>The frame _facts were filled on (worked out or reused).</summary>
    private ulong _factsFrame = ulong.MaxValue;
    /// <summary>Last frame's FrameKey, kept while the board probe sees nothing change.</summary>
    private long _lastFrameKey;
    /// <summary>The board probe _lastFrameKey was worked out from.</summary>
    private int _lastFrameKeyProbe;

    /// <summary>
    /// This frame's facts for a card in your hand, when Proc Indicators has worked them out (or reused them) this frame: Shuffle
    /// Warning, which runs after it, reads them instead of building them again. Null otherwise.
    /// </summary>
    internal static PlayFacts? FactsThisFrame(CardModel card) =>
        _instance is { } i && i._factsFrame == Godot.Engine.GetProcessFrames() && i._facts.TryGetValue(card, out var f) ? f : null;

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
        FreeCards.Observe(me, hand);
        if (hand.Count == 0) return;

        _me = me;
        _cs = cs;
        _indirect = _ctx.Settings.ProcIndirect;

        // Nothing changed since a frame whose every result was final: its facts and tabs again, without reading each card again
        // (decision #114). In dev mode every 30th such frame is worked out anyway and checked against what would have been reused.
        var te = TurnEnd();
        // The frame key reads every card through the game's hooks (its play count, whether it can be played): on a frame where the
        // board probe saw nothing change (decision #130), last frame's key stands.
        var probe = BoardProbe.Hash(me, cs);
        var probeSame = te != null && _lastFrameKey != 0 && _lastFrameKeyProbe == probe;
        var key = te == null ? 0 : probeSame ? _lastFrameKey : FrameKey(te, hand);
        _lastFrameKey = key;
        _lastFrameKeyProbe = probe;
        var checkReuse = false;
        if (key != 0 && key == _reuseKey)
        {
            if (!ModEntry.DevMode || ++_reusedSinceCheck < BoardProbe.CheckEvery)
            {
                Reuse(hand, cs);
                return;
            }
            _reusedSinceCheck = 0;
            checkReuse = true;
            if (probeSame && FrameKey(te!, hand) != key) BoardProbe.Missed("Proc Indicators", "its frame key changed while the board probe didn't");
        }

        // Sources: your powers, your relics, and powers on enemies (theirs, or ones you applied).
        var sources = Sources(me, cs);

        var pending = new List<(CardModel Card, Prepared P, CardDecor D)>();
        foreach (var card in hand)
        {
            _ctx.Decor.SeenInHand(card);
            // Nothing happens on play for a card that can't be played (Unplayable statuses and
            // curses, blocked cards, cards whose own condition isn't met). Cards you just can't
            // afford yet still show, since energy can come mid-turn.
            if (!CardPlays.CanPlayThisTurn(card)) continue;
            if (Prepare(card, sources, record: true) is not { } p) continue;
            pending.Add((card, p, _ctx.Decor.For(card)));
        }

        // Every card's simulations (one per target) run in parallel across CPU cores; the frame waits for new ones within its few
        // milliseconds (SimWait, decision #104), so tabs usually appear on the frame they belong to. What isn't ready by then shows
        // when it is, a frame or a few later (SimPending); a simulation that missed its frame never makes a later one wait.
        SimWait.All(pending.Where(x => x.P.Sim != null).SelectMany(x => x.P.Sim!.Jobs).ToList());

        // Held (decision #105): a card whose simulation, or the turn forecast it compares with, has no result for this board yet keeps
        // the tabs it showed this turn until it has one, instead of losing them for a few frames (flicker on every hit of a busy
        // play). Each card switches as soon as its own result is in. A card with nothing to hold shows what it has.
        var forecastPending = TurnEnd() == null;
        foreach (var card in hand)
            if (!pending.Any(x => ReferenceEquals(x.Card, card))) _lastTabs.Remove(card);
        var final = !forecastPending;
        var shown = new List<(CardModel Card, List<TabSpec> Tabs)>();
        foreach (var (card, p, d) in pending)
        {
            if ((forecastPending || p.Sim is { IsCompleted: false }) && Held(card, cs) is { } held)
            {
                _heldFrames++;
                final = false;
                foreach (var t in held) d.AddTab(t);
                continue;
            }
            if (p.Sim is { IsCompleted: false }) final = false;
            var tabs = Finish(card, p);
            _lastTabs[card] = (_ctx.Decor.Frame, tabs, cs, Sts2State.TurnNumber);
            if (_lastTabs.Count > 300) _lastTabs.Clear();
            foreach (var t in tabs) d.AddTab(t);
            shown.Add((card, tabs));
        }

        if (checkReuse) CheckReuse(shown);
        // Reusable only once every result is final: a pending simulation or forecast changes the tabs with nothing else changing.
        _reuseKey = final ? key : 0;
        _reuseTabs = shown;
    }

    // ------------------------------------------------------------------ reuse while nothing changes (decision #114)

    private long _reuseKey;
    private List<(CardModel Card, List<TabSpec> Tabs)> _reuseTabs = new();
    private int _reusedSinceCheck;
    private static readonly HashSet<string> _reuseWarned = new();

    /// <summary>
    /// What this frame's tabs depend on: the board (the turn forecast's state key: every creature, power, relic, orb, pile and
    /// count the simulator reads), the fight and turn, the settings that change tabs, and each card in hand with what it shows
    /// and can do (its numbers, cost, keywords, affliction, plays, whether it can be played now). Not where you aim: a tab that
    /// needs aim is worked out for every target (decision #55). Aim-dependent tabs would need the target in this key.
    /// </summary>
    private long FrameKey(TurnEndModel te, IReadOnlyList<CardModel> hand)
    {
        var h = new HashCode();
        h.Add(te.StateKey);
        h.Add(System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(_cs));
        h.Add(Sts2State.TurnNumber);
        h.Add(_indirect);
        h.Add(_ctx.Settings.ProcNumbers);
        foreach (var card in hand)
        {
            h.Add(System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(card));
            AddCardNumbers(ref h, card);
            try { h.Add(card.EnergyCost.GetWithModifiers(CostModifiers.All)); } catch { h.Add(-99); }
            foreach (var kw in card.Keywords) h.Add(kw);
            h.Add(card.Affliction?.GetType().Name);
            h.Add(CardPlays.Count(card));
            h.Add(CardPlays.CanPlayThisTurn(card));
        }
        var k = h.ToHashCode();
        return k == 0 ? 1 : k;
    }

    /// <summary>Last frame's facts and tabs again: kept for the audits (PreviousFacts, LastTabs) as a frame of their own.</summary>
    private void Reuse(IReadOnlyList<CardModel> hand, ICombatState cs)
    {
        foreach (var card in hand) _ctx.Decor.SeenInHand(card);
        foreach (var kv in PreviousFacts) _facts[kv.Key] = kv.Value;
        _factsFrame = Godot.Engine.GetProcessFrames();
        foreach (var (card, tabs) in _reuseTabs)
        {
            _lastTabs[card] = (_ctx.Decor.Frame, tabs, cs, Sts2State.TurnNumber);
            var d = _ctx.Decor.For(card);
            foreach (var t in tabs) d.AddTab(t);
        }
    }

    /// <summary>Dev mode: a frame worked out where the last one's tabs would have been reused; a difference means the key missed something.</summary>
    private void CheckReuse(List<(CardModel Card, List<TabSpec> Tabs)> now)
    {
        foreach (var (card, tabs) in now)
        {
            var before = _reuseTabs.FirstOrDefault(x => ReferenceEquals(x.Card, card)).Tabs;
            if (before == null || before.SequenceEqual(tabs)) continue;
            var name = card.GetType().Name;
            if (_reuseWarned.Add(name))
                ModEntry.Log.Warn($"[proc] reused tabs went stale for {name} with no change the frame key sees: " +
                    $"reused [{string.Join(", ", before.Select(t => t.Key))}], now [{string.Join(", ", tabs.Select(t => t.Key))}]");
        }
    }

    /// <summary>The tabs the card last showed from a result of its own, this turn of this fight; null when there are none to hold.</summary>
    private static List<TabSpec>? Held(CardModel card, ICombatState cs)
        => _lastTabs.TryGetValue(card, out var last) && ReferenceEquals(last.Combat, cs) && last.Turn == Sts2State.TurnNumber ? last.Tabs : null;

    /// <summary>Card frames shown with held tabs since the last call (the combat summary: decision #105).</summary>
    internal static int TakeHeldFrames()
    {
        var n = _heldFrames;
        _heldFrames = 0;
        return n;
    }

    private static int _heldFrames;

    /// <summary>
    /// Each card's proc tabs from the last frame it was in hand with a result of its own (not held: decision #105), as this feature
    /// gave them to the card (every one, before the settings hide any), with the frame, fight and turn: what ProcAudit holds against its prediction as the play begins (decision #103).
    /// </summary>
    public static IReadOnlyDictionary<CardModel, (long Frame, List<TabSpec> Tabs, ICombatState Combat, int Turn)> LastTabs => _lastTabs;

    private static readonly Dictionary<CardModel, (long Frame, List<TabSpec> Tabs, ICombatState Combat, int Turn)> _lastTabs = new(ReferenceEqualityComparer.Instance);

    /// <summary>A card's facts, the proc sources this frame, and its running simulations.</summary>
    private sealed record Prepared(PlayFacts F, List<AbstractModel> Sources, SimBatch? Sim);


    /// <summary>The player whose Power cards a teammate's Imitation Learning copies (ImitationLearningPower.PlayerTarget).</summary>
    internal static Player? CopiesFrom(ImitationLearningPower p)
    {
        try { return p.PlayerTarget; } catch { return null; }
    }

    /// <summary>
    /// The proc sources (each with a tab when its model fires): your powers and relics, powers on every living enemy, and every
    /// living teammate's powers and relics (decision #75: what your card sets off for a teammate gets a tab like your own).
    /// </summary>
    private static List<AbstractModel> Sources(Player me, ICombatState cs)
    {
        var sources = new List<AbstractModel>();
        sources.AddRange(me.Creature.Powers);
        sources.AddRange(me.Relics); // each relic's tab has its own switch (Tabs "proc:<Relic>")
        // All living enemies: an untargetable one's powers still fire.
        foreach (var enemy in cs.Enemies.Where(e => e.IsAlive)) sources.AddRange(enemy.Powers);
        foreach (var mate in SimAdapter.Teammates(me, cs))
        {
            if (!mate.Creature.IsAlive) continue;
            sources.AddRange(mate.Creature.Powers);
            sources.AddRange(mate.Relics.Where(r => !r.IsMelted));
        }
        return sources;
    }

    /// <summary>
    /// A power's creature, or null for a canonical stand-in (WithOwnPowers: a power the card gives you that you don't have yet,
    /// which reads as yours). Reading Owner on a canonical model throws (AbstractModel.AssertMutable: issue #426).
    /// </summary>
    internal static Creature? OwnerOf(PowerModel p) => p.IsMutable ? p.Owner : null;

    /// <summary>The simulator's id of the teammate a source belongs to (their power or relic), or null for yours and enemies'.</summary>
    private static int? TeammateOf(AbstractModel src, Player me, ICombatState cs)
    {
        var owner = src switch
        {
            PowerModel p when OwnerOf(p) is { IsPlayer: true } c => c.Player,
            RelicModel r => r.Owner,
            _ => null,
        };
        if (owner == null || owner == me) return null;
        var k = SimAdapter.Teammates(me, cs).IndexOf(owner);
        return k < 0 ? null : Who.Teammate(k);
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
            f = PlayFacts.For(card, _me, _cs, CardPlays.Count(card));
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
            _factsFrame = Godot.Engine.GetProcessFrames();
            _lastFacts[card] = f;
            if (_lastFacts.Count > 300) _lastFacts.Clear();
        }
        SimBatch? sim = null;
        try
        {
            sim = StartSim(card, f);
        }
        catch (Exception ex)
        {
            SimFailed(card, ex);
        }
        return new Prepared(f, WithOwnPowers(card, sources), sim);
    }

    /// <summary>
    /// The sources plus each power the card gives you that you don't have yet and that has a tab: it lands during the play and
    /// can fire on that same play (Pale Blue Dot played as your 5th card draws next turn at once). The power's canonical model
    /// stands in for it (its icon and name; no owner, so it reads as yours).
    /// </summary>
    private static List<AbstractModel> WithOwnPowers(CardModel card, List<AbstractModel> sources)
    {
        List<AbstractModel>? result = null;
        foreach (var t in Diagnostics.CardCoverage.PowersApplied(card.GetType()))
        {
            if (!typeof(PowerModel).IsAssignableFrom(t) || PlayFacts.IsDebuffType(t)) continue;
            if (SimModels.Get(SimAdapter.SimPowerName(t))?.Tab == null) continue;
            if (card.Owner?.Creature.Powers.Any(p => p.GetType() == t) != false) continue;
            if (ModelDb.GetById<PowerModel>(ModelDb.GetId(t)) is not { } canon) continue;
            (result ??= new List<AbstractModel>(sources)).Add(canon);
        }
        return result ?? sources;
    }

    /// <summary>A prepared card's tabs, once its simulations are done.</summary>
    private List<TabSpec> Finish(CardModel card, Prepared p)
    {
        var tabs = new Dictionary<string, TabSpec>();
        var lethal = false;
        var lethalAimed = false;
        string? lethalChance = null;
        try
        {
            lethal = p.Sim != null && FinishSim(p.F, p.Sources, p.Sim, tabs, out lethalAimed, out lethalChance);
        }
        catch (Exception ex)
        {
            SimFailed(card, ex);
        }
        var list = tabs.Values.ToList();
        if (lethal) list.Add(new TabSpec("lethal", GameIcons.Skull(), Aimed: lethalAimed, NumberText: lethalChance));
        // Cards this play brings back to your hand (Make It So, Right Hand Hand, Whistling Arrows): plain, one per card name,
        // with the returning card as the icon.
        foreach (var back in p.F.CardsBack.GroupBy(c => c.GetType().Name))
            list.Add(new TabSpec("return:" + back.Key, null, Card: back.First()));
        // Free Card, Free Attack, Free Skill, Free Power: the energy this play saves (or "?" when you couldn't know the cost); crossed when
        // the card already costs 0 or is X-cost, so playing it throws the free card away.
        foreach (var use in FreeCards.Uses(card, _me))
        {
            var numbers = _ctx.Settings.ProcNumbers;
            list.Add(new TabSpec(FreeCards.Prefix + use.Power.GetType().Name, GameIcons.Power(use.Power), Crossed: use.Wasted,
                Number: numbers ? use.Saved : null, Kind: TabAmount.EnergySaved, Amount: use.Saved ?? 0, NumberText: numbers && use.Hidden ? "?" : null));
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
            if (p.Sim is { IsCompleted: false } t) SimWait.All(t.Jobs, ownBudget: true);
            return (self.Finish(card, p), p.F);
        }
        finally
        {
            // Uncached: nothing reads these simulations again.
            self._uncached.ForEach(b => b.Cancel());
            self._uncached.Clear();
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
    // builds every card's inputs, starts all their simulations at once, and waits for them a few milliseconds at most (SimWait).
    // Results are cached until the state changes. One simulation's memory is bounded (SimWork.MaxHeldStates, shared: decision #107)
    // and its work counted by SimPerformanceTests.
    private readonly Dictionary<string, SimBatch> _simCache = new();
    /// <summary>Simulations made without the cache (the play audit's), cancelled once read.</summary>
    private readonly List<SimBatch> _uncached = new();

    /// <summary>Runs the outcome simulator for this card (once per possible target for single-target cards) against "end your turn now".</summary>
    private SimBatch? StartSim(CardModel card, PlayFacts f)
    {
        var te = TurnEnd();
        if (te == null) return null;

        // A card aimed at any enemy, or at any teammate (AnyAlly: one simulation per living teammate, Who.Teammate),
        // is simulated once per target; a tab that needs some aim shows when one target gives it.
        var targets = card.TargetType switch
        {
            TargetType.AnyEnemy => te.Ids.Where(kv => kv.Key.IsAlive && kv.Key.IsHittable).Select(kv => kv.Value).ToList(),
            TargetType.AnyAlly => Enumerable.Range(0, te.Teammates.Count).Select(Who.Teammate).ToList(),
            _ => new List<int> { -1 },
        };
        if (targets.Count == 0) targets.Add(-1);

        var stateKey = te.StateKey;
        if (!_noCache && stateKey != _simKey)
        {
            _simKey = stateKey;
            foreach (var old in _simCache.Values) old.Cancel(); // the board changed: their explorations stop
            _simCache.Clear();
        }
        // The card's own numbers too: upgrading it in hand (Blessing of the Forge, Armaments) or a value that grows (Claw)
        // changes its play without changing the board.
        var cacheKey = $"{card.GetHashCode()}|{f.Plays}|{f.Energy}|{f.AttackHits}|{f.BlockGains}|{f.Draws}|{f.Generated}|{CardNumbers(card)}";
        // The plays are built here (they read the game); only the simulation runs off the main thread.
        var inputs = new SimInputs
        {
            Start = te.State, Ids = te.Ids, Allies = te.Teammates.Select(p => p.Creature).ToList(),
            FullStart = ReferenceEquals(te.FullState, te.State) ? null : te.FullState,
        };
        foreach (var t in targets) inputs.PlayByTarget[t] = SimAdapter.Play(card, f, _me, _cs, te.Ids, t);
        f.Sim = inputs;
        if (_noCache || !_simCache.TryGetValue(cacheKey, out var task))
        {
            var start = te.State;
            var name = card.GetType().Name;
            task = new SimBatch(targets.Select(t => SimJob.Start(SimSearch.Of(start, inputs.PlayByTarget[t]), $"{name} (target {t})")).ToArray());
            if (!_noCache) _simCache[cacheKey] = task;
            else _uncached.Add(task);
        }
        return task;
    }

    /// <summary>CardNumbers as a hash, for the key worked out every frame: the same numbers, no string built.</summary>
    private static void AddCardNumbers(ref HashCode h, CardModel card)
    {
        try
        {
            h.Add(card.CurrentUpgradeLevel);
            h.Add(card.Enchantment?.GetType());
            h.Add(card.Enchantment?.Amount);
            foreach (var kv in card.DynamicVars) { h.Add(kv.Key); h.Add(kv.Value.BaseValue); }
        }
        catch
        {
            h.Add(-1);
        }
    }

    /// <summary>The card's upgrade level, enchantment and every dynamic value, for the simulation cache.</summary>
    private static string CardNumbers(CardModel card)
    {
        try
        {
            var sb = new System.Text.StringBuilder();
            sb.Append(card.CurrentUpgradeLevel).Append('|').Append(card.Enchantment?.GetType().Name).Append(card.Enchantment?.Amount);
            foreach (var kv in card.DynamicVars) sb.Append('|').Append(kv.Key).Append('=').Append(kv.Value.BaseValue);
            return sb.ToString();
        }
        catch
        {
            return "?";
        }
    }

    /// <summary>
    /// The chance tab of a card that acts on what it draws (decision #119): the chance in the middle ("40%", with a ? when its search
    /// stopped at a cap or met something not modelled), and for Pillage and Scrape the most likely number above the chance (Attacks drawn,
    /// cards drawn that cost 0; a tie goes to the higher). From the first aim's outcomes: what a draw gives doesn't depend on the aim.
    /// </summary>
    private static void DrawnTab(PlayFacts f, SimOutcome[] outcomes, Dictionary<string, TabSpec> tabs)
    {
        if (outcomes.Length == 0 || !SimAdapter.DrawnCardReaders.TryGetValue(f.Card.GetType().Name, out var reader)) return;
        if (DrawnChance.Distribution(outcomes[0]) is not { } d) return;
        f.Drawn = d;
        var unsure = outcomes[0].Truncated || outcomes[0].AnyUnmodelled;
        string Pct(double p) => new SimChance(p, p, p > 0).Text() + (unsure ? "?" : "");
        if (!reader.Counts)
        {
            tabs["drawn"] = new TabSpec("drawn", null, Text: Pct(DrawnChance.ChanceOfAny(d)));
            return;
        }
        var (n, chance) = DrawnChance.MostLikely(d);
        tabs["drawn"] = new TabSpec("drawn", null, Text: Pct(chance), NumberText: n.ToString());
    }

    /// <summary>
    /// Reads a card's simulations into its tabs; returns whether it's lethal (some aim makes an enemy surely more likely to die
    /// before it acts than ending the turn now does: decision #87), in <paramref name="lethalAimed"/> whether only some aims are,
    /// and in <paramref name="lethalChance"/> the chance ("40%", "40–55%" while exploring; null when certain).
    /// </summary>
    private bool FinishSim(PlayFacts f, List<AbstractModel> sources, SimBatch task, Dictionary<string, TabSpec> tabs, out bool lethalAimed,
        out string? lethalChance)
    {
        lethalAimed = false;
        lethalChance = null;
        var te = TurnEnd();
        if (te == null) return false;
        if (!task.IsCompleted)
        {
            f.SimPending = true;
            return false;
        }
        if (task.Fault is { } fault) throw fault;
        var outcomes = task.Latest;
        _firesAt.Clear();
        _firesBy.Clear();
        var baseline = te.Outcome;
        if (outcomes.Any(o => o.Truncated || o.Branches.Count > 1)) f.RandomOutcome = true;
        f.SimIncomplete = outcomes.Any(o => o.Truncated);
        f.Unmodelled = outcomes.SelectMany(o => o.UnmodelledReasons).Distinct().ToList();
        if (!_noCache) Diagnostics.DamageFormulaAudit.Check(f.Card, f, te.Ids, outcomes);
        DrawnTab(f, outcomes, tabs);

        // ---- the play log: each source whose model fired during the play
        foreach (var src in sources)
        {
            int? ownerId = TeammateOf(src, _me, _cs);
            if (src is PowerModel ep && OwnerOf(ep) is { IsEnemy: true } enemyOwner)
            {
                if (!te.Ids.TryGetValue(enemyOwner, out var eid)) continue;
                ownerId = eid;
            }
            LogTab(src, ownerId, outcomes, tabs);
        }
        // Effects the engine logs itself (Juggernaut on every block gain, Thunder on Lightning evokes): logged under their power's
        // name, yours and every teammate's.
        foreach (var src in sources.OfType<PowerModel>().Where(x => OwnerOf(x) is { IsPlayer: true } && x.GetType().Name is nameof(JuggernautPower) or nameof(ThunderPower)))
            LogTab(src, TeammateOf(src, _me, _cs), outcomes, tabs, TabKind.Damage);

        // What the audits hold the play to, per aim (decisions #87, #95): the tabbed sources it sets off in every outcome and in
        // some, and each enemy's chance of dying before it acts.
        if (f.Sim?.PlayByTarget.Keys.ToList() is { } aims && aims.Count == outcomes.Length)
        {
            IReadOnlyDictionary<int, IReadOnlyCollection<Type>> By(Func<(Type T, bool[] Every, bool[] Any), bool[]> at) =>
                aims.Select((a, i) => (a, i)).ToDictionary(x => x.a, x => (IReadOnlyCollection<Type>)_firesBy.Values.Where(v => at(v)[x.i]).Select(v => v.T).Distinct().ToList());
            f.FiresEveryByAim = By(v => v.Every);
            f.FiresAnyByAim = By(v => v.Any);
            var enemies = te.Ids.Values.Distinct().ToList();
            f.DeathByAim = aims.Select((a, i) => (a, i)).ToDictionary(x => x.a,
                x => (IReadOnlyDictionary<int, SimChance>)enemies.ToDictionary(id => id, id => outcomes[x.i].DeadChanceRange(id)));
        }

        // ---- end-of-turn comparisons: what the play changes by the time the enemies act
        // Each rule is a test per target (one simulation each): the tab shows when some target passes it, with the aim marker
        // when not every target does (several enemies with the same power: when no target passes any of them).
        void Add(AbstractModel src, bool harmful, Func<SimOutcome, bool> at)
        {
            var key = "proc:" + src.GetType().Name;
            var firesAt = outcomes.Select(at).ToArray();
            if (!firesAt.Any(x => x)) return;
            if (_firesAt.TryGetValue(key, out var prev) && prev.Length == firesAt.Length)
                for (var i = 0; i < firesAt.Length; i++) firesAt[i] |= prev[i];
            _firesAt[key] = firesAt;
            tabs[key] = new TabSpec(key, IconOf(src), harmful, Aimed: firesAt.Length > 1 && !firesAt.All(x => x));
        }
        foreach (var (enemy, id) in te.Ids)
        {
            if (!enemy.IsAlive) continue;
            foreach (var p in enemy.Powers)
            {
                (bool Harmful, Func<SimOutcome, bool> At)? rule = p.GetType().Name switch
                {
                    nameof(ImbalancedPower) when !baseline.FirstHitBlockedInAll(id) => (false, o => o.FirstHitBlockedInAll(id)),
                    nameof(AsleepPower) when baseline.UnblockedMax(id) < 1 => (true, o => o.UnblockedMax(id) >= 1),
                    nameof(SlumberPower) when baseline.UnblockedMax(id) < p.Amount => (true, o => o.UnblockedMax(id) >= p.Amount),
                    // Its death sets something off (warnings: in any branch, for any aim).
                    nameof(InfestedPower) or nameof(SteamEruptionPower) or nameof(SurprisePower) or nameof(ExplosivePower)
                        => (true, o => o.KilledByPlayInAny.Contains(id)),
                    // Stock: a new Axebot takes its place (it attacks this enemy turn) while stock is left.
                    nameof(StockPower) when p.Amount > 0 => (true, o => o.KilledByPlayInAny.Contains(id)),
                    // Another enemy's death makes it stronger.
                    nameof(CrabRagePower) or nameof(RavenousPower) => (true, o => o.KilledByPlayInAny.Any(k => k != id)),
                    // Stunned by your turn (block broken, enough hits through, HP threshold), in every branch for some aim, and not
                    // already without this card.
                    nameof(BurrowedPower) or nameof(FlutterPower) or nameof(PlowPower) or nameof(ShriekPower)
                        when !baseline.StunnedInAll(id) => (false, o => o.StunnedInAll(id)),
                    nameof(TheGambitPower) when !baseline.GambitInAll(id) => (false, o => o.GambitInAll(id)),
                    nameof(SlipperyPower) when !baseline.SlipperyInAny(id) => (true, o => o.SlipperyInAny(id)),
                    _ => null,
                };
                if (rule is { } r) Add(p, r.Harmful, r.At);
            }
        }

        foreach (var r in _me.Relics)
        {
            switch (r)
            {
                // Hand Drill (Vulnerable on an enemy whose block you break): good, so every branch for some aim.
                case HandDrill: Add(r, false, o => o.PlayBreaksBlockInEveryBranch); break;
                // Parrying Shield (end of turn, block at least its threshold): plain when this play makes it fire in every branch and
                // it wouldn't for sure now; crossed when it would for sure now and this play can stop it.
                case ParryingShield when !baseline.ParryingShieldInAll: Add(r, false, o => o.ParryingShieldInAll); break;
                case ParryingShield: Add(r, true, o => !o.ParryingShieldInAll); break;
                // Gremlin Horn (energy and a draw when an enemy dies): some aim kills an enemy in every branch.
                case GremlinHorn: Add(r, false, o => o.PlayKillsInEveryBranch); break;
            }
        }
        // Compost (yours or a teammate's: energy for every player on your side when an enemy dies): every branch for some aim.
        var compost = _cs.Players.Select(p => p.Creature).Where(c => c is { IsAlive: true }).SelectMany(c => c.Powers).FirstOrDefault(p => p is CompostPower);
        if (compost != null) Add(compost, false, o => o.PlayKillsInEveryBranch);

        // HP you lose during the play (the card's own HP cost, enemy Thorns before each hit), through your block, Tungsten Rod,
        // Beating Remnant and Buffer: good, so every branch for some aim.
        Func<SimOutcome, bool> losesHp = o => o.PlayHpLossesMin > 0;
        foreach (var r in _me.Relics)
        {
            // Keys by nameof, so the settings check (CatalogCheck reads this class's string literals) sees each rule.
            Func<SimOutcome, bool>? at = r switch
            {
                // AfterDamageReceived with UnblockedDamage > 0: HP actually lost, after Tungsten Rod.
                SelfFormingClay => losesHp,
                EmotionChip => losesHp,
                DemonTongue when !Sts2State.Private(r, "_triggeredThisTurn", true) => losesHp,
                // AfterCurrentHpChanged: Strength once your HP is at or below its threshold.
                RedSkull when _me.Creature is var c && c.CurrentHp > c.MaxHp * Sts2State.Var(r, "HpThreshold", 50) / 100m
                    => o => o.PlayHpLostMin > 0 && c.CurrentHp - o.PlayHpLostMin <= c.MaxHp * Sts2State.Var(r, "HpThreshold", 50) / 100m,
                _ => null,
            };
            if (at != null) Add(r, false, at);
        }
        // Rupture (Strength when you lose HP on your turn).
        if (_me.Creature.Powers.FirstOrDefault(p => p is RupturePower) is { } rupture) Add(rupture, false, losesHp);

        // Lethal (decision #87): the card raises an enemy's chance of dying before it acts, for sure: its explored deaths (Low)
        // beat everything ending the turn now could give (High). Certain kills always count; a chance only with chances on.
        var chances = _ctx.Settings.ShowDeathChance;
        SimChance? Lethal(SimOutcome o)
        {
            SimChance? best = null;
            foreach (var id in te.Ids.Values)
            {
                var c = o.DeadChanceRange(id);
                if (!(c.Certain || chances) || c.Low <= 0 || c.Low <= baseline.DeadChanceRange(id).High) continue;
                if (best == null || c.Low > best.Value.Low) best = c;
            }
            return best;
        }
        var perAim = outcomes.Select(Lethal).ToList();
        if (perAim.All(c => c == null)) return false;
        var top = perAim.Where(c => c != null).Select(c => c!.Value).OrderByDescending(c => c.Low).First();
        lethalChance = top.Certain ? null : top.ShortText();
        // Aimed: some aim doesn't give it (or not as surely as the best one).
        lethalAimed = outcomes.Length > 1 && perAim.Any(c => c == null || c.Value.Low < top.Low);
        return true;
    }

    /// <summary>
    /// The sources whose tabs come from the end-of-turn comparisons by type. Not referenced in code: the settings check
    /// (CatalogCheck) reads the string literals compiled into this class, and these names are how it finds those rules.
    /// </summary>
    internal static readonly string[] OutcomeTabs =
    {
        nameof(HandDrill), nameof(ParryingShield), nameof(GremlinHorn), nameof(CompostPower), nameof(RupturePower),
        nameof(SelfFormingClay), nameof(EmotionChip), nameof(DemonTongue), nameof(RedSkull),
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
    private void LogTab(AbstractModel src, int? ownerId, SimOutcome[] outcomes, Dictionary<string, TabSpec> tabs, TabKind? kindOverride = null)
    {
        if (src is PowerModel && SimAdapter.IsTemporaryThorns(src.GetType())) return; // its Thorns has the ThornsPower tab
        var name = SimReaders.ModelName(src);
        var model = SimModels.Get(name);
        if ((kindOverride ?? model?.Tab) is not { } kind) return;
        var harmful = model?.Harmful ?? false;
        // An enemy's or a teammate's source reads its own key (SimState.LogFired: a teammate's firings are only there); yours, the
        // plain name.
        var key = ownerId is { } id ? $"{name}@{id}" : name;
        static int Data(PowerSet log, string k) => log.DataOf(k);
        bool Fired(PowerSet log) => log.Has(key) && (_indirect || log[key] > log["~" + key]);
        {
            // For the audits, direct or not (the game flashes either way), whether or not a tab shows.
            var srcName = src.GetType().Name;
            var every = outcomes.Select(o => o.PlayFiredEvery.Has(key)).ToArray();
            var any = outcomes.Select(o => o.PlayFiredAny.Has(key)).ToArray();
            if (_firesBy.TryGetValue(srcName, out var had) && had.Every.Length == every.Length)
                for (var i = 0; i < every.Length; i++) { every[i] |= had.Every[i]; any[i] |= had.Any[i]; }
            _firesBy[srcName] = (src.GetType(), every, any);
        }
        Func<SimOutcome, bool> at = harmful ? o => Fired(o.PlayFiredAny) : o => Fired(o.PlayFiredEvery);
        var shown = outcomes.Any(at);
        if (!shown) return;
        // Which targets give the tab, across every source sharing it (several enemies with the same power).
        var tabKeyAt = "proc:" + src.GetType().Name;
        var firesAt = outcomes.Select(at).ToArray();
        if (_firesAt.TryGetValue(tabKeyAt, out var prevAt) && prevAt.Length == firesAt.Length)
            for (var i = 0; i < firesAt.Length; i++) firesAt[i] |= prevAt[i];
        _firesAt[tabKeyAt] = firesAt;
        var aimed = firesAt.Length > 1 && !firesAt.All(x => x);
        var amount = harmful
            ? outcomes.Max(o => Data(o.PlayFiredAny, key))
            : outcomes.Where(o => Fired(o.PlayFiredEvery)).Max(o => Data(o.PlayFiredEvery, key));
        var min = outcomes.Min(o => Data(o.PlayFiredEvery, key));
        var max = outcomes.Max(o => Data(o.PlayFiredAny, key));
        if (kind != TabKind.Trigger && max <= 0) return;
        var tabKey = "proc:" + src.GetType().Name;
        if (tabs.TryGetValue(tabKey, out var prev))
        {
            amount += prev.Amount;
            min += prev.AuditMin;
            max += prev.AuditMax;
        }
        int? number = kind != TabKind.Trigger && _ctx.Settings.ProcNumbers ? amount : null;
        TabAmount? counts = kind switch
        {
            TabKind.Block => TabAmount.Block, TabKind.Damage => TabAmount.Damage, TabKind.Summon => TabAmount.Summon,
            TabKind.Draw => TabAmount.CardsDrawn, TabKind.Energy => TabAmount.EnergyGained, // decision #132
            _ => null,
        };
        tabs[tabKey] = new TabSpec(tabKey, IconOf(src), harmful, number, counts, amount, min, max, Aimed: aimed);
    }

    /// <summary>
    /// A tab shown because of some targets but not all of them (decision #55): the card is aimed at several enemies or teammates
    /// (one simulation each) and at least one of them doesn't give it. It gets the aim marker.
    /// </summary>
    private static bool Aimed(SimOutcome[] outcomes, Func<SimOutcome, bool> at) => outcomes.Length > 1 && !outcomes.All(at);


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
