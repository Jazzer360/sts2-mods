using System.Reflection;
using SpireQoL.Diagnostics;
using Xunit;

namespace SpireQoL.Catalog.Tests;

/// <summary>
/// The coverage ledger's hook lists can't drift from the game (decision #323). Every model hook the game dispatches is in
/// Diagnostics/CoverageHooks.txt (a class overriding it needs a ledger entry), OutOfCombatHooks.txt or ExemptHooks.txt (each with why), read on
/// both branches: every AbstractModel hook the game's Hook class calls (its dispatch methods, their async bodies, iterators and lambdas), and
/// each model kind's own hooks (decision #330): a power's (PowerModel's, which PowerCmd and a death call), a monster's (MonsterModel's, which
/// a creature joining, leaving and dying of Doom call), an enchantment's (its damage, block and play-count terms, which the game's math calls),
/// an affliction's, an orb's, a card's, a relic's and a potion's. A hook in none of them hides every class whose only fight hooks it has, as
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

    /// <summary>
    /// The model kinds whose own hooks the lists hold (decision #330): every kind the ledger reviews (CoverageLedger's folders) and PowerModel.
    /// A kind's own hook is a public virtual method its base class declares (not a property's accessor) that answers or acts for the game
    /// (MonsterModel.AfterAddedToRoom, EnchantmentModel.EnchantPlayCount) or only draws (CardModel.OnEnqueuePlayVfx).
    /// </summary>
    private static readonly string[] Kinds =
    {
        "PowerModel", "MonsterModel", "EnchantmentModel", "AfflictionModel", "OrbModel", "CardModel", "RelicModel", "PotionModel",
    };

    /// <summary>
    /// The kind's own hooks: those that answer or act (a Task of anything, a bool, a number or nothing), not a value it builds for the screen
    /// (a bestiary's move list, an animator), and not an interface's method it implements for good.
    /// </summary>
    private static IEnumerable<MethodInfo> KindHooks(BranchCode code, string kind) =>
        HooksOf(Named(code, "MegaCrit.Sts2.Core.Models." + kind)).Where(method => !method.IsFinal
            && (method.ReturnType.FullName is "System.Boolean" or "System.Void" or "System.Decimal" or "System.Int32"
                || method.ReturnType.FullName?.StartsWith("System.Threading.Tasks.Task", StringComparison.Ordinal) == true));

    /// <summary>The hooks the game dispatches on this branch: each AbstractModel hook the Hook class's code calls, and each model kind's own.</summary>
    private static SortedSet<string> Dispatched(BranchCode code)
    {
        var hooks = HooksOf(Named(code, "MegaCrit.Sts2.Core.Models.AbstractModel")).Select(method => method.Name).ToHashSet(StringComparer.Ordinal);
        var hookClass = Named(code, "MegaCrit.Sts2.Core.Hooks.Hook");
        var dispatched = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var type in Nested(hookClass).Prepend(hookClass))
            foreach (var method in type.GetMethods(All).Cast<MethodBase>().Concat(type.GetConstructors(All)))
                foreach (var instruction in code.Read(method))
                    if (instruction.IsCall && instruction.Member is { DeclaringType: "AbstractModel" } member && hooks.Contains(member.Name)) dispatched.Add(member.Name);
        foreach (var kind in Kinds)
            foreach (var method in KindHooks(code, kind)) dispatched.Add(method.Name);
        return dispatched;
    }

    private static IEnumerable<Type> Nested(Type type) => type.GetNestedTypes(All).SelectMany(nested => Nested(nested).Prepend(nested));

    [Theory]
    [MemberData(nameof(Branches))]
    public void EveryHookTheGameDispatchesIsListed(string branch)
    {
        if (!File.Exists(PathOf(branch))) return;
        var dispatched = Dispatched(BranchCode.Of(PathOf(branch)));
        // The reading itself: the game dispatches well over a hundred hooks, AfterCardPlayed, a power's AfterApplied, a monster's AfterAddedToRoom
        // and an enchantment's EnchantPlayCount among them.
        Assert.True(dispatched.Count > 100 && new[] { "AfterCardPlayed", "AfterApplied", "AfterAddedToRoom", "EnchantPlayCount" }.All(dispatched.Contains),
            $"Hook dispatch read too little ({branch}): {dispatched.Count} hooks");
        // An enchantment's or affliction's OnPlay is reviewed per class as well (CoverageLedger.OnCardHooks; a card's own OnPlay is its program).
        var listed = List("CoverageHooks.txt").Concat(List("OutOfCombatHooks.txt")).Concat(List("ExemptHooks.txt")).Concat(CoverageLedger.ReviewedOnCardHooks)
            .ToHashSet(StringComparer.Ordinal);
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
            foreach (var kind in Kinds) hooks.UnionWith(KindHooks(code, kind).Select(method => method.Name));
        }
        if (hooks.Count == 0) return;
        var reviewed = List("CoverageHooks.txt");
        var outOfCombat = List("OutOfCombatHooks.txt");
        var exempt = List("ExemptHooks.txt");
        // All three lists: OutOfCombatHooks.txt's ModifyRewards, a hook on neither branch, went unseen while only the other two were read.
        var stale = reviewed.Concat(outOfCombat).Concat(exempt).Where(hook => !hooks.Contains(hook)).Distinct().Order(StringComparer.Ordinal).ToList();
        Assert.True(stale.Count == 0, "Hooks the lists name that neither branch's AbstractModel or model kinds have (renamed or gone?):\n" + string.Join("\n", stale));
        var both = reviewed.Intersect(exempt).Order(StringComparer.Ordinal).ToList();
        Assert.True(both.Count == 0, "Hooks both reviewed per class (CoverageHooks.txt) and exempt (ExemptHooks.txt):\n" + string.Join("\n", both));
    }
}
