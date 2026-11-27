using System.Reflection;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Monsters;
using SpireQoL.Sim;
using Xunit;

namespace SpireQoL.Catalog.Tests;

/// <summary>
/// Decision #412, from the owner's Fool run: Mourning Blow's GetResultLocationForCardPlay sends it back to your hand where the base's location would be
/// the discard pile (and it turns into Sadness there); the play audit had your hand at 3 where the game's held 4. A card's own result location is read
/// from its code: your hand (Mourning Blow, Particle Wall), the top of your draw pile (Shining Strike), a random teammate's draw pile (The Ball: none of
/// yours with one; as the base's alone).
/// </summary>
[Collection(GameFightCollection.Name)]
public class OwnResultLocationTests
{
    private static Creature Enemy(GameFight fight)
    {
        var enemy = fight.Combat.CreateCreature(ModelDb.Monster<Axebot>().ToMutable(), CombatSide.Enemy, slot: null);
        fight.Combat.AddCreature(enemy);
        return enemy;
    }

    [Fact]
    public void MourningBlowGoesBackToYourHandAsSadness()
    {
        var fight = new GameFight();
        var enemy = Enemy(fight);
        var blow = fight.Add<MourningBlow>();
        fight.Add<StrikeIronclad>();
        fight.Add<DefendIronclad>();
        var start = SimAdapter.Build(fight.Me, fight.Combat, out var ids);
        var play = SimAdapter.Play(blow, fight.Me, fight.Combat, ids, ids[enemy]);
        SimEngine.ApplyPlayTo(start, play, out var branches, out _);
        Assert.All(branches, branch => Assert.Equal(3, branch.You.HandCount));
        Assert.All(branches, branch => Assert.Null(branch.Unmodelled));
    }

    [Fact]
    public void ShiningStrikeGoesOntoYourDrawPilesTop()
    {
        var fight = new GameFight();
        var enemy = Enemy(fight);
        var strike = fight.Add<ShiningStrike>();
        SimAdapter.Build(fight.Me, fight.Combat, out var ids);
        var play = SimAdapter.Play(strike, fight.Me, fight.Combat, ids, ids[enemy]);
        Assert.Equal(SimResultLocation.DrawTop, play.OwnResult![SimResultLocation.Default]);
        Assert.Equal(SimResultLocation.Exhaust, play.OwnResult[SimResultLocation.Exhaust]);
    }

    [Fact]
    public void TheBallAloneGoesWhereTheBaseSendsIt()
    {
        var fight = new GameFight();
        var ball = fight.Add<TheBall>();
        Enemy(fight);
        var (map, unknown) = SimAdapter.OwnResultLocation(ball, fight.Me, fight.Combat);
        Assert.Equal(SimResultLocation.Default, map![SimResultLocation.Default]);
        Assert.Null(unknown);
    }

    [Fact]
    public void EveryCardThatSendsItselfSomewhereOfItsOwnIsRead()
    {
        // Every card class overriding GetResultLocationForCardPlay, in a fight alone: each base location read, or a reason it isn't.
        GameModels.EnsureLoaded();
        var unread = new List<string>();
        var fight = new GameFight();
        foreach (var canonical in ModelDb.AllCards)
        {
            var method = canonical.GetType().GetMethod("GetResultLocationForCardPlay", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            if (method == null || method.DeclaringType == typeof(CardModel)) continue;
            var (map, unknown) = SimAdapter.OwnResultLocation(canonical, fight.Me, fight.Combat);
            if (map is not { Count: 3 } || unknown != null) unread.Add($"{canonical.GetType().Name}: {unknown ?? "not every base location read"}");
        }
        Assert.True(unread.Count == 0, "Cards whose own result location isn't read:\n" + string.Join("\n", unread));
    }
}
