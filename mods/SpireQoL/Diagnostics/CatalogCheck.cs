using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using MegaCrit.Sts2.Core.Models;
using SpireQoL.Data;
using SpireQoL.Features;
using SpireQoL.Menu;

namespace SpireQoL.Diagnostics;

/// <summary>
/// Everything the mod tracks has a row in the settings menu, and every row still has something
/// behind it (SettingsCatalog). Checked once at load, from the compiled mod and game:
///  - proc rules: the relic and power classes Proc Indicators names in its rules (nameof compiles
///    to string literals, read from ProcIndicatorsFeature's code) against the catalog's proc rows;
///  - tracker rows: RelicSpecs (in the tray) and PowerSpecs against the catalog's tracker rows;
///  - double plays: every game class that changes how many times a card plays
///    (ModifyCardPlayCount) against the catalog's double-play rows;
///  - features: every registered feature against the menu's on/off switches.
/// Problems are logged and reported (CoverageWatch). Tab kinds shown in play that the catalog
/// doesn't know are reported as they appear, too.
/// </summary>
internal static class CatalogCheck
{
    /// <summary>
    /// Every problem found. <paramref name="featureIds"/>: the features registered in game; by default the mod's feature classes
    /// (what tests/SpireQoL.Catalog.Tests checks before every release, without the game running).
    /// </summary>
    public static List<string> Problems(IEnumerable<string>? featureIds = null)
    {
        var problems = new List<string>();
        var sources = SettingsCatalog.Sources;

        // Proc rules.
        var gameTypes = GameModelTypes();
        // Tab keys are built as "proc:" + nameof(X), which the compiler folds into one literal.
        var ruleNames = LiteralsIn(typeof(ProcIndicatorsFeature)).Select(n => n.StartsWith("proc:") ? n[5..] : n)
            .Where(n => gameTypes.TryGetValue(n, out var t) && (typeof(RelicModel).IsAssignableFrom(t) || typeof(PowerModel).IsAssignableFrom(t)))
            .ToHashSet();
        var procRows = sources.Where(s => s.Tag != null && s.TagPrefix == "proc:").Select(s => s.Type).ToHashSet();
        foreach (var n in ruleNames.Except(procRows).OrderBy(n => n)) problems.Add($"proc rule without a settings row: {n}");
        foreach (var n in procRows.Except(ruleNames).OrderBy(n => n)) problems.Add($"settings row (proc tab) without a rule: {n}");

        // Tracker rows.
        // Cards with a tray row of their own (Make It So's Skill count: RelicCountersFeature, Procs/ReturnToHand).
        var specs = RelicSpecs.All.Where(s => s.InTray).Select(s => s.TypeName).Concat(PowerSpecs.All.Select(s => s.TypeName))
            .Append(Procs.ReturnToHand.MakeItSo).ToHashSet();
        var trackerRows = sources.Where(s => s.Tracker != null).Select(s => s.Type).ToHashSet();
        foreach (var n in specs.Except(trackerRows).OrderBy(n => n)) problems.Add($"tracker row without a settings row: {n}");
        foreach (var n in trackerRows.Except(specs).OrderBy(n => n)) problems.Add($"settings row (tracker) without a tracker spec: {n}");

        // Double plays: every source of extra plays the game has.
        var replayers = gameTypes.Values
            .Where(t => t.GetMethod("ModifyCardPlayCount", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly) is { } m
                        && m.GetBaseDefinition().DeclaringType != t)
            .Select(t => t.Name).ToHashSet();
        var doubleRows = sources.Where(s => s.TagPrefix == "double-play:").Select(s => s.Type).ToHashSet();
        foreach (var n in replayers.Except(doubleRows).OrderBy(n => n)) problems.Add($"source of extra plays without a double-play row: {n}");
        foreach (var n in doubleRows.Except(replayers).OrderBy(n => n)) problems.Add($"double-play row for a class that doesn't add plays: {n}");

        // Run reminders: every rule has a settings row, every row a rule.
        var reminderRules = ChoiceReminders.Rules.Select(r => r.Relic).ToHashSet();
        var reminderRows = sources.Where(s => s.OutsideFights).Select(s => s.Type).ToHashSet();
        foreach (var n in reminderRules.Except(reminderRows).OrderBy(n => n)) problems.Add($"reminder rule without a settings row: {n}");
        foreach (var n in reminderRows.Except(reminderRules).OrderBy(n => n)) problems.Add($"settings row (reminder) without a rule: {n}");
        // Each place (card rewards, events, shops) has its text exactly where the rule shows there.
        foreach (var r in ChoiceReminders.Rules)
        {
            if (sources.FirstOrDefault(s => s.Type == r.Relic && s.OutsideFights) is not { } row) continue;
            foreach (var place in Enum.GetValues<ReminderPlace>())
            {
                if (r.In(place) && row.Reminder(place) == null) problems.Add($"reminder shown at {place} without a settings switch there: {r.Relic}");
                if (!r.In(place) && row.Reminder(place) != null) problems.Add($"settings switch at {place} where the reminder never shows: {r.Relic}");
            }
        }

        // Features: each one registered has an on/off switch in the menu, and each switch a feature.
        var registered = (featureIds ?? FeatureIds()).ToHashSet();
        var switches = SettingsMenu.FeatureRows.Select(r => r.Id).ToHashSet();
        foreach (var n in registered.Except(switches).OrderBy(n => n)) problems.Add($"feature without a settings switch: {n}");
        foreach (var n in switches.Except(registered).OrderBy(n => n)) problems.Add($"settings switch without a feature: {n}");
        return problems;
    }

    /// <summary>The id of every feature class in the mod (Id is a constant on each: read from an instance made without its constructor).</summary>
    public static IEnumerable<string> FeatureIds()
    {
        foreach (var t in typeof(CatalogCheck).Assembly.GetTypes())
        {
            if (t is not { IsAbstract: false, IsClass: true } || !typeof(Sts2Mods.Core.Features.IFeature).IsAssignableFrom(t)) continue;
            var f = (Sts2Mods.Core.Features.IFeature)RuntimeHelpers.GetUninitializedObject(t);
            yield return f.Id;
        }
    }

    private static Dictionary<string, Type> GameModelTypes()
    {
        Type[] all;
        try { all = typeof(AbstractModel).Assembly.GetTypes(); }
        catch (ReflectionTypeLoadException ex) { all = ex.Types.Where(t => t != null).ToArray()!; }
        var d = new Dictionary<string, Type>();
        foreach (var t in all)
            if (t is { IsAbstract: false, IsClass: true } && typeof(AbstractModel).IsAssignableFrom(t)) d.TryAdd(t.Name, t);
        return d;
    }

    /// <summary>String literals in a type's methods, its lambdas and async state machines (nested types).</summary>
    private static HashSet<string> LiteralsIn(Type type)
    {
        var set = new HashSet<string>();
        const BindingFlags all = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
        var types = new List<Type> { type };
        for (var i = 0; i < types.Count; i++) types.AddRange(types[i].GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic));
        foreach (var t in types)
        {
            foreach (var m in t.GetMethods(all).Cast<MethodBase>().Concat(t.GetConstructors(all)))
            {
                try
                {
                    foreach (var ins in PatchProcessor.GetOriginalInstructions(m))
                        if (ins.operand is string s) set.Add(s);
                }
                catch
                {
                    // unreadable: skipped
                }
            }
        }
        return set;
    }
}
