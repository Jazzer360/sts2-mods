using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using SpireQoL.Procs;
using Xunit;

namespace SpireQoL.Catalog.Tests;

/// <summary>
/// Step 1 of every prediction (PlayFacts: what a card does if played right now), checked on the installed game's own cards
/// in a fight built outside the game (GameFight). The sweeps run every card in the game; the other tests are cards whose
/// count depends on the state, from their decompiled OnPlay. A bug in step 1 reported from players' games gets a test here.
/// </summary>
[Collection(GameFightCollection.Name)]
public class PlayFactsTests
{
    /// <summary>Every card in the game, in a fresh fight with a Defend beside it in hand, with its facts.</summary>
    private static IEnumerable<(CardModel Card, PlayFacts? Facts, Exception? Error)> EveryCard()
    {
        GameModels.EnsureLoaded();
        foreach (var canon in ModelDb.AllCards.ToList())
        {
            var gameFight = new GameFight();
            gameFight.Add<DefendIronclad>();
            CardModel card;
            PlayFacts? facts = null;
            Exception? error = null;
            try
            {
                card = gameFight.Combat.CreateCard(canon, gameFight.Me);
                PileType.Hand.GetPile(gameFight.Me).AddInternal(card);
                facts = PlayFacts.For(card, gameFight.Me, gameFight.Combat, plays: 1);
            }
            catch (Exception ex)
            {
                card = canon;
                error = ex;
            }
            yield return (card, facts, error);
        }
    }

    [Fact]
    public void EveryCardInTheGameHasFacts()
    {
        var failed = EveryCard().Where(cardFacts => cardFacts.Error != null)
            .Select(cardFacts => $"{cardFacts.Card.GetType().Name}: {cardFacts.Error!.GetType().Name} {cardFacts.Error.Message.Split('\n')[0]}").ToList();
        Assert.True(failed.Count == 0, string.Join("\n", failed));
    }

    [Fact]
    public void EveryCardGainsBlockExactlyWhenTheGameSaysItDoes()
    {
        // CardModel.GainsBlock: the game's own flag (its card glow, Stretch). A Power whose Block value is for later gains none
        // on play; Sovereign Blade only with Parry.
        // A card whose every gain waits on a condition that fails on this board (Bone Shards and Sacrifice: Osty missing) gains none,
        // which the game's flag doesn't say.
        static bool GainsWaitOnAFailedTest(MegaCrit.Sts2.Core.Models.CardModel card) =>
            Procs.Programs.CardEffects.BlockGains(card.GetType()) is { Count: > 0 } gains
            && gains.All(gain => Procs.Programs.ProgramEvaluator.Holds(gain.Condition, new Procs.Programs.ProgramBoard(card), Procs.Programs.CardPrograms.Of(card.GetType())) == false);
        var wrong = EveryCard().Where(cardFacts => cardFacts.Facts != null && (cardFacts.Facts.BlockGains > 0) != cardFacts.Card.GainsBlock
                                                   && !(cardFacts.Facts.BlockGains == 0 && GainsWaitOnAFailedTest(cardFacts.Card)))
            .Select(cardFacts => $"{cardFacts.Card.GetType().Name}: counted {cardFacts.Facts!.BlockGains}, the game says {cardFacts.Card.GainsBlock}").ToList();
        Assert.True(wrong.Count == 0, string.Join("\n", wrong));
    }

    [Fact]
    public void ImpatienceDrawsOnlyWithNoAttackInHand()
    {
        // Reports from 1.11.4 and 1.11.5: Impatience is in the plain draw list too, which was read before its condition.
        var gameFight = new GameFight();
        var card = gameFight.Add<Impatience>();
        gameFight.Add<DefendIronclad>();
        Assert.Equal(2, PlayFacts.For(card, gameFight.Me, gameFight.Combat, plays: 1).OwnDraws);
        gameFight.Add<StrikeIronclad>();
        Assert.Equal(0, PlayFacts.For(card, gameFight.Me, gameFight.Combat, plays: 1).OwnDraws);
    }

    [Fact]
    public void CentennialPuzzlesDrawsFromHpTheSimulationLosesCount()
    {
        // A dev review: Tune+ was Galvanized and the enemy's Galvanic cost you HP, which set off Centennial Puzzle's 3 draws. The card's
        // own count has them only for its own HP cost; the simulation's (CountSimulatedHpLoss) brings them in: with 2 cards in the draw
        // pile, they shuffle the discard pile in, which the Shuffle Warning shows.
        var gameFight = new GameFight();
        var card = gameFight.Add<DefendIronclad>();
        gameFight.Add<StrikeIronclad>(PileType.Draw);
        gameFight.Add<StrikeIronclad>(PileType.Draw);
        gameFight.Add<StrikeIronclad>(PileType.Discard);
        gameFight.Me.AddRelicInternal(MegaCrit.Sts2.Core.Models.ModelDb.Relic<MegaCrit.Sts2.Core.Models.Relics.CentennialPuzzle>().ToMutable());
        var facts = PlayFacts.For(card, gameFight.Me, gameFight.Combat, plays: 1);
        Assert.Equal(0, facts.Draws);
        Assert.False(facts.Shuffles);
        facts.CountSimulatedHpLoss();
        Assert.Equal(3, facts.Draws);
        Assert.True(facts.Shuffles);
    }

    [Fact]
    public void NoDrawStopsACardsDraws()
    {
        // The owner's request: a card whose draws No Draw (Battle Trance) would stop gets No Draw's tab (decision #171).
        var gameFight = new GameFight();
        var card = gameFight.Add<ShrugItOff>();
        gameFight.Add<StrikeIronclad>(PileType.Draw);
        Assert.False(PlayFacts.For(card, gameFight.Me, gameFight.Combat, plays: 1).DrawsStopped);
        var noDraw = (MegaCrit.Sts2.Core.Models.PowerModel)MegaCrit.Sts2.Core.Models.ModelDb.Power<MegaCrit.Sts2.Core.Models.Powers.NoDrawPower>().ToMutable();
        noDraw.ApplyInternal(gameFight.Me.Creature, amount: 1);
        var facts = PlayFacts.For(card, gameFight.Me, gameFight.Combat, plays: 1);
        Assert.Equal(0, facts.Draws);
        Assert.True(facts.DrawsStopped);
        // A card that draws nothing has nothing to stop.
        Assert.False(PlayFacts.For(gameFight.Add<DefendIronclad>(), gameFight.Me, gameFight.Combat, plays: 1).DrawsStopped);
    }

    [Fact]
    public void CardsForYourSideIncludeYou()
    {
        // Huddle Up, Blade Symphony and Glimpse Beyond go over CombatState.GetTeammatesOf(you): every player of your side, you included
        // (GetCreaturesOnSide gives the Allies list). Alone, you draw, get your Shivs and your Souls (1.14.9 had left you out).
        var gameFight = new GameFight();
        var huddle = gameFight.Add<HuddleUp>();
        Assert.Equal(Sts2Mods.Core.Game.Sts2State.Var(huddle, "Cards"), PlayFacts.For(huddle, gameFight.Me, gameFight.Combat, plays: 1).OwnDraws);
        var symphony = gameFight.Add<BladeSymphony>();
        Assert.Equal(Sts2Mods.Core.Game.Sts2State.Var(symphony, "Cards"), PlayFacts.For(symphony, gameFight.Me, gameFight.Combat, plays: 1).Generated);
        var glimpse = gameFight.Add<GlimpseBeyond>();
        Assert.Equal(Sts2Mods.Core.Game.Sts2State.Var(glimpse, "Cards"), PlayFacts.For(glimpse, gameFight.Me, gameFight.Combat, plays: 1).Generated);
    }

    [Fact]
    public void RestlessnessDrawsOnlyAsTheLastCardInHand()
    {
        var gameFight = new GameFight();
        var card = gameFight.Add<Restlessness>();
        Assert.True(PlayFacts.For(card, gameFight.Me, gameFight.Combat, plays: 1).OwnDraws > 0);
        gameFight.Add<DefendIronclad>();
        Assert.Equal(0, PlayFacts.For(card, gameFight.Me, gameFight.Combat, plays: 1).OwnDraws);
    }

    [Fact]
    public void SovereignBladeGainsNoBlockWithoutParry()
    {
        // A report from 1.11.4: block predicted 1, the game gained none (SovereignBlade.OnPlay: only while you have Parry).
        var gameFight = new GameFight();
        var card = gameFight.Add<SovereignBlade>();
        Assert.Equal(0, PlayFacts.For(card, gameFight.Me, gameFight.Combat, plays: 1).BlockGains);
    }

    [Fact]
    public void CardsAPlayPutsInYourHandFillItBeforeMakeItSoComesBack()
    {
        // A report from 1.11.4: Bundle of Joy+ with six other cards in hand filled it (10), so Make It So went to the discard
        // pile (CardPileCmd.Add to a full hand). Make It So set to come back on every Skill.
        var gameFight = new GameFight();
        var bundle = gameFight.Add<BundleOfJoy>(upgraded: true);
        var makeItSo = gameFight.Add<MakeItSo>(PileType.Draw);
        makeItSo.DynamicVars["Cards"].BaseValue = 1;
        for (var i = 0; i < 5; i++) gameFight.Add<DefendIronclad>();
        Assert.Single(PlayFacts.For(bundle, gameFight.Me, gameFight.Combat, plays: 1).CardsBack);
        gameFight.Add<DefendIronclad>();
        Assert.Empty(PlayFacts.For(bundle, gameFight.Me, gameFight.Combat, plays: 1).CardsBack);
    }
}
