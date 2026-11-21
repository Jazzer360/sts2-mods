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
        Assert.Equal(1, b.PlayEvents[CardEvent.Discarded]);
    }

    [Fact]
    public void ScrapeDiscardingASlyCardIsNotModelled()
    {
        var o = SimEngine.Run(You(Pile((SlyCard, 1), (FreeCard, 1))), Playing("Scrape", new DrawnCardAction(1) { DiscardUnlessFree = true }));
        Assert.Contains(o.Branches, b => b.Unmodelled != null);
        Assert.Contains(o.Branches, b => b.Unmodelled == null);
    }

    // ---- against the textbook odds (decision #120): every pile makeup and hand size, not just ones worked out by hand

    /// <summary>
    /// Pillage's chance of drawing each number of Attacks, worked out the plain way: it has drawn t Attacks so far; the next card
    /// is a non-Attack with chance k / (cards left), which stops it, else an Attack, and it goes on, until the hand is full (room
    /// draws) or the piles are empty.
    /// </summary>
    private static Dictionary<int, double> PillageOdds(int attacks, int others, int room)
    {
        var d = new Dictionary<int, double>();
        var p = 1.0;
        for (var t = 0; p > 0; t++)
        {
            var left = attacks + others - t;
            if (t == room || left == 0) { d[t] = d.GetValueOrDefault(t) + p; break; }
            d[t] = d.GetValueOrDefault(t) + p * others / left;
            p *= (double)(attacks - t) / left;
        }
        return d.Where(kv => kv.Value > 0).ToDictionary(kv => kv.Key, kv => kv.Value);
    }

    private static double Choose(int n, int k) => k < 0 || k > n ? 0 : Enumerable.Range(1, k).Aggregate(1.0, (a, i) => a * (n - k + i) / i);

    private static void SameOdds(Dictionary<int, double> expected, SimOutcome o, string what)
    {
        var d = DrawnChance.Distribution(o)!;
        Assert.True(Math.Abs(d.Values.Sum() - 1) < 1e-9, $"{what}: the chances add to {d.Values.Sum()}");
        foreach (var n in expected.Keys.Union(d.Keys))
            Assert.True(Math.Abs(expected.GetValueOrDefault(n) - d.GetValueOrDefault(n)) < 1e-9,
                $"{what}: {n} drawn, expected {expected.GetValueOrDefault(n)}, simulated {d.GetValueOrDefault(n)}");
    }

    [Fact]
    public void PillageMatchesTheTextbookOddsForEveryPileAndHand()
    {
        // The hand counts Pillage; it leaves first, so the hand has room for 11 - hand draws.
        for (var attacks = 0; attacks <= 8; attacks++)
        for (var others = 0; others <= 6; others++)
        for (var hand = 1; hand <= 10; hand++)
        {
            if (attacks + others == 0) continue;
            var pile = new List<(SimDrawCard, int)>();
            if (attacks > 0) pile.Add((Attack, attacks));
            if (others > 0) pile.Add((Skill, others));
            var o = SimEngine.Run(You(Pile(pile.ToArray()), hand), Playing("Pillage", new DrawnCardAction(1) { DrawAgainIf = SimCardType.Attack }));
            SameOdds(PillageOdds(attacks, others, 11 - hand), o, $"{attacks} Attacks, {others} others, hand {hand}");
        }
    }

    [Fact]
    public void PillageWithAttacksOfSeveralKindsHasTheSameOddsAndStaysCheap()
    {
        // 12 Attacks of four kinds (a Burn-like one among them) and 8 others, an empty hand: up to 10 draws. Drawn in every order,
        // that's 4^10 paths; merged draw by draw, it's the sets drawn. The odds don't depend on the kinds.
        var burnAttack = new SimDrawCard("burn attack") { Type = SimCardType.Attack, TurnEnd = new TurnEndCard("Burn", "Burn", 2) };
        var kinds = new[] { Attack, new SimDrawCard("attack 2") { Type = SimCardType.Attack }, new SimDrawCard("attack 3") { Type = SimCardType.Attack }, burnAttack };
        var mixed = SimEngine.Run(You(Pile((kinds[0], 3), (kinds[1], 3), (kinds[2], 3), (kinds[3], 3), (Skill, 8)), hand: 1),
            Playing("Pillage", new DrawnCardAction(1) { DrawAgainIf = SimCardType.Attack }));
        SameOdds(PillageOdds(12, 8, 10), mixed, "four kinds of Attack");
        Assert.True(mixed.Copies < 5000, $"{mixed.Copies} state copies");
    }

    [Fact]
    public void ScrapeMatchesTheTextbookOddsForEveryPile()
    {
        // Draws 4 (with an empty hand, all fit): f free cards among them with chance C(F, f) C(X, 4 - f) / C(F + X, 4), or all
        // of a smaller pile.
        for (var free = 0; free <= 7; free++)
        for (var others = 0; others <= 7; others++)
        {
            var n = free + others;
            if (n == 0) continue;
            var draws = Math.Min(4, n);
            var pile = new List<(SimDrawCard, int)>();
            if (free > 0) pile.Add((FreeCard, free));
            if (others > 0) pile.Add((Burn, others));
            var o = SimEngine.Run(You(Pile(pile.ToArray()), hand: 1), Playing("Scrape", new DrawnCardAction(4) { DiscardUnlessFree = true }));
            var expected = Enumerable.Range(0, draws + 1).ToDictionary(f => f, f => Choose(free, f) * Choose(others, draws - f) / Choose(n, draws));
            SameOdds(expected.Where(kv => kv.Value > 0).ToDictionary(kv => kv.Key, kv => kv.Value), o, $"{free} free, {others} others");
        }
    }

    [Fact]
    public void EscapePlanMatchesTheShareOfSkills()
    {
        for (var skills = 0; skills <= 6; skills++)
        for (var others = 0; others <= 6; others++)
        {
            if (skills + others == 0) continue;
            var pile = new List<(SimDrawCard, int)>();
            if (skills > 0) pile.Add((Skill, skills));
            if (others > 0) pile.Add((Attack, others));
            var o = SimEngine.Run(You(Pile(pile.ToArray())), Playing("EscapePlan", EscapePlan));
            Assert.Equal((double)skills / (skills + others), DrawnChance.ChanceOfAny(DrawnChance.Distribution(o)!), 9);
        }
    }

    [Fact]
    public void PillageWithIterationDrawsItsExtraCardsWithoutCountingThem()
    {
        // Iteration draws 2 more on your first Status this turn: a Status stops Pillage (not an Attack), and the 2 it chains
        // aren't Pillage's (CardPileCmd.Draw returns only its own card), so they neither count nor carry it on. 1 Attack, 1 Status,
        // 2 Attacks: it draws the Status first half the time (0 Attacks counted), else the Attack, then the Status (1).
        var status = new SimDrawCard("status") { Type = SimCardType.Status, Status = true };
        var s = You(Pile((Attack, 1), (status, 1)), hand: 1);
        s.P.Powers = s.P.Powers.With("IterationPower", 2, 0);
        s.P.Draw!.Rest.Add(new SimDrawGroup(new SimDrawCard("attack b") { Type = SimCardType.Attack }, 2));
        var o = SimEngine.Run(s, Playing("Pillage", new DrawnCardAction(1) { DrawAgainIf = SimCardType.Attack }));
        // Attacks: 3 in 4. Draws Attacks until the Status: 0 with 1/4, 1 with 3/4 * 1/3, 2 with 3/4 * 2/3 * 1/2, 3 with 1/4.
        SameOdds(new() { [0] = 0.25, [1] = 0.25, [2] = 0.25, [3] = 0.25 }, o, "with Iteration");
        // Iteration's 2 follow the Status: drawn first, it leaves 1 card in the pile; any later, it empties it.
        Assert.Equal(0.25, o.Branches.Where(b => b.P.Draw!.RestCount == 1).Sum(b => b.Weight), 9);
        Assert.Equal(0.75, o.Branches.Where(b => b.P.Draw!.RestCount == 0).Sum(b => b.Weight), 9);
    }

    // ---- Hellraiser (decision #121): a Strike you draw plays itself at once

    /// <summary>A Strike as the adapter builds its auto-play: 6 damage at each enemy it could be aimed at.</summary>
    private static SimDrawCard HellStrike(params int[] targets) => new("strike") { Type = SimCardType.Attack, Strike = true, Auto = new SimHandCard
    {
        Name = "StrikeIronclad", IsAttack = true, Plays = targets.Select(t => new SimPlay
        {
            Name = "StrikeIronclad", IsAttack = true, AutoPlay = true, HandDelta = -1, TargetId = t,
            Card = new SimCardPlay(new SimCard("StrikeIronclad", SimCardType.Attack), Who.You, t, 0, 1, true),
            Body = { new AttackAction(1, SimPlay.Target.Single, new DamageSpec(0m, SimProps.Move) { BaseOn = new Dictionary<int, decimal> { [t] = 6 }, Fixed = true }) },
        }).ToList(),
    } };

    private static SimState Hellraised(SimDrawPile pile, int hand = 1, int enemies = 1)
    {
        var s = You(pile, hand);
        for (var id = 2; id <= enemies; id++) s.Enemies.Add(new SimEnemy { Id = id, Name = "E" + id, Hp = 50, Hits = new() { 10 } });
        s.P.Powers = s.P.Powers.With("HellraiserPower", 1, 0);
        return s;
    }

    [Fact]
    public void HellraiserPlaysADrawnStrikeAtARandomEnemy()
    {
        // A draw of 1 from a pile of only Strikes, two enemies: it plays itself at either, half each, and leaves your hand.
        var o = SimEngine.Run(Hellraised(Pile((HellStrike(1, 2), 3)), hand: 3, enemies: 2),
            new SimPlay { Name = "Draw1", HandCard = 7, HandDelta = -1, Body = { new CardEventAction(CardEvent.Drawn, 1) } });
        Assert.All(o.Branches, b => Assert.Null(b.Unmodelled));
        Assert.Equal(0.5, o.Branches.Where(b => b.Enemies[0].Hp == 44 && b.Enemies[1].Hp == 50).Sum(b => b.Weight), 9);
        Assert.Equal(0.5, o.Branches.Where(b => b.Enemies[1].Hp == 44 && b.Enemies[0].Hp == 50).Sum(b => b.Weight), 9);
        Assert.All(o.Branches, b => Assert.Equal(1, b.AutoPlayed));
    }

    [Fact]
    public void PillageWithHellraiserKeepsDrawingPastAStrikeThatPlayedItself()
    {
        // Pillage's draw is still the Strike (an Attack) after it plays itself, and your hand has the room it left: it draws on.
        // One Strike, one Skill: the Strike first (half): 1 Attack, 6 damage; the Skill first: none.
        var o = SimEngine.Run(Hellraised(Pile((HellStrike(1), 1), (Skill, 1))),
            Playing("Pillage", new DrawnCardAction(1) { DrawAgainIf = SimCardType.Attack }));
        Assert.All(o.Branches, b => Assert.Null(b.Unmodelled));
        SameOdds(new() { [0] = 0.5, [1] = 0.5 }, o, "with Hellraiser");
        Assert.Equal(0.5, o.Branches.Where(b => b.Enemies[0].Hp == 44).Sum(b => b.Weight), 9);
        // The Strike played: whichever came first, both cards were drawn in the Strike-first branch, one in the other.
        Assert.Equal(0.5, o.Branches.Where(b => b.P.Draw!.RestCount == 0).Sum(b => b.Weight), 9);
    }

    [Fact]
    public void HellraiserWithAStrikeWhosePlayIsntBuiltIsNotModelled()
    {
        var bare = new SimDrawCard("bare strike") { Type = SimCardType.Attack, Strike = true };
        var o = SimEngine.Run(Hellraised(Pile((bare, 2))),
            new SimPlay { Name = "Draw1", HandCard = 7, HandDelta = -1, Body = { new CardEventAction(CardEvent.Drawn, 1) } });
        Assert.All(o.Branches, b => Assert.NotNull(b.Unmodelled));
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
