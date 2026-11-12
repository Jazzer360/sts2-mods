using MegaCrit.Sts2.Core.Entities.Cards;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Combat.History;
using MegaCrit.Sts2.Core.Combat.History.Entries;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using SpireQoL.Cards;
using SpireQoL.Data;
using SpireQoL.Features;
using SpireQoL.Procs;
using SpireQoL.Sim;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.GameActions;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using Sts2Mods.Core.Features;
using Sts2Mods.Core.Game;
using Sts2Mods.Core.Ui;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.Models.Cards;

namespace SpireQoL.Diagnostics;

/// <summary>
/// Checks every card play against what the mod predicted, and logs mismatches to the game log
/// (lines start with [SpireQoL] [audit]), so missing or wrong tag rules surface from real play.
///
/// Each card you play is one PlayCardAction in the game's action queue: spending its cost, before-play
/// procs, the card, after-play procs, exhausting or discarding it, and every draw or effect its hooks
/// set off. Everything that happens while that action is running is attributed to the play: history
/// entries (CombatHistory.Changed), relic and power flashes, and shuffles (an observe-only prefix on
/// CardPileCmd.Shuffle). Potions and ending the turn are other actions, so they never mix in. When the
/// action finishes (GameAction.AfterFinished) the play has fully resolved and is checked. Compared:
///  - Fired with no tab: a relic or power that flashed (RelicModel/PowerModel.Flashed).
///  - Relic tab that didn't fire: a predicted, uncrossed relic tab whose relic never flashed.
///  - Proc block: block you gained from anything but the card itself vs the block tabs (exact).
///  - Proc damage: damage with no card source vs the damage tabs' audit range (each target's own
///    modifiers applied), separately for damage to enemies and to you.
///  - Budget: draws, exhausts, discards, created cards, orbs, summons and powers applied, actual vs
///    what the card itself does (PlayFacts) plus what predicted tabs can add. More than that with no
///    tab that explains it = "unexplained" (a missing tag). Less than the card's own = a "[model]"
///    line (our model of the card is off; often fine, e.g. an empty draw pile).
///  - Shuffle: whether the play shuffled your discard pile into your draw pile (seen from the piles'
///    events: the game keeps no history entry for it) against PlayFacts.Shuffles.
///  - State after the play: once the play has fully resolved (the game's action queue is idle), the
///    simulator's state is rebuilt from the game and compared with the simulator's own prediction
///    of the play (the same start state and play behind the card's lethal, Imbalanced and wake
///    tabs): enemy HP, block, poison and intents; your block, Plating, Juggernaut, Thorns, Flame
///    Barrier, Feel No Pain, orbs and Osty. Random targets are branches: reported only when no
///    branch matches. A mismatch means those tabs were computed from the wrong state.
/// A one-line summary per combat gives the match rate. Read-only: events and history only.
/// </summary>
public sealed class ProcAudit : IFeature, IFrameFeature
{
    private readonly QolContext _ctx;

    public ProcAudit(QolContext ctx) => _ctx = ctx;

    public string Id => "proc-audit";
    public string DisplayName => "Proc Audit (log only)";
    public bool EnabledByDefault => true;

    public void Apply(Harmony harmony)
    {
        // Observe-only: counts shuffles (the game keeps no history entry for them). Doesn't change the game.
        harmony.Patch(AccessTools.Method(typeof(CardPileCmd), nameof(CardPileCmd.Shuffle), new[] { typeof(PlayerChoiceContext), typeof(Player) }),
            prefix: new HarmonyMethod(typeof(ProcAudit), nameof(ShufflePrefix)));
    }

    public void Attach(OverlayHost host)
    {
    }

    private sealed class Window
    {
        /// <summary>The card a hand choice auto-played (Decisions, Decisions' pick): its plays are predicted, not random.</summary>
        public CardModel? Chosen;
        public required CardModel Card;
        public required string CardName;
        public required IReadOnlyList<TabSpec> Predicted;
        public required PlayFacts? Facts;
        /// <summary>History entries the play's action added (see PlayRecord), and their span for reports.</summary>
        public required List<int> Indices;
        public required int From;
        public required int To;
        /// <summary>History index of this play's own CardPlayStartedEntry.</summary>
        public required int Start;
        public required List<(int At, AbstractModel Model)> Flashes;
        public int Shuffles;
        /// <summary>Another play finished after this card's tabs were last computed (played quickly): not checked.</summary>
        public string? StaleBecause;
        /// <summary>Game state when the play started (before its effects), for reports.</summary>
        public IReadOnlyDictionary<string, string>? Before;
        /// <summary>Frames between the card's last frame in hand and its play starting.</summary>
        public long Lag;
        /// <summary>The play's target (single-target cards).</summary>
        public Creature? Target;
        /// <summary>The simulator's state rebuilt from the game when the play's action finished.</summary>
        public SimState? After;
        public Dictionary<Creature, int>? AfterIds;
        /// <summary>Why the after-play state wasn't captured.</summary>
        public string? AfterSkipped;
        /// <summary>
        /// The play ended the fight. The game stops logging history once it's ending (CreatureCmd.Damage
        /// skips History.DamageReceived when CombatManager.IsEnding), so the killing hit and anything after
        /// it are missing: shortfalls against the prediction aren't checked.
        /// </summary>
        public bool FightEnded;
        public Dictionary<(Creature, string), int>? PowersBefore, PowersAfter;
        public PlaySnapshot? FullBefore, FullAfter;
    }

    private enum Effect { Draw, Exhaust, Discard, Generate, Orb, Summon, Power }

    /// <summary>
    /// Powers that flash when they change damage (caps), not because a play set them off. The
    /// card's damage already reflects them, so they never get a tab.
    /// </summary>
    private static readonly HashSet<string> Modifiers = new() { nameof(HardToKillPower), nameof(HardenedShellPower), nameof(IntangiblePower) };

    /// <summary>
    /// Your powers that react to which card is drawn (draw more, auto-play it): the draw order is
    /// hidden, so they're never predicted. When they fire on a play that drew cards, noted, not reported.
    /// </summary>
    private static readonly HashSet<string> DrawReactive = new() { nameof(PagestormPower), nameof(IterationPower), nameof(HellraiserPower), nameof(ChainsOfBindingPower) };

    /// <summary>Sources whose tabs come from the outcome simulator (missing while its run was pending).</summary>
    private static readonly string[] SimTabSources =
    {
        nameof(ImbalancedPower), nameof(AsleepPower), nameof(SlumberPower), nameof(InfestedPower), nameof(SteamEruptionPower),
        nameof(SurprisePower), nameof(CrabRagePower), nameof(RavenousPower), nameof(GremlinHorn), nameof(CurlUpPower), nameof(SkittishPower), nameof(SneakyPower), nameof(EnragePower), nameof(JuggernautPower),
        nameof(BurrowedPower), nameof(FlutterPower), nameof(PlowPower), nameof(ShriekPower), nameof(TheGambitPower), nameof(SlipperyPower), nameof(HandDrill),
    };

    /// <summary>Your relics and powers that react to an enemy dying or being hit.</summary>
    private static readonly HashSet<string> KillReactive = new() { nameof(GremlinHorn) };

    /// <summary>What a trigger tab's source can add beyond the card itself (for the budget check).</summary>
    private static readonly Dictionary<string, Effect[]> SourceEffects = new()
    {
        // draws (Dark Embrace, Game Piece, Iron Club, Joss Paper, Vicious and Centennial Puzzle are
        // counted in PlayFacts.Draws, so their tabs explain nothing beyond it)
        [nameof(GremlinHorn)] = new[] { Effect.Draw }, [nameof(UnceasingTop)] = new[] { Effect.Draw }, [nameof(CorrosiveWavePower)] = new[] { Effect.Power },
        [nameof(PagestormPower)] = new[] { Effect.Draw }, [nameof(IterationPower)] = new[] { Effect.Draw },
        [nameof(BiiigHug)] = new[] { Effect.Generate },
        // created cards
        [nameof(CalamityPower)] = new[] { Effect.Generate }, [nameof(JugglingPower)] = new[] { Effect.Generate }, [nameof(MusicBox)] = new[] { Effect.Generate },
        [nameof(BurningSticks)] = new[] { Effect.Generate }, [nameof(PersonalHivePower)] = new[] { Effect.Generate }, [nameof(WitheringPresencePower)] = new[] { Effect.Generate },
        [nameof(ImitationLearningPower)] = new[] { Effect.Generate, Effect.Power },
        // orbs
        [nameof(StormPower)] = new[] { Effect.Orb }, [nameof(TrashToTreasurePower)] = new[] { Effect.Orb },
        // summons
        [nameof(DevourLifePower)] = new[] { Effect.Summon },
        // powers
        [nameof(MonologuePower)] = new[] { Effect.Power }, [nameof(Kunai)] = new[] { Effect.Power }, [nameof(Shuriken)] = new[] { Effect.Power },
        [nameof(HelicalDart)] = new[] { Effect.Power }, [nameof(RainbowRing)] = new[] { Effect.Power }, [nameof(MiniRegent)] = new[] { Effect.Power },
        [nameof(ArsenalPower)] = new[] { Effect.Power }, [nameof(RupturePower)] = new[] { Effect.Power }, [nameof(EnvenomPower)] = new[] { Effect.Power },
        [nameof(ConcoctPower)] = new[] { Effect.Power }, [nameof(ReaperFormPower)] = new[] { Effect.Power }, [nameof(MonarchsGazePower)] = new[] { Effect.Power },
        [nameof(OblivionPower)] = new[] { Effect.Power }, [nameof(TenderPower)] = new[] { Effect.Power }, [nameof(EnragePower)] = new[] { Effect.Power },
        [nameof(HandDrill)] = new[] { Effect.Power }, [nameof(UnsettlingLamp)] = new[] { Effect.Power }, [nameof(SelfFormingClay)] = new[] { Effect.Power },
        [nameof(RedSkull)] = new[] { Effect.Power }, [nameof(CrabRagePower)] = new[] { Effect.Power }, [nameof(RavenousPower)] = new[] { Effect.Power },
        [nameof(SlowPower)] = new[] { Effect.Power }, [nameof(SmoggyPower)] = new[] { Effect.Power }, [nameof(VitalSparkPower)] = new[] { Effect.Power },
        [nameof(SleightOfFleshPower)] = new[] { Effect.Power }, [nameof(MasterPlannerPower)] = new[] { Effect.Power }, [nameof(PaleBlueDotPower)] = new[] { Effect.Power },
    };

    private CombatHistory? _history;
    private readonly HashSet<AbstractModel> _subscribed = new(ReferenceEqualityComparer.Instance);

    // Per-combat tallies for the summary line.
    private int _checked;
    private int _withLeadIn; // plays whose tabs ran plays still resolving first (decision #27)
    private int _matched;
    private readonly Dictionary<string, int> _issueCounts = new();

    // Every play this turn (card, its tabs, result), sent with each report: the turn is the unit
    // that explains most mismatches (counters, "first this turn" relics, queued plays).
    private readonly List<string> _turnPlays = new();
    private int _turnPlaysTurn = -1;

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
        /// <summary>Every power on every creature when the play started and when it finished.</summary>
        public Dictionary<(Creature, string), int>? PowersBefore, PowersAfter;
        /// <summary>Energy, stars, gold, HP, cards and relics when the play started and finished.</summary>
        public PlaySnapshot? FullBefore, FullAfter;
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
        public IReadOnlyList<TabSpec>? Predicted;
        public PlayFacts? Facts;
        public long Lag = -1;
        public string? StaleBecause;
    }

    /// <summary>Power amounts on every creature in the fight, by creature and power class.</summary>
    private static Dictionary<(Creature, string), int> PowerSnapshot()
    {
        var d = new Dictionary<(Creature, string), int>();
        var cs = Sts2State.Combat;
        if (cs == null) return d;
        foreach (var c in cs.Creatures)
            foreach (var p in c.Powers)
                d[(c, p.GetType().Name)] = d.TryGetValue((c, p.GetType().Name), out var a) ? a + p.Amount : p.Amount;
        return d;
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
        var cs = Sts2State.Combat;
        if (me == null || cs == null)
        {
            _finished.Clear();
            _running.Clear();
            _current = null;
            Summarize();
            DetachHistory();
            return;
        }

        // While a hand choice's pick will be auto-played, keep each hand card's tabs and facts on the play
        // that opened it (they describe the auto-plays; once the choice closes they're back to one play).
        if (CardIndicatorsFeature.ChoiceAutoPlays(me) > 0
            && _running.Values.FirstOrDefault(r => r.Card is DecisionsDecisions) is { } choosing)
        {
            var hand = new Dictionary<CardModel, (IReadOnlyList<TabSpec>, PlayFacts?)>(ReferenceEqualityComparer.Instance);
            foreach (var c in Sts2State.Hand(me))
                hand[c] = (_ctx.Decor.LastInHand.TryGetValue(c, out var t) ? t.ToList() : new List<TabSpec>(),
                           ProcIndicatorsFeature.LastFacts.TryGetValue(c, out var cf) ? cf : null);
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
        var queueSet = RunManager.Instance?.ActionQueueSet;
        if (queueSet != null && !ReferenceEquals(queueSet, _queueSet))
        {
            if (_queueSet != null) _queueSet.ActionEnqueued -= OnEnqueued;
            _queueSet = queueSet;
            _queueSet.ActionEnqueued += OnEnqueued;
            _atEnqueue.Clear();
        }

        Subscribe(me, cs);
        _countAtFrame[_ctx.Decor.Frame] = history.Entries.Count();
        if (_countAtFrame.Count > 600) _countAtFrame.Remove(_countAtFrame.Keys.Min());

        while (_finished.Count > 0) Check(_finished.Dequeue(), me);
    }

    private ActionQueueSet? _queueSet;

    /// <summary>
    /// The tabs and facts a play of yours had the moment it was queued (ActionQueueSet.ActionEnqueued): what
    /// you saw when you played it. A queued card stays in your hand until it starts, and keeps being
    /// recomputed there, so its tabs at the start can be from after the plays ahead of it resolved.
    /// </summary>
    private readonly Dictionary<GameAction, (List<TabSpec>? Tabs, PlayFacts? Facts)> _atEnqueue = new(ReferenceEqualityComparer.Instance);

    private void OnEnqueued(GameAction action)
    {
        try
        {
            if (action is not PlayCardAction play || play.Player != Sts2State.Me) return;
            var card = play.NetCombatCard.ToCardModelOrNull();
            if (card == null) return;
            _atEnqueue[action] = (_ctx.Decor.LastInHand.TryGetValue(card, out var tabs) ? tabs.ToList() : null,
                                  ProcIndicatorsFeature.LastFacts.TryGetValue(card, out var facts) ? facts : null);
            if (_atEnqueue.Count > 50) _atEnqueue.Remove(_atEnqueue.Keys.First());
        }
        catch (Exception ex)
        {
            ModEntry.Log.Warn($"[audit] couldn't record a queued play: {ex.Message}");
        }
    }

    private void DetachHistory()
    {
        if (_history != null) _history.Changed -= OnHistoryChanged;
        _history = null;
    }

    private void OnBeforeAction(GameAction action)
    {
        try
        {
            if (action is not PlayCardAction play || play.Player != Sts2State.Me)
            {
                _current = null; // potions, end of turn, other players: not a play of yours
                return;
            }
            if (!_running.TryGetValue(action, out var rec))
            {
                rec = new PlayRecord { Action = action, Target = play.Target, Before = CaptureBefore(), PowersBefore = PowerSnapshot(), FullBefore = PlaySnapshot.Take(play.Player) };
                var card = play.NetCombatCard.ToCardModelOrNull();
                if (card != null)
                {
                    rec.Card = card;
                    // What you saw when you played it: from the moment it was queued (else its last frame in hand).
                    if (_atEnqueue.Remove(action, out var queued) && queued.Facts != null)
                    {
                        rec.Predicted = queued.Tabs;
                        rec.Facts = queued.Facts;
                    }
                    else
                    {
                        rec.Predicted = _ctx.Decor.LastInHand.TryGetValue(card, out var tabs) ? tabs.ToList() : null;
                        rec.Facts = ProcIndicatorsFeature.LastFacts.TryGetValue(card, out var facts) ? facts : null;
                    }
                    rec.Lag = _ctx.Decor.LastSeenFrame.TryGetValue(card, out var seen) ? _ctx.Decor.Frame - seen : -1;
                    if (_history != null) rec.StaleBecause = StaleBecause(_history.Entries.ToList(), card, _history.Entries.Count(), play.Player);
                }
                _running[action] = rec;
                action.AfterFinished += OnActionFinished;
                action.BeforeCancelled += OnActionCancelled;
                action.BeforePausedForPlayerChoice += OnActionPaused;
            }
            // Also called again when it resumes after a choice (Burning Pact's pick).
            _current = rec;
        }
        catch (Exception ex)
        {
            ModEntry.Log.Warn($"[audit] couldn't track an action: {ex.Message}");
            Sts2Mods.Core.Diagnostics.ErrorReporter.Instance?.Exception(ex, "audit-track");
        }
    }

    private void OnActionPaused(GameAction action)
    {
        OnHistoryChanged();
        if (_current != null && ReferenceEquals(_current.Action, action)) _current = null;
    }

    private void OnActionCancelled(GameAction action)
    {
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

    private void OnActionFinished(GameAction action)
    {
        try
        {
            OnHistoryChanged();
            Unhook(action);
            if (!_running.Remove(action, out var rec)) return;
            if (_current != null && ReferenceEquals(_current.Action, action)) _current = null;
            // The play has fully resolved and nothing else has run: the state it left.
            var me = Sts2State.Me;
            var cs = Sts2State.Combat;
            if (CombatManager.Instance.IsOverOrEnding)
            {
                // The fight is ending: its cleanup (temporary powers expiring, Focus back) changes the
                // state, so the state the play left can't be compared. The history checks still run.
                rec.AfterSkipped = "the fight ended";
                rec.FightEnded = true;
            }
            else if (me != null && cs != null)
            {
                rec.PowersAfter = PowerSnapshot();
                rec.FullAfter = PlaySnapshot.Take(me);
                try
                {
                    rec.After = SimAdapter.Build(me, cs, out var ids);
                    rec.AfterIds = ids;
                }
                catch (Exception ex)
                {
                    rec.AfterSkipped = $"couldn't read the state: {ex.Message}";
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
        if (_history == null) return;
        var count = _history.Entries.Count();
        if (count < _attributed) _attributed = count; // cleared
        if (_current != null)
            for (var i = _attributed; i < count; i++) _current.Indices.Add(i);
        _attributed = count;
    }

    private void OnRelicFlashed(RelicModel relic, IEnumerable<Creature> _) => Flash(relic);

    private void OnPowerFlashed(PowerModel power) => Flash(power);

    private void Flash(AbstractModel m)
    {
        if (_history == null || _current == null) return;
        OnHistoryChanged();
        _current.Flashes.Add((_history.Entries.Count(), m));
    }

    /// <summary>
    /// Harmony prefix on CardPileCmd.Shuffle (observes only; the game keeps no history entry for a
    /// shuffle): counts it for the play running now.
    /// </summary>
    private static void ShufflePrefix(Player player)
    {
        try
        {
            var a = _instance;
            if (a?._current == null || player != Sts2State.Me || CombatManager.Instance.IsOverOrEnding) return;
            a._current.Shuffles++;
        }
        catch
        {
            // diagnostics only
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
        // Plays its tabs already ran first (the lead-in, decision #27) don't make them stale.
        var leadIn = ProcIndicatorsFeature.LastFacts.TryGetValue(card, out var facts) && facts.Sim != null ? facts.Sim.LeadInCards : Array.Empty<CardModel>();
        for (var k = Math.Max(0, then); k < start && k < hist.Count; k++)
        {
            // Any player's play (a teammate's too) or a potion that resolved in between, unless the tabs ran it first.
            if (hist[k] is CardPlayFinishedEntry { CardPlay: var p } && !ReferenceEquals(p.Card, card)
                && !leadIn.Any(c => ReferenceEquals(c, p.Card)))
                return SafeTitle(p.Card);
            if (hist[k] is PotionUsedEntry pu)
                return SafePotion(pu);
        }
        return null;
    }

    /// <summary>
    /// The window for reports: the history around the play, each entry with its position, in our
    /// own words (the game's draw text says "discarded"), marked when it isn't this play's, and the
    /// relic and power flashes at their positions.
    /// </summary>
    private static List<string> WindowLines(Window w, List<CombatHistoryEntry> hist, Player me)
    {
        var lines = new List<string>();
        var mine = w.Indices.ToHashSet();
        var from = Math.Max(0, w.From - 3);
        var to = Math.Min(hist.Count, w.To + 3);
        for (var i = from; i <= to; i++)
        {
            foreach (var fl in w.Flashes.Where(x => x.At == i)) lines.Add($"   ⚡ {fl.Model.GetType().Name} flashed");
            if (i >= to) break;
            lines.Add($"{(mine.Contains(i) ? "" : "(not this play) ")}#{i} {Describe(hist[i])}");
        }
        return lines;
    }

    internal static string Describe(CombatHistoryEntry e)
    {
        static string N(object? o) => o switch { null => "?", Creature c => c.Monster?.GetType().Name ?? c.Player?.Character?.GetType().Name ?? "creature", _ => o.GetType().Name };
        try
        {
            return e switch
            {
                CardPlayStartedEntry s => $"play started: {N(s.CardPlay.Card)} (play {s.CardPlay.PlayIndex + 1}/{s.CardPlay.PlayCount}{(s.CardPlay.IsAutoPlay ? ", auto-play" : "")}, energy {s.CardPlay.Resources.EnergyValue})",
                CardPlayFinishedEntry s => $"play finished: {N(s.CardPlay.Card)}",
                CardDrawnEntry s => $"drew {N(s.Card)}{(s.FromHandDraw ? " (turn draw)" : "")}",
                CardDiscardedEntry s => $"discarded {N(s.Card)}",
                CardExhaustedEntry s => $"exhausted {N(s.Card)}",
                CardGeneratedEntry s => $"created {N(s.Card)}",
                CardAfflictedEntry s => $"afflicted {N(s.Card)} with {N(s.Affliction)}",
                EnergySpentEntry s => $"spent {s.Amount} energy",
                StarsModifiedEntry s => $"stars {(s.Amount >= 0 ? "+" : "")}{s.Amount}",
                BlockGainedEntry s => $"{N(s.Actor)} gained {s.Amount} block{(s.CardPlay != null ? $" (from {N(s.CardPlay.Card)})" : "")}",
                DamageReceivedEntry s => $"{N(s.Dealer)} → {N(s.Result.Receiver)}: {s.Result.TotalDamage} ({s.Result.BlockedDamage} blocked, {s.Result.UnblockedDamage} through){(s.Result.WasTargetKilled ? ", killed" : "")}{(s.CardSource != null ? $" [card {N(s.CardSource)}]" : " [no card]")}",
                PowerReceivedEntry s => $"{N(s.Applier)} → {N(s.Actor)}: {N(s.Power)} {(s.Amount >= 0 ? "+" : "")}{s.Amount}",
                OrbChanneledEntry s => $"channeled {N(s.Orb)}",
                SummonedEntry s => $"summoned {s.Amount}",
                PotionUsedEntry s => $"potion {N(s.Potion)}",
                MonsterPerformedMoveEntry s => $"{N(s.Monster)} move {s.Move.Id}",
                _ => e.Description,
            };
        }
        catch
        {
            return e.GetType().Name;
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

    private void Subscribe(Player me, ICombatState cs)
    {
        foreach (var r in me.Relics)
        {
            if (_subscribed.Add(r)) r.Flashed += OnRelicFlashed;
        }
        foreach (var c in cs.Creatures)
        {
            foreach (var p in c.Powers)
            {
                if (_subscribed.Add(p)) p.Flashed += OnPowerFlashed;
            }
        }
    }

    /// <summary>Checks one finished play of yours (see PlayRecord) and logs and reports what didn't match.</summary>
    private void Check(PlayRecord rec, Player me)
    {
        if (_history == null) return;
        var hist = _history.Entries.ToList();
        var indices = rec.Indices.Where(i => i < hist.Count).ToList();
        var start = indices.FirstOrDefault(i => hist[i] is CardPlayStartedEntry { CardPlay: var p } && p.Card.Owner == me && p.IsFirstInSeries && !p.IsAutoPlay, -1);
        if (start < 0) return; // nothing was played (the card couldn't be played after all)
        var card = ((CardPlayStartedEntry)hist[start]).CardPlay.Card;
        // What was predicted when the play's action started (fallback: now, for a card the action
        // couldn't name up front).
        var captured = ReferenceEquals(rec.Card, card);
        var predicted = captured && rec.Predicted != null ? rec.Predicted
            : _ctx.Decor.LastInHand.TryGetValue(card, out var tabs) ? tabs : Array.Empty<TabSpec>();
        var facts = captured ? rec.Facts : ProcIndicatorsFeature.LastFacts.TryGetValue(card, out var f0) ? f0 : null;
        // A hand choice's pick, auto-played: its tabs and facts from the choice join the card's own.
        var autoPlays = indices.Select(i => hist[i]).OfType<CardPlayStartedEntry>().Where(e => e.CardPlay.IsAutoPlay && e.CardPlay.Card.Owner == me).ToList();
        CardModel? chosen = null;
        var target = rec.Target;
        if (rec.Choice != null && autoPlays.Count > 0 && autoPlays.All(e => ReferenceEquals(e.CardPlay.Card, autoPlays[0].CardPlay.Card))
            && rec.Choice.TryGetValue(autoPlays[0].CardPlay.Card, out var pick))
        {
            chosen = autoPlays[0].CardPlay.Card;
            predicted = predicted.Concat(pick.Tabs).ToList();
            facts = facts != null && pick.Facts != null ? facts.Then(pick.Facts) : pick.Facts ?? facts;
            // Its simulated plays share one target; a single-target pick gets a random one each time.
            var targets = autoPlays.Select(e => e.CardPlay.Target).Distinct().ToList();
            target = targets.Count == 1 ? targets[0] : null;
        }
        var w = new Window
        {
            Chosen = chosen,
            Card = card, CardName = SafeTitle(card), Predicted = predicted, Facts = facts,
            Start = start, From = indices.Min(), To = indices.Max() + 1, Indices = indices,
            Flashes = rec.Flashes, Shuffles = rec.Shuffles,
            Lag = captured ? rec.Lag : _ctx.Decor.LastSeenFrame.TryGetValue(card, out var seen) ? _ctx.Decor.Frame - seen : -1,
            Before = rec.Before, Target = target,
            After = rec.After, AfterIds = rec.AfterIds, AfterSkipped = rec.AfterSkipped, FightEnded = rec.FightEnded,
            PowersBefore = rec.PowersBefore, PowersAfter = rec.PowersAfter,
            FullBefore = rec.FullBefore, FullAfter = rec.FullAfter,
            StaleBecause = captured ? rec.StaleBecause : StaleBecause(hist, card, start, me),
        };
        Report(w, me, hist);
    }

    private void Report(Window w, Player me, List<CombatHistoryEntry> hist)
    {
        try
        {
            var entries = w.Indices.Select(i => hist[i]).ToList();
            var flashed = w.Flashes.Select(x => x.Model).ToList();
            var issues = Compare(w, me, entries, flashed, out var model);
            ShuffleIssues(w, me, entries, flashed, issues, model);
            AfterPlayIssues(w, entries, issues, model);
            PowerChangeIssues(w, me, entries, flashed, issues, model);
            EverythingIssues(w, me, entries, flashed, issues, model);
            if (w.StaleBecause != null && issues.Count > 0)
            {
                // Played before the previous card finished: its tabs didn't see that card's effects.
                model.Add($"not checked: its tabs were computed before {w.StaleBecause} finished ({string.Join("; ", issues)})");
                issues.Clear();
            }
            if (w.Facts?.SimPending == true)
            {
                // Its simulation was still running when the frame stopped waiting: its simulator tabs may be missing.
                var simOnly = issues.Where(x => SimTabSources.Any(x.Contains)).ToList();
                foreach (var x in simOnly) model.Add($"not checked (simulation pending): {x}");
                issues.RemoveAll(simOnly.Contains);
            }
            _checked++;
            if (issues.Count == 0) _matched++;
            if (w.Facts?.Sim is { LeadIn.Count: > 0 }) _withLeadIn++;
            foreach (var issue in issues)
            {
                var kind = issue.Split(':')[0];
                _issueCounts[kind] = (_issueCounts.TryGetValue(kind, out var n) ? n : 0) + 1;
            }
            var log = ModEntry.Log;
            var tabs = w.Predicted.Count == 0 ? "none" : string.Join(", ", w.Predicted.Select(t => t.Text ?? (t.Amount > 0 ? $"{t.Key} {t.Amount}" : t.Key)));
            if (Sts2State.TurnNumber != _turnPlaysTurn)
            {
                _turnPlays.Clear();
                _turnPlaysTurn = Sts2State.TurnNumber;
            }
            _turnPlays.Add($"{_turnPlays.Count + 1}. {w.CardName}: tabs {tabs} → {(issues.Count == 0 ? "matched" : string.Join("; ", issues))}");
            if (issues.Count > 0)
            {
                log.Warn($"[audit] played {w.CardName}: {string.Join("; ", issues)}. Tabs were: {tabs}.");
                _tabFilter = _ctx.Decor.Filter;
                var lines = WindowLines(w, hist, me);
                foreach (var l in lines) log.Warn($"[audit]   {l}");
                Report(w, issues, tabs, lines, entries.Count, string.Join("\n", _turnPlays));
            }
            foreach (var m in model) log.Info($"[audit] [model] played {w.CardName}: {m}");
        }
        catch (Exception ex)
        {
            ModEntry.Log.Warn($"[audit] couldn't check {w.CardName}: {ex.Message}");
            Sts2Mods.Core.Diagnostics.ErrorReporter.Instance?.Exception(ex, "audit-check");
        }
    }

    /// <summary>
    /// One Sentry report per issue, grouped by what's wrong rather than by amounts: a missing or
    /// silent source groups by that source; block, damage and budget issues group by the card.
    /// </summary>
    private static Func<string, bool>? _tabFilter;

    private static string Hidden(Window w)
    {
        var hidden = _tabFilter == null ? new List<string>() : w.Predicted.Where(t => !_tabFilter(t.Key)).Select(t => t.Key).ToList();
        return hidden.Count == 0 ? "none" : string.Join(", ", hidden);
    }

    private static void Report(Window w, List<string> issues, string tabs, List<string> window, int windowSize, string turnPlays)
    {
        var reporter = Sts2Mods.Core.Diagnostics.ErrorReporter.Instance;
        if (reporter is not { Enabled: true }) return;
        var card = w.Card.GetType().Name;
        foreach (var issue in issues)
        {
            var colon = issue.IndexOf(':');
            var kind = colon > 0 ? issue[..colon] : issue;
            var rest = colon > 0 ? issue[(colon + 1)..].Trim() : "";
            // "unexplained: 2 more draws than the card's own 1" → effect "draws";
            // "unexplained: powers applied (X, Y)" → effect "powers".
            var effect = rest.Contains(" more ")
                ? rest.Split(" more ")[1].Split(" than ")[0]
                : rest.Split(' ')[0];
            string[] fp = kind switch
            {
                "no tab" or "didn't fire" => new[] { kind, rest.Split(' ')[0] },
                "power went down" => new[] { kind, rest.Split(' ')[0] },
                // Grouped by the sources that could have done it (amounts and cards left out).
                _ when kind.StartsWith("untracked") => new[] { kind, rest.Contains("could be: ") ? rest[(rest.IndexOf("could be: ") + 10)..].TrimEnd(')') : card },
                // Which powers, and which way an amount was off: different causes, different issues.
                "unexplained" when effect == "powers" => new[] { kind, card, "powers", rest },
                "unexplained" => new[] { kind, card, effect },
                _ when rest.StartsWith("predicted") => new[] { kind, card, Direction(rest) },
                _ => new[] { kind, card },
            };
            var extra = new Dictionary<string, string> { ["audit_window"] = string.Join("\n", window), ["turn_plays"] = turnPlays };
            if (w.Before != null)
            {
                foreach (var key in new[] { "player", "player_powers", "relics", "orbs", "enemies", "hand" })
                    if (w.Before.TryGetValue(key, out var v)) extra["before_" + key] = v;
            }
            if (kind.StartsWith("after play") && w.Facts?.Sim != null)
            {
                extra["sim_state"] = w.Facts.Sim.Start.ToJson();
                if (w.After != null) extra["sim_state_after"] = w.After.ToJson();
            }
            var lag = w.Lag < 0 ? "never seen in hand" : $"{w.Lag} frame(s)";
            reporter.Mismatch("proc", $"Proc audit: {issue} (played {card})", fp,
                $"Played {w.CardName} ({card}).\nIssue: {issue}\nAll issues this play: {string.Join("; ", issues)}\nTabs before the play: {tabs}" +
                $"\nFrom the card's last frame in hand to its play starting: {lag}" +
                $"\nExpected from the card itself: {(w.Facts != null ? Snapshot.FactsLine(w.Facts) : "no facts")}" +
                $"\nTabs hidden by settings: {Hidden(w)}" +
                $"\nPlay: {windowSize} history entries from its action (span {w.From}..{w.To}), play started at {w.Start}; shuffles {w.Shuffles}",
                extra);
        }
    }

    /// <summary>"predicted 4, actual 0" → "less" (more/less than predicted; amounts left out).</summary>
    private static string Direction(string rest)
    {
        static int Last(string x) => int.TryParse(new string(x.Trim().Reverse().TakeWhile(char.IsDigit).Reverse().ToArray()), out var v) ? v : 0;
        var parts = rest.Split(',');
        if (parts.Length < 2) return "";
        var actual = Last(parts[1]);
        var predicted = parts[0].Replace("predicted", "").Trim();
        var range = predicted.Split('–');
        var lo = Last(range[0]);
        var hi = Last(range[^1]);
        return actual > hi ? "more" : actual < lo ? "less" : "";
    }

    private void Summarize()
    {
        if (_checked == 0) return;
        var issues = _issueCounts.Count == 0 ? "" : $" ({string.Join(", ", _issueCounts.OrderByDescending(k => k.Value).Select(k => $"{k.Key} ×{k.Value}"))})";
        ModEntry.Log.Info($"[audit] combat summary: {_checked} card plays checked ({_withLeadIn} after plays still resolving), {_matched} matched{issues}.");
        _checked = _matched = _withLeadIn = 0;
        _issueCounts.Clear();
    }

    private static List<string> Compare(Window w, Player me, List<CombatHistoryEntry> entries, List<AbstractModel> flashed, out List<string> model)
    {
        var issues = new List<string>();
        var modelLines = new List<string>();
        model = modelLines;
        // Random outcomes (several enemies): only effects the history shows a random hit caused are
        // excused. Random hits = damage to enemies with no card source (procs, orbs) or from this card
        // when it targets a random enemy.
        var random = w.Facts?.RandomOutcome == true;
        var randomHits = !random
            ? new List<DamageReceivedEntry>()
            : entries.OfType<DamageReceivedEntry>()
                .Where(d => d.Result.Receiver.IsEnemy
                            && (d.CardSource == null || (w.Card.TargetType == TargetType.RandomEnemy && ReferenceEquals(d.CardSource, w.Card))))
                .ToList();
        var randomKill = randomHits.Any(d => d.Result.WasTargetKilled);
        bool RandomlyHit(Creature c) => randomHits.Any(d => d.Result.Receiver == c);
        var sourceOf = new Dictionary<string, TabSpec>();
        foreach (var t in w.Predicted)
        {
            var colon = t.Key.IndexOf(':');
            if (colon > 0) sourceOf[t.Key[(colon + 1)..]] = t;
        }

        // ---- sources that fired with no tab (missing rules)
        foreach (var m in flashed.Distinct())
        {
            if (sourceOf.ContainsKey(m.GetType().Name) || Modifiers.Contains(m.GetType().Name)) continue;
            // A random-target hit or kill (several enemies) can set off enemy powers and kill relics
            // we can't predict: noted, not reported.
            var causedByRandom = m switch
            {
                PowerModel { Owner.IsEnemy: true } ep => RandomlyHit(ep.Owner) || randomKill,
                _ => KillReactive.Contains(m.GetType().Name) && randomKill,
            };
            if (causedByRandom)
            {
                modelLines.Add($"{m.GetType().Name} fired after a random hit or kill (not predictable)");
                continue;
            }
            if (DrawReactive.Contains(m.GetType().Name) && entries.OfType<CardDrawnEntry>().Any(d => !d.FromHandDraw && d.Card.Owner == me))
            {
                modelLines.Add($"{m.GetType().Name} fired on a drawn card (the draw order is hidden: not predictable)");
                sourceOf[m.GetType().Name] = new TabSpec("proc:" + m.GetType().Name, null);
                continue;
            }
            issues.Add($"no tab: {m.GetType().Name} fired");
        }

        // ---- relic tabs whose relic didn't fire (crossed ones mean "this breaks it": no flash)
        foreach (var (name, t) in sourceOf)
        {
            if (t.Crossed || !t.Key.StartsWith("proc:")) continue;
            if (me.Relics.Any(r => r.GetType().Name == name) && !flashed.Any(f => f.GetType().Name == name))
                issues.Add($"didn't fire: {name}");
        }

        // ---- block from procs (exact)
        var procBlock = entries.OfType<BlockGainedEntry>()
            .Where(b => b.Actor == me.Creature && b.CardPlay == null)
            .Sum(b => b.Amount);
        var predictedBlock = w.Predicted.Where(t => t.Kind == "block").Sum(t => t.Amount) + (w.Facts?.OrbBlock ?? 0);
        // The play ended the fight: the history stops at the killing hit, so less than predicted is expected.
        void Shortfall(string what, string predicted, int actual) =>
            modelLines.Add($"{what}: predicted {predicted}, actual {actual} (the fight ended: the game logs nothing from the killing hit on)");
        if (procBlock < predictedBlock && w.FightEnded) Shortfall("block", predictedBlock.ToString(), procBlock);
        else if (procBlock != predictedBlock) issues.Add($"block: predicted {predictedBlock}, actual {procBlock}");

        // ---- damage from procs (no card source), against each tab's range
        var procDamage = entries.OfType<DamageReceivedEntry>().Where(d => d.CardSource == null).ToList();
        int Dealt(DamageReceivedEntry d) => d.Result.TotalDamage + d.Result.OverkillDamage;
        var toEnemies = procDamage.Where(d => d.Result.Receiver.IsEnemy).Sum(Dealt);
        var toMe = procDamage.Where(d => d.Result.Receiver == me.Creature).Sum(Dealt);
        var enemyTabs = w.Predicted.Where(t => t.Kind == "damage" && !t.Crossed).ToList();
        var meTabs = w.Predicted.Where(t => t.Kind == "damage" && t.Crossed).ToList();
        var (eMin, eMax) = (enemyTabs.Sum(t => t.AuditMin), enemyTabs.Sum(t => t.AuditMax));
        // Orbs the card itself evokes (Dualcast, channelling into full slots) deal unsourced damage too.
        eMin += w.Facts?.OrbDamageMin ?? 0;
        eMax += w.Facts?.OrbDamageMax ?? 0;
        var (mMin, mMax) = (meTabs.Sum(t => t.AuditMin), meTabs.Sum(t => t.AuditMax));
        if (toEnemies < eMin && w.FightEnded) Shortfall("damage to enemies", Range(eMin, eMax), toEnemies);
        else if (toEnemies < eMin || toEnemies > eMax) issues.Add($"damage to enemies: predicted {Range(eMin, eMax)}, actual {toEnemies}");
        if (toMe < mMin && w.FightEnded) Shortfall("damage to you", Range(mMin, mMax), toMe);
        else if (toMe < mMin || toMe > mMax) issues.Add($"damage to you: predicted {Range(mMin, mMax)}, actual {toMe}");

        // ---- budget: everything else the play caused
        var autoPlayed = UnpredictedAutoPlay(w, entries, me);
        if (autoPlayed) modelLines.Add("auto-played cards (random, so not predicted): budget not checked");
        var f = autoPlayed ? null : w.Facts;
        bool Explained(Effect e) => sourceOf.Keys.Any(s => SourceEffects.TryGetValue(s, out var fx) && fx.Contains(e));
        void Budget(Effect e, string what, int actual, int own)
        {
            if (actual > own && !Explained(e) && randomKill) modelLines.Add($"{actual - own} more {what} after a random kill (not predictable)");
            else if (actual > own && !Explained(e)) issues.Add($"unexplained: {actual - own} more {what} than the card's own {own}");
            else if (actual < own) modelLines.Add($"expected {own} {what}, saw {actual}");
        }
        var draws = entries.OfType<CardDrawnEntry>().Count(d => !d.FromHandDraw && d.Card.Owner == me);
        var exhausts = entries.OfType<CardExhaustedEntry>().Count(d => d.Card.Owner == me);
        var discards = entries.OfType<CardDiscardedEntry>().Count(d => d.Card.Owner == me);
        var generated = entries.OfType<CardGeneratedEntry>().Count(d => d.Creator == me || d.Card.Owner == me);
        var orbs = entries.OfType<OrbChanneledEntry>().Count();
        var summons = entries.OfType<SummonedEntry>().Count();
        if (f != null)
        {
            // Draws and channels aren't a choice: fewer than predicted is a model gap (FTL's play limit),
            // unless the fight ended (nothing more runs), fewer cards were exhausted than the upper bound
            // (Dark Embrace and Joss Paper draw per exhaust), or you have no orb slots left (the channel fails).
            if (draws < f.Draws && !w.FightEnded && exhausts >= f.TotalExhausts) issues.Add($"draws: predicted {f.Draws}, actual {draws}");
            if (orbs < f.Channels && !w.FightEnded && w.After?.P.OrbCapacity != 0) issues.Add($"orbs channeled: predicted {f.Channels}, actual {orbs}");
            Budget(Effect.Draw, "draws", draws, f.Draws);
            Budget(Effect.Exhaust, "exhausts", exhausts, f.TotalExhausts);
            Budget(Effect.Discard, "discards", discards, f.Discards);
            // Transformed cards are logged as created too (CardCmd.Transform), up to what the card can transform.
            Budget(Effect.Generate, "created cards", generated, f.Generated + f.Transforms);
            Budget(Effect.Orb, "orbs channeled", orbs, f.Channels);
            Budget(Effect.Summon, "summons", summons, w.Card.DynamicVars.ContainsKey("Summon") ? f.Plays : 0);
        }

        // Powers applied: types the card doesn't carry itself (its PowerVar<T> values), with no tab
        // whose source applies powers. Strength/Dexterity changes from Vigor-style consumption and
        // temporary powers expiring are excluded.
        var own = OwnPowerTypes(w.Card);
        var foreign = entries.OfType<PowerReceivedEntry>()
            .Where(p => p.Amount > 0 && !own.Contains(p.Power.GetType()) && p.Power is not ITemporaryPower)
            .Where(p => !(p.Actor.IsEnemy && p.Applier is { IsEnemy: true })) // enemies acting on each other (Heist)
            .Select(p => p.Power.GetType().Name)
            .Distinct()
            .ToList();
        if (foreign.Count > 0 && randomKill) modelLines.Add($"powers applied after a random kill: {string.Join(", ", foreign)}");
        else if (foreign.Count > 0 && !autoPlayed && !Explained(Effect.Power)) issues.Add($"unexplained: powers applied ({string.Join(", ", foreign)})");

        return issues;
    }

    // ------------------------------------------------------------------ every power

    /// <summary>Powers the simulator tracks itself: the after-play check compares them.</summary>
    private static readonly HashSet<string> SimComparedPowers = new()
    {
        nameof(PoisonPower), nameof(PlatingPower), nameof(JuggernautPower), nameof(ThornsPower), nameof(FlameBarrierPower), nameof(FeelNoPainPower),
        nameof(FlutterPower),
    };

    /// <summary>
    /// Every power on every creature, before the play against after it: a power that went down or
    /// away with nothing in the play to explain it. Explained: powers the card applies, powers that
    /// flashed (they acted: Slippery, Curl Up...), sources with a tab, Artifact when the card
    /// applies debuffs, temporary powers, the simulator's own powers (compared already), and anything on
    /// a creature that died. Increases are the budget check's ("unexplained: powers applied").
    /// </summary>
    private static void PowerChangeIssues(Window w, Player me, List<CombatHistoryEntry> entries, List<AbstractModel> flashed, List<string> issues, List<string> model)
    {
        if (w.PowersBefore == null || w.PowersAfter == null) return;
        // The card's own powers, and those of cards it auto-played.
        var own = OwnPowerTypes(w.Card).Select(t => t.Name).ToHashSet();
        foreach (var e in entries.OfType<CardPlayStartedEntry>().Where(e => e.CardPlay.IsAutoPlay))
            own.UnionWith(OwnPowerTypes(e.CardPlay.Card).Select(t => t.Name));
        var acted = flashed.Select(m => m.GetType().Name).ToHashSet();
        var tabbed = w.Predicted.Select(t => t.Key.Contains(':') ? t.Key[(t.Key.IndexOf(':') + 1)..] : t.Key).ToHashSet();
        // Artifact blocks any debuff, temporary ones included (Dark Shackles).
        var debuffs = w.Facts?.EnemyDebuffs.Count > 0 || own.Any(t => PowerTypeNamed(t) is { } pt && PlayFacts.IsDebuffType(pt));
        var died = DiedInPlay(entries);
        foreach (var ((creature, type), before) in w.PowersBefore)
        {
            var after = w.PowersAfter.TryGetValue((creature, type), out var a) ? a : 0;
            if (after >= before) continue;
            if (!creature.IsAlive) continue;
            if (own.Contains(type) || acted.Contains(type) || tabbed.Contains(type) || SimComparedPowers.Contains(type)) continue;
            if (type == nameof(ArtifactPower) && debuffs) continue;
            if (creature.Powers.FirstOrDefault(p => p.GetType().Name == type) is ITemporaryPower) continue;
            // A death in the play: powers that react to it (Dampen comes off you when its caster dies).
            if (died && PowerTypeNamed(type) is { } pt && CodeIndex.Does(pt, Array.Empty<string>(), DeathHooks)) continue;
            var who = creature == me.Creature ? "you" : creature.Monster?.GetType().Name ?? "creature";
            issues.Add($"power went down: {type} on {who} {before}→{after}, with nothing in the play to explain it");
        }
    }

    /// <summary>Auto-plays other than a hand choice's pick (random ones: their effects aren't predicted).</summary>
    private static bool UnpredictedAutoPlay(Window w, List<CombatHistoryEntry> entries, Player me)
        => entries.OfType<CardPlayStartedEntry>().Any(e => e.CardPlay.IsAutoPlay && e.CardPlay.Card.Owner == me && !ReferenceEquals(e.CardPlay.Card, w.Chosen));

    private static readonly string[] DeathHooks = { "AfterDeath", "BeforeDeath" };

    private static bool DiedInPlay(List<CombatHistoryEntry> entries) => entries.OfType<DamageReceivedEntry>().Any(d => d.Result.WasTargetKilled);

    /// <summary>The game's power class with this name (powers are recorded by name before the play).</summary>
    private static Type? PowerTypeNamed(string name) => typeof(ArtifactPower).Assembly.GetType(typeof(ArtifactPower).Namespace + "." + name);

    // ------------------------------------------------------------------ everything else

    /// <summary>
    /// Energy, stars, gold, HP, max HP, your cards and relics before and after the play (PlaySnapshot):
    /// a change no code that ran explains is reported with the sources present that could have made it.
    /// </summary>
    private static void EverythingIssues(Window w, Player me, List<CombatHistoryEntry> entries, List<AbstractModel> flashed, List<string> issues, List<string> model)
    {
        if (w.FullBefore == null || w.FullAfter == null) return;
        var cs = Sts2State.Combat;
        if (cs == null) return;
        var present = new List<AbstractModel>();
        present.AddRange(me.Relics);
        foreach (var c in cs.Creatures)
        {
            present.AddRange(c.Powers);
            if (c.Monster != null) present.Add(c.Monster);
        }
        foreach (var card in w.FullBefore.Cards.Keys)
        {
            present.Add(card);
            if (card.Enchantment != null) present.Add(card.Enchantment);
            if (card.Affliction != null) present.Add(card.Affliction);
        }
        var tabbed = w.Predicted.Select(t => t.Key.Contains(':') ? t.Key[(t.Key.IndexOf(':') + 1)..] : t.Key).ToHashSet();
        // Code that ran: the card, sources with a tab, sources that flashed, cards it auto-played, cards
        // it drew (and their enchantments), and sources that react to draws when it drew.
        var autoPlayed = entries.OfType<CardPlayStartedEntry>().Where(e => e.CardPlay.IsAutoPlay).Select(e => e.CardPlay.Card).ToList();
        var drawn = entries.OfType<CardDrawnEntry>().Where(d => !d.FromHandDraw && d.Card.Owner == me).Select(d => d.Card).ToList();
        var drawReactors = drawn.Count == 0 ? Enumerable.Empty<AbstractModel>()
            : present.Where(m => CodeIndex.Does(m.GetType(), Array.Empty<string>(), new[] { "AfterCardDrawn", "AfterCardDrawnEarly" }));
        // A death in the play runs death hooks, including those of powers gone by its end (Dampen comes
        // off you when its caster dies and upgrades your cards back).
        var deathReactors = !DiedInPlay(entries) ? Enumerable.Empty<Type>()
            : present.Select(m => m.GetType())
                .Concat(w.PowersBefore?.Keys.Select(k => PowerTypeNamed(k.Item2)).OfType<Type>() ?? Enumerable.Empty<Type>())
                .Where(t => CodeIndex.Does(t, Array.Empty<string>(), DeathHooks));
        var explainers = present.Where(m => tabbed.Contains(m.GetType().Name)).Select(m => m.GetType())
            .Concat(flashed.Select(m => m.GetType())).Append(w.Card.GetType())
            .Concat(autoPlayed.Select(c => c.GetType()))
            .Concat(drawn.Select(c => c.GetType())).Concat(drawn.Where(c => c.Enchantment != null).Select(c => c.Enchantment!.GetType()))
            .Concat(drawReactors.Select(m => m.GetType()))
            .Concat(deathReactors)
            .Distinct().ToList();
        var tracked = new HashSet<string>(tabbed);
        foreach (var s in RelicSpecs.All) tracked.Add(s.TypeName);
        issues.AddRange(PlaySnapshot.Unexplained(w.FullBefore, w.FullAfter, w.Card, entries, w.Shuffles, explainers, present, me, tracked));
    }

    // ------------------------------------------------------------------ shuffles

    /// <summary>Shuffles during the play (CardPileCmd.Shuffle prefix) against PlayFacts.Shuffles (the Shuffle Warning and shuffle procs).</summary>
    private static void ShuffleIssues(Window w, Player me, List<CombatHistoryEntry> entries, List<AbstractModel> flashed, List<string> issues, List<string> model)
    {
        var f = w.Facts;
        if (f == null) return;
        var shuffled = w.Shuffles > 0;
        if (shuffled == f.Shuffles) return;
        var autoPlayed = UnpredictedAutoPlay(w, entries, me);
        var drawReactive = flashed.Any(m => DrawReactive.Contains(m.GetType().Name) || m.GetType().Name is nameof(GremlinHorn) or nameof(UnceasingTop));
        var draws = entries.OfType<CardDrawnEntry>().Count(d => !d.FromHandDraw && d.Card.Owner == me);
        var why = $"(draws: predicted {f.Draws} = own {f.OwnDraws} + set off {f.ChainedDraws}{(f.DrawsBlocked ? ", blocked" : "")}, actual {draws})";
        if (autoPlayed || (shuffled && drawReactive))
        {
            model.Add($"shuffle {(shuffled ? "happened" : "didn't happen")} after an unpredictable draw or auto-play {why}");
            return;
        }
        issues.Add(shuffled
            ? $"shuffle: not predicted, but the discard pile was shuffled {why}"
            : $"shuffle: predicted, but no shuffle happened {why}");
    }

    // ------------------------------------------------------------------ state after the play

    /// <summary>
    /// The state the play left against the simulator's prediction of it (see the class summary).
    /// Each difference from the closest branch is one issue.
    /// </summary>
    private static void AfterPlayIssues(Window w, List<CombatHistoryEntry> entries, List<string> issues, List<string> model)
    {
        var sim = w.Facts?.Sim;
        if (sim == null)
        {
            model.Add("state after the play: not checked (no simulation for this card)");
            return;
        }
        if (w.After == null || w.AfterIds == null)
        {
            model.Add($"state after the play: not checked ({w.AfterSkipped ?? "not captured"})");
            return;
        }
        if (entries.OfType<CardPlayStartedEntry>().Any(e => e.CardPlay.IsAutoPlay && !ReferenceEquals(e.CardPlay.Card, w.Chosen)))
        {
            model.Add("state after the play: not checked (auto-played cards)");
            return;
        }
        if (w.Chosen != null && w.Chosen.TargetType == TargetType.AnyEnemy && w.Target == null)
        {
            model.Add("state after the play: not checked (the pick's auto-plays hit different random enemies)");
            return;
        }
        var targetId = w.Target != null && sim.Ids.TryGetValue(w.Target, out var tid) ? tid : -1;
        if (!sim.PlayByTarget.TryGetValue(targetId, out var play) && !sim.PlayByTarget.TryGetValue(-1, out play))
        {
            model.Add("state after the play: not checked (no simulated play for its target)");
            return;
        }
        SimEngine.ApplyPlayTo(sim.Start, sim.LeadIn, play, out var branches, out var truncated);
        if (truncated || branches.Count == 0)
        {
            model.Add("state after the play: not checked (too many branches)");
            return;
        }
        var after = w.After;
        var afterIds = w.AfterIds;
        // Summoning raises Osty's HP in ways the simulator doesn't model: Osty isn't compared then.
        var summoned = entries.OfType<SummonedEntry>().Any();

        List<string> Diff(SimState pred)
        {
            var d = new List<string>();
            foreach (var (creature, id) in sim.Ids)
            {
                var pe = pred.Enemy(id);
                if (pe == null || !afterIds.TryGetValue(creature, out var aid) || after.Enemy(aid) is not { } ae) continue;
                var name = pe.Name;
                static int Hp(SimEnemy e) => Math.Max(0, e.Hp);
                if (Hp(pe) != Hp(ae)) d.Add($"after play enemy HP: {name} predicted {Hp(pe)}, actual {Hp(ae)}");
                if (ae.Hp <= 0 || pe.Hp <= 0) continue;
                if (pe.Block != ae.Block) d.Add($"after play enemy block: {name} predicted {pe.Block}, actual {ae.Block}");
                if (pe.Poison != ae.Poison) d.Add($"after play enemy poison: {name} predicted {pe.Poison}, actual {ae.Poison}");
                // FlutterPower.AfterDamageReceived: one stack per powered hit that gets through (no flash until it stuns).
                if (pe.Flutter != ae.Flutter) d.Add($"after play enemy Flutter: {name} predicted {pe.Flutter}, actual {ae.Flutter}");
                if (pe.Stunned != ae.Stunned) d.Add($"after play enemy stunned: {name} predicted {(pe.Stunned ? "stunned" : "not stunned")}, actual {(ae.Stunned ? "stunned" : "not stunned")}");
                // A stunned enemy's intent becomes the stun move; the simulator keeps its hits and skips them.
                if (!pe.Stunned && !ae.Stunned && !pe.Hits.SequenceEqual(ae.Hits)) d.Add($"after play enemy intent: {name} predicted [{string.Join(", ", pe.Hits)}], actual [{string.Join(", ", ae.Hits)}]");
            }
            var pp = pred.P;
            var ap = after.P;
            void Cmp(string what, int p, int a)
            {
                if (p != a) d.Add($"after play {what}: predicted {p}, actual {a}");
            }
            Cmp("your block", pp.Block, ap.Block);
            Cmp("Plating", pp.Plating, ap.Plating);
            Cmp(nameof(Juggernaut), pp.Juggernaut, ap.Juggernaut);
            Cmp("Thorns", pp.Thorns, ap.Thorns);
            Cmp("Flame Barrier", pp.FlameBarrier, ap.FlameBarrier);
            Cmp("Feel No Pain", pp.FeelNoPain, ap.FeelNoPain);
            if (!summoned) Cmp("Osty HP", pp.OstyHp, ap.OstyHp);
            // A random orb (Chaos) is predicted as Unknown: any orb matches it.
            static string Orb(SimOrb o) => $"{o.Kind} {o.Passive}/{o.Evoke}";
            var orbsMatch = pp.Orbs.Count == ap.Orbs.Count
                            && pp.Orbs.Zip(ap.Orbs).All(x => x.First.Kind == OrbKind.Unknown || Orb(x.First) == Orb(x.Second));
            if (!orbsMatch) d.Add($"after play orbs: predicted [{string.Join(", ", pp.Orbs.Select(Orb))}], actual [{string.Join(", ", ap.Orbs.Select(Orb))}]");
            return d;
        }

        var best = branches.Select(Diff).OrderBy(d => d.Count).First();
        if (best.Count == 0) return;
        var note = branches.Count > 1 ? $" (closest of {branches.Count} branches)" : "";
        foreach (var x in best) issues.Add(x + note);
    }

    private static string Range(int min, int max) => min == max ? $"{min}" : $"{min}–{max}";

    private static readonly Dictionary<Type, HashSet<Type>> AppliedInCode = new();

    /// <summary>
    /// Power types a card class applies in its own code: the T of every PowerCmd.Apply&lt;T&gt; call
    /// in its methods, including async ones (their state machines' MoveNext). Read once per class
    /// from the compiled code, so new and changed cards are covered without a table.
    /// </summary>
    private static HashSet<Type> PowersAppliedInCode(Type cardType)
    {
        if (AppliedInCode.TryGetValue(cardType, out var cached)) return cached;
        var set = new HashSet<Type>();
        const BindingFlags all = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
        var methods = cardType.GetMethods(all).Cast<MethodBase>()
            .Concat(cardType.GetNestedTypes(BindingFlags.NonPublic | BindingFlags.Public)
                .Where(t => typeof(IAsyncStateMachine).IsAssignableFrom(t))
                .Select(t => t.GetMethod("MoveNext", all))
                .Where(m => m != null)
                .Cast<MethodBase>());
        foreach (var m in methods)
        {
            try
            {
                foreach (var ins in PatchProcessor.GetOriginalInstructions(m))
                {
                    if (ins.operand is MethodInfo { IsGenericMethod: true, Name: "Apply" } mi && mi.DeclaringType?.Name == "PowerCmd")
                        set.Add(mi.GetGenericArguments()[0]);
                }
            }
            catch
            {
                // a method we can't read: its powers stay "foreign"
            }
        }
        AppliedInCode[cardType] = set;
        return set;
    }

    private static HashSet<Type> OwnPowerTypes(CardModel card)
    {
        var set = new HashSet<Type>(PowersAppliedInCode(card.GetType()));
        foreach (var v in card.DynamicVars.Values)
        {
            var t = v.GetType();
            if (t.IsGenericType && t.GetGenericTypeDefinition() == typeof(MegaCrit.Sts2.Core.Localization.DynamicVars.PowerVar<>))
                set.Add(t.GetGenericArguments()[0]);
        }
        // A temporary stat power applies the stat itself (TemporaryStrengthPower.AfterApplied →
        // PowerCmd.Apply<StrengthPower>): Dark Shackles' -9 Strength.
        foreach (var t in set.ToList())
        {
            if (typeof(TemporaryStrengthPower).IsAssignableFrom(t)) set.Add(typeof(StrengthPower));
            if (typeof(TemporaryDexterityPower).IsAssignableFrom(t)) set.Add(typeof(DexterityPower));
            if (typeof(TemporaryFocusPower).IsAssignableFrom(t)) set.Add(typeof(FocusPower));
        }
        return set;
    }

    private static string SafePotion(PotionUsedEntry e)
    {
        try { return e.Potion.GetType().Name; } catch { return "a potion"; }
    }

    private static string SafeTitle(CardModel c)
    {
        try { return c.Title; } catch { return c.GetType().Name; }
    }
}
