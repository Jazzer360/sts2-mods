using SpireQoL.Sim;
using Xunit;

namespace SpireQoL.Catalog.Tests;

/// <summary>
/// Afflictions read from their code (decision #225): on both branches every one reads whole with nothing the engine runs (what it does to its card
/// is a fact the powers' code tests: SimCard.Affliction), so the simulator's outcomes are what they were; one a game update makes unreadable is an
/// unread spec, told once, and a card with it plays unknown.
/// </summary>
public class AfflictionReadTests
{
    public static TheoryData<string> Branches() => new() { "beta", "main" };

    [Theory]
    [MemberData(nameof(Branches))]
    public void EveryAfflictionReadsWholeWithNothingToRun(string branch)
    {
        var path = branch == "main" ? BranchCode.MainPath : BranchCode.BetaPath;
        if (!File.Exists(path)) return; // no decompile of this branch here
        var code = BranchCode.Of(path);
        var afflictions = HookReader.Afflictions(code.Assembly.GetTypes()).ToList();
        Assert.NotEmpty(afflictions);
        foreach (var type in afflictions)
        {
            var reading = HookReader.ReadAffliction(type, code.Read);
            Assert.True(reading.Whole, $"{branch}: {type.Name}: {string.Join("; ", reading.Gaps)}");
            Assert.Empty(reading.Spec.Hooks);
            Assert.True(Enum.TryParse<SimAffliction>(type.Name, out var known) && known != SimAffliction.Other, $"{type.Name}: no SimAffliction names it");
        }
        // The game's test mocks aren't afflictions a run puts on a card.
        Assert.DoesNotContain(afflictions, type => type.Name.StartsWith("Mock", StringComparison.Ordinal));
    }

    [Fact]
    public void AnAfflictionThatNoLongerReadsIsUnreadAndToldOnce()
    {
        if (!File.Exists(BranchCode.BetaPath)) return;
        var code = BranchCode.Of(BranchCode.BetaPath);
        var told = new List<string>();
        List<Procs.Programs.CodeOp> Failing(System.Reflection.MethodBase method) =>
            method.DeclaringType?.Name.Contains(nameof(MegaCrit.Sts2.Core.Models.Afflictions.Hexed), StringComparison.Ordinal) == true ? throw new InvalidOperationException("a game update") : code.Read(method);
        var specs = ReadModels.Specs(code.Assembly.GetTypes(), Failing, unread: (name, _) => told.Add(name));
        var hexed = Assert.Single(specs, spec => spec.Name == nameof(MegaCrit.Sts2.Core.Models.Afflictions.Hexed));
        Assert.Equal(ModelKind.Affliction, hexed.Kind);
        Assert.NotNull(hexed.Unread);
        Assert.Contains(nameof(MegaCrit.Sts2.Core.Models.Afflictions.Hexed), told);
    }
}
