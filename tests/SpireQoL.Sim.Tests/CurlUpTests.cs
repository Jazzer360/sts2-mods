using Xunit;

namespace SpireQoL.Sim.Tests;

/// <summary>
/// CurlUpPower read from its code (decision #274). AfterDamageReceived: `target == base.Owner`, a powered attack, `cardSource != null`, and either no card
/// kept or that one (`playedCard != null &amp;&amp; cardSource != playedCard` returns): it keeps the hit's card (`playedCard = cardSource`), the identity of the card
/// play it comes from. AfterCardPlayed: `cardPlay.Card == playedCard`: it lets it go, gains its amount of block (unpowered), sets Louse Progenitor's
/// Curled, and the power goes. A card played inside the one that hit it (Hellraiser's Strike) is another card play: its hit isn't kept, and its
/// AfterCardPlayed gives no block.
/// </summary>
public class CurlUpTests
{
    /// <summary>A Strike in the draw pile that plays itself when drawn (Hellraiser), at enemy 1, for 6.</summary>
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
                    Body = { ScriptBuilder.Attack(baseDamage: 6) },
                },
            },
        },
    };

    private static SimState Board(int curlUp)
    {
        var state = new SimState { Enemies = { new SimEnemy { Id = 1, Name = "Louse", Hp = 80 } } };
        state.You.Hp = 50;
        state.You.HandCount = 3;
        state.You.Draw = new SimDrawPile { Rest = { new SimDrawGroup(HellStrike, 3) } };
        state.You.Powers = state.You.Powers.With(PowerId.Hellraiser, amount: 1, data: 0);
        state.Enemies[0].CurlUp = curlUp;
        return state;
    }

    private static SimPlay Bash(params SimAction[] body)
    {
        var play = new SimPlay { Name = "Bash", IsAttack = true, HandCard = 7, HandDelta = -1, TargetId = 1,
            Card = new SimCardPlay(new SimCard("Bash", SimCardType.Attack), Who.You, Target: 1, PlayIndex: 0, PlayCount: 1, AutoPlay: false) };
        play.Body.AddRange(body);
        return play;
    }

    [Fact]
    public void ItsBlockWaitsForThePlayOfTheCardThatHitIt()
    {
        // Bash hits for 5 (kept), draws the Strike, which plays inside it and hits for 6 (not kept: another card), then hits for 5 again: the Strike's
        // AfterCardPlayed gives no block, so Bash's second hit lands on HP (80 - 16); its block comes after Bash's play.
        var state = Board(curlUp: 7);
        SimEngine.ApplyPlayTo(state, Bash(ScriptBuilder.Attack(baseDamage: 5), new CardEventAction(CardEvent.Drawn, Count: 1), ScriptBuilder.Attack(baseDamage: 5)), out var branches, out _);
        var branch = Assert.Single(branches);
        Assert.Null(branch.Unmodelled);
        Assert.Equal(1, branch.AutoPlayed);
        Assert.Equal(64, branch.Enemy(id: 1)!.Hp);
        Assert.Equal(7, branch.Enemy(id: 1)!.Block);
        Assert.False(branch.Enemy(id: 1)!.Powers.Has(PowerId.CurlUp));
    }

    [Fact]
    public void ACardPlayedInsideTheOneThatDidntHitItIsTheOneKept()
    {
        // Bash gives no hit and draws the Strike, whose hit is kept: its own play's AfterCardPlayed gives the block, before Bash's 5 lands on it.
        var state = Board(curlUp: 7);
        SimEngine.ApplyPlayTo(state, Bash(new CardEventAction(CardEvent.Drawn, Count: 1), ScriptBuilder.Attack(baseDamage: 5)), out var branches, out _);
        var branch = Assert.Single(branches);
        Assert.Equal(80 - 6, branch.Enemy(id: 1)!.Hp);
        Assert.Equal(2, branch.Enemy(id: 1)!.Block);
    }

    [Fact]
    public void OnLouseProgenitorItCurls()
    {
        var state = Board(curlUp: 7);
        state.Enemies[0].Monster = "LouseProgenitor";
        SimEngine.ApplyPlayTo(state, Bash(ScriptBuilder.Attack(baseDamage: 5)), out var branches, out _);
        var branch = Assert.Single(branches);
        Assert.Equal(7, branch.Enemy(id: 1)!.Block);
        Assert.Equal(1, branch.Enemy(id: 1)!.Fields["Monster.LouseProgenitor.Curled"]);
    }
}
