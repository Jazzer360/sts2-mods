using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Enchantments;
using SpireQoL.Procs;
using SpireQoL.Sim;
using Xunit;

namespace SpireQoL.Catalog.Tests;

/// <summary>
/// An enchantment's OnPlay acts on every play of its card, after the card (EnchantmentModel.OnPlay), and the play the adapter builds
/// carries it, on the installed game's cards (GameFight). A report: a Corrupted Kingly Punch hurt you for 2, which set off Centennial
/// Puzzle, and nothing predicted either.
/// </summary>
[Collection(GameFightCollection.Name)]
public class EnchantmentPlayTests
{
    private static SimPlay PlayOf(GameFight gameFight, MegaCrit.Sts2.Core.Models.CardModel card) =>
        SimAdapter.Play(card, PlayFacts.For(card, gameFight.Me, gameFight.Combat, plays: 1), gameFight.Me, gameFight.Combat,
            new Dictionary<Creature, int>(), targetId: Who.None);

    [Fact]
    public void ACorruptedCardHurtsYouAfterItsOwnEffects()
    {
        var gameFight = new GameFight();
        var card = gameFight.Add<StrikeIronclad>();
        Assert.DoesNotContain(PlayOf(gameFight, card).Body, action => action is LoseHpAction);
        CardCmd.Enchant<Corrupted>(card, amount: 1);
        var body = PlayOf(gameFight, card).Body;
        Assert.Equal(2, Assert.IsType<LoseHpAction>(body[^1]).Amount);
    }

    [Fact]
    public void AnInkyCardAppliesWeak()
    {
        var gameFight = new GameFight();
        var card = gameFight.Add<StrikeIronclad>();
        CardCmd.Enchant<Inky>(card, amount: 1);
        Assert.Contains(PlayOf(gameFight, card).Body, action => action is ApplyPowerAction { Power.Name: "WeakPower", To: ApplyPowerAction.Where.Target, Debuff: true });
    }
}
