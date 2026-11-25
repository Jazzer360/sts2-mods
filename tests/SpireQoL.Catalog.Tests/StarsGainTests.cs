using System.Reflection;
using SpireQoL.Procs.Programs;
using Xunit;

namespace SpireQoL.Catalog.Tests;

/// <summary>
/// What the model reader takes from the game's code about a stars gain, held to it on both branches: AfterStarsGained's amount is more than 0 for
/// every gain (HookReader.Compare: Black Hole's amount &gt; 0), because every PlayerCmd.GainStars in a model's code gains its own Stars value (each
/// model's canonical Stars is at least 1) or a power's own amount (a power at 0 goes: PowerCmd's rules, decision #234).
/// </summary>
public class StarsGainTests
{
    public static TheoryData<string> Branches => new() { "beta", "main" };

    [Theory]
    [MemberData(nameof(Branches))]
    public void EveryStarsGainIsOfAStarsValueOrAPowersAmount(string branch)
    {
        var path = branch == "main" ? BranchCode.MainPath : BranchCode.BetaPath;
        if (!File.Exists(path)) return;
        var code = BranchCode.Of(path);
        const BindingFlags declared = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
        var found = 0;
        var odd = new List<string>();
        // The dev console's command and the test mocks are no model a fight runs.
        foreach (var type in code.Models().Where(type => type.Namespace is not ("MegaCrit.Sts2.Core.Models.Cards.Mocks")))
            foreach (var method in type.GetMethods(declared).Where(method => !method.IsAbstract).Select(method => method.Name).Distinct(StringComparer.Ordinal))
            {
                ModelProgram program;
                try { program = ProgramReader.Read(type, code.Read, method); }
                catch (Exception) { continue; }
                foreach (var gain in program.All().OfType<CommandStep>().Where(step => step.Command.FullName == "PlayerCmd.GainStars"))
                {
                    found++;
                    var amount = gain.Values.Count > 0 ? gain.Values[0] : null;
                    var ok = amount switch
                    {
                        CardValue { Key: "Stars" } => true,
                        QueryValue { Name: "Amount", Of: NamedValue { Name: "this" } } => Derives(type, "PowerModel"),
                        QueryValue { Name: "BaseValue", Of: QueryValue { Name: "Stars" } } => true,
                        _ => false,
                    };
                    if (!ok) odd.Add($"{type.Name}.{method}: {amount}");
                    else if (amount is not QueryValue { Name: "Amount" } && CanonicalStars(type) is not >= 1m) odd.Add($"{type.Name}: canonical Stars {CanonicalStars(type)}");
                }
            }
        Assert.True(found > 10, $"only {found} stars gains found");
        Assert.True(odd.Count == 0, string.Join("\n", odd));
    }

    /// <summary>The model's canonical Stars value, from the installed game's own model of that class (ModelDb); null when it has none.</summary>
    private static decimal? CanonicalStars(Type type)
    {
        GameModels.EnsureLoaded();
        var model = MegaCrit.Sts2.Core.Models.ModelDb.All.FirstOrDefault(each => each.GetType().Name == type.Name);
        var vars = model?.GetType().GetProperty("DynamicVars", BindingFlags.Instance | BindingFlags.Public)?.GetValue(model) as MegaCrit.Sts2.Core.Localization.DynamicVars.DynamicVarSet;
        return vars?.TryGetValue("Stars", out var stars) == true ? stars.BaseValue : null;
    }

    private static bool Derives(Type type, string baseName)
    {
        for (var at = type.BaseType; at != null; at = at.BaseType)
            if (at.Name == baseName) return true;
        return false;
    }
}
