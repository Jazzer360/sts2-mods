using Xunit;

namespace SpireQoL.Sim.Tests;

/// <summary>
/// Damage to a player from a card's HP cost and from a power's or relic's hook is one game command (CreatureCmd.Damage), so one path in the
/// simulator (SimEngine.DamagePlayer): Hook.ModifyDamage with its caps, AfterModifyingDamageAmount for the sources that changed the amount,
/// block unless it's Unblockable, the HP-loss pipeline and the hit's reactions, each told the command's own dealer, props and card (its
/// cardSource: the card for an HP cost, none for a hook's).
/// </summary>
public class OwnerDamageTests
{
    private static SimState Board()
    {
        var state = new SimState { Enemies = { new SimEnemy { Id = 0, Name = "E0", Hp = 100 } } };
        state.You.Hp = 50;
        state.You.MaxHp = 70;
        state.You.HandCount = 3;
        return state;
    }

    /// <summary>A card play whose body is the HP it costs (CreatureCmd.Damage of its owner: Bloodletting's 3).</summary>
    private static SimPlay HpCost(string card, int amount, SimAffliction affliction = SimAffliction.None) => new()
    {
        Name = card, HandCard = 7, HandDelta = -1,
        Card = new SimCardPlay(new SimCard(card, SimCardType.Skill) { Affliction = affliction }, Who.You, Who.None, PlayIndex: 0, PlayCount: 1, AutoPlay: false),
        Body = { new LoseHpAction(amount) },
    };

    [Fact]
    public void AnHpCostGoesThroughTheDamageMathsCaps()
    {
        // CreatureCmd.Damage → Hook.ModifyDamage: Hard to Kill's ModifyDamageCap holds damage to its owner to its amount, an HP cost included.
        var state = Board();
        state.You.Powers = PowerSet.Empty.With(PowerId.HardToKill, amount: 1);
        var branch = Assert.Single(SimEngine.Run(state, HpCost(card: "Bloodletting", amount: 3)).Branches);
        Assert.Equal(1, branch.You.Taken);
    }

    [Fact]
    public void AnHpCostTellsTheSourcesThatChangedItsAmount()
    {
        // CreatureCmd.Damage: Hook.AfterModifyingDamageAmount for Intangible's cap (IntangiblePower.AfterModifyingDamageAmount flashes); its HP-loss
        // pass then finds 1 and changes nothing (Hook.ModifyHpLost keeps only a source that changed the amount): one flash, 1 HP lost.
        var state = Board();
        state.You.Powers = PowerSet.Empty.With(PowerId.Intangible, amount: 1);
        var branch = Assert.Single(SimEngine.Run(state, HpCost(card: "Bloodletting", amount: 3)).Branches);
        Assert.Equal(1, branch.You.Taken);
        Assert.Equal(1, branch.Fired[PowerId.Intangible.Name]);
    }

    [Fact]
    public void AHooksDamageToItsOwnerTellsTheSourcesThatChangedItsAmount()
    {
        // GalvanicPower.AfterCardPlayed: CreatureCmd.Damage of the card's owner, the same command: Intangible's cap is told too.
        var state = Board();
        state.You.Powers = PowerSet.Empty.With(PowerId.Intangible, amount: 1);
        state.Enemies[0].Powers = PowerSet.Empty.With(PowerId.Galvanic, amount: 4);
        var play = HpCost(card: "Inflame", amount: 0, SimAffliction.Galvanized);
        play.Body.Clear();
        var branch = Assert.Single(SimEngine.Run(state, play).Branches);
        Assert.Equal(1, branch.You.Taken);
        Assert.Equal(1, branch.Fired[PowerId.Intangible.Name]);
    }

    [Fact]
    public void AHooksDamageIsntTheCardInPlays()
    {
        // BufferPower.ModifyHpLostAfterOstyLate lets Brightest Flame's own HP cost through (cardSource is BrightestFlame) and stops the rest:
        // Galvanic's damage passes no card (CreatureCmd.Damage(..., null, null)), so it's stopped even while Brightest Flame is being played.
        var state = Board();
        state.You.Powers = PowerSet.Empty.With(PowerId.Buffer, amount: 2);
        state.Enemies[0].Powers = PowerSet.Empty.With(PowerId.Galvanic, amount: 4);
        var branch = Assert.Single(SimEngine.Run(state, HpCost(card: CardId.BrightestFlame.Name, amount: 2, SimAffliction.Galvanized)).Branches);
        Assert.Equal(2, branch.You.Taken);
        Assert.Equal(1, branch.You.Powers[PowerId.Buffer]);
    }

    /// <summary>Brightest Flame's play: CreatureCmd.LoseMaxHp(ctx, Owner.Creature, MaxHp, this) (decision #341).</summary>
    private static SimPlay MaxHpLoss(int hp, int maxHp, int amount, out SimState state)
    {
        state = Board();
        state.You.Hp = hp;
        state.You.MaxHp = maxHp;
        var play = HpCost(card: CardId.BrightestFlame.Name, amount: 0);
        play.Body.Clear();
        play.Body.Add(new LoseMaxHpAction(amount));
        return play;
    }

    [Fact]
    public void MaxHpLostFromACardTakesTheHpAboveTheNewMaxAsTheCardsDamage()
    {
        // At full HP: the 2 above the new max is CreatureCmd.Damage with the card as its source (dealt by you, Unblockable, Unpowered, Move), through
        // the damage math (Hard to Kill holds it to 1), then the max goes down to 68 and HP above it comes down to it.
        var play = MaxHpLoss(hp: 70, maxHp: 70, amount: 2, out var state);
        var branch = Assert.Single(SimEngine.Run(state, play).Branches);
        Assert.Equal(68, branch.You.MaxHp);
        Assert.Equal(68, branch.You.Hp);
        Assert.Equal(2, branch.You.Taken);

        var capped = MaxHpLoss(hp: 70, maxHp: 70, amount: 2, out var hardToKill);
        hardToKill.You.Powers = PowerSet.Empty.With(PowerId.HardToKill, amount: 1);
        var cappedBranch = Assert.Single(SimEngine.Run(hardToKill, capped).Branches);
        Assert.Equal(1, cappedBranch.You.Taken);
        Assert.Equal(68, cappedBranch.You.MaxHp);
        Assert.Equal(68, cappedBranch.You.Hp); // SetMaxHp brings the HP the cap left above the new max down to it
    }

    [Fact]
    public void MaxHpLostFromACardBelowItsMaxTakesNoHp()
    {
        // HP 50 of 70: the new max 68 is above it, so no damage; at least 1 max is left.
        var play = MaxHpLoss(hp: 50, maxHp: 70, amount: 2, out var state);
        var branch = Assert.Single(SimEngine.Run(state, play).Branches);
        Assert.Equal(68, branch.You.MaxHp);
        Assert.Equal(50, branch.You.Hp);
        Assert.Equal(0, branch.You.Taken);
        var floored = MaxHpLoss(hp: 1, maxHp: 2, amount: 5, out var low);
        Assert.Equal(1, Assert.Single(SimEngine.Run(low, floored).Branches).You.MaxHp);
    }
}
