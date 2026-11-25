using Xunit;

namespace SpireQoL.Sim.Tests;

/// <summary>
/// A card play a model keeps (decision #269: Stretch's data's cardPlay, Pael's Legion's AffectedCardPlay) is that card play, by its identity: a card
/// played inside it (Hellraiser's Strike, drawn by the card being played) is another, so its AfterCardPlayed neither spends nor lets go what the outer
/// play kept, and the outer play's later block still gets it (StretchPower.AfterCardPlayed's cardPlay == internalData.cardPlay; PaelsLegion's
/// AffectedCardPlay == cardPlay).
/// </summary>
public class KeptPlayTests
{
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

    private static SimState Board()
    {
        var state = new SimState { Enemies = { new SimEnemy { Id = 1, Name = "E1", Hp = 50 } } };
        state.You.Hp = 50;
        state.You.HandCount = 3;
        state.You.Draw = new SimDrawPile { Rest = { new SimDrawGroup(HellStrike, 3) } };
        state.You.Powers = state.You.Powers.With(PowerId.Hellraiser, amount: 1, data: 0);
        return state;
    }

    /// <summary>A Skill that gains block, its actions <paramref name="body"/> (a draw plays the Strike inside it).</summary>
    private static SimPlay Defend(params SimAction[] body)
    {
        var card = new SimCard("Defend", SimCardType.Skill) { GainsBlock = true };
        var play = new SimPlay { Name = card.Name, HandCard = 7, HandDelta = -1, Card = new SimCardPlay(card, Who.You, Who.None, PlayIndex: 0, PlayCount: 1, AutoPlay: false) };
        play.Body.AddRange(body);
        return play;
    }

    [Fact]
    public void StretchKeepsTheCardPlayItBoundThroughAPlayInsideIt()
    {
        // Stretch 3 binds the Defend (it gains block); the Strike its draw plays isn't it, so the Defend's block after the draw gets the 3, and the
        // Defend's own AfterCardPlayed spends them.
        var state = Board();
        state.You.Powers = state.You.Powers.With(PowerId.Stretch, amount: 3);
        SimEngine.ApplyPlayTo(state, Defend(new CardEventAction(CardEvent.Drawn, Count: 1), ScriptBuilder.Block(baseBlock: 5)), out var branches, out _);
        var branch = Assert.Single(branches);
        Assert.Null(branch.Unmodelled);
        Assert.Equal(1, branch.AutoPlayed);
        Assert.Equal(8, branch.You.Block);
        Assert.False(branch.You.Powers.Has(PowerId.Stretch));
    }

    [Fact]
    public void PaelsLegionDoublesTheBlockOfThePlayItKeptAfterAPlayInsideIt()
    {
        // Pael's Legion off cooldown doubles the Defend's first block and keeps its card play; the Strike played inside it isn't that play, so its
        // AfterCardPlayed starts no cooldown, and the Defend's second block is doubled too: 10 + 10.
        var state = Board();
        state.You.Relics = state.You.Relics.With(RelicId.PaelsLegion, amount: 0, data: 0);
        SimEngine.ApplyPlayTo(state, Defend(ScriptBuilder.Block(baseBlock: 5), new CardEventAction(CardEvent.Drawn, Count: 1), ScriptBuilder.Block(baseBlock: 5)), out var branches, out _);
        var branch = Assert.Single(branches);
        Assert.Null(branch.Unmodelled);
        Assert.Equal(1, branch.AutoPlayed);
        Assert.Equal(20, branch.You.Block);
    }

    [Fact]
    public void PaelsLegionDoublesOnlyTheFirstCardPlaysBlockAndStartsItsCooldown()
    {
        // PaelsLegion: off cooldown, the first play whose block it changes is kept; its AfterCardPlayed sets its Cooldown to its Turns (2), so the next
        // play's block isn't doubled.
        var state = new SimState { Enemies = { new SimEnemy { Id = 1, Name = "E1", Hp = 50 } } };
        state.You.Relics = state.You.Relics.With(RelicId.PaelsLegion, amount: 0, data: 0);
        SimEngine.ApplyPlayTo(state, Defend(ScriptBuilder.Block(baseBlock: 5)), out var first, out _);
        var after = Assert.Single(first);
        Assert.Equal(10, after.You.Block);
        Assert.True(after.Fired.Has(RelicId.PaelsLegion.Name));
        SimEngine.ApplyPlayTo(after, Defend(ScriptBuilder.Block(baseBlock: 5)), out var second, out _);
        Assert.Equal(15, Assert.Single(second).You.Block);
    }
}

/// <summary>
/// A card's enchantment tested (decision #269: card.Enchantment != null, card.Enchantment?.GainsBlock: CardEnchanted, EnchantmentGainsBlock): Mystic
/// Lighter, read from its code with it, adds its Damage (9) to its owner's powered attack from an enchanted card, and nothing to one from a plain card.
/// </summary>
public class EnchantedCardTests
{
    private static int DamageDealt(bool enchanted)
    {
        var state = new SimState { Enemies = { new SimEnemy { Id = 1, Name = "E1", Hp = 50 } } };
        state.You.Relics = state.You.Relics.With(RelicId.MysticLighter, amount: 0, data: 0);
        var card = new SimCard("Strike", SimCardType.Attack) { Enchanted = enchanted };
        var after = Assert.Single(ScriptBuilder.After(state, ScriptBuilder.Play(card, target: 1, plays: 1, ScriptBuilder.Attack(baseDamage: 6))));
        return 50 - after.Enemy(id: 1)!.Hp;
    }

    [Fact]
    public void MysticLighterAddsItsDamageToAnEnchantedCardsAttack()
    {
        Assert.Equal(15, DamageDealt(enchanted: true));
        Assert.Equal(6, DamageDealt(enchanted: false));
    }
}
