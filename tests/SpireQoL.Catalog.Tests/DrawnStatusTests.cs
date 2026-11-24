using MegaCrit.Sts2.Core.Models.Powers;
using SpireQoL.Procs.Programs;
using SpireQoL.Sim;
using Xunit;

namespace SpireQoL.Catalog.Tests;

/// <summary>
/// The draw profile keeps a Status apart when the board asks about drawn Statuses (SimAdapter.AsksDrawnStatus: a question to the fight's log,
/// or a read hook testing the drawn card's type), a rule from what's asked where it named Iteration. On both branches, the classes whose own
/// code asks it are Iteration's alone, so the rule gives what the name did; a new one a game update adds is caught by the rule, not missed.
/// </summary>
public class DrawnStatusTests
{
    public static TheoryData<string> Branches => new() { "beta", "main" };

    [Theory]
    [MemberData(nameof(Branches))]
    public void TheClassesThatAskAboutDrawnStatusesAreIterationsAlone(string branch)
    {
        var path = branch == "main" ? BranchCode.MainPath : BranchCode.BetaPath;
        if (!File.Exists(path)) return; // no decompile of this branch here (docs/DEVELOPMENT.md "Decompiled game source")
        using var code = new BranchCode(path);
        var asking = new List<string>();
        foreach (var type in code.Models().Where(type => type.Namespace is "MegaCrit.Sts2.Core.Models.Cards" or "MegaCrit.Sts2.Core.Models.Powers" or "MegaCrit.Sts2.Core.Models.Relics" && !type.IsAbstract))
        {
            var questions = new List<HistoryQuery>();
            try
            {
                foreach (var (_, query) in ProgramReader.HistoryQueries(type, code.Read))
                    if (query is QueryValue value && HistoryQueryReader.Read(value, code.EnumName).Query is { } read) questions.Add(read);
            }
            catch { continue; } // no code to read
            if (SimAdapter.AsksDrawnStatus(questions, Array.Empty<MegaCrit.Sts2.Core.Models.AbstractModel>())) asking.Add(type.Name);
        }
        Assert.Equal(new[] { nameof(IterationPower) }, asking);
    }
}
