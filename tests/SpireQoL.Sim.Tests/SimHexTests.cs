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
        var s = new SimState { Enemies = { Enemy(1, 5, 10), Enemy(2, 6, 35) } };
        s.You.CharonsAshesDamage = 3;
        s.You.Fading = 2;
        s.You.HexFading = 2;
        s.You.HexFrom = 1;
        return s;
    }

    [Fact]
    public void HexedCardsExhaustWhileTheCasterLives()
    {
        var o = SimEngine.Run(Knights());
        Assert.True(o.DeadInAll(1));
        Assert.True(o.DeadInAll(2));
    }

    [Fact]
    public void KillingTheCasterEndsTheirEthereal()
    {
        // The caster dies before your turn ends: Hex goes, the cards stay in hand, and Charon's Ashes doesn't fire.
        var o = SimEngine.Run(Knights(), Strike(1, 9));
        Assert.True(o.DeadInAll(1));
        Assert.False(o.DeadInAny(2));
        Assert.Equal(35, o.TakenMax);
    }

    [Fact]
    public void StampedeKillingTheCasterEndsTheirEthereal()
    {
        // The auto-play phase comes before the Ethereal exhausts: a Stampede pick that kills the caster ends them too.
        var s = Knights();
        s.You.Stampede = 1;
        s.You.AutoHand = new()
        {
            new SimHandCard { Id = 11, Name = "Strike", IsAttack = true, Cost = 1, Plays = { Strike1(1, 11), Strike1(2, 11) } },
        };
        var o = SimEngine.Run(s);
        // Aimed at the caster (1): it dies, the cards stay, Magi Knight lives. Aimed at Magi Knight (2): it dies, and so does the caster.
        Assert.Equal(0.5, o.DeadChance(2), 3);
        Assert.True(o.DeadInAll(1));
    }

    private static SimPlay Strike1(int target, int hand)
    {
        var p = Strike(target, 9);
        p.HandCard = hand;
        return p;
    }

    [Fact]
    public void TheirOwnEtherealStillExhausts()
    {
        // One card Ethereal of its own, one only through Hex: with the caster dead, one still exhausts (3 to Magi Knight).
        var s = Knights();
        s.You.HexFading = 1;
        s.Enemies[1].Hp = 3;
        var o = SimEngine.Run(s, Strike(1, 9));
        Assert.True(o.DeadInAll(2));
    }

    [Fact]
    public void PlayingAHexedCardTakesItOffTheCount()
    {
        // Played: it's no longer in hand to exhaust or to lose its Ethereal.
        var s = Knights();
        var play = Strike(1, 9);
        play.FadingDelta = -1;
        play.HexFadingDelta = -1;
        s.You.Fading = 3; // one more of its own
        s.Enemies[1].Hp = 3;
        var o = SimEngine.Run(s, play);
        Assert.True(o.DeadInAll(2)); // the card of its own exhausts: 3
    }
}
