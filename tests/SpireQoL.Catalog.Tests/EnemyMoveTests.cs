using System.Reflection;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Monsters;
using MegaCrit.Sts2.Core.MonsterMoves.Intents;
using MegaCrit.Sts2.Core.MonsterMoves.MonsterMoveStateMachine;
using SpireQoL.Procs.Programs;
using SpireQoL.Sim;
using Xunit;

namespace SpireQoL.Catalog.Tests;

/// <summary>
/// The enemy turn runs each enemy's move as its code runs it (decision #195): a real fight built from the game's objects, each enemy given
/// the move named, read by the adapter (SimAdapter.MoveOf) and run by the simulator. What an earlier enemy does before a later hit (Vulnerable
/// on you, Strength to its allies, block on an ally) changes that hit, as in the game; a hidden intent keeps its move hidden (decision #72).
/// </summary>
[Collection(GameFightCollection.Name)]
public class EnemyMoveTests
{
    private static readonly MethodInfo Generate = typeof(MonsterModel).GetMethod("GenerateMoveStateMachine", BindingFlags.Instance | BindingFlags.NonPublic)!;

    /// <summary>A monster of type T in the fight, about to make the move <paramref name="moveId"/>.</summary>
    private static Creature Enemy<T>(GameFight fight, string moveId) where T : MonsterModel
    {
        var creature = fight.Combat.CreateCreature(ModelDb.Monster<T>().ToMutable(), CombatSide.Enemy, slot: null);
        fight.Combat.AddCreature(creature);
        var monster = creature.Monster!;
        monster.SetUpForCombat();
        MakeNext(monster, (MoveState)monster.MoveStateMachine!.States[moveId]);
        return creature;
    }

    /// <summary>The monster's next move set as SetMoveImmediate sets it, without its creature node (none outside the game).</summary>
    private static void MakeNext(MonsterModel monster, MoveState move)
    {
        typeof(MonsterModel).GetProperty(nameof(MonsterModel.NextMove))!.SetValue(monster, move);
        monster.MoveStateMachine!.ForceCurrentState(move);
    }

    private static SimState Read(GameFight fight, out Dictionary<Creature, int> ids) => SimAdapter.Build(fight.Me, fight.Combat, out ids);

    private static int Hit(Creature attacker, GameFight fight) =>
        attacker.Monster!.NextMove.Intents.OfType<AttackIntent>().First().GetSingleDamage(fight.Combat.Players.Select(player => player.Creature), attacker);

    [Fact]
    public void MawlersRoarRaisesTheNextEnemysHit()
    {
        // Mawler's Roar: PowerCmd.Apply<VulnerablePower>(targets, 3). Byrdonis' Swoop after it lands on a Vulnerable you: 1.5 times.
        var fight = new GameFight();
        var mawler = Enemy<Mawler>(fight, "ROAR_MOVE");
        var byrdonis = Enemy<Byrdonis>(fight, "SWOOP_MOVE");
        var state = Read(fight, out var ids);
        var roar = Assert.IsType<MovePowerAction>(Assert.Single(state.Enemy(ids[mawler])!.Move!));
        Assert.Equal((PowerId.Vulnerable, 3, true), (roar.Power, roar.Amount, roar.Debuff));
        Assert.Equal(new[] { Who.You }, roar.To);
        Assert.Equal((int)(Hit(byrdonis, fight) * 1.5m), SimEngine.Run(state).TakenMax);
    }

    [Fact]
    public void AHiddenIntentKeepsItsMoveHidden()
    {
        // The same Roar behind a hidden intent: what it does isn't known, so it isn't followed (the enemy is its intent alone).
        var fight = new GameFight();
        var mawler = Enemy<Mawler>(fight, "ROAR_MOVE");
        var roar = (MoveState)mawler.Monster!.MoveStateMachine!.States["ROAR_MOVE"];
        var perform = SimAdapter.PerformOf(roar)!.CreateDelegate<Func<IReadOnlyList<Creature>, Task>>(mawler.Monster);
        typeof(MonsterModel).GetProperty(nameof(MonsterModel.NextMove))!.SetValue(mawler.Monster, new MoveState("HIDDEN_ROAR", perform, new HiddenIntent()));
        var byrdonis = Enemy<Byrdonis>(fight, "SWOOP_MOVE");
        var state = Read(fight, out var ids);
        Assert.Null(state.Enemy(ids[mawler])!.Move);
        Assert.Equal(Hit(byrdonis, fight), SimEngine.Run(state).TakenMax);
    }

    [Fact]
    public void AnAttackThenVulnerableHitsFirstThenRaisesTheNext()
    {
        // Ovicopter's Tenderizer: its attack, then 2 Vulnerable on its targets. Its own hit is as shown; Byrdonis' after it is 1.5 times.
        var fight = new GameFight();
        var ovicopter = Enemy<Ovicopter>(fight, "TENDERIZER_MOVE");
        var byrdonis = Enemy<Byrdonis>(fight, "SWOOP_MOVE");
        var state = Read(fight, out var ids);
        var move = state.Enemy(ids[ovicopter])!.Move!;
        Assert.IsType<MoveAttackAction>(move[0]);
        Assert.Equal(PowerId.Vulnerable, Assert.IsType<MovePowerAction>(move[1]).Power);
        Assert.Equal(Hit(ovicopter, fight) + (int)(Hit(byrdonis, fight) * 1.5m), SimEngine.Run(state).TakenMax);
    }

    [Fact]
    public void TheObscurasWailGivesItsWholeSideStrength()
    {
        // The Obscura's Wail: PowerCmd.Apply<StrengthPower>(GetTeammatesOf(Creature), 3): itself and the Parafright after it.
        var fight = new GameFight();
        var obscura = Enemy<TheObscura>(fight, "SAIL_MOVE");
        var parafright = Enemy<Parafright>(fight, "SLAM_MOVE");
        var state = Read(fight, out var ids);
        var wail = Assert.IsType<MovePowerAction>(Assert.Single(state.Enemy(ids[obscura])!.Move!));
        Assert.Equal(PowerId.Strength, wail.Power);
        Assert.Equal(new[] { ids[obscura], ids[parafright] }, wail.To.OrderBy(id => id));
        Assert.Equal(Hit(parafright, fight) + wail.Amount, SimEngine.Run(state).TakenMax);
    }

    [Fact]
    public void BurnBrightForMeGivesStrengthToEveryAllyButTheQueen()
    {
        // Queen's Burn Bright for Me: a loop over her side less herself (Where(c => c != Creature)), Strength each, then 20 block on herself.
        var fight = new GameFight();
        var queen = Enemy<Queen>(fight, "BURN_BRIGHT_FOR_ME_MOVE");
        var amalgam = Enemy<TorchHeadAmalgam>(fight, "STRONG_TACKLE_MOVE");
        var state = Read(fight, out var ids);
        var move = state.Enemy(ids[queen])!.Move!;
        var strength = Assert.IsType<MovePowerAction>(move[0]);
        Assert.Equal(new[] { ids[amalgam] }, strength.To);
        Assert.Equal(new[] { ids[queen] }, Assert.IsType<MoveBlockAction>(move[1]).To);
        Assert.Equal(Hit(amalgam, fight) + strength.Amount, SimEngine.Run(state).TakenMax);
    }

    [Fact]
    public void GuardbotsGuardBlocksItsSidesFabricators()
    {
        // Guardbot's Guard: CreatureCmd.GainBlock(each, 15, Unpowered) over its side's Fabricators (Where(c => c.Monster is Fabricator)).
        var fight = new GameFight();
        var guardbot = Enemy<Guardbot>(fight, "GUARD_MOVE");
        Enemy<Byrdonis>(fight, "SWOOP_MOVE");
        var fabricator = Enemy<Fabricator>(fight, "DISINTEGRATE_MOVE");
        var state = Read(fight, out var ids);
        var guard = Assert.IsType<MoveBlockAction>(Assert.Single(state.Enemy(ids[guardbot])!.Move!));
        Assert.Equal(new[] { ids[fabricator] }, guard.To);
        Assert.Equal(15, guard.Amount);
    }

    [Fact]
    public void EveryMovesAttacksArePlacedAgainstItsIntent()
    {
        // Each attack intent of a move is one unconditional attack step of its code, in order (SimAdapter.AttackPlacementGap), so the intent's
        // hits stay the game's numbers at their place in the move. A move that isn't stays its intent's hits alone; each is reviewed here.
        GameModels.EnsureLoaded();
        var unplaced = new List<string>();
        foreach (var canon in ModelDb.All.OfType<MonsterModel>())
        {
            var monster = canon.ToMutable();
            MonsterMoveStateMachine machine;
            try { machine = (MonsterMoveStateMachine)Generate.Invoke(monster, null)!; } catch { continue; }
            foreach (var move in machine.States.Values.OfType<MoveState>())
            {
                if (SimAdapter.PerformOf(move) is not { DeclaringType: { } owner } perform) continue;
                var program = ModelPrograms.Of(owner, perform.Name);
                if (SimAdapter.AttackPlacementGap(program, move.Intents.OfType<AttackIntent>().Count()) is { } gap && !UnplacedAttacks.ContainsKey($"{monster.GetType().Name}.{move.Id}"))
                    unplaced.Add($"{monster.GetType().Name}.{move.Id}: {gap}");
            }
        }
        Assert.True(unplaced.Count == 0, "Moves whose attacks can't be placed against their intent: read the move and review it here, or teach the placement:\n" + string.Join("\n", unplaced));
    }

    /// <summary>Moves whose attacks aren't one unconditional step for each attack intent, reviewed from their code: each stays its intent's hits alone.</summary>
    private static readonly Dictionary<string, string> UnplacedAttacks = new();

    [Fact]
    public void EveryStepThatCantBeBuiltIsReviewed()
    {
        // A move's power or block that can't be built whatever the board (on a condition, a power made first: SimAdapter.MoveGaps) keeps the
        // enemy turn as it was. Each is reviewed: none comes before a later hit and changes it.
        GameModels.EnsureLoaded();
        var found = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var monster in MonsterCode.Monsters(typeof(MonsterModel).Assembly.GetTypes()))
            foreach (var move in MonsterCode.Moves(monster))
                foreach (var (step, gap) in SimAdapter.MoveGaps(ModelPrograms.Of(monster, move.Name)))
                    found.Add($"{monster.Name}.{move.Name}: {step.Name} ({gap})");
        var unreviewed = found.Where(entry => !ReviewedGaps.ContainsKey(entry)).ToList();
        Assert.True(unreviewed.Count == 0, "Move steps the enemy turn can't build, unreviewed (decision #195: model it, or say why it can't change a later hit):\n" + string.Join("\n", unreviewed));
        var stale = ReviewedGaps.Keys.Where(entry => !found.Contains(entry)).ToList();
        Assert.True(stale.Count == 0, "Reviewed, but no longer a gap:\n" + string.Join("\n", stale));
    }

    /// <summary>Each move step the enemy turn can't build, and why it can't change a later hit this turn.</summary>
    private static readonly Dictionary<string, string> ReviewedGaps = new()
    {
        ["CeremonialBeast.PlowMove: PowerCmd.Apply<StrengthPower> (on a condition)"] = "its own Strength while it has Plow, after its own attack: no later hit of its this turn",
        ["Entomancer.SpitMove: PowerCmd.Apply<PersonalHivePower> (on a condition)"] = "its own Personal Hive below 3: a move with no attack",
        ["Entomancer.SpitMove: PowerCmd.Apply<StrengthPower> (on a condition)"] = "its own Strength by its Personal Hive: a move with no attack",
        ["Fabricator.FabricateMove: PowerCmd.Apply<MinionPower> (on a condition)"] = "Minion on the bot it summons (CreatureCmd.Add), which joins for its next turn",
        ["Fabricator.FabricatingStrikeMove: PowerCmd.Apply<MinionPower> (on a condition)"] = "Minion on the bot it summons after its attack, which joins for its next turn",
        ["MagiKnight.DampenMove: PowerCmd.Apply (on a condition)"] = "Dampen on a target without it: no damage hook (DampenPower only reacts to its applier's death)",
        ["Ovicopter.LayEggsMove: PowerCmd.Apply<MinionPower> (on a condition)"] = "Minion on each egg it lays, which joins for its next turn",
        ["TestSubject.RespawnMove: PowerCmd.Apply<NemesisPower> (on a condition)"] = "its own power on its second respawn: a move with no attack (Nemesis acts at the turn's end)",
        ["TestSubject.RespawnMove: PowerCmd.Apply<PainfulStabsPower> (on a condition)"] = "its own power on its first respawn: a move with no attack (Painful Stabs acts after its own attacks)",
        ["TheInsatiable.LiquifyMove: PowerCmd.Apply (a power made first)"] = "Sandpit on itself, one for each target: it counts down as the enemies' next turn starts (SandpitPower's model), not in this enemy turn",
        ["ThievingHopper.ThieveryMove: PowerCmd.Apply (a power made first)"] = "Swipe on itself for each card it stole: it gives the card back when it dies, no damage hook",
    };
}
