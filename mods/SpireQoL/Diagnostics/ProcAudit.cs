using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Combat.History;
using MegaCrit.Sts2.Core.Combat.History.Entries;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.Models;
using SpireQoL.Cards;
using SpireQoL.Features;
using SpireQoL.Procs;
using Sts2Mods.Core.Features;
using Sts2Mods.Core.Game;
using Sts2Mods.Core.Ui;

namespace SpireQoL.Diagnostics;

/// <summary>
/// Checks every card play against what the mod predicted, and logs mismatches to the game log
/// (lines start with [SpireQoL] [audit]), so missing or wrong tag rules surface from real play.
///
/// For each card you play (first play of its series, from the game's combat history), the window
/// runs from just before the play is recorded (BeforeCardPlayed procs land first) until your next
/// card play or the end of your turn. Compared:
///  - Fired with no tab: a relic or power that flashed (RelicModel/PowerModel.Flashed).
///  - Relic tab that didn't fire: a predicted, uncrossed relic tab whose relic never flashed.
///  - Proc block: block you gained from anything but the card itself vs the block tabs (exact).
///  - Proc damage: damage with no card source vs the damage tabs' audit range (each target's own
///    modifiers applied), separately for damage to enemies and to you.
///  - Budget: draws, exhausts, discards, created cards, orbs, summons and powers applied, actual vs
///    what the card itself does (PlayFacts) plus what predicted tabs can add. More than that with no
///    tab that explains it = "unexplained" (a missing tag). Less than the card's own = a "[model]"
///    line (our model of the card is off; often fine, e.g. an empty draw pile).
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
    }

    public void Attach(OverlayHost host)
    {
    }

    private sealed class Window
    {
        public required CardModel Card;
        public required string CardName;
        public required IReadOnlyList<TabSpec> Predicted;
        public required PlayFacts? Facts;
        public required int From { get; set; }
        /// <summary>Earliest possible start; when below From, Close picks the best fit in between.</summary>
        public int FromMin = -1;
        /// <summary>History index of this play's own CardPlayStartedEntry.</summary>
        public required int Start;
        /// <summary>Exclusive end (the next play's window start), or -1 for "up to now".</summary>
        public int To = -1;
        /// <summary>Game state when the play started (before its effects), for reports.</summary>
        public IReadOnlyDictionary<string, string>? Before;
        /// <summary>Frames between the card's last frame in hand and its play starting.</summary>
        public long Lag;
        /// <summary>How the window's start was chosen (for reports).</summary>
        public string Split = "";
    }

    private enum Effect { Draw, Exhaust, Discard, Generate, Orb, Summon, Power }

    /// <summary>
    /// Powers that flash when they change damage (caps), not because a play set them off. The
    /// card's damage already reflects them, so they never get a tab.
    /// </summary>
    private static readonly HashSet<string> Modifiers = new() { "HardToKillPower", "HardenedShellPower" };

    /// <summary>What a trigger tab's source can add beyond the card itself (for the budget check).</summary>
    private static readonly Dictionary<string, Effect[]> SourceEffects = new()
    {
        // draws
        ["DarkEmbracePower"] = new[] { Effect.Draw }, ["GamePiece"] = new[] { Effect.Draw }, ["IronClub"] = new[] { Effect.Draw },
        ["GremlinHorn"] = new[] { Effect.Draw }, ["CentennialPuzzle"] = new[] { Effect.Draw }, ["ViciousPower"] = new[] { Effect.Draw },
        ["JossPaper"] = new[] { Effect.Draw }, ["UnceasingTop"] = new[] { Effect.Draw }, ["CorrosiveWavePower"] = new[] { Effect.Power },
        // created cards
        ["CalamityPower"] = new[] { Effect.Generate }, ["JugglingPower"] = new[] { Effect.Generate }, ["MusicBox"] = new[] { Effect.Generate },
        ["BurningSticks"] = new[] { Effect.Generate }, ["PersonalHivePower"] = new[] { Effect.Generate }, ["WitheringPresencePower"] = new[] { Effect.Generate },
        ["ImitationLearningPower"] = new[] { Effect.Generate, Effect.Power },
        // orbs
        ["StormPower"] = new[] { Effect.Orb }, ["TrashToTreasurePower"] = new[] { Effect.Orb },
        // summons
        ["DevourLifePower"] = new[] { Effect.Summon },
        // powers
        ["MonologuePower"] = new[] { Effect.Power }, ["Kunai"] = new[] { Effect.Power }, ["Shuriken"] = new[] { Effect.Power },
        ["HelicalDart"] = new[] { Effect.Power }, ["RainbowRing"] = new[] { Effect.Power }, ["MiniRegent"] = new[] { Effect.Power },
        ["ArsenalPower"] = new[] { Effect.Power }, ["RupturePower"] = new[] { Effect.Power }, ["EnvenomPower"] = new[] { Effect.Power },
        ["ConcoctPower"] = new[] { Effect.Power }, ["ReaperFormPower"] = new[] { Effect.Power }, ["MonarchsGazePower"] = new[] { Effect.Power },
        ["OblivionPower"] = new[] { Effect.Power }, ["TenderPower"] = new[] { Effect.Power }, ["EnragePower"] = new[] { Effect.Power },
        ["HandDrill"] = new[] { Effect.Power }, ["UnsettlingLamp"] = new[] { Effect.Power }, ["SelfFormingClay"] = new[] { Effect.Power },
        ["RedSkull"] = new[] { Effect.Power }, ["CrabRagePower"] = new[] { Effect.Power }, ["RavenousPower"] = new[] { Effect.Power },
        ["SlowPower"] = new[] { Effect.Power }, ["SmoggyPower"] = new[] { Effect.Power }, ["VitalSparkPower"] = new[] { Effect.Power },
        ["SleightOfFleshPower"] = new[] { Effect.Power }, ["MasterPlannerPower"] = new[] { Effect.Power }, ["PaleBlueDotPower"] = new[] { Effect.Power },
    };

    private Window? _open;
    private int _seen;
    private CombatHistory? _history;
    private readonly HashSet<AbstractModel> _subscribed = new(ReferenceEqualityComparer.Instance);

    // Per-combat tallies for the summary line.
    private int _checked;
    private int _matched;
    private readonly Dictionary<string, int> _issueCounts = new();

    // Every play this turn (card, its tabs, result), sent with each report: the turn is the unit
    // that explains most mismatches (counters, "first this turn" relics, queued plays).
    private readonly List<string> _turnPlays = new();
    private int _turnPlaysTurn = -1;

    public void OnFrame(OverlayHost host)
    {
        var me = Sts2State.Me;
        var cs = Sts2State.Combat;
        if (me == null || cs == null)
        {
            Close(null);
            Summarize();
            _history = null;
            return;
        }

        var history = CombatManager.Instance.History;
        if (!ReferenceEquals(history, _history))
        {
            Summarize();
            _history = history;
            _seen = history.Entries.Count();
            _open = null;
            _flashes.Clear();
        }

        Subscribe(me, cs);
        if (!ReferenceEquals(_endTurnSource, CombatManager.Instance))
        {
            _endTurnSource = CombatManager.Instance;
            CombatManager.Instance.PlayerEndedTurn += OnPlayerEndedTurn;
        }

        var count = history.Entries.Count(); // list-backed: O(1)
        var list = count > _seen ? history.Entries.ToList() : null;
        for (var i = _seen; list != null && i < count; i++)
        {
            // Auto-plays (Catastrophe, Mayhem...) belong to the window of whatever played them.
            if (list[i] is CardPlayStartedEntry { CardPlay: var play } && play.Card.Owner == me && play.IsFirstInSeries && !play.IsAutoPlay)
            {
                var from = SplitBefore(list, i, me, out var split, out var fromMin);
                if (_open != null) _open.To = from;
                Close(me);
                // The tabs it had when it was last in your hand: a play can start several frames
                // after the card leaves the hand (the action queue was busy).
                var predicted = _ctx.Decor.LastInHand.TryGetValue(play.Card, out var tabs) ? tabs : Array.Empty<TabSpec>();
                ProcIndicatorsFeature.LastFacts.TryGetValue(play.Card, out var facts);
                _open = new Window
                {
                    Card = play.Card, CardName = SafeTitle(play.Card), Predicted = predicted, Facts = facts,
                    From = from, FromMin = fromMin, Start = i, Split = split,
                    Lag = _ctx.Decor.LastSeenFrame.TryGetValue(play.Card, out var seen) ? _ctx.Decor.Frame - seen : -1,
                    Before = CaptureBefore(),
                };
            }
        }
        _seen = count;

        if (_open != null && !Sts2State.IsPlayerTurn)
        {
            _open.To = count;
            Close(me);
        }
    }

    // Flashes with the history size when they happened, so they're split between plays the same
    // way history entries are.
    private readonly List<(int At, AbstractModel Model)> _flashes = new();

    // End turn: the last play's window ends here, before end-of-turn effects (orb passives,
    // Plating, Ethereal exhausts...), which otherwise land in it.
    private CombatManager? _endTurnSource;

    private void OnPlayerEndedTurn(Player player, bool _)
    {
        try
        {
            if (_open == null || _history == null || player != Sts2State.Me) return;
            _open.To = _history.Entries.Count();
            Close(player);
        }
        catch (Exception ex)
        {
            ModEntry.Log.Warn($"[audit] end-turn close failed: {ex.Message}");
        }
    }

    private void OnRelicFlashed(RelicModel relic, IEnumerable<Creature> _) => Flash(relic);

    private void OnPowerFlashed(PowerModel power) => Flash(power);

    private void Flash(AbstractModel m)
    {
        if (_history == null) return;
        _flashes.Add((_history.Entries.Count(), m));
        if (_flashes.Count > 400) _flashes.RemoveRange(0, 200);
    }

    /// <summary>
    /// Where the play starting at history index <paramref name="start"/> begins. A play runs:
    /// energy/stars spent → "before play" procs (Intimidating Helmet, Spirit of Ash, Pen Nib...) →
    /// play started → the card → play finished → "after play" procs (counters, Tuning Fork...).
    /// Between the previous play's finish and this start, the previous card's after-play procs come
    /// first and this card's before-play procs last. Spending energy or stars marks the split
    /// exactly; for a 0-cost card the split is the point that best fits the previous card's tabs.
    /// </summary>
    private int SplitBefore(List<CombatHistoryEntry> list, int start, Player me, out string how, out int fromMin)
    {
        how = "";
        fromMin = -1;
        var floor = _open != null ? _open.Start + 1 : 0;
        var gapStart = start;
        while (gapStart > floor && list[gapStart - 1] is not (CardPlayFinishedEntry or CardPlayStartedEntry or MonsterPerformedMoveEntry))
            gapStart--;

        for (var k = gapStart; k < start; k++)
        {
            if (list[k].Actor == me.Creature && list[k] is EnergySpentEntry or StarsModifiedEntry { Amount: < 0 })
            {
                how = $"at its energy/star spend (gap {gapStart}..{start})";
                return k;
            }
        }

        if (_open == null)
        {
            // First play this turn with no cost (Snecko Eye, free cards): the gap holds
            // start-of-turn effects, then this card's before-play procs, with nothing marking the
            // split. Decided when the play is checked: the start that best fits its own tabs.
            how = $"first play, no cost: best fit in gap {gapStart}..{start}";
            fromMin = gapStart;
            return start;
        }

        // 0-cost card after another play: try each split, keep the one where the previous card's
        // window fits its tabs best (ties: give the gap to the previous card).
        var best = start;
        var bestIssues = int.MaxValue;
        for (var k = start; k >= gapStart; k--)
        {
            try
            {
                var issues = Compare(_open, me, list.GetRange(_open.From, Math.Max(0, k - _open.From)), FlashesIn(_open.From, k), out _).Count;
                if (issues < bestIssues)
                {
                    bestIssues = issues;
                    best = k;
                }
            }
            catch
            {
                // keep the current best
            }
        }
        how = $"0-cost after another play: best fit at {best} (gap {gapStart}..{start})";
        return best;
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

    private List<AbstractModel> FlashesIn(int from, int to)
        => _flashes.Where(x => x.At > from && x.At <= to).Select(x => x.Model).ToList();

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

    private void Close(Player? me)
    {
        var w = _open;
        _open = null;
        if (w == null || me == null || _history == null) return;

        try
        {
            var hist = _history.Entries.ToList();
            var to = w.To >= 0 ? Math.Min(w.To, hist.Count) : hist.Count;
            // A potion used during the window isn't the card's: the window ends there.
            for (var k = w.Start + 1; k < to; k++)
            {
                if (hist[k] is PotionUsedEntry)
                {
                    to = k;
                    break;
                }
            }
            if (w.FromMin >= 0 && w.FromMin < w.From)
            {
                var best = w.From;
                var bestIssues = int.MaxValue;
                for (var k = w.From; k >= w.FromMin; k--)
                {
                    try
                    {
                        var n = Compare(w, me, hist.GetRange(k, to - k), FlashesIn(k, to), out _).Count;
                        if (n < bestIssues)
                        {
                            bestIssues = n;
                            best = k;
                        }
                    }
                    catch
                    {
                        // keep the current best
                    }
                }
                w.From = best;
                w.Split += $": chose {best}";
            }
            var entries = hist.GetRange(w.From, Math.Max(0, to - w.From));
            w.To = to;
            var issues = Compare(w, me, entries, FlashesIn(w.From, to), out var model);
            _checked++;
            if (issues.Count == 0) _matched++;
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
                var lines = new List<string>();
                foreach (var e in entries)
                {
                    if (e is BlockGainedEntry or DamageReceivedEntry or CardPlayStartedEntry or PowerReceivedEntry or CardDrawnEntry or CardGeneratedEntry or OrbChanneledEntry)
                    {
                        log.Warn($"[audit]   {e.HumanReadableString}");
                        lines.Add(e.HumanReadableString);
                    }
                }
                Report(w, issues, tabs, lines, entries.Count, string.Join("\n", _turnPlays));
            }
            foreach (var m in model) log.Info($"[audit] [model] played {w.CardName}: {m}");
        }
        catch (Exception ex)
        {
            ModEntry.Log.Warn($"[audit] couldn't check {w.CardName}: {ex.Message}");
        }
    }

    /// <summary>
    /// One Sentry report per issue, grouped by what's wrong rather than by amounts: a missing or
    /// silent source groups by that source; block, damage and budget issues group by the card.
    /// </summary>
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
            var lag = w.Lag < 0 ? "never seen in hand" : $"{w.Lag} frame(s)";
            reporter.Mismatch("proc", $"Proc audit: {issue} (played {card})", fp,
                $"Played {w.CardName} ({card}).\nIssue: {issue}\nAll issues this play: {string.Join("; ", issues)}\nTabs before the play: {tabs}" +
                $"\nFrom the card's last frame in hand to its play starting: {lag}" +
                $"\nWindow: history {w.From}..{(w.To >= 0 ? w.To : w.From + windowSize)} ({windowSize} entries), play started at {w.Start}; start chosen {w.Split}",
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
        ModEntry.Log.Info($"[audit] combat summary: {_checked} card plays checked, {_matched} matched{issues}.");
        _checked = _matched = 0;
        _issueCounts.Clear();
    }

    private static List<string> Compare(Window w, Player me, List<CombatHistoryEntry> entries, List<AbstractModel> flashed, out List<string> model)
    {
        var issues = new List<string>();
        var modelLines = new List<string>();
        model = modelLines;
        var sourceOf = new Dictionary<string, TabSpec>();
        foreach (var t in w.Predicted)
        {
            var colon = t.Key.IndexOf(':');
            if (colon > 0) sourceOf[t.Key[(colon + 1)..]] = t;
        }

        // ---- sources that fired with no tab (missing rules)
        foreach (var m in flashed.Distinct())
        {
            if (!sourceOf.ContainsKey(m.GetType().Name) && !Modifiers.Contains(m.GetType().Name)) issues.Add($"no tab: {m.GetType().Name} fired");
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
        if (procBlock != predictedBlock) issues.Add($"block: predicted {predictedBlock}, actual {procBlock}");

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
        if (toEnemies < eMin || toEnemies > eMax) issues.Add($"damage to enemies: predicted {Range(eMin, eMax)}, actual {toEnemies}");
        if (toMe < mMin || toMe > mMax) issues.Add($"damage to you: predicted {Range(mMin, mMax)}, actual {toMe}");

        // ---- budget: everything else the play caused
        var autoPlayed = entries.OfType<CardPlayStartedEntry>().Any(e => e.CardPlay.IsAutoPlay && e.CardPlay.Card.Owner == me);
        if (autoPlayed) modelLines.Add("auto-played cards (random, so not predicted): budget not checked");
        var f = autoPlayed ? null : w.Facts;
        bool Explained(Effect e) => sourceOf.Keys.Any(s => SourceEffects.TryGetValue(s, out var fx) && fx.Contains(e));
        void Budget(Effect e, string what, int actual, int own)
        {
            if (actual > own && !Explained(e)) issues.Add($"unexplained: {actual - own} more {what} than the card's own {own}");
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
            Budget(Effect.Draw, "draws", draws, f.Draws);
            Budget(Effect.Exhaust, "exhausts", exhausts, f.TotalExhausts);
            Budget(Effect.Discard, "discards", discards, f.Discards);
            Budget(Effect.Generate, "created cards", generated, f.Generated);
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
        if (foreign.Count > 0 && !autoPlayed && !Explained(Effect.Power)) issues.Add($"unexplained: powers applied ({string.Join(", ", foreign)})");

        return issues;
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
        return set;
    }

    private static string SafeTitle(CardModel c)
    {
        try { return c.Title; } catch { return c.GetType().Name; }
    }
}
