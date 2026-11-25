using Xunit;
using static SpireQoL.Sim.Tests.ScriptBuilder;

namespace SpireQoL.Sim.Tests;

/// <summary>
/// Who takes an extra turn as the players' turn is over (decision #319; CombatManager.SwitchFromPlayerToEnemySide asks Hook.ShouldTakeExtraTurn of
/// each player: every listener's ShouldTakeExtraTurnEarly, then ShouldTakeExtraTurn, the first yes its source). Extra Turn (ExtraTurnPower, read from
/// its code: `Amount > 0 &amp;&amp; player == base.Owner.Player`; The Hanged Man and Ambergris apply it) and Pael's Eye (its hand-written model: armed, its
/// owner took part and played no card). The enemy turn is still simulated after it: an enemy's death before it acts is unchanged.
/// </summary>
public class ExtraTurnTests
{
    private static SimEnemy Attacker(int id, int hp, params int[] hits) => new() { Id = id, Name = $"E{id}", Hp = hp, Hits = hits.ToList(), IntendsToAttack = hits.Length > 0 };

    private static SimState Board()
    {
        var state = new SimState { Enemies = { Attacker(id: 0, hp: 40, hits: 10) } };
        state.You.Hp = 50;
        state.You.MaxHp = 50;
        return state;
    }

    [Fact]
    public void ExtraTurnGivesYouAnotherTurnWhileItsAmountIsAboveZero()
    {
        var state = Board();
        state.You.Powers = state.You.Powers.With(PowerId.ExtraTurn, 1);
        var outcome = SimEngine.Run(state);
        Assert.Equal(PowerId.ExtraTurn.Name, outcome.ExtraTurnFrom(Who.You));
        // The enemy turn after it is still simulated (what Incoming Damage no longer shows, decision #319).
        Assert.Equal(10, outcome.TakenMax);

        var none = Board();
        none.You.Powers = none.You.Powers.With(PowerId.ExtraTurn, 0);
        Assert.Null(SimEngine.Run(none).ExtraTurnFrom(Who.You));
        Assert.Null(SimEngine.Run(Board()).ExtraTurnFrom(Who.You));
    }

    [Fact]
    public void APlayThatAppliesExtraTurnGivesItAsTheTurnEnds()
    {
        // The Hanged Man: PowerCmd.Apply<ExtraTurnPower>(you, 1), then the turn's end asks.
        var hangedMan = Play(Card("TheHangedMan", SimCardType.Skill), target: Who.None, plays: 1, new ApplyPowerAction(PowerId.ExtraTurn, Amount: 1) { To = ApplyPowerAction.Where.You });
        var outcome = SimEngine.Run(Board(), hangedMan);
        Assert.Equal(PowerId.ExtraTurn.Name, outcome.ExtraTurnFrom(Who.You));
        Assert.True(outcome.ExtraTurnInSome(Who.You));
    }

    [Fact]
    public void PaelsEyeArmedAnswersFirstAndACardYouPlayGivesItUp()
    {
        var armed = Board();
        armed.You.Relics = armed.You.Relics.With(RelicId.PaelsEye, 0);
        armed.You.PaelsEyeExhaust = true;
        // Its Early pass comes before Extra Turn's: the source is Pael's Eye.
        armed.You.Powers = armed.You.Powers.With(PowerId.ExtraTurn, 1);
        Assert.Equal(RelicId.PaelsEye.Name, SimEngine.Run(armed).ExtraTurnFrom(Who.You));

        // A card played disarms it: with nothing else, no extra turn.
        var alone = Board();
        alone.You.Relics = alone.You.Relics.With(RelicId.PaelsEye, 0);
        alone.You.PaelsEyeExhaust = true;
        Assert.Equal(RelicId.PaelsEye.Name, SimEngine.Run(alone).ExtraTurnFrom(Who.You));
        var played = SimEngine.Run(alone, new SimPlay { Name = "Defend", HandDelta = -1 });
        Assert.Null(played.ExtraTurnFrom(Who.You));
        Assert.False(played.ExtraTurnInSome(Who.You));
        // An auto-play doesn't.
        Assert.Equal(RelicId.PaelsEye.Name, SimEngine.Run(alone, new SimPlay { Name = "Strike", HandDelta = -1, AutoPlay = true }).ExtraTurnFrom(Who.You));
    }

    [Fact]
    public void ATeammatesExtraTurnIsTheirsAlone()
    {
        // ExtraTurnPower's `player == base.Owner.Player`: asked for you, a teammate's says no.
        var state = Board();
        state.Teammates.Add(new SimPlayer { Hp = 40, MaxHp = 60, Powers = PowerSet.Empty.With(PowerId.ExtraTurn, 1) });
        var outcome = SimEngine.Run(state);
        Assert.Equal(PowerId.ExtraTurn.Name, outcome.ExtraTurnFrom(Who.Teammate(0)));
        Assert.Null(outcome.ExtraTurnFrom(Who.You));
    }

    [Fact]
    public void NoExtraTurnForAPlayerWhoDiedAsTheTurnEnded()
    {
        // A Burn in hand takes your last 2 HP: you're dead before the side switch, so no extra turn is asked for.
        var state = Board();
        state.You.Hp = 2;
        state.You.Powers = state.You.Powers.With(PowerId.ExtraTurn, 1);
        state.You.TurnEndCards.Add(new TurnEndCard("Burn", "Burn", Amount: 2));
        var outcome = SimEngine.Run(state);
        Assert.True(outcome.Branches.All(branch => branch.Died));
        Assert.Null(outcome.ExtraTurnFrom(Who.You));
    }

    [Fact]
    public void AKillStillCountsAsBeforeTheEnemyActs()
    {
        // The Death Skull and the lethal tags are unchanged: a kill counts as dead before acting, and the extra turn still comes (another enemy
        // keeps the fight going).
        var state = new SimState { Enemies = { Attacker(id: 0, hp: 5, hits: 10), Attacker(id: 1, hp: 40, hits: 4) } };
        state.You.Hp = 50;
        state.You.MaxHp = 50;
        state.You.Powers = state.You.Powers.With(PowerId.ExtraTurn, 1);
        var outcome = SimEngine.Run(state, Play(Card("Strike"), target: 0, plays: 1, Attack(baseDamage: 6)));
        Assert.True(outcome.DeadInAll(enemyId: 0));
        Assert.Equal(PowerId.ExtraTurn.Name, outcome.ExtraTurnFrom(Who.You));
        // A play that wins the fight leaves no turn to take.
        var last = new SimState { Enemies = { Attacker(id: 0, hp: 5, hits: 10) } };
        last.You.Powers = last.You.Powers.With(PowerId.ExtraTurn, 1);
        Assert.Null(SimEngine.Run(last, Play(Card("Strike"), target: 0, plays: 1, Attack(baseDamage: 6))).ExtraTurnFrom(Who.You));
    }
}
