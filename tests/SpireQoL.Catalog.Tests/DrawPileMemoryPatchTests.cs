using HarmonyLib;
using SpireQoL.Combat;
using Xunit;

namespace SpireQoL.Catalog.Tests;

/// <summary>
/// Decision #382: the draw pile's known top is patched by the first of Incoming Damage, Proc Tags and the Death Skull to apply. When
/// that feature's apply fails, the host undoes its patches (`FeatureHost.TryApply`: `UnpatchAll` of the feature's own Harmony id),
/// the draw pile's with them, and the next feature to apply patches it again: whether it's patched is read from the game method's
/// patches, never a flag that outlives them. (The host itself isn't run here: its failure line goes to the game's log, which needs
/// the engine.)
/// </summary>
public class DrawPileMemoryPatchTests
{
    private const string Failing = "spireqol-test.fails";
    private const string Applying = "spireqol-test.applies";
    private const string AlsoApplying = "spireqol-test.also-applies";

    private static List<string> Owners(System.Reflection.MethodBase method) =>
        (Harmony.GetPatchInfo(method)?.Prefixes ?? Enumerable.Empty<Patch>())
        .Where(patch => patch.PatchMethod.DeclaringType == typeof(DrawPileMemory))
        .Select(patch => patch.owner).ToList();

    [Fact]
    public void TheNextFeaturePatchesWhenTheFirstOnesApplyIsUndone()
    {
        var add = DrawPileMemory.AddMethod;
        Assert.NotNull(add);
        Assert.False(DrawPileMemory.IsPatched(add));
        try
        {
            // The first feature patches, then fails later in its apply: the host undoes everything under its id.
            var failing = new Harmony(Failing);
            DrawPileMemory.Patch(failing);
            Assert.Equal(new[] { Failing }, Owners(add!));
            failing.UnpatchAll(Failing);
            Assert.False(DrawPileMemory.IsPatched(add));

            // The next one patches again, and the one after finds it in place.
            DrawPileMemory.Patch(new Harmony(Applying));
            DrawPileMemory.Patch(new Harmony(AlsoApplying));
            Assert.Equal(new[] { Applying }, Owners(add!));
        }
        finally
        {
            foreach (var id in new[] { Failing, Applying, AlsoApplying }) new Harmony(id).UnpatchAll(id);
        }
        Assert.False(DrawPileMemory.IsPatched(add));
    }
}
