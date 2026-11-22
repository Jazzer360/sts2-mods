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

    /// <summary>AscensionHelper.IsDread: the Dread ascension, on the beta branch only (main: no Dread).</summary>
    private static readonly MethodInfo? IsDread = typeof(MegaCrit.Sts2.Core.Helpers.AscensionHelper).GetMethod("IsDread", Type.EmptyTypes);

    /// <summary>Rules by the move's perform method: "Monster.Method". Each reads only what a player sees (decision #72).</summary>
    internal static readonly Dictionary<string, Rule> Rules = new()
    {
        // Below 3 Personal Hive (or none): +1 Personal Hive and +1 Strength; at 3 or more: +2 Strength.
        ["Entomancer.SpitMove"] = new(new[] { nameof(PersonalHivePower), nameof(StrengthPower), nameof(StrengthPower) },
            (monster, all) => monster.Creature.GetPower<PersonalHivePower>() is { Amount: < 3 } ? new[] { all[0], all[1] } : new[] { all[2] }),
        // Strength only while it still has Plow (its Plow is taken away once it's stunned out of the charge).
        ["CeremonialBeast.PlowMove"] = new(new[] { nameof(StrengthPower) }, (monster, all) => monster.Creature.HasPower<PlowPower>() ? all : Array.Empty<Applied>()),
        // Dampen on a target that doesn't have it yet (one that does only adds the caster): yours decides what's shown.
        ["MagiKnight.DampenMove"] = new(new[] { nameof(DampenPower) }, (monster, all) => Sts2State.Me?.Creature.HasPower<DampenPower>() == true ? Array.Empty<Applied>() : all),
        // Minion on each egg it lays, one per free slot of the encounter (up to 3).
        ["Ovicopter.LayEggsMove"] = new(new[] { nameof(MinionPower) }, (monster, all) =>
            monster.Creature.CombatState is { } combat && combat.Encounter?.Slots.Any(slot => combat.Enemies.All(creature => creature.SlotName != slot)) == true ? all : Array.Empty<Applied>()),
        // Strength to every ally but herself: 1, or 2 at the Dread ascension (a local, so the code's amount can't be read).
        ["Queen.BurnBrightForMeMove"] = new(new[] { nameof(StrengthPower) }, (monster, all) =>
            monster.Creature.CombatState is { } combat && combat.Enemies.Any(creature => creature != monster.Creature && creature.IsAlive)
                ? new[] { all[0] with { Amount = IsDread?.Invoke(null, null) is true ? 2 : 1 } }
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

    /// <summary>The powers that land, when the move has a rule whose shape still matches; null otherwise (the caller shows Maybe).</summary>
    public static IReadOnlyList<MovePowers.Applied>? Settle(MonsterModel monster, MethodInfo perform, IReadOnlyList<MovePowers.Applied> all)
    {
        if (!Rules.TryGetValue(Key(perform), out var rule) || !rule.Shape.SequenceEqual(all.Select(applied => applied.Power.Name))) return null;
        try { return rule.Pick(monster, all).Select(applied => applied with { Maybe = false }).ToList(); }
        catch { return null; }
    }
}
