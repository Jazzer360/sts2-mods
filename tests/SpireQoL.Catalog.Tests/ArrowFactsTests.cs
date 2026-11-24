using System.Reflection;
using SpireQoL.Sim;
using Xunit;

namespace SpireQoL.Catalog.Tests;

/// <summary>
/// What the model reader takes from ArrowCmd's code about an arrow (decision #241), held to it on both branches: AfterArrowImpaled's Impaled has an
/// arrow, and its first arrow's owner is the shooter (HookReader.IsImpaledsFirstArrow), because ImpaledPower.AddArrow is called by ArrowCmd.ImpaleTarget
/// alone, on the instance of the arrow's owner (one per applier: ImpaledPower is InstancedPerApplier), before Hook.AfterArrowImpaled.
/// </summary>
public class ArrowFactsTests
{
    public static TheoryData<string> Branches => new() { "beta", "main" };

    [Theory]
    [MemberData(nameof(Branches))]
    public void OnlyImpaleTargetAddsAnArrowToTheShootersImpaled(string branch)
    {
        var path = branch == "main" ? BranchCode.MainPath : BranchCode.BetaPath;
        if (!File.Exists(path)) return;
        var code = BranchCode.Of(path);
        if (code.Assembly.GetTypes().FirstOrDefault(type => type.Name == nameof(MegaCrit.Sts2.Core.Models.Powers.ImpaledPower)) is not { } impaled) return; // main has no arrows yet
        const BindingFlags all = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
        var callers = new HashSet<string>(StringComparer.Ordinal);
        foreach (var type in code.Assembly.GetTypes())
            foreach (var method in type.GetMethods(all).Cast<MethodBase>().Concat(type.GetConstructors(all)))
            {
                List<Procs.Programs.CodeOp> ops;
                try { ops = code.Read(method); }
                catch (Exception) { continue; }
                if (!ops.Any(instruction => instruction.Member is { Name: "AddArrow", DeclaringType: nameof(MegaCrit.Sts2.Core.Models.Powers.ImpaledPower) })) continue;
                // An async method's body is its state machine's MoveNext: named by its outer type and method (<ImpaleTarget>d__12).
                var outer = type.DeclaringType ?? type;
                var name = type.DeclaringType != null && type.Name.StartsWith('<') ? type.Name[1..type.Name.IndexOf('>')] : method.Name;
                callers.Add(outer.Name + "." + name);
                // The arrow joins before the hook that tells of it.
                var add = ops.FindIndex(instruction => instruction.Member is { Name: "AddArrow" });
                var told = ops.FindIndex(instruction => instruction.Member is { Name: "AfterArrowImpaled" });
                Assert.True(told > add, $"{outer.Name}.{name}: Hook.AfterArrowImpaled before ImpaledPower.AddArrow");
            }
        Assert.Equal(new[] { "ArrowCmd.ImpaleTarget" }, callers.Order(StringComparer.Ordinal));
        Assert.Equal(PowerCopies.PerApplier, code.HookReading(impaled).Spec.Copies);
    }
}
