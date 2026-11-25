using Xunit;

namespace SpireQoL.Sim.Tests;

/// <summary>
/// Potion Tags from a potion's simulated use (decision #318: PotionTabs), as a card's proc tabs come from its play's log: the use alone
/// (SimSearch.PlayOnly), and of your relics whose code listens to a potion's use, the ones its log shows acting (a read model where its code
/// flashes). ReptileTrinket.AfterPotionUsed flashes for every potion of its owner's; BeltBuckle.AfterPotionUsed for your last one while its
/// Dexterity isn't up (DexterityApplied). As the name rules they replaced had them: Reptile Trinket on every potion, Belt Buckle on your last.
/// </summary>
public class PotionTabsTests
{
    private static SimState Fight(int potions)
    {
        var state = new SimState { Enemies = { new SimEnemy { Id = 0, Name = "E0", Hp = 40, Hits = new() { 6 }, IntendsToAttack = true } } };
        state.You.Hp = 50;
        state.You.MaxHp = 50;
        state.You.Potions = potions;
        return state
            .WithRelic(Who.You, RelicId.ReptileTrinket, key: "StrengthPower", value: 3)
            .WithRelic(Who.You, RelicId.BeltBuckle, key: "DexterityPower", value: 2);
    }

    private static SimPlay FirePotion()
    {
        var play = new SimPlay { Name = "FirePotion", TargetId = 0, Potion = new SimPotionUse("FirePotion", Who.You, Target: 0) };
        play.Body.Add(new DamageAction(new DamageSpec(20, SimProps.Unpowered)) { TargetId = 0 });
        return play;
    }

    private static readonly string[] Yours = { RelicId.ReptileTrinket.Name, RelicId.BeltBuckle.Name, RelicId.Anchor.Name };

    private static List<(string Model, bool Crossed)> TagsOf(SimState state) => PotionTabs.SetOff(SimSearch.PlayOnly(state, FirePotion()).SearchAll(), Yours);

    [Fact]
    public void EveryPotionSetsOffReptileTrinket()
    {
        Assert.Equal(new[] { (RelicId.ReptileTrinket.Name, false) }, TagsOf(Fight(potions: 2)));
    }

    [Fact]
    public void YourLastPotionSetsOffBeltBuckleTooWhileItsDexterityIsntUp()
    {
        Assert.Equal(new[] { (RelicId.ReptileTrinket.Name, false), (RelicId.BeltBuckle.Name, false) }, TagsOf(Fight(potions: 1)));
        // Its Dexterity already up (holding no potion gave it): drinking doesn't set it off again.
        var given = Fight(potions: 1);
        SimEngine.SetField(given, Who.You, RelicId.BeltBuckle.Name + ".DexterityApplied", value: 1);
        Assert.Equal(new[] { (RelicId.ReptileTrinket.Name, false) }, TagsOf(given));
    }

    [Fact]
    public void ARelicThatDoesntListenToAPotionsUseIsNeverATag()
    {
        // Anchor's code listens to no potion hook: what a potion does never makes it a potion tag (the tags stay the relics drinking sets off).
        Assert.True(SimModels.Get(RelicId.Anchor.Name) is not { } anchor || !PotionTabs.ListensToPotions(anchor));
        Assert.True(PotionTabs.ListensToPotions(SimModels.Get(RelicId.ReptileTrinket.Name)!));
        Assert.True(PotionTabs.ListensToPotions(SimModels.Get(RelicId.BeltBuckle.Name)!));
    }

    [Fact]
    public void TheUseAloneIsSearchedWithNoTurnAfterIt()
    {
        // SimSearch.PlayOnly: the potion's use and its own log; the enemy's attack after it isn't part of it.
        var use = SimSearch.PlayOnly(Fight(potions: 2), FirePotion()).SearchAll();
        Assert.Equal(0, use.TakenMax);
        Assert.Equal(20, Assert.Single(use.Branches).Enemy(id: 0)!.Hp);
        Assert.True(SimEngine.Run(Fight(potions: 2), FirePotion()).TakenMax > 0);
    }
}
