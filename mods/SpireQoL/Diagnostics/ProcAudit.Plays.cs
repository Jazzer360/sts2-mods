using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Combat.History;
using MegaCrit.Sts2.Core.Combat.History.Entries;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using SpireQoL.Cards;
using SpireQoL.Combat;
using SpireQoL.Procs;
using SpireQoL.Sim;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.GameActions;
using Sts2Mods.Core.Game;
using Sts2Mods.Core.Ui;
using MegaCrit.Sts2.Core.Models.Cards;

namespace SpireQoL.Diagnostics;

/// <summary>ProcAudit, part: Each play recorded from the game's actions: the window of history it owns, its state before and after, the flashes during it.</summary>
public sealed partial class ProcAudit
{
    // ------------------------------------------------------------------ attribution to play actions

    /// <summary>
    /// One card play as the game ran it: a PlayCardAction, from spending its cost to its last
    /// after-play effect (exhaust, the card moving to its pile, draws its hooks set off). Everything
    /// that happened while it was the running action is attributed to it: history entries (by the
    /// history's Changed event), relic and power flashes, and shuffles (CardPileCmd.Shuffle prefix).
    /// </summary>
    private sealed class PlayRecord
    {
        public required GameAction Action;
        public Creature? Target;
        public IReadOnlyDictionary<string, string>? Before;
        public readonly List<int> Indices = new();
        public readonly List<(int At, AbstractModel Model)> Flashes = new();
        public int Shuffles;
        public SimState? After;
        public Dictionary<Creature, int>? AfterIds;
        public string? AfterSkipped;
        public bool FightEnded;
        /// <summary>Waiting for your choice (Burning Pact's pick): other players' actions can run meanwhile.</summary>
        public bool Paused;
        /// <summary>
        /// Another action ran while this play waited for your choice (ActionQueueSet.GetReadyAction skips a paused
        /// action, so a teammate's queued play runs): its effects are inside this play's before/after snapshots.
        /// </summary>
        public string? RanDuringPause;
        /// <summary>Every power on every creature when the play started and when it finished.</summary>
        public Dictionary<(Creature, string), int>? PowersBefore, PowersAfter;
        /// <summary>Energy, stars, gold, HP, cards and relics when the play started and finished.</summary>
        public PlaySnapshot? FullBefore, FullAfter;
        /// <summary>Waterfall Giants already about to blow when the play began (see <see cref="Blowing"/>).</summary>
        public HashSet<Creature>? BlowingBefore;
        /// <summary>The play ended the fight: the enemies it was simulated against that were dead as it ended (it killed them).</summary>
        public HashSet<Creature>? DeadAtFightEnd;
        /// <summary>
        /// A hand choice whose pick is auto-played (Decisions, Decisions): each card's tabs and facts on
        /// the last frame of the choice, when they described the auto-plays.
        /// </summary>
        public Dictionary<CardModel, (IReadOnlyList<TabSpec> Tabs, PlayFacts? Facts)>? Choice;
        /// <summary>
        /// The card and what was predicted for it, taken when its action starts: a card that comes
        /// back to your hand during its play (Feral) gets new tabs and facts before the check runs.
        /// </summary>
        public CardModel? Card;
        /// <summary>The played card's upgrade level as the play began (Razor Tooth's check).</summary>
        public int UpgradeBefore = -1;
        public IReadOnlyList<TabSpec>? Predicted;
        public PlayFacts? Facts;
        public long Lag = -1;
        public string? StaleBecause;
        public List<string>? ShownDiff;
        public string? ShownSkip;
    }

    /// <summary>
    /// Waterfall Giants about to blow: brought to 0, Steam Eruption keeps one at 999999999 HP, stunned until it explodes and kills
    /// itself (WaterfallGiant.TriggerAboutToBlowState, ExplodeMove). Every check that compares an enemy's HP or death treats it so,
    /// all three rules here (decided once, after it came back several times; a new check of enemy HP or death applies them too):
    ///  1. A play that brings it to 0 killed it: as good as dead (the lethal check; the after-play HP check skips its 999999999).
    ///  2. One already about to blow before the play is no play's kill: it explodes on its turn whatever the play does (the lethal
    ///     check skips it, with a note).
    ///  3. The forecast check counts its HP only up to the killing hit (ForecastAudit.EnemyIssues); its self-kill after exploding
    ///     isn't a hit, so it acted before dying.
    /// </summary>
    private static HashSet<Creature> Blowing()
    {
        var set = new HashSet<Creature>();
        if (Sts2State.Combat is { } combat)
            foreach (var creature in combat.Enemies)
                if (IsBlowing(creature)) set.Add(creature);
        return set;
    }

    private static bool IsBlowing(Creature creature) =>
        creature.Monster is MegaCrit.Sts2.Core.Models.Monsters.WaterfallGiant giant && Sts2State.Private(giant, "IsAboutToBlow", fallback: false);

    /// <summary>Power amounts on every creature in the fight, by creature and power class.</summary>
    private static Dictionary<(Creature, string), int> PowerSnapshot()
    {
        var amounts = new Dictionary<(Creature, string), int>();
        var combat = Sts2State.Combat;
        if (combat == null) return amounts;
        foreach (var creature in combat.Creatures)
            foreach (var power in creature.Powers)
                amounts[(creature, power.GetType().Name)] = amounts.TryGetValue((creature, power.GetType().Name), out var already) ? already + power.Amount : power.Amount;
        return amounts;
    }

    private static ProcAudit? _instance;
    private ActionExecutor? _executor;
    private PlayRecord? _current;
    private readonly Dictionary<GameAction, PlayRecord> _running = new(ReferenceEqualityComparer.Instance);
    private readonly Queue<PlayRecord> _finished = new();
    private int _attributed;

    public void OnFrame(OverlayHost host)
    {
        _instance = this;
        var me = Sts2State.Me;
        var combat = Sts2State.Combat;
        if (me == null || combat == null)
        {
            _finished.Clear();
            _running.Clear();
            _countAtFrame.Clear(); // trimmed by frame below, which skips the frames out of a fight
            _current = null;
            Summarize();
            DetachHistory();
            return;
        }

        // While a hand choice's pick will be auto-played, keep each hand card's tabs and facts on the play
        // that opened it (they describe the auto-plays; once the choice closes they're back to one play).
        if (CardPlays.ChoiceAutoPlays(me) > 0
            && _running.Values.FirstOrDefault(playRecord => playRecord.Card is DecisionsDecisions) is { } choosing)
        {
            var hand = new Dictionary<CardModel, (IReadOnlyList<TabSpec>, PlayFacts?)>(ReferenceEqualityComparer.Instance);
            foreach (var card in Sts2State.Hand(me))
                hand[card] = (_ctx.Decor.LastInHand.TryGetValue(card, out var tabs) ? tabs.ToList() : new List<TabSpec>(),
                           _ctx.Predictions.LastFacts.TryGetValue(card, out var facts) ? facts : null);
            choosing.Choice = hand;
        }

        var history = CombatManager.Instance.History;
        if (!ReferenceEquals(history, _history))
        {
            Summarize();
            DetachHistory();
            _history = history;
            _attributed = history.Entries.Count();
            _history.Changed += OnHistoryChanged;
        }
        var executor = RunManager.Instance?.ActionExecutor;
        if (executor != null && !ReferenceEquals(executor, _executor))
        {
            if (_executor != null) _executor.BeforeActionExecuted -= OnBeforeAction;
            _executor = executor;
            _executor.BeforeActionExecuted += OnBeforeAction;
        }

        // New relics and powers come with a change on the board (the probe: decision #130); subscribing every frame rebuilt the game's
        // player and creature lists each time (a dev review: the play check's garbage while idle).
        var probe = BoardProbe.Hash(me, combat);
        if (probe != _subscribedAt)
        {
            Subscribe(me, combat);
            _subscribedAt = probe;
        }
        _countAtFrame[_ctx.Decor.Frame] = history.Entries.Count();
        _countAtFrame.Remove(_ctx.Decor.Frame - 600); // frames count up by one: the oldest kept is 600 back

        while (_finished.Count > 0) Check(_finished.Dequeue(), me);
    }

    private void DetachHistory()
    {
        if (_history != null) _history.Changed -= OnHistoryChanged;
        _history = null;
    }

    private void OnBeforeAction(GameAction action)
    {
        using var timing = OverlayHost.TimeHook("proc-audit");
        try
        {
            // Another action starting while a play of yours waits for a choice (a teammate's play, in multiplayer).
            foreach (var playRecord in _running.Values)
                if (playRecord.Paused && !ReferenceEquals(playRecord.Action, action))
                    playRecord.RanDuringPause ??= DescribeAction(action);
            if (action is not PlayCardAction play || play.Player != Sts2State.Me)
            {
                _current = null; // potions, end of turn, other players: not a play of yours
                return;
            }
            if (!_running.TryGetValue(action, out var rec))
            {
                rec = new PlayRecord { Action = action, Target = play.Target, Before = CaptureBefore(), PowersBefore = PowerSnapshot(), FullBefore = PlaySnapshot.Take(play.Player),
                    BlowingBefore = Blowing() };
                var card = play.NetCombatCard.ToCardModelOrNull();
                if (card != null)
                {
                    rec.Card = card;
                    rec.UpgradeBefore = card.CurrentUpgradeLevel;
                    // The prediction checked (decision #28): from the board as the play begins to resolve, through
                    // the same code as the tabs, so nothing ahead of it is pending or unknown.
                    if (_ctx.Predictions.PredictNow(card) is { } now)
                    {
                        rec.Predicted = now.Tabs;
                        rec.Facts = now.Facts;
                        rec.Lag = 0;
                        (rec.ShownDiff, rec.ShownSkip) = ShownCheck(card, now.Tabs, now.Facts);
                    }
                    else
                    {
                        // Couldn't be rebuilt: the tabs it last showed in hand, unless a play finished since.
                        _fromHand++;
                        rec.ShownSkip = "no prediction as the play began";
                        rec.Predicted = _ctx.Decor.LastInHand.TryGetValue(card, out var tabs) ? tabs.ToList() : null;
                        rec.Facts = _ctx.Predictions.LastFacts.TryGetValue(card, out var facts) ? facts : null;
                        rec.Lag = _ctx.Decor.LastSeenFrame.TryGetValue(card, out var seen) ? _ctx.Decor.Frame - seen : -1;
                        if (_history != null) rec.StaleBecause = StaleBecause(_history.Entries.ToList(), card, _history.Entries.Count(), play.Player);
                    }
                }
                _running[action] = rec;
                action.AfterFinished += OnActionFinished;
                action.BeforeCancelled += OnActionCancelled;
                action.BeforePausedForPlayerChoice += OnActionPaused;
            }
            // Also called again when it resumes after a choice (Burning Pact's pick).
            rec.Paused = false;
            _current = rec;
        }
        catch (Exception ex)
        {
            ModEntry.Log.Warn($"[audit] couldn't track an action: {ex.Message}");
            Sts2Mods.Core.Diagnostics.ErrorReporter.Instance?.Exception(ex, "audit-track");
        }
    }

    private static string DescribeAction(GameAction action)
    {
        try
        {
            if (action is PlayCardAction playCardAction && playCardAction.NetCombatCard.ToCardModelOrNull() is { } card)
                return $"{SafeTitle(card)} ({(playCardAction.Player == Sts2State.Me ? "yours" : "a teammate's")})";
            return action.GetType().Name;
        }
        catch
        {
            return "another action";
        }
    }

    private void OnActionPaused(GameAction action)
    {
        using var timing = OverlayHost.TimeHook("proc-audit");
        if (_running.TryGetValue(action, out var paused)) paused.Paused = true;
        OnHistoryChanged();
        if (_current != null && ReferenceEquals(_current.Action, action)) _current = null;
    }

    private void OnActionCancelled(GameAction action)
    {
        using var timing = OverlayHost.TimeHook("proc-audit");
        Unhook(action);
        _running.Remove(action);
        if (_current != null && ReferenceEquals(_current.Action, action)) _current = null;
    }

    private void Unhook(GameAction action)
    {
        action.AfterFinished -= OnActionFinished;
        action.BeforeCancelled -= OnActionCancelled;
        action.BeforePausedForPlayerChoice -= OnActionPaused;
    }

    // Why the state after a play wasn't captured: the play ended the fight, or your turn (Void Form: PlayerCmd.EndTurn lets the end
    // of the turn run, Chains of Binding clearing Bound, before its state is read). Expected skips (CheckSkip.Expected: decision #108).
    private const string FightEndedSkip = CheckSkip.FightEnded;
    private const string TurnEndedSkip = CheckSkip.TurnEnded;

    private void OnActionFinished(GameAction action)
    {
        using var timing = OverlayHost.TimeHook("proc-audit");
        try
        {
            OnHistoryChanged();
            Unhook(action);
            if (!_running.Remove(action, out var rec)) return;
            if (_current != null && ReferenceEquals(_current.Action, action)) _current = null;
            // The play has fully resolved and nothing else has run: the state it left.
            var me = Sts2State.Me;
            var combat = Sts2State.Combat;
            if (CombatManager.Instance.IsOverOrEnding)
            {
                // The fight is ending: its cleanup (temporary powers expiring, Focus back) changes the
                // state, so the state the play left can't be compared. The history checks still run.
                rec.AfterSkipped = FightEndedSkip;
                rec.FightEnded = true;
                // Who it killed is still known (the lethal check reads it): the creatures are there, dead or not.
                rec.DeadAtFightEnd = rec.Facts?.Sim?.Ids.Keys.Where(creature => creature.IsDead).ToHashSet();
            }
            else if (me != null && combat != null && combat.CurrentSide != MegaCrit.Sts2.Core.Combat.CombatSide.Player)
            {
                // The play ended your turn (decision #126): what the end of the turn did is in the state, not the play's.
                rec.AfterSkipped = TurnEndedSkip;
            }
            else if (me != null && combat != null)
            {
                rec.PowersAfter = PowerSnapshot();
                rec.FullAfter = PlaySnapshot.Take(me);
                try
                {
                    rec.After = SimAdapter.Build(me, combat, out var ids);
                    rec.AfterIds = ids;
                }
                catch (Exception ex)
                {
                    // The reason stays the same whatever the exception, so it adds up; the message goes to the log.
                    ModEntry.Log.Warn($"[audit] couldn't read the state after a play: {ex.Message}");
                    rec.AfterSkipped = "couldn't read the state";
                }
            }
            _finished.Enqueue(rec);
        }
        catch (Exception ex)
        {
            ModEntry.Log.Warn($"[audit] couldn't finish an action: {ex.Message}");
            Sts2Mods.Core.Diagnostics.ErrorReporter.Instance?.Exception(ex, "audit-finish");
        }
    }

    /// <summary>New history entries belong to the play action running when they were added.</summary>
    private void OnHistoryChanged()
    {
        using var timing = OverlayHost.TimeHook("proc-audit");
        // Called from the game's CombatHistory.Changed event: an exception here would surface in the game's code.
        try { HistoryChanged(); }
        catch (Exception ex) { Sts2Mods.Core.Diagnostics.ErrorReporter.Instance?.Exception(ex, "audit-history"); }
    }

    private void HistoryChanged()
    {
        if (_history == null) return;
        var count = _history.Entries.Count();
        if (count < _attributed) _attributed = count; // cleared
        if (_current != null)
            for (var i = _attributed; i < count; i++) _current.Indices.Add(i);
        _attributed = count;
    }

    private void OnRelicFlashed(RelicModel relic, IEnumerable<Creature> _) => Flash(relic);

    private void OnPowerFlashed(PowerModel power) => Flash(power);

    private void Flash(AbstractModel model)
    {
        using var timing = OverlayHost.TimeHook("proc-audit");
        // Called from the game's Flashed events.
        try
        {
            if (_history == null || _current == null) return;
            HistoryChanged();
            _current.Flashes.Add((_history.Entries.Count(), model));
        }
        catch (Exception ex)
        {
            Sts2Mods.Core.Diagnostics.ErrorReporter.Instance?.Exception(ex, "audit-flash");
        }
    }

    /// <summary>
    /// Harmony prefix on CardPileCmd.Shuffle (observes only; the game keeps no history entry for a
    /// shuffle): counts it for the play running now.
    /// </summary>
    private static void ShufflePrefix(Player player)
    {
        using var timing = OverlayHost.TimeHook("proc-audit");
        try
        {
            var procAudit = _instance;
            if (procAudit?._current == null || player != Sts2State.Me || CombatManager.Instance.IsOverOrEnding) return;
            procAudit._current.Shuffles++;
        }
        catch
        {
            // diagnostics only
        }
    }

    /// <summary>
    /// The proc tabs the card last showed in hand against the prediction as its play began (decision #103), which the outcome is
    /// checked against: with nothing between them (no history entry since the frame they were drawn, the card's own play start
    /// aside) and both simulations finished, they come from the same board through the same code and must be the same. A
    /// difference is a tab drawn stale (a cache keyed on too little, a frame that didn't refresh), which checking only the
    /// outcome can't see. Returns the differences, or why the check was skipped.
    /// </summary>
    private (List<string>? Diff, string? Skip) ShownCheck(CardModel card, List<TabSpec> now, PlayFacts nowFacts)
    {
        if (!_ctx.Predictions.LastTabs.TryGetValue(card, out var last) || !_ctx.Predictions.LastFacts.TryGetValue(card, out var shownFacts))
            return (null, "none recorded in hand");
        // Drawn on the last frame the card was in hand: not held from an earlier board while its simulation ran (decision #105), nor
        // from a frame the feature skipped.
        if (!_ctx.Decor.LastSeenFrame.TryGetValue(card, out var seenAt) || last.Frame != seenAt) return (null, "held from an earlier board, or not drawn on its last frame in hand");
        var shown = last.Tabs;
        if (shownFacts.SimPending || shownFacts.SimIncomplete || nowFacts.SimPending || nowFacts.SimIncomplete)
            return (null, "a simulation not finished in time, or stopped at a cap");
        if (_history == null || !_ctx.Decor.LastSeenFrame.TryGetValue(card, out var frame) || !_countAtFrame.TryGetValue(frame, out var drawnAt))
            return (null, "no history position for them");
        var hist = _history.Entries.ToList();
        for (var entryIndex = Math.Max(0, drawnAt); entryIndex < hist.Count; entryIndex++)
        {
            if (hist[entryIndex] is CardPlayStartedEntry own && ReferenceEquals(own.CardPlay.Card, card)) continue;
            return (null, "the board changed since they were drawn");
        }
        var before = shown.GroupBy(tab => tab.Key).ToDictionary(sameKey => sameKey.Key, sameKey => Look(sameKey.First()));
        var atPlay = now.GroupBy(tab => tab.Key).ToDictionary(sameKey => sameKey.Key, sameKey => Look(sameKey.First()));
        var diff = new List<string>();
        foreach (var (key, was) in before)
        {
            if (!atPlay.TryGetValue(key, out var then)) diff.Add($"tabs shown: {key} shown ({was}), not predicted as the play began");
            else if (then != was) diff.Add($"tabs shown: {key} shown as {was}, predicted {then} as the play began");
        }
        foreach (var (key, then) in atPlay)
            if (!before.ContainsKey(key)) diff.Add($"tabs shown: {key} not shown, predicted ({then}) as the play began");
        return (diff.Count > 0 ? diff : null, null);

        // What the player sees of a tab: crossed or plain, the aim marker, its number or text.
        static string Look(TabSpec tab)
        {
            var parts = new List<string> { tab.Crossed ? "crossed" : "plain" };
            if (tab.Aimed) parts.Add("aimed");
            if (tab.Number is { } number) parts.Add(number.ToString());
            if (tab.NumberText != null) parts.Add(tab.NumberText);
            if (tab.Text != null) parts.Add(tab.Text);
            return string.Join(" ", parts);
        }
    }

    // History size at each frame, to tell when a card's tabs were computed relative to other plays.
    private readonly Dictionary<long, int> _countAtFrame = new();

    /// <summary>
    /// The card's tabs were last computed before another of your plays finished (you played quickly,
    /// so this card's prediction didn't see that play's effects): returns that play's card, else null.
    /// </summary>
    private string? StaleBecause(List<CombatHistoryEntry> hist, CardModel card, int start, Player me)
    {
        if (!_ctx.Decor.LastSeenFrame.TryGetValue(card, out var frame) || !_countAtFrame.TryGetValue(frame, out var then)) return null;
        for (var entryIndex = Math.Max(0, then); entryIndex < start && entryIndex < hist.Count; entryIndex++)
        {
            // Any player's play (a teammate's too) or a potion that resolved in between.
            if (hist[entryIndex] is CardPlayFinishedEntry { CardPlay: var play } && !ReferenceEquals(play.Card, card))
                return SafeTitle(play.Card);
            if (hist[entryIndex] is PotionUsedEntry potionUsedEntry)
                return SafePotion(potionUsedEntry);
        }
        return null;
    }

    /// <summary>
    /// The window for reports: the history around the play, each entry with its position, in our
    /// own words (the game's draw text says "discarded"), marked when it isn't this play's, and the
    /// relic and power flashes at their positions.
    /// </summary>
    private static List<string> WindowLines(Window window, List<CombatHistoryEntry> hist, Player me)
    {
        var lines = new List<string>();
        var mine = window.Indices.ToHashSet();
        var from = Math.Max(0, window.From - 3);
        var to = Math.Min(hist.Count, window.To + 3);
        for (var i = from; i <= to; i++)
        {
            foreach (var flash in window.Flashes.Where(otherFlash => otherFlash.At == i)) lines.Add($"   ⚡ {flash.Model.GetType().Name} flashed");
            if (i >= to) break;
            lines.Add($"{(mine.Contains(i) ? "" : "(not this play) ")}#{i} {Describe(hist[i])}");
        }
        return lines;
    }

    internal static string Describe(CombatHistoryEntry entry)
    {
        static string N(object? o) => o switch { null => "?", Creature creature => creature.Monster?.GetType().Name ?? creature.Player?.Character?.GetType().Name ?? "creature", _ => o.GetType().Name };
        try
        {
            return entry switch
            {
                CardPlayStartedEntry cardPlayStartedEntry => $"play started: {N(cardPlayStartedEntry.CardPlay.Card)} (play {cardPlayStartedEntry.CardPlay.PlayIndex + 1}/{cardPlayStartedEntry.CardPlay.PlayCount}{(cardPlayStartedEntry.CardPlay.IsAutoPlay ? ", auto-play" : "")}, energy {cardPlayStartedEntry.CardPlay.Resources.EnergyValue})",
                CardPlayFinishedEntry cardPlayFinishedEntry => $"play finished: {N(cardPlayFinishedEntry.CardPlay.Card)}",
                CardDrawnEntry cardDrawnEntry => $"drew {N(cardDrawnEntry.Card)}{(cardDrawnEntry.FromHandDraw ? " (turn draw)" : "")}",
                CardDiscardedEntry cardDiscardedEntry => $"discarded {N(cardDiscardedEntry.Card)}",
                CardExhaustedEntry cardExhaustedEntry => $"exhausted {N(cardExhaustedEntry.Card)}",
                CardGeneratedEntry cardGeneratedEntry => $"created {N(cardGeneratedEntry.Card)}",
                CardAfflictedEntry cardAfflictedEntry => $"afflicted {N(cardAfflictedEntry.Card)} with {N(cardAfflictedEntry.Affliction)}",
                EnergySpentEntry energySpentEntry => $"spent {energySpentEntry.Amount} energy",
                StarsModifiedEntry starsModifiedEntry => $"stars {(starsModifiedEntry.Amount >= 0 ? "+" : "")}{starsModifiedEntry.Amount}",
                BlockGainedEntry blockGainedEntry => $"{N(blockGainedEntry.Actor)} gained {blockGainedEntry.Amount} block{(blockGainedEntry.CardPlay != null ? $" (from {N(blockGainedEntry.CardPlay.Card)})" : "")}",
                DamageReceivedEntry damage => $"{N(damage.Dealer)} → {N(damage.Result.Receiver)}: {damage.Result.TotalDamage} ({damage.Result.BlockedDamage} blocked, {damage.Result.UnblockedDamage} through){(damage.Result.WasTargetKilled ? ", killed" : "")}{(damage.CardSource != null ? $" [card {N(damage.CardSource)}]" : " [no card]")}",
                PowerReceivedEntry powerReceivedEntry => $"{N(powerReceivedEntry.Applier)} → {N(powerReceivedEntry.Actor)}: {N(powerReceivedEntry.Power)} {(powerReceivedEntry.Amount >= 0 ? "+" : "")}{powerReceivedEntry.Amount}",
                OrbChanneledEntry orbChanneledEntry => $"channeled {N(orbChanneledEntry.Orb)}",
                SummonedEntry summonedEntry => $"summoned {summonedEntry.Amount}",
                PotionUsedEntry potionUsedEntry => $"potion {N(potionUsedEntry.Potion)}",
                MonsterPerformedMoveEntry monsterPerformedMoveEntry => $"{N(monsterPerformedMoveEntry.Monster)} move {monsterPerformedMoveEntry.Move.Id}",
                _ => entry.Description,
            };
        }
        catch
        {
            return entry.GetType().Name;
        }
    }

    private IReadOnlyDictionary<string, string>? CaptureBefore()
    {
        try
        {
            return Snapshot.Capture(_ctx);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>The board probe the relics and powers were last subscribed at.</summary>
    private int _subscribedAt;

    private void Subscribe(Player me, ICombatState combat)
    {
        SubscribeAll(combat.Allies);
        SubscribeAll(combat.Enemies);
    }

    /// <summary>
    /// Each creature's powers, and each player's relics: a teammate's relic your card sets off has a tab too (decision #75). By index: the
    /// game's Players and Creatures make lists on every read.
    /// </summary>
    private void SubscribeAll(IReadOnlyList<Creature> creatures)
    {
        for (var i = 0; i < creatures.Count; i++)
        {
            if (creatures[i].Player is { } player)
            {
                var relics = player.Relics;
                for (var j = 0; j < relics.Count; j++)
                    if (_subscribed.Add(relics[j])) relics[j].Flashed += OnRelicFlashed;
            }
            var powers = creatures[i].Powers;
            for (var j = 0; j < powers.Count; j++)
                if (_subscribed.Add(powers[j])) powers[j].Flashed += OnPowerFlashed;
        }
    }

    /// <summary>Checks one finished play of yours (see PlayRecord) and logs and reports what didn't match.</summary>
    private void Check(PlayRecord rec, Player me)
    {
        if (_history == null) return;
        var hist = _history.Entries.ToList();
        var indices = rec.Indices.Where(index => index < hist.Count).ToList();
        var start = indices.FirstOrDefault(index => hist[index] is CardPlayStartedEntry { CardPlay: var play } && play.Card.Owner == me && play.IsFirstInSeries && !play.IsAutoPlay, -1);
        if (start < 0) return; // nothing was played (the card couldn't be played after all)
        var card = ((CardPlayStartedEntry)hist[start]).CardPlay.Card;
        // What was predicted when the play's action started (fallback: now, for a card the action
        // couldn't name up front).
        var captured = ReferenceEquals(rec.Card, card);
        var predicted = captured && rec.Predicted != null ? rec.Predicted
            : _ctx.Decor.LastInHand.TryGetValue(card, out var tabs) ? tabs : Array.Empty<TabSpec>();
        var facts = captured ? rec.Facts : _ctx.Predictions.LastFacts.TryGetValue(card, out var otherFacts) ? otherFacts : null;
        // A hand choice's pick, auto-played: its tabs and facts from the choice join the card's own.
        var autoPlays = indices.Select(index => hist[index]).OfType<CardPlayStartedEntry>().Where(cardPlayStartedEntry => cardPlayStartedEntry.CardPlay.IsAutoPlay && cardPlayStartedEntry.CardPlay.Card.Owner == me).ToList();
        CardModel? chosen = null;
        var target = rec.Target;
        if (rec.Choice != null && autoPlays.Count > 0 && autoPlays.All(cardPlayStartedEntry => ReferenceEquals(cardPlayStartedEntry.CardPlay.Card, autoPlays[0].CardPlay.Card))
            && rec.Choice.TryGetValue(autoPlays[0].CardPlay.Card, out var pick))
        {
            chosen = autoPlays[0].CardPlay.Card;
            predicted = predicted.Concat(pick.Tabs).ToList();
            facts = facts != null && pick.Facts != null ? facts.Then(pick.Facts) : pick.Facts ?? facts;
            // Its simulated plays share one target; a single-target pick gets a random one each time.
            var targets = autoPlays.Select(cardPlayStartedEntry => cardPlayStartedEntry.CardPlay.Target).Distinct().ToList();
            target = targets.Count == 1 ? targets[0] : null;
        }
        var window = new Window
        {
            Chosen = chosen,
            Card = card, CardName = SafeTitle(card), Predicted = predicted, Facts = facts,
            Start = start, From = indices.Min(), To = indices.Max() + 1, Indices = indices,
            Flashes = rec.Flashes, Shuffles = rec.Shuffles,
            Lag = captured ? rec.Lag : _ctx.Decor.LastSeenFrame.TryGetValue(card, out var seen) ? _ctx.Decor.Frame - seen : -1,
            Before = rec.Before, Target = target, UpgradeBefore = captured ? rec.UpgradeBefore : -1,
            After = rec.After, AfterIds = rec.AfterIds, AfterSkipped = rec.AfterSkipped, FightEnded = rec.FightEnded, RanDuringPause = rec.RanDuringPause,
            PowersBefore = rec.PowersBefore, PowersAfter = rec.PowersAfter,
            FullBefore = rec.FullBefore, FullAfter = rec.FullAfter, BlowingBefore = rec.BlowingBefore, DeadAtFightEnd = rec.DeadAtFightEnd,
            StaleBecause = captured ? rec.StaleBecause : StaleBecause(hist, card, start, me),
            ShownDiff = captured ? rec.ShownDiff : null, ShownSkip = captured ? rec.ShownSkip : "the play's start wasn't seen",
        };
        Report(window, me, hist);
    }
}
