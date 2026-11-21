using MegaCrit.Sts2.Core.Entities.Cards;
using System.Reflection;
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
///  - Relic tab that didn't fire: a predicted, uncrossed relic tab whose relic never flashed; any tabbed source (powers, crossed
///    tabs) every outcome of the play's aim sets off, whose code flashes, that didn't (decision #95).
///  - Lethal: each enemy's death chance over the card's outcomes against the end of turn from the board the play left.
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
        /// <summary>The played card's upgrade level as the play began; -1 when not captured.</summary>
        public int UpgradeBefore = -1;
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
        /// <summary>The proc tabs the card showed in hand that the prediction as the play began disagrees with (decision #103).</summary>
        public List<string>? ShownDiff;
        /// <summary>Why the tabs shown weren't held to that prediction; null when they were.</summary>
        public string? ShownSkip;
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
        /// <summary>See PlayRecord.RanDuringPause.</summary>
        public string? RanDuringPause;
        public Dictionary<(Creature, string), int>? PowersBefore, PowersAfter;
        /// <summary>See PlayRecord.TargetDraws.</summary>
        public int? TargetDraws;
        public PlaySnapshot? FullBefore, FullAfter;
        /// <summary>Waterfall Giants already about to blow when the play began; null when the play's start wasn't seen.</summary>
        public HashSet<Creature>? BlowingBefore;
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

    /// <summary>An issue about the tabs the card's simulations gave (procs that fired or didn't, their block and damage).</summary>
    internal static bool SimDependent(string issue) =>
        issue.StartsWith("no tab:", StringComparison.Ordinal) || issue.StartsWith("didn't fire:", StringComparison.Ordinal)
        || issue.StartsWith("block:", StringComparison.Ordinal) || issue.StartsWith("damage to enemies:", StringComparison.Ordinal)
        || issue.StartsWith("damage to you:", StringComparison.Ordinal) || issue.StartsWith("lethal:", StringComparison.Ordinal)
        || SimTabSources.Any(issue.Contains);

    /// <summary>The aim the play took (its simulation key), when its simulations were complete and fully modelled; null otherwise.</summary>
    private static int? CheckedAim(Window window)
    {
        if (window.Facts is not { Sim: { } sim } facts || facts.SimIncomplete || facts.SimPending || facts.Unmodelled.Count > 0) return null;
        var key = sim.KeyFor(window.Target);
        return sim.PlayByTarget.ContainsKey(key) ? key : sim.PlayByTarget.ContainsKey(Who.None) ? Who.None : null;
    }

    /// <summary>
    /// Whether some outcome of the play set this source off (a roll decided it): true or false when the simulator models
    /// it with a tab and the play's simulations were complete; null when that can't be told (then the old excuses stand).
    /// </summary>
    private static bool? SimHadIt(Window window, AbstractModel model)
    {
        if (CheckedAim(window) is not { } aim || window.Facts?.FiresAnyByAim?.TryGetValue(aim, out var any) != true) return null;
        if (SimModels.Get(SimReaders.ModelName(model))?.Tab == null) return null;
        return any!.Contains(model.GetType());
    }

    /// <summary>Sources whose tabs come from the outcome simulator (missing while its run was pending).</summary>
    private static readonly HashSet<string> SimTabSources =
        SimModels.Names.Where(name => SimModels.Get(name) is { } model && (model.Tab != null || model.JudgedOnOutcome)).ToHashSet();

    /// <summary>
    /// Relic tabs about the end of your turn, not the play: they never fire during it (Parrying Shield doesn't
    /// even flash). The forecast check covers what they do (enemy HP at the end of your turn).
    /// </summary>
    private static readonly HashSet<string> EndOfTurnRelicTabs = new() { nameof(ParryingShield) };

    /// <summary>Your relics and powers that react to an enemy dying or being hit.</summary>
    private static readonly HashSet<string> KillReactive = new() { nameof(GremlinHorn), nameof(CompostPower), nameof(MightyWings) };

    /// <summary>Your relics and powers that react to HP you lose (enemy Thorns hitting you back, from a random hit too).</summary>
    private static readonly HashSet<string> HpLossReactive = new()
    {
        nameof(TungstenRod), nameof(SelfFormingClay), nameof(EmotionChip), nameof(CentennialPuzzle), nameof(DemonTongue), nameof(RedSkull),
        nameof(InfernoPower), nameof(RupturePower),
    };

    /// <summary>
    /// Your powers that act only on teammates when you play (Covering Fire: their block on your shots): simulated, with no
    /// tab by design (a teammate-only effect: what they get shows on their Incoming Damage), so their flash isn't a missing rule.
    /// </summary>
    private static readonly HashSet<string> TeammateOnly = new() { nameof(CoveringFirePower) };

    /// <summary>
    /// Your powers whose flash only changes a card for a later play, so nothing happens in this one (no tab by design: the coverage
    /// ledger's "ignore"). Show Off: an enemy's death raises the Show Off card's Aim and the power goes (ShowOffPower.AfterDeath →
    /// ShowOff.IncreaseAim).
    /// </summary>
    private static readonly HashSet<string> LaterPlayOnly = new() { nameof(ShowOffPower) };

    /// <summary>
    /// Relics that act without flashing, so "didn't fire" can't be checked by a flash (their effect still is: the
    /// card cost check). Mummified Hand makes a card in your hand free (MummifiedHand.AfterCardPlayed: no Flash call).
    /// </summary>
    private static readonly HashSet<string> SilentRelics = new() { nameof(MummifiedHand) };

    /// <summary>
    /// Your powers a card play uses up whatever the card costs (BeforeCardPlayed): Free Card, Free Attack, Free Skill and Free Power make
    /// your next card (Attack, Skill, Power) from hand free and go, and the costs they lowered go back up. The free-card check
    /// compares their charges with the "free:" tabs.
    /// </summary>
    private static readonly HashSet<string> SpentByAnyPlay = new(SpireQoL.Procs.FreeCards.Powers);

    /// <summary>
    /// Your powers your next powered attack uses up (BeforeAttack marks the command, AfterAttack spends it): Vigor,
    /// Gigantification.
    /// </summary>
    private static readonly HashSet<string> SpentByAttack = new() { nameof(VigorPower), nameof(GigantificationPower), nameof(OneTwoPunchPower) };

    /// <summary>Your stats a source that changes powers can take away (Tender: 1 Strength and Dexterity per card).</summary>
    private static readonly HashSet<string> Stats = new() { nameof(StrengthPower), nameof(DexterityPower), nameof(FocusPower) };

    /// <summary>What a trigger tab's source can add beyond the card itself (for the budget check).</summary>
    private static readonly Dictionary<string, Effect[]> SourceEffects = new()
    {
        // draws (Dark Embrace, Game Piece, Iron Club, Joss Paper, Vicious and Centennial Puzzle are
        // counted in PlayFacts.Draws, so their tabs explain nothing beyond it)
        [nameof(GremlinHorn)] = new[] { Effect.Draw }, [nameof(UnceasingTop)] = new[] { Effect.Draw }, [nameof(CorrosiveWavePower)] = new[] { Effect.Power },
        [nameof(PagestormPower)] = new[] { Effect.Draw }, [nameof(IterationPower)] = new[] { Effect.Draw },
        ["MotifPower"] = new[] { Effect.Draw }, ["ConductorsBaton"] = new[] { Effect.Power },
        // plays cards from the top of your draw pile: whatever those do
        ["LostLutePower"] = new[] { Effect.Draw, Effect.Exhaust, Effect.Discard, Effect.Generate, Effect.Orb, Effect.Summon, Effect.Power },
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
        [nameof(HandDrill)] = new[] { Effect.Power }, [nameof(Fertilizer)] = new[] { Effect.Power }, [nameof(WhittlingKnife)] = new[] { Effect.Power }, [nameof(MightyWings)] = new[] { Effect.Power }, [nameof(UnsettlingLamp)] = new[] { Effect.Power }, [nameof(SelfFormingClay)] = new[] { Effect.Power },
        [nameof(RedSkull)] = new[] { Effect.Power }, [nameof(CrabRagePower)] = new[] { Effect.Power }, [nameof(RavenousPower)] = new[] { Effect.Power },
        [nameof(SlowPower)] = new[] { Effect.Power }, [nameof(SmoggyPower)] = new[] { Effect.Power }, [nameof(VitalSparkPower)] = new[] { Effect.Power },
        [nameof(SleightOfFleshPower)] = new[] { Effect.Power }, [nameof(MasterPlannerPower)] = new[] { Effect.Power }, [nameof(PaleBlueDotPower)] = new[] { Effect.Power },
    };

    private CombatHistory? _history;
    private readonly HashSet<AbstractModel> _subscribed = new(ReferenceEqualityComparer.Instance);

    // Per-combat tallies for the summary line.
    private int _checked;
    private int _fromHand; // plays checked against the tabs last shown in hand (the prediction couldn't be rebuilt)
    private int _interleaved; // plays whose snapshot checks were logged only: another action ran during their choice
    private int _matched;
    private readonly Dictionary<string, int> _issueCounts = new();
    /// <summary>Decision #92: checks skipped, by reason (rule 1), and what the played card's simulations didn't model (rule 2).</summary>
    private readonly Dictionary<string, int> _skipCounts = new(), _gapCounts = new();

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
        /// <summary>Waiting for your choice (Burning Pact's pick): other players' actions can run meanwhile.</summary>
        public bool Paused;
        /// <summary>
        /// Another action ran while this play waited for your choice (ActionQueueSet.GetReadyAction skips a paused
        /// action, so a teammate's queued play runs): its effects are inside this play's before/after snapshots.
        /// </summary>
        public string? RanDuringPause;
        /// <summary>What a target-dependent draw card's plays draw at this target, from the board as it started (PlayFacts.TargetDraws).</summary>
        public int? TargetDraws;
        /// <summary>Every power on every creature when the play started and when it finished.</summary>
        public Dictionary<(Creature, string), int>? PowersBefore, PowersAfter;
        /// <summary>Energy, stars, gold, HP, cards and relics when the play started and finished.</summary>
        public PlaySnapshot? FullBefore, FullAfter;
        /// <summary>Waterfall Giants already about to blow when the play began (see <see cref="Blowing"/>).</summary>
        public HashSet<Creature>? BlowingBefore;
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

        Subscribe(me, combat);
        _countAtFrame[_ctx.Decor.Frame] = history.Entries.Count();
        if (_countAtFrame.Count > 600) _countAtFrame.Remove(_countAtFrame.Keys.Min());

        while (_finished.Count > 0) Check(_finished.Dequeue(), me);
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
                    // A card whose draws depend on its target (Ki Hop, Wrest): what its plays draw at this target, from the
                    // board as it starts (its own hits and Wrest's pull change the target's Impaled during the play).
                    if (play.Target != null)
                    {
                        try { rec.TargetDraws = PlayFacts.TargetDraws(card, play.Target, rec.Facts?.Plays ?? CardPlays.Count(card)); }
                        catch (Exception ex) { Sts2Mods.Core.Diagnostics.ErrorReporter.Instance?.Exception(ex, "audit-target-draws"); }
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
        if (_running.TryGetValue(action, out var paused)) paused.Paused = true;
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

    // Why the state after a play wasn't captured: the play ended the fight, or your turn (Void Form: PlayerCmd.EndTurn lets the end
    // of the turn run, Chains of Binding clearing Bound, before its state is read). Expected skips (CheckSkip.Expected: decision #108).
    private const string FightEndedSkip = CheckSkip.FightEnded;
    private const string TurnEndedSkip = CheckSkip.TurnEnded;

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
            var combat = Sts2State.Combat;
            if (CombatManager.Instance.IsOverOrEnding)
            {
                // The fight is ending: its cleanup (temporary powers expiring, Focus back) changes the
                // state, so the state the play left can't be compared. The history checks still run.
                rec.AfterSkipped = FightEndedSkip;
                rec.FightEnded = true;
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

    private void Subscribe(Player me, ICombatState combat)
    {
        // Every player's relics: a teammate's relic your card sets off has a tab too (decision #75).
        foreach (var relic in combat.Players.SelectMany(player => player.Relics))
        {
            if (_subscribed.Add(relic)) relic.Flashed += OnRelicFlashed;
        }
        foreach (var creature in combat.Creatures)
        {
            foreach (var power in creature.Powers)
            {
                if (_subscribed.Add(power)) power.Flashed += OnPowerFlashed;
            }
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
            PowersBefore = rec.PowersBefore, PowersAfter = rec.PowersAfter, TargetDraws = captured ? rec.TargetDraws : null,
            FullBefore = rec.FullBefore, FullAfter = rec.FullAfter, BlowingBefore = rec.BlowingBefore,
            StaleBecause = captured ? rec.StaleBecause : StaleBecause(hist, card, start, me),
            ShownDiff = captured ? rec.ShownDiff : null, ShownSkip = captured ? rec.ShownSkip : "the play's start wasn't seen",
        };
        Report(window, me, hist);
    }

    private void Report(Window window, Player me, List<CombatHistoryEntry> hist)
    {
        try
        {
            var entries = window.Indices.Select(index => hist[index]).ToList();
            var flashed = window.Flashes.Select(flash => flash.Model).ToList();
            var issues = Compare(window, me, entries, flashed, out var model);
            ShuffleIssues(window, me, entries, flashed, issues, model);
            DrawnIssues(window, me, entries, issues, model);
            // The before/after snapshot checks: they see everything between the play's start and its end.
            var snapshot = new List<string>();
            AfterPlayIssues(window, entries, snapshot, model);
            LethalIssues(window, entries, snapshot, model);
            PowerChangeIssues(window, me, entries, flashed, snapshot, model);
            EverythingIssues(window, me, entries, flashed, snapshot, model);
            if (window.RanDuringPause != null)
            {
                // Another action ran inside this play (during your choice): its effects are in the snapshots too.
                // The history checks above still ran; these are logged, and counted in the combat summary.
                _interleaved++;
                foreach (var issue in snapshot) model.Skip("", "another action ran during your choice", $"{window.RanDuringPause}: {issue}");
            }
            else issues.AddRange(snapshot);
            if (issues.Count > 0 && window.Target is { IsPlayer: true } ally && ally != me.Creature
                && CardCoverage.TeammateTargetUnmodelled(window.Chosen ?? window.Card))
            {
                // Aimed at a teammate (Ignition's Plasma goes to their orbs): the simulator applies the card to you.
                model.Skip("", "aimed at a teammate; the simulator applies it to you", string.Join("; ", issues));
                issues.Clear();
            }
            if (window.StaleBecause != null && issues.Count > 0)
            {
                // Played before the previous card finished: its tabs didn't see that card's effects.
                model.Skip("", "its tabs were computed before another play finished", $"{window.StaleBecause} finished first ({string.Join("; ", issues)})");
                issues.Clear();
            }
            if (window.Facts?.SimPending == true)
            {
                // Its simulation was still running when the frame stopped waiting: every tab the simulator gives may be missing (every
                // proc tab can come from a model's log, its block and damage too: SimDependent, as for a capped search below), so none
                // of those checks can be held to it. Counted as not checked (decision #92).
                var simOnly = issues.Where(SimDependent).ToList();
                foreach (var issue in simOnly) model.Skip("", "simulation pending", issue);
                issues.RemoveAll(simOnly.Contains);
            }
            if (window.Facts is { Sim: null, SimPending: false } && issues.Any(SimDependent))
            {
                // The card's simulation couldn't be built (it threw: reported as a crash on its own): every tab the simulator gives
                // is missing, so those checks would only repeat that crash. Counted, not reported (decision #92).
                var fromSim = issues.Where(SimDependent).ToList();
                foreach (var issue in fromSim) model.Skip("", "no simulation for this card", issue);
                issues.RemoveAll(fromSim.Contains);
            }
            if (window.Facts is { } facts && (facts.SimIncomplete || facts.Unmodelled.Count > 0))
            {
                // Its tabs came from simulations stopped at the memory cap, or not modelled (decisions #107, #92): what's shown
                // carries the ?, and what happened may be one of those. Every proc tab can come from the simulator (each model's log),
                // so their checks aren't reported; the snapshot checks run their own simulation and stay.
                var why = facts.Unmodelled.Count > 0 ? "a branch of its simulation not modelled" : "its simulation stopped at the memory cap";
                var fromSim = issues.Where(SimDependent).ToList();
                foreach (var issue in fromSim) model.Skip("", why, issue);
                issues.RemoveAll(fromSim.Contains);
            }
            if (issues.Count > 0 && window.Facts?.Sim is { FullStart: not null } handSim
                && (handSim.PlayByTarget.TryGetValue(handSim.KeyFor(window.Target), out var handPlay) || handSim.PlayByTarget.TryGetValue(-1, out handPlay))
                && handSim.HandChangesThePlay(handPlay))
            {
                // Count teammates' hands is off and their hand changes the play: its tabs were worked out without what decides it
                // (decision #25), so they can't be held to what happened. The state after the play is checked with it.
                var fromSim = issues.Where(SimDependent).ToList();
                foreach (var issue in fromSim) model.Skip("", "a teammate's hand changes it; Count teammates' hands is off", issue);
                issues.RemoveAll(fromSim.Contains);
            }
            // What the card showed in hand against the same prediction (decision #103): after the filters above, which are about
            // what happened, not about what was shown; this check has its own (ShownCheck).
            if (window.ShownDiff != null) issues.AddRange(window.ShownDiff);
            var turn = Sts2State.TurnNumber;
            if (window.ShownSkip != null)
            {
                var shownSkip = "tabs shown: " + window.ShownSkip;
                _skipCounts[shownSkip] = (_skipCounts.TryGetValue(shownSkip, out var skn) ? skn : 0) + 1;
                Telemetry.DevEvents.Post(new Telemetry.CheckSkipped(Telemetry.Audits.Play, turn, shownSkip, window.CardName));
            }
            foreach (var skip in model.Skips)
            {
                var reason = skip.Reason;
                _skipCounts[reason] = (_skipCounts.TryGetValue(reason, out var skipCount) ? skipCount : 0) + 1;
                // The play that ended the fight: what the game never logged or cleaned up first, which no model can close (Expected).
                var expected = window.FightEnded && skip.Expected;
                Telemetry.DevEvents.Post(new Telemetry.CheckSkipped(Telemetry.Audits.Play, turn, reason, window.CardName, expected));
            }
            foreach (var reason in window.Facts?.Unmodelled ?? Array.Empty<string>())
                _gapCounts[reason] = (_gapCounts.TryGetValue(reason, out var gapCount) ? gapCount : 0) + 1;
            _checked++;
            if (issues.Count == 0) _matched++;
            Telemetry.DevEvents.Post(new Telemetry.CheckDone(Telemetry.Audits.Play, turn, issues.Count == 0, issues.Count == 0 ? window.CardName : issues[0].Split(':')[0]));
            foreach (var issue in issues)
            {
                var kind = issue.Split(':')[0];
                _issueCounts[kind] = (_issueCounts.TryGetValue(kind, out var issueCount) ? issueCount : 0) + 1;
            }
            var log = ModEntry.Log;
            var tabs = window.Predicted.Count == 0 ? "none" : string.Join(", ", window.Predicted.Select(tab => tab.Text ?? (tab.Amount > 0 ? $"{tab.Key} {tab.Amount}" : tab.Key)));
            if (Sts2State.TurnNumber != _turnPlaysTurn)
            {
                _turnPlays.Clear();
                _turnPlaysTurn = Sts2State.TurnNumber;
            }
            _turnPlays.Add($"{_turnPlays.Count + 1}. {window.CardName}: tabs {tabs} → {(issues.Count == 0 ? "matched" : string.Join("; ", issues))}");
            if (issues.Count > 0)
            {
                log.Warn($"[audit] played {window.CardName}: {string.Join("; ", issues)}. Tabs were: {tabs}.");
                _tabFilter = _ctx.Decor.Filter;
                var lines = WindowLines(window, hist, me);
                foreach (var line in lines) log.Warn($"[audit]   {line}");
                Report(window, issues, tabs, lines, entries.Count, string.Join("\n", _turnPlays));
            }
            foreach (var note in model) log.Info($"[audit] [model] played {window.CardName}: {note}");
        }
        catch (Exception ex)
        {
            ModEntry.Log.Warn($"[audit] couldn't check {window.CardName}: {ex.Message}");
            Sts2Mods.Core.Diagnostics.ErrorReporter.Instance?.Exception(ex, "audit-check");
        }
    }

    /// <summary>
    /// One Sentry report per issue, grouped by what's wrong rather than by amounts: a missing or
    /// silent source groups by that source; block, damage and budget issues group by the card.
    /// </summary>
    private static Func<string, bool>? _tabFilter;

    private static string Hidden(Window window)
    {
        var hidden = _tabFilter == null ? new List<string>() : window.Predicted.Where(tab => !_tabFilter(tab.Key)).Select(tab => tab.Key).ToList();
        return hidden.Count == 0 ? "none" : string.Join(", ", hidden);
    }

    private static void Report(Window otherWindow, List<string> issues, string tabs, List<string> window, int windowSize, string turnPlays)
    {
        var reporter = Sts2Mods.Core.Diagnostics.ErrorReporter.Instance;
        if (reporter is not { Enabled: true }) return;
        var card = otherWindow.Card.GetType().Name;
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
                // Which tab on which card: a cache that missed something about this card, or a tab drawn from an old frame.
                "tabs shown" => new[] { kind, card, rest.Split(' ')[0] },
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
            if (otherWindow.Before != null)
            {
                foreach (var key in new[] { "player", "player_powers", "relics", "orbs", "enemies", "hand" })
                    if (otherWindow.Before.TryGetValue(key, out var before)) extra["before_" + key] = before;
            }
            if (kind.StartsWith("after play") && otherWindow.Facts?.Sim != null)
            {
                extra["sim_state"] = otherWindow.Facts.Sim.Start.ToJson();
                if (otherWindow.After != null) extra["sim_state_after"] = otherWindow.After.ToJson();
            }
            var lag = otherWindow.Lag < 0 ? "never seen in hand" : $"{otherWindow.Lag} frame(s)";
            // Tags: the card, the check, and the source the issue names (a relic or power, or the candidates).
            var source = fp.Length > 1 && fp[1] != card ? fp[1] : "";
            var tags = new Dictionary<string, string>
            {
                ["card"] = card, ["check"] = kind, ["source"] = source, ["effect"] = kind == "unexplained" ? effect : "",
                ["encounter"] = Sts2State.Combat?.Encounter?.GetType().Name ?? "",
            };
            reporter.Mismatch("proc", $"Proc audit: {issue} (played {card})", fp,
                $"Played {otherWindow.CardName} ({card}).\nIssue: {issue}\nAll issues this play: {string.Join("; ", issues)}\nTabs before the play: {tabs}" +
                $"\nFrom the card's last frame in hand to its play starting: {lag}" +
                $"\nExpected from the card itself: {(otherWindow.Facts != null ? Snapshot.FactsLine(otherWindow.Facts) : "no facts")}" +
                $"\nTabs hidden by settings: {Hidden(otherWindow)}" +
                $"\nPlay: {windowSize} history entries from its action (span {otherWindow.From}..{otherWindow.To}), play started at {otherWindow.Start}; shuffles {otherWindow.Shuffles}",
                extra, tags);
        }
    }

    /// <summary>"predicted 4, actual 0" → "less" (more/less than predicted; amounts left out).</summary>
    private static string Direction(string rest)
    {
        static int Last(string text) => int.TryParse(new string(text.Trim().Reverse().TakeWhile(char.IsDigit).Reverse().ToArray()), out var number) ? number : 0;
        var parts = rest.Split(',');
        if (parts.Length < 2) return "";
        var actual = Last(parts[1]);
        var predicted = parts[0].Replace("predicted", "").Trim();
        var range = predicted.Split('–');
        var low = Last(range[0]);
        var high = Last(range[^1]);
        return actual > high ? "more" : actual < low ? "less" : "";
    }

    private void Summarize()
    {
        // How the simulations did against the frame's wait and their caps (decision #104).
        if (Combat.SimWait.Summary() is { } waits) ModEntry.Log.Info($"[perf] combat summary: simulations: {waits}.");
        if (_ctx.Predictions.TakeHeldFrames() is > 0 and var held) ModEntry.Log.Info($"[perf] combat summary: card frames shown with tabs held from an earlier board while simulations ran: {held}.");
        if (Combat.TurnEndModel.TakeHeldFrames() is > 0 and var heldForecast) ModEntry.Log.Info($"[perf] combat summary: frames showing the forecast held from an earlier board: {heldForecast}.");
        if (SimJob.CapSummary() is { } caps) ModEntry.Log.Info($"[perf] combat summary: searches stopped by {caps}.");
        if (_checked == 0) return;
        var issues = _issueCounts.Count == 0 ? "" : $" ({string.Join(", ", _issueCounts.OrderByDescending(entry => entry.Value).Select(entry => $"{entry.Key} ×{entry.Value}"))})";
        ModEntry.Log.Info($"[audit] combat summary: {_checked} card plays checked ({_fromHand} against the tabs in hand, {_interleaved} with another action during a choice), {_matched} matched{issues}.");
        static string List(Dictionary<string, int> counts) => string.Join(", ", counts.OrderByDescending(entry => entry.Value).Select(entry => $"{entry.Key} ×{entry.Value}"));
        if (_skipCounts.Count > 0) ModEntry.Log.Info($"[audit] combat summary: card play checks skipped: {List(_skipCounts)}.");
        if (_gapCounts.Count > 0) ModEntry.Log.Info($"[audit] combat summary: not modelled in played cards' simulations: {List(_gapCounts)}.");
        _checked = _matched = _fromHand = _interleaved = 0;
        _issueCounts.Clear();
        _skipCounts.Clear();
        _gapCounts.Clear();
    }

    private static List<string> Compare(Window window, Player me, List<CombatHistoryEntry> entries, List<AbstractModel> flashed, out ModelLines model)
    {
        var issues = new List<string>();
        var modelLines = new ModelLines();
        model = modelLines;
        // Random outcomes (several enemies): only effects the history shows a random hit caused are
        // excused. Random hits = damage to enemies with no card source (procs, orbs) or from this card
        // when it targets a random enemy.
        var random = window.Facts?.RandomOutcome == true;
        var randomHits = !random
            ? new List<DamageReceivedEntry>()
            : entries.OfType<DamageReceivedEntry>()
                .Where(damage => damage.Result.Receiver.IsEnemy
                            && (damage.CardSource == null || (window.Card.TargetType == TargetType.RandomEnemy && ReferenceEquals(damage.CardSource, window.Card))))
                .ToList();
        var randomKill = randomHits.Any(damage => damage.Result.WasTargetKilled);
        bool RandomlyHit(Creature creature) => randomHits.Any(damage => damage.Result.Receiver == creature);
        var sourceOf = new Dictionary<string, TabSpec>();
        foreach (var tab in window.Predicted)
        {
            var colon = tab.Key.IndexOf(':');
            if (colon > 0) sourceOf[tab.Key[(colon + 1)..]] = tab;
        }

        // ---- sources that fired with no tab (missing rules)
        foreach (var otherModel in flashed.Distinct())
        {
            if (sourceOf.ContainsKey(otherModel.GetType().Name) || Modifiers.Contains(otherModel.GetType().Name) || TeammateOnly.Contains(otherModel.GetType().Name)) continue;
            // A power that only changes numbers (Fasten's block on Defends): the play's block and damage checks cover it.
            if (otherModel is PowerModel && CodeIndex.OnlyModifies(otherModel.GetType())) continue;
            if (LaterPlayOnly.Contains(otherModel.GetType().Name))
            {
                modelLines.Add($"{otherModel.GetType().Name} fired (it changes a card for a later play: no tab by design)");
                continue;
            }
            // A double-play source (Throwing Axe flashes in AfterModifyingCardPlayCount) when the card did play more
            // than once: its tab is Card Indicators' (the game's own play-count query), not a proc rule.
            if (CodeIndex.Does(otherModel.GetType(), Array.Empty<string>(), new[] { "ModifyCardPlayCount" })
                && entries.OfType<CardPlayStartedEntry>().Any(cardPlayStartedEntry => cardPlayStartedEntry.CardPlay.Card == window.Card && cardPlayStartedEntry.CardPlay.PlayCount > 1)) continue;
            // A random-target hit or kill (several enemies) can set off enemy powers and kill relics
            // we can't predict: noted, not reported.
            var causedByRandom = otherModel switch
            {
                PowerModel { Owner.IsEnemy: true } power => RandomlyHit(power.Owner) || randomKill,
                // HP lost to an enemy's Thorns that a random hit set off.
                _ when HpLossReactive.Contains(otherModel.GetType().Name) => entries.OfType<DamageReceivedEntry>()
                    .Any(damage => damage.Result.Receiver == me.Creature && damage.Dealer is { IsEnemy: true } dealer && RandomlyHit(dealer)),
                _ => KillReactive.Contains(otherModel.GetType().Name) && randomKill,
            };
            if (causedByRandom)
            {
                // Random targets are branches (decision #95): excused when some outcome of the simulation set it off; reported when none did.
                var had = SimHadIt(window, otherModel);
                if (had == true)
                {
                    modelLines.Add($"{otherModel.GetType().Name} fired after a random hit or kill (some outcomes set it off: a roll decided it)");
                    continue;
                }
                if (had == null)
                {
                    modelLines.Skip("no tab", "fired after a random hit or kill; no simulation to tell", otherModel.GetType().Name);
                    continue;
                }
                issues.Add($"no tab: {otherModel.GetType().Name} fired (after a random hit or kill, but no outcome of the simulation set it off)");
                continue;
            }
            // A simulator tab shows only when the play changes the outcome; what your end of turn sets off anyway
            // (a Lightning orb's hit stunning a Shriek enemy) gets no tab, though this play set it off first.
            if (otherModel is PowerModel { Owner.IsEnemy: true } otherPower && BaselineHasIt(window, otherPower.GetType().Name, otherPower.Owner))
            {
                modelLines.Add($"{otherModel.GetType().Name} fired on this play, but your end of turn sets it off anyway (no tab by design)");
                continue;
            }
            // Cards the play auto-plays at random (The Fool: random cards from your hand) set off what their own
            // plays would: which cards is a roll.
            if (UnpredictedAutoPlay(window, entries, me))
            {
                modelLines.Skip("no tab", "fired during random auto-plays", otherModel.GetType().Name);
                continue;
            }
            if (DrawReactive.Contains(otherModel.GetType().Name) && entries.OfType<CardDrawnEntry>().Any(cardDrawnEntry => !cardDrawnEntry.FromHandDraw && cardDrawnEntry.Card.Owner == me))
            {
                // Draws are branches (decisions #90, #95): excused when some outcome of the simulation set it off; reported when none did.
                var had = SimHadIt(window, otherModel);
                if (had == false)
                {
                    issues.Add($"no tab: {otherModel.GetType().Name} fired (on a drawn card, but no outcome of the simulation set it off)");
                    continue;
                }
                if (had == true) modelLines.Add($"{otherModel.GetType().Name} fired on a drawn card (some outcomes set it off: the draw decided it)");
                else modelLines.Skip("no tab", "fired on a drawn card; no simulation to tell", otherModel.GetType().Name);
                sourceOf[otherModel.GetType().Name] = new TabSpec("proc:" + otherModel.GetType().Name, Icon: null);
                continue;
            }
            // The play ended the fight: a proc with a rule fires with nothing left to hit (Violent Chord after the
            // killing blow), so its rule rightly showed no tab.
            if (window.FightEnded && SpireQoL.Menu.SettingsCatalog.Sources.Any(sourceEntry => sourceEntry.Type == otherModel.GetType().Name && sourceEntry.Tag != null))
            {
                modelLines.Add($"{otherModel.GetType().Name} fired as the play ended the fight (nothing left for it to act on)");
                continue;
            }
            issues.Add($"no tab: {otherModel.GetType().Name} fired");
        }

        // ---- relic tabs whose relic didn't fire (crossed ones mean "this breaks it": no flash)
        foreach (var (name, otherTab) in sourceOf)
        {
            if (otherTab.Crossed || !otherTab.Key.StartsWith("proc:") || EndOfTurnRelicTabs.Contains(name) || SilentRelics.Contains(name)) continue;
            // The play ended the fight: the game stops at the killing hit, so a relic that acts after it (Bone Flute after
            // Osty's attack) never gets to.
            if (window.FightEnded) continue;
            // Razor Tooth upgrades the played card without flashing (RazorTooth.AfterCardPlayed: no Flash call): its
            // effect is checked instead (CardCmd.Upgrade does nothing once the fight is ending).
            if (name == nameof(RazorTooth))
            {
                if (window.UpgradeBefore < 0 || window.FightEnded) continue;
                if (window.Card.CurrentUpgradeLevel <= window.UpgradeBefore) issues.Add($"didn't fire: {name} (the card wasn't upgraded)");
                continue;
            }
            var players = me.Creature.CombatState?.Players ?? (IReadOnlyList<Player>)new[] { me };
            if (players.Any(player => player.Relics.Any(relic => relic.GetType().Name == name)) && !flashed.Any(otherModel => otherModel.GetType().Name == name))
                issues.Add($"didn't fire: {name}");
        }

        GivenUpIssues(window, me, entries, sourceOf, issues, modelLines);

        // ---- Tabbed sources every outcome of the play sets off (decision #95): powers included, crossed or not, when their code
        // flashes. Relics are checked above (their plain tabs); a source that changes nothing visible doesn't flash.
        if (!window.FightEnded && CheckedAim(window) is { } everyAim && window.Facts?.FiresEveryByAim?.TryGetValue(everyAim, out var every) == true)
        {
            foreach (var type in every!)
            {
                // Powers, and relics whose crossed tab a model fires when it acts and flashes (Biiig Hug); plain relic tabs are
                // checked above, and crossed ones that give up a bonus by what the game shows instead (GivenUpIssues).
                var flashingRelic = typeof(RelicModel).IsAssignableFrom(type) && SimModels.Get(type.Name) is { Harmful: true, Seen: ActedSeen.Flash };
                if (!(typeof(PowerModel).IsAssignableFrom(type) || flashingRelic) || LaterPlayOnly.Contains(type.Name)) continue;
                if (!CodeIndex.FlashesOutsideAmountHooks(type)) continue;
                if (flashed.Any(otherModel => otherModel.GetType() == type)) continue;
                issues.Add($"didn't fire: {type.Name} (every outcome of this play sets it off)");
            }
        }

        // ---- Powers a play spends without flashing, so their tabs are checked against the amount: Stretch (block cards,
        // StretchPower.AfterCardPlayed), Vigor and Gigantification (attacks, AfterAttack). Cards the play auto-played (Lost
        // Lute) can spend them too; a card that gives you the same power itself can leave it where it was.
        if (window.PowersBefore != null && window.PowersAfter != null && !window.FightEnded
            && !entries.OfType<CardPlayStartedEntry>().Any(cardPlayStartedEntry => cardPlayStartedEntry.CardPlay.IsAutoPlay))
        {
            var ownTypes = OwnPowerTypes(window.Card).Select(type => type.Name).ToHashSet();
            foreach (var spent in new[] { "StretchPower", nameof(VigorPower), nameof(GigantificationPower) })
            {
                if (!window.PowersBefore.TryGetValue((me.Creature, spent), out var before) || before <= 0) continue;
                if (spent != "StretchPower" && ownTypes.Contains(spent)) continue;
                var after = window.PowersAfter.TryGetValue((me.Creature, spent), out var spentAfter) ? spentAfter : 0;
                var tabbed = sourceOf.ContainsKey(spent);
                // Spent: the history shows it going down, even when the card gives it back (Cho Ki re-applies the
                // Stretch it had, so the amount ends where it started).
                var wasSpent = after < before || entries.OfType<PowerReceivedEntry>()
                    .Any(powerReceivedEntry => powerReceivedEntry.Actor == me.Creature && powerReceivedEntry.Power.GetType().Name == spent && powerReceivedEntry.Amount < 0);
                if (tabbed && !wasSpent) issues.Add($"didn't fire: {spent} (still {after})");
                if (!tabbed && wasSpent) issues.Add($"no tab: {spent} spent ({before}→{after})");
            }
            // Free card powers: a "free:" tab (plain or crossed) exactly when the play takes a charge.
            foreach (var spent in SpireQoL.Procs.FreeCards.Powers)
            {
                if (!window.PowersBefore.TryGetValue((me.Creature, spent), out var before) || before <= 0) continue;
                var after = window.PowersAfter.TryGetValue((me.Creature, spent), out var spentAfter) ? spentAfter : 0;
                var tabbed = window.Predicted.Any(tab => tab.Key == SpireQoL.Procs.FreeCards.Prefix + spent);
                // Spent: the history shows a charge going, even when the card gives one back (Unrelenting spends Free Attack and
                // applies it again, so the amount ends where it started).
                var wasSpent = after < before || entries.OfType<PowerReceivedEntry>()
                    .Any(powerReceivedEntry => powerReceivedEntry.Actor == me.Creature && powerReceivedEntry.Power.GetType().Name == spent && powerReceivedEntry.Amount < 0);
                if (tabbed && !wasSpent) issues.Add($"didn't fire: {spent} (still {after})");
                if (!tabbed && wasSpent && !ownTypes.Contains(spent)) issues.Add($"no tab: {spent} spent ({before}→{after})");
            }
        }

        // ---- block from procs (exact)
        var procBlock = entries.OfType<BlockGainedEntry>()
            .Where(blockGainedEntry => blockGainedEntry.Actor == me.Creature && blockGainedEntry.CardPlay == null)
            .Sum(blockGainedEntry => blockGainedEntry.Amount);
        var predictedBlock = window.Predicted.Where(tab => tab.Kind == TabAmount.Block).Sum(tab => tab.Amount) + (window.Facts?.OrbBlock ?? 0);
        // The play ended the fight: the history stops at the killing hit, so less than predicted is expected.
        void Shortfall(string what, string predicted, int actual) =>
            modelLines.Skip(what, FightEndedSkip + ": the game logs nothing from the killing hit on", $"predicted {predicted}, actual {actual}");
        if (procBlock < predictedBlock && window.FightEnded) Shortfall("block", predictedBlock.ToString(), procBlock);
        // Cards auto-played at random (Lost Lute, The Fool) set off block procs of their own (Song Path on a Cho it played).
        else if (procBlock > predictedBlock && UnpredictedAutoPlay(window, entries, me)) modelLines.Skip("block", "random auto-plays", $"predicted {predictedBlock}, actual {procBlock}");
        else if (procBlock != predictedBlock) issues.Add($"block: predicted {predictedBlock}, actual {procBlock}");

        // ---- damage from procs (no card source), against each tab's range
        var procDamage = entries.OfType<DamageReceivedEntry>().Where(damage => damage.CardSource == null).ToList();
        // A card that sets off poison (Outbreak: PoisonPower.Trigger) deals it with no card and no dealer: the card's own effect,
        // checked by the enemies' HP after the play (the simulator), not a proc.
        if (Sim.SimAdapter.TriggerPoisonCards.Contains(window.Card.GetType().Name))
            procDamage = procDamage.Where(damage => !(damage.Dealer == null && damage.Result.Receiver.IsEnemy)).ToList();
        int Dealt(DamageReceivedEntry damage) => damage.Result.TotalDamage + damage.Result.OverkillDamage;
        var toEnemies = procDamage.Where(damage => damage.Result.Receiver.IsEnemy).Sum(Dealt);
        var toMe = procDamage.Where(damage => damage.Result.Receiver == me.Creature).Sum(Dealt);
        var enemyTabs = window.Predicted.Where(tab => tab.Kind == TabAmount.Damage && !tab.Crossed).ToList();
        var meTabs = window.Predicted.Where(tab => tab.Kind == TabAmount.Damage && tab.Crossed).ToList();
        var (eMin, eMax) = (enemyTabs.Sum(tab => tab.AuditMin), enemyTabs.Sum(tab => tab.AuditMax));
        // Orbs the card itself evokes (Dualcast, channelling into full slots) deal unsourced damage too.
        eMin += window.Facts?.OrbDamageMin ?? 0;
        eMax += window.Facts?.OrbDamageMax ?? 0;
        var (mMin, mMax) = (meTabs.Sum(tab => tab.AuditMin), meTabs.Sum(tab => tab.AuditMax));
        // A damage cap outside the game's damage math stopped some of it, seen by its flash: Hardened Shell (HardenedShellPower caps HP
        // lost per turn in ModifyHpLostBeforeOstyLate, which the damage hooks don't reach), Slippery (SlipperyPower.ModifyHpLostAfterOsty: at
        // most 1 HP a hit; Vantom).
        static bool Caps(AbstractModel otherModel) => otherModel is PowerModel { Owner.IsEnemy: true } && (Modifiers.Contains(otherModel.GetType().Name) || otherModel.GetType().Name == nameof(SlipperyPower));
        var capped = flashed.Any(Caps);
        if (toEnemies < eMin && capped) modelLines.Add($"damage to enemies: predicted {Range(eMin, eMax)}, actual {toEnemies}, capped by {string.Join(", ", flashed.Where(Caps).Select(otherModel => otherModel.GetType().Name).Distinct())}");
        else if (toEnemies < eMin && window.FightEnded) Shortfall("damage to enemies", Range(eMin, eMax), toEnemies);
        else if (toEnemies > eMax && UnpredictedAutoPlay(window, entries, me)) modelLines.Skip("damage to enemies", "random auto-plays", $"predicted {Range(eMin, eMax)}, actual {toEnemies}");
        else if (toEnemies < eMin || toEnemies > eMax) issues.Add($"damage to enemies: predicted {Range(eMin, eMax)}, actual {toEnemies}");
        if (toMe < mMin && window.FightEnded) Shortfall("damage to you", Range(mMin, mMax), toMe);
        else if (toMe < mMin || toMe > mMax) issues.Add($"damage to you: predicted {Range(mMin, mMax)}, actual {toMe}");

        // ---- budget: everything else the play caused
        var autoPlayed = UnpredictedAutoPlay(window, entries, me);
        if (autoPlayed) modelLines.Skip("budget", "random auto-plays");
        var facts = autoPlayed ? null : window.Facts;
        bool Explained(Effect effect) => sourceOf.Keys.Any(key => SourceEffects.TryGetValue(key, out var effects) && effects.Contains(effect));
        void Budget(Effect effect, string what, int actual, int own)
        {
            if (actual > own && !Explained(effect) && randomKill) modelLines.Skip($"unexplained {what}", "after a random kill", $"{actual - own} more than the card's own {own}");
            else if (actual > own && !Explained(effect)) issues.Add($"unexplained: {actual - own} more {what} than the card's own {own}");
            // Fewer than the card's own: "up to" counts you choose (exhausts, discards, created cards), or a pile ran out. Draws and
            // channels, which aren't a choice, are reported above.
            else if (actual < own) modelLines.Add($"expected {own} {what}, saw {actual}");
        }
        var draws = entries.OfType<CardDrawnEntry>().Count(cardDrawnEntry => !cardDrawnEntry.FromHandDraw && cardDrawnEntry.Card.Owner == me);
        // A card whose draws depend on its target (Ki Hop, Wrest): what it draws at the target it was played at.
        var expectedDraws = facts == null ? 0 : facts.Draws;
        if (facts != null && window.TargetDraws is { } targetDraws)
            expectedDraws = facts.DrawsWithOwn(targetDraws) ?? Math.Max(0, facts.Draws - (facts.OwnDraws - facts.EnchantmentDraws) + targetDraws);
        var exhausts = entries.OfType<CardExhaustedEntry>().Count(cardExhaustedEntry => cardExhaustedEntry.Card.Owner == me);
        var discards = entries.OfType<CardDiscardedEntry>().Count(cardDiscardedEntry => cardDiscardedEntry.Card.Owner == me);
        var generated = entries.OfType<CardGeneratedEntry>().Count(cardGeneratedEntry => cardGeneratedEntry.Creator == me || cardGeneratedEntry.Card.Owner == me);
        var orbs = entries.OfType<OrbChanneledEntry>().Count();
        // A card that summons every player (Legion of Bone) summons each up to its own count; any other card's summons all count as its own.
        var summoned = entries.OfType<SummonedEntry>().ToList();
        var summons = SimAdapter.AllPlayersSummonCards.Contains(window.Card.GetType().Name)
            ? summoned.GroupBy(summonedEntry => summonedEntry.Actor).Select(byActor => byActor.Count()).DefaultIfEmpty(0).Max()
            : summoned.Count;
        if (facts != null)
        {
            // Draws and channels aren't a choice: fewer than predicted is a model gap (FTL's play limit),
            // unless the fight ended (nothing more runs), fewer cards were exhausted than the upper bound
            // (Dark Embrace and Joss Paper draw per exhaust), or you have no orb slots left (the channel fails).
            if (draws < expectedDraws && !window.FightEnded && exhausts >= facts.TotalExhausts) issues.Add($"draws: predicted {expectedDraws}, actual {draws}");
            if (orbs < facts.Channels && !window.FightEnded && window.After?.You.OrbCapacity != 0) issues.Add($"orbs channeled: predicted {facts.Channels}, actual {orbs}");
            // A card whose draws depend on what it draws (Pillage) may draw up to its most (decision #118).
            Budget(Effect.Draw, "draws", draws, Math.Max(expectedDraws, facts.DrawsAtMost));
            Budget(Effect.Exhaust, "exhausts", exhausts, facts.TotalExhausts);
            Budget(Effect.Discard, "discards", discards, facts.Discards);
            // Transformed cards are logged as created too (CardCmd.Transform), up to what the card can transform.
            Budget(Effect.Generate, "created cards", generated, facts.Generated + facts.Transforms);
            Budget(Effect.Orb, "orbs channeled", orbs, facts.Channels);
            Budget(Effect.Summon, "summons", summons, window.Card.DynamicVars.ContainsKey("Summon") ? facts.Plays : 0);
        }

        // Powers applied: types the card doesn't carry itself (its PowerVar<T> values), with no tab
        // whose source applies powers. Strength/Dexterity changes from Vigor-style consumption and
        // temporary powers expiring are excluded.
        var own = OwnPowerTypes(window.Card);
        var foreign = entries.OfType<PowerReceivedEntry>()
            .Where(powerReceivedEntry => powerReceivedEntry.Amount > 0 && !own.Contains(powerReceivedEntry.Power.GetType()) && powerReceivedEntry.Power is not ITemporaryPower)
            .Where(powerReceivedEntry => !(powerReceivedEntry.Actor.IsEnemy && powerReceivedEntry.Applier is { IsEnemy: true })) // enemies acting on each other (Heist)
            // An enemy that comes in as another dies, set up with its powers by no one (StockPower.AfterDeath spawns the
            // next Axebot and gives it Stock).
            .Where(powerReceivedEntry => !(powerReceivedEntry.Actor.IsEnemy && powerReceivedEntry.Applier == null && DiedInPlay(entries)))
            // A death in the play giving back what a power on the dead creature took, applied by no one
            // (PossessStrengthPower.AfterDeath: the stolen Strength back to each player).
            .Where(powerReceivedEntry => !(powerReceivedEntry.Applier == null && DeathGivesPowers(window, entries)))
            // A summon that makes a new Osty gives it Die For You (OstyCmd.Summon), applied by no one.
            .Where(powerReceivedEntry => !(powerReceivedEntry.Power is DieForYouPower && powerReceivedEntry.Applier == null && entries.OfType<SummonedEntry>().Any()))
            .Select(powerReceivedEntry => powerReceivedEntry.Power.GetType().Name)
            .Distinct()
            .ToList();
        if (foreign.Count > 0 && !Explained(Effect.Power))
        {
            if (randomKill) modelLines.Skip("unexplained powers", "after a random kill", string.Join(", ", foreign));
            else if (autoPlayed) modelLines.Skip("unexplained powers", "random auto-plays", string.Join(", ", foreign));
            else issues.Add($"unexplained: powers applied ({string.Join(", ", foreign)})");
        }

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
    private static void PowerChangeIssues(Window window, Player me, List<CombatHistoryEntry> entries, List<AbstractModel> flashed, List<string> issues, ModelLines model)
    {
        if (window.PowersBefore == null || window.PowersAfter == null) return;
        // The card's own powers, and those of cards it auto-played.
        var own = OwnPowerTypes(window.Card).Select(type => type.Name).ToHashSet();
        foreach (var cardPlayStartedEntry in entries.OfType<CardPlayStartedEntry>().Where(entry => entry.CardPlay.IsAutoPlay))
            own.UnionWith(OwnPowerTypes(cardPlayStartedEntry.CardPlay.Card).Select(type => type.Name));
        var acted = flashed.Select(otherModel => otherModel.GetType().Name).ToHashSet();
        var tabbed = window.Predicted.Select(tab => tab.Key.Contains(':') ? tab.Key[(tab.Key.IndexOf(':') + 1)..] : tab.Key).ToHashSet();
        // Artifact blocks any debuff, temporary ones included (Dark Shackles).
        var debuffs = window.Facts?.EnemyDebuffs.Count > 0 || own.Any(typeName => PowerTypeNamed(typeName) is { } type && PlayFacts.IsDebuffType(type));
        var died = DiedInPlay(entries);
        foreach (var ((creature, type), before) in window.PowersBefore)
        {
            var after = window.PowersAfter.TryGetValue((creature, type), out var amountAfter) ? amountAfter : 0;
            if (after >= before) continue;
            if (!creature.IsAlive) continue;
            // Killed in the play but kept in the fight (the Waterfall Giant erupts): its powers come off at death
            // (ShouldPowerBeRemovedAfterOwnerDeath), all but the one keeping it there.
            if (entries.OfType<DamageReceivedEntry>().Any(damage => damage.Result.WasTargetKilled && damage.Result.Receiver == creature)) continue;
            if (own.Contains(type) || acted.Contains(type) || tabbed.Contains(type) || SimComparedPowers.Contains(type)) continue;
            if (creature == me.Creature && SpentByAnyPlay.Contains(type)) continue;
            if (creature == me.Creature && SpentByAttack.Contains(type) && window.Card.Type == CardType.Attack) continue;
            // A double-play source this play used (Burst, Duplication: ModifyCardPlayCount, then a stack goes) when the card
            // played more than once: its tab is Card Indicators' (the game's own play-count query), not a proc rule.
            if (creature == me.Creature && PowerTypeNamed(type) is { } countSource
                && CodeIndex.Does(countSource, Array.Empty<string>(), new[] { "ModifyCardPlayCount" })
                && entries.OfType<CardPlayStartedEntry>().Any(cardPlayStartedEntry => cardPlayStartedEntry.CardPlay.Card == window.Card && cardPlayStartedEntry.CardPlay.PlayCount > 1)) continue;
            if (creature == me.Creature && Stats.Contains(type)
                && acted.Concat(tabbed).Any(source => SourceEffects.TryGetValue(source, out var effects) && effects.Contains(Effect.Power))) continue;
            // A tabbed or flashed power on that creature removes it (Plow takes the Ceremonial Beast's Strength
            // as it stuns her: PowerCmd.Remove; the index has no type arguments).
            // (Read from the powers before the play: Plow removes itself too.)
            if (window.PowersBefore.Keys.Any(key => key.Item1 == creature && (acted.Contains(key.Item2) || tabbed.Contains(key.Item2))
                                             && PowerTypeNamed(key.Item2) is { } src
                                             && CodeIndex.Does(src, new[] { "PowerCmd.Remove" }, Array.Empty<string>()))) continue;
            if (creature.IsEnemy && BaselineHasIt(window, type, creature)) continue;
            // A power that counts down on each hit through its owner's block (Slumber, Flutter: AfterDamageReceived →
            // PowerCmd.Decrement), and this play got damage through to it.
            if (entries.OfType<DamageReceivedEntry>().Any(damage => damage.Result.Receiver == creature && damage.Result.UnblockedDamage > 0)
                && PowerTypeNamed(type) is { } counted
                && CodeIndex.Does(counted, new[] { "PowerCmd.Decrement" }, Array.Empty<string>())
                && CodeIndex.Does(counted, Array.Empty<string>(), new[] { "AfterDamageReceived" })) continue;
            if (type == nameof(ArtifactPower) && debuffs) continue;
            if (creature.Powers.FirstOrDefault(power => power.GetType().Name == type) is ITemporaryPower) continue;
            // A death in the play: powers that react to it (Dampen comes off you when its caster dies).
            if (died && PowerTypeNamed(type) is { } otherType && CodeIndex.Does(otherType, Array.Empty<string>(), DeathHooks)) continue;
            var who = creature == me.Creature ? "you" : creature.Monster?.GetType().Name ?? "creature";
            issues.Add($"power went down: {type} on {who} {before}→{after}, with nothing in the play to explain it");
        }
    }

    /// <summary>
    /// The end-of-turn forecast without this play already has this simulator-tab power on <paramref name="enemy"/> going
    /// off (stunned for Shriek, Plow, Burrowed, Flutter; woken for Asleep), so its tab rule rightly showed nothing.
    /// </summary>
    private static bool BaselineHasIt(Window window, string power, Creature enemy)
    {
        if (!SimTabSources.Contains(power) || window.Facts?.Sim is not { } sim || !sim.Ids.TryGetValue(enemy, out var id)) return false;
        try
        {
            var baseline = SimEngine.Run(sim.Start);
            return power switch
            {
                nameof(ShriekPower) or nameof(PlowPower) or nameof(BurrowedPower) or nameof(FlutterPower) => baseline.StunnedInAll(id),
                nameof(AsleepPower) => baseline.UnblockedMax(id) >= 1,
                _ => false,
            };
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Crossed tabs for giving up a relic's bonus, which the game doesn't flash (decision #117), held to what it shows instead, both
    /// ways (a tab when the bonus wasn't given up, or the bonus given up with no tab):
    /// <list type="bullet">
    /// <item>the relic going dark (ActedSeen.StatusCleared): Art of War and Ripple Basin on the turn's first Attack, Pael's Eye on
    /// its first card, each clearing its Active status in the play's hooks;</item>
    /// <item>no energy left (ActedSeen.NoEnergyLeft): Pael's Tears, whose bonus needs energy left as your turn ends; the tab says
    /// the play spends energy and leaves none, so it's held to the energy spent in the play and what's left once it's over.</item>
    /// </list>
    /// </summary>
    private static void GivenUpIssues(Window window, Player me, List<CombatHistoryEntry> entries, Dictionary<string, TabSpec> sourceOf,
        List<string> issues, ModelLines model)
    {
        if (window.FullBefore == null || window.FullAfter == null) return;
        foreach (var (relic, (_, statusBefore)) in window.FullBefore.Relics)
        {
            var name = relic.GetType().Name;
            if (SimModels.Get(name) is not { Harmful: true } simModel || simModel.Seen == ActedSeen.Flash) continue;
            var tabbed = sourceOf.TryGetValue(name, out var tab) && tab.Crossed;
            bool givenUp;
            string what;
            if (simModel.Seen == ActedSeen.StatusCleared)
            {
                var statusAfter = window.FullAfter.Relics.TryGetValue(relic, out var otherRelic) ? otherRelic.Status : statusBefore;
                givenUp = statusBefore == MegaCrit.Sts2.Core.Entities.Relics.RelicStatus.Active
                          && statusAfter != MegaCrit.Sts2.Core.Entities.Relics.RelicStatus.Active;
                what = givenUp ? "it went dark" : statusBefore == MegaCrit.Sts2.Core.Entities.Relics.RelicStatus.Active ? "it stayed lit" : "it was already dark";
            }
            else if (simModel.Seen == ActedSeen.CounterPassedLimit)
            {
                // Pocketwatch: its counter (cards this turn) went from within its limit to past it.
                var limit = Sts2State.Var(relic, simModel.LimitVar ?? "", int.MaxValue);
                var before = window.FullBefore.Relics[relic].Display;
                var after = window.FullAfter.Relics.TryGetValue(relic, out var otherRelic) ? otherRelic.Display : before;
                givenUp = before <= limit && after > limit;
                what = $"its count went {before} → {after}, limit {limit}";
            }
            else
            {
                var spent = entries.OfType<EnergySpentEntry>().Where(energySpentEntry => energySpentEntry.Actor == me.Creature).Sum(energySpentEntry => energySpentEntry.Amount);
                givenUp = spent > 0 && window.FullAfter.Energy <= 0;
                what = $"spent {spent}, {window.FullAfter.Energy} energy left";
            }
            if (tabbed == givenUp) continue;
            // The fight's cleanup clears the relics' status as the play ends it (AfterCombatEnd: Status = Normal).
            if (window.FightEnded) { model.Skip(tabbed ? "didn't fire" : "no tab", FightEndedSkip, name); continue; }
            // Cards the play auto-played at random (Lost Lute, The Fool) can be the turn's first Attack, or spend energy.
            if (UnpredictedAutoPlay(window, entries, me)) { model.Skip(tabbed ? "didn't fire" : "no tab", "random auto-plays", name); continue; }
            issues.Add(tabbed ? $"didn't fire: {name} (crossed: {what})" : $"no tab: {name} gave up its bonus ({what})");
        }
    }

    /// <summary>The drawn-card chances shown and what happened, over the session (decision #119): a report once if they drift apart.</summary>
    private static readonly OddsTally DrawnOdds = new();
    private static bool _drawnDriftReported;

    /// <summary>
    /// A card that acts on what it draws (decision #119): what its draw gave (Escape Plan: a Skill or not; Pillage: the Attacks it
    /// drew; Scrape: the cards it drew that cost 0), read from the play's draws, must be an outcome its simulation had; the chance its
    /// tab showed is logged with what happened ([audit] [odds]) and added up over the session, as the death chances are.
    /// </summary>
    private static void DrawnIssues(Window window, Player me, List<CombatHistoryEntry> entries, List<string> issues, ModelLines model)
    {
        var name = window.Card.GetType().Name;
        if (!SimAdapter.DrawnCardReaders.TryGetValue(name, out var reader) || window.Facts?.Drawn is not { } distribution) return;
        if (window.FightEnded) { model.Skip("drawn", FightEndedSkip, name); return; }
        if (UnpredictedAutoPlay(window, entries, me)) { model.Skip("drawn", "random auto-plays", name); return; }
        if (window.Facts.SimIncomplete || window.Facts.Unmodelled.Count > 0) { model.Skip("drawn", "its simulation stopped at a cap or met something not modelled", name); return; }
        var drawn = entries.OfType<CardDrawnEntry>().Where(cardDrawnEntry => !cardDrawnEntry.FromHandDraw && cardDrawnEntry.Card.Owner == me).Select(cardDrawnEntry => cardDrawnEntry.Card).ToList();
        static bool Free(CardModel card)
        {
            try { return !card.EnergyCost.CostsX && card.EnergyCost.GetWithModifiers(CostModifiers.All) == 0; } catch { return false; }
        }
        var actual = reader.What == SimAdapter.DrawnCardReader.Reads.Cost ? drawn.Count(Free)
            : reader.Counts ? drawn.Count(card => card.Type == CardType.Attack)
            : drawn.Count > 0 && drawn[0].Type == CardType.Skill ? 1 : 0;
        if (!distribution.TryGetValue(actual, out var share) || share <= 0)
        {
            issues.Add($"drawn: {name} gave {actual}, which its simulation had no chance of ({string.Join(", ", distribution.Select(entry => $"{entry.Key}: {entry.Value:0.###}"))})");
            return;
        }
        // The chance shown, and whether it happened.
        double likelihood;
        bool happened;
        if (reader.Counts)
        {
            var (n, chance) = DrawnChance.MostLikely(distribution);
            (likelihood, happened) = (chance, actual == n);
        }
        else (likelihood, happened) = (DrawnChance.ChanceOfAny(distribution), actual > 0);
        if (likelihood <= 0 || likelihood >= 1) return; // certain either way: the check above holds it
        ModEntry.Log.Info($"[audit] [odds] {name}: predicted {new SimChance(likelihood, likelihood, Seen: true).Text()}, {(happened ? "happened" : "didn't")} (gave {actual})");
        DrawnOdds.Add(new SimChance(likelihood, likelihood, Seen: true), happened);
        if (!DrawnOdds.Drifted || _drawnDriftReported) return;
        _drawnDriftReported = true;
        Sts2Mods.Core.Diagnostics.ErrorReporter.Instance?.Mismatch("odds", $"Drawn-card odds: what happened is far from the chances shown ({DrawnOdds.Text()})",
            new[] { "drawn-odds" }, $"Over this session, the chances on Escape Plan, Pillage and Scrape tabs don't match what their draws gave: {DrawnOdds.Text()}. " +
            "Each is logged as [audit] [odds] in the game log.", new Dictionary<string, string>(), new Dictionary<string, string> { ["check"] = "odds" });
    }

    /// <summary>
    /// The play auto-played a card of yours the simulation doesn't follow: any but the one you chose, and a Strike Hellraiser
    /// played as the play drew it, which the simulator plays too (decision #121) when nothing in it went unmodelled.
    /// </summary>
    private static bool UnpredictedAutoPlay(Window window, List<CombatHistoryEntry> entries, Player me)
        => entries.OfType<CardPlayStartedEntry>().Any(cardPlayStartedEntry => cardPlayStartedEntry.CardPlay.IsAutoPlay && cardPlayStartedEntry.CardPlay.Card.Owner == me && !ReferenceEquals(cardPlayStartedEntry.CardPlay.Card, window.Chosen)
            && !HellraiserDrew(window, entries, me, cardPlayStartedEntry.CardPlay.Card));

    /// <summary>A Strike drawn in the play while you have Hellraiser, with a simulation that modelled everything (decision #121).</summary>
    private static bool HellraiserDrew(Window window, List<CombatHistoryEntry> entries, Player me, CardModel card)
        => window.Facts is { Unmodelled.Count: 0 } && me.Creature.HasPower<HellraiserPower>() && card.Tags.Contains(CardTag.Strike)
           && entries.OfType<CardDrawnEntry>().Any(cardDrawnEntry => ReferenceEquals(cardDrawnEntry.Card, card));

    private static readonly string[] DeathHooks = { "AfterDeath", "BeforeDeath" };

    private static bool DiedInPlay(List<CombatHistoryEntry> entries) => entries.OfType<DamageReceivedEntry>().Any(damage => damage.Result.WasTargetKilled);

    /// <summary>A creature killed in the play had a power (before it) whose death hook applies powers (Possess Strength).</summary>
    private static bool DeathGivesPowers(Window window, List<CombatHistoryEntry> entries)
    {
        if (window.PowersBefore == null) return false;
        var killed = entries.OfType<DamageReceivedEntry>().Where(damage => damage.Result.WasTargetKilled).Select(damage => damage.Result.Receiver).ToHashSet();
        return killed.Count > 0 && window.PowersBefore.Keys.Any(key => killed.Contains(key.Item1) && PowerTypeNamed(key.Item2) is { } type
            && CodeIndex.Does(type, Array.Empty<string>(), DeathHooks) && CodeIndex.Does(type, new[] { "PowerCmd.Apply*" }, Array.Empty<string>()));
    }

    /// <summary>The game's power class with this name (powers are recorded by name before the play).</summary>
    private static Type? PowerTypeNamed(string name) => typeof(ArtifactPower).Assembly.GetType(typeof(ArtifactPower).Namespace + "." + name);

    // ------------------------------------------------------------------ everything else

    /// <summary>
    /// Energy, stars, gold, HP, max HP, your cards and relics before and after the play (PlaySnapshot):
    /// a change no code that ran explains is reported with the sources present that could have made it.
    /// </summary>
    private static void EverythingIssues(Window window, Player me, List<CombatHistoryEntry> entries, List<AbstractModel> flashed, List<string> issues, ModelLines model)
    {
        if (window.FullBefore != null && window.FullAfter == null && window.AfterSkipped != null) model.Skip("your cards, relics and resources after the play", window.AfterSkipped);
        if (window.FullBefore == null || window.FullAfter == null) return;
        var combat = Sts2State.Combat;
        if (combat == null) return;
        var present = new List<AbstractModel>();
        present.AddRange(me.Relics);
        foreach (var creature in combat.Creatures)
        {
            present.AddRange(creature.Powers);
            if (creature.Monster != null) present.Add(creature.Monster);
        }
        foreach (var card in window.FullBefore.Cards.Keys)
        {
            present.Add(card);
            if (card.Enchantment != null) present.Add(card.Enchantment);
            if (card.Affliction != null) present.Add(card.Affliction);
        }
        var tabbed = window.Predicted.Select(tab => tab.Key.Contains(':') ? tab.Key[(tab.Key.IndexOf(':') + 1)..] : tab.Key).ToHashSet();
        // Code that ran: the card, sources with a tab, sources that flashed, cards it auto-played, cards
        // it drew (and their enchantments), sources that react to draws when it drew, and cards it exhausted that react to that.
        var autoPlayed = entries.OfType<CardPlayStartedEntry>().Where(cardPlayStartedEntry => cardPlayStartedEntry.CardPlay.IsAutoPlay).Select(cardPlayStartedEntry => cardPlayStartedEntry.CardPlay.Card).ToList();
        var drawn = entries.OfType<CardDrawnEntry>().Where(cardDrawnEntry => !cardDrawnEntry.FromHandDraw && cardDrawnEntry.Card.Owner == me).Select(cardDrawnEntry => cardDrawnEntry.Card).ToList();
        // Cards it exhausted that act when exhausted run their own AfterCardExhausted (Drum of Battle's energy); not their play's code.
        var exhaustReactors = entries.OfType<CardExhaustedEntry>().Where(cardExhaustedEntry => cardExhaustedEntry.Card.Owner == me).Select(cardExhaustedEntry => cardExhaustedEntry.Card.GetType())
            .Where(type => CodeIndex.Does(type, Array.Empty<string>(), new[] { nameof(AbstractModel.AfterCardExhausted) }));
        var drawReactors = drawn.Count == 0 ? Enumerable.Empty<AbstractModel>()
            : present.Where(otherModel => CodeIndex.Does(otherModel.GetType(), Array.Empty<string>(), new[] { "AfterCardDrawn", "AfterCardDrawnEarly" }));
        // A death in the play runs death hooks, including those of powers gone by its end (Dampen comes
        // off you when its caster dies and upgrades your cards back).
        var deathReactors = !DiedInPlay(entries) ? Enumerable.Empty<Type>()
            : present.Select(otherModel => otherModel.GetType())
                .Concat(window.PowersBefore?.Keys.Select(key => PowerTypeNamed(key.Item2)).OfType<Type>() ?? Enumerable.Empty<Type>())
                .Where(type => CodeIndex.Does(type, Array.Empty<string>(), DeathHooks));
        var explainers = present.Where(otherModel => tabbed.Contains(otherModel.GetType().Name)).Select(otherModel => otherModel.GetType())
            .Concat(flashed.Select(otherModel => otherModel.GetType())).Append(window.Card.GetType())
            .Concat(autoPlayed.Select(card => card.GetType()))
            .Concat(drawn.Select(card => card.GetType())).Concat(drawn.Where(card => card.Enchantment != null).Select(card => card.Enchantment!.GetType()))
            .Concat(drawReactors.Select(drawReactor => drawReactor.GetType()))
            .Concat(exhaustReactors)
            // Orbs the play may have evoked (channelling into full slots, evoke cards): Plasma's energy.
            .Concat(entries.OfType<OrbChanneledEntry>().Any() || CodeIndex.Does(window.Card.GetType(), new[] { "OrbCmd.Evoke*" }, Array.Empty<string>()) ? window.FullBefore.Orbs : Enumerable.Empty<Type>())
            .Concat(deathReactors)
            // Your powers a play uses up (Free Card): the costs they lowered go back up.
            .Concat(window.PowersBefore?.Keys.Where(key => key.Item1 == me.Creature && SpentByAnyPlay.Contains(key.Item2)).Select(key => PowerTypeNamed(key.Item2)).OfType<Type>() ?? Enumerable.Empty<Type>())
            .Distinct().ToList();
        var tracked = new HashSet<string>(tabbed);
        foreach (var relicCounterSpec in RelicSpecs.All) tracked.Add(relicCounterSpec.TypeName);
        tracked.UnionWith(RelicTracking.CountsForLater);
        // Random auto-plays (The Fool) can change any relic's state the way their own plays would (Ripple Basin
        // armed until an attack is played): which cards they were is a roll.
        if (UnpredictedAutoPlay(window, entries, me)) foreach (var relic in me.Relics) tracked.Add(relic.GetType().Name);
        issues.AddRange(PlaySnapshot.Unexplained(window.FullBefore, window.FullAfter, window.Card, entries, window.Shuffles, explainers, present, me, tracked));

        // ---- Cards that come back to your hand (Procs.ReturnToHand): what moved back during the play against the return
        // tabs. A card moving itself always explains its own move, so this is the only check on those tabs.
        if (window.FullBefore != null && window.FullAfter != null && !window.FightEnded)
        {
            var drawnNow = entries.OfType<CardDrawnEntry>().Select(cardDrawnEntry => cardDrawnEntry.Card).ToHashSet(ReferenceEqualityComparer.Instance);
            var cameBack = window.FullAfter.Cards
                .Where(entry => ReturnToHand.Modelled.Contains(entry.Key.GetType().Name) && entry.Value.Pile == PileType.Hand && !ReferenceEquals(entry.Key, window.Card)
                             && window.FullBefore.Cards.TryGetValue(entry.Key, out var was) && was.Pile != PileType.Hand && !drawnNow.Contains(entry.Key))
                .Select(entry => entry.Key.GetType().Name).ToHashSet();
            var tabbedBack = window.Predicted.Where(tab => tab.Key.StartsWith("return:")).Select(tab => tab.Key["return:".Length..]).ToHashSet();
            var randomPlays = UnpredictedAutoPlay(window, entries, me);
            foreach (var name in cameBack.Except(tabbedBack))
            {
                if (randomPlays) model.Skip("no tab", "random auto-plays", $"{name} came back to your hand");
                else issues.Add($"no tab: {name} came back to your hand");
            }
            foreach (var name in tabbedBack.Except(cameBack))
                issues.Add($"didn't fire: {name} didn't come back to your hand");
        }
    }

    // ------------------------------------------------------------------ shuffles

    /// <summary>Shuffles during the play (CardPileCmd.Shuffle prefix) against PlayFacts.Shuffles (the Shuffle Warning and shuffle procs).</summary>
    private static void ShuffleIssues(Window window, Player me, List<CombatHistoryEntry> entries, List<AbstractModel> flashed, List<string> issues, ModelLines model)
    {
        var facts = window.Facts;
        if (facts == null) return;
        var shuffled = window.Shuffles > 0;
        if (shuffled == facts.Shuffles) return;
        var autoPlayed = UnpredictedAutoPlay(window, entries, me);
        var drawReactive = flashed.Any(otherModel => DrawReactive.Contains(otherModel.GetType().Name) || otherModel.GetType().Name is nameof(GremlinHorn) or nameof(UnceasingTop));
        var draws = entries.OfType<CardDrawnEntry>().Count(cardDrawnEntry => !cardDrawnEntry.FromHandDraw && cardDrawnEntry.Card.Owner == me);
        var why = $"(draws: predicted {facts.Draws} = own {facts.OwnDraws} + set off {facts.ChainedDraws}{(facts.DrawsBlocked ? ", blocked" : "")}, actual {draws})";
        if (window.TargetDraws is { } targetDraws && targetDraws != facts.OwnDraws - facts.EnchantmentDraws)
        {
            model.Skip("shuffle", "its draws depend on its target, counted for every target", $"{(shuffled ? "happened" : "didn't happen")} {why}");
            return;
        }
        if (!shuffled && window.FightEnded)
        {
            // The play's hit ended the fight (Compile Driver on the last enemy): its draws, and their shuffle, never came.
            model.Skip("shuffle", FightEndedSkip, $"didn't happen {why}");
            return;
        }
        if (autoPlayed || (shuffled && drawReactive))
        {
            model.Skip("shuffle", "after random auto-plays or a draw-reactive source", $"{(shuffled ? "happened" : "didn't happen")} {why}");
            return;
        }
        issues.Add(shuffled
            ? $"shuffle: not predicted, but the discard pile was shuffled {why}"
            : $"shuffle: predicted, but no shuffle happened {why}");
    }

    // ------------------------------------------------------------------ state after the play

    /// <summary>
    /// The lethal tab's inputs (decision #95): each enemy's chance of dying before it acts, over every outcome of the play, against
    /// the end of turn simulated from the board the play left (the game's, read back). An enemy dead in every outcome must be
    /// certain to die then; one that lives in every outcome must not die then. A chance in between can't be judged on one play:
    /// the end-of-turn check totals the chances it shows (ForecastAudit), and the forecast from this board is itself checked
    /// against the enemy turn, so every link is paired.
    /// </summary>
    private static void LethalIssues(Window window, List<CombatHistoryEntry> entries, List<string> issues, ModelLines model)
    {
        var sim = window.Facts?.Sim;
        var deaths = window.Facts?.DeathByAim;
        if (sim == null || deaths == null || window.After == null || window.AfterIds == null || window.FightEnded) return;
        if (entries.OfType<CardPlayStartedEntry>().Any(cardPlayStartedEntry => cardPlayStartedEntry.CardPlay.IsAutoPlay && !ReferenceEquals(cardPlayStartedEntry.CardPlay.Card, window.Chosen)
                && !HellraiserDrew(window, entries, window.Card.Owner, cardPlayStartedEntry.CardPlay.Card)))
        {
            model.Skip("lethal", "auto-played cards");
            return;
        }
        if (!deaths.TryGetValue(sim.KeyFor(window.Target), out var card) && !deaths.TryGetValue(Who.None, out card)) return;
        // What's shown leaves teammates' hands out when the setting is off (decisions #25, #94): the same here.
        var post = SimEngine.Run(sim.FullStart != null ? window.After.WithoutTeammateHands() : window.After);
        if (post.Truncated || post.AnyUnmodelled || post.Branches.Count == 0)
        {
            model.Skip("lethal", "the board after the play: its search stopped at a cap or met something not modelled");
            return;
        }
        foreach (var (creature, id) in sim.Ids)
        {
            if (!card.TryGetValue(id, out var chance) || sim.Start.Enemy(id) is not { Hp: > 0 } start) continue;
            // A Waterfall Giant already about to blow before the play explodes on its turn whatever the play does: not the play's
            // to kill or spare (its 999999999 HP lives through any play). Not checked when the play's start wasn't seen.
            if (window.BlowingBefore == null ? IsBlowing(creature) : window.BlowingBefore.Contains(creature))
            {
                model.Skip("lethal", "an enemy about to blow before the play", start.Name);
                continue;
            }
            // Gone from the board after the play: dead. So is a Waterfall Giant the play brought to 0: Steam Eruption keeps it at
            // 999999999 HP, stunned until it blows (WaterfallGiant.TriggerAboutToBlowState), as good as dead (issue #428).
            var aboutToBlow = IsBlowing(creature);
            var simChance = !aboutToBlow && window.AfterIds.TryGetValue(creature, out var aid) && post.Branches[0].Enemy(aid) != null ? post.DeadChanceRange(aid) : new SimChance(Low: 1, High: 1, Seen: true);
            if (chance.Certain && !simChance.Certain)
                issues.Add($"lethal: {start.Name} predicted to die before acting in every outcome of this play, but after it: {simChance.Text()}");
            else if (!chance.Possible && simChance.Low > 0)
                issues.Add($"lethal: {start.Name} predicted to live in every outcome of this play, but after it dies before acting ({simChance.Text()})");
        }
    }

    /// <summary>
    /// The state the play left against the simulator's prediction of it (see the class summary).
    /// Each difference from the closest branch is one issue.
    /// </summary>
    private static void AfterPlayIssues(Window window, List<CombatHistoryEntry> entries, List<string> issues, ModelLines model)
    {
        var sim = window.Facts?.Sim;
        if (sim == null)
        {
            model.Skip("state after the play", "no simulation for this card");
            return;
        }
        if (window.After == null || window.AfterIds == null)
        {
            model.Skip("state after the play", window.AfterSkipped ?? "not captured");
            return;
        }
        if (entries.OfType<CardPlayStartedEntry>().Any(cardPlayStartedEntry => cardPlayStartedEntry.CardPlay.IsAutoPlay && !ReferenceEquals(cardPlayStartedEntry.CardPlay.Card, window.Chosen)
                && !HellraiserDrew(window, entries, window.Card.Owner, cardPlayStartedEntry.CardPlay.Card)))
        {
            model.Skip("state after the play", "auto-played cards");
            return;
        }
        if (window.Chosen != null && window.Chosen.TargetType == TargetType.AnyEnemy && window.Target == null)
        {
            model.Skip("state after the play", "the pick's auto-plays hit different random enemies");
            return;
        }
        var targetId = sim.KeyFor(window.Target);
        if (!sim.PlayByTarget.TryGetValue(targetId, out var play) && !sim.PlayByTarget.TryGetValue(-1, out play))
        {
            model.Skip("state after the play", "no simulated play for its target");
            return;
        }
        // Teammates' hands included (decision #25: the check uses everything that isn't a roll).
        SimEngine.ApplyPlayTo(sim.FullStart ?? sim.Start, play, out var branches, out var truncated);
        if (truncated || branches.Count == 0)
        {
            model.Skip("state after the play", "its search stopped at a cap");
            return;
        }
        var after = window.After;
        var afterIds = window.AfterIds;
        // Cards drawn during the play (decision #132: the draw tabs' numbers are part of it, with the card's own draws), when the
        // simulator drew from your pile: the history's draws of yours, not your turn's hand draw.
        var startP = (sim.FullStart ?? sim.Start).You;
        var me = window.Card.Owner;
        int? drawn = startP.Draw == null ? null : entries.OfType<CardDrawnEntry>().Count(cardDrawnEntry => cardDrawnEntry.Actor == me.Creature && !cardDrawnEntry.FromHandDraw);
        // Summoning raises Osty's HP in ways the simulator doesn't model: Osty isn't compared then.
        var summoned = entries.OfType<SummonedEntry>().Any();

        List<string> Diff(SimState pred)
        {
            var differences = new List<string>();
            foreach (var (creature, id) in sim.Ids)
            {
                var enemy = pred.Enemy(id);
                if (enemy == null || !afterIds.TryGetValue(creature, out var aid) || after.Enemy(aid) is not { } simEnemy) continue;
                var name = enemy.Name;
                static int Hp(SimEnemy nextEnemy) => Math.Max(0, nextEnemy.Hp);
                // The Waterfall Giant doesn't leave at 0 HP: Steam Eruption puts it about to blow at 999999999 HP (WaterfallGiant.
                // TriggerAboutToBlowState), stunned this turn, as good as dead until it explodes.
                var aboutToBlow = enemy.Hp <= 0 && IsBlowing(creature);
                if (Hp(enemy) != Hp(simEnemy) && !aboutToBlow) differences.Add($"after play enemy HP: {name} predicted {Hp(enemy)}, actual {Hp(simEnemy)}");
                if (simEnemy.Hp <= 0 || enemy.Hp <= 0) continue;
                if (enemy.Block != simEnemy.Block) differences.Add($"after play enemy block: {name} predicted {enemy.Block}, actual {simEnemy.Block}");
                if (enemy.Poison != simEnemy.Poison) differences.Add($"after play enemy poison: {name} predicted {enemy.Poison}, actual {simEnemy.Poison}");
                // FlutterPower.AfterDamageReceived: one stack per powered hit that gets through (no flash until it stuns).
                if (enemy.Flutter != simEnemy.Flutter) differences.Add($"after play enemy Flutter: {name} predicted {enemy.Flutter}, actual {simEnemy.Flutter}");
                if (enemy.Stunned != simEnemy.Stunned) differences.Add($"after play enemy stunned: {name} predicted {(enemy.Stunned ? "stunned" : "not stunned")}, actual {(simEnemy.Stunned ? "stunned" : "not stunned")}");
                // A stunned enemy's intent becomes the stun move; the simulator keeps its hits and skips them. The predicted intent is
                // its hits as they'd land on you in the predicted state (what the play gave or took: Weak, Tainted, Conductor's
                // Baton...); Hits is only the intent as it was read before the play.
                var predHits = enemy.Intent is { Count: > 0 } intent
                    ? Enumerable.Range(0, intent.Count).Select(hitIndex => SimEngine.IntentDamage(pred, enemy, hitIndex, Who.You)).ToList()
                    : enemy.Hits;
                if (!enemy.Stunned && !simEnemy.Stunned && !predHits.SequenceEqual(simEnemy.Hits)) differences.Add($"after play enemy intent: {name} predicted [{string.Join(", ", predHits)}], actual [{string.Join(", ", simEnemy.Hits)}]");
            }
            var player = pred.You;
            var simPlayer = after.You;
            void Cmp(string what, int predicted, int actual)
            {
                if (predicted != actual) differences.Add($"after play {what}: predicted {predicted}, actual {actual}");
            }
            Cmp("your block", player.Block, simPlayer.Block);
            if (drawn is { } drawnCount) Cmp("cards drawn", player.Drawn - startP.Drawn, drawnCount);
            // Energy (decision #117): what the play spends and gives back, the card's own included (Pael's Tears reads it).
            Cmp("your energy", player.Energy, simPlayer.Energy);
            Cmp("Plating", player.Plating, simPlayer.Plating);
            Cmp(nameof(Juggernaut), player.Juggernaut, simPlayer.Juggernaut);
            Cmp("Thorns", player.Thorns, simPlayer.Thorns);
            Cmp("Flame Barrier", player.FlameBarrier, simPlayer.FlameBarrier);
            Cmp("Feel No Pain", player.FeelNoPain, simPlayer.FeelNoPain);
            if (!summoned) Cmp("Osty HP", player.OstyHp, simPlayer.OstyHp);
            // A random orb (Chaos) is predicted as Unknown: any orb matches it.
            static string Orb(SimOrb orb) => $"{orb.Kind} {orb.Passive}/{orb.Evoke}";
            var orbsMatch = player.Orbs.Count == simPlayer.Orbs.Count
                            && player.Orbs.Zip(simPlayer.Orbs).All(orbs => orbs.First.Kind == OrbKind.Unknown || Orb(orbs.First) == Orb(orbs.Second));
            if (!orbsMatch) differences.Add($"after play orbs: predicted [{string.Join(", ", player.Orbs.Select(Orb))}], actual [{string.Join(", ", simPlayer.Orbs.Select(Orb))}]");
            // Teammates (multiplayer): the block and HP your play leaves them (Beacon of Hope's share, hits).
            // The state after is read from the living ones only (SimAdapter.IsTeammate), in player order.
            var living = pred.Teammates.Where(teammate => teammate.Hp > 0).ToList();
            if (living.Count == after.Teammates.Count)
            {
                for (var enemyIndex = 0; enemyIndex < living.Count; enemyIndex++)
                {
                    var (nextPlayer, at) = (living[enemyIndex], after.Teammates[enemyIndex]);
                    Cmp($"teammate {enemyIndex + 1} block", nextPlayer.Block, at.Block);
                    Cmp($"teammate {enemyIndex + 1} HP", nextPlayer.Hp, at.Hp);
                    Cmp($"teammate {enemyIndex + 1} energy", nextPlayer.Energy, at.Energy);
                    // Their orbs (Ignition channels into them; full slots evoke their front orb): kinds, front first.
                    if (!nextPlayer.Orbs.Select(orb => orb.Kind).SequenceEqual(at.Orbs.Select(orb => orb.Kind))
                        && !nextPlayer.Orbs.Any(orb => orb.Kind == OrbKind.Unknown))
                        differences.Add($"after play teammate {enemyIndex + 1} orbs: predicted [{string.Join(", ", nextPlayer.Orbs.Select(orb => orb.Kind))}], actual [{string.Join(", ", at.Orbs.Select(orb => orb.Kind))}]");
                }
            }
            else differences.Add($"after play teammates alive: predicted {living.Count}, actual {after.Teammates.Count}");
            return differences;
        }

        var best = branches.Select(Diff).OrderBy(differences => differences.Count).First();
        if (best.Count == 0) return;
        var note = branches.Count > 1 ? $" (closest of {branches.Count} branches)" : "";
        foreach (var difference in best) issues.Add(difference + note);
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
        // Every nested type, at any depth: a local async function (Blossom Shot's ActionToPerform) lives in a
        // closure class with its own state machine inside it.
        var methods = cardType.GetMethods(all).Cast<MethodBase>()
            .Concat(CardCoverage.NestedTypes(cardType).SelectMany(type => type.GetMethods(all)));
        foreach (var method in methods)
        {
            try
            {
                foreach (var ins in SpireQoL.Diagnostics.GameCode.Instructions(method))
                {
                    if (ins.operand is MethodInfo { IsGenericMethod: true, Name: "Apply" } otherMethod && otherMethod.DeclaringType?.Name == "PowerCmd")
                        set.Add(otherMethod.GetGenericArguments()[0]);
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
        foreach (var dynamicVar in card.DynamicVars.Values)
        {
            var type = dynamicVar.GetType();
            if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(MegaCrit.Sts2.Core.Localization.DynamicVars.PowerVar<>))
                set.Add(type.GetGenericArguments()[0]);
        }
        // The Fool's powers the play touches outside PowerCmd.Apply in its code (Fool.PowersTouched): a shot Impales
        // its target and spends Aim and Quietus, attacks count Impaled down (it goes when its last arrow comes
        // loose), block cards spend Stretch.
        foreach (var name in Procs.Fool.PowersTouched(card))
            if (PowerTypeNamed(name) is { } otherType) set.Add(otherType);
        // A temporary stat power applies the stat itself (TemporaryStrengthPower.AfterApplied →
        // PowerCmd.Apply<StrengthPower>): Dark Shackles' -9 Strength.
        foreach (var type in set.ToList())
        {
            if (typeof(TemporaryStrengthPower).IsAssignableFrom(type)) set.Add(typeof(StrengthPower));
            if (typeof(TemporaryDexterityPower).IsAssignableFrom(type)) set.Add(typeof(DexterityPower));
            if (typeof(TemporaryFocusPower).IsAssignableFrom(type)) set.Add(typeof(FocusPower));
            if (type.BaseType?.Name == "TemporaryThornsPower") set.Add(typeof(ThornsPower)); // Cho Barbs (v0.113.0)
        }
        return set;
    }

    private static string SafePotion(PotionUsedEntry potionUsedEntry)
    {
        try { return potionUsedEntry.Potion.GetType().Name; } catch { return "a potion"; }
    }

    private static string SafeTitle(CardModel card)
    {
        try { return card.Title; } catch { return card.GetType().Name; }
    }
}
