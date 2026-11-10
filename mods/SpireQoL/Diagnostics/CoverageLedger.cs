using System.Reflection;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Models;
using Sts2Mods.Core;
using Sts2Mods.Core.Features;
using Sts2Mods.Core.Game;
using Sts2Mods.Core.Ui;

namespace SpireQoL.Diagnostics;

/// <summary>
/// Coverage of the game's combat mechanics: every game class (relic, power, card, monster, orb,
/// potion, enchantment, affliction) that overrides a combat hook (CoverageHooks.txt) must have a rule
/// in the mod, a reviewed reason it doesn't matter to any prediction, or a known-gap note.
///
/// The ledger (CoverageLedger.Data.cs, generated from the decompile by scripts/coverage-ledger.mjs)
/// records each class's hooks for the game version it was made from. When the mod loads, the running
/// game's classes are compared with it (reflection): classes that are new, whose hooks changed, or
/// that nobody reviewed are "unexpected". The first time an unexpected class shows up in a fight
/// (a relic you have, a power on anyone, a card in your hand, an enemy, an orb, an enchantment or
/// affliction on a card) it's logged and reported once. Known gaps are logged, not reported.
/// Card code (draws, channels, block, poison, exhausts, discards, created cards) is checked too:
/// see CardCoverage.
/// </summary>
internal static partial class CoverageLedger
{
    public enum Status { Covered, Ignored, Gap, Unreviewed, New, Changed }

    public sealed record Entry(string Type, Status Status, string Note, string Hooks);

    /// <summary>
    /// Classes reviewed without a rule: "ignore: why it can't change a prediction" or "gap: what isn't
    /// modelled yet" (logged when present; the tracker lists them). Everything the mod names in its
    /// code counts as covered (the generator marks it).
    /// </summary>
    private static readonly Dictionary<string, string> Reviewed = new()
    {
        // ---- relics
        ["BingBong"] = "gap: creates cards when cards change piles (created-card counts)",
        ["BookOfFiveRings"] = "ignore: heals you (predictions are about HP lost)",
        ["BookRepairKnife"] = "ignore: heals you",
        ["DarkstonePeriapt"] = "ignore: max HP",
        ["FurCoat"] = "ignore: sets enemy HP as they enter combat (read live)",
        ["GhostSeed"] = "ignore: adds a keyword to cards entering combat (read from the card)",
        ["HistoryCourse"] = "ignore: auto-plays at the start of your turn",
        ["LavaLamp"] = "ignore: upgrades cards (read from the card)",
        ["LizardTail"] = "ignore: prevents your death (not predicted)",
        ["LuckyFysh"] = "ignore: gold",
        ["LunarPastry"] = "gap: stars at the end of your turn (Black Hole's end-of-turn damage isn't simulated)",
        ["MeatOnTheBone"] = "ignore: heals you",
        ["PhilosophersStone"] = "ignore: Strength to enemies entering combat (intents read live)",
        ["SturdyClamp"] = "ignore: keeps your block into your next turn",
        ["TheBoot"] = "gap: your attacks lose an enemy at least its minimum HP (not in the simulator's damage)",
        ["WhisperingEarring"] = "ignore: auto-plays at the start of your turn",
        // ---- powers
        ["AdaptablePower"] = "gap: revives (a kill may not stick: lethal can be wrong)",
        ["BattlewornDummyTimeLimitPower"] = "ignore: the dummy escapes",
        ["BeaconOfHopePower"] = "ignore: shares your block with other players",
        ["BlockNextTurnPower"] = "ignore: block next turn",
        ["BlurPower"] = "ignore: keeps block into your next turn",
        ["BorrowedTimePower"] = "ignore: expires",
        ["ColossusPower"] = "ignore: duration (its effect is in the game's damage math)",
        ["ConfusedPower"] = "ignore: random costs on draw (costs read live)",
        ["CoveredPower"] = "gap: an ally intercepts attacks for it (single-target hits may land elsewhere)",
        ["DampenPower"] = "ignore: downgrades cards (read from the card)",
        ["DoubleDamagePower"] = "ignore: duration (damage math)",
        ["EscapeArtistPower"] = "ignore: duration",
        ["FlankingPower"] = "ignore: expires (damage math)",
        ["FrailPower"] = "ignore: duration (block math)",
        ["FreeAttackPower"] = "ignore: free cards (cost read live)",
        ["FreePowerPower"] = "ignore: free cards (cost read live)",
        ["FreeSkillPower"] = "ignore: free cards (cost read live)",
        ["GigantificationPower"] = "ignore: its damage boost is in the game's damage math",
        ["GuardedPower"] = "ignore: removed on death",
        ["HatchPower"] = "ignore: counts down to hatching (a later turn)",
        ["HeistPower"] = "ignore: steals on its own death",
        ["HexPower"] = "ignore: afflicts cards (read from the card)",
        ["HighVoltagePower"] = "ignore: Strength at the end of its own turn (later attacks)",
        ["IllusionPower"] = "gap: revives (a kill may not stick: lethal can be wrong)",
        ["InfernoPower"] = "gap: damage to all enemies when you lose HP on your turn",
        ["InterceptPower"] = "gap: takes attacks aimed at an ally (single-target hits may land elsewhere)",
        ["KnockdownPower"] = "ignore: expires",
        ["MayhemPower"] = "ignore: auto-plays at the start of your turn",
        ["NecroMasteryPower"] = "gap: damage when Osty's HP changes",
        ["NemesisPower"] = "ignore: Intangible at the end of its own turn (your next turn)",
        ["NoBlockPower"] = "ignore: duration (block math)",
        ["NoEnergyGainPower"] = "ignore: energy",
        ["NostalgiaPower"] = "gap: played cards go somewhere else (pile counts for shuffles)",
        ["PainfulStabsPower"] = "ignore: Wounds into your deck during its attack",
        ["PaperCutsPower"] = "ignore: max HP",
        ["PhantomBladesPower"] = "ignore: keywords on cards (read from the card)",
        ["PossessSpeedPower"] = "ignore: returns Dexterity when it dies",
        ["PossessStrengthPower"] = "ignore: returns Strength when it dies",
        ["ReattachPower"] = "gap: revives (a kill may not stick: lethal can be wrong)",
        ["ReboundPower"] = "gap: played cards go back on your draw pile (pile counts for shuffles)",
        ["RegenPower"] = "ignore: heals",
        ["RetainHandPower"] = "ignore: retain",
        ["RingingPower"] = "ignore: limits plays (read live through CanPlay)",
        ["RitualPower"] = "ignore: Strength at the end of its own turn (later attacks)",
        ["SandpitPower"] = "gap: counts down to killing a player or Osty at the end of the turn",
        ["SelfFormingClayPower"] = "ignore: block next turn",
        ["ShadowmeldPower"] = "ignore: expires",
        ["ShrinkPower"] = "ignore: duration (damage math)",
        ["SicEmPower"] = "ignore: expires",
        ["SlothPower"] = "ignore: limits plays (read live through CanPlay)",
        ["SoulboundPower"] = "gap: creates cards when cards are created (created-card counts)",
        ["StampedePower"] = "gap: auto-plays Attacks at the end of your turn (end-of-turn damage)",
        ["StockPower"] = "gap: spawns enemies when one dies",
        ["SuckPower"] = "ignore: Strength after its own attack (later attacks)",
        ["SurroundedPower"] = "ignore: facing for back attacks (damage math)",
        ["SwipePower"] = "ignore: takes a card from your deck",
        ["TaintedPower"] = "ignore: expires",
        ["TangledPower"] = "ignore: afflicts cards (read from the card)",
        ["TemporaryDexterityPower"] = "ignore: expires after your end-of-turn block",
        ["TemporaryFocusPower"] = "ignore: expires after your orbs' end-of-turn passives",
        ["TemporaryStrengthPower"] = "ignore: expires at the end of the turn (the simulator applies the change)",
        ["TerritorialPower"] = "ignore: Strength at the end of its own turn (later attacks)",
        ["ThunderPower"] = "gap: extra damage when Lightning is evoked",
        ["ToricToughnessPower"] = "ignore: block after its block clears (its own turn)",
        ["UnderworldPower"] = "gap: Doom (Doom kills aren't simulated)",
        ["VeilpiercerPower"] = "ignore: duration",
        ["VigorPower"] = "ignore: its damage is in the game's damage math",
        ["VoidFormPower"] = "ignore: costs (read live)",
        // ---- cards
        ["BansheesCry"] = "ignore: its own cost (read from the card)",
        ["Bombardment"] = "ignore: auto-plays at the start of your turn",
        ["Enthralled"] = "ignore: playability (CanPlay)",
        ["HowlFromBeyond"] = "gap: auto-plays at the end of your turn (end-of-turn damage)",
        ["IAmInvincible"] = "gap: auto-plays at the end of your turn (end-of-turn block)",
        ["KinglyKick"] = "ignore: its own cost on draw (read from the card)",
        ["KinglyPunch"] = "ignore: its own cost on draw (read from the card)",
        ["MakeItSo"] = "gap: comes back after enough plays (created-card and hand counts)",
        ["Melancholy"] = "ignore: its own cost when an enemy dies (read from the card)",
        ["Midnight"] = "ignore: its own cost (read from the card)",
        ["Normality"] = "ignore: playability (CanPlay)",
        ["Pinpoint"] = "ignore: its own cost (read from the card)",
        ["Stomp"] = "ignore: its own cost (read from the card)",
        ["Void"] = "ignore: energy loss on a random draw",
        // ---- monsters
        ["Aeonglass"] = "gap: monster mechanic on death or damage",
        ["Crusher"] = "gap: monster mechanic on death or HP change",
        ["DecimillipedeSegment"] = "gap: segments revive (a kill may not stick)",
        ["KinPriest"] = "gap: monster mechanic on death",
        ["LagavulinMatriarch"] = "gap: monster mechanic on damage or death",
        ["Queen"] = "gap: monster mechanic on death",
        ["Rocket"] = "gap: monster mechanic on death or HP change",
        ["SoulFysh"] = "gap: monster mechanic on death",
        ["TestSubject"] = "gap: revives in phases (a kill may not stick)",
        ["TheInsatiable"] = "gap: monster mechanic on death",
        ["Vantom"] = "gap: monster mechanic on death",
        ["WaterfallGiant"] = "gap: monster mechanic on death",
        // ---- potions, enchantments, afflictions
        ["FairyInABottle"] = "ignore: prevents your death",
        ["Glam"] = "gap: enchantment effect on play (not predicted)",
        ["Goopy"] = "gap: enchantment effect on play (not predicted)",
        ["Imbued"] = "ignore: auto-plays at the start of your turn",
        ["Slither"] = "ignore: enchantment effect on draw (random draws)",
        ["Vigorous"] = "gap: enchantment effect on play (not predicted)",
        ["Hexed"] = "ignore: affliction clears itself",
    };

    private static readonly string[] Folders = { "Relics", "Powers", "Cards", "Monsters", "Orbs", "Potions", "Enchantments", "Afflictions" };

    private static Dictionary<string, Entry>? _byType;

    /// <summary>The running game's classes against the ledger (built once).</summary>
    public static IReadOnlyDictionary<string, Entry> ByType => _byType ??= Build();

    private static Dictionary<string, Entry> Build()
    {
        var hookSet = Hooks.ToHashSet();
        var ledger = Generated.ToDictionary(g => g.Type, g => g);
        var result = new Dictionary<string, Entry>();
        foreach (var t in GameTypes())
        {
            string hooks;
            try
            {
                hooks = string.Join(",", t.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
                    .Where(m => hookSet.Contains(m.Name) && m.IsVirtual && m.GetBaseDefinition().DeclaringType != t)
                    .Select(m => m.Name).Distinct().OrderBy(n => n, StringComparer.Ordinal));
            }
            catch
            {
                continue;
            }
            var known = ledger.TryGetValue(t.Name, out var g);
            if (!known && hooks.Length == 0) continue;
            Entry e;
            if (!known) e = new Entry(t.Name, Status.New, "not in the ledger (new in this game version?)", hooks);
            else if (g.Hooks != hooks) e = new Entry(t.Name, Status.Changed, $"hooks were {g.Hooks}, now {hooks}", hooks);
            else if (g.Rule) e = new Entry(t.Name, Status.Covered, "", hooks);
            else if (Reviewed.TryGetValue(t.Name, out var why))
                e = new Entry(t.Name, why.StartsWith("gap:") ? Status.Gap : Status.Ignored, why, hooks);
            else e = new Entry(t.Name, Status.Unreviewed, "no rule and not reviewed", hooks);
            result[t.Name] = e;
        }
        return result;
    }

    private static IEnumerable<Type> GameTypes()
    {
        Type[] all;
        try { all = typeof(AbstractModel).Assembly.GetTypes(); }
        catch (ReflectionTypeLoadException ex) { all = ex.Types.Where(t => t != null).ToArray()!; }
        return all.Where(t => t is { IsAbstract: false, IsClass: true } && typeof(AbstractModel).IsAssignableFrom(t)
                              && Folders.Any(f => t.Namespace == "MegaCrit.Sts2.Core.Models." + f));
    }

    /// <summary>One summary line at load: the game's version against the ledger's, and the counts.</summary>
    public static void LogSummary(ModLog log, Sts2Mods.Core.Diagnostics.ErrorReporter? reporter)
    {
        try
        {
            var game = SafeGameVersion();
            var by = ByType.Values.GroupBy(e => e.Status).ToDictionary(g => g.Key, g => g.ToList());
            int N(Status s) => by.TryGetValue(s, out var l) ? l.Count : 0;
            log.Info($"[coverage] game {game}, ledger {LedgerGameVersion}: {ByType.Count} classes with combat hooks; covered {N(Status.Covered)}, ignored {N(Status.Ignored)}, " +
                     $"known gaps {N(Status.Gap)}, unreviewed {N(Status.Unreviewed)}, new {N(Status.New)}, changed {N(Status.Changed)}.");
            foreach (var s in new[] { Status.New, Status.Changed, Status.Unreviewed })
                if (by.TryGetValue(s, out var l))
                    log.Info($"[coverage] {s.ToString().ToLowerInvariant()}: {string.Join(", ", l.Select(e => e.Type))}");
            foreach (var c in CardCoverage.Issues)
                log.Info($"[coverage] card code: {c.Key}: {string.Join("; ", c.Value)}");
            if (!string.IsNullOrEmpty(game) && !game.Contains(LedgerGameVersion.TrimStart('v')))
            {
                log.Warn($"[coverage] the game ({game}) isn't the version the mod's coverage was checked against ({LedgerGameVersion}): " +
                         "new or changed mechanics are reported as they show up.");
                reporter?.Mismatch("coverage", $"Game version {game} differs from the coverage ledger ({LedgerGameVersion})",
                    new[] { "coverage", "game-version", game },
                    $"The mod's coverage ledger was generated from {LedgerGameVersion}; the game is {game}. Rerun scripts/decompile.ps1 and scripts/coverage-ledger.mjs, then review new and changed classes.");
            }
        }
        catch (Exception ex)
        {
            log.Warn($"[coverage] couldn't check coverage: {ex.Message}");
        }
    }

    public static string SafeGameVersion()
    {
        try { return MegaCrit.Sts2.Core.Nodes.NGame.GetGameVersion() ?? ""; }
        catch { return ""; }
    }
}

/// <summary>
/// Watches fights for game classes the ledger flags (new, changed, unreviewed, or a known gap) and
/// for cards whose code does something the mod doesn't count. Each is logged once per session;
/// unexpected ones are also reported (grouped by class).
/// </summary>
public sealed class CoverageWatch : IFeature, IFrameFeature
{
    public string Id => "coverage-watch";
    public string DisplayName => "Coverage Watch (log only)";
    public bool EnabledByDefault => true;

    private readonly HashSet<Type> _checked = new();
    private bool _summarized;
    // The ledger and the card-code scan read the whole game assembly once: on a worker thread, so
    // loading the game doesn't hitch. Nothing is checked until they're done.
    private Task? _building;

    public void Apply(Harmony harmony)
    {
    }

    public void Attach(OverlayHost host)
    {
    }

    public void OnFrame(OverlayHost host)
    {
        _building ??= Task.Run(() =>
        {
            _ = CoverageLedger.ByType;
            _ = CardCoverage.Issues;
        });
        if (!_building.IsCompleted) return;
        if (!_summarized)
        {
            _summarized = true;
            if (_building.IsFaulted) ModEntry.Log.Warn($"[coverage] couldn't build the ledger: {_building.Exception?.GetBaseException().Message}");
            CoverageLedger.LogSummary(ModEntry.Log, Sts2Mods.Core.Diagnostics.ErrorReporter.Instance);
        }
        var me = Sts2State.Me;
        var cs = Sts2State.Combat;
        if (me == null || cs == null) return;
        foreach (var r in me.Relics) Check(r);
        foreach (var c in cs.Creatures)
        {
            foreach (var p in c.Powers) Check(p);
            if (c.Monster != null) Check(c.Monster);
        }
        foreach (var card in Sts2State.Hand(me))
        {
            Check(card);
            if (card.Enchantment != null) Check(card.Enchantment);
            if (card.Affliction != null) Check(card.Affliction);
        }
        var orbs = me.PlayerCombatState?.OrbQueue?.Orbs;
        if (orbs != null) foreach (var o in orbs) Check(o);
    }

    private void Check(AbstractModel m)
    {
        var t = m.GetType();
        if (!_checked.Add(t)) return;
        try
        {
            var log = ModEntry.Log;
            if (CoverageLedger.ByType.TryGetValue(t.Name, out var e))
            {
                switch (e.Status)
                {
                    case CoverageLedger.Status.Gap:
                        log.Info($"[coverage] known gap in this fight: {t.Name} ({e.Note})");
                        break;
                    case CoverageLedger.Status.Unreviewed or CoverageLedger.Status.New or CoverageLedger.Status.Changed:
                        Unexpected(t.Name, e.Status.ToString().ToLowerInvariant(), $"{e.Note}. Combat hooks: {e.Hooks}.");
                        break;
                }
            }
            if (CardCoverage.Issues.TryGetValue(t.Name, out var issues))
                Unexpected(t.Name, "card-code", $"its code does what the mod doesn't count: {string.Join("; ", issues)}.");
        }
        catch (Exception ex)
        {
            ModEntry.Log.Warn($"[coverage] check failed for {t.Name}: {ex.Message}");
        }
    }

    private static void Unexpected(string type, string kind, string details)
    {
        ModEntry.Log.Warn($"[coverage] unexpected in this fight: {type} ({kind}): {details}");
        Sts2Mods.Core.Diagnostics.ErrorReporter.Instance?.Mismatch("coverage", $"Coverage: {type} ({kind})", new[] { "coverage", kind, type },
            $"{type} showed up in a fight. {details}\nGame {CoverageLedger.SafeGameVersion()}, ledger {CoverageLedger.LedgerGameVersion}. " +
            "Review it against the decompile: add a rule, or a reason in CoverageLedger.Reviewed.");
    }
}
