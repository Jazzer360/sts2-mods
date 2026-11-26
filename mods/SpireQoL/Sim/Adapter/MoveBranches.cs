using System.Reflection;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;
using Sts2Mods.Core.Game;
using Applied = SpireQoL.Sim.MovePowers.Applied;

namespace SpireQoL.Sim;

/// <summary>
/// Moves whose powers depend on a branch the game takes from state a player can see (decision #109): each one's rule picks the powers
/// of the branch that will run, from the monster now. Every move whose code applies a power on only some branches (MovePowers:
/// Branches.OnEveryPath) has a rule here or a reviewed reason in <see cref="Reviewed"/>; the catalog scan (MoveBranchTests) checks
/// that against every monster in the installed game, and each rule's <see cref="Rule.Shape"/> against the move's code, so a game
/// update that changes a move fails the test instead of showing the wrong powers.
/// </summary>
internal static class MoveBranches
{
    /// <summary>
    /// A move's rule. <see cref="Shape"/>: the powers its code applies, in order (as MovePowers reads them), which the rule's indexes
    /// refer to. <see cref="Pick"/>: from the monster and every applied power (in that order, amounts read), the ones that land.
    /// </summary>
    internal sealed record Rule(string[] Shape, Func<MonsterModel, IReadOnlyList<MovePowers.Applied>, IEnumerable<MovePowers.Applied>> Pick);

    /// <summary>The Dread ascension (AscensionHelper.IsDread): beta only (decision #163); main has no Dread.</summary>
    [BetaOnly, System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static bool IsDreadRun() => MegaCrit.Sts2.Core.Helpers.AscensionHelper.IsDread();

    private static readonly Func<bool>? IsDread = BetaOnly.Gate<Func<bool>>(IsDreadRun);

    /// <summary>The run is on the Dread ascension (false on main, which has none).</summary>
    internal static bool DreadRun => IsDread?.Invoke() == true;

    /// <summary>Rules by the move's perform method: "Monster.Method". Each reads only what a player sees (decision #72).</summary>
    internal static readonly Dictionary<string, Rule> Rules = new()
    {
        // Dampen on a target that doesn't have it yet (one that does only adds the caster): yours decides what's shown.
        ["MagiKnight.DampenMove"] = new(new[] { nameof(DampenPower) }, (monster, all) => Sts2State.Me?.Creature.HasPower<DampenPower>() == true ? Array.Empty<Applied>() : all),
        // Minion on each egg it lays, one per free slot of the encounter (up to 3).
        ["Ovicopter.LayEggsMove"] = new(new[] { nameof(MinionPower) }, (monster, all) =>
            monster.Creature.CombatState is { } combat && combat.Encounter?.Slots.Any(slot => combat.Enemies.All(creature => creature.SlotName != slot)) == true ? all : Array.Empty<Applied>()),
        // Strength to every ally but herself: 1, or 2 at the Dread ascension (a local, so the code's amount can't be read).
        ["Queen.BurnBrightForMeMove"] = new(new[] { nameof(StrengthPower) }, (monster, all) =>
            monster.Creature.CombatState is { } combat && combat.Enemies.Any(creature => creature != monster.Creature && creature.IsAlive)
                ? new[] { all[0] with { Amount = IsDread?.Invoke() == true ? 2 : 1 } }
                : Array.Empty<Applied>()),
        // Its first respawn gains Painful Stabs, its second Nemesis (Respawns counts them; the move adds one first).
        ["TestSubject.RespawnMove"] = new(new[] { nameof(PainfulStabsPower), nameof(NemesisPower) }, (monster, all) =>
            Sts2State.Private(monster, "Respawns", fallback: -1) switch { 0 => new[] { all[0] }, 1 => new[] { all[1] }, _ => Array.Empty<Applied>() }),
        // A loop over the move's targets, which always include you: every one of them gets it.
        ["SpectralKnight.HexMove"] = new(new[] { nameof(HexPower) }, (_, all) => all),
        ["TheInsatiable.LiquifyMove"] = new(new[] { nameof(SandpitPower) }, (_, all) => all),
        // One Swipe for each card it steals: one from each target's draw and discard piles, which are never both empty in a fight.
        ["ThievingHopper.ThieveryMove"] = new(new[] { nameof(SwipePower) }, (_, all) => all),
    };

    /// <summary>Moves with branch powers left as Maybe on purpose, and why (the scan's reviewed list).</summary>
    internal static readonly Dictionary<string, string> Reviewed = new();

    internal static string Key(MethodInfo perform) => $"{perform.DeclaringType?.Name}.{perform.Name}";

    /// <summary>
    /// The powers that land: read from the move's code where it can be (FromCode), else by the move's rule when its shape still matches; null
    /// otherwise (the caller shows Maybe).
    /// </summary>
    public static IReadOnlyList<MovePowers.Applied>? Settle(MonsterModel monster, MethodInfo perform, IReadOnlyList<MovePowers.Applied> all)
    {
        if (FromCode(monster, perform, all) is { } read) return read;
        if (!Rules.TryGetValue(Key(perform), out var rule) || !rule.Shape.SequenceEqual(all.Select(applied => applied.Power.Name))) return null;
        try { return rule.Pick(monster, all).Select(applied => applied with { Maybe = false }).ToList(); }
        catch { return null; }
    }

    /// <summary>
    /// The powers that land, read from the move's code (decision #377): its program's PowerCmd.Apply&lt;T&gt; steps, one for each power applied in the
    /// same order, each on its condition asked of the monster now (ProgramEvaluator.Holds: its own powers, its side, the run's ascension), as the
    /// rules picked them; an amount the IL reading couldn't give (a local) is its argument worked out now. Null where a condition can't be told
    /// now or reads a field of its own the move stores first (Test Subject's Respawns), or the steps don't match: the rule, or Maybe, then.
    /// </summary>
    internal static IReadOnlyList<MovePowers.Applied>? FromCode(MonsterModel monster, MethodInfo perform, IReadOnlyList<MovePowers.Applied> all)
    {
        try
        {
            if (ReadableSteps(perform, all.Select(applied => applied.Power.Name).ToList()) is not { } read) return null;
            var (program, applies) = read;
            var board = Procs.Programs.ProgramBoard.Of(monster);
            var landing = new List<MovePowers.Applied>();
            for (var index = 0; index < applies.Count; index++)
            {
                var apply = applies[index];
                if (apply.Condition is { } condition)
                {
                    if (Procs.Programs.ProgramEvaluator.Holds(condition, board, program) is not { } holds) return null;
                    if (!holds) continue;
                }
                var amount = all[index].Amount;
                if (amount == null && apply.Values.Count > 2)
                    amount = Procs.Programs.ProgramEvaluator.Value(apply.Values[2], board, program) switch { decimal number => number, int number => number, _ => null };
                if (amount == null) return null; // an amount neither reading gives: not read here
                landing.Add(all[index] with { Amount = amount, Maybe = false });
            }
            return landing;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// A move whose branch powers FromCode reads (decision #377), known from its code alone (the scan's: MoveBranchTests): its program read whole, each
    /// PowerCmd.Apply&lt;T&gt; outside a loop, one for each of <paramref name="powers"/> in order, no condition reading a field of its own the move stores.
    /// </summary>
    internal static bool Readable(MethodInfo perform, IReadOnlyList<string> powers) => ReadableSteps(perform, powers) != null;

    private static (Procs.Programs.ModelProgram Program, List<Procs.Programs.CommandStep> Applies)? ReadableSteps(MethodInfo perform, IReadOnlyList<string> powers)
    {
        if (perform.DeclaringType is not { } owner) return null;
        var program = Procs.Programs.ModelPrograms.Of(owner, perform.Name);
        if (program.Unknowns.Any()) return null;
        static bool IsApply(Procs.Programs.ProgramStep step) => step is Procs.Programs.CommandStep { Command.FullName: "PowerCmd.Apply" };
        // Each outside a loop: one in a loop (Queen's Strength for each ally) runs as many times as its loop does, which this can't tell.
        var applies = program.Steps.Where(IsApply).Cast<Procs.Programs.CommandStep>().ToList();
        if (program.All().Count(IsApply) != applies.Count) return null;
        if (applies.Count != powers.Count || applies.Where((apply, index) => apply.Command.GenericArguments is not [var power] || power != powers[index]).Any()) return null;
        // A test of a field of its own the move stores (Test Subject's Respawns, counted up before its switch): asked now, it would read the count before.
        var stored = program.All().OfType<Procs.Programs.StoreStep>().Select(store => store.Field).ToHashSet(StringComparer.Ordinal);
        if (applies.Any(apply => apply.Condition?.Literals.Any(literal => literal.Test.Parts().Any(part =>
                part is Procs.Programs.QueryValue { Of: Procs.Programs.NamedValue { Name: "this" } } own && stored.Contains(own.Name))) == true)) return null;
        return (program, applies);
    }
}