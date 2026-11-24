using Xunit;

namespace SpireQoL.Sim.Tests;

/// <summary>
/// A card changed while it's being played stays changed for its later plays (CardModel.OnPlayWrapper: each play's OnPlay reads the card as the
/// play before it left it). An upgrade (CardCmd.Upgrade: UpgradeInternal, FinalizeUpgradeInternal) changes its values, so a replay's later plays
/// are its upgraded copy's (CardPlayAction.Upgraded, built by the adapter as the game makes an upgrade preview: MutableClone, UpgradeInternal).
/// </summary>
public class CardChangeTests
{
    private static SimState Board()
    {
        var state = new SimState { Enemies = { new SimEnemy { Id = 0, Name = "E0", Hp = 100 } } };
        state.You.HandCount = 3;
        return state;
    }

    private static AttackAction Hit(int damage) =>
        new(Hits: 1, SimPlay.Target.Single, new DamageSpec(Base: 0m, SimProps.Move) { BaseOn = new Dictionary<int, decimal> { [0] = damage }, Fixed = true }) { TargetId = 0 };

    /// <summary>A Strike (6, upgraded 9) played <paramref name="plays"/> times; its upgraded copy carried when <paramref name="carried"/>.</summary>
    private static SimPlay Strike(int plays, bool carried = true)
    {
        var card = new SimCard("StrikeIronclad", SimCardType.Attack) { Upgradable = true, Energy = 1 };
        var play = new SimPlay
        {
            Name = "StrikeIronclad", TargetId = 0, IsAttack = true, HandCard = 7, HandDelta = -1, Plays = plays,
            Card = new SimCardPlay(card, Who.You, Target: 0, PlayIndex: 0, PlayCount: plays, AutoPlay: false),
            Body = { Hit(damage: 6) },
        };
        if (carried) play.Upgraded = new CardVersion(card with { Upgraded = true, Upgradable = false }, new SimAction[] { Hit(damage: 9) });
        return play;
    }

    [Fact]
    public void ACardRazorToothUpgradesPlaysUpgradedOnItsReplay()
    {
        // RazorTooth.AfterCardPlayed: CardCmd.Upgrade(cardPlay.Card) after the first play; the second OnPlay reads the upgraded Damage (9).
        var state = Board();
        state.You.Relics = RelicSet.Empty.With(RelicId.RazorTooth, amount: 0);
        var branch = Assert.Single(SimEngine.Run(state, Strike(plays: 2)).Branches);
        Assert.Equal(100 - 6 - 9, branch.Enemies[0].Hp);
        Assert.Null(branch.Unmodelled);
    }

    [Fact]
    public void AnUpgradedCardIsUpgradedOnceInAPlay()
    {
        // Played three times: upgraded after the first play, the upgraded card can't be upgraded again (CardCmd.Upgrade: !IsUpgradable continues),
        // so the second and third plays are the upgraded copy's and Razor Tooth acts once.
        var state = Board();
        state.You.Relics = RelicSet.Empty.With(RelicId.RazorTooth, amount: 0);
        var branch = Assert.Single(SimEngine.Run(state, Strike(plays: 3)).Branches);
        Assert.Equal(100 - 6 - 9 - 9, branch.Enemies[0].Hp);
        Assert.Equal(1, branch.Fired[RelicId.RazorTooth.Name]);
    }

    [Fact]
    public void WithoutAnUpgradeEveryPlayIsTheCardAsItWas()
    {
        var branch = Assert.Single(SimEngine.Run(Board(), Strike(plays: 2)).Branches);
        Assert.Equal(100 - 6 - 6, branch.Enemies[0].Hp);
    }

    [Fact]
    public void AnUpgradeWhoseCopyWasntReadMakesTheLaterPlaysUnknown()
    {
        // The card is upgraded mid-play and its upgraded copy wasn't read: its later plays aren't known (decision #92), never its old numbers.
        var state = Board();
        state.You.Relics = RelicSet.Empty.With(RelicId.RazorTooth, amount: 0);
        var branch = Assert.Single(SimEngine.Run(state, Strike(plays: 2, carried: false)).Branches);
        Assert.NotNull(branch.Unmodelled);
    }

    [Fact]
    public void AnUpgradeAfterTheLastPlayChangesNoPlay()
    {
        // Played once: Razor Tooth upgrades it after its only play; nothing plays it again, so nothing is unknown.
        var state = Board();
        state.You.Relics = RelicSet.Empty.With(RelicId.RazorTooth, amount: 0);
        var branch = Assert.Single(SimEngine.Run(state, Strike(plays: 1, carried: false)).Branches);
        Assert.Equal(100 - 6, branch.Enemies[0].Hp);
        Assert.Null(branch.Unmodelled);
        Assert.Equal(1, branch.Fired[RelicId.RazorTooth.Name]);
    }
}
