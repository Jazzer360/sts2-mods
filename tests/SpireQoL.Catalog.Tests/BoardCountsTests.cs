using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using SpireQoL.Procs;
using SpireQoL.Procs.Programs;
using Xunit;

namespace SpireQoL.Catalog.Tests;

/// <summary>
/// What a card's program gives on the board before its play (BoardCounts, MadeCards: what the step translators, the card scan and the
/// Shuffle Tag read) and the card play the adapter builds from it, checked on the installed game's own cards in a fight built outside the
/// game (GameFight). The sweeps run every card in the game; the other tests are cards whose count depends on the state, from their
/// decompiled OnPlay. A bug in what the adapter reads, reported from players' games, gets a test here.
/// </summary>
[Collection(GameFightCollection.Name)]
public class BoardCountsTests
{
    /// <summary>The card's block gains on the board, and its play as the adapter builds it.</summary>
    private sealed record Read(int BlockGains, SpireQoL.Sim.SimPlay Play);

    /// <summary>Every card in the game, in a fresh fight with a Defend beside it in hand, with what the board gives it.</summary>
    private static IEnumerable<(CardModel Card, Read? Facts, Exception? Error)> EveryCard()
    {
        GameModels.EnsureLoaded();
        foreach (var canon in ModelDb.AllCards.ToList())
        {
            var gameFight = new GameFight();
            gameFight.Add<DefendIronclad>();
            CardModel card;
            Read? facts = null;
            Exception? error = null;
            try
            {
                card = gameFight.Combat.CreateCard(canon, gameFight.Me);
                PileType.Hand.GetPile(gameFight.Me).AddInternal(card);
                var start = SpireQoL.Sim.SimAdapter.Build(gameFight.Me, gameFight.Combat, out var ids);
                facts = new Read(BoardCounts.BlockGainCount(card), SpireQoL.Sim.SimAdapter.Play(card, gameFight.Me, gameFight.Combat, ids, SpireQoL.Sim.Who.None, plays: 1));
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
    public void EveryCardInTheGameHasAPlay()
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
        Assert.Equal(2, BoardCounts.CardsDrawn(card, handCount: 2));
        gameFight.Add<StrikeIronclad>();
        Assert.Equal(0, BoardCounts.CardsDrawn(card, handCount: 3));
    }

    /// <summary>The card played in the fight as it is, through the adapter and the simulator: the branches it ends in.</summary>
    private static List<SpireQoL.Sim.SimState> Simulated(GameFight gameFight, CardModel card)
    {
        var start = SpireQoL.Sim.SimAdapter.Build(gameFight.Me, gameFight.Combat, out var ids);
        var play = SpireQoL.Sim.SimAdapter.Play(card, gameFight.Me, gameFight.Combat, ids, SpireQoL.Sim.Who.None, plays: 1);
        SpireQoL.Sim.SimEngine.ApplyPlayTo(start, play, out var branches, out _);
        return branches;
    }

    [Fact]
    public void CentennialPuzzlesDrawsShuffleInTheSimulation()
    {
        // A dev review: Tune+ was Galvanized and the enemy's Galvanic cost you HP, which set off Centennial Puzzle's 3 draws. The play's
        // draws and its shuffle are its simulation's (decision #193): Bloodletting's HP cost sets off Centennial Puzzle's 3 draws, and with
        // 2 cards in the draw pile they shuffle the discard pile in, which the Shuffle Tag shows.
        var gameFight = new GameFight();
        var card = gameFight.Add<Bloodletting>();
        gameFight.Add<StrikeIronclad>(PileType.Draw);
        gameFight.Add<StrikeIronclad>(PileType.Draw);
        gameFight.Add<StrikeIronclad>(PileType.Discard);
        gameFight.Me.AddRelicInternal(MegaCrit.Sts2.Core.Models.ModelDb.Relic<MegaCrit.Sts2.Core.Models.Relics.CentennialPuzzle>().ToMutable());
        var after = Assert.Single(Simulated(gameFight, card));
        Assert.Equal(3, after.PlayEvents[SpireQoL.Sim.CardEvent.Drawn]);
        Assert.Equal(1, after.PlayEvents[SpireQoL.Sim.CardEvent.Shuffled]);
    }

    [Fact]
    public void NoDrawStopsACardsDrawsInTheSimulation()
    {
        // The owner's request: a card whose draws No Draw (Battle Trance) stops gets No Draw's tag (decision #171), from its simulation's
        // log: NoDrawPower.ShouldDraw flashes as it stops the draw (SimEngine.DrawPrevented).
        var gameFight = new GameFight();
        var card = gameFight.Add<ShrugItOff>();
        gameFight.Add<StrikeIronclad>(PileType.Draw);
        Assert.False(Assert.Single(Simulated(gameFight, card)).Fired.Has(nameof(MegaCrit.Sts2.Core.Models.Powers.NoDrawPower)));
        var noDraw = (MegaCrit.Sts2.Core.Models.PowerModel)MegaCrit.Sts2.Core.Models.ModelDb.Power<MegaCrit.Sts2.Core.Models.Powers.NoDrawPower>().ToMutable();
        noDraw.ApplyInternal(gameFight.Me.Creature, amount: 1);
        var stopped = Assert.Single(Simulated(gameFight, card));
        Assert.Equal(0, stopped.PlayEvents[SpireQoL.Sim.CardEvent.Drawn]);
        Assert.True(stopped.Fired.Has(nameof(MegaCrit.Sts2.Core.Models.Powers.NoDrawPower)));
        // A card that draws nothing has nothing to stop.
        Assert.False(Assert.Single(Simulated(gameFight, gameFight.Add<DefendIronclad>())).Fired.Has(nameof(MegaCrit.Sts2.Core.Models.Powers.NoDrawPower)));
    }

    [Fact]
    public void CardsForYourSideIncludeYou()
    {
        // Huddle Up, Blade Symphony and Glimpse Beyond go over CombatState.GetTeammatesOf(you): every player of your side, you included
        // (GetCreaturesOnSide gives the Allies list). Alone, you draw, get your Shivs and your Souls (1.14.9 had left you out).
        var gameFight = new GameFight();
        var huddle = gameFight.Add<HuddleUp>();
        Assert.Equal(Sts2Mods.Core.Game.Sts2State.Var(huddle, "Cards"), BoardCounts.CardsDrawn(huddle, handCount: 1));
        var symphony = gameFight.Add<BladeSymphony>();
        Assert.Equal(Sts2Mods.Core.Game.Sts2State.Var(symphony, "Cards"), MadeCards.Summary(symphony).Made);
        var glimpse = gameFight.Add<GlimpseBeyond>();
        Assert.Equal(Sts2Mods.Core.Game.Sts2State.Var(glimpse, "Cards"), MadeCards.Summary(glimpse).Made);
    }

    [Fact]
    public void RestlessnessDrawsOnlyAsTheLastCardInHand()
    {
        var gameFight = new GameFight();
        var card = gameFight.Add<Restlessness>();
        Assert.True(BoardCounts.CardsDrawn(card, handCount: 1) > 0);
        gameFight.Add<DefendIronclad>();
        Assert.Equal(0, BoardCounts.CardsDrawn(card, handCount: 3));
    }

    [Fact]
    public void SovereignBladeGainsNoBlockWithoutParry()
    {
        // A report from 1.11.4: block predicted 1, the game gained none (SovereignBlade.OnPlay: only while you have Parry).
        var gameFight = new GameFight();
        var card = gameFight.Add<SovereignBlade>();
        Assert.Equal(0, BoardCounts.BlockGainCount(card));
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
        Assert.Single(ReturnToHand.Back(bundle, gameFight.Me, plays: 1));
        gameFight.Add<DefendIronclad>();
        Assert.Empty(ReturnToHand.Back(bundle, gameFight.Me, plays: 1));
    }
}
