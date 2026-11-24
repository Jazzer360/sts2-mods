using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Relics;
using SpireQoL.Sim;
using Xunit;

namespace SpireQoL.Catalog.Tests;

/// <summary>
/// A card a play upgrades (RazorTooth.AfterCardPlayed: CardCmd.Upgrade(cardPlay.Card)) plays its later plays upgraded, as CardModel.OnPlayWrapper's
/// next OnPlay reads the upgraded card's values: the play the adapter builds carries its upgraded copy (SimPlay.Upgraded, made as the game makes an
/// upgrade preview), on the installed game's cards (GameFight).
/// </summary>
[Collection(GameFightCollection.Name)]
public class UpgradedReplayTests
{
    private static SimState Board() => new() { Enemies = { new SimEnemy { Id = 0, Name = "E0", Hp = 100 } } };

    [Fact]
    public void AStrikeRazorToothUpgradesHitsForItsUpgradedDamageOnItsReplay()
    {
        var gameFight = new GameFight();
        gameFight.Me.AddRelicInternal(ModelDb.Relic<RazorTooth>().ToMutable());
        var card = gameFight.Add<StrikeIronclad>();
        var ids = new Dictionary<Creature, int>();
        var once = SimAdapter.Play(card, gameFight.Me, gameFight.Combat, ids, targetId: 0, plays: 1);
        Assert.Null(once.Upgraded);
        var twice = SimAdapter.Play(card, gameFight.Me, gameFight.Combat, ids, targetId: 0, plays: 2);
        Assert.NotNull(twice.Upgraded);
        Assert.True(twice.Upgraded!.Card.Upgraded);
        Assert.False(twice.Upgraded.Card.Upgradable);
        var damage = (int)card.DynamicVars.Damage.BaseValue;
        var upgraded = (CardModel)card.MutableClone();
        upgraded.UpgradeInternal();
        upgraded.FinalizeUpgradeInternal();
        var upgradedDamage = (int)upgraded.DynamicVars.Damage.BaseValue;
        Assert.True(upgradedDamage > damage);
        var board = Board();
        board.You.Relics = NamedSet<RelicId>.Empty.With(RelicId.RazorTooth, amount: 0);
        var outcome = SimEngine.Run(board, twice);
        Assert.True(outcome.InEvery(branch => branch.Enemy(0)!.Hp == 100 - damage - upgradedDamage));
    }

    [Fact]
    public void WithNothingToUpgradeItNoUpgradedCopyIsRead()
    {
        var gameFight = new GameFight();
        var card = gameFight.Add<StrikeIronclad>();
        Assert.Null(SimAdapter.Play(card, gameFight.Me, gameFight.Combat, new Dictionary<Creature, int>(), targetId: 0, plays: 2).Upgraded);
    }
}
