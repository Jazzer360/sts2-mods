using System.Reflection;
using SpireQoL.Sim;
using Xunit;

namespace SpireQoL.Catalog.Tests;

/// <summary>
/// The damage and block math of a modelled class is its model's: the adapter captures a term only for a class with no model
/// (SimReaders.Damage, Block: every listener SimModels doesn't have). So a hand-written model whose game class overrides one of those hooks must
/// answer it, or the simulator drops what the class does to the numbers (Conqueror's ×2 on a Sovereign Blade was dropped so). A read model answers
/// each hook its code has.
/// </summary>
public class ModifyHookCoverageTests
{
    /// <summary>The hooks whose terms the adapter captures for an unmodelled class (SimReaders.Damage, SimReaders.Block).</summary>
    private static readonly string[] CapturedHooks =
    {
        "ModifyDamageAdditive", "ModifyDamageMultiplicative", "ModifyDamageMultiplicativeLate", "ModifyDamageCap", "ModifyBlockAdditive", "ModifyBlockMultiplicative",
    };

    [Fact]
    public void EveryHandWrittenModelAnswersTheMathHooksItsClassOverrides()
    {
        var types = typeof(MegaCrit.Sts2.Core.Models.Powers.StrengthPower).Assembly.GetTypes();
        var missing = new List<string>();
        foreach (var type in HookReader.Powers(types).Concat(HookReader.Relics(types)))
        {
            if (SimModels.Get(type.Name) is not { } model || model is ReadModel) continue;
            var overridden = type.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly).Select(method => method.Name).ToHashSet();
            missing.AddRange(CapturedHooks.Where(hook => overridden.Contains(hook) && !model.Overrides(hook)).Select(hook => $"{type.Name}.{hook}"));
        }
        Assert.True(missing.Count == 0, "modelled by hand without the math its class does (no term is captured for a modelled class): " + string.Join(", ", missing));
    }
}
