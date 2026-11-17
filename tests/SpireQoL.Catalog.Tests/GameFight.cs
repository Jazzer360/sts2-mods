using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Characters;
using MegaCrit.Sts2.Core.Runs;

namespace SpireQoL.Catalog.Tests;

/// <summary>
/// A fight built from the installed game's own objects, outside the game, for checking what the mod reads from a card in a
/// known state (PlayFacts: step 1 of every prediction). Uses the game's test factories (RunState.CreateForTest, the Deprived
/// test character) and its combat state (CombatState.AddPlayer, CreateCard, the piles' AddInternal); nothing is played.
/// </summary>
internal sealed class GameFight
{
    public RunState Run { get; }
    public CombatState Combat { get; }
    public Player Me { get; }

    public GameFight()
    {
        GameModels.EnsureLoaded();
        Me = Player.CreateForNewRun<Deprived>(MegaCrit.Sts2.Core.Unlocks.UnlockState.all, 1uL);
        Run = RunState.CreateForTest(new[] { Me }, seed: "TESTSEED");
        Combat = new CombatState(null, Run);
        Me.ResetCombatState();
        Combat.AddPlayer(Me);
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
