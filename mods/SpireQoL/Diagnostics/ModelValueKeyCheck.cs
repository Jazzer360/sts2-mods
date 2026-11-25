using System.Reflection;
using MegaCrit.Sts2.Core.Models;
using SpireQoL.Procs.Programs;
using SpireQoL.Sim;
using SpireQoL.Sim.Models;

namespace SpireQoL.Diagnostics;

/// <summary>
/// Every value key the simulator names, against the DynamicVars keys of the game class it reads. A model's own keys come from its
/// code (Var, VarTotal and PropsOf's key; RelicBlock's "Block", RelicDamageAll's and RelicDamageRandom's "Damage"); a SimEngine.Var
/// anywhere in the simulator that names its model by a literal ("InfusedCore") names that model's. A key its game class (the class named like the model, its canonical model's
/// DynamicVars on the installed game) doesn't have reads nothing, so the model runs on its fallback whatever the game holds:
/// Kunai read "Dexterity" where the key is "DexterityPower". ModelValueKeyTests runs it.
/// </summary>
internal static class ModelValueKeyCheck
{
    /// <summary>A value key the simulator names: whose values (the game's class name), the key, and the code naming it.</summary>
    internal readonly record struct ValueKey(string Model, string Key, string Where)
    {
        public override string ToString() => $"{Model}:{Key} ({Where})";
    }

    /// <summary>
    /// What the check found: how many keys it compared, the keys their game class doesn't have, the calls whose key or model it
    /// couldn't read (not a literal), and reviewed entries nothing names any more.
    /// </summary>
    internal sealed record Report(int Compared, List<ValueKey> Wrong, List<string> Unread, List<string> StaleReviews);

    /// <summary>
    /// Reviewed: a key ("Model:Key") that isn't one of its game class's DynamicVars on purpose (a value the adapter makes), and why.
    /// An entry nothing names any more fails the check (stale).
    /// </summary>
    internal static readonly Dictionary<string, string> Reviewed = new();

    private const BindingFlags All = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

    /// <summary>The check on the loaded game.</summary>
    public static Report Run()
    {
        var unread = new List<string>();
        var named = Named(unread);
        var gameTypes = CoverageLedger.ModelTypes().GroupBy(type => type.Name).ToDictionary(group => group.Key, group => group.ToList());
        var wrong = new List<ValueKey>();
        var stale = Reviewed.Keys.ToHashSet();
        var compared = 0;
        foreach (var valueKey in named.Distinct().OrderBy(key => key.ToString(), StringComparer.Ordinal))
        {
            if (!gameTypes.TryGetValue(valueKey.Model, out var types))
            {
                unread.Add($"{valueKey}: no game class {valueKey.Model}");
                continue;
            }
            var keys = types.SelectMany(GameKeys).ToHashSet();
            compared++;
            if (keys.Contains(valueKey.Key)) continue;
            if (Reviewed.ContainsKey($"{valueKey.Model}:{valueKey.Key}"))
            {
                stale.Remove($"{valueKey.Model}:{valueKey.Key}");
                continue;
            }
            wrong.Add(valueKey);
        }
        return new Report(compared, wrong, unread, stale.OrderBy(key => key, StringComparer.Ordinal).ToList());
    }

    /// <summary>A game class's DynamicVars keys: its canonical model's (ModelDb), none when it has none.</summary>
    internal static IEnumerable<string> GameKeys(Type gameType)
    {
        AbstractModel? canonical;
        try { canonical = ModelDb.GetByIdOrNull<AbstractModel>(ModelDb.GetId(gameType)); }
        catch { canonical = null; }
        if (canonical?.GetType().GetProperty("DynamicVars", BindingFlags.Public | BindingFlags.Instance)?.GetValue(canonical) is not IEnumerable<KeyValuePair<string, MegaCrit.Sts2.Core.Localization.DynamicVars.DynamicVar>> vars)
            return Array.Empty<string>();
        return vars.Select(pair => pair.Key).ToList();
    }

    /// <summary>Every value key the simulator names (models' own, literal model names anywhere in it).</summary>
    internal static List<ValueKey> Named(List<string> unread)
    {
        var keys = new List<ValueKey>();
        foreach (var name in SimModels.Names)
        {
            if (SimModels.Get(name) is not { } model) continue;
            // A model read from its code reads the keys its code names (decision #195).
            if (model is ReadModel read)
            {
                keys.AddRange(read.Spec.Values().OfType<HookVar>().Select(variable => new ValueKey(model.Name, variable.Key.Replace(".Props", "", StringComparison.Ordinal), "read from its code")));
                continue;
            }
            foreach (var method in ModelEffectCheck.OwnTypes(model.GetType()).SelectMany(type => type.GetMethods(All).Cast<MethodBase>().Concat(type.GetConstructors(All))))
                keys.AddRange(KeysIn(method, ownModel: model.Name, unread));
        }
        // A model named by a literal (SimEngine.Var(state, player, "InfusedCore", "ExtraDamage")): anywhere in the simulator.
        foreach (var type in typeof(SimEngine).Assembly.GetTypes().Where(type => type.Namespace?.StartsWith("SpireQoL.Sim", StringComparison.Ordinal) == true && !ModelEffectCheck.OwnTypes(typeof(ReadModel)).Contains(type)))
            foreach (var method in type.GetMethods(All).Cast<MethodBase>().Concat(type.GetConstructors(All)))
                keys.AddRange(KeysIn(method, ownModel: null, unread));
        return keys;
    }

    /// <summary>
    /// The value keys one method names: with <paramref name="ownModel"/>, every key a model's helpers read for it (a model's own code);
    /// without, only SimEngine.Var calls naming their model by a literal.
    /// </summary>
    private static IEnumerable<ValueKey> KeysIn(MethodBase method, string? ownModel, List<string> unread)
    {
        if (method.GetMethodBody() == null) yield break;
        List<CodeOp> ops;
        try { ops = RuntimeCode.Read(method); } catch { yield break; }
        var landings = CodeDecoder.Landings(ops);
        var where = method.DeclaringType?.Name + "." + method.Name;
        for (var callAt = 0; callAt < ops.Count; callAt++)
        {
            if (!ops[callAt].IsCall || ops[callAt].Member is not { Kind: CodeMemberKind.Method } member) continue;
            if (member.DeclaringType is not (nameof(SimEngine) or nameof(ProcModel) or nameof(RelicProc))) continue;
            if (ownModel != null && member.DeclaringType == nameof(RelicProc))
            {
                if (member.Name == "RelicBlock") yield return new ValueKey(ownModel, "Block", where);
                if (member.Name is "RelicDamageAll" or "RelicDamageRandom") yield return new ValueKey(ownModel, "Damage", where);
                continue;
            }
            if (member.Name is not (nameof(SimEngine.Var) or nameof(SimEngine.VarTotal) or "PropsOf")) continue;
            var names = member.ParameterNames.ToList();
            string? Literal(string parameter) =>
                names.IndexOf(parameter) is var index and >= 0 && CodeDecoder.Producer(ops, callAt, CodeDecoder.ArgumentDepth(member, index), landings: landings) is { } producer
                    ? ops[producer].Text : null;
            string? modelName;
            if (member.DeclaringType == nameof(SimEngine))
            {
                var modelArgument = names.IndexOf("model") is var index and >= 0 ? CodeDecoder.Producer(ops, callAt, CodeDecoder.ArgumentDepth(member, index), landings: landings) : null;
                modelName = modelArgument is { } producer && ops[producer].Text is { } literal ? literal
                    : modelArgument is { } nameCall && ops[nameCall].Member is { Name: "get_Name" } ? ownModel : null;
                // The framework's own helpers pass their model through (ProcModel.Var's SimEngine.Var(..., Name, var, ...)).
                if (modelName == null) continue;
            }
            else if (ownModel == null) continue;
            else modelName = ownModel;
            if (Literal("var") is { } key) yield return new ValueKey(modelName, key, where);
            else unread.Add($"{where}: {member.Name}'s key isn't a literal");
        }
    }
}
