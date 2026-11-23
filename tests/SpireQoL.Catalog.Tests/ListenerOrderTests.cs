using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Monsters;
using MegaCrit.Sts2.Core.Models.Potions;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.Models.Relics;
using SpireQoL.Sim;
using Xunit;

namespace SpireQoL.Catalog.Tests;

/// <summary>
/// The simulator calls its models in the game's listener order (docs/SIMULATOR.md "Listener order"): a fight built from the game's own
/// objects (GameFight), with powers, relics, a potion and cards in several piles on you and powers on an enemy, listed by the game
/// (CombatState.IterateHookListeners) and by the simulator from the adapter's read of the same fight (SimEngine.ListenerOrder). The
/// game's list, kept to what the simulator models, is the simulator's: the same kinds, names and owners in the same order.
/// </summary>
[Collection(GameFightCollection.Name)]
public class ListenerOrderTests
{
    private static GameFight Fight(out Creature enemy)
    {
        var gameFight = new GameFight();
        enemy = gameFight.Combat.CreateCreature(ModelDb.Monster<Axebot>().ToMutable(), CombatSide.Enemy, slot: null);
        gameFight.Combat.AddCreature(enemy);
        Power<EnragePower>(enemy, amount: 2);
        Power<ArtifactPower>(enemy, amount: 1);
        var me = gameFight.Me.Creature;
        Power<DarkEmbracePower>(me, amount: 1);
        Power<VulnerablePower>(me, amount: 1);
        Power<IterationPower>(me, amount: 1);
        foreach (var relic in new RelicModel[] { ModelDb.Relic<PenNib>().ToMutable(), ModelDb.Relic<Anchor>().ToMutable(), ModelDb.Relic<Kunai>().ToMutable() })
            gameFight.Me.AddRelicInternal(relic);
        gameFight.Me.AddPotionInternal(ModelDb.Potion<FairyInABottle>().ToMutable());
        gameFight.Add<StrikeIronclad>();
        gameFight.Add<Regret>();
        gameFight.Add<DefendIronclad>(PileType.Draw);
        gameFight.Add<MegaCrit.Sts2.Core.Models.Cards.Void>(PileType.Draw);
        gameFight.Add<MegaCrit.Sts2.Core.Models.Cards.Void>(PileType.Discard);
        return gameFight;
    }

    private static void Power<T>(Creature creature, int amount) where T : PowerModel => ((T)ModelDb.Power<T>().ToMutable()).ApplyInternal(creature, amount);

    /// <summary>Each of the game's listeners as the simulator names it: its kind, its model's name (null: not modelled), its owner.</summary>
    private static List<(ListenerKind Kind, string? Name, Creature Owner)> GameListeners(GameFight gameFight)
    {
        var listeners = new List<(ListenerKind, string?, Creature)>();
        foreach (var model in gameFight.Combat.IterateHookListeners())
        {
            (ListenerKind Kind, string? Name, Creature Owner) listener = model switch
            {
                PowerModel power => (ListenerKind.Power, Modelled(power), power.Owner),
                RelicModel relic => (ListenerKind.Relic, Modelled(relic), relic.Owner.Creature),
                PotionModel potion => (ListenerKind.Potion, Modelled(potion), potion.Owner.Creature),
                OrbModel orb => (ListenerKind.Orb, Modelled(orb), orb.Owner.Creature),
                CardModel card => (ListenerKind.Card, SimReaders.CardListenerOf(card)?.Name, card.Owner.Creature),
                MonsterModel monster => (ListenerKind.Monster, Modelled(monster), monster.Creature),
                // A card's affliction and enchantment come right after it: its owner's.
                _ => (ListenerKind.Card, null, listeners[^1].Item3),
            };
            listeners.Add(listener);
        }
        return listeners;
    }

    /// <summary>The model a power, relic, potion, orb or monster has (a card's model is never one of these), or null.</summary>
    private static string? Modelled(AbstractModel model) =>
        SimModels.Get(SimReaders.ModelName(model)) is { } simModel and not CardListener ? simModel.Name : null;

    [Fact]
    public void TheSimulatorsListenersAreTheGamesInTheGamesOrder()
    {
        var gameFight = Fight(out _);
        var start = SimAdapter.Build(gameFight.Me, gameFight.Combat, out var ids);
        int IdOf(Creature creature) => creature == gameFight.Me.Creature ? Who.You : ids[creature];
        // One model per card class: each copy's hook answers only its own card's events, so the class listens once, at its first copy.
        var game = GameListeners(gameFight).Where(listener => listener.Name != null)
            .Select(listener => (listener.Kind, listener.Name!, IdOf(listener.Owner))).Distinct().ToList();
        Assert.Equal(game, SimEngine.ListenerOrder(start));
        // The fight has every kind the simulator models, on you and on the enemy, and both players' card copies.
        Assert.Contains((ListenerKind.Power, nameof(IterationPower), Who.You), game);
        Assert.Contains((ListenerKind.Relic, nameof(PenNib), Who.You), game);
        Assert.Contains((ListenerKind.Card, nameof(Regret), Who.You), game);
        Assert.Contains((ListenerKind.Card, CardId.Void.Name, Who.You), game);
        Assert.Contains(game, listener => listener.Kind == ListenerKind.Power && listener.Item3 >= 0);
        Assert.Equal(2, start.You.Cards[CardId.Void]);
    }

    [Fact]
    public void EachCreaturesListenersComeInTheOrderOfTheirKinds()
    {
        // ListenerKind is the game's order within a creature: powers, then relics, potions, orbs and cards (a player), or the monster.
        var gameFight = Fight(out var enemy);
        var listeners = GameListeners(gameFight);
        foreach (var creature in new[] { gameFight.Me.Creature, enemy })
        {
            var kinds = listeners.Where(listener => listener.Owner == creature).Select(listener => listener.Kind).ToList();
            Assert.Equal(kinds.Order(), kinds);
            Assert.True(kinds.Distinct().Count() >= 2, $"{(creature == enemy ? "the enemy" : "you")}: {string.Join(", ", kinds)}");
        }
        Assert.Equal(new[] { ListenerKind.Power, ListenerKind.Relic, ListenerKind.Potion, ListenerKind.Card },
            listeners.Where(listener => listener.Owner == gameFight.Me.Creature).Select(listener => listener.Kind).Distinct());
        Assert.Equal(ListenerKind.Monster, listeners.Last(listener => listener.Owner == enemy).Kind);
    }
}
