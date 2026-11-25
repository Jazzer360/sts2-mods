using Xunit;

namespace SpireQoL.Sim.Tests;

/// <summary>
/// A card played during another card's play (Hellraiser's Strike, drawn by the card being played) is a card play of its own: what a model binds to
/// a card play (its own data keyed by cardPlay.Card: Music Box's CardBeingPlayed, Rupture's amounts for played cards) is that card
/// play's, so the inner play's hooks see their own play's and the outer play gets its own back (SimCardPlay.Serial: decision #267).
/// </summary>
public class NestedPlayTests
{
    private static readonly SimCard Pommel = new("PommelStrike", SimCardType.Attack);

    /// <summary>A Strike in the draw pile that plays itself when drawn (Hellraiser), at enemy 1.</summary>
    private static readonly SimDrawCard HellStrike = new("strike")
    {
        Type = SimCardType.Attack, Strike = true, Auto = new SimHandCard
        {
            Id = 31, Name = "StrikeIronclad", IsAttack = true, Plays =
            {
                new SimPlay
                {
                    Name = "StrikeIronclad", IsAttack = true, AutoPlay = true, HandCard = 31, HandDelta = -1, TargetId = 1,
                    Card = new SimCardPlay(new SimCard("StrikeIronclad", SimCardType.Attack), Who.You, Target: 1, PlayIndex: 0, PlayCount: 1, AutoPlay: true),
                    Body = { new AttackAction(Hits: 1, SimPlay.Target.Single, new DamageSpec(Base: 0m, SimProps.Move) { BaseOn = new Dictionary<int, decimal> { [1] = 6 }, Fixed = true }) },
                },
            },
        },
    };

    [Fact]
    public void MusicBoxCopiesTheCardItBoundNotTheOnePlayedInsideIt()
    {
        // MusicBox.BeforeCardPlayed keeps the Attack played (CardBeingPlayed); the Strike it draws plays itself inside its play (Hellraiser): that
        // play's BeforeCardPlayed finds a card kept already, and its AfterCardPlayed's cardPlay.Card == CardBeingPlayed is false. The outer play's
        // AfterCardPlayed is the one that copies: a Pommel Strike made, never a Strike.
        var madePommel = new HistoryQuery(HistoryKind.CardGenerated, HistoryScope.ThisTurn, HistoryWhose.Anyone) { Card = new CardId(Pommel.Name) };
        var madeStrike = new HistoryQuery(HistoryKind.CardGenerated, HistoryScope.ThisTurn, HistoryWhose.Anyone) { Card = new CardId("StrikeIronclad") };
        var state = new SimState { Enemies = { new SimEnemy { Id = 1, Name = "E1", Hp = 50 } }, History = new List<HistoryQuery> { madePommel, madeStrike } };
        state.You.HandCount = 3;
        state.You.Draw = new SimDrawPile { Rest = { new SimDrawGroup(HellStrike, 3) } };
        state.You.Powers = state.You.Powers.With(PowerId.Hellraiser, amount: 1, data: 0);
        state.You.Relics = state.You.Relics.With(RelicId.MusicBox, amount: 0, data: 0);
        var pommel = new SimPlay
        {
            Name = Pommel.Name, IsAttack = true, HandCard = 7, HandDelta = -1, TargetId = 1,
            Card = new SimCardPlay(Pommel, Who.You, Target: 1, PlayIndex: 0, PlayCount: 1, AutoPlay: false),
            Body = { new CardEventAction(CardEvent.Drawn, Count: 1) },
        };
        SimEngine.ApplyPlayTo(state, pommel, out var branches, out _);
        var branch = Assert.Single(branches);
        Assert.Null(branch.Unmodelled);
        Assert.Equal(1, branch.AutoPlayed);
        Assert.Equal(1, branch.You.Turn[madePommel.Key]);
        Assert.Equal(0, branch.You.Turn[madeStrike.Key]);
    }
}
