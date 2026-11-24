using SpireQoL.Diagnostics;
using Xunit;

namespace SpireQoL.Catalog.Tests;

/// <summary>
/// Decision #156: a relic or power model whose game class changes the fight must change the simulation too, not only fire its tab
/// (ModelEffectCheck; a report: Mini Regent fired and its Strength never landed). A model reported here gets its effect, or a
/// reviewed reason in ModelEffectCheck.Reviewed. And it must take each amount where its game class's hooks do (ModelAmountCheck,
/// docs/CARD_PROGRAMS.md B2: Vicious and Dark Embrace drew 1 where their hooks draw their amount): a model reported there gets
/// fixed, or a reviewed reason in ModelAmountCheck.Reviewed. An amount the check can't read back on one side is reported too: a
/// skipped comparison is a gap, not a pass.
/// </summary>
[Collection(GameFightCollection.Name)]
public class ModelEffectTests
{
    public static TheoryData<string> Branches => new() { "beta", "main" };

    [Fact]
    public void EveryModelDoesWhatItsGameClassDoes()
    {
        GameModels.EnsureLoaded();
        var unexplained = ModelEffectCheck.Unexplained();
        Assert.True(unexplained.Count == 0,
            $"{unexplained.Count} models fire without doing what their game class does:\n" + string.Join("\n", unexplained.Select(entry => $"  {entry.Model}: {entry.GameCommands}")));
    }

    [Fact]
    public void EveryModelTakesItsAmountsWhereItsGameClassDoes()
    {
        GameModels.EnsureLoaded();
        var report = ModelAmountCheck.Run();
        // Fewer each round: a model read from its code (decision #195) takes its amounts where its code does, and isn't compared.
        Assert.True(report.Compared > 15, $"only {report.Compared} models compared: the check stopped reading the game's hooks or the models");
        Assert.True(report.Differences.Count + report.Unread.Count + report.StaleReviews.Count == 0, Describe(report));
    }

    [Theory]
    [MemberData(nameof(Branches))]
    public void EveryModelTakesItsAmountsWhereEachBranchDoes(string branch)
    {
        // Each branch's game classes read from its decompile's assembly (main's are what a player on main runs the models against).
        // A reviewed entry for a class only one branch has is stale on the other, so only the loaded game's run checks for those.
        var path = branch == "main" ? BranchCode.MainPath : BranchCode.BetaPath;
        if (!File.Exists(path)) return; // no decompile of this branch here (docs/DEVELOPMENT.md "Decompiled game source")
        GameModels.EnsureLoaded();
        var code = BranchCode.Of(path);
        var models = code.Assembly.GetTypes().Where(type => type is { IsClass: true, IsAbstract: false, IsNested: false }
            && type.Namespace is "MegaCrit.Sts2.Core.Models.Powers" or "MegaCrit.Sts2.Core.Models.Relics");
        var report = ModelAmountCheck.Run(models, code.Read);
        // Fewer each round: a model read from its code (decision #195) takes its amounts where its code does, and isn't compared.
        Assert.True(report.Compared > 7, $"only {report.Compared} models compared on {branch}");
        Assert.True(report.Differences.Count + report.Unread.Count == 0, Describe(report));
    }

    private static string Describe(ModelAmountCheck.Report report) =>
        $"{report.Compared} models compared.\n{report.Differences.Count} take an amount from somewhere other than their game class:\n"
        + string.Join("\n", report.Differences.Select(entry => "  " + entry))
        + $"\n{report.Unread.Count} couldn't be compared (a source on one side the check can't read back):\n" + string.Join("\n", report.Unread.Select(entry => "  " + entry))
        + $"\n{report.StaleReviews.Count} reviewed entries no longer differ (remove them):\n" + string.Join("\n", report.StaleReviews.Select(entry => "  " + entry));
}
