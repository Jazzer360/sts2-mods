using System.Reflection;
using Xunit;

namespace SpireQoL.Catalog.Tests;

/// <summary>
/// The coverage ledger's hook lists can't drift from the game (decision #323). Every model hook the game dispatches is in
/// Diagnostics/CoverageHooks.txt (a class overriding it needs a ledger entry), OutOfCombatHooks.txt or ExemptHooks.txt (each with why), read on
/// both branches: every AbstractModel hook the game's Hook class calls (its dispatch methods, their async bodies, iterators and lambdas), and a
/// power's own hooks (PowerModel's, which PowerCmd and a death call). A hook in none of them hides every class whose only fight hooks it has, as
/// the turn-start hooks hid Sandpit until decision #321.
/// </summary>
public class CoverageHookListTests
{
    public static TheoryData<string> Branches => new() { "beta", "main" };

    private static string PathOf(string branch) => branch == "main" ? BranchCode.MainPath : BranchCode.BetaPath;

    private const BindingFlags All = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

    private static HashSet<string> List(string name) =>
        File.ReadAllLines(Path.Combine(HookProgramTests.RepoRoot(), "mods", "SpireQoL", "Diagnostics", name))
            .Select(line => line.Trim()).Where(line => line.Length > 0 && !line.StartsWith('#')).ToHashSet(StringComparer.Ordinal);

    /// <summary>A model class's own hooks: its public virtual methods (not property accessors).</summary>
    private static IEnumerable<MethodInfo> HooksOf(Type model) =>
        model.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly).Where(method => method.IsVirtual && !method.IsSpecialName);

    private static Type Named(BranchCode code, string fullName) => code.Assembly.GetTypes().Single(type => type.FullName == fullName);

    /// <summary>The hooks the game dispatches on this branch: each AbstractModel hook the Hook class's code calls, and PowerModel's own that answer or act (a Task or a bool).</summary>
    private static SortedSet<string> Dispatched(BranchCode code)
    {
        var hooks = HooksOf(Named(code, "MegaCrit.Sts2.Core.Models.AbstractModel")).Select(method => method.Name).ToHashSet(StringComparer.Ordinal);
        var hookClass = Named(code, "MegaCrit.Sts2.Core.Hooks.Hook");
        var dispatched = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var type in Nested(hookClass).Prepend(hookClass))
            foreach (var method in type.GetMethods(All).Cast<MethodBase>().Concat(type.GetConstructors(All)))
                foreach (var instruction in code.Read(method))
                    if (instruction.IsCall && instruction.Member is { DeclaringType: "AbstractModel" } member && hooks.Contains(member.Name)) dispatched.Add(member.Name);
        foreach (var method in HooksOf(Named(code, "MegaCrit.Sts2.Core.Models.PowerModel")))
            if (method.ReturnType.FullName is "System.Threading.Tasks.Task" or "System.Boolean") dispatched.Add(method.Name);
        return dispatched;
    }

    private static IEnumerable<Type> Nested(Type type) => type.GetNestedTypes(All).SelectMany(nested => Nested(nested).Prepend(nested));

    [Theory]
    [MemberData(nameof(Branches))]
    public void EveryHookTheGameDispatchesIsListed(string branch)
    {
        if (!File.Exists(PathOf(branch))) return;
        var dispatched = Dispatched(BranchCode.Of(PathOf(branch)));
        // The reading itself: the game dispatches well over a hundred hooks, AfterCardPlayed and a power's AfterApplied among them.
        Assert.True(dispatched.Count > 100 && dispatched.Contains("AfterCardPlayed") && dispatched.Contains("AfterApplied"), $"Hook dispatch read too little ({branch}): {dispatched.Count} hooks");
        var listed = List("CoverageHooks.txt").Concat(List("OutOfCombatHooks.txt")).Concat(List("ExemptHooks.txt")).ToHashSet(StringComparer.Ordinal);
        var unlisted = dispatched.Where(hook => !listed.Contains(hook)).ToList();
        Assert.True(unlisted.Count == 0,
            $"Hooks the game dispatches ({branch}) in none of Diagnostics/CoverageHooks.txt (a class overriding it needs a ledger entry), OutOfCombatHooks.txt " +
            "or ExemptHooks.txt (with why): add each, rerun scripts/coverage-ledger.mjs and review the classes it lists:\n" + string.Join("\n", unlisted));
    }

    [Fact]
    public void TheListsNameHooksOfTheGameAndExemptNoneReviewed()
    {
        var hooks = new HashSet<string>(StringComparer.Ordinal);
        foreach (var path in new[] { BranchCode.BetaPath, BranchCode.MainPath }.Where(File.Exists))
        {
            var code = BranchCode.Of(path);
            hooks.UnionWith(HooksOf(Named(code, "MegaCrit.Sts2.Core.Models.AbstractModel")).Select(method => method.Name));
            hooks.UnionWith(HooksOf(Named(code, "MegaCrit.Sts2.Core.Models.PowerModel")).Select(method => method.Name));
        }
        if (hooks.Count == 0) return;
        var reviewed = List("CoverageHooks.txt");
        var exempt = List("ExemptHooks.txt");
        var stale = reviewed.Concat(exempt).Where(hook => !hooks.Contains(hook)).Order(StringComparer.Ordinal).ToList();
        Assert.True(stale.Count == 0, "Hooks the lists name that neither branch's AbstractModel or PowerModel has (renamed or gone?):\n" + string.Join("\n", stale));
        var both = reviewed.Intersect(exempt).Order(StringComparer.Ordinal).ToList();
        Assert.True(both.Count == 0, "Hooks both reviewed per class (CoverageHooks.txt) and exempt (ExemptHooks.txt):\n" + string.Join("\n", both));
    }
}
