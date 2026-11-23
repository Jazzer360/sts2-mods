using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Monsters;
using MegaCrit.Sts2.Core.Models.Powers;
using SpireQoL.Procs;
using Xunit;

namespace SpireQoL.Catalog.Tests;

/// <summary>
/// Draws on a condition, read from each card's program and evaluated against the board (decision #182, ProgramEvaluator): the
/// condition holding and not, and a replay's later plays, on fights built from the game's objects. No card is named in the rule; these
/// are the cards whose conditions it was written from.
/// </summary>
[Collection(GameFightCollection.Name)]
public class ConditionalDrawTests
{
    private static Creature Enemy(GameFight gameFight)
    {
        var enemy = gameFight.Combat.CreateCreature(ModelDb.Monster<Axebot>().ToMutable(), CombatSide.Enemy, slot: null);
        gameFight.Combat.AddCreature(enemy);
        enemy.SetMaxHpInternal(100);
        enemy.SetCurrentHpInternal(100);
        return enemy;
    }

    /// <summary>A fight with five cards to draw, so no draw runs short.</summary>
    private static GameFight Fight()
    {
        var gameFight = new GameFight();
        for (var i = 0; i < 5; i++) gameFight.Add<DefendIronclad>(PileType.Draw);
        return gameFight;
    }

    private static void Impale(Creature enemy, Creature applier)
    {
        var impaled = (ImpaledPower)ModelDb.Power<ImpaledPower>().ToMutable();
        impaled.ApplyInternal(enemy, 1);
        impaled.Applier = applier;
    }

    [Fact]
    public void ImpatienceDrawsWithNoAttackInYourHand()
    {
        // Impatience.OnPlay: if no card in your hand is an Attack (itself, a Skill, has left it), draw Cards.
        var gameFight = Fight();
        Enemy(gameFight);
        var card = gameFight.Add<Impatience>();
        gameFight.Add<DefendIronclad>();
        var cards = card.DynamicVars["Cards"].IntValue;
        Assert.Equal(cards, PlayFacts.CardsDrawn(card, handCount: 2));
        Assert.Equal(cards * 2, PlayFacts.CardsDrawnTotal(card, handCount: 2, plays: 2));
        gameFight.Add<StrikeIronclad>();
        Assert.Equal(0, PlayFacts.CardsDrawn(card, handCount: 3));
    }

    [Fact]
    public void RestlessnessDrawsOnlyAlone()
    {
        // Restlessness.OnPlay: if (!IsOnlyCardInHand) return; then a draw Cards times. The test's direction is kept.
        var gameFight = Fight();
        Enemy(gameFight);
        var card = gameFight.Add<Restlessness>();
        Assert.Equal(card.DynamicVars["Cards"].IntValue, PlayFacts.CardsDrawn(card, handCount: 1));
        gameFight.Add<DefendIronclad>();
        Assert.Equal(0, PlayFacts.CardsDrawn(card, handCount: 2));
    }

    [Fact]
    public void FtlDrawsWhileFewerThanItsMaxPlaysHaveFinished()
    {
        // Ftl.CanDrawCard counts the plays finished this turn: a replay's earlier plays are finished plays, so with PlayMax 3 the
        // fourth play of four doesn't draw.
        var gameFight = Fight();
        Enemy(gameFight);
        var card = gameFight.Add<Ftl>();
        Assert.Equal(card.DynamicVars["Cards"].IntValue * 3, PlayFacts.CardsDrawnTotal(card, handCount: 1, plays: 4));
    }

    [Fact]
    public void WrestDrawsOnceWhenYourArrowImpalesItsTarget()
    {
        // Wrest.OnPlay: if your arrow Impales the target, pull them all out (a while on the same test), then draw. A replay's later
        // play finds none of yours left.
        var gameFight = Fight();
        var enemy = Enemy(gameFight);
        var card = gameFight.Add<Wrest>();
        Assert.Equal(0, PlayFacts.TargetDraws(card, enemy, plays: 1));
        Impale(enemy, gameFight.Me.Creature);
        var cards = card.DynamicVars["Cards"].IntValue;
        Assert.Equal(cards, PlayFacts.TargetDraws(card, enemy, plays: 1));
        Assert.Equal(cards, PlayFacts.TargetDraws(card, enemy, plays: 2));
        Assert.Equal(cards, PlayFacts.CardsDrawnTotal(card, handCount: 1, plays: 2));
    }

    [Fact]
    public void KiHopDrawsOnEveryPlayWhileAnotherArrowImpalesItsTarget()
    {
        // KiHop.OnPlay takes wasImpaled before its hit (anyone's arrow): one that isn't yours stays whatever you hit.
        var gameFight = Fight();
        var enemy = Enemy(gameFight);
        var card = gameFight.Add<KiHop>();
        Assert.Equal(0, PlayFacts.CardsDrawn(card, handCount: 1));
        Impale(enemy, enemy);
        var cards = card.DynamicVars["Cards"].IntValue;
        Assert.Equal(cards, PlayFacts.CardsDrawn(card, handCount: 1));
        Assert.Equal(cards * 2, PlayFacts.TargetDraws(card, enemy, plays: 2));
    }

    [Fact]
    public void ACardThatDoesNotAskItsTargetHasNoTargetDraws()
    {
        var gameFight = Fight();
        var enemy = Enemy(gameFight);
        Assert.Null(PlayFacts.TargetDraws(gameFight.Add<Impatience>(), enemy, plays: 1));
    }
}
