using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Enchantments;
using SpireQoL.Sim;
using Xunit;

namespace SpireQoL.Catalog.Tests;

/// <summary>
/// An enchantment's OnPlay acts on every play of its card, after the card (CardModel.OnPlayWrapper), read from its class's code (decision #217):
/// the play the adapter builds carries the card's enchantment (SimCard.Enchantment: its class, amount and status), and the engine runs it, on the
/// installed game's cards (GameFight). A report: a Corrupted Kingly Punch hurt you for 2, which set off Centennial Puzzle, and nothing predicted either.
/// </summary>
[Collection(GameFightCollection.Name)]
public class EnchantmentPlayTests
{
    private static SimPlay PlayOf(GameFight gameFight, MegaCrit.Sts2.Core.Models.CardModel card, int targetId = Who.None) =>
        SimAdapter.Play(card, gameFight.Me, gameFight.Combat, new Dictionary<Creature, int>(), targetId);

    private static SimState Board() => new() { Enemies = { new SimEnemy { Id = 0, Name = "E0", Hp = 60 } } };

    [Fact]
    public void ACorruptedCardHurtsYouAfterItsOwnEffects()
    {
        var gameFight = new GameFight();
        var card = gameFight.Add<StrikeIronclad>();
        Assert.Null(PlayOf(gameFight, card).Card!.Card.Enchantment);
        Assert.Equal(0, SimEngine.Run(Board(), PlayOf(gameFight, card)).TakenMax);
        CardCmd.Enchant<Corrupted>(card, amount: 1);
        var play = PlayOf(gameFight, card);
        Assert.Equal(nameof(Corrupted), play.Card!.Card.Enchantment?.Name);
        var outcome = SimEngine.Run(Board(), play);
        Assert.Equal(2, outcome.TakenMin);
        Assert.Equal(2, outcome.TakenMax);
    }

    [Fact]
    public void AnInkyCardAppliesWeak()
    {
        var gameFight = new GameFight();
        var card = gameFight.Add<StrikeIronclad>();
        CardCmd.Enchant<Inky>(card, amount: 1);
        // Right after the play: the enemies' turn's end ticks it down (WeakPower.AfterSideTurnEnd).
        SimEngine.ApplyPlayTo(Board(), PlayOf(gameFight, card, targetId: 0), out var afterThePlay, out _);
        Assert.All(afterThePlay, branch => Assert.Equal(1, branch.Enemy(0)!.Powers[PowerId.Weak]));
    }

    [Fact]
    public void ASwiftCardsStatusIsReadFromTheGame()
    {
        var gameFight = new GameFight();
        var card = gameFight.Add<DefendIronclad>();
        CardCmd.Enchant<Swift>(card, amount: 2);
        var enchantment = PlayOf(gameFight, card).Card!.Card.Enchantment!;
        Assert.Equal(2, enchantment.Amount);
        Assert.Equal(0, enchantment.Fields["Status"]);
        card.Enchantment!.Status = MegaCrit.Sts2.Core.Entities.Enchantments.EnchantmentStatus.Disabled;
        Assert.Equal(1, PlayOf(gameFight, card).Card!.Card.Enchantment!.Fields["Status"]);
    }
}
