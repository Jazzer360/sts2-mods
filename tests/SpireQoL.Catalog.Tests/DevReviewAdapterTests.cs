using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Monsters;
using MegaCrit.Sts2.Core.Models.Powers;
using SpireQoL.Sim;
using Xunit;

namespace SpireQoL.Catalog.Tests;

/// <summary>
/// A dev review of the owner's Defect win against Aeonglass (decision #402): what the adapter reads of a card made, taken and replayed.
/// </summary>
[Collection(GameFightCollection.Name)]
public class DevReviewAdapterTests
{
    private static Creature Aeonglass(GameFight fight, int upgrades)
    {
        var creature = fight.Combat.CreateCreature(ModelDb.Monster<Aeonglass>().ToMutable(), CombatSide.Enemy, slot: null);
        fight.Combat.AddCreature(creature);
        var aeonglass = (Aeonglass)creature.Monster!;
        aeonglass.WitherUpgradeCount = upgrades;
        var presence = (WitheringPresencePower)ModelDb.Power<WitheringPresencePower>().ToMutable();
        presence.Target = fight.Me.Creature;
        presence.ApplyInternal(creature, amount: 6m);
        return creature;
    }

    [Fact]
    public void AWitherAeonglassMakesHasTheUpgradesItGave()
    {
        // Aeonglass.AfterCardGeneratedForCombat: a Wither made in the fight gets FakeUpgrade (3 more damage) for each WitherUpgradeCount. The Wither
        // Withering Presence puts in your hand hits for that much as your turn ends, not the canonical 3.
        Assert.True(SimAdapter.GenerationTouchesOnlyTheCard(typeof(Aeonglass)));
        var fight = new GameFight();
        fight.Add<StrikeDefect>();
        Aeonglass(fight, upgrades: 2);
        var state = SimAdapter.Build(fight.Me, fight.Combat, out _);
        var wither = state.You.MadeCards![CardId.Wither.Name];
        Assert.Equal(9, wither.TurnEnd!.Amount);
        Assert.Equal(3, ModelDb.Card<Wither>().DynamicVars.Damage.IntValue); // the canonical card is untouched
    }

    [Fact]
    public void CompactTakesTheWithersEndOfTurnDamageWithIt()
    {
        // Compact transforms every Status in your hand into Fuel: a Wither in hand doesn't hit you as your turn ends once it's Fuel.
        var fight = new GameFight();
        var compact = fight.Add<Compact>();
        fight.Add<Wither>();
        var state = SimAdapter.Build(fight.Me, fight.Combat, out var ids);
        Assert.Contains(state.You.TurnEndCards, card => card.Card == CardId.Wither.Name);
        var play = SimAdapter.Play(compact, fight.Me, fight.Combat, ids, targetId: Who.None);
        var take = Assert.Single(play.Body.OfType<TakeFromHandAction>());
        Assert.Contains(take.TurnEndTaken!, card => card.Card == CardId.Wither.Name);
        SimEngine.ApplyPlayTo(state, play, out var after, out _);
        Assert.All(after, branch => Assert.DoesNotContain(branch.You.TurnEndCards, card => card.Card == CardId.Wither.Name));
    }

    [Fact]
    public void SimpletonInHandHasYourHandReadForTheAutoPlayItGives()
    {
        // The owner's Fool run: Simpleton's play gives SimpletonPower, whose AfterAutoPostPlayPhaseEntered auto-plays your cheapest card as this turn
        // ends. With no Simpleton on you yet, the board reads your hand for it all the same, and the play lands the power the simulator acts on.
        var fight = new GameFight();
        var simpleton = fight.Add<Simpleton>();
        fight.Add<StrikeDefect>();
        var enemy = fight.Combat.CreateCreature(ModelDb.Monster<Seapunk>().ToMutable(), CombatSide.Enemy, slot: null);
        fight.Combat.AddCreature(enemy);
        var state = SimAdapter.Build(fight.Me, fight.Combat, out var ids);
        Assert.Equal(0, state.You.Simpleton);
        Assert.Contains(state.You.AutoHand!, card => card.Name == nameof(StrikeDefect));
        var play = SimAdapter.Play(simpleton, fight.Me, fight.Combat, ids, targetId: Who.None);
        Assert.Contains(play.Body.OfType<ApplyPowerAction>(), action => action.Power == PowerId.Simpleton && action.Amount == 1);
    }

    [Fact]
    public void ClawCarriesItsGrowthForAReplayTheSimulatorDecides()
    {
        // Echo Form's replay is decided as the play starts (CardPlayAction.CountByModels), so the play read now has one play: Claw's growth (its Increase
        // after each play) must be on it anyway, or the replay hits for the first play's damage (the game's 17 then 20 with Claw+).
        var fight = new GameFight();
        var claw = fight.Add<Claw>(upgraded: true);
        var enemy = fight.Combat.CreateCreature(ModelDb.Monster<Seapunk>().ToMutable(), CombatSide.Enemy, slot: null);
        fight.Combat.AddCreature(enemy);
        SimAdapter.Build(fight.Me, fight.Combat, out var ids);
        var play = SimAdapter.Play(claw, fight.Me, fight.Combat, ids, targetId: ids[enemy], plays: 1);
        var attack = Assert.Single(play.Body.OfType<AttackAction>());
        Assert.Equal(3m, attack.Damage.GrowsEachPlay);
    }
}
