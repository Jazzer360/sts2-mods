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
    public static List<string> Problems()
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
        var specs = RelicSpecs.All.Where(s => s.InTray).Select(s => s.TypeName).Concat(PowerSpecs.All.Select(s => s.TypeName)).ToHashSet();
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

        // Features: each one registered has an on/off switch in the menu, and each switch a feature.
        var registered = ModEntry.Features.States.Keys.ToHashSet();
        var switches = SettingsMenu.FeatureRows.Select(r => r.Id).ToHashSet();
        foreach (var n in registered.Except(switches).OrderBy(n => n)) problems.Add($"feature without a settings switch: {n}");
        foreach (var n in switches.Except(registered).OrderBy(n => n)) problems.Add($"settings switch without a feature: {n}");
        return problems;
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
