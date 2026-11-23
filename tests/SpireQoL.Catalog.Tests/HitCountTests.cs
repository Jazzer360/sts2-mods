using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Monsters;
using MegaCrit.Sts2.Core.Models.Powers;
using SpireQoL.Procs;
using Sts2Mods.Core.Game;
using Xunit;

namespace SpireQoL.Catalog.Tests;

/// <summary>
/// Hits per play, read from the attack in the card's program (decision #182: PlayFacts.HitsPerPlay): its condition and its hit count's
/// value evaluated on the board, at a target or at none.
/// </summary>
[Collection(GameFightCollection.Name)]
public class HitCountTests
{
    private static Creature Enemy(GameFight gameFight)
    {
        var enemy = gameFight.Combat.CreateCreature(ModelDb.Monster<Axebot>().ToMutable(), CombatSide.Enemy, slot: null);
        gameFight.Combat.AddCreature(enemy);
        enemy.SetMaxHpInternal(100);
        enemy.SetCurrentHpInternal(100);
        return enemy;
    }

    private static int Hits(GameFight gameFight, CardModel card, Creature? target) => PlayFacts.HitsPerPlay(card, gameFight.Combat, target);

    [Fact]
    public void DismantleHitsAVulnerableTargetItsHitCount()
    {
        var gameFight = new GameFight();
        var plain = Enemy(gameFight);
        var vulnerable = Enemy(gameFight);
        ((VulnerablePower)ModelDb.Power<VulnerablePower>().ToMutable()).ApplyInternal(vulnerable, 2);
        var card = gameFight.Add<Dismantle>();
        Assert.Equal(1, Hits(gameFight, card, plain));
        Assert.Equal(Sts2State.Var(card, "HitCount"), Hits(gameFight, card, vulnerable));
        // No target: the least it can be.
        Assert.Equal(1, Hits(gameFight, card, target: null));
    }

    [Fact]
    public void FixedAndXHitCountsAreTheirValues()
    {
        var gameFight = new GameFight();
        var enemy = Enemy(gameFight);
        var twinStrike = gameFight.Add<TwinStrike>();
        Assert.Equal(Sts2State.Var(twinStrike, "HitCount"), Hits(gameFight, twinStrike, enemy));
        gameFight.Me.PlayerCombatState!.Energy = 3;
        Assert.Equal(3, Hits(gameFight, gameFight.Add<Whirlwind>(), enemy));
        // Fiend Fire: once for each other card in your hand (it exhausts them first).
        var fiendFire = gameFight.Add<FiendFire>();
        Assert.Equal(PileOthers(gameFight, fiendFire), Hits(gameFight, fiendFire, enemy));
    }

    [Fact]
    public void NailHitsOnlyAnImpaledTarget()
    {
        // Nail.OnPlay attacks only when its target is Impaled: with none, at no target, no enemy could be.
        var gameFight = new GameFight();
        var enemy = Enemy(gameFight);
        var card = gameFight.Add<Nail>();
        Assert.Equal(0, Hits(gameFight, card, enemy));
        Assert.Equal(0, Hits(gameFight, card, target: null));
    }

    [Fact]
    public void PactsEndAttacksOnlyWhenItCanDealDamage()
    {
        var gameFight = new GameFight();
        Enemy(gameFight);
        Assert.Equal(0, Hits(gameFight, gameFight.Add<PactsEnd>(), target: null));
    }

    private static int PileOthers(GameFight gameFight, CardModel card) =>
        PileType.Hand.GetPile(gameFight.Me).Cards.Count(other => !ReferenceEquals(other, card));
}
