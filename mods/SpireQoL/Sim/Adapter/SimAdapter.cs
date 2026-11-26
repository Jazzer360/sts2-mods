using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.MonsterMoves.Intents;
using Sts2Mods.Core.Game;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Relics;

namespace SpireQoL.Sim;

/// <summary>
/// Builds the simulator's plain state from the live game, and turns a card in hand into a
/// <see cref="SimPlay"/>. Only knowable information goes in (CLAUDE.md): what's on screen, counters
/// a player could track, and the game's rules. Random targets stay random (the simulator branches).
/// </summary>
public static partial class SimAdapter
{

    /// <summary>
    /// Your powers the simulator tracks that aren't read from their code (Juggernaut, Accelerant). One read from its code (Feel No Pain,
    /// Thorns, Flame Barrier: decision #281; Plating: decision #289) is simulated as any read model is, its amount the card's step's.
    /// </summary>
    internal static readonly HashSet<string> SelfPowers = new()
    {
        nameof(JuggernautPower),
        nameof(AccelerantPower),
    };

    /// <summary>
    /// How many times an enemy's poison ticks: 1 plus the Accelerant of every living opponent of it, so every player's
    /// (PoisonPower.TriggerCount). Not capped at its stacks, as the game's count is: stacks added later tick that many times.
    /// </summary>
    private static int PoisonTriggers(Creature enemy, ICombatState combat)
    {
        try { return 1 + combat.GetOpponentsOf(enemy).Where(creature => creature.IsAlive).Sum(creature => creature.GetPowerAmount<AccelerantPower>()); }
        catch { return 1; }
    }



    /// <summary>
    /// The power the simulator models for a power type: a temporary Thorns power (Cho Barbs, v0.113.0) gives real
    /// Thorns until the end of the enemy turn (TemporaryThornsPower.BeforeApplied applies ThornsPower), so it's Thorns.
    /// </summary>
    internal static string SimPowerName(Type power) => IsTemporaryThorns(power) ? nameof(ThornsPower) : power.Name;

    /// <summary>
    /// A temporary Thorns power (Cho Barbs): what it gives is already in the owner's ThornsPower, so reading the state and the
    /// proc tabs leave it out (Thorns has the one tab).
    /// </summary>
    internal static bool IsTemporaryThorns(Type power) => power.BaseType?.Name == "TemporaryThornsPower";

    // ------------------------------------------------------------------ state

    /// <summary>
    /// The state of the fight from the game (BuildFromGame). A member it couldn't read (Sts2State.ReadScope) makes it unknown, named
    /// by the member: its fallback is a guess, not the game's state. With <paramref name="pileAutoPlays"/>, each card in your piles has its play
    /// from your draw pile built (SimDrawCard.FromPile) whatever is in your hand, as when a card there auto-plays from it: a use that auto-plays the
    /// pile's top (Distilled Chaos, decision #338) gets its own start this way, never the forecast's (its profiles split by card, so every other
    /// simulation's draws would branch more). The ids are the build's own handle (BoardIds), carrying its piles' profiles for the plays translated on it.
    /// </summary>
    public static SimState Build(Player me, ICombatState combat, out Dictionary<Creature, int> ids, bool pileAutoPlays = false)
    {
        using var reads = Sts2State.TrackReads();
        var state = BuildFromGame(me, combat, out ids, pileAutoPlays);
        if (reads.Unread.Count > 0) state.Unmodelled ??= SimState.Unread(reads.Unread[0]);
        return state;
    }

    /// <summary>
    /// The fight's multiplayer scaling (MultiplayerScalingModel.GetMultiplayerScaling(encounter, act), what PowerModel's GetScaledAmountForMultiplayer
    /// multiplies by: decision #234), from the game's own math: Creature.ScaleHpForMultiplayer is hp × players × it. Null in a fight of one player
    /// (nothing is scaled) or where the game's math has no value (an act it doesn't scale).
    /// </summary>
    private static decimal? EncounterScaling(ICombatState combat)
    {
        var players = combat.Players.Count;
        if (players <= 1) return null;
        try { return Creature.ScaleHpForMultiplayer(1m, combat.Encounter, players, combat.RunState.CurrentActIndex) / players; }
        catch (ArgumentOutOfRangeException) { return null; }
    }

    /// <summary>
    /// Whether a creature's death sets off Fatal (every power on it answers PowerModel.ShouldOwnerDeathTriggerFatal true: Book Repair Knife's count of
    /// the Doom kills), read from each power's class: one that doesn't override it answers true (PowerModel's own); one whose override returns a
    /// constant (Minion's false), that; any other (Reattach's: whether its other segments are dead, which can change before it dies) null, unknown.
    /// </summary>
    private static bool? DeathSetsOffFatal(Creature creature)
    {
        var answer = true;
        foreach (var power in creature.Powers)
        {
            var type = power.GetType();
            var fatal = FatalAnswers.GetOrAdd(type, ReadFatalAnswer);
            if (fatal == null) return null;
            answer &= fatal.Value;
        }
        return answer;
    }

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<Type, bool?> FatalAnswers = new();

    /// <summary>A power class's answer to ShouldOwnerDeathTriggerFatal, read from its code: true without an override, a constant's, or null.</summary>
    private static bool? ReadFatalAnswer(Type type)
    {
        var method = type.GetMethod(nameof(PowerModel.ShouldOwnerDeathTriggerFatal), System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public, Type.EmptyTypes);
        if (method == null || method.DeclaringType == typeof(PowerModel)) return true;
        try
        {
            var ways = SpireQoL.Procs.Programs.ProgramReader.ReadReturnedWays(method.DeclaringType!, method, SpireQoL.Procs.Programs.RuntimeCode.Read).Ways;
            return ways.Count > 0 && ways.All(way => way.Value is SpireQoL.Procs.Programs.ConstantValue) && ways.Select(way => ((SpireQoL.Procs.Programs.ConstantValue)way.Value).Value != 0).Distinct().Count() == 1
                ? ((SpireQoL.Procs.Programs.ConstantValue)ways[0].Value).Value != 0 : null;
        }
        catch (Exception exception) when (exception is InvalidOperationException or NotSupportedException) { return null; }
    }

    /// <summary>
    /// Any creature's simulator id (Who), as BuildFromGame gives them: an enemy's (its id in <paramref name="enemyIds"/>, read as each is asked), you,
    /// teammate k (<paramref name="mates"/>' order), or one of a player's pets the simulator keeps; null for any other.
    /// </summary>
    private static Func<Creature, int?> CreatureIds(Player me, List<Player> mates, IReadOnlyDictionary<Creature, int> enemyIds)
    {
        int? PlayerIdOf(Creature creature) => creature == me.Creature ? Who.You
            : mates.FindIndex(player => player.Creature == creature) is var teammateIndex and >= 0 ? Who.Teammate(teammateIndex) : null;
        return creature => enemyIds.TryGetValue(creature, out var enemyId) ? enemyId : PlayerIdOf(creature)
            ?? (creature.PetOwner is { } petOwner && PetKindOf(petOwner, creature) is { } kind && PlayerIdOf(petOwner.Creature) is { } ownerId ? Who.PetOf(ownerId, kind) : null);
    }

    private static SimState BuildFromGame(Player me, ICombatState combat, out Dictionary<Creature, int> ids, bool pileAutoPlays)
    {
        var idMap = new BoardIds();
        ids = idMap;
        var state = new SimState { MainRules = BranchRules.MainRulesNow(), TurnNumber = me.PlayerCombatState?.TurnNumber ?? 0, RoundNumber = combat.RoundNumber, Dread = MoveBranches.DreadRun, EncounterScaling = EncounterScaling(combat) };
        var mates = Teammates(me, combat);
        // Any creature's simulator id: the enemies' as they're given below (the map read as each is asked).
        var creatureIds = CreatureIds(me, mates, idMap);
        int? IdOf(Creature creature) => creatureIds(creature);
        var targets = combat.Players.Select(player => player.Creature).ToList();

        var nextEnemyId = 0;
        var moves = new List<(SimEnemy Enemy, MonsterModel Monster, List<int> HitsPerAttack)>();
        foreach (var creature in combat.Enemies)
        {
            var id = nextEnemyId++;
            idMap[creature] = id;
            var enemy = new SimEnemy
            {
                Id = id,
                Name = Combat.TurnEndModel.SafeTitle(creature.Monster!),
                Monster = creature.Monster?.GetType().Name ?? "",
                Hp = creature.CurrentHp,
                Block = creature.Block,
                Hittable = creature.IsAlive && creature.IsHittable,
                Stunned = creature.IsStunned,
                Poison = SafeInt(() => creature.GetPower<PoisonPower>()?.CalculateTotalDamageNextTurn() ?? 0),
                PoisonTriggers = PoisonTriggers(creature, combat),
                IntendsToAttack = creature.IsAlive && SafeBool(() => creature.Monster?.IntendsToAttack == true),
            };
            // Every power, with the numbers its model keeps (Curl Up, Sneaky, Skittish, Thorns, Burrowed, Asleep, Slumber, Flutter,
            // Plow, Shriek, The Gambit, Slippery, Hard to Kill, Intangible, Hardened Shell, Barricade, Strength, Weak...).
            enemy.Powers = SimReaders.Powers(creature, IdOf);
            enemy.Fields = SimReaders.Fields(creature.Powers, IdOf);
            enemy.DeathSetsOffFatal = DeathSetsOffFatal(creature);
            foreach (var power in creature.Powers) SimReaders.AddVars(state, id, power);
            enemy.StartedAlive = creature.IsAlive;
            // The creatures its death adds (Surprise's gremlins), read from its powers' AfterDeath code, with every HP each can start with.
            if (creature.IsAlive) enemy.Spawns = SpawnsOf(creature, combat);
            // Its move rules, where one of its powers' code compares its next move (decision #307: a stun naming the move after it).
            if (creature.IsAlive && creature.Powers.Any(power => SimModels.Get(power.GetType().Name) is ReadModel { ComparesNextMove: true })) enemy.Rules = MoveRulesOf(creature);
            if (!creature.IsAlive) enemy.Hp = 0;
            if (creature.IsAlive && creature.Monster != null)
            {
                try
                {
                    var intent = new List<IntentHit>();
                    var hitsPerAttack = new List<int>();
                    foreach (var attack in creature.Monster.NextMove.Intents.OfType<AttackIntent>())
                    {
                        var perHit = attack.GetSingleDamage(targets, creature);
                        hitsPerAttack.Add(Math.Max(1, attack.Repeats));
                        for (var i = 0; i < Math.Max(1, attack.Repeats); i++) enemy.Hits.Add(perHit);
                        // the hit's base and the captured terms on each player; the models (its Strength and Weak, your Vulnerable,
                        // Intangible...) apply as it lands, so what your cards change shows.
                        if (intent == null || attack.DamageCalc == null) { intent = null; continue; }
                        var damageCalc = attack.DamageCalc();
                        var terms = new Dictionary<int, CapturedTerms> { [Who.You] = SimReaders.Damage(me.RunState, combat, card: null, creature, me.Creature, damageCalc, MegaCrit.Sts2.Core.ValueProps.ValueProp.Move) };
                        for (var teammateIndex = 0; teammateIndex < mates.Count; teammateIndex++)
                            terms[Who.Teammate(teammateIndex)] = SimReaders.Damage(me.RunState, combat, card: null, creature, mates[teammateIndex].Creature, damageCalc, MegaCrit.Sts2.Core.ValueProps.ValueProp.Move);
                        for (var i = 0; i < Math.Max(1, attack.Repeats); i++) intent.Add(new IntentHit(damageCalc) { Captured = terms });
                    }
                    if (intent is { Count: > 0 }) enemy.Intent = intent;
                    moves.Add((enemy, creature.Monster, hitsPerAttack));
                }
                catch
                {
                    // intent unavailable: no attack
                }
            }
            state.Enemies.Add(enemy);
        }
        // Each enemy's move as its code runs it (decision #195): what it does before a later hit (Vulnerable on you, Strength to an ally),
        // read once every enemy has its id.
        foreach (var (enemy, monster, hitsPerAttack) in moves)
        {
            enemy.Move = MoveOf(monster, hitsPerAttack, IdOf, combat);
            // The monster's own state its move asks (Bowlbug Rock's IsOffBalance: decision #271), as the game has it now.
            if (enemy.Move != null) enemy.Fields = MoveConditions.StateFields(monster, enemy.Move, enemy.Fields);
            // The moves its powers' code can set it to (Steam Eruption's AboutToBlowState as the Waterfall Giant dies: decision #276).
            enemy.SetMoves = SetMovesOf(monster.Creature, IdOf, combat, targets);
        }

        // The history ledger (decision #191): the questions this board's models ask of the fight's log, each player's counts seeded here, once.
        state.History = HistoryLedger.QueriesFor(combat);
        FillPlayer(state.You, me, combat, IdOf);
        state.You.Fight = HistoryLedger.Seed(state.History, me, combat, ref state.You.Turn);
        foreach (var power in me.Creature.Powers) SimReaders.AddVars(state, Who.You, power);
        foreach (var relic in me.Relics) SimReaders.AddVars(state, Who.You, relic);
        FillAutoPlays(state.You, me, combat, ids);
        state.CostsUnknown = CostPassesNotRun(combat);
        FillDrawPile(state.You, me, combat, idMap, state.History, pileAutoPlays);
        state.You.BeaconOfHope = me.Creature.HasPower<BeaconOfHopePower>();
        FillSurvival(state.You, me);

        // Teammates (multiplayer): built like you (FillPlayer), in player order. Decision #25: their hand is
        // used too (their Ethereal exhausts can kill enemies); what's shown runs without it (SimState.WithoutTeammateHands).
        // Each enemy hit's amount on each teammate goes through the game's damage math for them.
        var players = combat.Players.ToList();
        var myIndex = players.IndexOf(me);
        var teamCreatures = new List<Creature>();
        for (var i = 0; i < players.Count; i++)
        {
            var other = players[i];
            if (!IsTeammate(other, me)) continue;
            var creature = other.Creature;
            var player = new SimPlayer();
            FillPlayer(player, other, combat, IdOf);
            player.Fight = HistoryLedger.Seed(state.History, other, combat, ref player.Turn);
            var mateId = Who.Teammate(state.Teammates.Count);
            foreach (var power in creature.Powers) SimReaders.AddVars(state, mateId, power);
            foreach (var relic in other.Relics) SimReaders.AddVars(state, mateId, relic);
            FillAutoPlays(player, other, combat, ids, mateId);
            FillSurvival(player, other); // their potions show in the game's expanded player view, their relics on screen
            player.BeaconOfHope = creature.HasPower<BeaconOfHopePower>();
            state.Teammates.Add(player);
            teamCreatures.Add(creature);
            if (i < myIndex) state.TeammatesBeforeMe++;
        }
        // The powers on the players' side whose next duration tick is skipped (PowerModel.SkipNextDurationTick: a debuff that came during the
        // enemies' turn keeps its stack through that turn's end, PowerCmd.TickDownDuration).
        foreach (var player in players)
            foreach (var creature in player.Creature.Pets.Prepend(player.Creature))
                if (IdOf(creature) is { } creatureId)
                    foreach (var power in creature.Powers)
                        if (power.SkipNextDurationTick) state.TickSkips = state.TickSkips.With(SimKeys.Owned(SimPowerName(power.GetType()), creatureId), amount: 1);
        if (teamCreatures.Count > 0)
        {
            foreach (var (creature, id) in ids)
            {
                if (!creature.IsAlive || creature.Monster == null || state.Enemy(id) is not { } enemy) continue;
                foreach (var teamCreature in teamCreatures) enemy.TeammateHits.Add(HitsOn(creature, teamCreature, me, combat) ?? enemy.Hits);
            }
        }
        return state;
    }

    /// <summary>
    /// A player's HP, and what keeps them alive at 0 (the Death Skull, Fairy in a Bottle and Lizard Tail), the same for every
    /// player (decision #93): the heal brings them from 0 to that much (CreatureCmd.Heal; the HP setter drops the fraction).
    /// </summary>
    private static void FillSurvival(SimPlayer simPlayer, Player player)
    {
        simPlayer.Hp = player.Creature.CurrentHp;
        simPlayer.MaxHp = player.Creature.MaxHp;
        var maxHp = player.Creature.MaxHp;
        simPlayer.Fairies = SafeInt(() => player.Potions.Count(CountsAsFairy));
        // The potions in their belt (on screen): a potion's use takes one (decision #313), which Belt Buckle's code counts.
        simPlayer.Potions = SafeInt(() => player.Potions.Count());
        simPlayer.PotionSlots = SafeInt(() => player.MaxPotionCount);
        simPlayer.FairyHeal = Math.Max(1, (int)(maxHp * 0.3m));
        simPlayer.LizardTailHeal = player.Relics.FirstOrDefault(relic => relic is LizardTail) is { IsUsedUp: false } tail
            ? Math.Max(1, (int)(maxHp * (Sts2State.Var(tail, "Heal", fallback: 50) / 100m)))
            : 0;
    }

    /// <summary>
    /// A card's cost before global modifiers (CardEnergyCost.GetWithModifiers(CostModifiers.Local): SimHandCard.LocalCost, decision #334); null for
    /// an X cost.
    /// </summary>
    internal static int? LocalCostOf(CardModel card) => card.EnergyCost.CostsX ? null : card.EnergyCost.GetWithModifiers(CostModifiers.Local);

    /// <summary>
    /// A card's star cost before the listeners' pass (CardModel.CurrentStarCost: SimHandCard.LocalStarCost, decision #386); null when it costs no stars
    /// (a negative star cost, none, or X stars: CostsEnergyOrStars counts neither).
    /// </summary>
    internal static int? LocalStarCostOf(CardModel card) => card.HasStarCostX || card.CurrentStarCost < 0 ? null : card.CurrentStarCost;

    /// <summary>
    /// Why a hand card's live cost isn't known on this board (SimState.CostsUnknown, decision #334): the first of the game's hook listeners
    /// (CombatState.IterateHookListeners) whose class has a cost pass (TryModifyEnergyCostInCombat, its Late pass, TryModifyStarCost: decision #386)
    /// the simulator doesn't run: a class not read whole (Tangled's, whose AfterApplied loop doesn't read yet), a card's own hook (Cho Limbs, Coda).
    /// Null when every one runs.
    /// </summary>
    internal static string? CostPassesNotRun(ICombatState combat)
    {
        try
        {
            foreach (var model in combat.IterateHookListeners())
            {
                var type = model.GetType();
                foreach (var pass in CostPassesOf(type))
                    if (SimModels.Get(type.Name) is not { } simModel || !simModel.Overrides(pass))
                        return $"{type.Name}'s {pass} changes what cards cost, and the simulator doesn't run it";
            }
            return null;
        }
        catch (Exception exception)
        {
            return $"the cost passes on the board couldn't be read ({exception.GetType().Name})";
        }
    }

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<Type, string[]> CostPasses = new();

    /// <summary>The cost passes a class overrides (its own or a base class's below AbstractModel).</summary>
    private static string[] CostPassesOf(Type type) => CostPasses.GetOrAdd(type, modelType =>
        new[] { nameof(SimModel.TryModifyEnergyCostInCombat), nameof(SimModel.TryModifyEnergyCostInCombatLate), nameof(SimModel.TryModifyStarCost) }
            .Where(pass => modelType.GetMethod(pass, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance) is { } method
                           && method.DeclaringType != typeof(AbstractModel))
            .ToArray());

    /// <summary>A potion SimPlayer.Fairies counts: one whose revive the simulator follows (Fairy in a Bottle).</summary>
    internal static bool CountsAsFairy(PotionModel potion) => potion.GetType().Name == "FairyInABottle";

    /// <summary>
    /// A player's end-of-turn auto-plays (the auto-play phase, before BeforeSideTurnEnd): Stampede and Simpleton pick from
    /// their hand, Howl from Beyond plays from their exhaust pile, and I Am Invincible from the top of their draw pile when
    /// that's certain (every card there is a copy of it). Auto-plays get no procs from the rules (Combat/CardPredictions).
    /// For a teammate (<paramref name="owner"/>) the same, with their hand's cards played as theirs (SimAdapter.Play with them as the
    /// owner): what the checks use (decision #25: everything that isn't a roll); what's shown leaves their hand out
    /// (SimState.WithoutTeammateHands) unless Count teammates' hands is on.
    /// </summary>
    private static void FillAutoPlays(SimPlayer player, Player me, ICombatState combat, IReadOnlyDictionary<Creature, int> ids, int owner = Who.You)
    {
        try
        {
            player.Stampede = me.Creature.GetPower<StampedePower>()?.Amount ?? 0;
            player.Simpleton = SimpletonOf?.Invoke(me.Creature) ?? 0;
            // The cards in your hand that auto-play cards of your piles (decision #360): what the board reads for them.
            var sources = owner == Who.You ? PileAutoPlaySources(me) : new List<PileAutoPlaySource>();
            var fromHand = sources.Any(source => source.Read.Pile == SimPile.Hand);
            // A card of yours upgrading every card in your hand (decision #363): each hand card's upgraded play read too (SimHandCard.Upgraded).
            var handUpgraded = owner == Who.You && PilesYourCardsUpgrade(me).HasFlag(UpgradedPiles.Hand);
            if (sources.Count > 0) player.PileAutoPlays = new SimPileAutoPlays(sources.Where(source => source.Readable).Select(source => source.Key).ToList()) { FromHand = fromHand };
            if (player.Stampede > 0 || player.Simpleton > 0 || fromHand)
            {
                player.AutoHand = new List<SimHandCard>();
                foreach (var card in Sts2State.Hand(me))
                {
                    if (card.Keywords.Contains(CardKeyword.Unplayable)) continue;
                    player.AutoHand.Add(new SimHandCard
                    {
                        Id = HandId(card),
                        Name = card.GetType().Name,
                        IsAttack = card.Type == CardType.Attack,
                        Cost = card.EnergyCost.CostsX ? 999999999 : card.EnergyCost.GetResolved(),
                        LocalCost = LocalCostOf(card), LocalStarCost = LocalStarCostOf(card), Card = SimReaders.CostedFacts(card),
                        Plays = AutoPlays(card, me, combat, ids, owner),
                        AutoPlayedBy = AutoPlayedBy(card, sources),
                        Upgraded = handUpgraded ? UpgradedHandCard(card, me, combat, ids, owner, sources) : null,
                    });
                }
            }
            foreach (var card in PileType.Exhaust.GetPile(me).Cards)
            {
                if (card is HowlFromBeyond) player.ExhaustAutoPlays.Add(AutoPlay(card, me, combat, ids, target: Who.None, owner: owner));
            }
            var drawPile = PileType.Draw.GetPile(me).Cards;
            if (drawPile.Count > 0 && drawPile.All(card => card is IAmInvincible))
            {
                player.InvincibleBlock = CardBlock(drawPile[0], combat, me);
                player.InvincibleDrawPile = drawPile.Count;
            }
        }
        catch
        {
            // unreadable: no auto-plays predicted (the forecast check skips a turn with unpredicted auto-plays)
            player.AutoHand = null;
            player.Stampede = player.Simpleton = 0;
            player.PileAutoPlays = null; // a card's auto-play of your piles' cards is unknown then (AutoPlayCardsAction: not read)
        }
    }

    /// <summary>
    /// A card in your hand whose play auto-plays cards of your piles (decision #360: CardAutoPlaysOf), with what it's read with: its own board and program,
    /// for its filters. Readable: the board can follow it; not when its filter lets an Unplayable card of your hand through (the hand's followed cards,
    /// AutoHand, are the playable ones), as Cho Chorus' tags alone would.
    /// </summary>
    internal sealed record PileAutoPlaySource(string Key, CardAutoPlayRead Read, CardModel Card, Procs.Programs.ProgramBoard Board, Procs.Programs.ModelProgram Program, bool Readable);

    /// <summary>
    /// A hand card as an auto-play picks it once upgraded (decision #363): its upgraded copy's cost, facts and plays, played as the card in your hand (its id);
    /// null when it can't be upgraded or its copy can't be read.
    /// </summary>
    private static SimHandCard? UpgradedHandCard(CardModel card, Player me, ICombatState combat, IReadOnlyDictionary<Creature, int> ids, int owner, IReadOnlyList<PileAutoPlaySource> sources)
    {
        if (!SafeBool(() => card.IsUpgradable)) return null;
        try
        {
            var copy = SpireQoL.Cards.UpgradedCopy.Of(card);
            var id = HandId(card);
            var plays = AutoPlays(copy, me, combat, ids, owner);
            foreach (var play in plays)
                if (play.HandCard == HandId(copy))
                {
                    play.HandCard = id;
                    if (play.Card != null) play.Card = play.Card with { Card = play.Card.Card with { HandId = id } };
                }
            return new SimHandCard
            {
                Id = id, Name = card.GetType().Name, IsAttack = copy.Type == CardType.Attack,
                Cost = copy.EnergyCost.CostsX ? 999999999 : copy.EnergyCost.GetResolved(),
                LocalCost = LocalCostOf(copy), LocalStarCost = LocalStarCostOf(copy), Card = SimReaders.CostedFacts(copy), Plays = plays, AutoPlayedBy = AutoPlayedBy(copy, sources),
            };
        }
        catch { return null; }
    }

    /// <summary>The auto-plays of <paramref name="cards"/>' plays that upgrade each card first on the board (Knife Trap+: its upgrade step's condition holds), by key.</summary>
    private static HashSet<string> CardAutoPlaysUpgrading(IEnumerable<CardModel> cards) =>
        cards.SelectMany(card => CardAutoPlaysOf(card.GetType())
                .Where(entry => entry.Upgrade is { } upgrade && Procs.Programs.ProgramEvaluator.Holds(upgrade.Condition ?? Procs.Programs.ProgramCondition.Always,
                    Procs.Programs.ProgramBoard.For(card), Procs.Programs.CardPrograms.Of(card.GetType())) == true)
                .Select(entry => entry.Key))
            .ToHashSet(StringComparer.Ordinal);

    /// <summary>Your hand's cards that auto-play cards of your piles, each auto-play once (by its key).</summary>
    internal static List<PileAutoPlaySource> PileAutoPlaySources(Player me)
    {
        var sources = new List<PileAutoPlaySource>();
        foreach (var card in Sts2State.Hand(me))
            foreach (var (key, read, _, _) in CardAutoPlaysOf(card.GetType()))
            {
                if (sources.Any(source => source.Key == key)) continue;
                var board = Procs.Programs.ProgramBoard.For(card);
                var program = Procs.Programs.CardPrograms.Of(card.GetType());
                var readable = read.Pile != SimPile.Hand || read.Filter == null
                    || Sts2State.Hand(me).Where(other => other.Keywords.Contains(CardKeyword.Unplayable)).All(other => Procs.Programs.ProgramEvaluator.Passes(read.Filter, other, board, program) == false);
                sources.Add(new PileAutoPlaySource(key, read, card, board, program, readable));
            }
        return sources;
    }

    /// <summary>
    /// The auto-plays of <paramref name="sources"/> whose filter lets <paramref name="card"/> through (its key; "/else" for a fallback's; with "?" when the
    /// filter can't be asked of it), sorted; null with none.
    /// </summary>
    internal static IReadOnlyList<string>? AutoPlayedBy(CardModel card, IReadOnlyList<PileAutoPlaySource> sources)
    {
        if (sources.Count == 0) return null;
        var keys = new List<string>();
        void Ask(string key, Procs.Programs.LambdaValue? filter, PileAutoPlaySource source)
        {
            switch (filter == null ? true : Procs.Programs.ProgramEvaluator.Passes(filter, card, source.Board, source.Program))
            {
                case true: keys.Add(key); break;
                case null: keys.Add(key + "?"); break;
            }
        }
        foreach (var source in sources)
        {
            Ask(source.Key, source.Read.Filter, source);
            if (source.Read.Fallback != null) Ask(source.Key + "/else", source.Read.Fallback, source);
        }
        return keys.Count == 0 ? null : keys.Order(StringComparer.Ordinal).ToList();
    }

    /// <summary>
    /// The creatures' ids of one board read (Build's) and that read's own draw profiles (decision #338): each card in your piles and its profile
    /// (FillDrawPile), what a play translated on this board names as the cards a take from your piles can take (TakeToHandAction), and with
    /// Reboot in your hand, your hand's cards as they join your draw pile (ShuffleDrawPileAction). The build's own, so a second build in a frame
    /// (a potion's own start) can't change what the plays translated on the first one read. Until its piles are read (a play the build itself
    /// translates: a hand card's auto-play, a pile card's play) it has none (ProfilesRead false).
    /// </summary>
    public sealed class BoardIds : Dictionary<Creature, int>
    {
        public IReadOnlyDictionary<CardModel, SimDrawCard> DrawProfiles { get; internal set; } = EmptyProfiles;
        public bool ProfilesRead { get; internal set; }
        /// <summary>A listener on the board changes where a played card goes (MovesPlayedCards): its hand cards' profiles are read, and each hand card's play carries its own.</summary>
        public bool PlayedCardsMove { get; internal set; }
    }

    private static readonly IReadOnlyDictionary<CardModel, SimDrawCard> EmptyProfiles = new Dictionary<CardModel, SimDrawCard>();

    /// <summary>The draw profiles of the board <paramref name="ids"/> were read with, or null where they aren't read (yet, or ids made by hand).</summary>
    internal static IReadOnlyDictionary<CardModel, SimDrawCard>? DrawProfilesOf(IReadOnlyDictionary<Creature, int> ids) =>
        ids is BoardIds { ProfilesRead: true } board ? board.DrawProfiles : null;

    /// <summary>
    /// Whether a drawn card's being a Status changes what the board does (decision #195, round five): a question to the fight's log the board
    /// asks counts the Statuses drawn (the history ledger's, read from every card, power and relic: Iteration's), or a hook of a model of yours
    /// read from its code tests the drawn card's type. A rule from what's asked, never a power's name.
    /// </summary>
    internal static bool AsksDrawnStatus(IEnumerable<HistoryQuery> questions, IEnumerable<AbstractModel> models) =>
        questions.Any(question => question.Kind == HistoryKind.CardDrawn && question.CardType == SimCardType.Status)
        || models.Any(model => SimModels.Get(SimReaders.ModelName(model)) is ReadModel read && read.Spec.Of(HookName.AfterCardDrawn) is { } drawn
            && new ModelSpec(read.Name, read.Spec.Kind, new[] { drawn }).Values().Any(value => value is HookFactValue { Fact: HookFact.CardType }));

    /// <summary>
    /// Whether a drawn card's being Ethereal changes what the board does: a hook of a model of yours read from its code tests the drawn card's
    /// Ethereal (Pagestorm's `card.Keywords.Contains(CardKeyword.Ethereal)`). A rule from what's asked, never a power's name.
    /// </summary>
    internal static bool AsksDrawnEthereal(IEnumerable<AbstractModel> models) =>
        models.Any(model => SimModels.Get(SimReaders.ModelName(model)) is ReadModel read && read.Spec.Of(HookName.AfterCardDrawn) is { } drawn
            && new ModelSpec(read.Name, read.Spec.Kind, new[] { drawn }).Values().Any(value => value is HookFactValue { Fact: HookFact.CardEthereal }));

    /// <summary>The facts of a card a read hook can ask of the card it's given (a drawn card, in a draw hook).</summary>
    private static readonly HashSet<HookFact> CardFacts = new()
    {
        HookFact.CardType, HookFact.CardEthereal, HookFact.CardUpgradable, HookFact.CardAffliction, HookFact.CardTag, HookFact.CardEnergy, HookFact.CardIs,
        HookFact.CardGainsBlock, HookFact.CardEnchanted, HookFact.EnchantmentGainsBlock,
    };

    /// <summary>
    /// What a model read from its code asks of a drawn card in its draw hooks (AfterCardDrawn, AfterCardDrawnEarly) that the draw profile doesn't keep
    /// (decision #90), or null when the profile keeps all it asks: nothing (any card: a count), its being a Status (its type tested against Status, or a
    /// question to the fight's log counting the Statuses drawn: AsksDrawnStatus), or its Ethereal (AsksDrawnEthereal). What a class's reading gives in
    /// place of a review by its name (DrawReactions, decision #281).
    /// </summary>
    internal static string? DrawnCardNotKept(ModelSpec spec)
    {
        var hooks = spec.Hooks.Where(program => program.Hook is HookName.AfterCardDrawn).ToList();
        if (hooks.Count == 0) return null;
        var values = new ModelSpec(spec.Name, spec.Kind, hooks).Values().ToList();
        // Its type compared with Status (Iteration's card.Type == CardType.Status) is the profile's Status; any other use of its type isn't kept.
        static bool IsType(HookValue value) => value is HookFactValue { Fact: HookFact.CardType };
        static bool IsStatus(HookValue value) => value is HookConstant constant && constant.Value == (int)SimCardType.Status;
        var statusTests = values.OfType<HookMath>().Count(math => math.Operation is HookOperation.Equal or HookOperation.NotEqual
            && (IsType(math.Left) && IsStatus(math.Right) || IsStatus(math.Left) && IsType(math.Right)));
        var typesAsked = values.Count(IsType);
        if (typesAsked > statusTests) return "its type, other than whether it's a Status";
        var unkept = values.OfType<HookFactValue>().Where(fact => CardFacts.Contains(fact.Fact) && fact.Fact is not (HookFact.CardType or HookFact.CardEthereal)).Select(fact => fact.Fact.ToString()).Distinct().ToList();
        if (unkept.Count > 0) return string.Join(", ", unkept);
        var questions = values.OfType<HookHistory>().Where(history => history.Query.Kind == HistoryKind.CardDrawn && history.Query.CardType is not (SimCardType.None or SimCardType.Status)).ToList();
        return questions.Count > 0 ? "a question to the fight's log about the cards drawn of a type other than Status" : null;
    }

    /// <summary>
    /// Your draw and discard piles by profile (decision #90): what about each card matters if you draw it on this board, never
    /// their order (hidden; only the cards known to be on top would be: none yet). What matters: acting in hand at end of
    /// turn (always), exhausting as Ethereal (always), a Status or Ethereal when the board asks it (AsksDrawnStatus, AsksDrawnEthereal), a Strike with
    /// Hellraiser, and with Stampede or Simpleton the card itself (how it plays). Everything else is one plain profile.
    /// </summary>
    private static void FillDrawPile(SimPlayer player, Player me, ICombatState combat, BoardIds ids, IReadOnlyList<HistoryQuery> questions, bool pileAutoPlays)
    {
        try
        {
            // A drawn card's being a Status matters when something on the board asks it (Iteration's count of the Statuses drawn this turn).
            var statusAsked = AsksDrawnStatus(questions, me.Creature.Powers.Cast<AbstractModel>().Concat(me.Relics));
            var etherealAsked = AsksDrawnEthereal(me.Creature.Powers.Cast<AbstractModel>().Concat(me.Relics));
            var hellraiser = me.Creature.HasPower<HellraiserPower>();
            var picks = player.AutoHand != null;
            // A card in your hand that acts on the cards it draws (decision #118): what it reads goes into the profile.
            var readers = PileType.Hand.GetPile(me).Cards.Select(card => DrawnCardReaderOf(card.GetType())).OfType<DrawnCardReader>().ToList();
            var readsType = readers.Any(drawnCardReader => drawnCardReader.What == DrawnCardReader.Reads.Type);
            var readsCost = readers.Any(drawnCardReader => drawnCardReader.What == DrawnCardReader.Reads.Cost);
            var autoByKey = new Dictionary<string, SimHandCard?>();
            // Something that auto-plays the top of your draw pile (Havoc, Cascade, The Devil in your hand; Lost Lute's power): how each
            // card plays from there goes into its profile, and which card it is.
            var handCards = PileType.Hand.GetPile(me).Cards;
            // A card in your hand that auto-plays cards of your piles (decision #360): each card where it takes from has its filters asked (AutoPlayedBy) and
            // its play from there built; one in your hand, its play from your hand.
            var sources = player.PileAutoPlays == null ? new List<PileAutoPlaySource>() : PileAutoPlaySources(me);
            var handSources = sources.Where(source => source.Read.Pile == SimPile.Hand).ToList();
            var topPlays = pileAutoPlays || handCards.Any(card => Procs.Programs.CardEffects.AutoPlaysFromDrawPile(card.GetType()) || card.GetType().Name == nameof(LostLute))
                           || me.Creature.Powers.Any(power => power.GetType().Name == nameof(LostLutePower))
                           || sources.Any(source => source.Read.Pile is SimPile.Draw or SimPile.Discard);
            var upgradingSources = CardAutoPlaysUpgrading(handCards);
            var topUpgrades = handCards.Any(card => Procs.Programs.BoardCounts.AutoPlaysOf(card) is { Upgrade: true })
                              || sources.Any(source => source.Read.Pile is SimPile.Draw or SimPile.Discard && upgradingSources.Contains(source.Key));
            var exhaustPlays = sources.Any(source => source.Read.Pile == SimPile.Exhaust);
            var exhaustUpgrades = sources.Any(source => source.Read.Pile == SimPile.Exhaust && upgradingSources.Contains(source.Key));
            // A card of yours that upgrades every card of your hand, draw or discard pile (decision #363): each profile an upgrade could change gets its upgraded
            // one (SimDrawCard.UpgradedTo), read from the card's upgraded copy.
            var everyUpgraded = PilesYourCardsUpgrade(me);
            var fromPileByKey = new Dictionary<string, (SimHandCard? Plain, SimHandCard? Upgraded)>();
            // The filtered takes of the cards in your hand (decision #189): a card drawn or taken into your hand during a play counts for
            // the ones it would be taken by (Second Wind's non-Attacks, The Hermit's Curse).
            var takeFilters = handCards.SelectMany(TakeFiltersOf).ToList();
            SimDrawCard Profile(CardModel card, bool exhausted = false, CardModel? original = null)
            {
                var turnEnd = TurnEndOf(card);
                var fading = IsFading(card, combat);
                var hexFading = fading && EtherealOnlyFromHex(card);
                var status = statusAsked && card.Type == CardType.Status;
                var ethereal = etherealAsked && card.Keywords.Contains(CardKeyword.Ethereal);
                var strike = hellraiser && card.Tags.Contains(CardTag.Strike);
                // Hellraiser's auto-play is built like a picked card's (decision #121), except past the game's cap on endless
                // auto-plays, when every enemy you could hit can't die (HellraiserPower: 9 a turn): then it isn't simulated.
                var hellraised = strike && !me.Creature.CombatState!.HittableEnemies.All(creature => creature.HpDisplay.IsInfinite());
                // What a card in your hand that auto-plays your piles' cards (decision #360) lets through: an Unplayable card a hand one lets through is followed
                // no further once drawn (its play isn't built: the hand's later auto-plays are unknown).
                var autoPlayedBy = AutoPlayedBy(card, sources);
                var unplayable = card.Keywords.Contains(CardKeyword.Unplayable);
                var pickable = picks && (!unplayable && (player.Simpleton > 0 || card.Type == CardType.Attack || handSources.Count > 0)
                                         || unplayable && handSources.Any(source => autoPlayedBy?.Contains(source.Key) == true || autoPlayedBy?.Contains(source.Key + "?") == true));
                var playsFromPile = topPlays || exhausted && exhaustPlays;
                var cost = card.EnergyCost.CostsX ? 999999999 : card.EnergyCost.GetResolved();
                var who = pickable || strike || playsFromPile ? $"{card.GetType().Name}+{card.CurrentUpgradeLevel}/{card.Enchantment?.GetType().Name}/{cost}{(LocalStarCostOf(card) is { } stars ? "*" + stars : "")}" : "";
                // I Am Invincible plays from the top of the draw pile at the end of your turn: its block.
                var invincible = card is IAmInvincible ? CardBlock(card, combat, me) : 0;
                var perfectFit = SimReaders.OnTopWhenShuffled(card);
                var type = readsType ? SimReaders.TypeOf(card) : SimCardType.None;
                bool? free = readsCost ? !card.EnergyCost.CostsX && card.EnergyCost.GetWithModifiers(CostModifiers.All) == 0 : null;
                // What it costs once drawn, worked out in your hand as the state stands then (decision #381): its cost before global modifiers and the
                // facts the cost passes test (its type, Ethereal, affliction: no pass reads a card's class, so cards that cost alike stay one profile).
                var localCost = readsCost ? LocalCostOf(card) : null;
                var costed = localCost != null ? SimReaders.CostedFacts(card) with { Name = "" } : null;
                var sly = readsCost && card.Keywords.Contains(CardKeyword.Sly);
                // It acts when it's exhausted (Drum of Battle): its type goes with it, for an exhaust of a kind.
                var exhaustEnergy = CardEnergy.WhenExhausted(card);
                var energyLost = CardEnergy.LostWhenDrawn(card);
                var takenBy = takeFilters.Count == 0 ? null : takeFilters.Where(filter => filter.Takes(card, false) != false).Select(filter => filter.Key).ToList() is { Count: > 0 } keys ? keys : null;
                var key = $"{who}|{turnEnd}|{fading}{hexFading}{status}{ethereal}{strike}|{invincible}|{perfectFit}|{type}|{free}{sly}{localCost}{(costed == null ? "" : "/" + costed.Type + "/" + costed.Ethereal + "/" + costed.Affliction)}|{exhaustEnergy}{(exhaustEnergy > 0 ? SimReaders.TypeOf(card) : SimCardType.None)}|{energyLost}|{(takenBy == null ? "" : string.Join(",", takenBy))}{(autoPlayedBy == null ? "" : "|" + string.Join(",", autoPlayedBy))}";
                SimHandCard? auto = null;
                if ((pickable && !unplayable || hellraised) && !autoByKey.TryGetValue(key, out auto))
                {
                    // How it plays once in your hand: as the adapter builds a hand card's auto-plays. A card in the draw pile counts as
                    // not in your hand there (its hand delta 0, no hand card): played once drawn, it's from your hand, so one less, and
                    // its plays are its own (each copy drawn takes them as its own: SimHandCard.CopyWithId).
                    var plays = AutoPlays(card, me, combat, ids);
                    var id = HandId(original ?? card);
                    // An upgraded copy plays as the card it stands for (its id in your hand).
                    if (original != null) foreach (var play in plays) if (play.HandCard == HandId(card)) play.HandCard = id;
                    if (card.Pile?.Type == PileType.Draw) foreach (var play in plays) { play.HandDelta -= 1; play.HandCard = id; }
                    auto = new SimHandCard { Id = id, Name = card.GetType().Name, IsAttack = card.Type == CardType.Attack, Cost = cost, LocalCost = LocalCostOf(card), LocalStarCost = LocalStarCostOf(card),
                        Card = SimReaders.CostedFacts(card), Plays = plays, AutoPlayedBy = autoPlayedBy };
                    autoByKey[key] = auto;
                }
                (SimHandCard? Plain, SimHandCard? Upgraded) fromPile = default;
                if (playsFromPile && !fromPileByKey.TryGetValue(key + (exhausted ? "|exhausted" : ""), out fromPile))
                {
                    fromPile = (FromPile(card, card, me, combat, ids), topUpgrades || exhausted && exhaustUpgrades ? UpgradedFromPile(card, me, combat, ids) : null);
                    fromPileByKey[key + (exhausted ? "|exhausted" : "")] = fromPile;
                }
                var drawCard = new SimDrawCard(key)
                {
                    FromPile = fromPile.Plain, FromPileUpgraded = fromPile.Upgraded,
                    EnergyLostOnDraw = energyLost,
                    Exhausting = exhaustEnergy > 0 ? new ExhaustingCard(HandCard: 0, exhaustEnergy, SimReaders.TypeOf(card)) : null,
                    TurnEnd = turnEnd, Fading = fading, HexFading = hexFading, Status = status, Ethereal = ethereal, Strike = strike,
                    Pickable = pickable, Auto = auto, InvincibleBlock = invincible, OnTopWhenShuffled = perfectFit, Type = type, Free = free, LocalCost = localCost, Costed = costed, Sly = sly, TakenBy = takenBy, AutoPlayedBy = autoPlayedBy,
                };
                // One an upgrade could change (something about it is followed): an upgrade of its pile or its hand makes what it does unknown, unless its upgraded
                // profile is read (decision #363: a card of yours upgrades every card there).
                if (drawCard.Plain || !Safe(() => card.IsUpgradable)) return drawCard;
                drawCard = drawCard with { Key = key + "|upgradable", Upgradable = true };
                if (original != null || everyUpgraded == UpgradedPiles.None) return drawCard;
                try
                {
                    var upgraded = Profile(SpireQoL.Cards.UpgradedCopy.Of(card), exhausted, original: card);
                    if (auto != null) auto.Upgraded = upgraded.Auto;
                    return drawCard with { UpgradedTo = upgraded, TurnEnd = drawCard.TurnEnd is { } acts ? acts with { Upgraded = upgraded.TurnEnd } : null };
                }
                catch { return drawCard; }
            }
            var profiles = new Dictionary<CardModel, SimDrawCard>(ReferenceEqualityComparer.Instance);
            SimDrawCard Remembered(CardModel card)
            {
                var profile = Profile(card);
                profiles[card] = profile;
                return profile;
            }
            List<SimDrawGroup> Groups(IEnumerable<CardModel> cards) => cards.Select(Remembered).GroupBy(drawn => drawn.Key)
                .Select(sameProfile => new SimDrawGroup(sameProfile.First(), sameProfile.Count())).OrderBy(group => group.Card.Key, StringComparer.Ordinal).ToList();
            // The cards known to be on top (placed there: decision #91), in order; the rest as a set.
            var known = SpireQoL.Combat.DrawPileMemory.KnownTop.Where(card => card.Pile?.Type == PileType.Draw && card.Owner == me).ToList();
            player.Draw = new SimDrawPile
            {
                Top = known.Select(Remembered).ToList(),
                Rest = Groups(PileType.Draw.GetPile(me).Cards.Where(card => !known.Contains(card))),
                Discard = Groups(PileType.Discard.GetPile(me).Cards),
            };
            // The cards in your hand too, for a play that puts them into your draw pile (Reboot), or a listener that sends a card played from your hand
            // there (its ModifyCardPlayResultLocation: Rebound's, Nostalgia's top of the draw pile): each hand card's play, and its auto-plays, carry it.
            var movesPlayed = MovesPlayedCards(me, combat);
            if (movesPlayed || handCards.Any(card => Procs.Programs.CardEffects.ShufflesDiscardPileIn(card.GetType())))
                foreach (var card in handCards) Remembered(card);
            if (movesPlayed && player.AutoHand != null)
                foreach (var card in handCards)
                    if (profiles.TryGetValue(card, out var handProfile) && player.AutoHand.Find(handCard => handCard.Id == HandId(card)) is { } picked)
                    {
                        foreach (var play in picked.Plays) if (play.HandCard == picked.Id) play.PileProfile = handProfile;
                        // Its upgraded plays as its upgraded profile (decision #363), where that's read.
                        if (picked.Upgraded != null && handProfile.UpgradedTo is { } upgradedProfile)
                            foreach (var play in picked.Upgraded.Plays) if (play.HandCard == picked.Id) play.PileProfile = upgradedProfile;
                    }
            // Your exhaust pile's cards in its order, for a card in your hand that auto-plays cards from there (decision #360: Eidolon, Knife Trap).
            if (exhaustPlays && player.PileAutoPlays != null)
                player.PileAutoPlays = player.PileAutoPlays with { ExhaustCards = PileType.Exhaust.GetPile(me).Cards.Select(card => Profile(card, exhausted: true)).ToList() };
            ids.DrawProfiles = profiles;
            ids.ProfilesRead = true;
            ids.PlayedCardsMove = movesPlayed;
            // Cards something on this board makes into a draw pile (its code's MakeIntoDrawStep: Personal Hive's Dazed, Biiig Hug's Soot): their
            // profiles, from the game's own card, for a draw later in the same simulation.
            var made = new Dictionary<string, SimDrawCard>();
            void Made(string cardClass)
            {
                if (made.ContainsKey(cardClass)) return;
                try
                {
                    if (Procs.Programs.MadeCards.CanonicalOf(cardClass) is not { } card) return;
                    made[cardClass] = Profile(card);
                    // It listens from your piles once made (none of it there yet).
                    if (SimReaders.CardListenerOf(card) is { } listener) player.Cards = player.Cards.Add(listener, delta: 0);
                }
                catch { /* not built: a draw of it is unknown */ }
            }
            void MadeBy(AbstractModel model)
            {
                if (SimModels.Get(SimReaders.ModelName(model)) is ReadModel { CardsMadeIntoDraw: { Count: > 0 } cards }) foreach (var cardClass in cards) Made(cardClass);
                // And into your hand (decision #364: Withering Presence's Wither): what each does there as the turn ends.
                if (SimModels.Get(SimReaders.ModelName(model)) is { CardsMadeIntoHand: { Count: > 0 } intoHand }) foreach (var cardClass in intoHand) Made(cardClass);
            }
            // Your draw pile's: what an enemy's powers, your powers and your relics make into it.
            foreach (var creature in combat.Enemies.Append(me.Creature))
                foreach (var power in creature.Powers) MadeBy(power);
            foreach (var relic in me.Relics) MadeBy(relic);
            // The cards your hand's cards make into your hand: into a full one they go to your discard pile (CardEventAction.MadeCard).
            foreach (var maker in handCards)
                if (MadeCardOf(maker) is { } madeByPlay && !made.ContainsKey(madeByPlay.GetType().Name))
                {
                    try { made[madeByPlay.GetType().Name] = Profile(madeByPlay); }
                    catch { /* not built: it isn't followed into the pile */ }
                }
            player.MadeCards = made.Count > 0 ? made : null;
        }
        catch
        {
            player.Draw = null; // unreadable: draws are only counted, as before
        }
    }

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<Type, DrawnCardReader?> DrawnReaders = new();

    /// <summary>
    /// How a card acts on the cards its own draw gives (decision #118), read from its program (decision #182): a test of the result of
    /// its CardPileCmd.Draw on a later step. A loop going round while the drawn card is of a type, drawing again (Pillage: Attacks); its
    /// block only when the drawn card is of a type (Escape Plan: a Skill); a discard of the drawn cards its filter on their cost keeps
    /// (Scrape: those that cost energy). The draw profile carries what it reads while one is in your hand, and the play runs as a
    /// DrawnCardAction. Null for a card that doesn't act on what it draws. DrawScanTests (catalog tests) finds every card that uses what it
    /// draws, so a new one whose reaction isn't one of these is caught.
    /// </summary>
    internal static DrawnCardReader? DrawnCardReaderOf(Type cardType) => DrawnReaders.GetOrAdd(cardType, type =>
    {
        var program = Procs.Programs.CardPrograms.Of(type);
        static bool OnDrawn(Procs.Programs.ProgramValue value) => value.Parts().Any(part => part is Procs.Programs.ResultValue { Command: "CardPileCmd.Draw" });
        // A test that the drawn card is of a type (Type == T holding, or Type != T failing).
        static SimCardType? TypeTested(Procs.Programs.ProgramCondition? condition) => condition?.Literals
            .Where(literal => literal.Test is Procs.Programs.CompareValue { Left: Procs.Programs.QueryValue { Name: "Type" } tested, Right: Procs.Programs.ConstantValue } compare
                              && OnDrawn(tested) && (compare.Op == Procs.Programs.Comparison.Equal) == literal.Holds
                              && compare.Op is Procs.Programs.Comparison.Equal or Procs.Programs.Comparison.NotEqual)
            .Select(literal => (Procs.Programs.CompareValue)literal.Test).FirstOrDefault()
            is { Right: Procs.Programs.ConstantValue { Value: var cardType } } && Enum.TryParse<SimCardType>(((CardType)cardType).ToString(), out var simType) ? simType : null;
        foreach (var step in program.All())
        {
            switch (step)
            {
                case Procs.Programs.LoopStep loop when loop.While?.Literals.Any(literal => OnDrawn(literal.Test)) == true
                                                     && new Procs.Programs.ModelProgram("", loop.Body).Commands.Any(command => command.Command is { DeclaringType: "CardPileCmd", Name: "Draw" })
                                                     && TypeTested(loop.While) is { } again:
                    return new DrawnCardReader(DrawnCardReader.Reads.Type, $"draws again while the card it draws is a {again} and your hand has room", DrawsAgain: true, Counts: true)
                        { Type = again };
                case Procs.Programs.CommandStep { Command: { DeclaringType: "CreatureCmd", Name: "GainBlock" } } gain when TypeTested(gain.Condition) is { } blockIf:
                    return new DrawnCardReader(DrawnCardReader.Reads.Type, $"its block only when the card it draws is a {blockIf}") { Type = blockIf, BlockIf = true };
                case Procs.Programs.CommandStep { Command: { DeclaringType: "CardCmd", Name: "Discard" } } discard
                    when discard.Values.Count > 1 && Unstored(discard.Values[1]) is Procs.Programs.QueryValue { Name: "Where", Arguments: [var drawn, Procs.Programs.LambdaValue filter] } && OnDrawn(drawn)
                         && filter.Parts().Any(part => part is Procs.Programs.QueryValue { Name: "CostsX" or "GetWithModifiers" }):
                    return new DrawnCardReader(DrawnCardReader.Reads.Cost, "discards the cards it draws unless they cost 0 (an X cost counts as not 0)", Counts: true);
            }
        }
        return null;
    });

    private static Procs.Programs.ProgramValue Unstored(Procs.Programs.ProgramValue value) => value is Procs.Programs.StoredValue stored ? Unstored(stored.Value) : value;

    /// <summary>
    /// What a card reads about the cards it draws (DrawnCardReaderOf). DrawsAgain: how many it draws depends on them. Counts: its chance
    /// tab shows the most likely number (Attacks drawn, cards drawn that cost 0) with its chance, not just the chance its effect happens
    /// (decision #119).
    /// </summary>
    internal sealed record DrawnCardReader(DrawnCardReader.Reads What, string Note, bool DrawsAgain = false, bool Counts = false)
    {
        internal enum Reads { Type, Cost }

        /// <summary>The card type its reaction tests (Pillage: Attack; Escape Plan: Skill); None for a test of the cost.</summary>
        public SimCardType Type { get; init; }

        /// <summary>Its block only when the drawn card is of <see cref="Type"/> (Escape Plan).</summary>
        public bool BlockIf { get; init; }
    }

    /// <summary>
    /// Every game class that reacts to a card being drawn (AfterCardDrawn, AfterCardDrawnEarly) and isn't read from its code, reviewed for the
    /// draw profile (decision #90): what about the drawn card it reads, and how the simulator covers it. One read from its code is covered by what
    /// its reading asks of the drawn card (DrawnCardNotKept, decision #281). DrawScanTests (catalog tests) fails a class that's neither, so a new
    /// draw reaction can't merge cards the profile should keep apart.
    /// </summary>
    internal static readonly IReadOnlyDictionary<string, string> DrawReactions = new Dictionary<string, string>
    {
        [nameof(HellraiserPower)] = "a Strike (the profile's Strike, with Hellraiser): its auto-play isn't simulated; later auto-plays aren't predicted",
        [nameof(ChainsOfBindingPower)] = "afflicts drawn cards Bound (playability, read live; nothing at end of turn)",
        [nameof(ConfusedPower)] = "a random cost (costs matter to Simpleton only: read live in hand; a drawn card keeps the cost it had)",
        [nameof(KinglyKick)] = "its own cost on draw (Simpleton: the cost it had in the pile)",
        [nameof(KinglyPunch)] = "its own cost on draw (Simpleton: the cost it had in the pile)",
        [nameof(MegaCrit.Sts2.Core.Models.Cards.Void)] = "itself: loses energy when drawn (the profile's EnergyLostOnDraw; model VoidCard, after your powers and relics)",
        ["Slither"] = "a random cost on draw (Simpleton: the cost it had in the pile)",
        ["MockRemoveDrawnCardsFromCombatPower"] = "the game's test mock: never in a real fight",
    };

    /// <summary>
    /// The piles a card of yours upgrades every card of when played or used (decision #363: CardCmd.Upgrade of each card of your hand, draw or discard pile,
    /// not random ones; the cards in your hand and the potions in your belt), read from their code: what the board reads each card's upgraded copy for.
    /// </summary>
    internal static UpgradedPiles PilesYourCardsUpgrade(Player me)
    {
        try
        {
            var models = Sts2State.Hand(me).Cast<AbstractModel>().Concat(me.Potions);
            return models.Aggregate(UpgradedPiles.None, (piles, model) => piles | PilesEveryCardUpgraded(model.GetType()));
        }
        catch { return UpgradedPiles.None; }
    }

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<Type, UpgradedPiles> EveryCardUpgraded = new();

    /// <summary>The piles a card's or potion's code upgrades every card of (UpgradeTargetOf's piles, not a roll's: UpgradesRandomCards).</summary>
    internal static UpgradedPiles PilesEveryCardUpgraded(Type type) => EveryCardUpgraded.GetOrAdd(type, modelType =>
    {
        var program = Procs.Programs.CardPrograms.Of(modelType);
        var piles = UpgradedPiles.None;
        foreach (var command in program.All().OfType<Procs.Programs.CommandStep>().Where(step => step.Command.FullName == "CardCmd.Upgrade"))
            if (UpgradeTargetOf(command, program) is { Target: UpgradeTarget.Piles, Piles: var upgraded } && !UpgradesRandomCards(command, program)) piles |= upgraded;
        return piles;
    });

    /// <summary>
    /// A card in your hand whose upgrade is read for what it does as the turn ends (decision #363): one acting in hand then whose upgraded copy does too
    /// (TurnEndCard.Upgraded), or an Ethereal one whose upgraded copy still is.
    /// </summary>
    private static bool UpgradeReadAtTurnEnd(CardModel card, ICombatState combat)
    {
        try
        {
            var copy = SpireQoL.Cards.UpgradedCopy.Of(card);
            if (TurnEndOf(card) != null && TurnEndOf(copy)?.Card != card.GetType().Name) return false;
            return !IsFading(card, combat) || copy.Keywords.Contains(CardKeyword.Ethereal);
        }
        catch { return false; }
    }

    /// <summary>The card's turn-end action once upgraded (its upgraded copy's), or null where it can't be read or it can't be upgraded.</summary>
    private static TurnEndCard? UpgradedTurnEnd(CardModel card)
    {
        try { return SafeBool(() => card.IsUpgradable) ? TurnEndOf(SpireQoL.Cards.UpgradedCopy.Of(card)) : null; }
        catch { return null; }
    }

    /// <summary>A card that acts in hand at the end of your turn (SimEngine.HandAtTurnEnd), with its amount; null otherwise.</summary>
    private static TurnEndCard? TurnEndOf(CardModel card)
    {
        if (!SimEngine.HandAtTurnEnd.TryGetValue(card.GetType().Name, out var kind)) return null;
        var amount = kind switch { SimEngine.InHand.Damage => Sts2State.Var(card, "Damage"), SimEngine.InHand.HpLoss => Sts2State.Var(card, "HpLoss"), _ => 0 };
        return new TurnEndCard(card.GetType().Name, SafeCardTitle(card), amount) { Source = SimReaders.Card(card, energy: 0) };
    }

    /// <summary>Which card in your hand a play is (SimPlay.HandCard, SimHandCard.Id): its object identity.</summary>
    public static int HandId(CardModel card) => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(card) | 1;


    /// <summary>
    /// How <paramref name="played"/> plays when a card auto-plays it from your draw pile (SimDrawCard.FromPile): as an auto-play of a
    /// card that isn't in your hand (its hand delta its own, no hand card). <paramref name="pileCard"/> is the card in your pile (the
    /// same, or the one an upgraded copy stands for). No plays for an Unplayable card (it goes to the discard pile unplayed). Null when
    /// it can't be built. Where it goes after is its play's result location, worked out in the simulation (SimEngine.PlayFromPile).
    /// </summary>
    private static SimHandCard? FromPile(CardModel played, CardModel pileCard, Player me, ICombatState combat, IReadOnlyDictionary<Creature, int> ids)
    {
        try
        {
            var plays = played.Keywords.Contains(CardKeyword.Unplayable) ? new List<SimPlay>() : AutoPlays(played, me, combat, ids);
            // Built for a card leaving your hand unless it's in your draw pile (SimPlay's HandDelta): it isn't in your hand.
            if (played.Pile?.Type != PileType.Draw) foreach (var play in plays) play.HandDelta += 1;
            var cost = played.EnergyCost.CostsX ? 999999999 : played.EnergyCost.GetResolved();
            return new SimHandCard { Id = 0, Name = pileCard.GetType().Name, IsAttack = played.Type == CardType.Attack, Cost = cost, Plays = plays };
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// FromPile for the card upgraded first (The Devil+: CardCmd.Upgrade before each auto-play), from an upgraded copy made the way the
    /// game makes its upgrade previews (MutableClone, UpgradeInternal); the card itself when it can't be upgraded further (the game's
    /// upgrade does nothing then). Null when the copy can't be made or played.
    /// </summary>
    private static SimHandCard? UpgradedFromPile(CardModel card, Player me, ICombatState combat, IReadOnlyDictionary<Creature, int> ids)
    {
        if (!card.IsUpgradable) return FromPile(card, card, me, combat, ids);
        try
        {
            var copy = SpireQoL.Cards.UpgradedCopy.Of(card);
            return FromPile(copy, card, me, combat, ids);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// A hand card's auto-plays: one per hittable enemy for a card aimed at one (CardCmd.AutoPlay picks one at random),
    /// else one. A card that draws or makes cards plays too (its hits land; the simulator stops predicting later auto-plays,
    /// whose hand it changed). None when it aims at a teammate.
    /// </summary>
    private static List<SimPlay> AutoPlays(CardModel card, Player me, ICombatState combat, IReadOnlyDictionary<Creature, int> ids, int owner = Who.You)
    {
        if (card.TargetType == TargetType.AnyAlly) return new List<SimPlay>();
        if (card.TargetType != TargetType.AnyEnemy) return new List<SimPlay> { AutoPlay(card, me, combat, ids, target: Who.None, owner) };
        return ids.Where(entry => entry.Key.IsAlive && entry.Key.IsHittable).Select(entry => AutoPlay(card, me, combat, ids, entry.Value, owner)).ToList();
    }

    private static SimPlay AutoPlay(CardModel card, Player me, ICombatState combat, IReadOnlyDictionary<Creature, int> ids, int target, int owner = Who.You)
    {
        var play = Play(card, me, combat, ids, target, owner);
        play.AutoPlay = true;
        if (play.Card != null) play.Card = play.Card with { AutoPlay = true };
        // It costs nothing (CardCmd.AutoPlay: no SpendResources, its ResourceInfo's EnergySpent and StarsSpent 0; decision #361): none of its energy or
        // stars is spent, and nothing that reacts to a spend hears of one.
        play.Before.RemoveAll(action => action is CardEventAction { Event: CardEvent.EnergySpent });
        play.Body.RemoveAll(action => action is CardEventAction { Event: CardEvent.StarsSpent, FirstPlayOnly: true });
        return play;
    }


    /// <summary>The creature's Simpleton (it auto-plays your cheapest cards): beta only (decision #163); main has no Simpleton.</summary>
    [BetaOnly, System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static int SimpletonAmount(Creature creature) => creature.GetPower<SimpletonPower>()?.Amount ?? 0;

    internal static readonly Func<Creature, int>? SimpletonOf = BetaOnly.Gate<Func<Creature, int>>(SimpletonAmount);

    /// <summary>
    /// Teammates are simulated (the "Multiplayer predictions (experimental)" setting, read each frame).
    /// Off: none are, so every prediction is for you alone.
    /// </summary>
    internal static bool IncludeTeammates = true;

    /// <summary>Another living player in the fight: a simulated teammate (SimState.Teammates, in this order).</summary>
    internal static bool IsTeammate(Player player, Player me) => IncludeTeammates && player != me && player.Creature is { IsAlive: true } && player.PlayerCombatState != null;

    /// <summary>The simulated teammates, in player order (SimState.Teammates[k] is the k-th).</summary>
    public static List<Player> Teammates(Player me, ICombatState combat) => combat.Players.Where(player => IsTeammate(player, me)).ToList();

    /// <summary>
    /// A player's state for the simulator: block, powers, relics, hand, orbs, Osty. The same for you and
    /// for each teammate (multiplayer), so a teammate is modelled exactly like you.
    /// </summary>
    private static void FillPlayer(SimPlayer player, Player me, ICombatState combat, Func<Creature, int?> idOf)
    {
        var creature = me.Creature;
        var pcs = me.PlayerCombatState!;
        var hand = Sts2State.Hand(me);
        player.Block = creature.Block;
        // every power and relic with the numbers its model keeps, and the turn's card plays (docs/SIMULATOR.md).
        player.Powers = SimReaders.Powers(creature, idOf);
        player.Relics = SimReaders.Relics(me);
        player.Fields = SimReaders.Fields(creature.Powers.Cast<AbstractModel>().Concat(me.Relics.Where(relic => !relic.IsMelted)), idOf);
        player.Cards = SimReaders.Cards(me);
        player.Turn = SimReaders.Turn(me, combat);
        foreach (var relic in me.Relics)
        {
            switch (relic.GetType().Name)
            {
                case nameof(PaelsEye):
                    // Status Active: unused, and no card played this turn (BeforeCardPlayed sets it back); Whispering
                    // Earring's turn 1 counts as played (PaelsEye.AnyCardsPlayedThisTurn).
                    player.PaelsEyeExhaust = relic.Status == MegaCrit.Sts2.Core.Entities.Relics.RelicStatus.Active
                        && !(Sts2State.TurnNumber == 1 && me.Relics.Any(otherRelic => otherRelic is WhisperingEarring));
                    break;
            }
        }

        player.HandCount = hand.Count;
        player.ExhaustPile = SafeInt(() => PileType.Exhaust.GetPile(me).Cards.Count);
        player.Energy = pcs.Energy;
        player.Arrows = SpireQoL.Procs.Fool.ArrowsAvailable(me);
        player.Fading = hand.Count(card => IsFading(card, combat));
        // A card of yours that upgrades every card in your hand (decision #363: Armaments+, Apotheosis): each card's upgraded copy is read here, so what an
        // upgrade changes of one acting as the turn ends is followed (its TurnEndCard.Upgraded); one an upgrade leaves Ethereal changes nothing then.
        var handUpgraded = PilesYourCardsUpgrade(me).HasFlag(UpgradedPiles.Hand);
        player.UpgradableAtTurnEnd = hand.Count(card => !SafeBool(() => !card.IsUpgradable) && (IsFading(card, combat) || TurnEndOf(card) != null)
            && !(handUpgraded && UpgradeReadAtTurnEnd(card, combat)));
        player.ExhaustingInHand = hand.Where(card => CardEnergy.WhenExhausted(card) > 0)
            .Select(card => new ExhaustingCard(HandId(card), CardEnergy.WhenExhausted(card), SimReaders.TypeOf(card))).ToList();
        // Hex (decision #85): its cards' Ethereal goes when its caster dies.
        if (creature.GetPower<HexPower>() is { Applier: { } hexer } && idOf(hexer) is { } hexId && hexId >= 0)
        {
            player.HexFrom = hexId;
            player.HexFading = hand.Count(card => IsFading(card, combat) && EtherealOnlyFromHex(card));
        }
        // Cards that act in hand at the end of the turn (the simulator's list): their Damage or HpLoss as the card shows it.
        foreach (var card in hand)
            if (TurnEndOf(card) is { } turnEnd) player.TurnEndCards.Add(handUpgraded ? turnEnd with { Upgraded = UpgradedTurnEnd(card) } : turnEnd);

        // Focus and Thunder are in their powers (read from their code: ModifyOrbValue, AfterOrbEvoked); Gold-Plated Cables in its relic.
        player.OrbCapacity = pcs.OrbQueue.Capacity;
        player.SlotOnFirstChannel = SafeInt(() => me.Character.BaseOrbSlotCount) == 0;
        foreach (var orb in pcs.OrbQueue.Orbs) player.Orbs.Add(ToSim(orb));

        // Their pets, each a creature with its own powers (Die For You, Wuppy's Loyalty: read from their code), listening at its place.
        var osty = me.Osty;
        player.OstyHp = osty != null && osty.IsAlive ? osty.CurrentHp : 0;
        if (osty != null) player.OstyMaxHp = osty.MaxHp;
        player.OstyShields = osty != null;
        if (osty != null) player.OstyPowers = SimReaders.Powers(osty, idOf);
        if (me.Creature.Pets.FirstOrDefault(pet => PetKindOf(me, pet) == PetKind.Wuppy) is { } wuppy)
        {
            player.WuppyHp = wuppy.IsAlive ? wuppy.CurrentHp : 0;
            player.WuppyPowers = SimReaders.Powers(wuppy, idOf);
        }
    }

    /// <summary>
    /// Which of the pets the simulator keeps a player's pet is (null: none of them): their Osty (Player.Osty), or the Wuppy relic's (its monster,
    /// MegaCrit.Sts2.Core.Models.Monsters.Wuppy, v0.113.0).
    /// </summary>
    internal static PetKind? PetKindOf(Player owner, Creature pet) =>
        pet == owner.Osty ? PetKind.Osty : pet.Monster?.GetType().Name == nameof(MegaCrit.Sts2.Core.Models.Monsters.Wuppy) ? PetKind.Wuppy : null;

    /// <summary>
    /// An enemy's attack hits on a player, through the game's damage math with that player as the target
    /// (their Vulnerable, Intangible, Tank...), as AttackIntent.GetSingleDamage does for you. Null if the
    /// intent can't be read.
    /// </summary>
    private static List<int>? HitsOn(Creature creature, Creature target, Player me, ICombatState combat)
    {
        try
        {
            var hits = new List<int>();
            foreach (var attack in creature.Monster!.NextMove.Intents.OfType<AttackIntent>())
            {
                if (attack.DamageCalc == null) return null;
                var damage = Hook.ModifyDamage(me.RunState, combat, target, creature, attack.DamageCalc(), MegaCrit.Sts2.Core.ValueProps.ValueProp.Move, null, null,
                    ModifyDamageHookType.All, CardPreviewMode.None, out _);
                var perHit = Math.Max(0, (int)damage);
                for (var i = 0; i < Math.Max(1, attack.Repeats); i++) hits.Add(perHit);
            }
            return hits;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Ethereal only through Hex: Hexed, and not Ethereal on its own (its own or Music Box's, KeywordSources.Local).</summary>
    internal static bool EtherealOnlyFromHex(CardModel card)
    {
        try
        {
            return card.Affliction is MegaCrit.Sts2.Core.Models.Afflictions.Hexed
                   && !card.GetKeywordsWithSources(KeywordSources.Local).Contains(CardKeyword.Ethereal);
        }
        catch
        {
            return false;
        }
    }

    private static bool IsFading(CardModel card, ICombatState combat)
    {
        try
        {
            return card.Keywords.Contains(CardKeyword.Ethereal) && (card.HasTurnEndInHandEffect || Hook.ShouldEtherealTrigger(combat, card));
        }
        catch
        {
            return false;
        }
    }

    /// <summary>An orb's kind to the simulator: the game's orb class's (HookReader.OrbKindOf, as its code is read); another mod's is Unknown.</summary>
    internal static OrbKind KindOf(OrbModel orb) =>
        orb.GetType() is { Namespace: "MegaCrit.Sts2.Core.Models.Orbs" } type && HookReader.OrbKindOf(type.Name) is { } kind ? kind : OrbKind.Unknown;

    /// <summary>
    /// An orb as the simulator keeps it (decision #214): its values as the game shows them (PassiveVal, EvokeVal through the game's pipeline) and
    /// the fields its code reads and stores (Glass's _passiveVal), each named by its class's code (ModelSpec.Starts, but a value's own).
    /// </summary>
    private static SimOrb ToSim(OrbModel orb)
    {
        var kind = KindOf(orb);
        var sim = new SimOrb { Kind = kind, Passive = (int)orb.PassiveVal, Evoke = (int)orb.EvokeVal };
        if (SimModels.Orb(kind)?.Spec is { Starts: { } starts } spec)
            foreach (var (field, start) in starts.OrderBy(pair => pair.Key, StringComparer.Ordinal))
                if (spec.OrbAliases?.ContainsKey(field) != true) sim.Fields = sim.Fields.With(field, (int)Sts2State.Private(orb, field, fallback: start));
        return sim;
    }

    // ------------------------------------------------------------------ card plays

    /// <summary>
    /// The card's block through the game's block math. gainer: who gets it (you by default; a targeted teammate for
    /// Lift); calcTarget: the target a calculated value is worked out on (Mimic, Demonic Shield aimed at a teammate).
    /// </summary>
    public static int CardBlock(CardModel card, ICombatState combat, Player me, Creature? gainer = null, Creature? calcTarget = null)
    {
        try
        {
            decimal raw;
            if (card.DynamicVars.TryGetValue("CalculatedBlock", out var dynamicVar) && dynamicVar is CalculatedVar calc) raw = calc.Calculate(calcTarget);
            else if (card.DynamicVars.TryGetValue("Block", out var otherDynamicVar)) raw = otherDynamicVar.BaseValue;
            else return 0;
            if (raw <= 0) return 0;
            return Math.Max(0, (int)Hook.ModifyBlock(combat, gainer ?? me.Creature, raw, MegaCrit.Sts2.Core.ValueProps.ValueProp.Move, card, null, out _));
        }
        catch
        {
            return 0;
        }
    }

    private static int SafeInt(Func<int> read)
    {
        try { return read(); } catch { return 0; }
    }

    private static int SafeInt(Func<int> read, int fallback)
    {
        try { return read(); } catch { return fallback; }
    }

    /// <summary>Each monster's move rules as last read, by the move on screen and its moves so far (read again only when either changes).</summary>
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<MegaCrit.Sts2.Core.Models.MonsterModel, Tuple<string, int, SimMoveRules?>> MoveRulesRead = new();

    /// <summary>An enemy's move rules (decision #307: IntentReader.Rules), or null where they can't be read.</summary>
    private static SimMoveRules? MoveRulesOf(Creature creature)
    {
        try
        {
            if (creature.Monster is not { MoveStateMachine: { } machine } monster) return null;
            var (current, moves) = (monster.NextMove.Id, machine.StateLog.Count);
            if (MoveRulesRead.TryGetValue(monster, out var kept) && kept.Item1 == current && kept.Item2 == moves) return kept.Item3;
            var rules = IntentReader.Rules(creature);
            MoveRulesRead.AddOrUpdate(monster, Tuple.Create(current, moves, rules));
            return rules;
        }
        catch { return null; }
    }

    private static bool SafeBool(Func<bool> read)
    {
        try { return read(); } catch { return false; }
    }

    private static string SafeCardTitle(CardModel card)
    {
        try { return card.Title; } catch { return card.GetType().Name; }
    }
}
