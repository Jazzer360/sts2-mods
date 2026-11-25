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
    /// the game's code (BranchRules), the monster and power classes by name (the spawns' and the moves' powers), what each read model works out
    /// on its first ask, and the intents' code: each move's powers and block (MovePowers) and each conditional branch's test (MoveConditions).
    /// </summary>
    /// <returns>How many programs were read.</returns>
    internal static int ReadFightCode(IReadOnlyList<Type> modelTypes)
    {
        _ = BranchRules.MainRulesNow();
        foreach (var rule in BranchRules.All) _ = BranchRules.BetaBehaviour(rule);
        var programs = 0;
        var monsters = MonsterCode.Monsters(typeof(MonsterModel).Assembly.GetTypes()).ToList();
        // A move's program is asked by the class declaring its perform method: a monster's own, or a base class's (MonsterCode.Monsters has both).
        foreach (var monster in monsters)
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
            // What a read model works out on its first ask (whether it keeps its applier, its own fields: SimReaders' board read).
            if (SimModels.Get(typeof(PowerModel).IsAssignableFrom(type) ? SimPowerName(type) : type.Name) is ReadModel read)
            {
                _ = read.UsesApplier;
                _ = read.OwnFields;
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
        // The intents: each move's powers and block (Intent Details, the intents' corners), each conditional branch's test (Intent Forecast).
        programs += MovePowers.ReadAtLoad(monsters);
        programs += MoveConditions.ReadAtLoad(monsters);
        return programs;
    }
}
