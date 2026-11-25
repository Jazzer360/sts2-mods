using MegaCrit.Sts2.Core.Models;
using SpireQoL.Procs.Programs;

namespace SpireQoL.Sim;

/// <summary>
/// The game's code the adapter reads on a fight's first board, read once a launch on a worker as the mod loads (decision #254, as the cards'
/// programs are: decision #237), so the first frame of the first fight doesn't read it on the game's thread. Only code is read here, never a
/// game object: what's read is what the board read asks the same caches for, so a class a fight meets before this is done is read then.
/// </summary>
public static partial class SimAdapter
{
    /// <summary>
    /// Reads every monster's moves (MoveOf) and AfterAddedToRoom (StartingPowers), every power's AfterDeath (SpawnsOf), the questions every
    /// card, power and relic asks of the fight's log (HistoryLedger.QueriesFor), every card's energy commands (CardEnergy), the rules told from
    /// the game's code (BranchRules), and the monster and power classes by name (the spawns' and the moves' powers).
    /// </summary>
    /// <returns>How many programs were read.</returns>
    internal static int ReadFightCode(IReadOnlyList<Type> modelTypes)
    {
        _ = BranchRules.MainRulesNow();
        foreach (var rule in BranchRules.All) _ = BranchRules.BetaBehaviour(rule);
        var programs = 0;
        var gameTypes = typeof(MonsterModel).Assembly.GetTypes();
        // A move's program is asked by the class declaring its perform method: a monster's own, or a base class's (MonsterCode.Monsters has both).
        foreach (var monster in MonsterCode.Monsters(gameTypes))
        {
            foreach (var perform in MonsterCode.Moves(monster))
            {
                _ = ModelPrograms.Of(monster, perform.Name);
                programs++;
            }
            if (monster.IsAbstract) continue;
            _ = ModelPrograms.Of(monster, "AfterAddedToRoom");
            _ = MonsterType(monster.Name);
            programs++;
        }
        foreach (var type in modelTypes)
        {
            if (typeof(PowerModel).IsAssignableFrom(type))
            {
                _ = ModelPrograms.Of(type, "AfterDeath");
                _ = PowerType(type.Name);
                programs++;
            }
            if (typeof(CardModel).IsAssignableFrom(type))
            {
                // The energy a card gives as it's exhausted and loses as it's drawn (the profile of each card in your piles).
                _ = CardEnergy.CallsOf(type);
                _ = CardEnergy.CallsOf(type, "LoseEnergy");
                programs++;
            }
            if (typeof(CardModel).IsAssignableFrom(type) || typeof(PowerModel).IsAssignableFrom(type) || typeof(RelicModel).IsAssignableFrom(type))
            {
                _ = HistoryLedger.QueriesOf(type);
                programs++;
            }
        }
        return programs;
    }
}
