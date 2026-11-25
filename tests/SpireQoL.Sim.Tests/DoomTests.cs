using Xunit;
using static SpireQoL.Sim.Tests.ScriptBuilder;

namespace SpireQoL.Sim.Tests;

/// <summary>
/// Doom's kill, read from its code (decision #310): DoomPower.BeforeSideTurnEnd (side != CombatSide.Player) and AfterSideTurnEnd (side !=
/// CombatSide.Enemy), each on ShouldDoomTrigger (the fight not ending, its owner taking part, alive, doomed: CurrentHp &lt;= Amount, and the first doomed
/// creature of its side), DoomKill(GetDoomedCreatures(side)): CreatureCmd.Kill of each creature of the side ending whose own Doom is at least its HP
/// (DoomPower.IsOwnerDoomed, ModelSpec.Tests), then Hook.AfterDiedToDoom told that list (Book Repair Knife's heal for each enemy whose death sets off
/// Fatal). End of Days' play runs the same kill over the hittable enemies after its Doom (KillEachAction).
/// </summary>
public class DoomTests
{
    private static SimEnemy Attacker(int id, int hp, int hit) =>
        new() { Id = id, Name = $"E{id}", Hp = hp, Hits = new() { hit }, Intent = new List<IntentHit> { new(Base: hit) }, IntendsToAttack = true };

    private static SimState Fight(params SimEnemy[] enemies)
    {
        var state = new SimState();
        state.You.Hp = 60;
        state.You.MaxHp = 80;
        state.Enemies.AddRange(enemies);
        return state;
    }

    private static SimState AfterTheTurn(SimState state) => Assert.Single(SimEngine.Run(state).Branches);

    private static SimEnemy Doomed(SimEnemy enemy, int doom)
    {
        enemy.Powers = enemy.Powers.With(PowerId.Doom, amount: doom);
        return enemy;
    }

    [Fact]
    public void TheEnemiesTurnEndKillsTheDoomedAndSparesTheRest()
    {
        // Both attack first (Doom doesn't stop an attack); as their side's turn ends, E0 (10 HP, 12 Doom) dies and E1 (30 HP, 12 Doom) lives.
        var state = Fight(Doomed(Attacker(id: 0, hp: 10, hit: 6), doom: 12), Doomed(Attacker(id: 1, hp: 30, hit: 4), doom: 12));
        var after = AfterTheTurn(state);
        Assert.Equal(10, after.Taken);
        Assert.False(after.Enemy(id: 0)!.Alive);
        Assert.True(after.Enemy(id: 1)!.Alive);
        Assert.Null(after.Unmodelled);
        Assert.Null(after.UnmodelledAtTheirTurnEnd);
    }

    [Fact]
    public void DoomAtExactlyItsHpKills()
    {
        // IsOwnerDoomed: base.Owner.CurrentHp <= base.Amount: 12 HP under 12 Doom dies; 13 HP doesn't.
        Assert.False(AfterTheTurn(WithBystander(Fight(Doomed(Attacker(id: 0, hp: 12, hit: 1), doom: 12)))).Enemy(id: 0)!.Alive);
        Assert.True(AfterTheTurn(WithBystander(Fight(Doomed(Attacker(id: 0, hp: 13, hit: 1), doom: 12)))).Enemy(id: 0)!.Alive);
    }

    [Fact]
    public void KillingTheLastEnemyEndsTheFight()
    {
        var after = AfterTheTurn(Fight(Doomed(Attacker(id: 0, hp: 10, hit: 6), doom: 12)));
        Assert.False(after.Enemy(id: 0)!.Alive);
        Assert.True(SimEngine.FightOver(after));
    }

    [Fact]
    public void TheFirstDoomedKillsEveryDoomedOfItsSideOnce()
    {
        // Each doomed enemy's Doom hears the turn's end; only the first doomed of the side acts (ShouldDoomTrigger), and its kill takes both: Book
        // Repair Knife is told once, with both, and heals 3 for each (54 after the hits, 60 after the heal).
        var state = WithBystander(Fight(Doomed(Attacker(id: 0, hp: 5, hit: 3), doom: 9), Doomed(Attacker(id: 1, hp: 7, hit: 3), doom: 9)));
        state.You.Relics = state.You.Relics.With(RelicId.BookRepairKnife, 0);
        var after = AfterTheTurn(state);
        Assert.False(after.Enemy(id: 0)!.Alive);
        Assert.False(after.Enemy(id: 1)!.Alive);
        Assert.Equal(60, after.You.Hp);
    }

    [Fact]
    public void BookRepairKnifeHealsOnlyForADeathThatSetsOffFatal()
    {
        // BookRepairKnife.AfterDiedToDoom counts the enemies whose every power answers ShouldOwnerDeathTriggerFatal true: a Minion's death doesn't
        // (MinionPower's false, as the adapter reads it: SimEnemy.DeathSetsOffFatal), so no heal; one whose answer can't be read is unknown.
        SimState Board(bool? fatal)
        {
            var state = WithBystander(Fight(Doomed(Attacker(id: 0, hp: 5, hit: 6), doom: 9)));
            state.Enemies[0].DeathSetsOffFatal = fatal;
            state.You.Relics = state.You.Relics.With(RelicId.BookRepairKnife, 0);
            return state;
        }
        Assert.Equal(57, AfterTheTurn(Board(fatal: true)).You.Hp);
        Assert.Equal(54, AfterTheTurn(Board(fatal: false)).You.Hp);
        var unknown = AfterTheTurn(Board(fatal: null));
        Assert.Contains(RelicId.BookRepairKnife.Name, unknown.UnmodelledAtTheirTurnEnd, StringComparison.Ordinal);
    }

    [Fact]
    public void DoomOnYouKillsYouAsYourTurnEnds()
    {
        // DoomPower.AfterSideTurnEnd: side != CombatSide.Enemy, so a player's Doom (Neurosurge's) kills them as the players' side's turn ends, before
        // the enemy turn: 5 HP under 10 Doom.
        var state = Fight(Attacker(id: 0, hp: 40, hit: 6));
        state.You.Hp = 5;
        state.You.Powers = state.You.Powers.With(PowerId.Doom, amount: 10);
        var after = AfterTheTurn(state);
        Assert.True(after.You.Died);
    }

    [Fact]
    public void AnEnemysDoomWaitsForItsOwnSidesTurnEnd()
    {
        // As the players' side's turn ends, an enemy's Doom doesn't act (AfterSideTurnEnd: side != CombatSide.Enemy, but its owner doesn't take part):
        // the doomed enemy makes its attack, then dies as its own side's turn ends.
        var after = AfterTheTurn(WithBystander(Fight(Doomed(Attacker(id: 0, hp: 4, hit: 7), doom: 20))));
        Assert.Equal(7, after.Taken);
        Assert.False(after.Enemy(id: 0)!.Alive);
    }

    [Fact]
    public void EndOfDaysKillsEachHittableEnemyItsDoomTakes()
    {
        // EndOfDays.OnPlay: 29 Doom on each hittable enemy, then DoomPower.DoomKill(GetDoomedCreatures(HittableEnemies)): E0 (20 HP) dies at once,
        // E1 (40 HP) lives; Book Repair Knife heals 3 for the one.
        var state = Fight(Attacker(id: 0, hp: 20, hit: 6), Attacker(id: 1, hp: 40, hit: 4));
        state.You.Relics = state.You.Relics.With(RelicId.BookRepairKnife, 0);
        var play = Play(Card("EndOfDays", SimCardType.Skill), target: -1, plays: 1,
            new ApplyPowerAction(PowerId.Doom, Amount: 29) { To = ApplyPowerAction.Where.AllEnemies, Debuff = true },
            new KillEachAction(PowerId.Doom, "IsOwnerDoomed") { TellsDiedToDoom = true });
        var after = Assert.Single(After(state, play));
        Assert.False(after.Enemy(id: 0)!.Alive);
        Assert.True(after.Enemy(id: 1)!.Alive);
        Assert.Equal(63, after.You.Hp);
        Assert.Null(after.Unmodelled);
    }
}
