using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Characters;
using MegaCrit.Sts2.Core.Runs;

namespace SpireQoL.Catalog.Tests;

/// <summary>
/// A fight built from the installed game's own objects, outside the game, for checking what the mod reads from a card in a
/// known state (BoardCounts and the adapter: step 1 of every prediction). Uses the game's test factories (RunState.CreateForTest, the Deprived
/// test character) and its combat state (CombatState.AddPlayer, CreateCard, the piles' AddInternal); nothing is played.
/// </summary>
internal sealed class GameFight
{
    public RunState Run { get; }
    public CombatState Combat { get; }
    public Player Me { get; }

    /// <summary>The other players in the fight, after you (none by default).</summary>
    public IReadOnlyList<Player> Teammates { get; }

    public GameFight(int teammates = 0)
    {
        GameModels.EnsureLoaded();
        var players = Enumerable.Range(1, 1 + teammates)
            .Select(netId => Player.CreateForNewRun<Deprived>(MegaCrit.Sts2.Core.Unlocks.UnlockState.all, (ulong)netId)).ToArray();
        Me = players[0];
        Teammates = players.Skip(1).ToList();
        Run = RunState.CreateForTest(players, seed: "TESTSEED");
        Combat = new CombatState(null, Run);
        foreach (var player in players)
        {
            player.ResetCombatState();
            Combat.AddPlayer(player);
        }
    }

    /// <summary>
    /// The game's log of the fight (CombatManager.Instance.History's entries), shared by every fight: a test that adds to it clears it
    /// before and after (the game only logs during a live fight).
    /// </summary>
    public static List<MegaCrit.Sts2.Core.Combat.History.CombatHistoryEntry> Log => (List<MegaCrit.Sts2.Core.Combat.History.CombatHistoryEntry>)typeof(MegaCrit.Sts2.Core.Combat.History.CombatHistory)
        .GetField("_entries", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(CombatManager.Instance.History)!;

    /// <summary>A new card of this type, owned by <paramref name="owner"/>, put into one of their piles.</summary>
    public T AddFor<T>(Player owner, PileType pile) where T : CardModel
    {
        var card = Combat.CreateCard<T>(owner);
        pile.GetPile(owner).AddInternal(card);
        return card;
    }

    /// <summary>A new card of this type, owned by you, put into a pile (your hand by default).</summary>
    public T Add<T>(PileType pile = PileType.Hand, bool upgraded = false) where T : CardModel
    {
        var card = Combat.CreateCard<T>(Me);
        if (upgraded) { card.UpgradeInternal(); card.FinalizeUpgradeInternal(); }
        pile.GetPile(Me).AddInternal(card);
        return card;
    }
}

/// <summary>
/// Tests that build game fights (GameFight) share the game's static state (the run, its acts): they run one at a time, never
/// in parallel ("No default act for index 3!" when two set it up at once).
/// </summary>
[Xunit.CollectionDefinition(Name, DisableParallelization = true)]
public sealed class GameFightCollection
{
    public const string Name = "game fights";
}
