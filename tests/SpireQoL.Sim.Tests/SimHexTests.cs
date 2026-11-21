using Xunit;

namespace SpireQoL.Sim.Tests;

/// <summary>
/// Hex (decision #85, decompile: HexPower). While it's on you, your Hexed cards are Ethereal (TryModifyKeywordsInCombat).
/// It's removed when the enemy that cast it dies (AfterDeath), and the Ethereal goes with it: those cards don't exhaust at the
/// end of your turn. Ethereal of a card's own (or Music Box's) stays.
/// </summary>
public class SimHexTests
{
    private static SimEnemy Enemy(int id, int hp, params int[] hits) => new() { Id = id, Name = $"E{id}", Hp = hp, Hits = hits.ToList() };

    private static SimPlay Strike(int target, int damage)
        => new() { Name = "Strike", IsAttack = true, HitTarget = SimPlay.Target.Single, TargetId = target, HitDamage = { [target] = damage }, Hits = 1 };

    /// <summary>
    /// In game (the Knights elite): Spectral Knight (1) cast Hex, Magi Knight (2) is at 6 with a big attack, Charon's Ashes
    /// deals 3 to every enemy per card exhausted, two Hexed cards in hand.
    /// </summary>
    private static SimState Knights()
    {
        var state = new SimState { Enemies = { Enemy(id: 1, hp: 5, hits: 10), Enemy(id: 2, hp: 6, hits: 35) } };
        state.You.CharonsAshesDamage = 3;
        state.You.Fading = 2;
        state.You.HexFading = 2;
        state.You.HexFrom = 1;
        return state;
    }

    [Fact]
    public void HexedCardsExhaustWhileTheCasterLives()
    {
        var outcome = SimEngine.Run(Knights());
        Assert.True(outcome.DeadInAll(enemyId: 1));
        Assert.True(outcome.DeadInAll(enemyId: 2));
    }

    [Fact]
    public void KillingTheCasterEndsTheirEthereal()
    {
        // The caster dies before your turn ends: Hex goes, the cards stay in hand, and Charon's Ashes doesn't fire.
        var outcome = SimEngine.Run(Knights(), Strike(target: 1, damage: 9));
        Assert.True(outcome.DeadInAll(enemyId: 1));
        Assert.False(outcome.DeadInAny(enemyId: 2));
        Assert.Equal(35, outcome.TakenMax);
    }

    [Fact]
    public void StampedeKillingTheCasterEndsTheirEthereal()
    {
        // The auto-play phase comes before the Ethereal exhausts: a Stampede pick that kills the caster ends them too.
        var state = Knights();
        state.You.Stampede = 1;
        state.You.AutoHand = new()
        {
            new SimHandCard { Id = 11, Name = "Strike", IsAttack = true, Cost = 1, Plays = { Strike1(target: 1, hand: 11), Strike1(target: 2, hand: 11) } },
        };
        var outcome = SimEngine.Run(state);
        // Aimed at the caster (1): it dies, the cards stay, Magi Knight lives. Aimed at Magi Knight (2): it dies, and so does the caster.
        Assert.Equal(0.5, outcome.DeadChance(enemyId: 2), 3);
        Assert.True(outcome.DeadInAll(enemyId: 1));
    }

    private static SimPlay Strike1(int target, int hand)
    {
        var play = Strike(target, damage: 9);
        play.HandCard = hand;
        return play;
    }

    [Fact]
    public void TheirOwnEtherealStillExhausts()
    {
        // One card Ethereal of its own, one only through Hex: with the caster dead, one still exhausts (3 to Magi Knight).
        var state = Knights();
        state.You.HexFading = 1;
        state.Enemies[1].Hp = 3;
        var outcome = SimEngine.Run(state, Strike(target: 1, damage: 9));
        Assert.True(outcome.DeadInAll(enemyId: 2));
    }

    [Fact]
    public void PlayingAHexedCardTakesItOffTheCount()
    {
        // Played: it's no longer in hand to exhaust or to lose its Ethereal.
        var state = Knights();
        var play = Strike(target: 1, damage: 9);
        play.FadingDelta = -1;
        play.HexFadingDelta = -1;
        state.You.Fading = 3; // one more of its own
        state.Enemies[1].Hp = 3;
        var outcome = SimEngine.Run(state, play);
        Assert.True(outcome.DeadInAll(enemyId: 2)); // the card of its own exhausts: 3
    }
}
