using Xunit;

namespace SpireQoL.Sim.Tests;

/// <summary>
/// Cards auto-played from the top of your draw pile (AutoPlayFromDrawAction; decompile v0.113.1: CardPileCmd.AutoPlayFromDrawPile at
/// CardPilePosition.Top, from Havoc, Cascade, The Devil and Lost Lute's power): the known top card, else a branch per profile by its
/// count; each plays free (CardCmd.AutoPlay), then goes to the discard pile, or the exhaust pile for Havoc (ExhaustOnNextPlay). An empty
/// draw pile shuffles the discard pile in first.
/// </summary>
public class SimAutoPlayFromDrawTests
{
    private static SimPlay Hits(int damage, int target = 1) => new()
    {
        Name = "StrikeIronclad", IsAttack = true, AutoPlay = true, HandDelta = 0, TargetId = target,
        Card = new SimCardPlay(new SimCard("StrikeIronclad", SimCardType.Attack), Who.You, target, PlayIndex: 0, PlayCount: 1, AutoPlay: true),
        Body = { new AttackAction(Hits: 1, SimPlay.Target.Single, new DamageSpec(Base: 0m, SimProps.Move) { BaseOn = new Dictionary<int, decimal> { [target] = damage }, Fixed = true }) },
    };

    private static SimPlay Blocks(int block) => new()
    {
        Name = "DefendIronclad", AutoPlay = true, HandDelta = 0, TargetId = Who.None,
        Card = new SimCardPlay(new SimCard("DefendIronclad", SimCardType.Skill), Who.You, Who.None, PlayIndex: 0, PlayCount: 1, AutoPlay: true),
        Body = { new GainBlockAction(Times: 1, block, SimProps.Move) },
    };

    private static readonly SimDrawCard Strike = new("strike") { FromPile = new SimHandCard { Name = "StrikeIronclad", IsAttack = true, Plays = { Hits(damage: 6) } } };
    private static readonly SimDrawCard Defend = new("defend") { FromPile = new SimHandCard { Name = "DefendIronclad", Plays = { Blocks(block: 5) } } };
    private static readonly SimDrawCard Unbuilt = new("unbuilt");

    private static SimState You(List<SimDrawGroup> rest, List<SimDrawGroup>? discard = null)
    {
        var state = new SimState { Enemies = { new SimEnemy { Id = 1, Name = "E1", Hp = 50 } } };
        state.You.HandCount = 5;
        state.You.Hp = 50;
        state.You.Draw = new SimDrawPile { Rest = rest, Discard = discard ?? new() };
        return state;
    }

    private static SimPlay Havoc(int count = 1, bool exhaust = true) => new()
    {
        Name = "Havoc", HandCard = 7, HandDelta = -1,
        Body = { new AutoPlayFromDrawAction(count) { ForceExhaust = exhaust } },
    };

    [Fact]
    public void HavocPlaysTheTopCardAndExhaustsIt()
    {
        var outcome = SimEngine.Run(You(new() { new SimDrawGroup(Strike, 1) }), Havoc());
        var branch = Assert.Single(outcome.Branches);
        Assert.Equal(44, branch.Enemy(id: 1)!.Hp);
        Assert.Equal(0, branch.You.Draw!.Count);
        Assert.Equal(0, branch.You.Draw.DiscardCount); // exhausted, not discarded
        Assert.Equal(1, branch.AutoPlayed);
    }

    [Fact]
    public void AnUnknownTopIsABranchPerProfileAndTheCardIsDiscarded()
    {
        // Cascade at X 1 over a Strike and three Defends: the Strike 1/4.
        var outcome = SimEngine.Run(You(new() { new SimDrawGroup(Strike, 1), new SimDrawGroup(Defend, 3) }), Havoc(exhaust: false));
        Assert.Equal(2, outcome.Branches.Count);
        var hit = outcome.Chance(branch => branch.Enemy(id: 1)!.Hp < 50);
        Assert.Equal(0.25, hit.Low, precision: 9);
        Assert.Equal(0.25, hit.High, precision: 9);
        Assert.All(outcome.Branches, branch => Assert.Equal(1, branch.You.Draw!.DiscardCount));
    }

    [Fact]
    public void AnEmptyDrawPileShufflesTheDiscardPileInFirst()
    {
        var outcome = SimEngine.Run(You(new(), discard: new() { new SimDrawGroup(Strike, 1) }), Havoc());
        Assert.Equal(44, Assert.Single(outcome.Branches).Enemy(id: 1)!.Hp);
    }

    private static SimState WithCorruption(SimState state)
    {
        state.You.Powers = state.You.Powers.With(PowerId.Corruption, amount: 1).With(PowerId.FeelNoPain, amount: 3).With(PowerId.DarkEmbrace, amount: 1);
        return state;
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void CorruptionExhaustsASkillPlayedFromYourDrawPileOnce(bool havoc)
    {
        // CardCmd.AutoPlay plays it through OnPlayWrapper, whose result location is the card's own (the discard pile, or the exhaust pile for
        // Havoc's ExhaustOnNextPlay) through Hook.ModifyCardPlayResultLocation: Corruption sends a Skill to the exhaust pile. The Defend's one
        // exhaust and the card that played it (a Skill too, Corruption's as well): two Feel No Pain blocks, two Dark Embrace draws, two cards in
        // the exhaust pile, none in the discard pile.
        var state = WithCorruption(You(new() { new SimDrawGroup(new SimDrawCard("drawn"), 2) }));
        state.You.Draw!.Top.Add(Defend);
        var branches = SimEngine.Run(state, Havoc(exhaust: havoc)).Branches;
        Assert.All(branches, branch =>
        {
            Assert.Equal(5 + 3 + 3, branch.You.Block);
            Assert.Equal(2, branch.You.ExhaustPile);
            Assert.Equal(0, branch.You.Draw!.DiscardCount);
            Assert.Equal(4 + 2, branch.You.HandCount); // the card that played it left your hand, Dark Embrace drew two
        });
    }

    [Fact]
    public void CorruptionGainedDuringThePlayExhaustsASkillPlayedFromYourDrawPile()
    {
        // Corruption given before the auto-play (it wasn't on the board): its result location still sends the Skill to the exhaust pile.
        var state = You(new() { new SimDrawGroup(Defend, 1) });
        state.You.Powers = state.You.Powers.With(PowerId.FeelNoPain, amount: 3);
        var play = Havoc(exhaust: false);
        play.Body.Insert(0, new ApplyPowerAction(PowerId.Corruption, Amount: 1) { To = ApplyPowerAction.Where.You });
        var branch = Assert.Single(SimEngine.Run(state, play).Branches);
        Assert.Equal(5 + 3, branch.You.Block);
        Assert.Equal(1, branch.You.ExhaustPile);
        Assert.Equal(0, branch.You.Draw!.DiscardCount);
    }

    [Fact]
    public void ReboundPutsACardPlayedFromYourDrawPileBackOnTop()
    {
        // ReboundPower.ModifyCardPlayResultLocation: a card of its owner's bound for the discard pile goes on top of the draw pile, a stack each
        // (the card that played it uses the first).
        // Havoc itself goes on top once its play is over (decision #388), above the card it played.
        var state = You(new() { new SimDrawGroup(Strike, 1) });
        state.You.Powers = state.You.Powers.With(PowerId.Rebound, amount: 2);
        var havoc = Havoc(exhaust: false);
        havoc.PileProfile = new SimDrawCard("havoc");
        var branch = Assert.Single(SimEngine.Run(state, havoc).Branches);
        Assert.Equal(44, branch.Enemy(id: 1)!.Hp);
        Assert.Equal(new[] { "havoc", "strike" }, branch.You.Draw!.Top.Select(card => card.Key));
        Assert.Equal(0, branch.You.Draw.DiscardCount);
        Assert.False(branch.You.Powers.Has(PowerId.Rebound));
        Assert.Null(branch.Unmodelled);
    }

    private static SimPlay HandDefend(SimDrawCard? pileProfile) => new()
    {
        Name = "DefendIronclad", HandCard = 7, HandDelta = -1, TargetId = Who.None, PileProfile = pileProfile,
        Card = new SimCardPlay(new SimCard("DefendIronclad", SimCardType.Skill), Who.You, Who.None, PlayIndex: 0, PlayCount: 1, AutoPlay: false),
        Body = { new GainBlockAction(Times: 1, Base: 5, SimProps.Move) },
    };

    [Fact]
    public void ReboundPutsACardPlayedFromYourHandOnTopForALaterDraw()
    {
        // Decision #388: OnPlayWrapper moves a card played from your hand to its result location (Rebound's top of the draw pile) before
        // CheckForEmptyHand, so your last card played under Rebound with an empty draw pile is the card Unceasing Top draws.
        var state = You(new());
        state.You.HandCount = 1;
        state.You.Relics = state.You.Relics.With(RelicId.UnceasingTop, amount: 0, data: 1);
        state.You.Powers = state.You.Powers.With(PowerId.Rebound, amount: 1);
        var branch = Assert.Single(SimEngine.Run(state, HandDefend(new SimDrawCard("defend") { Fading = true })).Branches);
        Assert.True(branch.Fired.Has("UnceasingTop"));
        Assert.Equal(1, branch.You.HandCount);
        Assert.Equal(0, branch.You.Draw!.Count);
        Assert.Equal(1, branch.You.Fading); // drawn as itself: Ethereal in your hand
        Assert.Null(branch.Unmodelled);

        // With another card left in your hand nothing draws it: it waits on top of the pile.
        state.You.HandCount = 2;
        branch = Assert.Single(SimEngine.Run(state, HandDefend(new SimDrawCard("defend"))).Branches);
        Assert.Equal("defend", Assert.Single(branch.You.Draw!.Top).Key);
        Assert.Equal(1, branch.You.HandCount);
    }

    [Fact]
    public void ACardPutOnTopWhoseProfileIsntReadIsUnknownOnlyOnceDrawn()
    {
        // A play built before the board's piles are read carries no profile: the card counts in the pile, and drawing it makes the branch unknown.
        var state = You(new());
        state.You.HandCount = 2;
        state.You.Powers = state.You.Powers.With(PowerId.Rebound, amount: 1);
        var waiting = Assert.Single(SimEngine.Run(state, HandDefend(pileProfile: null)).Branches);
        Assert.Equal(1, waiting.You.Draw!.Count);
        Assert.Null(waiting.Unmodelled);
        state.You.HandCount = 1;
        state.You.Relics = state.You.Relics.With(RelicId.UnceasingTop, amount: 0, data: 1);
        Assert.NotNull(Assert.Single(SimEngine.Run(state, HandDefend(pileProfile: null)).Branches).Unmodelled);
    }

    [Fact]
    public void ACardHavocExhaustsGivesItsOwnExhaustEnergy()
    {
        // Drum of Battle's AfterCardExhausted (card == this): its energy once Havoc's ExhaustOnNextPlay exhausts it, as when it exhausts itself.
        var drum = new SimDrawCard("drum")
        {
            FromPile = new SimHandCard { Name = "DrumOfBattle", Plays = { new SimPlay { Name = "DrumOfBattle", AutoPlay = true, HandDelta = 0, TargetId = Who.None,
                Card = new SimCardPlay(new SimCard("DrumOfBattle", SimCardType.Skill) { ExhaustEnergy = 1 }, Who.You, Who.None, PlayIndex: 0, PlayCount: 1, AutoPlay: true) } } },
        };
        var state = You(new() { new SimDrawGroup(drum, 1) });
        var energy = state.You.Energy;
        var branch = Assert.Single(SimEngine.Run(state, Havoc()).Branches);
        Assert.Equal(energy + 1, branch.You.Energy);
        Assert.Equal(1, branch.You.ExhaustPile);
        Assert.Equal(0, branch.You.Draw!.DiscardCount);
    }

    [Fact]
    public void HavocsPowerGoesNowhere()
    {
        // GetResultLocationForCardPlay: a Power goes nowhere, ExhaustOnNextPlay or not (its type is asked first): no exhaust.
        var power = new SimDrawCard("power")
        {
            FromPile = new SimHandCard { Name = "Inflame", Plays = { new SimPlay { Name = "Inflame", AutoPlay = true, HandDelta = 0, TargetId = Who.None,
                Card = new SimCardPlay(new SimCard("Inflame", SimCardType.Power), Who.You, Who.None, PlayIndex: 0, PlayCount: 1, AutoPlay: true) } } },
        };
        var state = You(new() { new SimDrawGroup(power, 1) });
        state.You.Powers = state.You.Powers.With(PowerId.FeelNoPain, amount: 3);
        var branch = Assert.Single(SimEngine.Run(state, Havoc()).Branches);
        Assert.Equal(0, branch.You.Block);
        Assert.Equal(0, branch.You.ExhaustPile);
        Assert.Equal(0, branch.You.Draw!.DiscardCount);
    }

    [Fact]
    public void ACardWhosePlayIsntBuiltIsUnknown()
    {
        var outcome = SimEngine.Run(You(new() { new SimDrawGroup(Unbuilt, 1) }), Havoc());
        Assert.NotNull(Assert.Single(outcome.Branches).Unmodelled);
    }
}
