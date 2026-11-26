using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Relics;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.ValueProps;

namespace SpireQoL.Catalog.Tests;

/// <summary>
/// Relics written for the tests, never in the game: each overrides a choice hook no relic of the game's does yet (decisions #371, #372), so the readings
/// of those hooks are held before a game update brings one. Read from their compiled code as the game's relics are (RuntimeCode.Read); never made.
/// </summary>
internal static class TestRelics
{
    /// <summary>Keeps curses out of your deck (ShouldAddToDeck false for a curse) and flashes when it does (AfterAddToDeckPrevented).</summary>
    internal sealed class CurseWard : RelicModel
    {
        public override RelicRarity Rarity => RelicRarity.Common;

        public override bool ShouldAddToDeck(CardModel card) => card.Type != CardType.Curse;

        public override Task AfterAddToDeckPrevented(CardModel card)
        {
            Flash();
            return Task.CompletedTask;
        }
    }

    /// <summary>Gives 5 gold for every card that leaves your deck (BeforeCardRemoved), flashing each time.</summary>
    internal sealed class RemovalRefund : RelicModel
    {
        public override RelicRarity Rarity => RelicRarity.Common;

        public override Task BeforeCardRemoved(CardModel card)
        {
            Flash();
            return PlayerCmd.GainGold(amount: 5m, Owner);
        }
    }

    /// <summary>Gives 5 gold when you smith at a rest site (AfterRestSiteSmith).</summary>
    internal sealed class SmithsToken : RelicModel
    {
        public override RelicRarity Rarity => RelicRarity.Common;

        public override Task AfterRestSiteSmith(Player player) => player == Owner ? PlayerCmd.GainGold(amount: 5m, player) : Task.CompletedTask;
    }

    /// <summary>Makes the HP you lose larger (ModifyHpLostAfterOsty: amount + 1): more of what's against you.</summary>
    internal sealed class HeavierFall : RelicModel
    {
        public override RelicRarity Rarity => RelicRarity.Common;

        public override decimal ModifyHpLostAfterOsty(Creature target, decimal amount, ValueProp props, Creature? dealer, CardModel? cardSource) =>
            target == Owner.Creature ? amount + 1 : amount;
    }

    /// <summary>Halves the gold you gain (ModifyGoldGained: amount / 2): less of what's for you.</summary>
    internal sealed class ThinPurse : RelicModel
    {
        public override RelicRarity Rarity => RelicRarity.Common;

        public override decimal ModifyGoldGained(Player player, decimal amount) => player == Owner ? amount / 2 : amount;
    }

    /// <summary>Takes away the HP you'd lose (ModifyHpLostAfterOsty: 0): what's against you taken away.</summary>
    internal sealed class FeatherFall : RelicModel
    {
        public override RelicRarity Rarity => RelicRarity.Common;

        public override decimal ModifyHpLostAfterOsty(Creature target, decimal amount, ValueProp props, Creature? dealer, CardModel? cardSource) =>
            target == Owner.Creature ? 0m : amount;
    }
}
