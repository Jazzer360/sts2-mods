using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Monsters;
using MegaCrit.Sts2.Core.Models.Powers;
using SpireQoL.Sim;
using Xunit;

namespace SpireQoL.Catalog.Tests;

/// <summary>
/// A spread of the play's target's debuffs read from the card's code (decision #293): Misery.OnPlay takes the target's powers whose amount is a
/// debuff before its attack and, after it, applies a clone of each (or changes the one already there) on every other hittable enemy. Built from
/// the game's objects; the card isn't named by the rule, only here, as the card the rule was written from.
/// </summary>
[Collection(GameFightCollection.Name)]
public class DebuffSpreadReadTests
{
    private static Creature Enemy(GameFight gameFight, int hp = 100)
    {
        var enemy = gameFight.Combat.CreateCreature(ModelDb.Monster<Axebot>().ToMutable(), CombatSide.Enemy, slot: null);
        gameFight.Combat.AddCreature(enemy);
        enemy.SetMaxHpInternal(hp);
        enemy.SetCurrentHpInternal(hp);
        return enemy;
    }

    private static void Give<T>(Creature enemy, int amount, Creature? applier) where T : PowerModel
    {
        var power = (PowerModel)ModelDb.Power<T>().ToMutable();
        power.ApplyInternal(enemy, amount);
        power.Applier = applier;
    }

    private static (SimState Start, SimPlay Play, IReadOnlyDictionary<Creature, int> Ids) Misery(GameFight gameFight, Creature target)
    {
        var card = gameFight.Add<Misery>();
        var start = SimAdapter.Build(gameFight.Me, gameFight.Combat, out var ids);
        return (start, SimAdapter.Play(card, gameFight.Me, gameFight.Combat, ids, ids[target], plays: 1), ids);
    }

    [Fact]
    public void YourDebuffsOnTheTargetSpreadToTheOtherEnemies()
    {
        var gameFight = new GameFight();
        var target = Enemy(gameFight);
        var other = Enemy(gameFight);
        Give<VulnerablePower>(target, amount: 2, gameFight.Me.Creature);
        Give<WeakPower>(target, amount: 1, gameFight.Me.Creature);
        Give<WeakPower>(other, amount: 1, gameFight.Me.Creature);
        var (start, play, ids) = Misery(gameFight, target);
        Assert.Contains(play.Body, action => action is KeepTargetDebuffsAction);
        Assert.Contains(play.Body, action => action is SpreadKeptDebuffsAction);
        SimEngine.ApplyPlayTo(start, play, out var branches, out _);
        var branch = Assert.Single(branches);
        Assert.Null(branch.Unmodelled);
        Assert.Equal(2, branch.Enemy(ids[other])!.Powers[PowerId.Vulnerable]);
        Assert.Equal(2, branch.Enemy(ids[other])!.Powers[PowerId.Weak]);
    }

    [Fact]
    public void ADebuffAnotherAppliedIsUnknown()
    {
        // Its clone is applied by its own applier (item2.Key.Applier): the amount hooks of whoever that is, which the simulation's spread (yours) isn't.
        var gameFight = new GameFight();
        var target = Enemy(gameFight);
        Enemy(gameFight);
        Give<VulnerablePower>(target, amount: 2, applier: null);
        var (start, play, _) = Misery(gameFight, target);
        SimEngine.ApplyPlayTo(start, play, out var branches, out _);
        Assert.Contains("a debuff another applied", Assert.Single(branches).Unmodelled);
    }
}
