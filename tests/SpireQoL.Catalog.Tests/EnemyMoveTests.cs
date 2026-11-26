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
    public void FogmogsIllusionAddsAnEyeWithTeethToTheEnemyTurn()
    {
        // Fogmog's Illusion: CreatureCmd.Add<EyeWithTeeth> while the fight is live (CombatState.IsLiveCombat: true on a live fight), read as a death's
        // spawn is (decision #376): its HP (6) and its AfterAddedToRoom's Illusion, with the Minion Illusion's AfterApplied gives it. It joins in the
        // enemy turn, after Fogmog, and makes no move then; Byrdonis' Swoop after it lands as shown.
        var fight = new GameFight();
        var fogmog = Enemy<Fogmog>(fight, "ILLUSION_MOVE");
        var byrdonis = Enemy<Byrdonis>(fight, "SWOOP_MOVE");
        var state = Read(fight, out var ids);
        var spawn = Assert.IsType<MoveSpawnAction>(Assert.Single(state.Enemy(ids[fogmog])!.Move!)).Spawn;
        Assert.Equal("EyeWithTeeth", spawn.Monster);
        Assert.Equal(new[] { ModelDb.Monster<EyeWithTeeth>().MinInitialHp }, spawn.Hp);
        Assert.Equal(1, spawn.Powers[PowerId.Illusion]);
        Assert.Equal(1, spawn.Powers[PowerId.Minion]);
        var outcome = SimEngine.Run(state);
        var branch = Assert.Single(outcome.Branches);
        Assert.Null(branch.Unmodelled);
        Assert.Contains(branch.Enemies, enemy => enemy.Monster == "EyeWithTeeth" && enemy.AddedBy == ids[fogmog]);
        Assert.Equal(Hit(byrdonis, fight), outcome.TakenMax);
    }

    [Fact]
    public void TheParafrightTheObscuraAddsRollsItsSlamInTheProjection()
    {
        // The Obscura's Illusion adds a Parafright in the enemy turn; its rules are read from the monster as it joins (decision #378: a copy of the
        // canonical one, its machine made as AddCreature makes it), so the projection's later turns roll its Slam from its initial state on.
        var fight = new GameFight();
        Enemy<TheObscura>(fight, "ILLUSION_MOVE");
        var input = SimAdapter.ProjectionStart(fight.Me, fight.Combat)!;
        var rules = input.Joining["Parafright"];
        Assert.Equal("SLAM_MOVE", rules.Graph.InitialId);
        var slam = rules.Moves["SLAM_MOVE"];
        var otherFight = new GameFight();
        var parafright = Enemy<Parafright>(otherFight, "SLAM_MOVE");
        Assert.Equal((decimal)Hit(parafright, otherFight), slam.Intent!.Single().Base);
        var projection = SimEngine.Project(input.State, input.Turns, input.Rules, input.Joining);
        var joined = input.State.Enemies.Max(enemy => enemy.Id) + 1;
        Assert.Equal("SLAM_MOVE", projection.ForecastOf(joined)!.First().Moves.Single().Move);
        Assert.NotNull(projection.Of(turn: 0, enemyId: joined, move: "SLAM_MOVE"));
    }

    [Fact]
    public void FabricatorsRandomBotsAreReadAsPicks()
    {
        // Fabricator's bots: RunRng.MonsterAi.NextItem of its defence set, then its aggro set, less _lastSpawned (decision #379), each with the Minion
        // it then applies. Read as picks of the sets' monsters; after a play they add nothing (their HP is a roll: a reviewed gap), as before.
        var fight = new GameFight();
        var fabricator = Enemy<Fabricator>(fight, "FABRICATE_MOVE");
        var state = Read(fight, out var ids);
        var picks = state.Enemy(ids[fabricator])!.Move!.OfType<MovePickSpawnAction>().ToList();
        Assert.Equal(new[] { "Guardbot,Noisebot", "Stabbot,Zapbot" }, picks.Select(pick => string.Join(",", pick.Options.Select(option => option.Monster).Order(StringComparer.Ordinal))));
        Assert.All(picks, pick => Assert.Equal(("_lastSpawned", 1), (pick.Field, pick.AfterJoin[PowerId.Minion])));
        Assert.Single(Assert.Single(SimEngine.Run(state).Branches).Enemies);
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
                foreach (var (step, gap) in SimAdapter.MoveGaps(ModelPrograms.Of(monster, move.Name), monster))
                    found.Add($"{monster.Name}.{move.Name}: {step.Name} ({gap})");
        var unreviewed = found.Where(entry => !ReviewedGaps.ContainsKey(entry)).ToList();
        Assert.True(unreviewed.Count == 0, "Move steps the enemy turn can't build, unreviewed (decision #195: model it, or say why it can't change a later hit):\n" + string.Join("\n", unreviewed));
        var stale = ReviewedGaps.Keys.Where(entry => !found.Contains(entry)).ToList();
        Assert.True(stale.Count == 0, "Reviewed, but no longer a gap:\n" + string.Join("\n", stale));
    }

    /// <summary>Each move step the enemy turn can't build, and why it can't change a later hit this turn.</summary>
    private static readonly Dictionary<string, string> ReviewedGaps = new()
    {
        ["Fabricator.FabricateMove: CreatureCmd.Add (a monster picked: the projection's only)"] = "its two bots, each a random one of a set (RunRng.MonsterAi.NextItem) other than the last it made: a branch per bot in the projection (decision #379); after a play its HP among several is a roll the enemy turn's one state doesn't branch on (TRACKER: summons in a move), and they make no move this turn",
        ["Fabricator.FabricatingStrikeMove: CreatureCmd.Add (a monster picked: the projection's only)"] = "its bot after its attack, a random one of a set other than the last it made: a branch per bot in the projection (decision #379); after a play its HP is a roll (TRACKER: summons in a move), and it makes no move this turn",
        ["LivingFog.BloatMove: CreatureCmd.Add<GasBomb> (in a loop)"] = "its Gas Bombs, BloatAmount of them while a slot is free (a kill frees one), before its attack: not in the enemy turn's state (TRACKER: summons in a move); they make no move this turn",
        ["Ovicopter.LayEggsMove: CreatureCmd.Add<ToughEgg> (in a loop)"] = "three Tough Eggs while a slot is free (a kill frees one): not in the enemy turn's state (TRACKER: summons in a move); they make no move this turn",
        ["TwoTailedRat.CallForBackup: CreatureCmd.Add<TwoTailedRat> (a test a play can change)"] = "a rat while a slot is free (a kill frees one): not in the enemy turn's state (TRACKER: summons in a move); it makes no move this turn",
        ["MagiKnight.DampenMove: PowerCmd.Apply (on a condition)"] = "Dampen on a target without it: no damage hook (DampenPower only reacts to its applier's death)",
        ["Ovicopter.LayEggsMove: PowerCmd.Apply<MinionPower> (on a condition)"] = "Minion on each egg it lays, which joins for its next turn",
        ["TestSubject.RespawnMove: PowerCmd.Apply<NemesisPower> (on a condition)"] = "its own power on its second respawn: a move with no attack (Nemesis acts at the turn's end)",
        ["TestSubject.RespawnMove: PowerCmd.Apply<PainfulStabsPower> (on a condition)"] = "its own power on its first respawn: a move with no attack (Painful Stabs acts after its own attacks)",
        ["TheInsatiable.LiquifyMove: PowerCmd.Apply (a power made first)"] = "Sandpit on itself, one for each target: it counts down as the enemies' next turn starts (SandpitPower's model), not in this enemy turn",
        ["ThievingHopper.ThieveryMove: PowerCmd.Apply (a power made first)"] = "Swipe on itself for each card it stole: it gives the card back when it dies, no damage hook",
    };
}
