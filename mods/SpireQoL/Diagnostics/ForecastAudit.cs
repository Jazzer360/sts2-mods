using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Combat.History;
using MegaCrit.Sts2.Core.Combat.History.Entries;
using MegaCrit.Sts2.Core.Entities.Players;
using SpireQoL.Combat;
using SpireQoL.Sim;
using Sts2Mods.Core.Features;
using Sts2Mods.Core.Game;
using Sts2Mods.Core.Ui;

namespace SpireQoL.Diagnostics;

/// <summary>
/// Checks Incoming Damage's forecast against what actually happened, and logs a mismatch to the
/// game log (godot.log, lines start with [SpireQoL] [audit]) so forecast bugs surface.
///
/// The forecast from the last frame of your turn (before ending the turn starts, so it covers all
/// end-of-turn effects) is frozen. When your next turn starts, the combat history recorded in
/// between is split by whose turn each entry happened on (the entry's own CurrentSide):
///  - your end of turn (player side): block gained there vs the forecast's end-of-turn block;
///  - your end of turn + the enemy turn: HP you and Osty lost vs the forecast. Entries from your
///    next turn (Poison ticking on you at its start, etc.) are left out.
/// It also checks the outcome simulator enemy by enemy (Sim/SimEngine; the forecast's own run):
///  - dead before it acted: predicted in every branch but it acted, or in no branch but it died first;
///  - its HP when your end of turn was over, against the branches' range;
///  - damage instances that got through its block during your end of turn (what Slumber and Asleep
///    count), against the branches' range;
///  - Imbalanced: whether it set off its stun (its code's test: any hit it dealt that a block took whole, any player's), when every branch
///    agreed (its Flash() in the simulation, decision #273).
/// Only what no branch allows is reported, so a random target never causes a report.
/// A one-line summary per combat gives the match rate. Read-only: it only reads the history.
/// </summary>
public sealed class ForecastAudit : IFeature, IFrameFeature
{
    public string Id => "forecast-audit";
    public string DisplayName => "Forecast Audit (log only)";
    public bool EnabledByDefault => true;

    public void Apply(Harmony harmony)
    {
    }

    public void Attach(OverlayHost host)
    {
    }

    private TurnEndModel? _last;      // forecast on the latest frame of your turn
    private int _lastHistoryCount;    // history size at that frame
    private int _lastTurn;
    private bool _wasPlayerTurn;

    /// <summary>
    /// Your creature, watched for its Died event (Creature.InvokeDiedEvent: raised only when nothing prevents the death). HP 0 alone
    /// isn't a death: Fairy in a Bottle and Lizard Tail heal you from 0 a few frames later (Hook.ShouldDie, AfterPreventingDeath).
    /// </summary>
    private MegaCrit.Sts2.Core.Entities.Creatures.Creature? _watched;
    private bool _youDied;

    private void Watch(MegaCrit.Sts2.Core.Entities.Creatures.Creature? creature)
    {
        if (ReferenceEquals(creature, _watched)) return;
        if (_watched != null)
        {
            _watched.Died -= OnYouDied;
            _watched.Revived -= OnYouRevived;
        }
        _watched = creature;
        _youDied = false;
        if (creature == null) return;
        creature.Died += OnYouDied;
        creature.Revived += OnYouRevived;
    }

    private void OnYouDied(MegaCrit.Sts2.Core.Entities.Creatures.Creature creature) => _youDied = true;
    private void OnYouRevived(MegaCrit.Sts2.Core.Entities.Creatures.Creature creature) => _youDied = false;

    private TurnEndModel? _frozen;
    /// <summary>
    /// The forecast with teammates' hands had no result yet when your turn ended (decision #104: the audit waits a few milliseconds
    /// at most): the shown forecast it belongs to and its exploration, pinned; the frozen forecast is built from it when the turn
    /// is checked.
    /// </summary>
    private (TurnEndModel From, SimJob Job)? _frozenPending;

    /// <summary>The frozen forecast to check: frozen as your turn ended, or built now from its exploration (null: it never had a result).</summary>
    private TurnEndModel? Frozen() => _frozen ?? (_frozenPending is { } fromJob ? fromJob.From.FullFrom(fromJob.Job) : null);

    private bool HasFrozen => _frozen != null || _frozenPending != null;

    private void Unfreeze(bool cancel)
    {
        if (cancel)
        {
            _frozen?.Job?.Cancel();
            _frozenPending?.Job.Cancel();
        }
        _frozen = null;
        _frozenPending = null;
    }

    /// <summary>Checks the frozen turn, or counts it as not checked when its forecast never had a result.</summary>
    private void CheckFrozen(CombatHistory history, Player me, bool youDied = false)
    {
        if (Frozen() is { } forecast) Compare(forecast, history, _frozenAt, me, youDied);
        else
        {
            Skip("the forecast had no result by the enemy turn");
            _frozenPending?.Job.Cancel();
        }
        Unfreeze(cancel: false);
    }
    private int _frozenAt;
    private int _frozenTurn;

    private CombatHistory? _history;
    private int _checked;
    private int _matched;
    /// <summary>
    /// Decision #92, rule 1: turns not checked, by reason (a gap, not a pass), and what the forecasts didn't model, by reason
    /// (rule 2): both in the combat summary, so the logs show which gaps come up and how often.
    /// </summary>
    private static readonly Dictionary<string, int> Skips = new(), Gaps = new();

    private static readonly OddsTally FightOdds = new();
    private static readonly OddsTally _sessionOdds = new();
    private static bool _driftReported;

    private void Odds(string what, SimChance chance, bool happened)
    {
        if (chance.High <= 0 || chance.Low >= 1) return; // certain either way: the checks above hold those
        ModEntry.Log.Detail($"[audit] [odds] turn {_frozenTurn}: {what}: predicted {chance.Text()}, {(happened ? "happened" : "didn't")}");
        FightOdds.Add(chance, happened);
        _sessionOdds.Add(chance, happened);
        if (!_sessionOdds.Drifted || _driftReported) return;
        _driftReported = true;
        Sts2Mods.Core.Diagnostics.ErrorReporter.Instance?.Mismatch("odds", $"Forecast odds: what happened is far from the chances shown ({_sessionOdds.Text()})",
            new[] { "odds-drift" }, $"Over this session, the chances shown (your death, enemies' before they act, the most likely outcome) don't match what happened: {_sessionOdds.Text()}. " +
            "Each chance is logged as [audit] [odds] in the game log.", new Dictionary<string, string>(), new Dictionary<string, string> { ["check"] = "odds" });
    }

    private Dictionary<MegaCrit.Sts2.Core.Entities.Creatures.Creature, Features.IntentDetailsFeature.Shown> _intentsShown = new(ReferenceEqualityComparer.Instance);

    /// <summary>The creatures on the board when the intents were shown (as your turn ended): one the enemy turn spawns isn't among them.</summary>
    private HashSet<MegaCrit.Sts2.Core.Entities.Creatures.Creature> _onBoard = new(ReferenceEqualityComparer.Instance);

    /// <summary>
    /// Intent Details against the enemy turn (decision #98): each enemy that made the move its intent showed, the block it gained
    /// during that move and the powers it applied (the history entries between the enemy's previous move and its own move entry).
    /// A power Artifact took leaves no entry: not reported.
    /// </summary>
    private List<string> IntentIssues(List<CombatHistoryEntry> window)
    {
        var issues = new List<string>();
        var start = window.FindIndex(entry => SideOf(entry) == CombatSide.Enemy);
        if (start < 0) return issues;
        for (var i = start; i < window.Count; i++)
        {
            if (window[i] is not MonsterPerformedMoveEntry monsterPerformedMoveEntry || monsterPerformedMoveEntry.Monster.Creature is not { } creature || !_intentsShown.TryGetValue(creature, out var shown)) continue;
            var span = window.Skip(start).Take(i - start + 1).ToList();
            start = i + 1;
            if (monsterPerformedMoveEntry.Move.Id != shown.MoveId) continue; // another move than the intent showed (stunned, changed): nothing to compare
            var name = monsterPerformedMoveEntry.Monster.GetType().Name;
            if (shown.Block is { } block)
            {
                var gained = span.OfType<BlockGainedEntry>().Where(blockGainedEntry => blockGainedEntry.Receiver == creature).Sum(blockGainedEntry => blockGainedEntry.Amount);
                if (gained != block) issues.Add($"intent block: {name}'s {shown.MoveId} showed {block}, it gained {gained}");
            }
            var applied = span.OfType<PowerReceivedEntry>().Where(powerReceivedEntry => powerReceivedEntry.Applier == creature).ToList();
            // A move can apply a power with no applier, to its own monster (Devoted Sculptor's Ritual: PowerCmd.Apply(..., null)) or to
            // you (Slimed Berserker's Leeching Hug: Weak, applier null): counted for the powers the intent showed, when it's on its
            // monster or gives some (powers ticking down lose some). Not for "applied, not shown": ticking powers have no applier either.
            var ownless = span.OfType<PowerReceivedEntry>()
                .Where(powerReceivedEntry => powerReceivedEntry.Applier == null && (powerReceivedEntry.Actor == creature || powerReceivedEntry.Amount > 0)).ToList();
            var artifact = span.OfType<PowerReceivedEntry>().Any(powerReceivedEntry => powerReceivedEntry.Power is MegaCrit.Sts2.Core.Models.Powers.ArtifactPower && powerReceivedEntry.Amount < 0);
            foreach (var power in shown.Powers)
            {
                if (power.Maybe) continue; // only on some branches, shown as ? (decision #109): nothing promised either way
                var same = applied.Concat(ownless).Where(powerReceivedEntry => powerReceivedEntry.Power.GetType() == power.Power).ToList();
                if (same.Count == 0)
                {
                    if (!artifact) issues.Add($"intent power: {name}'s {shown.MoveId} showed {power.Power.Name}, it applied none");
                }
                else if (power.Amount is { } amount && same.All(powerReceivedEntry => powerReceivedEntry.Amount != amount))
                    issues.Add($"intent power: {name}'s {shown.MoveId} showed {power.Power.Name} {amount}, it applied {string.Join(", ", same.Select(powerReceivedEntry => powerReceivedEntry.Amount))}");
            }
            // A power on a creature the move spawned is part of the summon (its Summon intent), not a buff or debuff: the Fabricator's
            // bots get Minion as it makes them (a dev review report).
            foreach (var type in applied.Where(powerReceivedEntry => powerReceivedEntry.Actor is not { } receiver || _onBoard.Contains(receiver))
                         .Select(powerReceivedEntry => powerReceivedEntry.Power.GetType()).Distinct().Where(type => shown.Powers.All(power => power.Power != type)))
                issues.Add($"intent power: {name}'s {shown.MoveId} applied {type.Name}, not shown");
        }
        return issues;
    }

    private static void CountIn(Dictionary<string, int> counts, string reason) => counts[reason] = (counts.TryGetValue(reason, out var count) ? count : 0) + 1;

    /// <summary>A turn (or part of one) not checked: counted for the combat summary, and in the session's counts (decision #108).</summary>
    private void Skip(string reason, int? turn = null)
    {
        CountIn(Skips, reason);
        Telemetry.DevEvents.Post(new Telemetry.CheckSkipped(Telemetry.Audits.Forecast, turn ?? _frozenTurn, reason, Detail: null));
    }

    public void OnFrame(OverlayHost host)
    {
        var me = Sts2State.Me;
        // You died before your next turn: the fight ends without one, so check the turn now, with what happened up to your death. A
        // death is the game's Died event, not HP 0: a death something prevents passes through 0 before its heal.
        Watch(me?.Creature);
        if (HasFrozen && _history != null && me != null && _youDied) CheckFrozen(_history, me, youDied: true);
        if (me == null || !Sts2State.InCombat)
        {
            Summarize();
            Unfreeze(cancel: true);
            _wasPlayerTurn = false;
            _history = null;
            return;
        }

        var history = CombatManager.Instance.History;
        if (!ReferenceEquals(history, _history))
        {
            Summarize();
            _history = history;
            Unfreeze(cancel: true);
        }

        var playerTurn = Sts2State.IsPlayerTurn;
        if (playerTurn)
        {
            // A frozen forecast from the previous turn: the enemy turn is over, compare.
            if (HasFrozen && Sts2State.TurnNumber > _frozenTurn) CheckFrozen(history, me);
            _last = TurnEndModel.Compute();
            _lastHistoryCount = history.Entries.Count();
            _lastTurn = Sts2State.TurnNumber;
        }
        else if (_wasPlayerTurn && _last == null)
        {
            // The board changed on your turn's last frame and its forecast had no result yet (decision #104): nothing to hold.
            Skip("the forecast was still on its way as the turn ended", _lastTurn);
        }
        else if (_wasPlayerTurn && _last != null)
        {
            // Ending the turn just started: freeze the last forecast.
            _frozen = _last.Full; // teammates' hands included (decision #25: the check uses everything that isn't a roll)
            if (_frozen == null && _last.PendingFull is { } pendingJob)
            {
                // Its first result is still on its way (decision #104): kept exploring, and built when the turn is checked.
                _frozenPending = (_last, pendingJob);
                pendingJob.Pinned = true;
            }
            // What each enemy's intent showed (Intent Details): its block and the powers its move applies (decision #98).
            _onBoard = new HashSet<MegaCrit.Sts2.Core.Entities.Creatures.Creature>(_last.Ids.Keys, ReferenceEqualityComparer.Instance);
            _intentsShown = _last.Ids.Keys.Select(creature => (creature, Features.IntentDetailsFeature.For(creature))).Where(creature => creature.Item2 != null)
                .ToDictionary(otherCreature => otherCreature.creature, creature => creature.Item2!, (IEqualityComparer<MegaCrit.Sts2.Core.Entities.Creatures.Creature>)ReferenceEqualityComparer.Instance);
            // Its exploration goes on after the board changes (decision #87): the check compares against all of it.
            if (_frozen?.Job is { } frozenJob) frozenJob.Pinned = true;
            _frozenAt = _lastHistoryCount;
            _frozenTurn = _lastTurn;
        }
        _wasPlayerTurn = playerTurn;
    }

    private void Summarize()
    {
        static string List(Dictionary<string, int> counts) => string.Join(", ", counts.OrderByDescending(entry => entry.Value).Select(entry => $"{entry.Key} ×{entry.Value}"));
        if (_checked > 0) ModEntry.Log.Detail($"[audit] combat summary: {_checked} enemy turns forecast, {_matched} matched.");
        if (Skips.Count > 0) ModEntry.Log.Detail($"[audit] combat summary: enemy turns not checked: {List(Skips)}.");
        if (Gaps.Count > 0) ModEntry.Log.Detail($"[audit] combat summary: not modelled in the forecast: {List(Gaps)}.");
        if (FightOdds.Count > 0) ModEntry.Log.Detail($"[audit] combat summary: chances: {FightOdds.Text()} (this session: {_sessionOdds.Text()}).");
        FightOdds.Count = FightOdds.Happened = 0;
        FightOdds.Low = FightOdds.High = FightOdds.Variance = 0;
        _checked = _matched = 0;
        Skips.Clear();
        Gaps.Clear();
    }

    /// <summary>The side whose turn an entry happened on (CombatHistoryEntry.CurrentSide is private).</summary>
    private static CombatSide SideOf(CombatHistoryEntry entry) => Sts2State.Private(entry, "CurrentSide", CombatSide.Player);

    /// <summary>
    /// Each simulated teammate's HP lost this round against the forecast's range (worst branch to best),
    /// on every game: a teammate is simulated through its own code path (not the one their game checks for
    /// themselves), and Teammate Incoming Damage shows it. Skipped when a teammate auto-played a card at end
    /// of turn (Stampede: a random card). What's shown leaves their hand out (decision #25); when that range
    /// differs, the line says so, and an HP loss only the cards in their hand explain is logged, not reported.
    /// </summary>
    private string TeammateIssues(TurnEndModel forecast, List<DamageReceivedEntry> damage, List<CombatHistoryEntry> endOfTurn, List<string> issues)
    {
        var outcome = forecast.Outcome;
        var line = "";
        for (var teammateIndex = 0; teammateIndex < forecast.Teammates.Count && outcome.Branches.Count > 0 && teammateIndex < outcome.Branches[0].Teammates.Count; teammateIndex++)
        {
            var mate = forecast.Teammates[teammateIndex];
            var name = SafeCharacter(mate);
            // Overkill counts, as yours does (decision #93: the forecast isn't capped by their HP).
            var actual = damage.Where(otherDamage => otherDamage.Result.Receiver == mate.Creature).Sum(otherDamage => otherDamage.Result.UnblockedDamage + otherDamage.Result.OverkillDamage);
            var (min, max) = (outcome.TeammateTakenMin(teammateIndex), outcome.TeammateTakenMax(teammateIndex));
            line += $"; teammate {name} HP lost {actual} (predicted {(min == max ? $"{max}" : $"{min}-{max}")})";
            var shown = teammateIndex < forecast.TeammateView.Count ? forecast.TeammateView[teammateIndex] : null;
            if (shown != null && (shown.TakenMin != min || shown.TakenMax != max))
                line += $" (shown without their hand: {(shown.TakenMin == shown.TakenMax ? $"{shown.TakenMax}" : $"{shown.TakenMin}-{shown.TakenMax}")})";
            if (endOfTurn.OfType<CardPlayStartedEntry>().Any(cardPlayStartedEntry => cardPlayStartedEntry.CardPlay.IsAutoPlay && cardPlayStartedEntry.CardPlay.Card.Owner == mate))
            {
                line += " (auto-played a card: not checked)";
                Skip("a teammate auto-played a card at end of turn");
                continue;
            }
            if (actual > max || actual < min) issues.Add($"teammate {name} HP lost: forecast {(min == max ? $"{max}" : $"{min}-{max}")}, actual {actual}");
            // Their Osty (Teammate Incoming Damage shows what he absorbs).
            if (mate.Osty is { } osty)
            {
                var ostyActual = damage.Where(otherDamage => otherDamage.Result.Receiver == osty).Sum(otherDamage => otherDamage.Result.UnblockedDamage);
                var (oMin, oMax) = (outcome.TeammateOstyTakenMin(teammateIndex), outcome.TeammateOstyTakenMax(teammateIndex));
                line += $", their Osty {ostyActual} (predicted {(oMin == oMax ? $"{oMax}" : $"{oMin}-{oMax}")})";
                if (ostyActual > oMax || ostyActual < oMin) issues.Add($"teammate {name} Osty HP lost: forecast {(oMin == oMax ? $"{oMax}" : $"{oMin}-{oMax}")}, actual {ostyActual}");
            }
        }
        return line;
    }

    private static string SafeCharacter(Player player)
    {
        try { return player.Character.GetType().Name; } catch { return "?"; }
    }

    /// <param name="youDied">You died before your next turn (the fight ended): the enemy turn stopped there, so your HP lost is
    /// checked as a death against the forecast's, and the enemies left to act didn't.</param>
    private void Compare(TurnEndModel forecast, CombatHistory history, int from, Player me, bool youDied = false)
    {
        // The frozen forecast's search result; it isn't needed after this.
        forecast = forecast.Refreshed();
        var shownChances = forecast.Outcome; // the chances as they stood when you ended your turn, before the history narrows them
        // What it didn't model (rule 2): logged and counted, whatever the check finds.
        foreach (var reason in forecast.Outcome.UnmodelledReasons) CountIn(Gaps, reason);
        if (forecast.Outcome.AnyUnmodelled) ModEntry.Log.Detail($"[audit] [gaps] turn {_frozenTurn}: the forecast didn't model: {string.Join("; ", forecast.Outcome.UnmodelledReasons)}");
        if (forecast.Job is { } job)
        {
            job.Pinned = false;
            job.Cancel();
        }
        // Your end of turn (player side), then the enemy turn; stop at your next turn.
        var window = new List<CombatHistoryEntry>();
        var endOfTurn = new List<CombatHistoryEntry>();
        var sawEnemy = false;
        foreach (var entry in history.Entries.Skip(from))
        {
            var side = SideOf(entry);
            if (side == CombatSide.Enemy) sawEnemy = true;
            else if (sawEnemy) break;
            window.Add(entry);
            if (!sawEnemy) endOfTurn.Add(entry);
        }

        // A teammate's end-of-turn pick from their hand (Stampede, Simpleton) is predicted from their hand (decision #25: the check uses
        // it), a branch per card it can be; one whose hand couldn't be read into the forecast (no auto-play hand built) isn't, so what it
        // did to the enemies can't be held to it (issue #430). Counted, not reported.
        var mates = forecast.Teammates.Where((player, index) => (player.Creature.HasPower<MegaCrit.Sts2.Core.Models.Powers.StampedePower>() || (SimAdapter.SimpletonOf?.Invoke(player.Creature) ?? 0) > 0)
                                                              && (index >= forecast.FullState.Teammates.Count || forecast.FullState.Teammates[index].AutoHand == null)).ToList();
        if (mates.Count > 0 && endOfTurn.OfType<CardPlayStartedEntry>().Any(cardPlayStartedEntry => cardPlayStartedEntry.CardPlay.IsAutoPlay && mates.Contains(cardPlayStartedEntry.CardPlay.Card.Owner)))
        {
            ModEntry.Log.Detail($"[audit] [outcome] turn {_frozenTurn}: not checked (a teammate's Stampede or Simpleton played a card at end of turn from a hand the forecast couldn't read)");
            Skip("a teammate's end-of-turn pick from a hand not read");
            return;
        }

        // No enemy turn came (an extra turn: The Hanged Man's ExtraTurnPower.ShouldTakeExtraTurn): nothing to compare.
        if (!sawEnemy && !youDied)
        {
            ModEntry.Log.Detail($"[audit] [outcome] turn {_frozenTurn}: not checked (no enemy turn followed: an extra turn)");
            Skip("an extra turn (no enemy turn to compare)");
            return;
        }

        var osty = me.Osty;
        var damage = window.OfType<DamageReceivedEntry>().ToList();
        // A hit that kills you (Fairy in a Bottle, Lizard Tail bring you back) takes only the HP you had left; the
        // forecast isn't capped by your HP, so the overkill counts too (Creature.LoseHpInternal: OverkillDamage).
        var actualTaken = damage.Where(otherDamage => otherDamage.Result.Receiver == me.Creature).Sum(otherDamage => otherDamage.Result.UnblockedDamage + otherDamage.Result.OverkillDamage);
        var actualOsty = osty == null ? 0 : damage.Where(otherDamage => otherDamage.Result.Receiver == osty).Sum(otherDamage => otherDamage.Result.UnblockedDamage);
        var actualBlockGain = endOfTurn.OfType<BlockGainedEntry>().Where(blockGainedEntry => blockGainedEntry.Actor == me.Creature).Sum(blockGainedEntry => blockGainedEntry.Amount);

        // Cards of yours auto-played at end of turn: the simulator plays Stampede's, Simpleton's, Howl from Beyond and
        // I Am Invincible (SimEngine.AutoPostPlay), Stampede's and Simpleton's picks a branch each. The history shows which
        // cards were picked and what they hit, so only the branches that picked the same are checked (decision #86). The turn
        // isn't checked when the pick that happened changed your hand (it drew or made cards: what came after isn't
        // simulated), or when the count matches no branch (another source).
        var issues = new List<string>();
        var autoPlays = endOfTurn.OfType<CardPlayStartedEntry>().Where(cardPlayStartedEntry => cardPlayStartedEntry.CardPlay.IsAutoPlay && cardPlayStartedEntry.CardPlay.IsFirstInSeries && cardPlayStartedEntry.CardPlay.Card.Owner == me).ToList();
        if (autoPlays.Count > 0 && autoPlays[0] is var auto)
        {
            // Your hand as the forecast saw it: each card by its identity, and what it is (picks are by what it is: SimPick).
            var hand = forecast.State.You.AutoHand?.ToDictionary(handCard => handCard.Id, handCard => handCard.Key);
            var picks = hand == null ? new List<SimPick>() : autoPlays.Where(cardPlayStartedEntry => hand.ContainsKey(SimAdapter.HandId(cardPlayStartedEntry.CardPlay.Card)))
                .Select(cardPlayStartedEntry => new SimPick(hand[SimAdapter.HandId(cardPlayStartedEntry.CardPlay.Card)], cardPlayStartedEntry.CardPlay.Target is { } creature && forecast.Ids.TryGetValue(creature, out var tid) ? tid : Who.None)).ToList();
            bool Picked(SimState state) => state.AutoPlaysUnknown
                ? state.AutoPlayed <= autoPlays.Count && picks.Count >= state.AutoPicks.Count && picks.Take(state.AutoPicks.Count).SequenceEqual(state.AutoPicks)
                : state.AutoPlayed == autoPlays.Count && picks.SequenceEqual(state.AutoPicks);
            string Predicted() => $"HP lost {actualTaken} (predicted {(forecast.TakenMin == forecast.Taken ? $"{forecast.Taken}" : $"{forecast.TakenMin}-{forecast.Taken}")})";
            var narrowed = forecast.Narrowed(Picked);
            if (narrowed == null && hand != null && picks.Count == autoPlays.Count && forecast.State.You.Stampede + forecast.State.You.Simpleton > 0)
            {
                // Every auto-play was a pick from your hand, and no branch made those picks: the simulator's picks are wrong.
                var names = string.Join(", ", autoPlays.Select(cardPlayStartedEntry => $"{cardPlayStartedEntry.CardPlay.Card.GetType().Name}{(cardPlayStartedEntry.CardPlay.Target is { } creature ? $" at {creature.Monster?.GetType().Name ?? "you"}" : "")}"));
                issues.Add($"auto-play: you auto-played {names}, and no branch picked that");
            }
            else if (narrowed == null)
            {
                ModEntry.Log.Detail($"[audit] [outcome] turn {_frozenTurn}: not checked (you auto-played {auto.CardPlay.Card.GetType().Name} at end of turn, which no branch did); {Predicted()}");
                Skip("an end-of-turn auto-play no branch made (another source)");
                return;
            }
            else if (narrowed.Outcome.Branches.Any(branch => branch.AutoPlaysUnknown))
            {
                // The pick it stopped at (the last one in that branch).
                var last = narrowed.Outcome.Branches.First(branch => branch.AutoPlaysUnknown).AutoPicks.LastOrDefault();
                var changer = autoPlays.FirstOrDefault(cardPlayStartedEntry => last != null && hand != null && hand.TryGetValue(SimAdapter.HandId(cardPlayStartedEntry.CardPlay.Card), out var picked) && picked == last.Card)?.CardPlay.Card ?? auto.CardPlay.Card;
                ModEntry.Log.Detail($"[audit] [outcome] turn {_frozenTurn}: not checked (you auto-played {changer.GetType().Name} at end of turn, which changed your hand); {Predicted()}");
                Skip("an end-of-turn auto-play that changed your hand");
                return;
            }
            else
            {
                forecast = narrowed;
            }
        }

        // A branch the forecast didn't model (decision #92, rule 2) is unknown both ways: what happened may be that branch, so
        // nothing can be called wrong. The gap is counted above; the check is skipped and counted too (rule 1).
        if (forecast.Outcome.AnyUnmodelled)
        {
            ModEntry.Log.Detail($"[audit] [outcome] turn {_frozenTurn}: not checked (a branch the forecast didn't model: {string.Join("; ", forecast.Outcome.UnmodelledReasons)}); HP lost {actualTaken} (predicted {(forecast.TakenMin == forecast.Taken ? $"{forecast.Taken}" : $"{forecast.TakenMin}-{forecast.Taken}")})");
            Skip("a branch the forecast didn't model");
            return;
        }
        // The search stopped at its memory cap (decision #107): it showed ? for your damage and promised nothing past what was
        // settled, so there's nothing to check it against. Skipped and counted (rule 1); the cap itself is reported by SimJob.
        if (forecast.Outcome.Truncated)
        {
            ModEntry.Log.Detail($"[audit] [outcome] turn {_frozenTurn}: not checked (the forecast's search stopped at its memory cap); HP lost {actualTaken}");
            Skip("the forecast's search stopped at its memory cap");
            return;
        }

        // Less than forecast is expected when random-target end-of-turn damage was left out: it may
        // have killed an attacker (the forecast stays on the safe side on purpose).
        // The forecast is the worst branch; a random target can make it come in lower (down to TakenMin).
        // Your death ends the enemy turn: hits after it never come, so it's the death that's checked (the forecast's worst
        // branch, or any branch when a roll decides it).
        if (youDied)
        {
            if (!forecast.Dies && !forecast.Outcome.Branches.Any(branch => branch.Died)) issues.Add($"your death: forecast you survive (HP lost {forecast.Taken}), but you died (HP lost {actualTaken})");
        }
        else if (actualTaken > forecast.Taken || actualTaken < forecast.TakenMin) issues.Add($"HP lost: forecast {forecast.Taken}, actual {actualTaken}");
        if (actualOsty > forecast.OstyTaken || actualOsty < forecast.OstyTakenMin) issues.Add($"Osty HP lost: forecast {forecast.OstyTaken}, actual {actualOsty}");
        // Your end-of-turn block: any branch's, since a roll can decide it (I Am Invincible on top of a draw pile whose order isn't
        // known). Every branch keeps its own breakdown (the forecast runs with Log on).
        var blockGains = forecast.Outcome.Branches.Count == 0 ? new List<int> { forecast.BlockGain }
            : forecast.Outcome.Branches.Select(branch => branch.EndOfTurnBlock.Sum(source => source.Amount)).Distinct().Order().ToList();
        if (!blockGains.Contains(actualBlockGain)) issues.Add($"end-of-turn block: forecast {string.Join(" or ", blockGains)}, actual {actualBlockGain}");
        // Teammates (multiplayer, decision #24): each one's HP lost against their range over the branches.
        var teamLine = TeammateIssues(forecast, damage, endOfTurn, issues);
        var enemyIssues = EnemyIssues(forecast, window, endOfTurn, me, out var outcomeLine, out var actuals);
        issues.AddRange(enemyIssues.Select(enemyIssue => enemyIssue.Issue));
        issues.AddRange(IntentIssues(window));
        // Each number above is checked against its own range over the branches; together they must
        // also come from one branch (one set of random targets explains everything that happened).
        var outcome = forecast.Outcome;
        if (issues.Count == 0 && !youDied && outcome.Branches.Count > 1)
        {
            bool Explains(SimState state) =>
                state.Taken == actualTaken && (osty == null || state.OstyTaken == actualOsty) && state.AutoPlayed == autoPlays.Count
                && actuals.All(entry => state.Enemy(entry.Key) is { } enemy && enemy.HpAtTurnEnd == entry.Value.Hp && enemy.UnblockedAtTurnEnd == entry.Value.Through && enemy.DiedBeforeActing == entry.Value.Died);
            if (!outcome.Branches.Any(Explains))
                issues.Add($"no single branch: each result is possible on its own, but none of the {outcome.Branches.Count} branches gives them all (HP lost {actualTaken}; {outcomeLine})");
        }
        // The chances (decision #95): each one between 0 and 100% with what happened, totalled per fight and over the session.
        foreach (var (_, id) in forecast.Ids)
            if (forecast.State.Enemy(id) is { Hp: > 0 } simEnemy && actuals.TryGetValue(id, out var act))
                Odds($"{simEnemy.Name} dies before acting", shownChances.DeadChanceRange(id), act.Died);
        Odds("you die", shownChances.YouDieChance(), youDied);
        // The most likely outcome Incoming Damage showed (decision #155): what you lost (or your death), as it said.
        if (forecast.Outcomes.Count > 1 && forecast.MostLikely is { } likely)
            Odds($"the most likely outcome ({(likely.Dies ? "you die" : $"lose {likely.Taken} HP")})", likely.Chance, youDied ? likely.Dies : !likely.Dies && actualTaken == likely.Taken);

        // Every enemy turn, matched or not: predicted vs actual per enemy, so patterns show before they're reports.
        ModEntry.Log.Detail($"[audit] [outcome] turn {_frozenTurn}:{(youDied ? " you died;" : "")} HP lost {actualTaken} (predicted {(forecast.TakenMin == forecast.Taken ? $"{forecast.Taken}" : $"{forecast.TakenMin}-{forecast.Taken}")}); {outcomeLine}{teamLine}");

        _checked++;
        Telemetry.DevEvents.Post(new Telemetry.CheckDone(Telemetry.Audits.Forecast, _frozenTurn, issues.Count == 0, issues.Count == 0 ? null : issues[0].Split(':')[0]));
        if (issues.Count == 0)
        {
            _matched++;
            return;
        }

        var log = ModEntry.Log;
        log.Detail($"[audit] turn {_frozenTurn} enemy turn: {string.Join("; ", issues)}.");
        log.Detail($"[audit]   forecast: block {forecast.CurrentBlock} + end of turn {List(forecast.EndOfTurnBlock)}; blockable {List(forecast.Blockable)}; " +
                 $"unblockable {List(forecast.Unblockable)}; your end-of-turn damage {List(forecast.EndOfTurnDamage)}; " +
                 $"skipped (poison {forecast.SkippedPoisoned}, end of turn {forecast.SkippedEndOfTurn}), cancelled hits {forecast.CancelledHits}, Osty HP {forecast.OstyHp}.");
        foreach (var entry in window)
            if (entry is DamageReceivedEntry or BlockGainedEntry)
                log.Detail($"[audit]   actual: {entry.HumanReadableString}");

        // One report per issue, grouped by what was wrong and the encounter.
        var reporter = Sts2Mods.Core.Diagnostics.ErrorReporter.Instance;
        if (reporter is not { Enabled: true }) return;
        var encounter = Sts2State.Combat?.Encounter?.GetType().Name ?? "unknown";
        var breakdown = $"block {forecast.CurrentBlock} + end of turn {List(forecast.EndOfTurnBlock)}\nblockable {List(forecast.Blockable)}\nunblockable {List(forecast.Unblockable)}\n" +
                        $"your end-of-turn damage {List(forecast.EndOfTurnDamage)}\nskipped: poison {forecast.SkippedPoisoned}, end of turn {forecast.SkippedEndOfTurn}; cancelled hits {forecast.CancelledHits}; Osty HP {forecast.OstyHp}";
        var prediction = Prediction(forecast);
        var fullWindow = string.Join("\n", window.Select((entry, index) => $"#{from + index} {ProcAudit.Describe(entry)}{(SideOf(entry) == CombatSide.Enemy ? " (enemy turn)" : "")}"));
        foreach (var issue in issues)
        {
            var kind = issue.Split(':')[0];
            // Enemy outcome issues group by the monster too; amounts are left out of the fingerprint.
            var monster = enemyIssues.FirstOrDefault(enemyIssue => enemyIssue.Issue == issue).Monster;
            var fingerprint = monster != null ? new[] { kind, encounter, monster } : new[] { kind, encounter };
            reporter.Mismatch("forecast", $"Forecast audit: {issue} ({encounter}, turn {_frozenTurn})", fingerprint,
                $"Enemy turn after your turn {_frozenTurn}.\nIssue: {issue}\nAll issues: {string.Join("; ", issues)}\n\nForecast:\n{breakdown}\n\nSimulator, per enemy (over {forecast.Outcome.Branches.Count} branch(es){(forecast.Outcome.Truncated ? ", truncated" : "")}):\n{prediction}",
                new Dictionary<string, string> { ["audit_window"] = fullWindow, ["sim_state"] = forecast.State.ToJson() },
                new Dictionary<string, string> { ["check"] = kind, ["encounter"] = encounter, ["monster"] = monster ?? "" });
        }
    }

    /// <summary>
    /// The simulator's per-enemy predictions against what the history shows (see the class summary).
    /// Returns each issue with the monster it's about. Only a finished search gets here: a capped one is skipped first (decision #107),
    /// so every range below covers every outcome.
    /// </summary>
    private static List<(string Issue, string? Monster)> EnemyIssues(TurnEndModel forecast, List<CombatHistoryEntry> window, List<CombatHistoryEntry> endOfTurn, Player me, out string summary,
        out Dictionary<int, (int Hp, int Through, bool Died)> actuals)
    {
        var result = new List<(string, string?)>();
        actuals = new Dictionary<int, (int Hp, int Through, bool Died)>();
        var parts = new List<string>();
        summary = "";
        var outcome = forecast.Outcome;
        if (outcome.Branches.Count == 0) return result;
        foreach (var (creature, id) in forecast.Ids)
        {
            var start = forecast.State.Enemy(id);
            if (start == null || start.Hp <= 0) continue;
            var monster = creature.Monster?.GetType().Name ?? "enemy";
            var name = start.Name;

            // What happened: HP lost and instances through its block during your end of turn; whether it
            // died before its move; its first hit on you.
            // Up to the hit that kills it, unless its own death's code brought it back, as the simulator follows (the Waterfall Giant with Steam
            // Eruption: alive at 999999999 HP, its max: decision #276), when its HP is that less the hits after. One that comes back later, in its
            // own move (Test Subject's Respawn: not simulated), counts as dead before acting, as the simulator has it.
            var eot = endOfTurn.OfType<DamageReceivedEntry>().Where(damage => damage.Result.Receiver == creature).ToList();
            var killHit = eot.FindIndex(damage => damage.Result.WasTargetKilled);
            var revived = killHit >= 0 && creature.IsAlive && outcome.InSome(branch => branch.Enemy(id) is { Revived: true });
            var hpAtTurnEnd = revived ? Math.Max(0, creature.MaxHp - eot.Skip(killHit + 1).Sum(damage => damage.Result.UnblockedDamage)) : 0;
            if (killHit >= 0 && !revived) eot = eot.Take(killHit + 1).ToList();
            if (!revived) hpAtTurnEnd = Math.Max(0, start.Hp - eot.Sum(damage => damage.Result.UnblockedDamage));
            var through = eot.Count(damage => damage.Result.UnblockedDamage > 0);
            var killAt = window.FindIndex(entry => entry is DamageReceivedEntry damage && damage.Result.Receiver == creature && damage.Result.WasTargetKilled);
            var moveAt = window.FindIndex(entry => entry is MonsterPerformedMoveEntry monsterPerformedMoveEntry && ReferenceEquals(monsterPerformedMoveEntry.Monster, creature.Monster));
            // Killed during its own attack (your Thorns hits back before each hit lands, ThornsPower.BeforeDamageReceived;
            // the move is logged once it ends): its hit still lands, so it acted.
            var hitAfterKill = killAt >= 0 && window.Skip(killAt + 1).Take(moveAt > killAt ? moveAt - killAt - 1 : int.MaxValue)
                .Any(entry => entry is DamageReceivedEntry damage && damage.Dealer == creature);
            var diedBeforeActing = killAt >= 0 && (moveAt < 0 || killAt < moveAt) && !hitAfterKill && !revived;
            string Range(int low, int high) => low == high ? $"{low}" : $"{low}-{high}";
            parts.Add($"{name} HP {start.Hp}→{hpAtTurnEnd} (pred {Range(outcome.HpAtTurnEndMin(id), outcome.HpAtTurnEndMax(id))}), through {through} (pred {Range(outcome.UnblockedAtTurnEndMin(id), outcome.UnblockedAtTurnEndMax(id))}), " +
                      $"died before acting {(diedBeforeActing ? "yes" : "no")} (pred {(outcome.DeadInAll(id) ? "always" : outcome.DeadInAny(id) ? "sometimes" : "never")})");

            actuals[id] = (hpAtTurnEnd, through, diedBeforeActing);
            if (outcome.DeadInAll(id) && !diedBeforeActing)
                result.Add(($"enemy death: {name} predicted dead before acting, but it lived to act (or outlived you)", monster));
            if (!outcome.DeadInAny(id) && diedBeforeActing)
                result.Add(($"enemy death: {name} predicted alive, but it died before acting", monster));

            int hpAtTurnEndMin = outcome.HpAtTurnEndMin(id), hpAtTurnEndMax = outcome.HpAtTurnEndMax(id);
            if (hpAtTurnEnd < hpAtTurnEndMin || hpAtTurnEnd > hpAtTurnEndMax)
                result.Add(($"enemy HP at turn end: {name} predicted {(hpAtTurnEndMin == hpAtTurnEndMax ? $"{hpAtTurnEndMin}" : $"{hpAtTurnEndMin}-{hpAtTurnEndMax}")}, actual {hpAtTurnEnd}", monster));
            // Your end of turn's hits on it, against the simulator's count when your end of turn is over (its turn's hits on it, Necro
            // Mastery's when it hits Osty, come later in both).
            int ulo = outcome.UnblockedAtTurnEndMin(id), uhi = outcome.UnblockedAtTurnEndMax(id);
            if (through < ulo || through > uhi)
                result.Add(($"hits through block: {name} predicted {(ulo == uhi ? $"{ulo}" : $"{ulo}-{uhi}")}, actual {through}", monster));

            // Imbalanced's stun: ImbalancedPower.AfterDamageGiven's `dealer == base.Owner && result.WasFullyBlocked`, told of every result of every
            // damage it deals (CreatureCmd.Damage: each player's, a redirect's), each one the fight's log keeps (DamageReceivedEntry).
            if (start.Powers.Has(PowerId.Imbalanced))
            {
                var setOff = window.OfType<DamageReceivedEntry>().Any(damage => damage.Dealer == creature && damage.Result.WasFullyBlocked);
                if (outcome.FlashedInAll(PowerId.Imbalanced.Name, id) && !setOff)
                    result.Add(($"Imbalanced: {name} predicted to set off its stun (a hit of its fully blocked), but no hit of its was", monster));
                if (!outcome.FlashedInAny(PowerId.Imbalanced.Name, id) && setOff)
                    result.Add(($"Imbalanced: {name} predicted not to set off its stun, but a hit of its was fully blocked", monster));
            }
        }
        summary = string.Join("; ", parts);
        return result;
    }

    /// <summary>Per enemy: the simulator's start state and predicted outcomes, for reports.</summary>
    private static string Prediction(TurnEndModel forecast)
    {
        var outcome = forecast.Outcome;
        var lines = new List<string>();
        foreach (var (_, id) in forecast.Ids)
        {
            var enemy = forecast.State.Enemy(id);
            if (enemy == null) continue;
            lines.Add($"{enemy.Name}: HP {enemy.Hp}, block {enemy.Block}, poison {enemy.Poison}, hits [{string.Join(", ", enemy.Hits)}]" +
                      $" → HP at turn end {outcome.HpAtTurnEndMin(id)}-{outcome.HpAtTurnEndMax(id)}, hits through block {outcome.UnblockedAtTurnEndMin(id)}-{outcome.UnblockedAtTurnEndMax(id)}, " +
                      $"dead before acting {(outcome.DeadInAll(id) ? "always" : outcome.DeadInAny(id) ? "sometimes" : "never")}" +
                      $"{(enemy.Powers.Has(PowerId.Imbalanced) ? $", Imbalanced set off {(outcome.FlashedInAll(PowerId.Imbalanced.Name, id) ? "always" : outcome.FlashedInAny(PowerId.Imbalanced.Name, id) ? "sometimes" : "never")}" : "")}");
        }
        var player = forecast.State.You;
        lines.Add($"you: block {player.Block}, orbs [{string.Join(", ", player.Orbs.Select(orb => $"{orb.Kind} {orb.Passive}/{orb.Evoke}"))}] (slots {player.OrbCapacity}), Juggernaut {player.Juggernaut}, Plating {player.Plating}, " +
                  $"hand {player.HandCount}, Ethereal exhausting {player.Fading}");
        return string.Join("\n", lines);
    }

    private static string List(IEnumerable<Source> sources)
    {
        var listed = string.Join(", ", sources.Select(source => $"{source.Name} {source.Amount}"));
        return listed.Length == 0 ? "none" : listed;
    }
}
