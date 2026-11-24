using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Orbs;
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

    /// <summary>Your powers the simulator tracks (Plating, Juggernaut, Feel No Pain, Thorns, Flame Barrier).</summary>
    internal static readonly HashSet<string> SelfPowers = new()
    {
        nameof(PlatingPower), nameof(JuggernautPower), nameof(FeelNoPainPower), nameof(ThornsPower), nameof(FlameBarrierPower),
        nameof(AccelerantPower),
    };

    /// <summary>
    /// A tracked power the card's code applies without a PowerVar for it: its plain value ("Power":
    /// Feel No Pain; "DamageBack": Flame Barrier; "Accelerant"), 0 when it has neither (the card scan reports it), or
    /// null (none, or it has a PowerVar, read with the others).
    /// </summary>
    private static readonly string[] PlainPowerVars = { "Power", "DamageBack", "Accelerant" }; // DynamicVars keys

    /// <summary>
    /// How many times an enemy's poison ticks: 1 plus the Accelerant of every living opponent of it, so every player's
    /// (PoisonPower.TriggerCount). Not capped at its stacks, as the game's count is: stacks added later tick that many times.
    /// </summary>
    private static int PoisonTriggers(Creature enemy, ICombatState combat)
    {
        try { return 1 + combat.GetOpponentsOf(enemy).Where(creature => creature.IsAlive).Sum(creature => creature.GetPowerAmount<AccelerantPower>()); }
        catch { return 1; }
    }

    internal static int? PlainSelfPowerAmount(CardModel card, Type power)
    {
        if (!SelfPowers.Contains(power.Name)) return null;
        var typed = card.DynamicVars.Values.Any(dynamicVar => dynamicVar.GetType() is { IsGenericType: true } type
                                                     && type.GetGenericTypeDefinition() == typeof(PowerVar<>) && type.GetGenericArguments()[0] == power);
        if (typed) return null;
        foreach (var key in PlainPowerVars)
            if (card.DynamicVars.TryGetValue(key, out var otherDynamicVar)) return (int)otherDynamicVar.BaseValue;
        return 0;
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
    /// by the member: its fallback is a guess, not the game's state.
    /// </summary>
    public static SimState Build(Player me, ICombatState combat, out Dictionary<Creature, int> ids)
    {
        using var reads = Sts2State.TrackReads();
        var state = BuildFromGame(me, combat, out ids);
        if (reads.Unread.Count > 0) state.Unmodelled ??= SimState.Unread(reads.Unread[0]);
        return state;
    }

    private static SimState BuildFromGame(Player me, ICombatState combat, out Dictionary<Creature, int> ids)
    {
        var idMap = new Dictionary<Creature, int>();
        ids = idMap;
        var state = new SimState { MainRules = BranchRules.MainRulesNow() };
        var mates = Teammates(me, combat);
        // Any creature's simulator id (Who): an enemy's, you, or teammate k.
        int? IdOf(Creature creature) => idMap.TryGetValue(creature, out var enemyId) ? enemyId : creature == me.Creature ? Who.You
            : mates.FindIndex(player => player.Creature == creature) is var teammateIndex and >= 0 ? Who.Teammate(teammateIndex) : null;
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
                Hp = creature.CurrentHp,
                Block = creature.Block,
                Hittable = creature.IsAlive && creature.IsHittable,
                Stunned = creature.IsStunned,
                Imbalanced = creature.HasPower<ImbalancedPower>(),
                Poison = SafeInt(() => creature.GetPower<PoisonPower>()?.CalculateTotalDamageNextTurn() ?? 0),
                PoisonStacks = creature.GetPower<PoisonPower>()?.Amount ?? 0,
                PoisonTriggers = PoisonTriggers(creature, combat),
                IntendsToAttack = creature.IsAlive && SafeBool(() => creature.Monster?.IntendsToAttack == true),
            };
            // Every power, with the numbers its model keeps (Curl Up, Sneaky, Skittish, Thorns, Burrowed, Asleep, Slumber, Flutter,
            // Plow, Shriek, The Gambit, Slippery, Hard to Kill, Intangible, Hardened Shell, Barricade, Strength, Weak...).
            enemy.Powers = SimReaders.Powers(creature, IdOf);
            enemy.Fields = SimReaders.Fields(creature.Powers);
            foreach (var power in creature.Powers) SimReaders.AddVars(state, id, power);
            // Explosive: its death damages every living player (unpowered, blockable), through the game's damage math.
            if (creature.IsAlive && (ExplosiveOf?.Invoke(creature) ?? 0) is > 0 and var explosive) enemy.Explosive = ExplosiveOn(creature, me.Creature, explosive, me, combat);
            enemy.StartedAlive = creature.IsAlive;
            // The creatures its death adds (Surprise's gremlins), read from its powers' AfterDeath code, with every HP each can start with.
            if (creature.IsAlive) enemy.Spawns = SpawnsOf(creature, combat);
            // An ally dying: Ravenous stuns it (and gives Strength for later); Crab Rage gives it Strength once.
            // Crab Rage: its Strength and block when an ally dies (CrabRagePower model: Strength as its amount, block here).
            if (creature.GetPower<CrabRagePower>() is { } crab)
            {
                enemy.Powers = enemy.Powers.With(PowerId.CrabRage, Sts2State.Var(crab, "StrengthPower"));
                enemy.CrabRageBlock = Sts2State.Var(crab, "Block");
            }
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
        foreach (var (enemy, monster, hitsPerAttack) in moves) enemy.Move = MoveOf(monster, hitsPerAttack, IdOf, combat);

        // The history ledger (decision #191): the questions this board's models ask of the fight's log, each player's counts seeded here, once.
        state.History = HistoryLedger.QueriesFor(combat);
        FillPlayer(state.You, me, combat, IdOf);
        state.You.Fight = HistoryLedger.Seed(state.History, me, combat, ref state.You.Turn);
        foreach (var power in me.Creature.Powers) SimReaders.AddVars(state, Who.You, power);
        foreach (var relic in me.Relics) SimReaders.AddVars(state, Who.You, relic);
        FillAutoPlays(state.You, me, combat, ids);
        FillDrawPile(state.You, me, combat, ids);
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
        if (teamCreatures.Count > 0)
        {
            foreach (var (creature, id) in ids)
            {
                if (!creature.IsAlive || creature.Monster == null || state.Enemy(id) is not { } enemy) continue;
                foreach (var teamCreature in teamCreatures) enemy.TeammateHits.Add(HitsOn(creature, teamCreature, me, combat) ?? enemy.Hits);
                if ((ExplosiveOf?.Invoke(creature) ?? 0) is > 0 and var explosive)
                    enemy.ExplosiveOnTeammates = teamCreatures.Select(teamCreature => ExplosiveOn(creature, teamCreature, explosive, me, combat)).ToList();
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
        simPlayer.Fairies = SafeInt(() => player.Potions.Count(potion => potion.GetType().Name == "FairyInABottle"));
        simPlayer.FairyHeal = Math.Max(1, (int)(maxHp * 0.3m));
        simPlayer.LizardTailHeal = player.Relics.FirstOrDefault(relic => relic is LizardTail) is { IsUsedUp: false } tail
            ? Math.Max(1, (int)(maxHp * (Sts2State.Var(tail, "Heal", fallback: 50) / 100m)))
            : 0;
    }

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
            if (player.Stampede > 0 || player.Simpleton > 0)
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
                        Plays = AutoPlays(card, me, combat, ids, owner),
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
        }
    }

    /// <summary>
    /// Each card in your piles and its draw profile, from the last board read (FillDrawPile, on the game's thread): what a play
    /// taking cards from your piles names as the ones it can take (TakeToHandAction), and with Reboot in your hand, your hand's
    /// cards as they join your draw pile (ShuffleDrawPileAction).
    /// </summary>
    internal static IReadOnlyDictionary<CardModel, SimDrawCard> DrawProfiles { get; private set; } = new Dictionary<CardModel, SimDrawCard>();

    /// <summary>
    /// Your draw and discard piles by profile (decision #90): what about each card matters if you draw it on this board, never
    /// their order (hidden; only the cards known to be on top would be: none yet). What matters: acting in hand at end of
    /// turn (always), exhausting as Ethereal (always), a Status with Iteration, Ethereal with Pagestorm, a Strike with
    /// Hellraiser, and with Stampede or Simpleton the card itself (how it plays). Everything else is one plain profile.
    /// </summary>
    private static void FillDrawPile(SimPlayer player, Player me, ICombatState combat, IReadOnlyDictionary<Creature, int> ids)
    {
        DrawProfiles = new Dictionary<CardModel, SimDrawCard>(ReferenceEqualityComparer.Instance);
        try
        {
            var iteration = me.Creature.HasPower<IterationPower>();
            var pagestorm = me.Creature.HasPower<PagestormPower>();
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
            var topPlays = handCards.Any(card => Procs.Programs.CardEffects.AutoPlaysFromDrawPile(card.GetType()) || card.GetType().Name == nameof(LostLute))
                           || me.Creature.Powers.Any(power => power.GetType().Name == nameof(LostLutePower));
            var topUpgrades = handCards.Any(card => Procs.Programs.BoardCounts.AutoPlaysOf(card) is { Upgrade: true });
            var fromPileByKey = new Dictionary<string, (SimHandCard? Plain, SimHandCard? Upgraded, bool Exhausts)>();
            // The filtered takes of the cards in your hand (decision #189): a card drawn or taken into your hand during a play counts for
            // the ones it would be taken by (Second Wind's non-Attacks, The Hermit's Curse).
            var takeFilters = handCards.SelectMany(TakeFiltersOf).ToList();
            SimDrawCard Profile(CardModel card)
            {
                var turnEnd = TurnEndOf(card);
                var fading = IsFading(card, combat);
                var hexFading = fading && EtherealOnlyFromHex(card);
                var status = iteration && card.Type == CardType.Status;
                var ethereal = pagestorm && card.Keywords.Contains(CardKeyword.Ethereal);
                var strike = hellraiser && card.Tags.Contains(CardTag.Strike);
                // Hellraiser's auto-play is built like a picked card's (decision #121), except past the game's cap on endless
                // auto-plays, when every enemy you could hit can't die (HellraiserPower: 9 a turn): then it isn't simulated.
                var hellraised = strike && !me.Creature.CombatState!.HittableEnemies.All(creature => creature.HpDisplay.IsInfinite());
                var pickable = picks && !card.Keywords.Contains(CardKeyword.Unplayable) && (player.Simpleton > 0 || card.Type == CardType.Attack);
                var cost = card.EnergyCost.CostsX ? 999999999 : card.EnergyCost.GetResolved();
                var who = pickable || strike || topPlays ? $"{card.GetType().Name}+{card.CurrentUpgradeLevel}/{card.Enchantment?.GetType().Name}/{cost}" : "";
                // I Am Invincible plays from the top of the draw pile at the end of your turn: its block.
                var invincible = card is IAmInvincible ? CardBlock(card, combat, me) : 0;
                var perfectFit = card.Enchantment is MegaCrit.Sts2.Core.Models.Enchantments.PerfectFit;
                var type = readsType ? SimReaders.TypeOf(card) : SimCardType.None;
                bool? free = readsCost ? !card.EnergyCost.CostsX && card.EnergyCost.GetWithModifiers(CostModifiers.All) == 0 : null;
                var sly = readsCost && card.Keywords.Contains(CardKeyword.Sly);
                // It acts when it's exhausted (Drum of Battle): its type goes with it, for an exhaust of a kind.
                var exhaustEnergy = CardEnergy.WhenExhausted(card);
                var energyLost = CardEnergy.LostWhenDrawn(card);
                var takenBy = takeFilters.Count == 0 ? null : takeFilters.Where(filter => filter.Takes(card, false) != false).Select(filter => filter.Key).ToList() is { Count: > 0 } keys ? keys : null;
                var key = $"{who}|{turnEnd}|{fading}{hexFading}{status}{ethereal}{strike}|{invincible}|{perfectFit}|{type}|{free}{sly}|{exhaustEnergy}{(exhaustEnergy > 0 ? SimReaders.TypeOf(card) : SimCardType.None)}|{energyLost}|{(takenBy == null ? "" : string.Join(",", takenBy))}";
                SimHandCard? auto = null;
                if ((pickable || hellraised) && !autoByKey.TryGetValue(key, out auto))
                {
                    // How it plays once in your hand: as the adapter builds a hand card's auto-plays. A card in the draw pile counts as
                    // not in your hand there (its hand delta 0, no hand card): played once drawn, it's from your hand, so one less, and
                    // its plays are its own (each copy drawn takes them as its own: SimHandCard.CopyWithId).
                    var plays = AutoPlays(card, me, combat, ids);
                    var id = HandId(card);
                    if (card.Pile?.Type == PileType.Draw) foreach (var play in plays) { play.HandDelta -= 1; play.HandCard = id; }
                    auto = new SimHandCard { Id = id, Name = card.GetType().Name, IsAttack = card.Type == CardType.Attack, Cost = cost, Plays = plays };
                    autoByKey[key] = auto;
                }
                (SimHandCard? Plain, SimHandCard? Upgraded, bool Exhausts) fromPile = default;
                if (topPlays && !fromPileByKey.TryGetValue(key, out fromPile))
                {
                    fromPile = (FromPile(card, card, me, combat, ids, out var exhausts), topUpgrades ? UpgradedFromPile(card, me, combat, ids) : null, exhausts);
                    fromPileByKey[key] = fromPile;
                }
                return new SimDrawCard(key)
                {
                    FromPile = fromPile.Plain, FromPileUpgraded = fromPile.Upgraded, ExhaustsWhenPlayed = fromPile.Exhausts,
                    EnergyLostOnDraw = energyLost,
                    Exhausting = exhaustEnergy > 0 ? new ExhaustingCard(HandCard: 0, exhaustEnergy, SimReaders.TypeOf(card)) : null,
                    TurnEnd = turnEnd, Fading = fading, HexFading = hexFading, Status = status, Ethereal = ethereal, Strike = strike,
                    Pickable = pickable, Auto = auto, InvincibleBlock = invincible, PerfectFit = perfectFit, Type = type, Free = free, Sly = sly, TakenBy = takenBy,
                };
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
            // The cards in your hand too, for a play that puts them into your draw pile (Reboot).
            if (handCards.Any(card => Procs.Programs.CardEffects.ShufflesDiscardPileIn(card.GetType())))
                foreach (var card in handCards) Remembered(card);
            DrawProfiles = profiles;
            // Cards something on this board makes into your draw pile (Personal Hive's Dazed, Biiig Hug's Soot): their profiles, from
            // the game's own card (ModelDb), for a draw later in the same simulation.
            var made = new Dictionary<string, SimDrawCard>();
            void Made<T>() where T : CardModel
            {
                try
                {
                    var card = ModelDb.Card<T>();
                    made[typeof(T).Name] = Profile(card);
                    // It listens from your piles once made (none of it there yet).
                    if (SimReaders.CardListenerOf(card) is { } listener) player.Cards = player.Cards.Add(listener, delta: 0);
                }
                catch { /* not built: a draw of it is unknown */ }
            }
            if (combat.Enemies.Any(enemy => enemy.Powers.Any(power => power.GetType().Name == nameof(PersonalHivePower)))) Made<Dazed>();
            // The cards your hand's cards make into your hand: into a full one they go to your discard pile (CardEventAction.MadeCard).
            foreach (var maker in handCards)
                if (MadeCardOf(maker) is { } madeByPlay && !made.ContainsKey(madeByPlay.GetType().Name))
                {
                    try { made[madeByPlay.GetType().Name] = Profile(madeByPlay); }
                    catch { /* not built: it isn't followed into the pile */ }
                }
            if (me.Relics.Any(relic => relic.GetType().Name == nameof(MegaCrit.Sts2.Core.Models.Relics.BiiigHug))) Made<Soot>();
            player.MadeCards = made.Count > 0 ? made : null;
            // Iteration counts the Status cards drawn this turn (CardDrawnEntry: you watched them come).
            if (iteration)
                player.StatusDrawn = CombatManager.Instance.History.Entries.OfType<MegaCrit.Sts2.Core.Combat.History.Entries.CardDrawnEntry>()
                    .Count(cardDrawnEntry => cardDrawnEntry.HappenedThisTurn(combat) && cardDrawnEntry.Actor == me.Creature && cardDrawnEntry.Card.Type == CardType.Status);
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
    /// Every game class that reacts to a card being drawn (AfterCardDrawn, AfterCardDrawnEarly), reviewed for the draw profile
    /// (decision #90): what about the drawn card it reads, and how the simulator covers it. DrawScanTests (catalog tests) fails a
    /// class that isn't here, so a new draw reaction can't merge cards the profile should keep apart.
    /// </summary>
    internal static readonly IReadOnlyDictionary<string, string> DrawReactions = new Dictionary<string, string>
    {
        [nameof(AutomationPower)] = "any card: a count (CardEvent.Drawn)",
        [nameof(CacophonyPower)] = "any card: a count (CardEvent.Drawn)",
        [nameof(SpeedsterPower)] = "any card: a count (CardEvent.Drawn)",
        [nameof(CorrosiveWavePower)] = "any card: a count (CardEvent.Drawn)",
        [nameof(IterationPower)] = "a Status (the profile's Status, with Iteration): draws more",
        [nameof(PagestormPower)] = "Ethereal (the profile's Ethereal, with Pagestorm): draws more",
        [nameof(HellraiserPower)] = "a Strike (the profile's Strike, with Hellraiser): its auto-play isn't simulated; later auto-plays aren't predicted",
        [nameof(ChainsOfBindingPower)] = "afflicts drawn cards Bound (playability, read live; nothing at end of turn)",
        [nameof(ConfusedPower)] = "a random cost (costs matter to Simpleton only: read live in hand; a drawn card keeps the cost it had)",
        [nameof(KinglyKick)] = "its own cost on draw (Simpleton: the cost it had in the pile)",
        [nameof(KinglyPunch)] = "its own cost on draw (Simpleton: the cost it had in the pile)",
        [nameof(MegaCrit.Sts2.Core.Models.Cards.Void)] = "itself: loses energy when drawn (the profile's EnergyLostOnDraw; model VoidCard, after your powers and relics)",
        ["Slither"] = "a random cost on draw (Simpleton: the cost it had in the pile)",
        ["MockRemoveDrawnCardsFromCombatPower"] = "the game's test mock: never in a real fight",
    };

    /// <summary>A card that acts in hand at the end of your turn (SimEngine.HandAtTurnEnd), with its amount; null otherwise.</summary>
    private static TurnEndCard? TurnEndOf(CardModel card)
    {
        if (!SimEngine.HandAtTurnEnd.TryGetValue(card.GetType().Name, out var kind)) return null;
        var amount = kind switch { SimEngine.InHand.Damage => Sts2State.Var(card, "Damage"), SimEngine.InHand.HpLoss => Sts2State.Var(card, "HpLoss"), _ => 0 };
        return new TurnEndCard(card.GetType().Name, SafeCardTitle(card), amount);
    }

    /// <summary>Which card in your hand a play is (SimPlay.HandCard, SimHandCard.Id): its object identity.</summary>
    public static int HandId(CardModel card) => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(card) | 1;


    /// <summary>
    /// How <paramref name="played"/> plays when a card auto-plays it from your draw pile (SimDrawCard.FromPile): as an auto-play of a
    /// card that isn't in your hand (its hand delta its own, no hand card). <paramref name="pileCard"/> is the card in your pile (the
    /// same, or the one an upgraded copy stands for). No plays for an Unplayable card (it goes to the discard pile unplayed). Null when
    /// it can't be built.
    /// </summary>
    private static SimHandCard? FromPile(CardModel played, CardModel pileCard, Player me, ICombatState combat, IReadOnlyDictionary<Creature, int> ids, out bool exhausts)
    {
        exhausts = false;
        try
        {
            exhausts = ExhaustsAfterPlay(played, me);
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
        if (!card.IsUpgradable) return FromPile(card, card, me, combat, ids, out _);
        try
        {
            var copy = (CardModel)card.MutableClone();
            copy.UpgradeInternal();
            copy.FinalizeUpgradeInternal();
            return FromPile(copy, card, me, combat, ids, out _);
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

    /// <summary>
    /// Where the card goes after its last play is the exhaust pile (CardModel.GetResultLocationForCardPlay, once however many plays): not a
    /// Power or a dupe (History Course's copy); the Exhaust keyword (added Exhaust included) or ExhaustOnNextPlay; then Corruption sends a
    /// played Skill there whatever it was (CorruptionPower.ModifyCardPlayResultLocation).
    /// </summary>
    internal static bool ExhaustsAfterPlay(CardModel card, Player me) =>
        card.Type != CardType.Power
        && ((!card.IsDupe && (card.Keywords.Contains(CardKeyword.Exhaust) || card.ExhaustOnNextPlay))
            || (card.Type == CardType.Skill && me.Creature.Powers.Any(power => power is CorruptionPower)));

    private static SimPlay AutoPlay(CardModel card, Player me, ICombatState combat, IReadOnlyDictionary<Creature, int> ids, int target, int owner = Who.You)
    {
        var play = Play(card, me, combat, ids, target, owner);
        play.AutoPlay = true;
        if (play.Card != null) play.Card = play.Card with { AutoPlay = true };
        return play;
    }

    /// <summary>The creature's Explosive (its death damages every player): beta only (decision #163); main has no Explosive.</summary>
    [BetaOnly, System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static int ExplosiveAmount(Creature creature) => creature.GetPower<ExplosivePower>()?.Amount ?? 0;

    private static readonly Func<Creature, int>? ExplosiveOf = BetaOnly.Gate<Func<Creature, int>>(ExplosiveAmount);

    /// <summary>The creature's Simpleton (it auto-plays your cheapest cards): beta only (decision #163); main has no Simpleton.</summary>
    [BetaOnly, System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static int SimpletonAmount(Creature creature) => creature.GetPower<SimpletonPower>()?.Amount ?? 0;

    internal static readonly Func<Creature, int>? SimpletonOf = BetaOnly.Gate<Func<Creature, int>>(SimpletonAmount);

    /// <summary>Explosive's damage on a player (ExplosivePower.AfterDeath: CreatureCmd.Damage, Unpowered | SkipHurtAnim, no dealer card).</summary>
    private static int ExplosiveOn(Creature enemy, Creature target, int amount, Player me, ICombatState combat)
        => SafeInt(() => Math.Max(0, (int)Hook.ModifyDamage(me.RunState, combat, target, enemy, amount,
            MegaCrit.Sts2.Core.ValueProps.ValueProp.Unpowered | MegaCrit.Sts2.Core.ValueProps.ValueProp.SkipHurtAnim, null, null,
            ModifyDamageHookType.All, CardPreviewMode.None, out _)), amount);

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
        player.Counters = SimReaders.Counters(creature);
        player.Relics = SimReaders.Relics(me);
        player.Fields = SimReaders.Fields(creature.Powers.Cast<AbstractModel>().Concat(me.Relics.Where(relic => !relic.IsMelted)));
        player.Cards = SimReaders.Cards(me);
        player.Turn = SimReaders.Turn(me, combat);
        player.Reflect = creature.Powers.Any(power => power is ReflectPower);
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
                case nameof(GoldPlatedCables): player.FrontOrbBonusTriggers += 1; break; // +1 for the front orb (ModifyOrbPassiveTriggerCounts)
            }
        }

        player.HandCount = hand.Count;
        player.ExhaustPile = SafeInt(() => PileType.Exhaust.GetPile(me).Cards.Count);
        player.Energy = pcs.Energy;
        player.Arrows = SpireQoL.Procs.Fool.ArrowsAvailable(me);
        player.Fading = hand.Count(card => IsFading(card, combat));
        player.ExhaustingInHand = hand.Where(card => CardEnergy.WhenExhausted(card) > 0)
            .Select(card => new ExhaustingCard(HandId(card), CardEnergy.WhenExhausted(card), SimReaders.TypeOf(card))).ToList();
        // Hex (decision #85): its cards' Ethereal goes when its caster dies.
        if (creature.GetPower<HexPower>() is { Applier: { } hexer } && idOf(hexer) is { } hexId && hexId >= 0)
        {
            player.HexFrom = hexId;
            player.HexFading = hand.Count(card => IsFading(card, combat) && EtherealOnlyFromHex(card));
        }
        player.AttacksThisTurn = Sts2State.CountPlayedThisTurn(me, CardType.Attack);
        // Cards that act in hand at the end of the turn (the simulator's list): their Damage or HpLoss as the card shows it.
        foreach (var card in hand)
            if (TurnEndOf(card) is { } turnEnd) player.TurnEndCards.Add(turnEnd);

        player.Focus = creature.GetPower<FocusPower>()?.Amount ?? 0;
        player.OrbCapacity = pcs.OrbQueue.Capacity;
        player.SlotOnFirstChannel = SafeInt(() => me.Character.BaseOrbSlotCount) == 0;
        player.Thunder = me.Creature.GetPower<ThunderPower>()?.Amount ?? 0;
        foreach (var orb in pcs.OrbQueue.Orbs) player.Orbs.Add(ToSim(orb, combat));
        // The game's trigger count includes Gold-Plated Cables' bonus on the current front orb; the
        // simulator gives that bonus to whichever orb is in front when passives fire.
        if (player.Orbs.Count > 0) player.Orbs[0].Triggers = Math.Max(1, player.Orbs[0].Triggers - player.FrontOrbBonusTriggers);

        var osty = me.Osty;
        player.OstyHp = osty != null && osty.IsAlive ? osty.CurrentHp : 0;
        player.OstyShields = osty != null && osty.HasPower<DieForYouPower>();
        player.WuppyHp = SafeInt(() => me.Creature.Pets.Where(pet => pet.IsAlive && pet.Powers.Any(power => power.GetType().Name == nameof(WuppysLoyaltyPower))).Sum(pet => pet.CurrentHp));
    }

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

    /// <summary>An orb's kind to the simulator, by its class.</summary>
    internal static OrbKind KindOf(OrbModel orb) => orb switch
    {
        LightningOrb => OrbKind.Lightning,
        FrostOrb => OrbKind.Frost,
        DarkOrb => OrbKind.Dark,
        GlassOrb => OrbKind.Glass,
        PlasmaOrb => OrbKind.Plasma,
        _ => OrbKind.Unknown,
    };

    private static SimOrb ToSim(OrbModel orb, ICombatState combat)
    {
        var kind = KindOf(orb);
        var triggers = 1;
        try { triggers = Hook.ModifyOrbPassiveTriggerCount(combat, orb, 1, out _); } catch { /* 1 */ }
        var sim = new SimOrb { Kind = kind, Passive = (int)orb.PassiveVal, Evoke = (int)orb.EvokeVal, Triggers = triggers };
        if (orb is GlassOrb)
        {
            // Base and bonus separately: the base decays, the bonus (Focus) doesn't.
            sim.GlassBase = (int)Sts2State.Private(orb, "_passiveVal", fallback: 4m);
            sim.GlassBonus = sim.Passive - sim.GlassBase;
        }
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

    private static bool SafeBool(Func<bool> read)
    {
        try { return read(); } catch { return false; }
    }

    private static string SafeCardTitle(CardModel card)
    {
        try { return card.Title; } catch { return card.GetType().Name; }
    }
}
