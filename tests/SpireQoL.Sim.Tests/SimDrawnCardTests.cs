using Xunit;

namespace SpireQoL.Sim.Tests;

/// <summary>
/// Cards that act on what their own draw gives (decision #118, decompile: the result of CardPileCmd.Draw): Escape Plan gains its
/// block only when it draws a Skill; Pillage draws again while it draws an Attack and your hand has room; Scrape discards what it
/// drew unless it costs 0 (an X cost isn't 0). Each draw is a branch per profile, which carries the type or the cost while such a
/// card is in your hand, so the chance of each outcome is exact.
/// </summary>
public class SimDrawnCardTests
{
    private static readonly SimDrawCard Skill = new("skill") { Type = SimCardType.Skill };
    private static readonly SimDrawCard Attack = new("attack") { Type = SimCardType.Attack };
    private static readonly SimDrawCard FreeCard = new("free") { Free = true };
    private static readonly SimDrawCard Burn = new("burn") { Free = false, TurnEnd = new TurnEndCard("Burn", "Burn", 2) };
    private static readonly SimDrawCard SlyCard = new("sly") { Free = false, Sly = true };

    private static SimDrawPile Pile(params (SimDrawCard Card, int Count)[] rest) => new() { Rest = rest.Select(r => new SimDrawGroup(r.Card, r.Count)).ToList() };

    private static SimState You(SimDrawPile pile, int hand = 5)
    {
        var s = new SimState { Enemies = { new SimEnemy { Id = 1, Name = "E1", Hp = 50, Hits = new() { 10 } } } };
        s.P.HandCount = hand;
        s.P.Hp = 50;
        s.P.Draw = pile;
        return s;
    }

    /// <summary>A card played from your hand whose draw it acts on.</summary>
    private static SimPlay Playing(string name, DrawnCardAction action) => new() { Name = name, HandCard = 7, HandDelta = -1 + action.Count, Body = { action } };

    private static DrawnCardAction EscapePlan => new(1) { BlockIf = SimCardType.Skill, Block = new GainBlockAction(1, 3, SimProps.Move) };

    [Fact]
    public void EscapePlanBlocksOnlyWhenItDrawsASkill()
    {
        // One Skill in four: a quarter of the outcomes have its 3 block, so the enemy's 10 takes 7 there and 10 in the rest.
        var o = SimEngine.Run(You(Pile((Skill, 1), (Attack, 3))), Playing("EscapePlan", EscapePlan));
        Assert.Equal(0.25, o.Branches.Where(b => b.Taken == 7).Sum(b => b.Weight), 9);
        Assert.Equal(0.75, o.Branches.Where(b => b.Taken == 10).Sum(b => b.Weight), 9);
    }

    [Fact]
    public void EscapePlanIntoAFullHandDrawsNothingAndGainsNothing()
    {
        var o = SimEngine.Run(You(Pile((Skill, 4)), hand: 11), Playing("EscapePlan", EscapePlan));
        Assert.All(o.Branches, b => Assert.Equal(10, b.Taken));
    }

    [Fact]
    public void PillageDrawsAgainWhileItDrawsAnAttack()
    {
        // Two Attacks and two Skills: it stops at the first Skill. 1 card: 1/2; 2: 2/4 * 2/3 = 1/3; 3: 2/4 * 1/3 * 2/2 = 1/6.
        var o = SimEngine.Run(You(Pile((Attack, 2), (Skill, 2)), hand: 5), Playing("Pillage", new DrawnCardAction(1) { DrawAgainIf = SimCardType.Attack }));
        double Drew(int n) => o.Branches.Where(b => b.P.Draw!.RestCount == 4 - n).Sum(b => b.Weight);
        Assert.Equal(0.5, Drew(1), 9);
        Assert.Equal(1.0 / 3, Drew(2), 9);
        Assert.Equal(1.0 / 6, Drew(3), 9);
    }

    [Fact]
    public void PillageStopsAtAFullHand()
    {
        // Nine in hand with Pillage: it leaves (8), then draws Attacks until the hand holds 10.
        var o = SimEngine.Run(You(Pile((Attack, 6)), hand: 9), Playing("Pillage", new DrawnCardAction(1) { DrawAgainIf = SimCardType.Attack }));
        Assert.All(o.Branches, b => Assert.Equal(4, b.P.Draw!.RestCount));
    }

    [Fact]
    public void ScrapeDiscardsWhatItDrewUnlessItCostsZero()
    {
        // Draws 2 from a free card and a Burn: the Burn is discarded, so it doesn't burn you at the end of your turn.
        var o = SimEngine.Run(You(Pile((FreeCard, 1), (Burn, 1))), Playing("Scrape", new DrawnCardAction(2) { DiscardUnlessFree = true }));
        var b = Assert.Single(o.Branches);
        Assert.Equal(10, b.Taken);                 // the enemy's 10, no Burn
        Assert.Empty(b.P.TurnEndCards);
        Assert.Equal(1, b.PlayEvents["Discarded"]);
    }

    [Fact]
    public void ScrapeDiscardingASlyCardIsNotModelled()
    {
        var o = SimEngine.Run(You(Pile((SlyCard, 1), (FreeCard, 1))), Playing("Scrape", new DrawnCardAction(1) { DiscardUnlessFree = true }));
        Assert.Contains(o.Branches, b => b.Unmodelled != null);
        Assert.Contains(o.Branches, b => b.Unmodelled == null);
    }

    // ---- the chance tab (decision #119)

    [Fact]
    public void EscapePlansTabIsTheChanceItDrawsASkill()
    {
        var o = SimEngine.Run(You(Pile((Skill, 1), (Attack, 3))), Playing("EscapePlan", EscapePlan));
        var d = DrawnChance.Distribution(o)!;
        Assert.Equal(0.25, DrawnChance.ChanceOfAny(d), 9);
    }

    [Fact]
    public void PillagesTabIsTheMostLikelyNumberOfAttacks()
    {
        // Two Attacks and two Skills: 0 Attacks 1/2, 1 Attack 1/3, 2 Attacks 1/6.
        var o = SimEngine.Run(You(Pile((Attack, 2), (Skill, 2))), Playing("Pillage", new DrawnCardAction(1) { DrawAgainIf = SimCardType.Attack }));
        var d = DrawnChance.Distribution(o)!;
        Assert.Equal(0.5, d[0], 9);
        Assert.Equal(1.0 / 3, d[1], 9);
        Assert.Equal(1.0 / 6, d[2], 9);
        Assert.Equal((0, 0.5), (DrawnChance.MostLikely(d).Tally, Math.Round(DrawnChance.MostLikely(d).Chance, 9)));
    }

    [Fact]
    public void ScrapesTabCountsTheCardsThatCostZeroAndATieGoesHigher()
    {
        // Draws 1 from one free card and one that isn't: 0 or 1 free, half each: the tie goes to 1.
        var o = SimEngine.Run(You(Pile((FreeCard, 1), (Burn, 1))), Playing("Scrape", new DrawnCardAction(1) { DiscardUnlessFree = true }));
        var (n, chance) = DrawnChance.MostLikely(DrawnChance.Distribution(o)!);
        Assert.Equal(1, n);
        Assert.Equal(0.5, chance, 9);
    }

    [Fact]
    public void APlayWithoutSuchADrawHasNoTab()
    {
        var o = SimEngine.Run(You(Pile((Skill, 2))), new SimPlay { Name = "Strike", HandCard = 7, HandDelta = -1 });
        Assert.Null(DrawnChance.Distribution(o));
    }

    [Fact]
    public void TheOddsTallyFlagsChancesFarFromWhatHappened()
    {
        var fair = new OddsTally();
        for (var i = 0; i < 100; i++) fair.Add(new SimChance(0.5, 0.5, true), i % 2 == 0);
        Assert.False(fair.Drifted);
        var off = new OddsTally();
        for (var i = 0; i < 100; i++) off.Add(new SimChance(0.1, 0.1, true), true);
        Assert.True(off.Drifted);
    }
}
