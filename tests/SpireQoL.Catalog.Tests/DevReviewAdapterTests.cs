using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Cards;
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
    public void AshwaterLeavesHowManyItExhaustsToYou()
    {
        // The owner's Ironclad run: Ashwater exhausts the cards you pick from your hand, 0 to any (CardSelectorPrefs(prompt, 0, 999999999)). How many
        // is your later decision (decision #406): with cards in your hand, its outcome is unknown, never your whole hand exhausted.
        GameModels.EnsureLoaded();
        var fight = new GameFight();
        fight.Add<DefendIronclad>();
        fight.Add<StrikeIronclad>();
        var potion = ModelDb.Potion<MegaCrit.Sts2.Core.Models.Potions.Ashwater>().ToMutable();
        fight.Me.AddPotionInternal(potion);
        var start = SimAdapter.Build(fight.Me, fight.Combat, out var ids);
        var use = SimAdapter.Potion(potion, fight.Me, fight.Combat, ids, Who.You);
        Assert.Contains(use.Body.OfType<TakeFromHandAction>(), take => take.ChoosesHowMany);
        SimEngine.ApplyPlayTo(start, use, out var branches, out _);
        Assert.All(branches, branch => Assert.NotNull(branch.Unmodelled));
    }

    [Fact]
    public void PurityLeavesHowManyItExhaustsToYou()
    {
        // Purity: exhaust up to 3 cards you pick (0 to 3): the same later decision.
        var fight = new GameFight();
        var purity = fight.Add<Purity>();
        fight.Add<DefendIronclad>();
        SimAdapter.Build(fight.Me, fight.Combat, out var ids);
        var play = SimAdapter.Play(purity, fight.Me, fight.Combat, ids, targetId: Who.None);
        Assert.Contains(play.Body.OfType<TakeFromHandAction>(), take => take.ChoosesHowMany);
    }

    [Fact]
    public void ColorlessPotionsPickIsOneYouMaySkip()
    {
        // The owner's Silent run: Colorless Potion offers three cards (CardSelectCmd.FromChooseACardScreen, canSkip: true) and adds the one you pick only
        // `if (cardModel != null)`; skipped, your hand held 6 where 7 were predicted. The card it makes is one you may skip (decision #408); a card made
        // without a pick (Blade Dance's Shivs) isn't.
        GameModels.EnsureLoaded();
        var fight = new GameFight();
        fight.Add<DefendSilent>();
        var discovery = fight.Add<Discovery>();
        var bladeDance = fight.Add<BladeDance>();
        var potion = ModelDb.Potion<MegaCrit.Sts2.Core.Models.Potions.ColorlessPotion>().ToMutable();
        fight.Me.AddPotionInternal(potion);
        SimAdapter.Build(fight.Me, fight.Combat, out var ids);
        var use = SimAdapter.Potion(potion, fight.Me, fight.Combat, ids, Who.You);
        Assert.Contains(use.Body.OfType<CardEventAction>(), made => made.Event == CardEvent.Generated && made.YouMaySkip);
        var play = SimAdapter.Play(discovery, fight.Me, fight.Combat, ids, targetId: Who.None);
        Assert.Contains(play.Body.OfType<CardEventAction>(), made => made.Event == CardEvent.Generated && made.YouMaySkip);
        var shivs = SimAdapter.Play(bladeDance, fight.Me, fight.Combat, ids, targetId: Who.None);
        Assert.DoesNotContain(shivs.Body.OfType<CardEventAction>(), made => made.YouMaySkip);
    }

    [Fact]
    public void HowlFromBeyondPlaysFromYourExhaustPile()
    {
        // True Grit+ exhausting Howl from Beyond: the play audit's pick check (decision #167) counts a card that plays from your exhaust pile as one the
        // simulation follows, as the board read does (SimAdapter.FillAutoPlays).
        GameModels.EnsureLoaded();
        Assert.True(SimAdapter.PlaysFromExhaustPile(ModelDb.Card<HowlFromBeyond>()));
        Assert.False(SimAdapter.PlaysFromExhaustPile(ModelDb.Card<TrueGrit>()));
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

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void APickOntoYourDrawPilesTopIsYoursWhenACardThereActsAsTheTurnEnds(bool invincibleInDiscard)
    {
        // The owner's Regent run: Cosmic Indifference put the I Am Invincible you picked from your discard pile on top of your draw pile, where it plays
        // itself as your turn ends (10 block); the preview had the pick change nothing. Which card goes there is your later decision: with a card in the
        // pile it picks from that acts from the top, unknown. Without one, the pick changes nothing the simulation follows.
        var fight = new GameFight();
        var cosmic = fight.Add<CosmicIndifference>();
        fight.Add<StrikeRegent>(PileType.Discard);
        if (invincibleInDiscard) fight.Add<IAmInvincible>(PileType.Discard);
        SimAdapter.Build(fight.Me, fight.Combat, out var ids);
        var play = SimAdapter.Play(cosmic, fight.Me, fight.Combat, ids, targetId: Who.None);
        Assert.Equal(invincibleInDiscard, play.Body.OfType<NotModelledAction>().Any());
    }

    [Fact]
    public void PhotonCutsPickFromYourHandOntoYourDrawPileIsYoursWithIAmInvincibleInHand()
    {
        // The same run: Photon Cut put the I Am Invincible you picked from your hand on top of your draw pile.
        var fight = new GameFight();
        var photon = fight.Add<PhotonCut>();
        fight.Add<IAmInvincible>();
        var enemy = fight.Combat.CreateCreature(ModelDb.Monster<Seapunk>().ToMutable(), CombatSide.Enemy, slot: null);
        fight.Combat.AddCreature(enemy);
        SimAdapter.Build(fight.Me, fight.Combat, out var ids);
        var play = SimAdapter.Play(photon, fight.Me, fight.Combat, ids, targetId: ids[enemy]);
        Assert.Contains(play.Body, action => action is NotModelledAction);
    }

    [Fact]
    public void WahKisPickFromYourDiscardPileGoesOnTopOfYourDrawPile()
    {
        // The owner's Fool run: Wah Ki with an empty draw pile, then Motif drew the card it put on top; the Shuffle tab said that draw would shuffle.
        var fight = new GameFight();
        var wahKi = fight.Add<WahKi>(upgraded: true);
        fight.Add<StrikeIronclad>(PileType.Discard);
        fight.Add<DefendIronclad>(PileType.Discard);
        var enemy = fight.Combat.CreateCreature(ModelDb.Monster<Seapunk>().ToMutable(), CombatSide.Enemy, slot: null);
        fight.Combat.AddCreature(enemy);
        var start = SimAdapter.Build(fight.Me, fight.Combat, out var ids);
        var play = SimAdapter.Play(wahKi, fight.Me, fight.Combat, ids, targetId: ids[enemy]);
        Assert.Contains(play.Body, action => action is PickDiscardOntoDrawTopAction);
        SimEngine.ApplyPlayTo(start, play, out var branches, out _);
        Assert.All(branches, branch => Assert.Equal(1, branch.You.Draw!.Count));
        Assert.All(branches, branch => Assert.Null(branch.Unmodelled));
    }
}
