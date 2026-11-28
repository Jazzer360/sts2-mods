using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Monsters;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.MonsterMoves.MonsterMoveStateMachine;
using SpireQoL.Sim;
using Xunit;

namespace SpireQoL.Catalog.Tests;

/// <summary>
/// A dev review of the owner's multiplayer run: Thieving Hopper's Flutter showed 5 on its intent and landed 12. PowerCmd.Apply scales a power with
/// ShouldScaleInMultiplayer on an enemy in a fight of more than one player (GetScaledAmountForMultiplayer); Intent Details shows the amount it lands.
/// </summary>
[Collection(GameFightCollection.Name)]
public class MovePowerScalingTests
{
    private static decimal? Flutter(int teammates)
    {
        var fight = new GameFight(teammates);
        var hopper = fight.Combat.CreateCreature(ModelDb.Monster<ThievingHopper>().ToMutable(), CombatSide.Enemy, slot: null);
        fight.Combat.AddCreature(hopper);
        hopper.Monster!.SetUpForCombat();
        var move = hopper.Monster!.MoveStateMachine!.States.Values.OfType<MoveState>().Single(state => state.Id == "FLUTTER_MOVE");
        return MovePowers.Of(hopper.Monster, move).Single(applied => applied.Power == typeof(FlutterPower)).Amount;
    }

    [Fact]
    public void AMovesBuffIsShownAsItLandsForThePlayersInTheFight()
    {
        Assert.Equal(5m, Flutter(teammates: 0));
        var scaled = Flutter(teammates: 1);
        Assert.NotNull(scaled);
        Assert.True(scaled > 5m, $"two players: {scaled}");
    }
}
