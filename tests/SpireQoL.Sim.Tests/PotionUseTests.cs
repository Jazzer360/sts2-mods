using Xunit;

namespace SpireQoL.Sim.Tests;

/// <summary>
/// A potion's use (decision #313, PotionModel.OnUseWrapper): it leaves its owner's belt (RemoveBeforeUse), Hook.BeforePotionUsed, its OnUse (its
/// commands as the adapter reads them from its code: SimAdapter.Potion), the game's log of it, then Hook.AfterPotionUsed. No card is played, so
/// nothing that answers a card play runs. Its damage and block are its own values through the game's math with the props its code gives
/// (Unpowered for every damage and block potion: Strength, Vulnerable and Dexterity don't change them).
/// </summary>
public class PotionUseTests
{
    private static SimEnemy Enemy(int id, int hp, int hit) => new() { Id = id, Name = $"E{id}", Hp = hp, Hits = new() { hit }, Intent = new() { new(Base: hit) }, IntendsToAttack = true };

    private static SimState Fight(int hp = 30, int hit = 10)
    {
        var state = new SimState { Enemies = { Enemy(id: 0, hp: hp, hit: hit) } };
        state.You.Hp = 50;
        state.You.MaxHp = 50;
        state.You.Potions = 2;
        return state;
    }

    /// <summary>A potion aimed at <paramref name="target"/> doing <paramref name="body"/>.</summary>
    private static SimPlay Potion(string name, int target, params SimAction[] body)
    {
        var play = new SimPlay { Name = name, TargetId = target, Potion = new SimPotionUse(name, Who.You, target) };
        play.Body.AddRange(body);
        return play;
    }

    /// <summary>Fire Potion: CreatureCmd.Damage(target, Damage.BaseValue, Damage.Props: Unpowered, Owner.Creature).</summary>
    private static SimPlay FirePotion(int target, int damage = 20) =>
        Potion("FirePotion", target, new DamageAction(new DamageSpec(damage, SimProps.Unpowered)) { TargetId = target });

    [Fact]
    public void ADamagePotionHitsItsTargetUnpowered()
    {
        var state = Fight();
        state.You.Powers = state.You.Powers.With(PowerId.Strength, amount: 5); // Strength doesn't add to unpowered damage
        SimEngine.ApplyPlayTo(state, FirePotion(target: 0), out var after, out _);
        Assert.Equal(10, Assert.Single(after).Enemy(id: 0)!.Hp);
    }

    [Fact]
    public void ADamagePotionThatKillsLeavesNoAttack()
    {
        var outcome = SimEngine.Run(Fight(hp: 15), FirePotion(target: 0));
        Assert.False(Assert.Single(outcome.Branches).Enemy(id: 0)!.Alive);
        Assert.Equal(0, outcome.TakenMax);
    }

    [Fact]
    public void ADamagePotionOnEveryEnemyHitsEachInTurn()
    {
        // Explosive Ampoule: CreatureCmd.Damage(HittableEnemies, ...): each enemy alive as it starts.
        var state = Fight();
        state.Enemies.Add(Enemy(id: 1, hp: 8, hit: 6));
        var play = Potion("ExplosiveAmpoule", Who.None, new DamageAction(new DamageSpec(10, SimProps.Unpowered)) { EveryEnemy = true });
        var outcome = SimEngine.Run(state, play);
        var branch = Assert.Single(outcome.Branches);
        Assert.Equal(20, branch.Enemy(id: 0)!.Hp);
        Assert.False(branch.Enemy(id: 1)!.Alive);
        Assert.Equal(10, outcome.TakenMax);
    }

    [Fact]
    public void ADamagePotionOnEveryCreatureHitsThePlayersThenTheEnemies()
    {
        // Foul Potion (decision #340): CreatureCmd.Damage(CombatState.Creatures.Where(c => !c.IsPet), 12, Unpowered, Owner.Creature): the fight's
        // allies (you, then your teammate), then its enemies, each in turn; block takes it (it isn't Unblockable), and a dead teammate is skipped.
        var state = Fight();
        state.You.Block = 5;
        state.Teammates.Add(new SimPlayer { Hp = 40, MaxHp = 40 });
        state.Teammates.Add(new SimPlayer { Hp = 0, MaxHp = 40 });
        var play = Potion("FoulPotion", Who.None, new DamageAction(new DamageSpec(12, SimProps.Unpowered))
            { EveryEnemy = true, Players = new[] { Who.You, Who.Teammate(teammateIndex: 0), Who.Teammate(teammateIndex: 1) }, Source = "FoulPotion" });
        SimEngine.ApplyPlayTo(state, play, out var after, out _);
        var branch = Assert.Single(after);
        Assert.Equal(43, branch.You.Hp);
        Assert.Equal(0, branch.You.Block);
        Assert.Equal(28, branch.Teammates[0].Hp);
        Assert.Equal(0, branch.Teammates[1].Hp);
        Assert.Equal(18, branch.Enemy(id: 0)!.Hp);
    }

    [Fact]
    public void ABlockPotionIsntRaisedByDexterity()
    {
        // Block Potion: CreatureCmd.GainBlock(target, BlockVar(12, Unpowered)) on you.
        var state = Fight(hit: 15);
        state.You.Powers = state.You.Powers.With(PowerId.Dexterity, amount: 3);
        var outcome = SimEngine.Run(state, Potion("BlockPotion", Who.You, new GainBlockAction(Times: 1, Base: 12, SimProps.Unpowered) { To = Who.You }));
        Assert.Equal(3, outcome.TakenMax);
    }

    [Fact]
    public void AStrengthPotionGivesYouStrength()
    {
        var state = Fight();
        SimEngine.ApplyPlayTo(state, Potion("StrengthPotion", Who.You, new ApplyPowerAction(PowerId.Strength, Amount: 2) { To = ApplyPowerAction.Where.Creature, Creature = Who.You }), out var after, out _);
        Assert.Equal(2, Assert.Single(after).You.Powers[PowerId.Strength]);
    }

    [Fact]
    public void AWeakPotionWeakensItsTargetsAttack()
    {
        var weak = Potion("WeakPotion", target: 0, new ApplyPowerAction(PowerId.Weak, Amount: 3) { To = ApplyPowerAction.Where.Creature, Creature = 0, Debuff = true });
        Assert.Equal(7, SimEngine.Run(Fight(), weak).TakenMax); // 10 × 0.75, rounded down
    }

    [Fact]
    public void ADrawPotionDrawsFromYourPile()
    {
        var state = Fight();
        state.You.HandCount = 5;
        state.You.Draw = new SimDrawPile { Rest = new List<SimDrawGroup> { new(new SimDrawCard("plain"), 10) } };
        SimEngine.ApplyPlayTo(state, Potion("SwiftPotion", Who.You, new CardEventAction(CardEvent.Drawn, Count: 3) { JoinsHand = true, Indirect = true }), out var after, out _);
        // No card left your hand: the potion's draws are all the hand gains.
        Assert.Equal(8, Assert.Single(after).You.HandCount);
    }

    [Fact]
    public void ReptileTrinketGivesStrengthAfterYourPotion()
    {
        // ReptileTrinket.AfterPotionUsed, read from its code: a potion of its owner's gives them its Strength (this turn).
        var state = Fight().WithRelic(Who.You, RelicId.ReptileTrinket, key: "StrengthPower", value: 3);
        SimEngine.ApplyPlayTo(state, FirePotion(target: 0), out var after, out _);
        Assert.Equal(3, Assert.Single(after).You.Powers[PowerId.Strength]);
    }

    [Fact]
    public void ReptileTrinketDoesntAnswerATeammatesPotion()
    {
        var state = Fight().WithRelic(Who.You, RelicId.ReptileTrinket, key: "StrengthPower", value: 3);
        state.Teammates.Add(new SimPlayer { Hp = 40, MaxHp = 40, Potions = 1 });
        var thrown = new SimPlay { Name = "FirePotion", TargetId = 0, Potion = new SimPotionUse("FirePotion", Who.Teammate(0), Target: 0) };
        thrown.Body.Add(new DamageAction(new DamageSpec(5, SimProps.Unpowered)) { TargetId = 0 });
        SimEngine.ApplyPlayTo(state, thrown, out var after, out _);
        Assert.Equal(0, Assert.Single(after).You.Powers[PowerId.Strength]);
    }

    [Theory]
    [InlineData(1, 2)] // your last potion: none left, so Belt Buckle gives its Dexterity
    [InlineData(2, 0)] // one still in your belt: nothing
    public void BeltBuckleGivesDexterityWhenYourLastPotionIsUsed(int potions, int dexterity)
    {
        // BeltBuckle.AfterPotionUsed, read from its code: with no potions left (Owner.Potions.Any() false) and its Dexterity not yet given.
        var state = Fight().WithRelic(Who.You, RelicId.BeltBuckle, key: "DexterityPower", value: 2);
        state.You.Potions = potions;
        SimEngine.ApplyPlayTo(state, FirePotion(target: 0), out var after, out _);
        Assert.Equal(dexterity, Assert.Single(after).You.Powers[PowerId.Dexterity]);
    }

    [Fact]
    public void APotionAimedAtTheCrabBehindYouTurnsYou()
    {
        // SurroundedPower.BeforePotionUsed: a potion of yours with a target turns you to face it, as a card aimed at it does. Facing left, the
        // Rocket (BackAttackRight) is behind you; a Weak Potion on it turns you, so the Crusher hits from behind instead.
        var state = new SimState
        {
            Enemies =
            {
                new SimEnemy { Id = 1, Name = "Crusher", Hp = 50, Hits = new() { 8 }, Intent = new() { new(Base: 8) }, IntendsToAttack = true, Powers = PowerSet.Empty.With(PowerId.BackAttackLeft, amount: 1) },
                new SimEnemy { Id = 2, Name = "Rocket", Hp = 50, Hits = new() { 10 }, Intent = new() { new(Base: 10) }, IntendsToAttack = true, Powers = PowerSet.Empty.With(PowerId.BackAttackRight, amount: 1) },
            },
        };
        state.You.Hp = 100;
        state.You.Powers = state.You.Powers.With(PowerId.Surrounded, amount: 1, data: 1);
        Assert.Equal(8 + 15, SimEngine.Run(state).TakenMax);
        var fire = Potion("FirePotion", target: 2, new DamageAction(new DamageSpec(1, SimProps.Unpowered)) { TargetId = 2 });
        Assert.Equal(12 + 10, SimEngine.Run(state, fire).TakenMax);
    }

    [Fact]
    public void ABlockPotionThrownAtATeammateIsTheirs()
    {
        var state = Fight();
        state.Teammates.Add(new SimPlayer { Hp = 40, MaxHp = 40 });
        SimEngine.ApplyPlayTo(state, Potion("BlockPotion", Who.Teammate(0), new GainBlockAction(Times: 1, Base: 12, SimProps.Unpowered) { To = Who.Teammate(0) }), out var after, out _);
        var branch = Assert.Single(after);
        Assert.Equal(12, branch.Teammates[0].Block);
        Assert.Equal(0, branch.You.Block);
    }

    [Fact]
    public void AUsedPotionLeavesTheBelt()
    {
        var state = Fight();
        state.You.Fairies = 1;
        var fairy = new SimPlay { Name = "FairyInABottle", TargetId = Who.You, Potion = new SimPotionUse("FairyInABottle", Who.You, Who.You, Fairy: true) };
        SimEngine.ApplyPlayTo(state, fairy, out var after, out _);
        var branch = Assert.Single(after);
        Assert.Equal(1, branch.You.Potions);
        Assert.Equal(0, branch.You.Fairies); // no Fairy in a Bottle left to hold back a death
    }

    [Fact]
    public void ACardAPotionMakesJoinsYourHand()
    {
        // Attack Potion (decision #335, the card translation's make): CardSelectCmd.FromChooseACardScreen of three random Attacks, then
        // CardPileCmd.AddGeneratedCardToCombat(the one you pick, Hand): a card made into your hand, its class a pick (unknown), heard once.
        var state = Fight();
        state.You.HandCount = 4;
        var made = new CardEventAction(CardEvent.Generated, Count: 1) { Indirect = true, JoinsHand = true };
        SimEngine.ApplyPlayTo(state, Potion("AttackPotion", Who.You, made), out var after, out _);
        var branch = Assert.Single(after);
        Assert.Equal(5, branch.You.HandCount);
        Assert.Equal(1, branch.You.Potions);
        Assert.Null(branch.Unmodelled);
    }

    [Fact]
    public void ACardAPotionMakesIntoAFullHandGoesToTheDiscardPile()
    {
        var state = Fight();
        state.You.HandCount = 10;
        var made = new CardEventAction(CardEvent.Generated, Count: 1) { Indirect = true, JoinsHand = true };
        SimEngine.ApplyPlayTo(state, Potion("SkillPotion", Who.You, made), out var after, out _);
        Assert.Equal(10, Assert.Single(after).You.HandCount);
    }

    [Fact]
    public void APickOfAFixedSizeToDiscardTakesItsMostAndDrawsAsManyAsItDiscarded()
    {
        // A pick of a fixed size (CardSelectorPrefs with one count): its most of the cards it can take, each a Discarded event; then a draw of what it
        // discarded (the cards the take took, read as it runs), so your hand keeps its size. (Gambler's Brew chooses how many: the next test.)
        var state = Fight();
        state.You.HandCount = 2;
        state.You.Draw = new SimDrawPile { Rest = { new SimDrawGroup(new SimDrawCard("plain"), 5) } };
        var play = Potion("GamblersBrew", Who.You,
            new TakeFromHandAction(HandTakeWay.Pick) { Event = CardEvent.Discarded, Most = 999999999 },
            new CardEventAction(CardEvent.Drawn, Count: 0) { Indirect = true, JoinsHand = true, CountFrom = SimCount.CardsTaken });
        SimEngine.ApplyPlayTo(state, play, out var after, out _);
        var branch = Assert.Single(after);
        Assert.Equal(2, branch.You.HandCount);
        Assert.Equal(3, branch.You.Draw!.Count);
    }

    [Fact]
    public void APickOfHowManyIsYourLaterDecisionWhenItCouldTakeACard()
    {
        // A dev review report: Ashwater (CardSelectCmd.FromHand, 0 to any, each exhausted) was predicted to exhaust your whole hand (27 block from
        // Feel No Pain); you exhausted fewer. How many is your later decision (decision #406): unknown, with a card it could take; none to take, none.
        var state = Fight();
        state.You.HandCount = 2;
        var play = Potion("Ashwater", Who.You, new TakeFromHandAction(HandTakeWay.Pick) { Event = CardEvent.Exhausted, Most = 999999999, ChoosesHowMany = true });
        SimEngine.ApplyPlayTo(state, play, out var after, out _);
        var branch = Assert.Single(after);
        Assert.NotNull(branch.Unmodelled);
        Assert.Equal(2, branch.You.HandCount);
        state.You.HandCount = 0;
        SimEngine.ApplyPlayTo(state, play, out var empty, out _);
        Assert.Null(Assert.Single(empty).Unmodelled);
    }

    [Fact]
    public void AHealThrownAtATeammateHealsThem()
    {
        // Blood Potion thrown at a teammate: CreatureCmd.Heal(target, a share of their max HP): their HP, not yours.
        var state = Fight();
        state.You.Hp = 30;
        state.Teammates.Add(new SimPlayer { Hp = 20, MaxHp = 60 });
        var heal = Potion("BloodPotion", Who.Teammate(teammateIndex: 0), new HealAction(Amount: 12) { Player = Who.Teammate(teammateIndex: 0) });
        SimEngine.ApplyPlayTo(state, heal, out var after, out _);
        var branch = Assert.Single(after);
        Assert.Equal(32, branch.Teammates[0].Hp);
        Assert.Equal(30, branch.You.Hp);
    }

    [Fact]
    public void APowerItsTargetAppliesToThemselvesIsTheirs()
    {
        // Duplicator and Focus Potion thrown at a teammate: PowerCmd.Apply(target, amount, applier: target): the teammate applies it, so what
        // answers a power its holder applies is theirs (ShroudPower.AfterPowerAmountChanged: Doom applied by its owner), never yours.
        var state = Fight();
        state.Teammates.Add(new SimPlayer { Hp = 40, MaxHp = 40 });
        state.You.Powers = state.You.Powers.With(PowerId.Shroud, amount: 2);
        state.Teammates[0].Powers = state.Teammates[0].Powers.With(PowerId.Shroud, amount: 2);
        var doom = new ApplyPowerAction(PowerId.Doom, Amount: 3) { To = ApplyPowerAction.Where.Creature, Creature = 0, Debuff = true, Applier = Who.Teammate(teammateIndex: 0) };
        SimEngine.ApplyPlayTo(state, Potion("PotionOfDoom", target: 0, doom), out var after, out _);
        var branch = Assert.Single(after);
        Assert.Equal(3, branch.Enemy(id: 0)!.Powers[PowerId.Doom]);
        Assert.Equal(2, branch.Teammates[0].Block);
        Assert.Equal(0, branch.You.Block);
    }

    /// <summary>You at <paramref name="hp"/> of 50 with Red Skull (its threshold 25), its Strength given when at or below it.</summary>
    private static SimState WithRedSkull(int hp)
    {
        var state = Fight();
        state.You.Hp = hp;
        var given = hp <= 25;
        state.You.Relics = state.You.Relics.With(RelicId.RedSkull, amount: 25, data: given ? Models.RedSkull.StrengthGiven : 0);
        if (given) state.You.Powers = state.You.Powers.With(PowerId.Strength, amount: 3);
        return state;
    }

    [Fact]
    public void AHealAboveRedSkullsThresholdTakesItsStrengthBack()
    {
        // CreatureCmd.Heal → Hook.AfterCurrentHpChanged (decision #342): RedSkull.ModifyStrengthIfNecessary finds your HP above 50% with its Strength
        // given, so PowerCmd.Apply of -3 Strength and StrengthApplied cleared; a heal that stays at or below it changes nothing.
        SimEngine.ApplyPlayTo(WithRedSkull(hp: 20), Potion("BloodPotion", Who.You, new HealAction(Amount: 10)), out var after, out _);
        var branch = Assert.Single(after);
        Assert.Equal(30, branch.You.Hp);
        Assert.Equal(0, branch.You.Powers[PowerId.Strength]);
        Assert.Equal(0, branch.You.Relics.DataOf(RelicId.RedSkull));

        SimEngine.ApplyPlayTo(WithRedSkull(hp: 10), Potion("BloodPotion", Who.You, new HealAction(Amount: 10)), out var still, out _);
        Assert.Equal(3, Assert.Single(still).You.Powers[PowerId.Strength]);

        // A max HP gain's heal tells it too (CreatureCmd.GainMaxHp: SetMaxHp, then Heal).
        SimEngine.ApplyPlayTo(WithRedSkull(hp: 22), Potion("FruitJuice", Who.You, new GainMaxHpAction(Amount: 5)), out var raised, out _);
        Assert.Equal(0, Assert.Single(raised).You.Powers[PowerId.Strength]);
    }

    [Fact]
    public void RedSkullsThresholdIsWorkedOutOnTheMaxHpAsItStands()
    {
        // RedSkull.ModifyStrengthIfNecessary: above it while CurrentHp > MaxHp × HpThreshold / 100, on the max HP then. Fruit Juice's 5 max HP takes you to
        // 27 of 55: not above 27.5, so its Strength stays (the threshold read as 25 HP at 50 max HP would have taken it back).
        var state = WithRedSkull(hp: 22);
        state.You.MaxHp = 50;
        state.Vars[$"{Who.You}:RedSkull:HpThreshold"] = 50;
        SimEngine.ApplyPlayTo(state, Potion("FruitJuice", Who.You, new GainMaxHpAction(Amount: 5)), out var raised, out _);
        var branch = Assert.Single(raised);
        Assert.Equal(27, branch.You.Hp);
        Assert.Equal(3, branch.You.Powers[PowerId.Strength]);
    }

    [Fact]
    public void AnOstyHealTellsTheHooksAsAPlayersDoes()
    {
        // CreatureCmd.Heal of Osty ends with Hook.AfterCurrentHpChanged (his gain): Red Skull asks its owner's HP on any creature's change, so with its
        // Strength given and you above its threshold it takes the Strength back.
        var state = WithRedSkull(hp: 30);
        state.You.Relics = state.You.Relics.With(RelicId.RedSkull, amount: 25, data: Models.RedSkull.StrengthGiven);
        state.You.Powers = state.You.Powers.With(PowerId.Strength, amount: 3);
        state.You.OstyShields = true;
        state.You.OstyHp = 4;
        state.You.OstyMaxHp = 10;
        SimEngine.ApplyPlayTo(state, Potion("BloodPotion", Who.You, new HealAction(Amount: 3) { Osty = true }), out var healed, out _);
        var branch = Assert.Single(healed);
        Assert.Equal(7, branch.You.OstyHp);
        Assert.Equal(0, branch.You.Powers[PowerId.Strength]);
    }

    [Fact]
    public void MaxHpGainedRaisesTheMaxThenHealsAsMuch()
    {
        // Fruit Juice: CreatureCmd.GainMaxHp(target, MaxHp): SetMaxHp(MaxHp + 5), then Heal of what it went up by.
        var state = Fight();
        state.You.Hp = 30;
        SimEngine.ApplyPlayTo(state, Potion("FruitJuice", Who.You, new GainMaxHpAction(Amount: 5)), out var after, out _);
        var branch = Assert.Single(after);
        Assert.Equal(55, branch.You.MaxHp);
        Assert.Equal(35, branch.You.Hp);
        Assert.Null(branch.Unmodelled);
    }

    [Fact]
    public void MaxHpGainedByATeammateIsTheirs()
    {
        var state = Fight();
        state.Teammates.Add(new SimPlayer { Hp = 40, MaxHp = 40 });
        var juice = Potion("FruitJuice", Who.Teammate(teammateIndex: 0), new GainMaxHpAction(Amount: 5) { Player = Who.Teammate(teammateIndex: 0) });
        SimEngine.ApplyPlayTo(state, juice, out var after, out _);
        var branch = Assert.Single(after);
        Assert.Equal(45, branch.Teammates[0].MaxHp);
        Assert.Equal(45, branch.Teammates[0].Hp);
        Assert.Equal(50, branch.You.MaxHp);
    }

    [Fact]
    public void MaxHpGainedWithNoneReadChangesNothing()
    {
        // A player whose max HP the simulator doesn't keep (none read: int.MaxValue) gains none, and isn't healed past what's kept.
        var state = Fight();
        state.You.MaxHp = int.MaxValue;
        state.You.Hp = 30;
        SimEngine.ApplyPlayTo(state, Potion("FruitJuice", Who.You, new GainMaxHpAction(Amount: 5)), out var after, out _);
        var branch = Assert.Single(after);
        Assert.Equal(int.MaxValue, branch.You.MaxHp);
        Assert.Equal(30, branch.You.Hp);
    }

    [Fact]
    public void APotionPlaysNoCard()
    {
        // Nothing of a card play's runs: your hand keeps its size, no card play is counted.
        var state = Fight();
        state.You.HandCount = 4;
        SimEngine.ApplyPlayTo(state, FirePotion(target: 0), out var after, out _);
        var branch = Assert.Single(after);
        Assert.Equal(4, branch.You.HandCount);
        Assert.Null(branch.Unmodelled);
    }

    // ---- a potion procured, gold gained (decision #362)

    /// <summary>A card of yours whose play is <paramref name="body"/>.</summary>
    private static SimPlay Card(string name, params SimAction[] body)
    {
        var play = new SimPlay
        {
            Name = name, HandCard = 9, HandDelta = -1, TargetId = Who.None,
            Card = new SimCardPlay(new SimCard(name, SimCardType.Skill) { HandId = 9 }, Who.You, Who.None, PlayIndex: 0, PlayCount: 1, AutoPlay: false),
        };
        play.Body.AddRange(body);
        return play;
    }

    [Theory]
    [InlineData(3, 0)] // room in your belt: the potion goes in, and Belt Buckle takes its Dexterity off (BeltBuckle.AfterPotionProcured, read from its code)
    [InlineData(1, 2)] // a full belt: nothing is procured (PotionCmd.TryToProcure: AddPotionInternal fails), so the Dexterity stays
    public void AlchemizesPotionGoesIntoYourBeltWhileItHasRoom(int slots, int dexterity)
    {
        // Your last potion drunk gives Belt Buckle's Dexterity (its DexterityApplied set); then Alchemize procures one.
        var state = Fight().WithRelic(Who.You, RelicId.BeltBuckle, key: "DexterityPower", value: 2);
        state.You.Potions = 1;
        state.You.PotionSlots = 3;
        SimEngine.ApplyPlayTo(state, FirePotion(target: 0), out var drunk, out _);
        var start = Assert.Single(drunk);
        Assert.Equal(2, start.You.Powers[PowerId.Dexterity]);
        start.You.PotionSlots = slots;
        if (slots == 1) start.You.Potions = 1; // full
        SimEngine.ApplyPlayTo(start, Card("Alchemize", new ProcurePotionAction()), out var after, out _);
        var branch = Assert.Single(after);
        Assert.Equal(dexterity, branch.You.Powers[PowerId.Dexterity]);
        Assert.Equal(1, branch.You.Potions); // one in, or the one already filling it
        Assert.Null(branch.Unmodelled);
    }

    [Fact]
    public void GoldGainedGivesDragonFruitsMaxHp()
    {
        // DragonFruit.AfterGoldGained, read from its code: gold for its owner gains them its max HP (CreatureCmd.GainMaxHp: max HP up, healed as much).
        var state = Fight().WithRelic(Who.You, RelicId.DragonFruit, key: "MaxHp", value: 1);
        state.You.Hp = 40;
        SimEngine.ApplyPlayTo(state, Card("HandOfGreed", new GainGoldAction()), out var after, out _);
        var branch = Assert.Single(after);
        Assert.Equal(51, branch.You.MaxHp);
        Assert.Equal(41, branch.You.Hp);
        Assert.Null(branch.Unmodelled);
    }
}
