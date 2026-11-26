using System.Reflection;
using SpireQoL.Diagnostics;
using Sts2Mods.Core.Features;
using Xunit;

namespace SpireQoL.Catalog.Tests;

/// <summary>
/// Decision #391: Harmony's IL generator proxy is made once, on the game's thread, before the mod starts any worker that reads the game's code and
/// before its patches: two threads making it at once fail. ModEntry.Initialize makes it first (GameCode.MakeEmitReady), and the module initializer
/// of these tests makes it by the same call (HarmonyWarmUp).
/// </summary>
public class EmitReadyTests
{
    [Fact]
    public void TheIlGeneratorProxyIsMadeOnce()
    {
        Assert.True(GameCode.MakeEmitReady());
        var proxies = AppDomain.CurrentDomain.GetAssemblies()
            .Where(assembly => assembly.GetName().Name?.Contains("ILGeneratorProxy", StringComparison.Ordinal) == true).ToList();
        Assert.Single(proxies);
    }

    [Fact]
    public void ManyThreadsReadingCodeAndMakingAccessorsAtOnceAllSucceed()
    {
        var failures = new System.Collections.Concurrent.ConcurrentBag<Exception>();
        var threads = Enumerable.Range(0, 8).Select(index => new Thread(() =>
        {
            try
            {
                if (index % 2 == 0) Assert.NotEmpty(GameCode.Instructions(typeof(EmitReadyTests).GetMethod(nameof(TheIlGeneratorProxyIsMadeOnce))!));
                else Assert.NotNull(HarmonyLib.AccessTools.FieldRefAccess<Holder, int>(nameof(Holder.Count)));
            }
            catch (Exception ex) { failures.Add(ex); }
        })).ToList();
        foreach (var thread in threads) thread.Start();
        foreach (var thread in threads) thread.Join();
        Assert.Empty(failures);
    }

    [Fact]
    public void TheModMakesItReadyBeforeAnyWorkerOrPatch()
    {
        var initialize = typeof(ModEntry).GetMethod(nameof(ModEntry.Initialize))!;
        var calls = GameCode.Instructions(initialize).Select(instruction => instruction.operand).OfType<MethodBase>().ToList();
        var ready = calls.FindIndex(method => method.DeclaringType == typeof(GameCode) && method.Name == nameof(GameCode.MakeEmitReady));
        Assert.True(ready >= 0, "ModEntry.Initialize doesn't make Harmony's emit helpers ready");
        var later = new (Type Type, string Name)[]
        {
            (typeof(ModEntry), "StartReadingCode"),
            (typeof(FeatureHost), nameof(FeatureHost.ApplyAll)),
            (typeof(Task), nameof(Task.Run)),
            (typeof(Thread), ".ctor"),
        };
        foreach (var (type, name) in later)
        {
            var first = calls.FindIndex(method => method.DeclaringType == type && method.Name == name);
            Assert.True(first < 0 || first > ready, $"ModEntry.Initialize calls {type.Name}.{name} before it makes Harmony's emit helpers ready");
        }
        Assert.Contains(calls, method => method.DeclaringType == typeof(ModEntry) && method.Name == "StartReadingCode");
    }

    private sealed class Holder
    {
        public int Count = 1;
    }
}
