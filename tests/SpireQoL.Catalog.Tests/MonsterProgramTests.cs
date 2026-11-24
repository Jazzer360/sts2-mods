using SpireQoL.Procs.Programs;
using SpireQoL.Sim;
using Xunit;

namespace SpireQoL.Catalog.Tests;

/// <summary>
/// Every monster's code read into programs (decision #195): each move (the method a MoveState performs) and each hook body (the game's
/// virtual methods it overrides) of every monster on both game branches is read with nothing the reader couldn't follow, and each command
/// in it is translated into the enemy turn's action (SimAdapter.MoveCommandsTranslated) or is a named gap with why
/// (MoveCommandsNotTranslated). A monster a game update adds, or a command it calls that nothing names, fails here before anyone fights it.
/// </summary>
public class MonsterProgramTests
{
    public static TheoryData<string> Branches => new() { "beta", "main" };

    private static string PathOf(string branch) => branch == "main" ? BranchCode.MainPath : BranchCode.BetaPath;

    /// <summary>Every move and hook of every monster on the branch, each with its program.</summary>
    private static List<(string Name, bool IsMove, ModelProgram Program)> Programs(BranchCode code)
    {
        var programs = new List<(string, bool, ModelProgram)>();
        foreach (var monster in MonsterCode.Monsters(code.Assembly.GetTypes()))
        {
            foreach (var move in MonsterCode.Moves(monster)) programs.Add(($"{monster.Name}.{move.Name}", true, ProgramReader.Read(monster, code.Read, move.Name)));
            foreach (var hook in MonsterCode.Hooks(monster)) programs.Add(($"{monster.Name}.{hook.Name}", false, ProgramReader.Read(monster, code.Read, hook.Name)));
        }
        return programs;
    }

    [Theory]
    [MemberData(nameof(Branches))]
    public void EveryMoveAndHookIsReadAndEachCommandTranslatedOrANamedGap(string branch)
    {
        if (!File.Exists(PathOf(branch))) return; // no decompile of this branch here (docs/DEVELOPMENT.md "Decompiled game source")
        var code = BranchCode.Of(PathOf(branch));
        var programs = Programs(code);
        var unread = programs.SelectMany(entry => entry.Program.Unknowns.Select(reason => $"{entry.Name}: {reason}")).ToList();
        var missing = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var (name, _, program) in programs)
            foreach (var command in program.Commands)
                if (!SimAdapter.MoveCommandsTranslated.ContainsKey(command.Command.FullName) && !SimAdapter.MoveCommandsNotTranslated.ContainsKey(command.Command.FullName))
                    missing.Add($"{command.Command.FullName} ({name})");
        Assert.True(unread.Count == 0, $"Monster code the reader can't follow ({branch}):\n" + string.Join("\n", unread));
        Assert.True(missing.Count == 0, $"Commands a monster's move or hook calls with no translation and no named gap ({branch}):\n" + string.Join("\n", missing));
        // The reader finds the moves and hooks by their shape: a game update that changes the shape loses them all at once.
        Assert.True(programs.Count(entry => entry.IsMove) > 300, $"only {programs.Count(entry => entry.IsMove)} moves found ({branch})");
        Assert.True(programs.Count(entry => !entry.IsMove) > 80,$"only {programs.Count(entry => !entry.IsMove)} hooks found ({branch})");
    }

    [Fact]
    public void EveryTranslationAndGapIsACommandAMonsterCalls()
    {
        var called = new HashSet<string>(StringComparer.Ordinal);
        foreach (var branch in new[] { "beta", "main" })
        {
            if (!File.Exists(PathOf(branch))) return;
            var code = BranchCode.Of(PathOf(branch));
            foreach (var (_, _, program) in Programs(code))
                foreach (var command in program.Commands) called.Add(command.Command.FullName);
        }
        var stale = SimAdapter.MoveCommandsTranslated.Keys.Concat(SimAdapter.MoveCommandsNotTranslated.Keys).Where(command => !called.Contains(command)).ToList();
        Assert.True(stale.Count == 0, "Named, but no monster's move or hook calls it on either branch:\n" + string.Join("\n", stale));
        Assert.Empty(SimAdapter.MoveCommandsTranslated.Keys.Intersect(SimAdapter.MoveCommandsNotTranslated.Keys));
    }

    [Theory]
    [MemberData(nameof(Branches))]
    public void AMovesProgramIsItsCode(string branch)
    {
        if (!File.Exists(PathOf(branch))) return;
        var code = BranchCode.Of(PathOf(branch));
        var monsters = MonsterCode.Monsters(code.Assembly.GetTypes()).ToDictionary(type => type.Name);
        // Byrdonis' Peck: one attack of PeckDamage, PeckRepeat hits.
        var peck = Assert.Single(ProgramReader.Read(monsters["Byrdonis"], code.Read, "PeckMove").Commands);
        Assert.Equal("DamageCmd.Attack", peck.Command.FullName);
        Assert.Equal("PeckRepeat", peck.HitCountValue?.ToString()?.Replace("this.", "", StringComparison.Ordinal));
        // Axebot's Hammer Uppercut: its attack, then Weak and Frail on its targets, in that order.
        Assert.Equal(new[] { "DamageCmd.Attack", "PowerCmd.Apply<WeakPower>", "PowerCmd.Apply<FrailPower>" },
            ProgramReader.Read(monsters["Axebot"], code.Read, "HammerUppercutMove").Commands.Select(command => command.Name));
    }

    [Fact]
    public void AHooksStoresAndModelActionsAreSteps()
    {
        if (!File.Exists(BranchCode.BetaPath)) return;
        var code = BranchCode.Of(BranchCode.BetaPath);
        var queen = MonsterCode.Monsters(code.Assembly.GetTypes()).Single(type => type.Name == "Queen");
        // Queen's AfterDeath: when her Amalgam dies, she notes it (HasAmalgamDied, read by her conditional move branch) and switches Burn
        // Bright for Me to Enrage (SetMoveImmediate).
        var program = ProgramReader.Read(queen, code.Read, "AfterDeath");
        Assert.Contains(program.All().OfType<StoreStep>(), store => store.Field == "HasAmalgamDied" && store.Value.ToString() == "1");
        Assert.Contains(program.Commands, command => command.Command.FullName == "MonsterModel.SetMoveImmediate");
        // A store to another object's property (Surprise's heistPower.Target = ...) isn't the model's own state.
        var surprise = code.Models().Single(type => type.Name == "SurprisePower");
        Assert.Empty(ProgramReader.Read(surprise, code.Read, "AfterDeath").All().OfType<StoreStep>());
        // base.AfterAddedToRoom() is MonsterModel's own code (nothing), not a step.
        Assert.DoesNotContain(ProgramReader.Read(code.Assembly.GetTypes().Single(type => type.Name == "Byrdonis" && type.Namespace == "MegaCrit.Sts2.Core.Models.Monsters"), code.Read, "AfterAddedToRoom").Commands,
            command => command.Command.DeclaringType == "MonsterModel");
    }
}
