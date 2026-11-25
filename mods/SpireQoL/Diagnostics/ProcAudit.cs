using SpireQoL.Combat;
using System.Reflection;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat.History;
using MegaCrit.Sts2.Core.Combat.History.Entries;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using SpireQoL.Cards;
using SpireQoL.Sim;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using Sts2Mods.Core.Features;
using Sts2Mods.Core.Ui;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.Models.Relics;

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
///    what the card itself does (CardPrediction) plus what predicted tabs can add. More than that with no
///    tab that explains it = "unexplained" (a missing tag). Less than the card's own = a "[model]"
///    line (our model of the card is off; often fine, e.g. an empty draw pile).
///  - Shuffle: whether the play shuffled your discard pile into your draw pile (seen from the piles'
///    events: the game keeps no history entry for it) against its simulation's.
///  - State after the play: once the play has fully resolved (the game's action queue is idle), the
///    simulator's state is rebuilt from the game and compared with the simulator's own prediction
///    of the play (the same start state and play behind the card's lethal, Imbalanced and wake
///    tabs): enemy HP, block, poison and intents; your block, Plating, Juggernaut, Thorns, Flame
///    Barrier, Feel No Pain, orbs and Osty. Random targets are branches: reported only when no
///    branch matches. A mismatch means those tabs were computed from the wrong state.
/// A one-line summary per combat gives the match rate. Read-only: events and history only.
/// </summary>
public sealed partial class ProcAudit : IFeature, IFrameFeature
{
    private readonly QolContext _ctx;

    public ProcAudit(QolContext ctx) => _ctx = ctx;

    public string Id => "proc-audit";
    public string DisplayName => "Play Audit (log only)";
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
        public required CardPrediction? Prediction;
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
        /// <summary>The end of turn searched from <see cref="After"/>, once, for the lethal and preview checks (ProcAudit.PostPlay).</summary>
        public SimOutcome? PostPlay;
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
        public PlaySnapshot? FullBefore, FullAfter;
        /// <summary>Waterfall Giants already about to blow when the play began; null when the play's start wasn't seen.</summary>
        public HashSet<Creature>? BlowingBefore;
        /// <summary>The play ended the fight: the enemies it was simulated against that were dead as it ended (it killed them).</summary>
        public HashSet<Creature>? DeadAtFightEnd;
    }

    internal enum Effect { Draw, Exhaust, Discard, Generate, Orb, Summon, Power }

    /// <summary>
    /// Powers that flash when they change damage (caps), not because a play set them off: their code caps a damage or the HP it takes
    /// (ModifyDamageCap, an HP-loss pass) and flashes as it does (AfterModifyingDamageAmount, AfterModifyingHpLost*): Hard to Kill, Intangible,
    /// Hardened Shell. The card's damage already reflects them, so they never get a tab.
    /// </summary>
    internal static bool Modifier(Type type) => typeof(PowerModel).IsAssignableFrom(type) && CodeIndex.Does(type, Array.Empty<string>(), CapHooks)
        && CodeIndex.Does(type, Array.Empty<string>(), new[] { "AfterModifyingDamageAmount", "AfterModifyingHpLostBeforeOsty", "AfterModifyingHpLostAfterOsty" });

    /// <summary>The hooks that cap a damage or the HP it takes (Hook.ModifyDamage's cap, the HP-loss passes).</summary>
    private static readonly string[] CapHooks = { "ModifyDamageCap", "ModifyHpLostBeforeOsty", "ModifyHpLostBeforeOstyLate", "ModifyHpLostAfterOsty", "ModifyHpLostAfterOstyLate" };

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
        if (window.Prediction is not { Sim: { } sim } prediction || prediction.SimIncomplete || prediction.SimPending || prediction.Unmodelled.Count > 0) return null;
        return sim.AimFor(window.Target);
    }

    /// <summary>
    /// Whether some outcome of the play set this source off (a roll decided it): true or false when the simulator models
    /// it with a tab and the play's simulations were complete; null when that can't be told (then the old excuses stand).
    /// </summary>
    private static bool? SimHadIt(Window window, AbstractModel model)
    {
        if (CheckedAim(window) is not { } aim || window.Prediction?.FiresAnyByAim?.TryGetValue(aim, out var any) != true) return null;
        if (SimModels.Get(SimReaders.ModelName(model))?.Tab == null) return null;
        return any!.Contains(model.GetType());
    }

    /// <summary>Sources whose tabs come from the outcome simulator (missing while its run was pending).</summary>
    private static readonly HashSet<string> SimTabSources =
        SimModels.Names.Where(name => SimModels.Get(name) is { } model && (model.Tab != null || model.JudgedOnOutcome)).ToHashSet();


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
    /// Your powers a card play uses up whatever the card costs (BeforeCardPlayed): Free Attack, Free Skill and Free Power make
    /// your next card (Attack, Skill, Power) from hand free and go, and the costs they lowered go back up. The free-card check
    /// compares their charges with the "free:" tabs.
    /// </summary>
    private static readonly HashSet<string> SpentByAnyPlay = new(SpireQoL.Procs.FreeCards.Powers);

    /// <summary>
    /// Your powers an attack uses up (BeforeAttack marks the command, AfterAttack spends it: Vigor, Gigantification), read from their code (their
    /// AfterAttack takes their own stacks: HookReader.SpentByAnAttack). One a card's play count spends (One-Two Punch) is the play-count check's.
    /// </summary>
    internal static bool SpentByAttack(string type) => SimModels.Get(type) is ReadModel { Spec: var spec } && HookReader.SpentByAnAttack(spec);

    /// <summary>Your stats a source that changes powers can take away (Tender: 1 Strength and Dexterity per card).</summary>
    private static readonly HashSet<string> Stats = new() { nameof(StrengthPower), nameof(DexterityPower), nameof(FocusPower) };

    /// <summary>
    /// What a trigger tab's source can add beyond the card itself (for the budget check), from its class's own code (CodeIndex: the commands it
    /// calls, the hooks it overrides). A source the simulator models has every card event and channel it makes in the play's own counts (its
    /// simulation's log: SimKeys.PlayEvent, PlayOrbChanneled), so it explains none of those; it explains the powers it applies or changes (the
    /// power check holds a play to the card's own powers) and the summons it makes (the summon check to the card's own Summon). A source the
    /// simulator doesn't model explains everything its code does; one whose code plays cards (CardCmd.AutoPlay, CardPileCmd.AutoPlayFromDrawPile:
    /// Lost Lute, Imitation Learning) explains whatever they do.
    /// </summary>
    internal static IReadOnlyCollection<Effect> EffectsOf(string source) => SourceEffects.GetOrAdd(source, name =>
    {
        if (ModelTypeNamed(name) is not { } type) return Array.Empty<Effect>();
        if (CodeIndex.Does(type, AutoPlays, Array.Empty<string>())) return Enum.GetValues<Effect>();
        var coded = EffectCode.Where(effect => CodeIndex.Does(type, effect.Calls, effect.Hooks)).Select(effect => effect.Effect);
        if (SimModels.Has(name)) coded = coded.Where(effect => effect is Effect.Power or Effect.Summon);
        return coded.ToArray();
    });

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, IReadOnlyCollection<Effect>> SourceEffects = new(StringComparer.Ordinal);

    /// <summary>The game's commands that play cards: a source calling one does what the cards it plays do.</summary>
    private static readonly string[] AutoPlays = { "CardCmd.AutoPlay", "CardPileCmd.AutoPlayFromDrawPile" };

    /// <summary>The game's commands (and hooks) each effect comes from.</summary>
    private static readonly (Effect Effect, string[] Calls, string[] Hooks)[] EffectCode =
    {
        (Effect.Draw, new[] { "CardPileCmd.Draw" }, Array.Empty<string>()),
        (Effect.Exhaust, new[] { "CardCmd.Exhaust" }, Array.Empty<string>()),
        (Effect.Discard, new[] { "CardCmd.Discard*" }, Array.Empty<string>()),
        (Effect.Generate, new[] { "CardPileCmd.AddGenerated*", "CardPileCmd.AddToCombat*" }, Array.Empty<string>()),
        (Effect.Orb, new[] { "OrbCmd.Channel*" }, Array.Empty<string>()),
        (Effect.Summon, new[] { "OstyCmd.Summon" }, Array.Empty<string>()),
        (Effect.Power, new[] { "PowerCmd.Apply*", "PowerCmd.ModifyAmount" }, new[] { "ModifyPowerAmountGiven", "ModifyPowerAmountGivenMultiplicative", "ModifyPowerAmountReceived" }),
    };

    /// <summary>The game's power or relic class with this name.</summary>
    private static Type? ModelTypeNamed(string name) => PowerTypeNamed(name) ?? typeof(Kunai).Assembly.GetType(typeof(Kunai).Namespace + "." + name);

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
