using System.Reflection;
using System.Runtime.CompilerServices;
using MegaCrit.Sts2.Core.Models;
using SpireQoL.Data;
using SpireQoL.Menu;

namespace SpireQoL.Diagnostics;

/// <summary>
/// Everything the mod tracks has a row in the settings menu, and every row still has something
/// behind it (SettingsCatalog). Checked once at load, from the compiled mod and game:
///  - proc rules: the simulator's models with a tab (from the play log, or judged on how the turn ends: SimModel.OutcomeTab),
///    and the relic and power classes the card predictions still name themselves (nameof compiles to string literals, read from CardPredictions' code),
///    against the catalog's proc rows;
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
        // The simulator's models with a tab (the play log's tabs), and the outcome comparisons Proc Indicators names (tab keys are
        // built as "proc:" + nameof(X), which the compiler folds into one literal).
        var ruleNames = LiteralsIn(typeof(Combat.CardPredictions)).Select(literal => literal.StartsWith("proc:", StringComparison.Ordinal) ? literal[5..] : literal)
            .Concat(Sim.SimModels.Names.Where(name => Sim.SimModels.Get(name) is { } model && (model.Tab != null || model.JudgedOnOutcome)))
            .Where(name => gameTypes.TryGetValue(name, out var type) && (typeof(RelicModel).IsAssignableFrom(type) || typeof(PowerModel).IsAssignableFrom(type)))
            .ToHashSet();
        var procRows = sources.Where(sourceEntry => sourceEntry.Tag != null && sourceEntry.TagPrefix == "proc:").Select(sourceEntry => sourceEntry.Type).ToHashSet();
        foreach (var name in ruleNames.Except(procRows).Order()) problems.Add($"proc rule without a settings row: {name}");
        foreach (var name in procRows.Except(ruleNames).Order()) problems.Add($"settings row (proc tab) without a rule: {name}");

        // Tracker rows.
        // Cards with a tray row of their own (Make It So's Skill count: RelicCountersFeature, Procs/ReturnToHand).
        var specs = RelicSpecs.All.Where(relicCounterSpec => relicCounterSpec.InTray).Select(relicCounterSpec => relicCounterSpec.TypeName).Concat(PowerSpecs.All.Select(powerCounterSpec => powerCounterSpec.TypeName))
            .Append(Procs.ReturnToHand.MakeItSo).ToHashSet();
        var trackerRows = sources.Where(sourceEntry => sourceEntry.Tracker != null).Select(sourceEntry => sourceEntry.Type).ToHashSet();
        foreach (var name in specs.Except(trackerRows).Order()) problems.Add($"tracker row without a settings row: {name}");
        foreach (var name in trackerRows.Except(specs).Order()) problems.Add($"settings row (tracker) without a tracker spec: {name}");

        // Double plays: every source of extra plays the game has.
        var replayers = gameTypes.Values
            .Where(type => type.GetMethod("ModifyCardPlayCount", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly) is { } method
                        && method.GetBaseDefinition().DeclaringType != type)
            .Select(type => type.Name).ToHashSet();
        var doubleRows = sources.Where(sourceEntry => sourceEntry.TagPrefix == "double-play:").Select(sourceEntry => sourceEntry.Type).ToHashSet();
        foreach (var name in replayers.Except(doubleRows).Order()) problems.Add($"source of extra plays without a double-play row: {name}");
        foreach (var name in doubleRows.Except(replayers).Order()) problems.Add($"double-play row for a class that doesn't add plays: {name}");

        // Run reminders: every rule has a settings row, every row a rule.
        var reminderRules = ChoiceReminders.Rules.Select(reminderRule => reminderRule.Relic).ToHashSet();
        var reminderRows = sources.Where(sourceEntry => sourceEntry.OutsideFights).Select(sourceEntry => sourceEntry.Type).ToHashSet();
        foreach (var name in reminderRules.Except(reminderRows).Order()) problems.Add($"reminder rule without a settings row: {name}");
        foreach (var name in reminderRows.Except(reminderRules).Order()) problems.Add($"settings row (reminder) without a rule: {name}");
        // Each place (card rewards, events, shops) has its text exactly where the rule shows there.
        foreach (var reminderRule in ChoiceReminders.Rules)
        {
            if (sources.FirstOrDefault(sourceEntry => sourceEntry.Type == reminderRule.Relic && sourceEntry.OutsideFights) is not { } row) continue;
            foreach (var place in Enum.GetValues<ReminderPlace>())
            {
                if (reminderRule.In(place) && row.Reminder(place) == null) problems.Add($"reminder shown at {place} without a settings switch there: {reminderRule.Relic}");
                if (!reminderRule.In(place) && row.Reminder(place) != null) problems.Add($"settings switch at {place} where the reminder never shows: {reminderRule.Relic}");
            }
        }

        // Features: each one registered has an on/off switch in the menu, and each switch a feature.
        var registered = (featureIds ?? FeatureIds()).ToHashSet();
        var switches = SettingsMenu.FeatureRows.Select(featureRow => featureRow.Id).ToHashSet();
        foreach (var name in registered.Except(switches).Order()) problems.Add($"feature without a settings switch: {name}");
        foreach (var name in switches.Except(registered).Order()) problems.Add($"settings switch without a feature: {name}");
        // Each switch is named as its feature is (decision #185: the name players read is in both, the dev dashboard showing the feature's).
        var names = Features().ToDictionary(feature => feature.Id, feature => feature.DisplayName);
        foreach (var (id, name, _, _) in SettingsMenu.FeatureRows)
            if (names.TryGetValue(id, out var displayName) && !string.Equals(Telemetry.FeatureNames.Plain(displayName), name, StringComparison.OrdinalIgnoreCase))
                problems.Add($"settings switch named apart from its feature: {id} is \"{name}\" in the menu, \"{displayName}\" as a feature");
        return problems;
    }

    /// <summary>The id of every feature class in the mod (Id is a constant on each: read from an instance made without its constructor).</summary>
    public static IEnumerable<string> FeatureIds() => Features().Select(feature => feature.Id);

    /// <summary>Every feature class in the mod, as an instance made without its constructor (its Id and DisplayName are constants).</summary>
    private static IEnumerable<Sts2Mods.Core.Features.IFeature> Features()
    {
        foreach (var type in typeof(CatalogCheck).Assembly.GetTypes())
        {
            if (type is not { IsAbstract: false, IsClass: true } || !typeof(Sts2Mods.Core.Features.IFeature).IsAssignableFrom(type)) continue;
            yield return (Sts2Mods.Core.Features.IFeature)RuntimeHelpers.GetUninitializedObject(type);
        }
    }

    private static Dictionary<string, Type> GameModelTypes()
    {
        Type[] all;
        try { all = typeof(AbstractModel).Assembly.GetTypes(); }
        catch (ReflectionTypeLoadException ex) { all = ex.Types.Where(type => type != null).ToArray()!; }
        var byName = new Dictionary<string, Type>();
        foreach (var type in all)
            if (type is { IsAbstract: false, IsClass: true } && typeof(AbstractModel).IsAssignableFrom(type)) byName.TryAdd(type.Name, type);
        return byName;
    }

    /// <summary>String literals in a type's methods, its lambdas and async state machines (nested types).</summary>
    private static HashSet<string> LiteralsIn(Type type)
    {
        var set = new HashSet<string>();
        const BindingFlags all = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
        var types = new List<Type> { type };
        for (var i = 0; i < types.Count; i++) types.AddRange(types[i].GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic));
        foreach (var otherType in types)
        {
            foreach (var method in otherType.GetMethods(all).Cast<MethodBase>().Concat(otherType.GetConstructors(all)))
            {
                try
                {
                    foreach (var ins in SpireQoL.Diagnostics.GameCode.Instructions(method))
                        if (ins.operand is string otherOperand) set.Add(otherOperand);
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
