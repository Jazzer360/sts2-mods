using SpireQoL.Procs.Programs;
using SpireQoL.Sim;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Models.Cards;
using Xunit;

namespace SpireQoL.Catalog.Tests;

/// <summary>
/// What a card's CardCmd.AutoPlay plays, read from its code (decision #360: SimAdapter.CardAutoPlayOf): the pile, the way it takes the cards, its
/// filter, the fallback and whom each plays at, for every card that auto-plays cards of your piles; and a card you pick then played (Decisions,
/// Decisions) not read, so its play stays unknown (decision #188).
/// </summary>
[Collection(GameFightCollection.Name)]
public class CardAutoPlayReadTests
{
    private static SimAdapter.CardAutoPlayRead? Read<T>() where T : MegaCrit.Sts2.Core.Models.CardModel
    {
        GameModels.EnsureLoaded();
        var reads = SimAdapter.CardAutoPlaysOf(typeof(T));
        return reads.Count == 0 ? null : Assert.Single(reads).Read;
    }

    [Fact]
    public void EachReadsItsPileWayAndTarget()
    {
        Assert.Equal((SimPile.Draw, AutoPlayWay.Random, false), Facts(Read<Uproar>()));
        Assert.Equal((SimPile.Discard, AutoPlayWay.Shuffled, false), Facts(Read<BeatDown>())); // its two AutoPlays (aimed or not): one read
        Assert.Equal((SimPile.Hand, AutoPlayWay.Random, false), Facts(Read<TheFool>()));
        Assert.Equal((SimPile.Hand, AutoPlayWay.Each, false), Facts(Read<ChoChorus>()));
        Assert.Equal((SimPile.Exhaust, AutoPlayWay.Each, false), Facts(Read<Eidolon>()));
        Assert.Equal((SimPile.Exhaust, AutoPlayWay.Each, true), Facts(Read<KnifeTrap>()));
    }

    private static (SimPile, AutoPlayWay, bool) Facts(SimAdapter.CardAutoPlayRead? read)
    {
        Assert.NotNull(read);
        return (read.Pile, read.Way, read.AtTarget);
    }

    [Fact]
    public void UproarFallsBackOnAnyAttack()
    {
        var read = Read<Uproar>()!;
        Assert.NotNull(read.Filter);
        Assert.NotNull(read.Fallback);
        Assert.NotEqual(read.Filter!.ToString(), read.Fallback!.ToString());
    }

    [Fact]
    public void KnifeTrapUpgradesEachShivFirst()
    {
        GameModels.EnsureLoaded();
        var (_, _, command, upgrade) = Assert.Single(SimAdapter.CardAutoPlaysOf(typeof(KnifeTrap)));
        Assert.NotNull(upgrade);
        // Its upgrade is the auto-play's own (AutoPlayCardsAction.Upgraded), not an upgrade of the exhaust pile.
        Assert.Equal(SimAdapter.UpgradeTarget.AutoPlayed, SimAdapter.UpgradeTargetOf(upgrade!, CardPrograms.Of(typeof(KnifeTrap)))?.Target);
        Assert.Same(command, SimAdapter.UpgradedByAutoPlay(upgrade!, CardPrograms.Of(typeof(KnifeTrap))));
    }

    [Fact]
    public void APickThenPlayedIsntRead()
    {
        GameModels.EnsureLoaded();
        var program = CardPrograms.Of(typeof(DecisionsDecisions));
        var command = program.All().OfType<CommandStep>().Single(step => step.Command.FullName == "CardCmd.AutoPlay");
        Assert.Null(SimAdapter.CardAutoPlayOf(command, program));
        Assert.Contains("decision #188", SimAdapter.AutoPlayNotRead(command));
    }

    [Fact]
    public void UproarPlaysAnAttackOfYourDrawPile()
    {
        // The board: Uproar in hand, a Strike and two Defends to draw. Its play: its attack, then the Strike at the enemy (the only Attack there).
        GameModels.EnsureLoaded();
        var gameFight = new GameFight();
        gameFight.Me.PlayerCombatState!.Energy = 3;
        var enemy = gameFight.Combat.CreateCreature(MegaCrit.Sts2.Core.Models.ModelDb.Monster<MegaCrit.Sts2.Core.Models.Monsters.Axebot>().ToMutable(), MegaCrit.Sts2.Core.Combat.CombatSide.Enemy, slot: null);
        gameFight.Combat.AddCreature(enemy);
        enemy.SetMaxHpInternal(100);
        enemy.SetCurrentHpInternal(100);
        var uproar = gameFight.Add<Uproar>();
        gameFight.Add<StrikeIronclad>(PileType.Draw);
        gameFight.Add<DefendIronclad>(PileType.Draw);
        gameFight.Add<DefendIronclad>(PileType.Draw);
        var start = SimAdapter.Build(gameFight.Me, gameFight.Combat, out var ids);
        var play = SimAdapter.Play(uproar, gameFight.Me, gameFight.Combat, ids, ids[enemy]);
        SimEngine.ApplyPlayTo(start, play, out var branches, out _);
        var branch = Assert.Single(branches);
        Assert.Null(branch.Unmodelled);
        Assert.Equal(100 - 12 - 6, branch.Enemy(ids[enemy])!.Hp);
        Assert.Equal(2, branch.You.Draw!.Count);
        // Uproar's own 2 spent; the Strike it plays costs nothing (CardCmd.AutoPlay spends no resources: decision #361).
        Assert.Equal(1, branch.You.Energy);
    }
}
