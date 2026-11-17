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
public class PlayFactsTests
{
    /// <summary>Every card in the game, in a fresh fight with a Defend beside it in hand, with its facts.</summary>
    private static IEnumerable<(CardModel Card, PlayFacts? Facts, Exception? Error)> EveryCard()
    {
        GameModels.EnsureLoaded();
        foreach (var canon in ModelDb.AllCards.ToList())
        {
            var f = new GameFight();
            f.Add<DefendIronclad>();
            CardModel card;
            PlayFacts? facts = null;
            Exception? error = null;
            try
            {
                card = f.Combat.CreateCard(canon, f.Me);
                PileType.Hand.GetPile(f.Me).AddInternal(card);
                facts = PlayFacts.For(card, f.Me, f.Combat, 1);
            }
            catch (Exception e)
            {
                card = canon;
                error = e;
            }
            yield return (card, facts, error);
        }
    }

    [Fact]
    public void EveryCardInTheGameHasFacts()
    {
        var failed = EveryCard().Where(c => c.Error != null)
            .Select(c => $"{c.Card.GetType().Name}: {c.Error!.GetType().Name} {c.Error.Message.Split('\n')[0]}").ToList();
        Assert.True(failed.Count == 0, string.Join("\n", failed));
    }

    [Fact]
    public void EveryCardGainsBlockExactlyWhenTheGameSaysItDoes()
    {
        // CardModel.GainsBlock: the game's own flag (its card glow, Stretch). A Power whose Block value is for later gains none
        // on play; Sovereign Blade only with Parry.
        var wrong = EveryCard().Where(c => c.Facts != null && (c.Facts.BlockGains > 0) != c.Card.GainsBlock)
            .Select(c => $"{c.Card.GetType().Name}: counted {c.Facts!.BlockGains}, the game says {c.Card.GainsBlock}").ToList();
        Assert.True(wrong.Count == 0, string.Join("\n", wrong));
    }

    [Fact]
    public void ImpatienceDrawsOnlyWithNoAttackInHand()
    {
        // Reports from 1.11.4 and 1.11.5: Impatience is in the plain draw list too, which was read before its condition.
        var f = new GameFight();
        var card = f.Add<Impatience>();
        f.Add<DefendIronclad>();
        Assert.Equal(2, PlayFacts.For(card, f.Me, f.Combat, 1).OwnDraws);
        f.Add<StrikeIronclad>();
        Assert.Equal(0, PlayFacts.For(card, f.Me, f.Combat, 1).OwnDraws);
    }

    [Fact]
    public void RestlessnessDrawsOnlyAsTheLastCardInHand()
    {
        var f = new GameFight();
        var card = f.Add<Restlessness>();
        Assert.True(PlayFacts.For(card, f.Me, f.Combat, 1).OwnDraws > 0);
        f.Add<DefendIronclad>();
        Assert.Equal(0, PlayFacts.For(card, f.Me, f.Combat, 1).OwnDraws);
    }

    [Fact]
    public void SovereignBladeGainsNoBlockWithoutParry()
    {
        // A report from 1.11.4: block predicted 1, the game gained none (SovereignBlade.OnPlay: only while you have Parry).
        var f = new GameFight();
        var card = f.Add<SovereignBlade>();
        Assert.Equal(0, PlayFacts.For(card, f.Me, f.Combat, 1).BlockGains);
    }

    [Fact]
    public void CardsAPlayPutsInYourHandFillItBeforeMakeItSoComesBack()
    {
        // A report from 1.11.4: Bundle of Joy+ with six other cards in hand filled it (10), so Make It So went to the discard
        // pile (CardPileCmd.Add to a full hand). Make It So set to come back on every Skill.
        var f = new GameFight();
        var bundle = f.Add<BundleOfJoy>(upgraded: true);
        var makeItSo = f.Add<MakeItSo>(PileType.Draw);
        makeItSo.DynamicVars["Cards"].BaseValue = 1;
        for (var i = 0; i < 5; i++) f.Add<DefendIronclad>();
        Assert.Single(PlayFacts.For(bundle, f.Me, f.Combat, 1).CardsBack);
        f.Add<DefendIronclad>();
        Assert.Empty(PlayFacts.For(bundle, f.Me, f.Combat, 1).CardsBack);
    }
}
