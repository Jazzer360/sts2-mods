using Xunit;

namespace SpireQoL.Sim.Tests;

/// <summary>
/// Cards a relic or power puts into your hand (CardPileCmd.AddGeneratedCardToCombat to the hand), and a played card Feral sends
/// back there, join the hand (SimEngine.AddToHand): a full hand sends them to the discard pile instead, the cards being played
/// still holding their slots. Reports: the hand after a play was one short with Feral, Withering Presence's Wither, and Wish.
/// </summary>
public class SimMadeInHandTests
{
    private static SimState Hand(int cards)
    {
        var state = new SimState { Enemies = { new SimEnemy { Id = 0, Name = "E0", Hp = 50 } } };
        state.You.HandCount = cards;
        return state;
    }

    /// <summary>A card played from your hand: it leaves the hand when it resolves (HandDelta -1).</summary>
    private static SimPlay Played(string name, SimCardType type, int energy = 1) => new()
    {
        Name = name, IsAttack = type == SimCardType.Attack, HandCard = 7, HandDelta = -1,
        Card = new SimCardPlay(new SimCard(name, type) { Energy = energy }, Who.You, Target: 0, PlayIndex: 0, PlayCount: 1, AutoPlay: false),
    };

    private static SimState After(SimState state, SimPlay play) => Assert.Single(SimEngine.Run(state, play).Branches);

    [Fact]
    public void FeralKeepsAFreeAttackInYourHand()
    {
        // Neutralize (0 energy) with Feral 1 in a hand of 7: it goes back to the hand, so 7 stay (a report: predicted 6, actual 7).
        var state = Hand(cards: 7);
        state.You.Powers = state.You.Powers.With(PowerId.Feral, amount: 1, data: 0); // none came back yet (FeralPower.zeroCostAttacksPlayed)
        var after = After(state, Played("Neutralize", SimCardType.Attack, energy: 0));
        Assert.True(after.Fired.Has("FeralPower"));
        Assert.Equal(7, after.You.HandCount);

        // Its charge used: the next free attack leaves the hand.
        Assert.Equal(6, After(after, Played("Neutralize", SimCardType.Attack, energy: 0)).You.HandCount);
    }

    [Fact]
    public void WitheringPresencePutsAWitherInYourHand()
    {
        // The card that completes its count (1 left) leaves the hand and a Wither joins it: 5 stay (a report: predicted 4, actual 5).
        var state = Hand(cards: 5);
        state.Enemies[0].Powers = PowerSet.Empty.With(PowerId.WitheringPresence, amount: 1, data: 1);
        var after = After(state, Played("DefendSilent", SimCardType.Skill));
        Assert.True(after.Fired.Has("WitheringPresencePower@0"));
        Assert.Equal(5, after.You.HandCount);

        // Any other card: no Wither.
        state.Enemies[0].Powers = PowerSet.Empty.With(PowerId.WitheringPresence, amount: 1, data: 3);
        Assert.Equal(4, After(state, Played("DefendSilent", SimCardType.Skill)).You.HandCount);
    }

    [Fact]
    public void MusicBoxsCopyIsEtherealInYourHand()
    {
        // The first attack of the turn is copied into the hand, Ethereal: the hand keeps its size, and the copy exhausts at end of turn.
        var state = Hand(cards: 5);
        state.You.Relics = state.You.Relics.With(RelicId.MusicBox, amount: 0, data: 1);
        var after = After(state, Played("StrikeSilent", SimCardType.Attack));
        Assert.True(after.Fired.Has("MusicBox"));
        Assert.Equal(5, after.You.HandCount);
        Assert.Equal(1, after.You.Fading);
    }

    [Fact]
    public void ACardMadeIntoAFullHandGoesToTheDiscardPile()
    {
        // Calamity 3 on an attack played from a full hand: the attack still holds its slot when the cards are made, so one fits;
        // it leaves, and the hand is full again (10), not 12.
        var state = Hand(cards: 10);
        state.You.Powers = state.You.Powers.With(PowerId.Calamity, amount: 3);
        Assert.Equal(10, After(state, Played("StrikeSilent", SimCardType.Attack)).You.HandCount);
    }
}
