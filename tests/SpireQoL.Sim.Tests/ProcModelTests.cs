using Xunit;
using static SpireQoL.Sim.Tests.ScriptBuilder;

namespace SpireQoL.Sim.Tests;

/// <summary>
/// Powers and relics that act on your plays and card events (Sim/Models/ProcPowerModels.cs, ProcRelicModels.cs), each as its hook
/// does in the decompile (v0.113.1): what it does, and what it logs (the play log the tabs come from).
/// </summary>
public class ProcModelTests
{
    private static SimState State(int enemies = 1, int hp = 100)
    {
        var state = new SimState();
        for (var i = 0; i < enemies; i++) state.Enemies.Add(Enemy(i, hp));
        return state;
    }

    private static SimState Power(SimState state, PowerId name, int amount, int data = 0)
    {
        state.You.Powers = state.You.Powers.With(name, amount, data);
        return state;
    }

    /// <summary>
    /// A power read from its code and kept in copies (decision #196), as one copy with its own state (its count: "Var.CardsLeft",
    /// "Data.energySpent"), as the adapter reads it from the game's object.
    /// </summary>
    private static SimState Copy(SimState state, PowerId name, int amount, params (string Field, int Value)[] fields)
    {
        state.You.Powers = state.You.Powers.With(name, amount);
        state.You.Fields = state.You.Fields.With($"{name.Name}@1", amount);
        foreach (var (field, value) in fields) state.You.Fields = state.You.Fields.With($"{name.Name}@1.{field}", value);
        return state;
    }

    private static SimState Relic(SimState state, RelicId name, int amount = 0, int data = 1, params (string Var, int Value)[] vars)
    {
        state.You.Relics = state.You.Relics.With(name, amount, data);
        foreach (var (v, value) in vars) state.Vars[$"{Who.You}:{name}:{v}"] = value;
        return state;
    }

    /// <summary>One of a relic's own fields, read from its code (decision #195), as the adapter seeds it from the game (SimReaders.Fields).</summary>
    private static SimState Field(SimState state, RelicId relic, string field, int value)
    {
        state.You.Fields = state.You.Fields.With($"{relic.Name}.{field}", value);
        return state;
    }


    private static SimState One(SimState state, SimScript script) => Assert.Single(After(state, script));

    private static SimScript Events(SimCard card, int plays, params SimAction[] body) => Play(card, target: Who.None, plays, body);

    // ---- your powers on play

    [Fact]
    public void AfterimageBlocksPerPlayButNotOnThePlayThatAppliesIt()
    {
        var after = One(Power(State(), PowerId.Afterimage, amount: 2), Events(Card("Defend", SimCardType.Skill), plays: 2));
        Assert.Equal(4, after.You.Block);
        Assert.Equal(2, after.Fired["AfterimagePower"]);
        Assert.Equal(4, after.Fired.DataOf("AfterimagePower"));
        // The Afterimage card: the power lands during the play, so nothing is bound to it.
        var own = One(State(), Events(Card("Afterimage", SimCardType.Power), plays: 1, new ApplyPowerAction(PowerId.Afterimage, Amount: 1) { To = ApplyPowerAction.Where.You }));
        Assert.Equal(0, own.You.Block);
        Assert.False(own.Fired.Has("AfterimagePower"));
    }

    [Fact]
    public void RageBlocksOnAttacksOnly()
    {
        var state = Power(State(), PowerId.Rage, amount: 3);
        Assert.Equal(3, One(state, Play(Card("Strike"), target: 0, plays: 1, Attack(baseDamage: 6))).You.Block);
        Assert.Equal(0, One(state, Events(Card("Defend", SimCardType.Skill), plays: 1)).You.Block);
    }

    [Fact]
    public void SpiritOfAshAndDanseMacabreBlockBeforeThePlay()
    {
        var ethereal = new SimCard("Wraith", SimCardType.Skill) { Ethereal = true };
        Assert.Equal(4, One(Power(State(), PowerId.SpiritOfAsh, amount: 4), Events(ethereal, plays: 1)).You.Block);
        var state = Power(State(), PowerId.DanseMacabre, amount: 3);
        state.Vars[$"{Who.You}:DanseMacabrePower:Energy"] = 2;
        Assert.Equal(3, One(state, Events(new SimCard("Big", SimCardType.Skill) { Energy = 2 }, plays: 1)).You.Block);
        Assert.Equal(0, One(state, Events(new SimCard("Small", SimCardType.Skill) { Energy = 1 }, plays: 1)).You.Block);
    }

    [Fact]
    public void PersonalHiveCountsItsDazedForEveryHit()
    {
        // PersonalHivePower.AfterDamageReceived: each powered attack hit from a player makes its amount of Dazed (decision #161).
        var state = new SimState { Enemies = { Enemy(id: 1, hp: 50) } };
        state.Enemies[0].Powers = PowerSet.Empty.With(PowerId.PersonalHive, amount: 2);
        var after = After(state, Play(Card("Flurry"), target: 1, plays: 1, Attack(baseDamage: 3, hits: 3))).Single();
        Assert.Equal(6, after.Fired.DataOf(SimKeys.Owned("PersonalHivePower", owner: 1)));
        var unpowered = After(state, Play(Card("Flurry"), target: 1, plays: 1, Attack(baseDamage: 3, hits: 3, props: SimProps.Move | SimProps.Unpowered))).Single();
        Assert.False(unpowered.Fired.Has(SimKeys.Owned("PersonalHivePower", owner: 1)));
    }

    [Fact]
    public void AModelThatFiredCountsInABranchNotFollowedPastIt()
    {
        // A dev review: Neow's Fury hits Personal Hive (its Dazed made), then you choose cards from your discard pile, which the
        // simulation doesn't follow (Unmodelled). Its warning showed 0%?: the Dazed were certain, made before that point.
        var key = SimKeys.Owned("PersonalHivePower", owner: 1);
        var fired = new SimState { Unmodelled = "you choose how many cards it takes into your hand" };
        fired.Fired = fired.Fired.With(key, 1, 2);
        fired.FiredAny = fired.FiredAny.With(key, 1, 2);
        var outcome = new SimOutcome { Branches = { fired } };
        var chance = outcome.LoggedChance(log => log.Has(key));
        Assert.True(chance.Certain);
        // One it didn't fire in, not followed: unknown, so 50% to 100%.
        var notFired = new SimState { Unmodelled = "you choose how many cards it takes into your hand" };
        var mixed = new SimOutcome { Branches = { fired, notFired } };
        Assert.Equal(0.5, mixed.LoggedChance(log => log.Has(key)).Low, precision: 9);
    }

    [Fact]
    public void PersonalHivesDazedGoIntoYourDrawPileAtRandomSpots()
    {
        // CardPileCmd.AddGeneratedCardToCombat(Dazed, Draw, Random): one hit, amount 1, into a pile of three plain cards: the pile
        // holds it after, so a later draw can find it.
        var dazed = new SimDrawCard("dazed") { Fading = true };
        var state = new SimState { Enemies = { Enemy(id: 1, hp: 50) } };
        state.Enemies[0].Powers = PowerSet.Empty.With(PowerId.PersonalHive, amount: 1);
        state.You.Draw = new SimDrawPile { Rest = { new SimDrawGroup(new SimDrawCard("plain"), 3) } };
        state.You.MadeCards = new Dictionary<string, SimDrawCard> { ["Dazed"] = dazed };
        var after = After(state, Play(Card("Strike"), target: 1, plays: 1, Attack(baseDamage: 3))).Single();
        Assert.Equal(1, after.You.Draw!.Rest.Single(group => group.Card.Key == "dazed").Count);
        // With a card known on top, it lands above it 1 time in 5 (5 spots in a pile of 4), else below it.
        state.You.Draw = new SimDrawPile { Top = { new SimDrawCard("plain") }, Rest = { new SimDrawGroup(new SimDrawCard("plain"), 3) } };
        var branches = After(state, Play(Card("Strike"), target: 1, plays: 1, Attack(baseDamage: 3)));
        Assert.Equal(2, branches.Count);
        Assert.Equal(0.2, branches.Single(branch => branch.You.Draw!.Top[0].Key == "dazed").Weight, precision: 9);
    }

    [Fact]
    public void BiiigHugsSootGoesIntoYourDrawPileOnEveryShuffle()
    {
        var soot = new SimDrawCard("soot");
        var state = Relic(State(), RelicId.BiiigHug);
        state.You.Draw = new SimDrawPile { Rest = { new SimDrawGroup(new SimDrawCard("plain"), 2) } };
        state.You.MadeCards = new Dictionary<string, SimDrawCard> { ["Soot"] = soot };
        var after = One(state, Events(Card("Defend", SimCardType.Skill), plays: 1, new CardEventAction(CardEvent.Shuffled, Count: 1)));
        Assert.Equal(1, after.You.Draw!.Rest.Single(group => group.Card.Key == "soot").Count);
        Assert.True(after.Fired.Has("BiiigHug"));
    }

    [Fact]
    public void SurprisesGremlinsJoinWhenItDiesAndLaterRandomHitsCanLandOnThem()
    {
        // SurprisePower.AfterDeath: CreatureCmd.Add of a Sneaky Gremlin (HP 10 or 11 here) and a Fat Gremlin (13), doing nothing this
        // turn. A 6 kills the Gremlin Merc; the random 3 after it lands on either gremlin, half the time each.
        var state = new SimState { Enemies = { Enemy(id: 0, hp: 5) } };
        state.Enemies[0].Powers = PowerSet.Empty.With(PowerId.Surprise, amount: 1);
        state.Enemies[0].Spawns = new List<SimSpawn> { new("Sneaky Gremlin", new[] { 10, 11 }) { Source = PowerId.Surprise }, new("Fat Gremlin", new[] { 13 }) { Source = PowerId.Surprise } };
        var outcome = SimEngine.Run(state, Play(Card("Strike"), target: 0, plays: 1, Attack(baseDamage: 6), Attack(baseDamage: 3, mode: SimPlay.Target.Random)));
        Assert.All(outcome.Branches, branch => Assert.Equal(3, branch.Enemies.Count));
        Assert.All(outcome.Branches, branch => Assert.Equal(new[] { 1, 2 }, branch.Enemies.Skip(1).Select(enemy => enemy.Id)));
        var fatHit = outcome.Chance(branch => branch.Enemy(id: 2)!.Hp == 10);
        Assert.Equal(0.5, fatHit.Low, precision: 9);
        Assert.Equal(0.5, outcome.Chance(branch => branch.Enemy(id: 1)!.Hp is 7 or 8).Low, precision: 9); // the Sneaky Gremlin, at either HP
        Assert.Equal(0.25, outcome.Chance(branch => branch.Enemy(id: 1)!.Hp == 11).Low, precision: 9); // HP 11, and the hit went to the Fat Gremlin
    }

    [Fact]
    public void ASpawnJoinsWithThePowersItStartsWith()
    {
        // A creature a death adds starts with the powers its AfterAddedToRoom gives it (SimSpawn.Powers, read from its code): 3 Plating here,
        // and it has them as it joins.
        var state = new SimState { Enemies = { Enemy(id: 0, hp: 5) } };
        state.Enemies[0].Powers = PowerSet.Empty.With(PowerId.Surprise, amount: 1);
        state.Enemies[0].Spawns = new List<SimSpawn> { new("Plated", new[] { 20 }) { Powers = PowerSet.Empty.With(PowerId.Plating, amount: 3), Source = PowerId.Surprise } };
        // The fight's first round: an enemy's Plating keeps its stacks as the enemies' turn starts (PlatingPower.AfterSideTurnStart: RoundNumber != 1).
        state.RoundNumber = 1;
        var branch = Assert.Single(SimEngine.Run(state, Play(Card("Strike"), target: 0, plays: 1, Attack(baseDamage: 6))).Branches);
        Assert.Equal(3, branch.Enemy(id: 1)!.Powers[PowerId.Plating]);
    }

    [Fact]
    public void BlossomShotGivesStretchForTheDamageItDealt()
    {
        // BlossomShot.OnPlay: PowerCmd.Apply<StretchPower>(the sum of TotalDamage + OverkillDamage) after the attack: 6 into 2 block and
        // 3 HP is 6 (blocked, unblocked and overkill).
        var state = new SimState { Enemies = { Enemy(id: 1, hp: 3, block: 2) } };
        var after = After(state, Play(Card("BlossomShot"), target: 1, plays: 1, Attack(baseDamage: 6) with { StretchFromDamage = true })).Single();
        Assert.Equal(6, after.You.Powers[PowerId.Stretch]);
    }

    [Fact]
    public void RedSkullsStrengthLandsAsYourHpDropsToItsThreshold()
    {
        // A dev review report: Hemokinesis at 42/80 (Red Skull's threshold 40) costs 2 HP, Red Skull gives 3 Strength, and the attack after
        // it has it (RedSkull.AfterCurrentHpChanged). Once: with its Strength already given, nothing more.
        SimState Board(int given)
        {
            var state = Relic(State(), RelicId.RedSkull, amount: 40, data: given, vars: ("StrengthPower", 3));
            state.You.Hp = 42;
            return state;
        }
        SimScript Hemokinesis() => Play(Card("Hemokinesis"), target: 0, plays: 1, new LoseHpAction(Amount: 2), Attack(baseDamage: 15));
        var after = One(Board(given: 0), Hemokinesis());
        Assert.Equal(82, after.Enemy(id: 0)!.Hp);
        Assert.Equal(3, after.You.Powers[PowerId.Strength]);
        Assert.Equal(85, One(Board(given: Models.RedSkull.StrengthGiven), Hemokinesis()).Enemy(id: 0)!.Hp);
    }

    [Fact]
    public void GravityHitsEveryEnemyAndSerpentFormBranchesOnItsTarget()
    {
        var after = One(Power(State(enemies: 2), PowerId.Gravity, amount: 3), Events(Card("Defend", SimCardType.Skill), plays: 1));
        Assert.Equal(97, after.Enemy(id: 0)!.Hp);
        Assert.Equal(97, after.Enemy(id: 1)!.Hp);
        Assert.Equal(6, after.Fired.DataOf("GravityPower"));
        var branches = After(Power(State(enemies: 2), PowerId.SerpentForm, amount: 5), Events(Card("Defend", SimCardType.Skill), plays: 1));
        Assert.Equal(2, branches.Count);
        Assert.All(branches, branch => Assert.Equal(5, branch.Fired.DataOf("SerpentFormPower")));
    }

    [Fact]
    public void PanacheFiresOnTheCardThatEndsItsCount()
    {
        // Read from its code: its CardsLeft counts down on each card its owner plays once it's been applied (alreadyApplied), and starts over at 5.
        var state = Copy(State(), PowerId.Panache, amount: 10, ("Var.CardsLeft", 2), ("Data.alreadyApplied", 1));
        var after = One(state, Events(Card("A", SimCardType.Skill), plays: 2));
        Assert.Equal(90, after.Enemy(id: 0)!.Hp);
        Assert.Equal(5, after.You.Fields[$"{PowerId.Panache.Name}@1.Var.CardsLeft"]);
        Assert.Equal(100, One(Copy(State(), PowerId.Panache, amount: 10, ("Var.CardsLeft", 3), ("Data.alreadyApplied", 1)), Events(Card("A", SimCardType.Skill), plays: 2)).Enemy(id: 0)!.Hp);
    }

    [Fact]
    public void TheSealedThroneGivesStarsThatSetOffBlackHole()
    {
        var state = Power(Power(State(), PowerId.TheSealedThrone, amount: 1), PowerId.BlackHole, amount: 4);
        var after = One(state, Events(Card("A", SimCardType.Skill), plays: 1));
        Assert.Equal(96, after.Enemy(id: 0)!.Hp);
        Assert.True(after.Fired.Has("TheSealedThronePower"));
        // A card that spends stars sets Black Hole off after its last play.
        var spender = new SimCard("Stardust", SimCardType.Skill) { Stars = 2 };
        Assert.Equal(96, One(Power(State(), PowerId.BlackHole, amount: 4), Events(spender, plays: 2)).Enemy(id: 0)!.Hp);
    }

    [Fact]
    public void JugglingCopiesTheThirdAttackOfTheTurn()
    {
        // Read from its code (decision #268): its data's attacksPlayedThisTurn counts the Attacks, the third kept and copied once it's played.
        SimState Juggling(int attacks)
        {
            var state = Power(State(), PowerId.Juggling, amount: 1);
            SimEngine.SetField(state, Who.You, PowerId.Juggling.Name + ".Data.attacksPlayedThisTurn", attacks);
            return state;
        }
        var after = One(Juggling(attacks: 2), Play(Card("Strike"), target: 0, plays: 1, Attack(baseDamage: 1)));
        Assert.True(after.Fired.Has("JugglingPower"));
        Assert.Equal(1, after.You.HandCount - State().You.HandCount);
        Assert.False(One(Juggling(attacks: 0), Play(Card("Strike"), target: 0, plays: 1, Attack(baseDamage: 1))).Fired.Has("JugglingPower"));
        // The fourth Attack isn't the third.
        Assert.False(One(Juggling(attacks: 3), Play(Card("Strike"), target: 0, plays: 1, Attack(baseDamage: 1))).Fired.Has("JugglingPower"));
    }

    [Fact]
    public void MonologueGivesStrengthThatTheReplayUses()
    {
        // MonologuePower.BeforeCardPlayed binds DynamicVars.Strength.IntValue, kept as "StrengthPower" (PowerVar<StrengthPower>): each copy's own
        // (its card sets it as it lands), read from its code.
        var state = Copy(State(), PowerId.Monologue, amount: 1, ("Var.StrengthPower", 2));
        var after = One(state, Play(Card("Strike"), target: 0, plays: 2, Attack(baseDamage: 5)));
        Assert.Equal(100 - 5 - 7, after.Enemy(id: 0)!.Hp);
        Assert.Equal(4, after.You.Powers[PowerId.Strength]);
    }

    [Fact]
    public void FeralNostalgiaAndPaleBlueDotCountTheirCharges()
    {
        var free = new SimCard("Claw", SimCardType.Attack) { Energy = 0 };
        Assert.True(One(Power(State(), PowerId.Feral, amount: 1, data: 0), Play(free, target: 0, plays: 1, Attack(baseDamage: 1))).Fired.Has("FeralPower"));
        Assert.False(One(Power(State(), PowerId.Feral, amount: 1, data: 1), Play(free, target: 0, plays: 1, Attack(baseDamage: 1))).Fired.Has("FeralPower"));
        Assert.False(One(Power(State(), PowerId.Feral, amount: 1, data: 0), Play(free with { Dupe = true }, target: 0, plays: 1, Attack(baseDamage: 1))).Fired.Has("FeralPower"));

        var state = Power(State(), PowerId.Nostalgia, amount: 1);
        Assert.True(One(state, Play(Card("Strike"), target: 0, plays: 1, Attack(baseDamage: 1))).Fired.Has("NostalgiaPower"));
        state.You.Turn = state.You.Turn.Add(SimKeys.Plays(PlayStage.Started, SimCardType.Skill), delta: 1);
        Assert.False(One(state, Play(Card("Strike"), target: 0, plays: 1, Attack(baseDamage: 1))).Fired.Has("NostalgiaPower"));

        var dot = Power(State(), PowerId.PaleBlueDot, amount: 1);
        dot.You.Turn = dot.You.Turn.Add("Finished", delta: 4);
        Assert.True(One(dot, Events(Card("A", SimCardType.Skill), plays: 1)).Fired.Has("PaleBlueDotPower"));
        // Already this turn: its own data's alreadyActivatedThisTurn (read from its code: a field of its own, seeded from the game).
        dot.You.Fields = dot.You.Fields.With(Assert.IsType<ReadModel>(SimModels.Get(PowerId.PaleBlueDot.Name)).FieldKey("Data.alreadyActivatedThisTurn"), 1);
        Assert.False(One(dot, Events(Card("A", SimCardType.Skill), plays: 1)).Fired.Has("PaleBlueDotPower"));
    }

    [Fact]
    public void PaleBlueDotFiresOnItsOwnPlayAsTheFifthCard()
    {
        // The power lands during the card's play and hears that play's AfterCardPlayed: played as your 5th card this turn, it
        // draws next turn at once (a report: DrawCardsNextTurnPower +2 right after Pale Blue Dot+ finished, 6th card).
        var state = State();
        state.You.Turn = state.You.Turn.Add("Finished", delta: 4);
        var dot = Events(Card("PaleBlueDot", SimCardType.Power), plays: 1, new ApplyPowerAction(PowerId.PaleBlueDot, Amount: 2) { To = ApplyPowerAction.Where.You });
        Assert.True(One(state, dot).Fired.Has("PaleBlueDotPower"));
        state.You.Turn = state.You.Turn.Add("Finished", delta: -1);
        Assert.False(One(state, dot).Fired.Has("PaleBlueDotPower"));
    }

    [Fact]
    public void TenderAndSmoggyAreWarnings()
    {
        Assert.True(SimModels.Get("TenderPower")!.HarmfulFor(Who.You)); // read from its code: its debuffs on you hurt you (decision #204)
        Assert.True(SimModels.Get("SmoggyPower")!.Harmful);
        Assert.True(One(Power(State(), PowerId.Smoggy, amount: 1), Events(Card("A", SimCardType.Skill), plays: 1)).Fired.Has("SmoggyPower"));
        Assert.False(One(Power(State(), PowerId.Smoggy, amount: 1), Play(Card("Strike"), target: 0, plays: 1, Attack(baseDamage: 1))).Fired.Has("SmoggyPower"));
    }

    // ---- melodies and arrows

    [Fact]
    public void FirstMelodiesFireTheirPowers()
    {
        var wah = Card("WahSlap", SimCardType.Attack, SimCardTag.Wah);
        var state = Power(Power(Power(State(), PowerId.ViolentChord, amount: 3), PowerId.Motif, amount: 1), PowerId.SongPath, amount: 4);
        var after = One(state, Play(wah, target: 0, plays: 2, Attack(baseDamage: 0)));
        Assert.Equal(97, after.Enemy(id: 0)!.Hp); // the first Wah only, not the replay's second play
        Assert.Equal(1, after.Fired["MotifPower"]);
        Assert.False(after.Fired.Has("SongPathPower"));
        state.You.Turn = state.You.Turn.Add(SimKeys.Plays(PlayStage.Started, SimCardTag.Wah), delta: 1);
        Assert.False(One(state, Play(wah, target: 0, plays: 1, Attack(baseDamage: 0))).Fired.Has("ViolentChordPower"));
    }

    [Fact]
    public void LostLutePlaysTheTopOfYourDrawPileOnTheFirstMelody()
    {
        // LostLutePower.AfterCardPlayed: AutoPlayFromDrawPile(melodies first this turn × Amount, Top). The top card is a 6-damage hit.
        var wah = Card("WahSlap", SimCardType.Attack, SimCardTag.Wah);
        var state = Power(State(), PowerId.LostLute, amount: 1);
        var strike = new SimPlay
        {
            Name = "StrikeIronclad", IsAttack = true, AutoPlay = true, TargetId = 0,
            Card = new SimCardPlay(new SimCard("StrikeIronclad", SimCardType.Attack), Who.You, 0, PlayIndex: 0, PlayCount: 1, AutoPlay: true),
            Body = { new AttackAction(Hits: 1, SimPlay.Target.Single, new DamageSpec(Base: 0m, SimProps.Move) { BaseOn = new Dictionary<int, decimal> { [0] = 6 }, Fixed = true }) },
        };
        state.You.Draw = new SimDrawPile { Top = { new SimDrawCard("strike") { FromPile = new SimHandCard { Name = "StrikeIronclad", IsAttack = true, Plays = { strike } } } } };
        var after = One(state, Play(wah, target: 0, plays: 1, Attack(baseDamage: 0)));
        Assert.Equal(94, after.Enemy(id: 0)!.Hp);
        Assert.Equal(1, after.AutoPlayed);
        Assert.Equal(0, after.You.Draw!.Count);
    }

    [Fact]
    public void AMelodyAutoPlayedInsideThePlayLeavesItTheFirst()
    {
        // MelodyCmd.IsFirstPlayThisTurn compares the first started entry with the play itself, "so a nested play can land in the history before the
        // outer play's hook sees it" (its own comment): Lost Lute, listening before Violent Chord, auto-plays a Wah off your draw pile in the Wah's
        // AfterCardPlayed; Violent Chord then hears the outer Wah, still the first (its 3 to every enemy), and not the inner one.
        var wah = Card("WahSlap", SimCardType.Attack, SimCardTag.Wah);
        var state = Power(Power(State(), PowerId.LostLute, amount: 1), PowerId.ViolentChord, amount: 3);
        var inner = new SimPlay
        {
            Name = "WahStep", AutoPlay = true, TargetId = Who.None,
            Card = new SimCardPlay(new SimCard("WahStep", SimCardType.Skill) { Tags = new() { SimCardTag.Wah } }, Who.You, Who.None, PlayIndex: 0, PlayCount: 1, AutoPlay: true),
        };
        state.You.Draw = new SimDrawPile { Top = { new SimDrawCard("wah step") { FromPile = new SimHandCard { Name = "WahStep", Plays = { inner } } } } };
        var after = One(state, Play(wah, target: 0, plays: 1, Attack(baseDamage: 0)));
        Assert.Equal(1, after.AutoPlayed);
        Assert.Equal(97, after.Enemy(id: 0)!.Hp);
    }

    [Fact]
    public void LullabyAndConductorsBatonFireOnTheThirdMelody()
    {
        // v0.114.0: MelodyCmd.PlayedThisTurn counts the turn's plays started (History.CardPlaysStarted), the card being played among them.
        var card = Card("KiStep", SimCardType.Skill, SimCardTag.Ki);
        var state = Power(State(), PowerId.Lullaby, amount: 5);
        Relic(state, RelicId.ConductorsBaton, data: 0);
        state.You.Turn = state.You.Turn.Add(SimKeys.Plays(PlayStage.Started, SimCardTag.Wah), delta: 1).Add(SimKeys.Plays(PlayStage.Started, SimCardTag.Cho), delta: 1);
        var after = One(state, Events(card, plays: 1));
        Assert.Equal(5, after.You.Block);
        Assert.True(after.Fired.Has("ConductorsBaton"));
        // Already complete: Lullaby's own ProccdThisTurn is set (by the play that completed them, or its AfterApplied), as the adapter reads it.
        state.You.Turn = state.You.Turn.Add(SimKeys.Plays(PlayStage.Started, SimCardTag.Ki), delta: 1);
        SimEngine.SetField(state, Who.You, key: "LullabyPower.ProccdThisTurn", value: 1);
        Assert.Equal(0, One(state, Events(card, plays: 1)).You.Block);
    }

    [Fact]
    public void ConductorsBatonPowerCutsTheEnemyTurnsAttacks()
    {
        // ConductorsBaton.AfterCardPlayed applies ConductorsBatonPower (1); its ModifyDamageMultiplicative is 0.75 on powered
        // attacks on its owner until the owner's next turn: Aeonglass' 22 becomes 16, 18 × 2 becomes 13 × 2.
        var card = Card("KiStep", SimCardType.Skill, SimCardTag.Ki);
        var state = State();
        Relic(state, RelicId.ConductorsBaton, data: 0);
        state.Enemies[0].Intent = new List<IntentHit> { new(Base: 22m) };
        state.You.Turn = state.You.Turn.Add(SimKeys.Plays(PlayStage.Started, SimCardTag.Wah), delta: 1).Add(SimKeys.Plays(PlayStage.Started, SimCardTag.Cho), delta: 1);
        var outcome = SimEngine.Run(state, Events(card, plays: 1));
        Assert.Equal(16, outcome.TakenMax);
        Assert.True(One(state, Events(card, plays: 1)).You.Powers.Has(PowerId.ConductorsBaton));
        state.Enemies[0].Intent = new List<IntentHit> { new(Base: 18m), new(Base: 18m) };
        Assert.Equal(26, SimEngine.Run(state, Events(card, plays: 1)).TakenMax);

        // Already on you (applied by an earlier play): read as a power, it cuts the intent the same way.
        var on = State();
        on.You.Powers = on.You.Powers.With(PowerId.ConductorsBaton, amount: 1);
        on.Enemies[0].Intent = new List<IntentHit> { new(Base: 22m) };
        Assert.Equal(16, SimEngine.Run(on, Events(Card("Defend", SimCardType.Skill), plays: 1)).TakenMax);
        // Not yet three melodies: no power, the full intent.
        state.You.Turn = NamedCounts.Empty.Add(SimKeys.Plays(PlayStage.Started, SimCardTag.Wah), delta: 1);
        Assert.Equal(36, SimEngine.Run(state, Events(card, plays: 1)).TakenMax);
    }

    [Fact]
    public void ExplosiveArrowsNeedsAnArrowAndYahAndTwineReactToDislodges()
    {
        // Explosive Arrows on the impale (after the attack), only with an arrow to shoot.
        var state = Power(State(), PowerId.ExplosiveArrows, amount: 4);
        state.You.Arrows = 1;
        var shot = Play(Card("QuickShot"), target: 0, plays: 1, new ShootAction(), Attack(baseDamage: 5), new ImpaleAction());
        var after = One(state, shot);
        Assert.Equal(100 - 5 - 4, after.Enemy(id: 0)!.Hp);
        Assert.Equal((1, 3), (after.Enemy(id: 0)!.Powers[PowerId.Impaled], after.Enemy(id: 0)!.Powers.DataOf(PowerId.Impaled)));
        Assert.Equal(0, after.You.Arrows);
        state.You.Arrows = 0;
        Assert.Equal(95, One(state, shot).Enemy(id: 0)!.Hp);

        // Recall pulls both arrows out: Yah once a turn, Twine per arrow; the arrows come back.
        var branch = Power(State(), PowerId.Yah, amount: 1);
        Relic(branch, RelicId.Twine, vars: ("Block", 2));
        branch.Enemies[0].Powers = PowerSet.Empty.With(PowerId.Impaled, amount: 2, data: 3);
        var recalled = One(branch, Play(Card("Recall", SimCardType.Skill), target: 0, plays: 1, new DislodgeAllAction()));
        Assert.Equal(1, recalled.Fired["YahPower"]);
        Assert.Equal(4, recalled.You.Block);
        Assert.Equal(2, recalled.You.Arrows);
        Assert.False(recalled.Enemy(id: 0)!.Powers.Has(PowerId.Impaled));
    }

    [Fact]
    public void RupturesStrengthLandsAsYouLoseHp()
    {
        // RupturePower.AfterDamageReceived: HP you lose on your turn from no card of yours in play (an enemy's Thorns hitting back) gives its Strength
        // at once (PowerCmd.Apply<StrengthPower>), so the attack's second hit has it: 5 then 7.
        var state = Power(State(hp: 100), PowerId.Rupture, amount: 2);
        state.You.Hp = 50;
        state.Enemies[0].Thorns = 3;
        var after = One(state, Play(Card("TwinStrike"), target: 0, plays: 1, Attack(baseDamage: 5, hits: 2)));
        Assert.Equal(88, after.Enemy(id: 0)!.Hp);
        Assert.Equal(4, after.You.Powers[PowerId.Strength]);
    }

    [Fact]
    public void RupturesStrengthFromTheCardBeingPlayedLandsAfterThePlay()
    {
        // RupturePower.AfterDamageReceived: HP lost from a card it keeps (cardSource != null && playedCards.ContainsKey(cardSource): BeforeCardPlayed
        // kept the card played) adds to that card's amount, given as Strength at its AfterCardPlayed: the card's own attack after its HP cost doesn't
        // have it (10, not 12).
        var state = Power(State(hp: 100), PowerId.Rupture, amount: 2);
        state.You.Hp = 50;
        var after = One(state, Play(Card("Hemokinesis"), target: 0, plays: 1, new LoseHpAction(Amount: 2), Attack(baseDamage: 10)));
        Assert.Equal(90, after.Enemy(id: 0)!.Hp);
        Assert.Equal(48, after.You.Hp);
        Assert.Equal(2, after.You.Powers[PowerId.Strength]);
    }

    [Fact]
    public void EveryThirdPoweredHitKnocksAnArrowOutAndADeathLetsThemAllGo()
    {
        var state = Relic(State(hp: 100), RelicId.Twine, vars: ("Block", 2));
        state.Enemies[0].Powers = PowerSet.Empty.With(PowerId.Impaled, amount: 2, data: 2);
        var after = One(state, Play(Card("Flurry"), target: 0, plays: 1, Attack(baseDamage: 1, hits: 4)));
        // Hit 2 knocks one out (counter 2 -> 0, back to 3); hits 3 and 4 count it down to 1.
        Assert.Equal((1, 1), (after.Enemy(id: 0)!.Powers[PowerId.Impaled], after.Enemy(id: 0)!.Powers.DataOf(PowerId.Impaled)));
        Assert.Equal(2, after.You.Block);
        Assert.Equal(1, after.You.Arrows);
        // A kill lets every arrow go.
        var kill = State(hp: 5);
        Relic(kill, RelicId.Twine, vars: ("Block", 2));
        kill.Enemies[0].Powers = PowerSet.Empty.With(PowerId.Impaled, amount: 2, data: 3);
        var killed = One(kill, Play(Card("Strike"), target: 0, plays: 1, Attack(baseDamage: 9)));
        Assert.Equal(4, killed.You.Block);
        Assert.Equal(2, killed.You.Arrows);
    }

    [Fact]
    public void OstysHitsFromYourCardCountYourImpaledDown()
    {
        // v0.114.0: ImpaledPower.AfterDamageReceived counts a powered hit whose source card's owner is its applier, whoever deals it (dealer != null &&
        // source.Owner.Creature == Applier): Osty's three hits from your card knock an arrow out (before v0.114.0 only its applier's own hits did).
        var state = State(hp: 100);
        state.You.WithOsty(hp: 10);
        state.Enemies[0].Powers = PowerSet.Empty.With(PowerId.Impaled, amount: 2, data: 3);
        var after = One(state, Play(Card("Squeeze"), target: 0, plays: 1, Attack(baseDamage: 1, hits: 3) with { ByOsty = true }));
        Assert.Equal((1, 3), (after.Enemy(id: 0)!.Powers[PowerId.Impaled], after.Enemy(id: 0)!.Powers.DataOf(PowerId.Impaled)));
        Assert.Equal(1, after.You.Arrows);
    }

    [Fact]
    public void CoveringFireBlocksTeammatesOnEveryShotTried()
    {
        var state = Power(State(), PowerId.CoveringFire, amount: 3);
        state.Teammates.Add(new SimPlayer { Hp = 50 });
        var after = One(state, Play(Card("QuickShot"), target: 0, plays: 2, new ShootAction(), new ImpaleAction()));
        Assert.Equal(6, after.Teammates[0].Block);
        Assert.Equal(0, after.You.Block);
    }

    [Fact]
    public void ATeammatesDrawsSetOffTheirDrawProcsNotYours()
    {
        // Huddle Up: every teammate draws 2, you don't (CardEffects.DrawsForEachTeammate, from its code). Their Speedster hits every enemy for each
        // card they draw; yours doesn't hear their draws.
        var state = State(hp: 100);
        state.You.Powers = state.You.Powers.With(PowerId.Speedster, amount: 3);
        var mate = new SimPlayer { Hp = 50 };
        mate.Powers = mate.Powers.With(PowerId.Speedster, amount: 2);
        state.Teammates.Add(mate);
        var after = One(state, Events(Card("HuddleUp", SimCardType.Skill), plays: 1,
            new CardEventAction(CardEvent.Drawn, Count: 2) { Player = Who.Teammate(teammateIndex: 0), Indirect = true }));
        Assert.Equal(100 - 2 * 2, after.Enemy(id: 0)!.Hp);
        Assert.Equal(2, after.Fired[$"SpeedsterPower@{Who.Teammate(teammateIndex: 0)}"]);
        Assert.False(after.Fired.Has("SpeedsterPower"));
    }

    [Fact]
    public void ShivsATeammateCreatesAreTheirsAndSoulsYouCreateAreYours()
    {
        // Blade Symphony: Shivs into the teammate's hand, created by them (their Arsenal, not yours). Glimpse Beyond: Souls into
        // their draw pile, created by you (your Arsenal, not theirs).
        var state = State();
        state.You.Powers = state.You.Powers.With(PowerId.Arsenal, amount: 1);
        var mate = new SimPlayer { Hp = 50 };
        mate.Powers = mate.Powers.With(PowerId.Arsenal, amount: 1);
        state.Teammates.Add(mate);
        var shivs = One(state, Events(Card("BladeSymphony", SimCardType.Skill), plays: 1,
            new CardEventAction(CardEvent.Generated, Count: 2) { Player = Who.Teammate(teammateIndex: 0), Indirect = true }));
        Assert.Equal(2, shivs.Fired[$"ArsenalPower@{Who.Teammate(teammateIndex: 0)}"]);
        Assert.False(shivs.Fired.Has("ArsenalPower"));
        var souls = One(state, Events(Card("GlimpseBeyond", SimCardType.Skill), plays: 1, new CardEventAction(CardEvent.Generated, Count: 3) { Indirect = true }));
        Assert.Equal(3, souls.Fired["ArsenalPower"]);
        Assert.False(souls.Fired.Has($"ArsenalPower@{Who.Teammate(teammateIndex: 0)}"));
    }

    [Fact]
    public void ATeammatesDrawsDontStopYourUnceasingTop()
    {
        // Your last card is Huddle Up: you draw and create nothing, so Unceasing Top draws, whatever your teammates draw.
        var state = Relic(State(), RelicId.UnceasingTop);
        state.You.HandCount = 1;
        state.Teammates.Add(new SimPlayer { Hp = 50 });
        var play = new SimPlay
        {
            Name = "HuddleUp", HandCard = 7, HandDelta = -1,
            Card = new SimCardPlay(new SimCard("HuddleUp", SimCardType.Skill), Who.You, Who.None, PlayIndex: 0, PlayCount: 1, AutoPlay: false),
            Body = { new CardEventAction(CardEvent.Drawn, Count: 2) { Player = Who.Teammate(teammateIndex: 0), Indirect = true } },
        };
        Assert.True(Assert.Single(SimEngine.Run(state, play).Branches).Fired.Has("UnceasingTop"));
    }

    [Fact]
    public void BlockForEveryPlayerSetsOffATeammatesJuggernaut()
    {
        // Rally (decision #75): block to you, then to each living teammate (CardEffects.BlocksEachPlayer, read from its code). A teammate's
        // Juggernaut hits a random enemy for its amount when they gain block: the only enemy (5 HP) dies.
        var state = State(hp: 5);
        var mate = new SimPlayer { Hp = 50 };
        mate.Powers = mate.Powers.With(PowerId.Juggernaut, amount: 6);
        state.Teammates.Add(mate);
        var after = One(state, Events(Card("Rally", SimCardType.Skill), plays: 1,
            new GainBlockAction(Times: 1, Base: 12, SimProps.Move), new GainBlockAction(Times: 1, Base: 12, SimProps.Move) { To = Who.Teammate(teammateIndex: 0) }));
        Assert.Equal(12, after.You.Block);
        Assert.Equal(12, after.Teammates[0].Block);
        Assert.True(after.Enemy(id: 0) is null or { Hp: <= 0 });
        // Logged under the teammate (decision #75): the tab on your card is theirs, not your Juggernaut's.
        Assert.Equal(1, after.Fired[$"JuggernautPower@{Who.Teammate(teammateIndex: 0)}"]);
    }

    // ---- card events

    [Fact]
    public void ExhaustsSetOffFeelNoPainDarkEmbraceAndTheirDraws()
    {
        var state = Power(Power(Power(State(), PowerId.FeelNoPain, amount: 3), PowerId.DarkEmbrace, amount: 1), PowerId.Speedster, amount: 2);
        var after = One(state, Events(Card("TrueGrit", SimCardType.Skill), plays: 1, new CardEventAction(CardEvent.Exhausted, Count: 2) { Indirect = true }));
        Assert.Equal(6, after.You.Block);
        Assert.Equal(2, after.Fired["DarkEmbracePower"]);
        Assert.Equal(96, after.Enemy(id: 0)!.Hp); // two draws from Dark Embrace, Speedster on each
        Assert.Equal(2, after.Fired["~FeelNoPainPower"]); // indirect: from the exhausts
    }

    [Fact]
    public void CountdownPowersFireWhenTheirCountRunsOut()
    {
        // Read from their code: Cacophony's Cards counts down a draw at a time; Orbit counts the energy spent (4 - energySpent % 4 to go).
        var state = Copy(State(), PowerId.Cacophony, amount: 7, ("Var.Cards", 2));
        var after = One(state, Events(Card("A", SimCardType.Skill), plays: 1, new CardEventAction(CardEvent.Drawn, Count: 3)));
        Assert.Equal(93, after.Enemy(id: 0)!.Hp);
        Assert.Equal(1, after.Fired["CacophonyPower"]);
        var orbit = One(Copy(State(), PowerId.Orbit, amount: 1, ("Data.energySpent", 1)), Events(Card("A", SimCardType.Skill), plays: 1, new CardEventAction(CardEvent.EnergySpent, Count: 2)));
        Assert.False(orbit.Fired.Has("OrbitPower"));
    }

    [Fact]
    public void CreatedCardsSetOffTheirPowersByKind()
    {
        var state = Power(Power(Power(State(), PowerId.PillarOfCreation, amount: 2), PowerId.Smokestack, amount: 3), PowerId.AllIsWell, amount: 4);
        var after = One(state, Events(Card("A", SimCardType.Skill), plays: 1,
            new CardEventAction(CardEvent.Generated, Count: 1), new CardEventAction(CardEvent.Generated, Count: 1) { Card = new SimCard(CardId.Dazed.Name, SimCardType.Status) },
            new CardEventAction(CardEvent.Generated, Count: 1) { Card = new SimCard(CardId.Sadness.Name, SimCardType.Status) }));
        Assert.Equal(3 * 2 + 4, after.You.Block); // Pillar on every created card, All Is Well on the Sadness
        Assert.Equal(94, after.Enemy(id: 0)!.Hp);   // Smokestack on each Status: the Dazed and the Sadness (a Status: Sadness' base(-1, CardType.Status, ...))
    }

    [Fact]
    public void ACorruptedCardsSelfDamageSetsOffCentennialPuzzle()
    {
        // Corrupted's 2 to you after the card (LoseHpAction) is HP lost from damage: Centennial Puzzle draws (a report: Kingly Punch
        // drew 3 that nothing predicted).
        var state = Relic(State(), RelicId.CentennialPuzzle, amount: 0, vars: ("Cards", 3));
        state.You.Hp = 50;
        var after = One(state, Play(Card("KinglyPunch"), target: 0, plays: 1, Attack(baseDamage: 10), new LoseHpAction(Amount: 2)));
        Assert.Equal(48, after.You.Hp);
        Assert.True(after.Fired.Has("CentennialPuzzle"));
    }

    [Fact]
    public void MiniRegentsStrengthIsOnTheCardThatSpentTheStars()
    {
        // MiniRegent.AfterStarsSpent: the stars go before the card does anything (PlayCardAction.SpendResources), so its Strength is on
        // the card's own hit (a report: Falling Star dealt 11, predicted 10). Once a turn.
        var state = Relic(State(), RelicId.MiniRegent, vars: ("StrengthPower", 1));
        // As the adapter builds it: the stars spent first, then the hit.
        var fallingStar = Play(Card("FallingStar"), target: 0, plays: 1, new CardEventAction(CardEvent.StarsSpent, Count: 1) { FirstPlayOnly = true }, Attack(baseDamage: 10));
        var after = One(state, fallingStar);
        Assert.True(after.Fired.Has("MiniRegent"));
        Assert.Equal(89, after.Enemies[0].Hp);
        Assert.Equal(1, after.You.Powers[PowerId.Strength]);
        Assert.Equal(78, One(after, fallingStar).Enemies[0].Hp); // used this turn: no more Strength (10 + 1)
    }

    [Fact]
    public void ChildOfTheStarsAndGalacticDustCountStarsSpent()
    {
        var state = Power(State(), PowerId.ChildOfTheStars, amount: 2);
        Relic(state, RelicId.GalacticDust, amount: 0, vars: new[] { ("Stars", 10), ("Block", 5) });
        // Galactic Dust's count is its own StarsSpent (read from its code: a field of its own, seeded from the game).
        var starsSpent = Assert.IsType<ReadModel>(SimModels.Get(RelicId.GalacticDust.Name)).FieldKey("StarsSpent");
        state.You.Fields = state.You.Fields.With(starsSpent, 8);
        var spender = new SimCard("Stardust", SimCardType.Skill) { Stars = 3 };
        var after = One(state, Events(spender, plays: 2, new CardEventAction(CardEvent.StarsSpent, Count: 1) { FirstPlayOnly = true }));
        Assert.Equal(6 + 5, after.You.Block);
        Assert.Equal(1, after.You.Fields[starsSpent]);
    }

    [Fact]
    public void DebuffsSetOffSleightOfFleshViciousAndShroud()
    {
        var state = Power(Power(Power(State(), PowerId.SleightOfFlesh, amount: 3), PowerId.Vicious, amount: 1), PowerId.Shroud, amount: 2);
        var after = One(state, Play(Card("Bash"), target: 0, plays: 1,
            new ApplyPowerAction(PowerId.Vulnerable, Amount: 2) { Debuff = true }, new ApplyPowerAction(PowerId.Doom, Amount: 3) { Debuff = true }));
        Assert.Equal(94, after.Enemy(id: 0)!.Hp);
        Assert.True(after.Fired.Has("ViciousPower"));
        Assert.Equal(2, after.You.Block);
        // Artifact takes the debuff: nothing lands, nothing fires.
        state.Enemies[0].Powers = PowerSet.Empty.With(PowerId.Artifact, amount: 1);
        Assert.Equal(100, One(state, Play(Card("Bash"), target: 0, plays: 1, new ApplyPowerAction(PowerId.Vulnerable, Amount: 2) { Debuff = true })).Enemy(id: 0)!.Hp);
    }

    [Fact]
    public void DarkEmbraceDrawsItsAmountForEachExhaust()
    {
        // DarkEmbracePower.AfterCardExhausted: CardPileCmd.Draw(base.Amount): two Dark Embraces draw two a card.
        var state = Power(State(), PowerId.DarkEmbrace, amount: 2);
        state.You.HandCount = 4;
        state.You.Draw = new SimDrawPile { Rest = { new SimDrawGroup(new SimDrawCard("plain"), Count: 10) } };
        var after = One(state, Events(Card("TrueGrit", SimCardType.Skill), plays: 1, new CardEventAction(CardEvent.Exhausted, Count: 1) { Indirect = true }));
        Assert.Equal(6, after.You.HandCount);
        Assert.Equal(2, after.Fired.DataOf("DarkEmbracePower"));
    }

    [Fact]
    public void NoDrawAndFiddleStopADrawInsideAPlay()
    {
        // CardPileCmd.DrawInternal: Hook.ShouldDraw first. No Draw (NoDrawPower.ShouldDraw) stops its owner's draws but the turn's
        // hand draw; Fiddle on its owner's turn. A dev review: Dark Embrace's draw on Forgotten Ritual's exhaust under No Draw.
        SimState Drawing(SimState state)
        {
            state.You.HandCount = 4;
            state.You.Draw = new SimDrawPile { Rest = { new SimDrawGroup(new SimDrawCard("plain"), Count: 10) } };
            return state;
        }
        var exhaust = Events(Card("ForgottenRitual", SimCardType.Skill), plays: 1, new CardEventAction(CardEvent.Exhausted, Count: 1) { Indirect = true });
        var noDraw = One(Drawing(Power(Power(State(), PowerId.DarkEmbrace, amount: 1), PowerId.NoDraw, amount: 1)), exhaust);
        Assert.Equal(4, noDraw.You.HandCount);
        Assert.Equal(0, noDraw.Fired.DataOf("DarkEmbracePower"));
        var fiddle = One(Drawing(Relic(Power(State(), PowerId.DarkEmbrace, amount: 1), RelicId.Fiddle, amount: 0)), exhaust);
        Assert.Equal(4, fiddle.You.HandCount);
        var free = One(Drawing(Power(State(), PowerId.DarkEmbrace, amount: 1)), exhaust);
        Assert.Equal(5, free.You.HandCount);
    }

    [Fact]
    public void ViciousDrawsItsAmountForEachVulnerableYouApply()
    {
        // ViciousPower.AfterPowerAmountChanged: CardPileCmd.Draw(base.Amount): Vicious 2 draws two (a dev review: Bash drew 2, the tab said 1).
        var state = Power(State(), PowerId.Vicious, amount: 2);
        state.You.HandCount = 4;
        state.You.Draw = new SimDrawPile { Rest = { new SimDrawGroup(new SimDrawCard("plain"), Count: 10) } };
        var after = One(state, Play(Card("Bash"), target: 0, plays: 1, new ApplyPowerAction(PowerId.Vulnerable, Amount: 2) { Debuff = true }));
        Assert.Equal(2, after.PlayEvents[CardEvent.Drawn]);
        Assert.Equal(6, after.You.HandCount);
        Assert.Equal(2, after.Fired.DataOf("ViciousPower"));
    }

    // ---- enemy powers

    [Fact]
    public void StrangleHitsItsOwnerForEachOfTheAppliersPlays()
    {
        var state = State();
        state.Enemies[0].Powers = PowerSet.Empty.With(PowerId.Strangle, amount: 2, Who.You + 100);
        var after = One(state, Events(Card("A", SimCardType.Skill), plays: 2));
        Assert.Equal(96, after.Enemy(id: 0)!.Hp);
        Assert.Equal(2, after.Fired["StranglePower@0"]);
    }

    [Fact]
    public void GalvanicHitsYouForAGalvanizedCard()
    {
        var state = State();
        state.You.Block = 1;
        state.You.Hp = 50;
        state.Enemies[0].Powers = PowerSet.Empty.With(PowerId.Galvanic, amount: 4);
        var after = One(state, Events(Card("A", SimCardType.Skill) with { Affliction = SimAffliction.Galvanized }, plays: 1));
        Assert.Equal(47, after.You.Hp);
        Assert.Equal(4, after.Fired.DataOf("GalvanicPower@0"));
        Assert.Equal(50, One(state, Events(Card("A", SimCardType.Skill), plays: 1)).You.Hp);
    }

    [Fact]
    public void WitheringPresenceCountsYourCardsDown()
    {
        var state = State();
        state.Enemies[0].Powers = PowerSet.Empty.With(PowerId.WitheringPresence, amount: 1, data: 2);
        Assert.True(One(state, Events(Card("A", SimCardType.Skill), plays: 2)).Fired.Has("WitheringPresencePower@0"));
        Assert.False(One(state, Events(Card("A", SimCardType.Skill), plays: 1)).Fired.Has("WitheringPresencePower@0"));
    }

    // ---- relics

    [Fact]
    public void ArtOfWarAndPaelsEyeWarnOnTheTurnsFirstPlays()
    {
        var state = Relic(Relic(State(), RelicId.ArtOfWar), RelicId.PaelsEye);
        state.TurnNumber = 1; // the game numbers its turns from 1 (ArtOfWar.AfterEnergyReset pays from the second)
        var after = One(state, Play(Card("Strike"), target: 0, plays: 2, Attack(baseDamage: 1)));
        Assert.Equal(1, after.Fired["ArtOfWar"]);
        Assert.Equal(1, after.Fired["PaelsEye"]);
        state.You.Turn = state.You.Turn.Add("Started", delta: 1).Add(SimKeys.Plays(PlayStage.Started, SimCardType.Attack), delta: 1);
        SimEngine.SetField(state, Who.You, "ArtOfWar.AnyAttacksPlayedThisTurn", 1); // its own record of that Attack, as the game keeps it
        var later = One(state, Play(Card("Strike"), target: 0, plays: 1, Attack(baseDamage: 1)));
        Assert.False(later.Fired.Has("ArtOfWar"));
        Assert.False(later.Fired.Has("PaelsEye"));
    }

    [Fact]
    public void PaelsTearsWarnsWhenTheLastEnergyIsSpent()
    {
        var state = Relic(State(), RelicId.PaelsTears, data: 0);
        state.You.Energy = 2;
        var spend = Spend(new SimCard("Big", SimCardType.Skill) { Energy = 2 }, energy: 2);
        Assert.True(One(state, spend).Fired.Has("PaelsTears"));
        state.You.Energy = 3;
        Assert.False(One(state, spend).Fired.Has("PaelsTears"));
        // Already at 0 before a free card: nothing given up by this play.
        state.You.Energy = 0;
        Assert.False(One(state, Spend(new SimCard("Free", SimCardType.Skill), energy: 0)).Fired.Has("PaelsTears"));
    }

    [Fact]
    public void GremlinHornGivesEnergyAndADrawWhenAnEnemyDies()
    {
        // GremlinHorn.AfterDeath (a dev-mode report: Unleash killing a segment left 2 energy, predicted 1): 1 energy, then a draw.
        var state = Relic(State(enemies: 2, hp: 5), RelicId.GremlinHorn, vars: new[] { ("Energy", 1), ("Cards", 1) });
        state.You.Energy = 1;
        state.You.HandCount = 3;
        var kill = One(state, Play(Card("Strike"), target: 0, plays: 1, Attack(baseDamage: 6)));
        Assert.Equal(2, kill.You.Energy);
        Assert.Equal(1, kill.PlayEvents[CardEvent.Drawn]);
        // No death, nothing.
        var hit = One(Relic(State(enemies: 2, hp: 50), RelicId.GremlinHorn), Play(Card("Strike"), target: 0, plays: 1, Attack(baseDamage: 6)));
        Assert.Equal(0, hit.You.Energy);
        Assert.Equal(0, hit.PlayEvents[CardEvent.Drawn]);
    }

    [Fact]
    public void PocketwatchWarnsOnThePlayThatPassesItsLimit()
    {
        // Pocketwatch (AfterCardPlayed counts each card played; ModifyHandDraw: 3 more next turn when no more than 3 were played):
        // with 3 played, the 4th gives up next turn's draws. Data is the threshold, Amount the count.
        Assert.True(One(Relic(State(), RelicId.Pocketwatch, amount: 3, data: 3), Play(Card("Strike"), target: 0, plays: 1, Attack(baseDamage: 1))).Fired.Has("Pocketwatch"));
        // The 3rd stays within it; the 5th was already past it: nothing more is given up.
        Assert.False(One(Relic(State(), RelicId.Pocketwatch, amount: 2, data: 3), Play(Card("Strike"), target: 0, plays: 1, Attack(baseDamage: 1))).Fired.Has("Pocketwatch"));
        Assert.False(One(Relic(State(), RelicId.Pocketwatch, amount: 4, data: 3), Play(Card("Strike"), target: 0, plays: 1, Attack(baseDamage: 1))).Fired.Has("Pocketwatch"));
        // Each play of a replay counts: two plays from 2 played take it to 4, past the limit.
        Assert.True(One(Relic(State(), RelicId.Pocketwatch, amount: 2, data: 3), Play(Card("Strike"), target: 0, plays: 2, Attack(baseDamage: 1))).Fired.Has("Pocketwatch"));
    }

    /// <summary>A play as SimScript.Of compiles it: the energy it spends first, the play, then the card resolved.</summary>
    private static SimScript Spend(SimCard card, int energy, params SimAction[] body)
    {
        var script = Events(card, plays: 1, body);
        if (energy > 0) script.Actions.Insert(0, new CardEventAction(CardEvent.EnergySpent, energy));
        script.Actions.Add(new CardResolvedAction(HandDelta: 0, HandCard: 0, AutoPlay: false, Draws: 0, FadingDelta: 0, IsAttack: false));
        return script;
    }

    [Fact]
    public void PaelsTearsKeepsItsBonusWhenThePlayGivesEnergyBack()
    {
        // Your last energy on a 1-cost card while Orbit is one energy from firing: Orbit gives energy back, so energy is left at the
        // end of the turn and nothing is given up (the owner's feedback, 1.21.1).
        var state = Copy(Relic(State(), RelicId.PaelsTears, data: 0), PowerId.Orbit, amount: 1, ("Data.energySpent", 3));
        state.You.Energy = 1;
        var after = One(state, Spend(new SimCard("Strike", SimCardType.Attack) { Energy = 1 }, energy: 1));
        Assert.True(after.Fired.Has("OrbitPower"));
        Assert.Equal(1, after.You.Energy);
        Assert.False(after.Fired.Has("PaelsTears"));
        // Ivory Tile on a 3-cost card at 3 energy: the same, from a relic reacting after the play.
        var tile = Relic(Relic(State(), RelicId.PaelsTears, data: 0), RelicId.IvoryTile, vars: new[] { ("Energy", 1), ("EnergyThreshold", 3) });
        tile.You.Energy = 3;
        var big = One(tile, Spend(new SimCard("Big", SimCardType.Skill) { Energy = 3 }, energy: 3));
        Assert.Equal(1, big.You.Energy);
        Assert.False(big.Fired.Has("PaelsTears"));
        // Without them, the warning stands.
        var plain = Relic(State(), RelicId.PaelsTears, data: 0);
        plain.You.Energy = 1;
        Assert.True(One(plain, Spend(new SimCard("Strike", SimCardType.Attack) { Energy = 1 }, energy: 1)).Fired.Has("PaelsTears"));
    }

    [Fact]
    public void EnergyFromPowersAndRelicsReachesYourEnergy()
    {
        // Automation, Subroutine, Yah, Nunchaku: each gives the simulator its energy, not just its tab.
        var auto = Power(State(), PowerId.Automation, amount: 2, data: 1);
        Assert.Equal(2, One(auto, Events(Card("Draw", SimCardType.Skill), plays: 1, new CardEventAction(CardEvent.Drawn, Count: 1))).You.Energy);
        var sub = Power(State(), PowerId.Subroutine, amount: 2);
        Assert.Equal(2, One(sub, Events(Card("Defragment", SimCardType.Power), plays: 1)).You.Energy);
    }

    [Fact]
    public void UnsettlingLampDoublesTheFirstDebuffingCardsDebuffs()
    {
        var state = Relic(State(enemies: 2), RelicId.UnsettlingLamp);
        var after = One(state, Play(Card("Bash"), target: 0, plays: 1, new ApplyPowerAction(PowerId.Vulnerable, Amount: 2) { Debuff = true }));
        Assert.Equal(4, after.Enemy(id: 0)!.Powers[PowerId.Vulnerable]);
        Assert.True(after.Fired.Has("UnsettlingLamp"));
        Assert.Equal(0, after.You.Relics.DataOf(RelicId.UnsettlingLamp));
        // An enemy with Artifact doesn't set it off.
        state.Enemies[0].Powers = PowerSet.Empty.With(PowerId.Artifact, amount: 1);
        Assert.False(One(state, Play(Card("Bash"), target: 0, plays: 1, new ApplyPowerAction(PowerId.Vulnerable, Amount: 2) { Debuff = true })).Fired.Has("UnsettlingLamp"));
    }

    [Fact]
    public void CounterRelicsFireOnTheCardThatCompletesTheCount()
    {
        // Each counts its own attacks or skills this turn (Kunai.AttacksPlayedThisTurn: two already); the third fires it.
        var state = Field(Relic(State(), RelicId.Kunai, vars: new[] { ("Cards", 3), ("DexterityPower", 1) }), RelicId.Kunai, "AttacksPlayedThisTurn", value: 2);
        var after = One(state, Play(Card("Strike"), target: 0, plays: 1, Attack(baseDamage: 1)));
        Assert.True(after.Fired.Has("Kunai"));
        Assert.Equal(1, after.You.Powers[PowerId.Dexterity]);
        Assert.Equal(3, after.You.Fields["Kunai.AttacksPlayedThisTurn"]);
        var fan = Field(Relic(State(), RelicId.OrnamentalFan, vars: new[] { ("Cards", 3), ("Block", 4) }), RelicId.OrnamentalFan, "AttacksPlayedThisTurn", value: 1);
        Assert.Equal(0, One(fan, Play(Card("Strike"), target: 0, plays: 1, Attack(baseDamage: 1))).You.Block);
        Assert.Equal(4, One(fan, Play(Card("Strike"), target: 0, plays: 2, Attack(baseDamage: 1))).You.Block);
        var opener = Field(Relic(State(enemies: 2), RelicId.LetterOpener, vars: new[] { ("Cards", 3), ("Damage", 5) }), RelicId.LetterOpener, "SkillsPlayedThisTurn", value: 2);
        var opened = One(opener, Events(Card("Defend", SimCardType.Skill), plays: 1));
        Assert.Equal(95, opened.Enemy(id: 1)!.Hp);
        Assert.Equal(10, opened.Fired.DataOf("LetterOpener"));
    }

    [Fact]
    public void KunaiAndShurikenGiveTheirPowerValues()
    {
        // Kunai.AfterCardPlayed: PowerCmd.Apply<DexterityPower>(DynamicVars.Dexterity.BaseValue); Shuriken the same with Strength. Their
        // values are kept as "DexterityPower" and "StrengthPower" (PowerVar<T>): a model reading "Dexterity" got its fallback, 1.
        var kunai = Field(Relic(State(), RelicId.Kunai, vars: new[] { ("Cards", 3), ("DexterityPower", 2) }), RelicId.Kunai, "AttacksPlayedThisTurn", value: 2);
        Assert.Equal(2, One(kunai, Play(Card("Strike"), target: 0, plays: 1, Attack(baseDamage: 1))).You.Powers[PowerId.Dexterity]);
        var shuriken = Field(Relic(State(), RelicId.Shuriken, vars: new[] { ("Cards", 3), ("StrengthPower", 2) }), RelicId.Shuriken, "AttacksPlayedThisTurn", value: 2);
        Assert.Equal(2, One(shuriken, Play(Card("Strike"), target: 0, plays: 1, Attack(baseDamage: 1))).You.Powers[PowerId.Strength]);
    }

    [Fact]
    public void GamePieceDrawsItsCardsValue()
    {
        // GamePiece.AfterCardPlayed: CardPileCmd.Draw(DynamicVars.Cards.BaseValue) for each Power its owner plays.
        SimState WithCards(int cards)
        {
            var state = Relic(State(), RelicId.GamePiece, vars: ("Cards", cards));
            state.You.HandCount = 4;
            state.You.Draw = new SimDrawPile { Rest = { new SimDrawGroup(new SimDrawCard("plain"), Count: 10) } };
            return state;
        }
        var two = One(WithCards(cards: 2), Events(Card("Inflame", SimCardType.Power), plays: 1));
        var one = One(WithCards(cards: 1), Events(Card("Inflame", SimCardType.Power), plays: 1));
        Assert.Equal(one.You.HandCount + 1, two.You.HandCount);
        Assert.Equal(2, two.Fired.DataOf("GamePiece"));
        Assert.Equal(0, One(WithCards(cards: 2), Events(Card("Defend", SimCardType.Skill), plays: 1)).Fired.DataOf("GamePiece"));
    }

    [Fact]
    public void OnPlayRelicsCheckTheCard()
    {
        var state = Relic(Relic(Relic(State(), RelicId.DaughterOfTheWind, vars: ("Block", 1)), RelicId.LostWisp, vars: ("Damage", 8)), RelicId.IntimidatingHelmet, vars: new[] { ("Energy", 2), ("Block", 4) });
        Assert.Equal(1, One(state, Play(Card("Strike"), target: 0, plays: 1, Attack(baseDamage: 0))).You.Block);
        Assert.Equal(92, One(state, Events(Card("Inflame", SimCardType.Power), plays: 1)).Enemy(id: 0)!.Hp);
        Assert.Equal(4, One(state, Events(new SimCard("Big", SimCardType.Skill) { Energy = 2 }, plays: 1)).You.Block);
        var once = Relic(State(), RelicId.Permafrost, vars: ("Block", 6));
        Assert.Equal(6, One(once, Events(Card("Inflame", SimCardType.Power), plays: 2)).You.Block);
    }

    [Fact]
    public void RainbowRingFiresOnTheTypeThatCompletesTheSet()
    {
        // Its own counts of this turn's kinds (RainbowRing.AttacksPlayedThisTurn, SkillsPlayedThisTurn), as the game keeps them.
        var state = Field(Field(Relic(State(), RelicId.RainbowRing, data: 0), RelicId.RainbowRing, "AttacksPlayedThisTurn", value: 1), RelicId.RainbowRing, "SkillsPlayedThisTurn", value: 1);
        Assert.True(One(state, Events(Card("Inflame", SimCardType.Power), plays: 1)).Fired.Has("RainbowRing"));
        Assert.False(One(state, Events(Card("Defend", SimCardType.Skill), plays: 1)).Fired.Has("RainbowRing"));
    }

    [Fact]
    public void ExhaustDiscardAndShuffleRelics()
    {
        var state = Relic(Relic(Relic(State(), RelicId.CharonsAshes, amount: 3), RelicId.ToughBandages, vars: ("Block", 3)), RelicId.TheAbacus, vars: ("Block", 6));
        Relic(state, RelicId.BurningSticks);
        var after = One(state, Events(Card("Burn", SimCardType.Skill), plays: 1,
            new CardEventAction(CardEvent.Exhausted, Count: 1) { Card = Card("Burn", SimCardType.Skill) },
            new CardEventAction(CardEvent.Discarded, Count: 1), new CardEventAction(CardEvent.Shuffled, Count: 1)));
        Assert.Equal(97, after.Enemy(id: 0)!.Hp);
        Assert.Equal(9, after.You.Block);
        Assert.True(after.Fired.Has("BurningSticks"));
        // Joss Paper (read from its code: its count is its CardsExhausted field) at 4 of its 5: the fifth exhaust draws.
        var joss = Relic(State(), RelicId.JossPaper, amount: 0, vars: ("ExhaustAmount", 5));
        SimEngine.SetField(joss, Who.You, RelicId.JossPaper.Name + ".CardsExhausted", 4);
        Assert.True(One(joss, Events(Card("A", SimCardType.Skill), plays: 1, new CardEventAction(CardEvent.Exhausted, Count: 1))).Fired.Has("JossPaper"));
    }

    [Fact]
    public void UnceasingTopDrawsWhenAPlayLeavesYourHandEmpty()
    {
        // UnceasingTop.AfterHandEmptied, checked once the card has resolved (CombatManager.CheckForEmptyHand): your last card played
        // draws one, from your draw pile; a play that draws, or with another card left, doesn't.
        var state = Relic(State(), RelicId.UnceasingTop);
        state.You.HandCount = 1;
        state.You.Draw = new SimDrawPile { Rest = { new SimDrawGroup(new SimDrawCard("plain"), 3) } };
        SimPlay Last(int draws = 0) => new()
        {
            Name = "A", HandCard = 7, HandDelta = -1 + draws, Draws = draws,
            Card = new SimCardPlay(new SimCard("A", SimCardType.Skill), Who.You, Who.None, PlayIndex: 0, PlayCount: 1, AutoPlay: false),
            Body = { new CardEventAction(CardEvent.Drawn, draws) },
        };
        var after = Assert.Single(SimEngine.Run(state, Last()).Branches);
        Assert.True(after.Fired.Has("UnceasingTop"));
        Assert.Equal(1, after.You.HandCount);
        Assert.Equal(2, after.You.Draw!.Count);
        Assert.False(Assert.Single(SimEngine.Run(state, Last(draws: 1)).Branches).Fired.Has("UnceasingTop"));
        state.You.HandCount = 2;
        Assert.False(Assert.Single(SimEngine.Run(state, Last()).Branches).Fired.Has("UnceasingTop"));
    }

    [Fact]
    public void EveryProcModelHasATab()
    {
        foreach (var name in SimModels.Names)
        {
            var model = SimModels.Get(name)!;
            // Covering Fire: no tab by design (decision #25); end-of-turn models show in Incoming Damage, not on cards.
            if (model is Models.ProcModel and not Models.EndOfTurnModel && name != "CoveringFirePower") Assert.NotNull(model.Tab);
        }
    }
    // ---- draw and energy tabs (decision #132): the amount is what the source really gave

    [Fact]
    public void MotifDrawsItsAmountForEachMelodyTheCardIsFirstToPlay()
    {
        // MotifPower.AfterCardPlayed: CardPileCmd.Draw(num * Amount), num the melodies (Wah, Cho, Ki) this play is the first of this turn.
        var duet = Card("Duet", SimCardType.Skill, SimCardTag.Wah, SimCardTag.Cho);
        var after = One(Power(State(), PowerId.Motif, amount: 2), Events(duet, plays: 1));
        Assert.Equal(4, after.PlayEvents[CardEvent.Drawn]);
        Assert.Equal(1, after.Fired["MotifPower"]);
        Assert.Equal(4, after.Fired.DataOf("MotifPower"));
        // A Wah already played this turn: only the Cho counts.
        var state = Power(State(), PowerId.Motif, amount: 2);
        state.You.Turn = state.You.Turn.Add(SimKeys.Plays(PlayStage.Started, SimCardTag.Wah), delta: 1);
        Assert.Equal(2, One(state, Events(duet, plays: 1)).Fired.DataOf("MotifPower"));
        // No melody: no draw.
        Assert.False(One(Power(State(), PowerId.Motif, amount: 2), Events(Card("Defend", SimCardType.Skill), plays: 1)).Fired.Has("MotifPower"));
    }

    [Fact]
    public void ADrawTabCountsOnlyTheCardsThatFitInYourHand()
    {
        // Dark Embrace draws one per exhaust; with one space left in hand (10 at most), two exhausts draw one card: its tab says 1,
        // and it fired twice.
        var state = Power(State(), PowerId.DarkEmbrace, amount: 1);
        state.You.HandCount = 9;
        state.You.Draw = new SimDrawPile { Rest = { new SimDrawGroup(new SimDrawCard("plain"), Count: 10) } };
        var after = One(state, Events(Card("Purge", SimCardType.Skill), plays: 1, new CardEventAction(CardEvent.Exhausted, Count: 2)));
        Assert.Equal(10, after.You.HandCount);
        Assert.Equal(2, after.Fired["DarkEmbracePower"]);
        Assert.Equal(1, after.Fired.DataOf("DarkEmbracePower"));
    }

    [Fact]
    public void CentennialPuzzleDrawsOnTheFirstHpLossOfTheCombat()
    {
        // CentennialPuzzle.AfterDamageReceived: UnblockedDamage > 0 and not UsedThisCombat: draws its Cards (3), once.
        var state = Relic(State(), RelicId.CentennialPuzzle, amount: 0, vars: new[] { ("Cards", 3) });
        state.You.Hp = 50;
        state.You.HandCount = 3;
        var bleed = One(state, Events(Card("Bloodletting", SimCardType.Skill), plays: 1, new LoseHpAction(Amount: 3)));
        Assert.Equal(3, bleed.PlayEvents[CardEvent.Drawn]);
        Assert.Equal(3, bleed.Fired.DataOf("CentennialPuzzle"));
        // Read from its code: UsedThisCombat is its own field.
        Assert.Equal(1, SimEngine.FieldsOf(bleed, Who.You)[RelicId.CentennialPuzzle.Name + ".UsedThisCombat"]);
        // Used up this combat: nothing.
        var used = Relic(State(), RelicId.CentennialPuzzle, amount: 0);
        SimEngine.SetField(used, Who.You, RelicId.CentennialPuzzle.Name + ".UsedThisCombat", 1);
        Assert.False(One(used, Events(Card("Bloodletting", SimCardType.Skill), plays: 1, new LoseHpAction(Amount: 3))).Fired.Has("CentennialPuzzle"));
    }

    [Fact]
    public void AnEnergyTabCountsTheEnergyItGives()
    {
        var after = One(Power(State(), PowerId.Subroutine, amount: 2), Events(Card("Defragment", SimCardType.Power), plays: 1));
        Assert.Equal(2, after.You.Energy);
        Assert.Equal(2, after.Fired.DataOf("SubroutinePower"));
        var nun = One(Field(Relic(State(), RelicId.Nunchaku, vars: new[] { ("Cards", 10), ("Energy", 1) }), RelicId.Nunchaku, "AttacksPlayed", value: 9), Play(Card("Strike"), target: 0, plays: 1, Attack(baseDamage: 1)));
        Assert.Equal(1, nun.Fired.DataOf("Nunchaku"));
    }
}

/// <summary>Teammates' arrows (ImpaledPowerOther): your hits don't knock them loose, but a kill gives them back (their Twine).</summary>
public class TeammateArrowTests
{
    [Fact]
    public void AKillGivesATeammatesArrowsBackAndTheirTwineBlocks()
    {
        var state = new SimState { Enemies = { Enemy(id: 0, hp: 5), new SimEnemy { Id = 1, Name = "E1", Hp = 50, Hits = new() { 10 } } } };
        state.Enemies[0].Powers = PowerSet.Empty.With(new PowerId("ImpaledPowerOther"), amount: 2, data: 2); // teammate 0: 2 arrows
        var mate = new SimPlayer { Hp = 40 };
        mate.Relics = mate.Relics.With(RelicId.Twine, amount: 0, data: 1);
        state.Teammates.Add(mate);
        state.Vars[$"{Who.Teammate(teammateIndex: 0)}:Twine:Block"] = 3;
        // A hit that doesn't kill: nothing comes back.
        var hit = Assert.Single(After(state, Play(Card("Strike"), target: 0, plays: 1, Attack(baseDamage: 2, hits: 2))));
        Assert.Equal(0, hit.Teammates[0].Block);
        // The kill: both arrows come back, Twine blocks for each.
        var kill = Assert.Single(After(state, Play(Card("Strike"), target: 0, plays: 1, Attack(baseDamage: 6))));
        Assert.Equal(6, kill.Teammates[0].Block);
        Assert.Equal(2, kill.Teammates[0].Arrows);
        Assert.False(kill.Enemy(id: 0)!.Powers.Has(new PowerId("ImpaledPowerOther")));
    }

}
