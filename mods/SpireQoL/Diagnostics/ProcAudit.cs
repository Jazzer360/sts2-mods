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
        public required int From;
        public readonly List<AbstractModel> Flashed = new();
    }

    private enum Effect { Draw, Exhaust, Discard, Generate, Orb, Summon, Power }

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
    private int _seenAtLastFrame;
    private CombatHistory? _history;
    private readonly HashSet<AbstractModel> _subscribed = new(ReferenceEqualityComparer.Instance);

    // Per-combat tallies for the summary line.
    private int _checked;
    private int _matched;
    private readonly Dictionary<string, int> _issueCounts = new();

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
            _seen = _seenAtLastFrame = history.Entries.Count();
            _open = null;
        }

        Subscribe(me, cs);

        var count = history.Entries.Count(); // list-backed: O(1)
        var i = _seen;
        foreach (var entry in count > _seen ? history.Entries.Skip(_seen) : Enumerable.Empty<CombatHistoryEntry>())
        {
            if (entry is CardPlayStartedEntry { CardPlay: var play } && play.Card.Owner == me && play.IsFirstInSeries)
            {
                Close(me);
                var predicted = _ctx.Decor.Previous.TryGetValue(play.Card, out var tabs) ? tabs : Array.Empty<TabSpec>();
                ProcIndicatorsFeature.PreviousFacts.TryGetValue(play.Card, out var facts);
                // Procs that fire before the play is recorded (BeforeCardPlayed: Intimidating
                // Helmet, Spirit of Ash...) land just before this entry, so the window starts at
                // the previous frame's history size.
                _open = new Window
                {
                    Card = play.Card, CardName = SafeTitle(play.Card), Predicted = predicted, Facts = facts,
                    From = Math.Min(_seenAtLastFrame, i),
                };
            }
            i++;
        }
        _seen = count;

        if (_open != null && !Sts2State.IsPlayerTurn) Close(me);
        _seenAtLastFrame = count;
    }

    private void OnRelicFlashed(RelicModel relic, IEnumerable<Creature> _) => _open?.Flashed.Add(relic);

    private void OnPowerFlashed(PowerModel power) => _open?.Flashed.Add(power);

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
            var issues = Compare(w, me, _history.Entries.Skip(w.From).ToList(), out var model);
            _checked++;
            if (issues.Count == 0) _matched++;
            foreach (var issue in issues)
            {
                var kind = issue.Split(':')[0];
                _issueCounts[kind] = (_issueCounts.TryGetValue(kind, out var n) ? n : 0) + 1;
            }
            var log = ModEntry.Log;
            var tabs = w.Predicted.Count == 0 ? "none" : string.Join(", ", w.Predicted.Select(t => t.Text ?? (t.Amount > 0 ? $"{t.Key} {t.Amount}" : t.Key)));
            if (issues.Count > 0)
            {
                log.Warn($"[audit] played {w.CardName}: {string.Join("; ", issues)}. Tabs were: {tabs}.");
                foreach (var e in _history.Entries.Skip(w.From))
                {
                    if (e is BlockGainedEntry or DamageReceivedEntry or CardPlayStartedEntry or PowerReceivedEntry or CardDrawnEntry or CardGeneratedEntry or OrbChanneledEntry)
                        log.Warn($"[audit]   {e.HumanReadableString}");
                }
            }
            foreach (var m in model) log.Info($"[audit] [model] played {w.CardName}: {m}");
        }
        catch (Exception ex)
        {
            ModEntry.Log.Warn($"[audit] couldn't check {w.CardName}: {ex.Message}");
        }
    }

    private void Summarize()
    {
        if (_checked == 0) return;
        var issues = _issueCounts.Count == 0 ? "" : $" ({string.Join(", ", _issueCounts.OrderByDescending(k => k.Value).Select(k => $"{k.Key} ×{k.Value}"))})";
        ModEntry.Log.Info($"[audit] combat summary: {_checked} card plays checked, {_matched} matched{issues}.");
        _checked = _matched = 0;
        _issueCounts.Clear();
    }

    private static List<string> Compare(Window w, Player me, List<CombatHistoryEntry> entries, out List<string> model)
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
        foreach (var m in w.Flashed.Distinct())
        {
            if (!sourceOf.ContainsKey(m.GetType().Name)) issues.Add($"no tab: {m.GetType().Name} fired");
        }

        // ---- relic tabs whose relic didn't fire (crossed ones mean "this breaks it": no flash)
        foreach (var (name, t) in sourceOf)
        {
            if (t.Crossed || !t.Key.StartsWith("proc:")) continue;
            if (me.Relics.Any(r => r.GetType().Name == name) && !w.Flashed.Any(f => f.GetType().Name == name))
                issues.Add($"didn't fire: {name}");
        }

        // ---- block from procs (exact)
        var procBlock = entries.OfType<BlockGainedEntry>()
            .Where(b => b.Actor == me.Creature && !ReferenceEquals(b.CardPlay?.Card, w.Card))
            .Sum(b => b.Amount);
        var predictedBlock = w.Predicted.Where(t => t.Kind == "block").Sum(t => t.Amount);
        if (procBlock != predictedBlock) issues.Add($"block: predicted {predictedBlock}, actual {procBlock}");

        // ---- damage from procs (no card source), against each tab's range
        var procDamage = entries.OfType<DamageReceivedEntry>().Where(d => d.CardSource == null).ToList();
        int Dealt(DamageReceivedEntry d) => d.Result.TotalDamage + d.Result.OverkillDamage;
        var toEnemies = procDamage.Where(d => d.Result.Receiver.IsEnemy).Sum(Dealt);
        var toMe = procDamage.Where(d => d.Result.Receiver == me.Creature).Sum(Dealt);
        var enemyTabs = w.Predicted.Where(t => t.Kind == "damage" && !t.Crossed).ToList();
        var meTabs = w.Predicted.Where(t => t.Kind == "damage" && t.Crossed).ToList();
        var (eMin, eMax) = (enemyTabs.Sum(t => t.AuditMin), enemyTabs.Sum(t => t.AuditMax));
        var (mMin, mMax) = (meTabs.Sum(t => t.AuditMin), meTabs.Sum(t => t.AuditMax));
        if (toEnemies < eMin || toEnemies > eMax) issues.Add($"damage to enemies: predicted {Range(eMin, eMax)}, actual {toEnemies}");
        if (toMe < mMin || toMe > mMax) issues.Add($"damage to you: predicted {Range(mMin, mMax)}, actual {toMe}");

        // ---- budget: everything else the play caused
        var f = w.Facts;
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
            .Select(p => p.Power.GetType().Name)
            .Distinct()
            .ToList();
        if (foreign.Count > 0 && !Explained(Effect.Power)) issues.Add($"unexplained: powers applied ({string.Join(", ", foreign)})");

        return issues;
    }

    private static string Range(int min, int max) => min == max ? $"{min}" : $"{min}–{max}";

    private static HashSet<Type> OwnPowerTypes(CardModel card)
    {
        var set = new HashSet<Type>();
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
