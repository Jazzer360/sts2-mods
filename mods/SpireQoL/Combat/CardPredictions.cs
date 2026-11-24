using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;
using SpireQoL.Cards;
using SpireQoL.Procs;
using SpireQoL.Sim;
using Sts2Mods.Core.Game;

namespace SpireQoL.Combat;

/// <summary>
/// The prediction for every card in your hand (decision #48, docs/SIMULATOR.md): each card's prediction (CardPrediction), its play simulated
/// once per target against ending the turn now, and the proc tabs that come from it, with everything kept between frames (the
/// simulations, reused results while nothing changes: decision #114, held tabs while a result is on its way: decision #105). Proc
/// Indicators draws the tabs; the play check (ProcAudit), the error reports and Shuffle Warning read the same predictions here. Its tab
/// rules: the models that fired during the play (the play log) with the block, damage, draws or energy they gave; the tabs judged on
/// how the turn ends (SimModel.OutcomeTab); lethal (decision #87); cards coming back; free cards. A good effect needs every branch of
/// some aim, a warning one branch of any aim.
/// </summary>
public sealed class CardPredictions
{
    private readonly QolContext _ctx;

    public CardPredictions(QolContext ctx) => _ctx = ctx;

    private readonly HashSet<string> _warned = new();

    // Per-frame context.
    private Player _me = null!;
    private ICombatState _cs = null!;
    private bool _indirect;

    /// <summary>The card being finished: for each proc tab, which of its targets (simulations) give it (the aim marker).</summary>
    private readonly Dictionary<string, bool[]> _firesAt = new();
    /// <summary>Per tabbed source this frame's card: whether each aim sets it off in every outcome and in some (CardPrediction.FiresEveryByAim).</summary>
    private readonly Dictionary<string, (Type T, bool[] Every, bool[] Any)> _firesBy = new();

    /// <summary>Each card's prediction from the last frame it was in hand (kept after it leaves; ProcAudit).</summary>
    public IReadOnlyDictionary<CardModel, CardPrediction> LastPredictions => _lastPredictions;

    private readonly Dictionary<CardModel, CardPrediction> _lastPredictions = new(ReferenceEqualityComparer.Instance);

    public IReadOnlyDictionary<CardModel, CardPrediction> PreviousPredictions => _previousPredictions;

    /// <summary>Last frame's prediction and this frame's swap places every frame: two dictionaries, none made per frame.</summary>
    private Dictionary<CardModel, CardPrediction> _previousPredictions = new(ReferenceEqualityComparer.Instance);

    private Dictionary<CardModel, CardPrediction> _predictions = new(ReferenceEqualityComparer.Instance);
    /// <summary>The frame _predictions were filled on (worked out or reused).</summary>
    private ulong _predictionsFrame = ulong.MaxValue;
    /// <summary>Last frame's FrameKey, kept while the board probe sees nothing change.</summary>
    private long _lastFrameKey;
    /// <summary>The board probe _lastFrameKey was worked out from.</summary>
    private int _lastFrameKeyProbe;

    /// <summary>
    /// This frame's prediction for a card in your hand, when they've been worked out (or reused) this frame: Shuffle Warning, which runs
    /// after Proc Indicators, reads them instead of building them again. Null otherwise.
    /// </summary>
    internal CardPrediction? PredictionThisFrame(CardModel card) =>
        _predictionsFrame == Godot.Engine.GetProcessFrames() && _predictions.TryGetValue(card, out var prediction) ? prediction : null;

    /// <summary>
    /// This frame's predictions for your hand: each card's tabs (every one, before the settings hide any), or null when there's no
    /// hand to predict (outside your turn). Once a frame, from Proc Indicators.
    /// </summary>
    public IReadOnlyList<(CardModel Card, List<TabSpec> Tabs)>? Update()
    {
        // Keep last frame's prediction for the audit, even on frames where nothing is computed (the
        // hand just emptied, the turn is ending).
        if (_predictions.Count > 0 || _previousPredictions.Count > 0)
        {
            (_previousPredictions, _predictions) = (_predictions, _previousPredictions);
            _predictions.Clear();
        }

        var me = Sts2State.Me;
        var combat = Sts2State.Combat;
        if (me == null || combat == null || !Sts2State.IsPlayerTurn) return null;
        var hand = Sts2State.Hand(me);
        FreeCards.Observe(me, hand);
        if (hand.Count == 0) return null;
        // The models are still being read (the mod just loaded): every card's tabs are pending, never worked out without them.
        if (!SimWait.Models()) return null;

        _me = me;
        _cs = combat;
        _indirect = _ctx.Settings.ProcIndirect;

        // Nothing changed since a frame whose every result was final: its prediction and tabs again, without reading each card again
        // (decision #114). In dev mode once a second such a frame is worked out anyway and checked against what would have been reused.
        // The forecast's start, not its result (decision #166): every card's search starts from it alongside the forecast's own.
        var begun = Start();
        // The frame key reads every card through the game's hooks (its play count, whether it can be played): on a frame where the
        // board probe saw nothing change (decision #130), last frame's key stands.
        var probe = BoardProbe.Hash(me, combat);
        var probeSame = begun != null && _lastFrameKey != 0 && _lastFrameKeyProbe == probe;
        var key = begun == null ? 0 : probeSame ? _lastFrameKey : FrameKey(begun, hand);
        _lastFrameKey = key;
        _lastFrameKeyProbe = probe;
        var reuse = _reuse.Decide(key != 0 && key == _reuseKey);
        if (reuse == FrameReuse.Reuse) return Reuse(combat);
        var checkReuse = reuse == FrameReuse.WorkOutAndCheck;
        if (checkReuse && probeSame) _reuse.Compare(FrameKey(begun!, hand) == key, "its frame key changed while the board probe didn't");

        // Sources: your powers, your relics, and powers on enemies (theirs, or ones you applied).
        var sources = Sources(me, combat);

        var pending = new List<(CardModel Card, Prepared Prepared)>();
        foreach (var card in hand)
        {
            // Nothing happens on play for a card that can't be played (Unplayable statuses and
            // curses, blocked cards, cards whose own condition isn't met). Cards you just can't
            // afford yet still show, since energy can come mid-turn.
            if (!CardPlays.CanPlayThisTurn(card)) continue;
            if (Prepare(card, sources, record: true) is not { } prepared) continue;
            pending.Add((card, prepared));
        }

        // Every card's simulations (one per target) and the forecast's run in parallel across CPU cores, all started before any wait
        // (decision #166); the frame waits for them together within its few milliseconds (SimWait, decision #104), so tabs usually
        // appear on the frame they belong to. What isn't ready by then shows when it is, a frame or a few later (SimPending); a
        // simulation that missed its frame never makes a later one wait.
        var jobs = pending.Where(cardPrepared => cardPrepared.Prepared.Sim != null).SelectMany(cardPrepared => cardPrepared.Prepared.Sim!.Jobs).ToList();
        if (begun != null) jobs.Add(begun.Job);
        SimWait.All(jobs);

        // Held (decision #105): a card whose simulation, or the turn forecast it compares with, has no result for this board yet keeps
        // the tabs it showed this turn until it has one, instead of losing them for a few frames (flicker on every hit of a busy
        // play). Each card switches as soon as its own result is in. A card with nothing to hold shows what it has.
        var forecastPending = TurnEnd() == null;
        foreach (var card in hand)
            if (!pending.Any(cardPrepared => ReferenceEquals(cardPrepared.Card, card))) _lastTabs.Remove(card);
        var final = !forecastPending;
        var shown = new List<(CardModel Card, List<TabSpec> Tabs)>();
        var result = new List<(CardModel Card, List<TabSpec> Tabs)>(pending.Count);
        foreach (var (card, otherPrepared) in pending)
        {
            if ((forecastPending || otherPrepared.Sim is { IsCompleted: false }) && Held(card, combat) is { } held)
            {
                _heldFrames++;
                final = false;
                result.Add((card, held));
                continue;
            }
            if (otherPrepared.Sim is { IsCompleted: false }) final = false;
            var tabs = Finish(card, otherPrepared);
            _lastTabs[card] = (_ctx.Decor.Frame, tabs, combat, Sts2State.TurnNumber);
            if (_lastTabs.Count > 300) _lastTabs.Clear();
            shown.Add((card, tabs));
            result.Add((card, tabs));
        }

        if (checkReuse) CheckReuse(shown);
        // Reusable only once every result is final: a pending simulation or forecast changes the tabs with nothing else changing.
        _reuseKey = final ? key : 0;
        _reuseTabs = shown;
        return result;
    }

    // ------------------------------------------------------------------ reuse while nothing changes (decision #114)

    private long _reuseKey;
    private List<(CardModel Card, List<TabSpec> Tabs)> _reuseTabs = new();
    private readonly ReuseCheck _reuse = new("the card predictions");

    /// <summary>
    /// What this frame's tabs depend on: the board (the turn forecast's state key: every creature, power, relic, orb, pile and
    /// count the simulator reads), the fight and turn, the settings that change tabs, and each card in hand with what it shows
    /// and can do (its numbers, cost, keywords, affliction, plays, whether it can be played now). Not where you aim: a tab that
    /// needs aim is worked out for every target (decision #55). Aim-dependent tabs would need the target in this key.
    /// </summary>
    private long FrameKey(TurnEndModel.ForecastStart begun, IReadOnlyList<CardModel> hand)
    {
        var hashCode = new HashCode();
        hashCode.Add(begun.StateKey);
        hashCode.Add(System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(_cs));
        hashCode.Add(Sts2State.TurnNumber);
        hashCode.Add(_indirect);
        hashCode.Add(_ctx.Settings.ProcNumbers);
        foreach (var card in hand)
        {
            hashCode.Add(System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(card));
            AddCardNumbers(ref hashCode, card);
            try { hashCode.Add(card.EnergyCost.GetWithModifiers(CostModifiers.All)); } catch { hashCode.Add(-99); }
            foreach (var cardKeyword in card.Keywords) hashCode.Add(cardKeyword);
            hashCode.Add(card.Affliction?.GetType().Name);
            hashCode.Add(CardPlays.Count(card));
            hashCode.Add(CardPlays.CanPlayThisTurn(card));
        }
        var key = hashCode.ToHashCode();
        return key == 0 ? 1 : key;
    }

    /// <summary>Last frame's prediction and tabs again: kept for the audits (PreviousPredictions, LastTabs) as a frame of their own.</summary>
    private List<(CardModel Card, List<TabSpec> Tabs)> Reuse(ICombatState combat)
    {
        foreach (var entry in _previousPredictions) _predictions[entry.Key] = entry.Value;
        _predictionsFrame = Godot.Engine.GetProcessFrames();
        foreach (var (card, tabs) in _reuseTabs) _lastTabs[card] = (_ctx.Decor.Frame, tabs, combat, Sts2State.TurnNumber);
        return _reuseTabs;
    }

    /// <summary>Dev mode: a frame worked out where the last one's tabs would have been reused; a difference means the key missed something.</summary>
    private void CheckReuse(List<(CardModel Card, List<TabSpec> Tabs)> now)
    {
        foreach (var (card, tabs) in now)
        {
            var before = _reuseTabs.FirstOrDefault(reuseTab => ReferenceEquals(reuseTab.Card, card)).Tabs;
            _reuse.Compare(before == null || before.SequenceEqual(tabs), "reused tabs went stale with no change the frame key sees",
                () => $"{card.GetType().Name}: reused [{string.Join(", ", before!.Select(tab => tab.Key))}], now [{string.Join(", ", tabs.Select(tab => tab.Key))}]");
        }
    }

    /// <summary>The tabs the card last showed from a result of its own, this turn of this fight; null when there are none to hold.</summary>
    private List<TabSpec>? Held(CardModel card, ICombatState combat)
        => _lastTabs.TryGetValue(card, out var last) && ReferenceEquals(last.Combat, combat) && last.Turn == Sts2State.TurnNumber ? last.Tabs : null;

    /// <summary>Card frames shown with held tabs since the last call (the combat summary: decision #105).</summary>
    internal int TakeHeldFrames()
    {
        var heldFrames = _heldFrames;
        _heldFrames = 0;
        return heldFrames;
    }

    private int _heldFrames;

    /// <summary>
    /// Each card's proc tabs from the last frame it was in hand with a result of its own (not held: decision #105), as this feature
    /// gave them to the card (every one, before the settings hide any), with the frame, fight and turn: what ProcAudit holds against its prediction as the play begins (decision #103).
    /// </summary>
    public IReadOnlyDictionary<CardModel, (long Frame, List<TabSpec> Tabs, ICombatState Combat, int Turn)> LastTabs => _lastTabs;

    private readonly Dictionary<CardModel, (long Frame, List<TabSpec> Tabs, ICombatState Combat, int Turn)> _lastTabs = new(ReferenceEqualityComparer.Instance);

    /// <summary>A card's prediction, the proc sources this frame, and its running simulations.</summary>
    private sealed record Prepared(CardPrediction F, List<AbstractModel> Sources, SimBatch? Sim);


    /// <summary>The player whose Power cards a teammate's Imitation Learning copies (ImitationLearningPower.PlayerTarget).</summary>
    internal static Player? CopiesFrom(ImitationLearningPower imitationLearningPower)
    {
        try { return imitationLearningPower.PlayerTarget; } catch { return null; }
    }

    /// <summary>
    /// The proc sources (each with a tab when its model fires): your powers and relics, powers on every living enemy, and every
    /// living teammate's powers and relics (decision #75: what your card sets off for a teammate gets a tab like your own).
    /// </summary>
    private static List<AbstractModel> Sources(Player me, ICombatState combat)
    {
        var sources = new List<AbstractModel>();
        sources.AddRange(me.Creature.Powers);
        sources.AddRange(me.Relics); // each relic's tab has its own switch (Tabs "proc:<Relic>")
        // All living enemies: an untargetable one's powers still fire.
        foreach (var enemy in combat.Enemies.Where(creature => creature.IsAlive)) sources.AddRange(enemy.Powers);
        foreach (var mate in SimAdapter.Teammates(me, combat))
        {
            if (!mate.Creature.IsAlive) continue;
            sources.AddRange(mate.Creature.Powers);
            sources.AddRange(mate.Relics.Where(relic => !relic.IsMelted));
        }
        return sources;
    }

    /// <summary>
    /// A power's creature, or null for a canonical stand-in (WithOwnPowers: a power the card gives you that you don't have yet,
    /// which reads as yours). Reading Owner on a canonical model throws (AbstractModel.AssertMutable: issue #426).
    /// </summary>
    internal static Creature? OwnerOf(PowerModel power) => power.IsMutable ? power.Owner : null;

    /// <summary>The simulator's id of the teammate a source belongs to (their power or relic), or null for yours and enemies'.</summary>
    private static int? TeammateOf(AbstractModel src, Player me, ICombatState combat)
    {
        var owner = src switch
        {
            PowerModel power when OwnerOf(power) is { IsPlayer: true } creature => creature.Player,
            RelicModel relic => relic.Owner,
            _ => null,
        };
        if (owner == null || owner == me) return null;
        var teammateIndex = SimAdapter.Teammates(me, combat).IndexOf(owner);
        return teammateIndex < 0 ? null : Who.Teammate(teammateIndex);
    }

    /// <summary>
    /// One card's prediction, up to its simulations (started, not awaited): the same code for the tabs on screen and for the
    /// audit's prediction at resolution (PredictNow). record: keep its prediction for the audit and the play queue.
    /// </summary>
    private Prepared? Prepare(CardModel card, List<AbstractModel> sources, bool record)
    {
        CardPrediction prediction;
        try
        {
            prediction = CardPrediction.For(card, _me, CardPlays.Count(card));
        }
        catch (Exception ex)
        {
            if (_warned.Add(card.GetType().Name))
            {
                ModEntry.Log.Warn($"proc prediction failed for {card.GetType().Name}: {ex.Message}");
                Sts2Mods.Core.Diagnostics.ErrorReporter.Instance?.Exception(ex, "play-prediction:" + card.GetType().Name);
            }
            return null;
        }

        if (record)
        {
            _predictions[card] = prediction;
            _predictionsFrame = Godot.Engine.GetProcessFrames();
            _lastPredictions[card] = prediction;
            if (_lastPredictions.Count > 300) _lastPredictions.Clear();
        }
        SimBatch? sim = null;
        try
        {
            sim = StartSim(card, prediction);
        }
        catch (Exception ex)
        {
            SimFailed(card, ex);
        }
        return new Prepared(prediction, WithOwnPowers(card, sources), sim);
    }

    /// <summary>
    /// The sources plus each power the card gives you that you don't have yet and that has a tab: it lands during the play and
    /// can fire on that same play (Pale Blue Dot played as your 5th card draws next turn at once). The power's canonical model
    /// stands in for it (its icon and name; no owner, so it reads as yours).
    /// </summary>
    private static List<AbstractModel> WithOwnPowers(CardModel card, List<AbstractModel> sources)
    {
        List<AbstractModel>? result = null;
        foreach (var type in Diagnostics.CardCoverage.PowersApplied(card.GetType()))
        {
            if (!typeof(PowerModel).IsAssignableFrom(type) || SimReaders.IsDebuffType(type)) continue;
            if (SimModels.Get(SimAdapter.SimPowerName(type))?.Tab == null) continue;
            if (card.Owner?.Creature.Powers.Any(power => power.GetType() == type) != false) continue;
            if (ModelDb.GetById<PowerModel>(ModelDb.GetId(type)) is not { } canon) continue;
            (result ??= new List<AbstractModel>(sources)).Add(canon);
        }
        return result ?? sources;
    }

    /// <summary>A prepared card's tabs, once its simulations are done.</summary>
    private List<TabSpec> Finish(CardModel card, Prepared prepared)
    {
        var tabs = new Dictionary<string, TabSpec>();
        var lethal = false;
        var lethalAimed = false;
        string? lethalChance = null;
        try
        {
            lethal = prepared.Sim != null && FinishSim(prepared.F, prepared.Sources, prepared.Sim, tabs, out lethalAimed, out lethalChance);
        }
        catch (Exception ex)
        {
            SimFailed(card, ex);
        }
        var list = tabs.Values.ToList();
        if (lethal) list.Add(new TabSpec("lethal", GameIcons.Skull(), Aimed: lethalAimed, NumberText: lethalChance));
        // No Draw (Battle Trance) stops the draws this card would make, in every outcome of its simulation at some aim (its log: the engine's
        // ShouldDraw, decision #171): plain, with its icon (its art already shows the crossed card).
        if (prepared.F.OutcomeByAim is { Count: > 0 } byAim && byAim.Values.Any(outcome => outcome.PlayFiredEvery.Has(nameof(NoDrawPower)))
            && _me.Creature.GetPower<NoDrawPower>() is { } noDraw)
            list.Add(new TabSpec("proc:" + nameof(NoDrawPower), GameIcons.Power(noDraw)));
        // Cards this play brings back to your hand (Make It So, Right Hand Hand, Whistling Arrows): plain, one per card name,
        // with the returning card as the icon.
        foreach (var back in prepared.F.CardsBack.GroupBy(otherCard => otherCard.GetType().Name))
            list.Add(new TabSpec("return:" + back.Key, Icon: null, Card: back.First()));
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
    /// Null when it can't be built (outside your turn, the card's prediction failed).
    /// </summary>
    public (List<TabSpec> Tabs, CardPrediction Prediction)? PredictNow(CardModel card)
    {
        var self = this;
        var me = Sts2State.Me;
        var combat = Sts2State.Combat;
        if (me == null || combat == null) return null;
        if (TurnEndModel.ComputeLive() is not { } live) return null;
        var (savedMe, savedCs, savedTe, savedStart, savedFrame, savedStartFrame) = (self._me, self._cs, self._turnEnd, self._start, self._turnEndFrame, self._startFrame);
        self._me = me;
        self._cs = combat;
        self._indirect = self._ctx.Settings.ProcIndirect;
        self._turnEnd = live.Model;
        self._start = live.Start;
        self._turnEndFrame = self._startFrame = self._ctx.Decor.Frame;
        self._noCache = true;
        try
        {
            if (self.Prepare(card, Sources(me, combat), record: false) is not { } prepared) return null;
            if (prepared.Sim is { IsCompleted: false } batch) SimWait.All(batch.Jobs, ownBudget: true);
            return (self.Finish(card, prepared), prepared.F);
        }
        finally
        {
            // Uncached: nothing reads these simulations again.
            self._uncached.ForEach(batch => batch.Cancel());
            self._uncached.Clear();
            self._noCache = false;
            (self._me, self._cs, self._turnEnd, self._start, self._turnEndFrame, self._startFrame) = (savedMe, savedCs, savedTe, savedStart, savedFrame, savedStartFrame);
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
    private SimBatch? StartSim(CardModel card, CardPrediction prediction)
    {
        var forecast = Start();
        if (forecast == null) return null;

        // A card aimed at any enemy, or at any teammate (AnyAlly: one simulation per living teammate, Who.Teammate),
        // is simulated once per target; a tab that needs some aim shows when one target gives it.
        var targets = card.TargetType switch
        {
            TargetType.AnyEnemy => forecast.Ids.Where(entry => entry.Key.IsAlive && entry.Key.IsHittable).Select(entry => entry.Value).ToList(),
            TargetType.AnyAlly => Enumerable.Range(0, forecast.Teammates.Count).Select(Who.Teammate).ToList(),
            _ => new List<int> { Who.None },
        };
        if (targets.Count == 0) targets.Add(Who.None); // aimed at an enemy with none to aim at: at no one

        var stateKey = forecast.StateKey;
        if (!_noCache && stateKey != _simKey)
        {
            _simKey = stateKey;
            foreach (var old in _simCache.Values) old.Cancel(); // the board changed: their explorations stop
            _simCache.Clear();
        }
        // The card's own numbers too: upgrading it in hand (Blessing of the Forge, Armaments) or a value that grows (Claw)
        // changes its play without changing the board.
        var cacheKey = $"{card.GetHashCode()}|{prediction.Plays}|{prediction.Energy}|{BoardCountsKey(card)}|{CardNumbers(card)}";
        // The plays are built here (they read the game); only the simulation runs off the main thread.
        var inputs = new SimInputs
        {
            Start = forecast.State, Ids = forecast.Ids, Allies = forecast.Teammates.Select(player => player.Creature).ToList(),
            FullStart = ReferenceEquals(forecast.FullState, forecast.State) ? null : forecast.FullState,
        };
        foreach (var target in targets) inputs.PlayByTarget[target] = SimAdapter.Play(card, _me, _cs, forecast.Ids, target, plays: prediction.Plays);
        prediction.Sim = inputs;
        if (_noCache || !_simCache.TryGetValue(cacheKey, out var task))
        {
            var start = forecast.State;
            var name = card.GetType().Name;
            task = new SimBatch(targets.Select(target => SimJob.Start(SimSearch.Of(start, inputs.PlayByTarget[target]), $"{name} (target {target})")).ToArray());
            if (!_noCache) _simCache[cacheKey] = task;
            else _uncached.Add(task);
        }
        return task;
    }

    /// <summary>
    /// What the card's program gives on the board that the board's key may not show (its draws, hits, block gains, made cards: BoardCounts),
    /// for the simulation cache: a play built differently is simulated again.
    /// </summary>
    private static string BoardCountsKey(CardModel card)
    {
        try
        {
            var hand = Sts2State.Hand(card.Owner).Count;
            return $"{Procs.Programs.BoardCounts.CardsDrawn(card, hand)}|{Procs.Programs.BoardCounts.HitsPerPlay(card, card.CombatState!, target: null)}|"
                   + $"{Procs.Programs.BoardCounts.BlockGainCount(card)}|{Procs.Programs.MadeCards.Summary(card).Made}";
        }
        catch
        {
            return "?";
        }
    }

    /// <summary>CardNumbers as a hash, for the key worked out every frame: the same numbers, no string built.</summary>
    private static void AddCardNumbers(ref HashCode hashCode, CardModel card)
    {
        try
        {
            hashCode.Add(card.CurrentUpgradeLevel);
            hashCode.Add(card.Enchantment?.GetType());
            hashCode.Add(card.Enchantment?.Amount);
            foreach (var entry in card.DynamicVars) { hashCode.Add(entry.Key); hashCode.Add(entry.Value.BaseValue); }
        }
        catch
        {
            hashCode.Add(-1);
        }
    }

    /// <summary>The card's upgrade level, enchantment and every dynamic value, for the simulation cache.</summary>
    private static string CardNumbers(CardModel card)
    {
        try
        {
            var text = new System.Text.StringBuilder();
            text.Append(card.CurrentUpgradeLevel).Append('|').Append(card.Enchantment?.GetType().Name).Append(card.Enchantment?.Amount);
            foreach (var entry in card.DynamicVars) text.Append('|').Append(entry.Key).Append('=').Append(entry.Value.BaseValue);
            return text.ToString();
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
    private static void DrawnTab(CardPrediction prediction, SimOutcome[] outcomes, Dictionary<string, TabSpec> tabs)
    {
        if (outcomes.Length == 0 || SimAdapter.DrawnCardReaderOf(prediction.Card.GetType()) is not { } reader) return;
        if (DrawnChance.Distribution(outcomes[0]) is not { } distribution) return;
        prediction.Drawn = distribution;
        var unsure = outcomes[0].Truncated || outcomes[0].AnyUnmodelled;
        string Pct(double share) => new SimChance(share, share, share > 0).Text() + (unsure ? "?" : "");
        if (!reader.Counts)
        {
            tabs["drawn"] = new TabSpec("drawn", Icon: null, Text: Pct(DrawnChance.ChanceOfAny(distribution)));
            return;
        }
        var (n, chance) = DrawnChance.MostLikely(distribution);
        tabs["drawn"] = new TabSpec("drawn", Icon: null, Text: Pct(chance), NumberText: n.ToString());
    }

    /// <summary>
    /// Reads a card's simulations into its tabs; returns whether it's lethal (some aim makes an enemy surely more likely to die
    /// before it acts than ending the turn now does: decision #87), in <paramref name="lethalAimed"/> whether only some aims are,
    /// and in <paramref name="lethalChance"/> the chance ("40%", "40–55%" while exploring; null when certain).
    /// </summary>
    private bool FinishSim(CardPrediction prediction, List<AbstractModel> sources, SimBatch task, Dictionary<string, TabSpec> tabs, out bool lethalAimed,
        out string? lethalChance)
    {
        lethalAimed = false;
        lethalChance = null;
        var forecast = TurnEnd();
        if (forecast == null) return false;
        if (!task.IsCompleted)
        {
            prediction.SimPending = true;
            return false;
        }
        if (task.Fault is { } fault) throw fault;
        var outcomes = task.Latest;
        _firesAt.Clear();
        _firesBy.Clear();
        var baseline = forecast.Outcome;
        if (outcomes.Any(outcome => outcome.Truncated || outcome.Branches.Count > 1)) prediction.RandomOutcome = true;
        prediction.SimIncomplete = outcomes.Any(outcome => outcome.Truncated);
        prediction.Unmodelled = outcomes.SelectMany(outcome => outcome.UnmodelledReasons).Distinct().ToList();
        if (!_noCache) Diagnostics.DamageFormulaAudit.Check(prediction.Card, prediction, forecast.Ids, outcomes);
        DrawnTab(prediction, outcomes, tabs);

        // ---- the play log: each source whose model fired during the play
        // A source's owner in the simulation: an enemy's id for its powers, a teammate's for theirs; null for yours (or an enemy no
        // longer in the fight).
        bool SimOwner(AbstractModel src, out int? owner)
        {
            owner = TeammateOf(src, _me, _cs);
            if (src is not PowerModel power || OwnerOf(power) is not { IsEnemy: true } enemyOwner) return true;
            if (!forecast.Ids.TryGetValue(enemyOwner, out var eid)) return false;
            owner = eid;
            return true;
        }
        foreach (var src in sources)
            if (SimOwner(src, out var ownerId)) LogTab(src, ownerId, outcomes, tabs);
        // Effects the engine logs itself (Juggernaut on every block gain, Thunder on Lightning evokes): logged under their power's
        // name, yours and every teammate's.
        foreach (var src in sources.OfType<PowerModel>().Where(power => OwnerOf(power) is { IsPlayer: true } && power.GetType().Name is nameof(JuggernautPower) or nameof(ThunderPower)))
            LogTab(src, TeammateOf(src, _me, _cs), outcomes, tabs, TabKind.Damage);

        // ---- end-of-turn comparisons: what the play changes by the time the enemies act
        // Each rule is a test per target (one simulation each): the tab shows when some target passes it, with the aim marker
        // when not every target does (several enemies with the same power: when no target passes any of them).
        void Add(AbstractModel src, bool harmful, Func<SimOutcome, bool> at, Func<SimState, bool>? inBranch, Func<SimOutcome, bool>? inSome, bool inPlay = true)
        {
            var key = "proc:" + src.GetType().Name;
            var firesAt = outcomes.Select(at).ToArray();
            // For the audits, per aim, as the play-log tabs are (LogTab): a bonus in every outcome where it's set off, in some where some
            // outcome sets it off; a warning (set off in some outcome) in some only.
            var every = harmful ? new bool[firesAt.Length] : firesAt.ToArray();
            var any = outcomes.Select((outcome, index) => firesAt[index] || inSome?.Invoke(outcome) == true).ToArray();
            var srcName = src.GetType().Name;
            // Set off after the play (OutcomeRule.InPlay false): nothing for the play check to see flash.
            if (inPlay)
            {
                if (_firesBy.TryGetValue(srcName, out var had) && had.Every.Length == every.Length)
                    for (var i = 0; i < every.Length; i++) { every[i] |= had.Every[i]; any[i] |= had.Any[i]; }
                _firesBy[srcName] = (src.GetType(), every, any);
            }
            if (!firesAt.Any(fires => fires)) return;
            if (_firesAt.TryGetValue(key, out var prev) && prev.Length == firesAt.Length)
                for (var i = 0; i < firesAt.Length; i++) firesAt[i] |= prev[i];
            _firesAt[key] = firesAt;
            // A warning only some outcomes set off: its chance (decision #159), the likeliest aim's.
            string? chance = null;
            if (harmful && inBranch != null && _ctx.Settings.Chances)
                chance = WarningChance(outcomes.Where(at).Select(outcome => outcome.Chance(inBranch)));
            tabs[key] = new TabSpec(key, IconOf(src), harmful, NumberText: chance, Aimed: firesAt.Length > 1 && !firesAt.All(fires => fires));
        }
        // The sources' own rules (SimModel.OutcomeTab), enemies' powers first, then relics, then players' powers.
        var start = prediction.Sim!.Start;
        foreach (var src in sources.OrderBy(model => model is PowerModel power && OwnerOf(power) is { IsEnemy: true } ? 0 : model is RelicModel ? 1 : 2))
        {
            if (SimModels.Get(SimReaders.ModelName(src)) is not { JudgedOnOutcome: true } model || !SimOwner(src, out var owner)) continue;
            if (model.OutcomeTab(start, baseline, owner ?? Who.You) is { } rule) Add(src, rule.Harmful, rule.SetOff, rule.InBranch, rule.InSome, rule.InPlay);
        }

        // What the audits hold the play to, per aim (decisions #87, #95): the tabbed sources it sets off in every outcome and in
        // some, and each enemy's chance of dying before it acts.
        if (prediction.Sim?.PlayByTarget.Keys.ToList() is { } aims && aims.Count == outcomes.Length)
        {
            IReadOnlyDictionary<int, IReadOnlyCollection<Type>> By(Func<(Type T, bool[] Every, bool[] Any), bool[]> at) =>
                aims.Select((aim, index) => (aim, index)).ToDictionary(aimed => aimed.aim, aimed => (IReadOnlyCollection<Type>)_firesBy.Values.Where(value => at(value)[aimed.index]).Select(value => value.T).Distinct().ToList());
            prediction.FiresEveryByAim = By(fired => fired.Every);
            prediction.FiresAnyByAim = By(fired => fired.Any);
            var enemies = forecast.Ids.Values.Distinct().ToList();
            prediction.DeathByAim = aims.Select((aim, index) => (aim, index)).ToDictionary(aimed => aimed.aim,
                aimed => (IReadOnlyDictionary<int, SimChance>)enemies.ToDictionary(id => id, id => outcomes[aimed.index].DeadChanceRange(id)));
            prediction.OutcomeByAim = aims.Select((aim, index) => (aim, index)).ToDictionary(aimed => aimed.aim, aimed => outcomes[aimed.index]);

        }


        // Lethal (decision #87): the card raises an enemy's chance of dying before it acts, for sure: its explored deaths (Low)
        // beat everything ending the turn now could give (High). Certain kills always count; a chance only with chances on.
        var chances = _ctx.Settings.ShowDeathChance;
        SimChance? Lethal(SimOutcome outcome)
        {
            SimChance? best = null;
            foreach (var id in forecast.Ids.Values)
            {
                var chance = outcome.DeadChanceRange(id);
                if (!(chance.Certain || chances) || chance.Low <= 0 || chance.Low <= baseline.DeadChanceRange(id).High) continue;
                if (best == null || chance.Low > best.Value.Low) best = chance;
            }
            return best;
        }
        var perAim = outcomes.Select(Lethal).ToList();
        if (perAim.All(chance => chance == null)) return false;
        var top = perAim.Where(chance => chance != null).Select(chance => chance!.Value).OrderByDescending(chance => chance.Low).First();
        lethalChance = top.Certain ? null : top.ShortText();
        // Aimed: some aim doesn't give it (or not as surely as the best one).
        lethalAimed = outcomes.Length > 1 && perAim.Any(chance => chance == null || chance.Value.Low < top.Low);
        return true;
    }

    /// <summary>
    /// A warning's chance on its tab (decision #159): the likeliest aim's chance that the play sets it off, as the tab's corner text
    /// ("40%", "40%?" while not every outcome is settled); null when it's certain, so the tab alone says it.
    /// </summary>
    private static string? WarningChance(IEnumerable<SimChance> perAim)
    {
        var chances = perAim.ToList();
        if (chances.Count == 0) return null;
        var likeliest = chances.MaxBy(chance => chance.High);
        return likeliest.Certain ? null : likeliest.ShortText();
    }

    private static Godot.Texture2D? IconOf(AbstractModel src) => src switch
    {
        RelicModel relic => GameIcons.Relic(relic),
        PowerModel power => GameIcons.Power(power),
        _ => null,
    };

    /// <summary>
    /// A source's tab from the play log (docs/SIMULATOR.md "Tabs from the log"): its model fired during the play, in every
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
        var key = SimKeys.Logged(name, ownerId);
        static int Data(NamedCounts log, string logKey) => log.DataOf(logKey);
        bool Fired(NamedCounts log) => log.Has(key) && (_indirect || log[key] > log[SimKeys.Indirect(key)]);
        {
            // For the audits, direct or not (the game flashes either way), whether or not a tab shows.
            var srcName = src.GetType().Name;
            var every = outcomes.Select(outcome => outcome.PlayFiredEvery.Has(key)).ToArray();
            var any = outcomes.Select(outcome => outcome.PlayFiredAny.Has(key)).ToArray();
            if (_firesBy.TryGetValue(srcName, out var had) && had.Every.Length == every.Length)
                for (var i = 0; i < every.Length; i++) { every[i] |= had.Every[i]; any[i] |= had.Any[i]; }
            _firesBy[srcName] = (src.GetType(), every, any);
        }
        Func<SimOutcome, bool> at = harmful ? outcome => Fired(outcome.PlayFiredAny) : outcome => Fired(outcome.PlayFiredEvery);
        var shown = outcomes.Any(at);
        if (!shown) return;
        // Which targets give the tab, across every source sharing it (several enemies with the same power).
        var tabKeyAt = "proc:" + src.GetType().Name;
        var firesAt = outcomes.Select(at).ToArray();
        if (_firesAt.TryGetValue(tabKeyAt, out var prevAt) && prevAt.Length == firesAt.Length)
            for (var i = 0; i < firesAt.Length; i++) firesAt[i] |= prevAt[i];
        _firesAt[tabKeyAt] = firesAt;
        var aimed = firesAt.Length > 1 && !firesAt.All(fires => fires);
        var amount = harmful
            ? outcomes.Max(outcome => Data(outcome.PlayFiredAny, key))
            : outcomes.Where(outcome => Fired(outcome.PlayFiredEvery)).Max(outcome => Data(outcome.PlayFiredEvery, key));
        var min = outcomes.Min(outcome => Data(outcome.PlayFiredEvery, key));
        var max = outcomes.Max(outcome => Data(outcome.PlayFiredAny, key));
        if (kind != TabKind.Trigger && max <= 0) return;
        var tabKey = "proc:" + src.GetType().Name;
        if (tabs.TryGetValue(tabKey, out var prev))
        {
            amount += prev.Amount;
            min += prev.AuditMin;
            max += prev.AuditMax;
        }
        int? number = kind != TabKind.Trigger && _ctx.Settings.ProcNumbers ? amount : null;
        // A warning only some outcomes set off (a random hit on a Thorns enemy): its chance in the corner instead of its amount
        // (decision #159), from each branch's own log: fired in every branch merged into it (Fired) up to in some (FiredAny).
        string? chance = null;
        if (harmful && _ctx.Settings.Chances)
            chance = WarningChance(outcomes.Where(at).Select(outcome => outcome.LoggedChance(Fired)));
        if (chance != null) number = null;
        TabAmount? counts = kind switch
        {
            TabKind.Block => TabAmount.Block, TabKind.Damage => TabAmount.Damage, TabKind.Summon => TabAmount.Summon,
            TabKind.Draw => TabAmount.CardsDrawn, TabKind.Energy => TabAmount.EnergyGained, // decision #132
            TabKind.CardsMade => TabAmount.CardsMade, // decision #161
            _ => null,
        };
        tabs[tabKey] = new TabSpec(tabKey, IconOf(src), harmful, number, counts, amount, min, max, NumberText: chance, Aimed: aimed);
    }

    /// <summary>
    /// A tab shown because of some targets but not all of them (decision #55): the card is aimed at several enemies or teammates
    /// (one simulation each) and at least one of them doesn't give it. It gets the aim marker.
    /// </summary>


    /// <summary>PredictNow is running: the simulation cache (keyed to this frame's state) is left alone.</summary>
    private bool _noCache;

    private TurnEndModel? _turnEnd;
    private TurnEndModel.ForecastStart? _start;
    private long _turnEndFrame = -1;
    private long _startFrame = -1;

    /// <summary>The Incoming Damage simulation for this frame (computed once, shared by every card): its result, waited for.</summary>
    private TurnEndModel? TurnEnd()
    {
        if (_turnEndFrame != _ctx.Decor.Frame)
        {
            _turnEnd = TurnEndModel.Compute();
            _turnEndFrame = _ctx.Decor.Frame;
        }
        return _turnEnd;
    }

    /// <summary>The forecast's start for this frame (decision #166): the board the card searches start from, not waited for.</summary>
    private TurnEndModel.ForecastStart? Start()
    {
        if (_startFrame != _ctx.Decor.Frame)
        {
            _start = TurnEndModel.Begin();
            _startFrame = _ctx.Decor.Frame;
        }
        return _start;
    }
}
