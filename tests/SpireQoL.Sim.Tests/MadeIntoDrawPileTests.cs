using Xunit;
using static SpireQoL.Sim.Tests.ScriptBuilder;

namespace SpireQoL.Sim.Tests;

/// <summary>
/// A card a model makes into a draw pile (CardPileCmd.AddGeneratedCardToCombat(card, PileType.Draw, creator, CardPilePosition.Random)): the game
/// adds it to the fight's log (CombatHistory.CardGenerated: its Creator), puts it in the pile at a random spot, then tells
/// Hook.AfterCardGeneratedForCombat(card, creator). Biiig Hug's Soot (BiiigHug.AfterShuffle) has you as its creator, so your created-card hooks
/// hear it (Pillar of Creation, Smokestack on a Status); Personal Hive's Dazed (PersonalHivePower.AfterDamageReceived) has none (creator null), so
/// no one's do, and its dealer's Osty puts them in his player's pile (`dealer = dealer.PetOwner.Creature`).
/// </summary>
public class MadeIntoDrawPileTests
{
    private static SimState BiiigHugBoard()
    {
        var state = new SimState { Enemies = { Enemy(id: 0, hp: 50) } };
        state.You.Relics = state.You.Relics.With(RelicId.BiiigHug, amount: 0, data: 1);
        state.You.Draw = new SimDrawPile { Rest = { new SimDrawGroup(new SimDrawCard("plain"), 2) } };
        state.You.MadeCards = new Dictionary<string, SimDrawCard> { [CardId.Soot.Name] = new SimDrawCard("soot") };
        return state;
    }

    private static SimScript Shuffles() => Play(Card("Defend", SimCardType.Skill), target: Who.None, plays: 1, new CardEventAction(CardEvent.Shuffled, Count: 1));

    [Fact]
    public void BiiigHugsSootIsACardYouMade()
    {
        // PillarOfCreationPower.AfterCardGeneratedForCombat: creator != null && creator.Creature == base.Owner → its block.
        var state = BiiigHugBoard();
        state.You.Powers = state.You.Powers.With(PowerId.PillarOfCreation, amount: 3);
        var after = Assert.Single(After(state, Shuffles()));
        Assert.Equal(1, after.You.Draw!.Rest.Single(group => group.Card.Key == "soot").Count);
        Assert.Equal(3, after.You.Block);
    }

    [Fact]
    public void BiiigHugsSootIsAStatusForSmokestack()
    {
        // SmokestackPower.AfterCardGeneratedForCombat: card.Type == Status && creator is its owner → its damage to every hittable enemy. Soot is a Status.
        var state = BiiigHugBoard();
        state.You.Powers = state.You.Powers.With(PowerId.Smokestack, amount: 4);
        var after = Assert.Single(After(state, Shuffles()));
        Assert.Equal(46, after.Enemy(0)!.Hp);
    }

    [Fact]
    public void PersonalHivesDazedHaveNoCreator()
    {
        // CardPileCmd.AddGeneratedCardToCombat(card, PileType.Draw, null, Random): no creator, so your Pillar of Creation doesn't hear them.
        var state = new SimState { Enemies = { Enemy(id: 1, hp: 50) } };
        state.Enemies[0].Powers = PowerSet.Empty.With(PowerId.PersonalHive, amount: 2);
        state.You.Powers = state.You.Powers.With(PowerId.PillarOfCreation, amount: 3);
        state.You.Draw = new SimDrawPile { Rest = { new SimDrawGroup(new SimDrawCard("plain"), 3) } };
        state.You.MadeCards = new Dictionary<string, SimDrawCard> { [CardId.Dazed.Name] = new SimDrawCard("dazed") };
        var after = Assert.Single(After(state, Play(Card("Strike"), target: 1, plays: 1, Attack(baseDamage: 3))));
        Assert.Equal(2, after.You.Draw!.Rest.Single(group => group.Card.Key == "dazed").Count);
        Assert.Equal(0, after.You.Block);
    }

    [Fact]
    public void OstysHitOnPersonalHivePutsTheDazedInYourPile()
    {
        // PersonalHivePower.AfterDamageReceived: dealer != null && props.IsPoweredAttack(); `if (dealer.Monster is Osty) dealer = dealer.PetOwner.Creature`,
        // then CreateCard<Dazed>(dealer.Player): your Osty's powered hit makes them for you.
        var state = new SimState { Enemies = { Enemy(id: 1, hp: 50) } };
        state.Enemies[0].Powers = PowerSet.Empty.With(PowerId.PersonalHive, amount: 1);
        state.You.WithOsty(hp: 10);
        state.You.Draw = new SimDrawPile { Rest = { new SimDrawGroup(new SimDrawCard("plain"), 3) } };
        state.You.MadeCards = new Dictionary<string, SimDrawCard> { [CardId.Dazed.Name] = new SimDrawCard("dazed") };
        var poke = new SimPlay { Name = "Poke", HitTarget = SimPlay.Target.Single, TargetId = 1, Hits = 1, HitDamage = { [1] = 4 }, ByOsty = true };
        SimEngine.ApplyPlayTo(state, poke, out var branches, out _);
        var after = Assert.Single(branches);
        Assert.Equal(1, after.You.Draw!.Rest.Single(group => group.Card.Key == "dazed").Count);
    }
}
