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
using SpireQoL.Procs;
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
    /// <summary>Orbs each card channels (OrbCmd.Channel&lt;T&gt; in its OnPlay; decompile v0.111.0). Chaos is random.</summary>
    private static readonly Dictionary<string, OrbKind[]> Channels = new()
    {
        [nameof(BallLightning)] = new[] { OrbKind.Lightning }, [nameof(Zap)] = new[] { OrbKind.Lightning },
        [nameof(Chill)] = new[] { OrbKind.Frost }, [nameof(ColdSnap)] = new[] { OrbKind.Frost }, [nameof(Coolheaded)] = new[] { OrbKind.Frost },
        [nameof(Glacier)] = new[] { OrbKind.Frost, OrbKind.Frost },
        [nameof(Fusion)] = new[] { OrbKind.Plasma }, [nameof(Ignition)] = new[] { OrbKind.Plasma },
        [nameof(MeteorStrike)] = new[] { OrbKind.Plasma, OrbKind.Plasma, OrbKind.Plasma },
        [nameof(Glasswork)] = new[] { OrbKind.Glass }, [nameof(Spinner)] = new[] { OrbKind.Glass },
        [nameof(Null)] = new[] { OrbKind.Dark }, [nameof(ShadowShield)] = new[] { OrbKind.Dark }, [nameof(Darkness)] = new[] { OrbKind.Dark },
        [nameof(Rainbow)] = new[] { OrbKind.Lightning, OrbKind.Frost, OrbKind.Dark },
    };

    /// <summary>
    /// Orb slots a card's play adds or removes (OrbCmd.AddSlots / RemoveSlots in OnPlay; decompile
    /// v0.111.0): Bulk Up loses its OrbSlots, Capacitor and Modded add their Repeat.
    /// </summary>
    internal static int SlotChange(CardModel card, string name) => name switch
    {
        nameof(BulkUp) => -Sts2State.Var(card, "OrbSlots", 1),
        nameof(Capacitor) or nameof(Modded) or "TheMagician" => Sts2State.Var(card, "Repeat", 1),
        _ => 0,
    };

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
    private static readonly string[] PlainPowerVars = { "Power", "DamageBack", "Accelerant" };

    /// <summary>
    /// How many times an enemy's poison ticks: 1 plus the Accelerant of every living opponent of it, so every player's
    /// (PoisonPower.TriggerCount). Not capped at its stacks, as the game's count is: stacks added later tick that many times.
    /// </summary>
    private static int PoisonTriggers(Creature enemy, ICombatState cs)
    {
        try { return 1 + cs.GetOpponentsOf(enemy).Where(c => c.IsAlive).Sum(c => c.GetPowerAmount<AccelerantPower>()); }
        catch { return 1; }
    }

    internal static int? PlainSelfPowerAmount(CardModel card, Type power)
    {
        if (!SelfPowers.Contains(power.Name)) return null;
        var typed = card.DynamicVars.Values.Any(v => v.GetType() is { IsGenericType: true } t
                                                     && t.GetGenericTypeDefinition() == typeof(PowerVar<>) && t.GetGenericArguments()[0] == power);
        if (typed) return null;
        foreach (var key in PlainPowerVars)
            if (card.DynamicVars.TryGetValue(key, out var pv)) return (int)pv.BaseValue;
        return 0;
    }

    /// <summary>
    /// Block cards that put back the Stretch they use (Cho Ki: applies the Stretch it had when played, before the play
    /// spends it), so a replay's later plays get Stretch too (v0.113.0). The card scan flags a new one.
    /// </summary>
    internal static readonly HashSet<string> KeepsStretchCards = new() { "ChoKi" };

    /// <summary>Cards whose play kills your Osty (CreatureCmd.Kill on Owner.Osty; decompile v0.111.0).</summary>
    internal static readonly HashSet<string> KillsOstyCards = new() { nameof(Sacrifice), nameof(BoneShards) };

    /// <summary>Cards that kill an enemy at or below a threshold (Judgement: SetCurrentHp to 0). The card scan flags a new one.</summary>
    internal static readonly HashSet<string> ExecuteCards = new() { "Judgement" };

    /// <summary>Cards whose kill sets off a hit on every enemy (Timber: OnKillDamage). The card scan flags a new damage value.</summary>
    internal static readonly HashSet<string> OnKillHitAllCards = new() { "Timber" };

    /// <summary>Cards whose hit on every enemy repeats once per kill (Echoing Slash: attackCount += WasTargetKilled).</summary>
    internal static readonly HashSet<string> RepeatPerKillCards = new() { nameof(EchoingSlash) };

    /// <summary>Cards that set off every enemy's poison during the play (Outbreak: PoisonPower.Trigger on each).</summary>
    internal static readonly HashSet<string> TriggerPoisonCards = new() { nameof(Outbreak) };

    /// <summary>Cards that take all of their target's block, then its Artifact (Expose: CreatureCmd.LoseBlock, PowerCmd.Remove).</summary>
    internal static readonly HashSet<string> StripBlockCards = new() { nameof(Expose) };

    /// <summary>
    /// Cards aimed at a teammate (AnyAlly) whose block goes to that teammate (CreatureCmd.GainBlock on cardPlay.Target: Lift,
    /// Constellation, Demonic Shield). Your Dexterity and doublers count (they key on the card's owner), their Frail too
    /// (it keys on the target): the game's block math with them as the target. The card scan flags a new ally card.
    /// </summary>
    internal static readonly HashSet<string> BlockToAllyCards = new() { nameof(Lift), nameof(Constellation), nameof(DemonicShield) };

    /// <summary>
    /// Cards that give their block to every living player (Rally.OnPlay: CreatureCmd.GainBlock for each player on your side): you
    /// first, then each teammate (decision #75: what it does for teammates counts).
    /// </summary>
    internal static readonly HashSet<string> AllPlayersBlockCards = new() { nameof(Rally) };

    /// <summary>
    /// Cards that summon every living player's Osty (LegionOfBone.OnPlay: OstyCmd.Summon for each of CombatState.PlayerCreatures),
    /// reviewed in CardCoverage.TeammateTargetModelled: the play check expects their summon on each player.
    /// </summary>
    internal static readonly HashSet<string> AllPlayersSummonCards = new() { nameof(LegionOfBone) };

    /// <summary>
    /// Cards that make every living teammate draw (HuddleUp.OnPlay: CardPileCmd.DrawWithoutBlockingOnOtherPlayers for each),
    /// not you: their draws are their card events (their draw procs), as many as their hand has room for and their piles hold.
    /// </summary>
    internal static readonly HashSet<string> TeammatesDrawCards = new() { nameof(HuddleUp) };

    /// <summary>
    /// Cards that put Shivs into every living teammate's hand, not yours (BladeSymphony.OnPlay: Shiv.CreateInHand(teammate),
    /// so the teammate is the creator: their created-card procs, Arsenal, Pillar of Creation, Regalite).
    /// </summary>
    internal static readonly HashSet<string> TeammatesShivCards = new() { nameof(BladeSymphony) };

    /// <summary>Cards aimed at a teammate whose channels go into that teammate's orbs (Ignition: OrbCmd.Channel on the target's player).</summary>
    internal static readonly HashSet<string> ChannelToAllyCards = new() { nameof(Ignition) };


    /// <summary>Cards that aren't attacks but damage their target (Capture Spirit: CreatureCmd.Damage). The card scan flags a new one.</summary>
    internal static readonly HashSet<string> TargetDamageCards = new() { nameof(CaptureSpirit) };

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

    /// <summary>The kind of orb every channel of this card makes, when it's one kind (null: mixed, random or none).</summary>
    internal static OrbKind? ChannelKind(string card) =>
        card is nameof(Tempest) or nameof(Voltaic) ? OrbKind.Lightning
        : Channels.TryGetValue(card, out var kinds) && kinds.Distinct().Count() == 1 ? kinds[0]
        : RepeatChannels.TryGetValue(card, out var k) && k != OrbKind.Unknown ? k
        : null;

    /// <summary>The simulator channels this card's orbs (for the coverage check).</summary>
    internal static bool HandlesChannel(string card) => Channels.ContainsKey(card) || RepeatChannels.ContainsKey(card) || card is nameof(Tempest) or nameof(Voltaic);

    /// <summary>Cards that channel their Repeat value of one orb kind.</summary>
    private static readonly Dictionary<string, OrbKind> RepeatChannels = new()
    {
        [nameof(ConsumingShadow)] = OrbKind.Dark, [nameof(Hibernate)] = OrbKind.Frost, [nameof(IceLance)] = OrbKind.Frost,
        [nameof(Refract)] = OrbKind.Glass, [nameof(Chaos)] = OrbKind.Unknown,
        ["TheMagician"] = OrbKind.Unknown, // random orbs (v0.113.0), after adding as many slots

    };

    /// <summary>
    /// Cards that change enemies' Strength this turn (decompile v0.111.0): the value's key ("" = 1),
    /// all enemies or the target, and the sign. The temporary ones (Piercing Wail, Dark Shackles...)
    /// apply TemporaryStrengthPower subclasses that take Strength away until the end of the turn.
    /// </summary>
    internal static readonly Dictionary<string, (string? Var, bool All, int Sign)> EnemyStrengthCards = new()
    {
        [nameof(PiercingWail)] = ("StrengthLoss", true, -1), [nameof(DarkShackles)] = ("StrengthLoss", false, -1),
        [nameof(EnfeeblingTouch)] = ("StrengthLoss", false, -1), [nameof(CrushUnder)] = ("StrengthLoss", true, -1),
        [nameof(Mangle)] = ("StrengthLoss", false, -1), [nameof(DyingStar)] = ("StrengthLoss", true, -1),
        [nameof(SharedFate)] = ("EnemyStrengthLoss", false, -1), [nameof(Resonance)] = ("", true, -1),
        [nameof(FightMe)] = ("EnemyStrength", false, 1),
    };

    // ------------------------------------------------------------------ state

    public static SimState Build(Player me, ICombatState cs, out Dictionary<Creature, int> ids)
    {
        var idMap = new Dictionary<Creature, int>();
        ids = idMap;
        var s = new SimState();
        var mates = Teammates(me, cs);
        // Any creature's simulator id (Who): an enemy's, you, or teammate k.
        int? IdOf(Creature c) => idMap.TryGetValue(c, out var i) ? i : c == me.Creature ? Who.You
            : mates.FindIndex(m => m.Creature == c) is var k and >= 0 ? Who.Teammate(k) : null;
        var targets = cs.Players.Select(p => p.Creature).ToList();

        var n = 0;
        foreach (var e in cs.Enemies)
        {
            var id = n++;
            idMap[e] = id;
            var se = new SimEnemy
            {
                Id = id,
                Name = Combat.TurnEndModel.SafeTitle(e.Monster!),
                Hp = e.CurrentHp,
                Block = e.Block,
                Hittable = e.IsAlive && e.IsHittable,
                Stunned = e.IsStunned,
                Imbalanced = e.HasPower<ImbalancedPower>(),
                Poison = SafeInt(() => e.GetPower<PoisonPower>()?.CalculateTotalDamageNextTurn() ?? 0),
                PoisonStacks = e.GetPower<PoisonPower>()?.Amount ?? 0,
                PoisonTriggers = PoisonTriggers(e, cs),
            };
            // Every power, with the numbers its model keeps (Curl Up, Sneaky, Skittish, Thorns, Burrowed, Asleep, Slumber, Flutter,
            // Plow, Shriek, The Gambit, Slippery, Hard to Kill, Intangible, Hardened Shell, Barricade, Strength, Weak...).
            se.Powers = SimReaders.Powers(e, IdOf);
            foreach (var pw in e.Powers) SimReaders.AddVars(s, id, pw);
            // Explosive: its death damages every living player (unpowered, blockable), through the game's damage math.
            if (e.IsAlive && e.GetPower<ExplosivePower>() is { Amount: > 0 } explosive) se.Explosive = ExplosiveOn(e, me.Creature, explosive.Amount, me, cs);
            se.StartedAlive = e.IsAlive;
            // An ally dying: Ravenous stuns it (and gives Strength for later); Crab Rage gives it Strength once.
            // Crab Rage: its Strength and block when an ally dies (CrabRagePower model: Strength as its amount, block here).
            if (e.GetPower<CrabRagePower>() is { } crab)
            {
                se.Powers = se.Powers.With("CrabRagePower", Sts2State.Var(crab, "StrengthPower"));
                se.CrabRageBlock = Sts2State.Var(crab, "Block");
            }
            if (!e.IsAlive) se.Hp = 0;
            if (e.IsAlive && e.Monster != null)
            {
                try
                {
                    var intent = new List<IntentHit>();
                    foreach (var attack in e.Monster.NextMove.Intents.OfType<AttackIntent>())
                    {
                        var perHit = attack.GetSingleDamage(targets, e);
                        for (var i = 0; i < Math.Max(1, attack.Repeats); i++) se.Hits.Add(perHit);
                        // v2: the hit's base and the captured terms on each player; the models (its Strength and Weak, your Vulnerable,
                        // Intangible...) apply as it lands, so what your cards change shows.
                        if (intent == null || attack.DamageCalc == null) { intent = null; continue; }
                        var b = attack.DamageCalc();
                        var terms = new Dictionary<int, CapturedTerms> { [Who.You] = SimReaders.Damage(me.RunState, cs, null, e, me.Creature, b, MegaCrit.Sts2.Core.ValueProps.ValueProp.Move) };
                        for (var k = 0; k < mates.Count; k++)
                            terms[Who.Teammate(k)] = SimReaders.Damage(me.RunState, cs, null, e, mates[k].Creature, b, MegaCrit.Sts2.Core.ValueProps.ValueProp.Move);
                        for (var i = 0; i < Math.Max(1, attack.Repeats); i++) intent.Add(new IntentHit(b) { Captured = terms });
                    }
                    if (intent is { Count: > 0 }) se.Intent = intent;
                }
                catch
                {
                    // intent unavailable: no attack
                }
            }
            s.Enemies.Add(se);
        }

        FillPlayer(s.P, me, cs, IdOf);
        foreach (var pw in me.Creature.Powers) SimReaders.AddVars(s, Who.You, pw);
        foreach (var r in me.Relics) SimReaders.AddVars(s, Who.You, r);
        FillAutoPlays(s.P, me, cs, ids);
        FillDrawPile(s.P, me, cs, ids);
        s.P.BeaconOfHope = me.Creature.HasPower<BeaconOfHopePower>();
        FillSurvival(s.P, me);

        // Teammates (multiplayer): built like you (FillPlayer), in player order. Decision #25: their hand is
        // used too (their Ethereal exhausts can kill enemies); what's shown runs without it (SimState.WithoutTeammateHands).
        // Each enemy hit's amount on each teammate goes through the game's damage math for them.
        var players = cs.Players.ToList();
        var myIndex = players.IndexOf(me);
        var teamCreatures = new List<Creature>();
        for (var i = 0; i < players.Count; i++)
        {
            var other = players[i];
            if (!IsTeammate(other, me)) continue;
            var oc = other.Creature;
            var t = new SimPlayer();
            FillPlayer(t, other, cs, IdOf);
            var mateId = Who.Teammate(s.Teammates.Count);
            foreach (var pw in oc.Powers) SimReaders.AddVars(s, mateId, pw);
            foreach (var r in other.Relics) SimReaders.AddVars(s, mateId, r);
            FillAutoPlays(t, other, cs, ids, mateId);
            FillSurvival(t, other); // their potions show in the game's expanded player view, their relics on screen
            t.BeaconOfHope = oc.HasPower<BeaconOfHopePower>();
            s.Teammates.Add(t);
            teamCreatures.Add(oc);
            if (i < myIndex) s.TeammatesBeforeMe++;
        }
        if (teamCreatures.Count > 0)
        {
            foreach (var (e, id) in ids)
            {
                if (!e.IsAlive || e.Monster == null || s.Enemy(id) is not { } se) continue;
                foreach (var tc in teamCreatures) se.TeammateHits.Add(HitsOn(e, tc, me, cs) ?? se.Hits);
                if (e.GetPower<ExplosivePower>() is { Amount: > 0 } explosive)
                    se.ExplosiveOnTeammates = teamCreatures.Select(tc => ExplosiveOn(e, tc, explosive.Amount, me, cs)).ToList();
            }
        }
        return s;
    }

    /// <summary>
    /// A player's HP, and what keeps them alive at 0 (the Death Skull, Fairy in a Bottle and Lizard Tail), the same for every
    /// player (decision #93): the heal brings them from 0 to that much (CreatureCmd.Heal; the HP setter drops the fraction).
    /// </summary>
    private static void FillSurvival(SimPlayer p, Player player)
    {
        p.Hp = player.Creature.CurrentHp;
        var maxHp = player.Creature.MaxHp;
        p.Fairies = SafeInt(() => player.Potions.Count(x => x.GetType().Name == "FairyInABottle"));
        p.FairyHeal = Math.Max(1, (int)(maxHp * 0.3m));
        p.LizardTailHeal = player.Relics.FirstOrDefault(r => r.GetType().Name == "LizardTail") is { IsUsedUp: false } tail
            ? Math.Max(1, (int)(maxHp * (Sts2State.Var(tail, "Heal", 50) / 100m)))
            : 0;
    }

    /// <summary>
    /// A player's end-of-turn auto-plays (the auto-play phase, before BeforeSideTurnEnd): Stampede and Simpleton pick from
    /// their hand, Howl from Beyond plays from their exhaust pile, and I Am Invincible from the top of their draw pile when
    /// that's certain (every card there is a copy of it). Auto-plays get no procs from the rules (Combat/CardPredictions).
    /// For a teammate (<paramref name="owner"/>), the piles only: Stampede never counts (a random card), and Simpleton's pick
    /// would need their card plays built as theirs, which only Howl from Beyond's is (reviewed: decision #80).
    /// </summary>
    private static void FillAutoPlays(SimPlayer p, Player me, ICombatState cs, IReadOnlyDictionary<Creature, int> ids, int owner = Who.You)
    {
        try
        {
            p.Stampede = owner == Who.You ? me.Creature.GetPower<StampedePower>()?.Amount ?? 0 : 0;
            p.Simpleton = owner == Who.You ? me.Creature.GetPower<SimpletonPower>()?.Amount ?? 0 : 0;
            if (p.Stampede > 0 || p.Simpleton > 0)
            {
                p.AutoHand = new List<SimHandCard>();
                foreach (var card in Sts2State.Hand(me))
                {
                    if (card.Keywords.Contains(CardKeyword.Unplayable)) continue;
                    p.AutoHand.Add(new SimHandCard
                    {
                        Id = HandId(card),
                        Name = card.GetType().Name,
                        IsAttack = card.Type == CardType.Attack,
                        Cost = card.EnergyCost.CostsX ? 999999999 : card.EnergyCost.GetResolved(),
                        Plays = AutoPlays(card, me, cs, ids),
                    });
                }
            }
            foreach (var card in PileType.Exhaust.GetPile(me).Cards)
            {
                if (card is HowlFromBeyond) p.ExhaustAutoPlays.Add(AutoPlay(card, me, cs, ids, -1, owner: owner));
            }
            var drawPile = PileType.Draw.GetPile(me).Cards;
            if (drawPile.Count > 0 && drawPile.All(c => c is IAmInvincible))
            {
                p.InvincibleBlock = CardBlock(drawPile[0], cs, me);
                p.InvincibleDrawPile = drawPile.Count;
            }
        }
        catch
        {
            // unreadable: no auto-plays predicted (the forecast check skips a turn with unpredicted auto-plays)
            p.AutoHand = null;
            p.Stampede = p.Simpleton = 0;
        }
    }

    /// <summary>
    /// Your draw and discard piles by profile (decision #90): what about each card matters if you draw it on this board, never
    /// their order (hidden; only the cards known to be on top would be: none yet). What matters: acting in hand at end of
    /// turn (always), exhausting as Ethereal (always), a Status with Iteration, Ethereal with Pagestorm, a Strike with
    /// Hellraiser, and with Stampede or Simpleton the card itself (how it plays). Everything else is one plain profile.
    /// </summary>
    private static void FillDrawPile(SimPlayer p, Player me, ICombatState cs, IReadOnlyDictionary<Creature, int> ids)
    {
        try
        {
            var iteration = me.Creature.HasPower<IterationPower>();
            var pagestorm = me.Creature.HasPower<PagestormPower>();
            var hellraiser = me.Creature.HasPower<HellraiserPower>();
            var picks = p.AutoHand != null;
            // A card in your hand that acts on the cards it draws (decision #118): what it reads goes into the profile.
            var readers = PileType.Hand.GetPile(me).Cards.Select(c => DrawnCardReaders.GetValueOrDefault(c.GetType().Name)).OfType<DrawnCardReader>().ToList();
            var readsType = readers.Any(r => r.What == DrawnCardReader.Reads.Type);
            var readsCost = readers.Any(r => r.What == DrawnCardReader.Reads.Cost);
            var autoByKey = new Dictionary<string, SimHandCard?>();
            SimDrawCard Profile(CardModel card)
            {
                var turnEnd = TurnEndOf(card);
                var fading = IsFading(card, cs);
                var hexFading = fading && EtherealOnlyFromHex(card);
                var status = iteration && card.Type == CardType.Status;
                var ethereal = pagestorm && card.Keywords.Contains(CardKeyword.Ethereal);
                var strike = hellraiser && card.Tags.Contains(CardTag.Strike);
                // Hellraiser's auto-play is built like a picked card's (decision #121), except past the game's cap on endless
                // auto-plays, when every enemy you could hit can't die (HellraiserPower: 9 a turn): then it isn't simulated.
                var hellraised = strike && !me.Creature.CombatState!.HittableEnemies.All(e => e.HpDisplay.IsInfinite());
                var pickable = picks && !card.Keywords.Contains(CardKeyword.Unplayable) && (p.Simpleton > 0 || card.Type == CardType.Attack);
                var cost = card.EnergyCost.CostsX ? 999999999 : card.EnergyCost.GetResolved();
                var who = pickable || strike ? $"{card.GetType().Name}+{card.CurrentUpgradeLevel}/{card.Enchantment?.GetType().Name}/{cost}" : "";
                // I Am Invincible plays from the top of the draw pile at the end of your turn: its block.
                var invincible = card is IAmInvincible ? CardBlock(card, cs, me) : 0;
                var perfectFit = card.Enchantment is MegaCrit.Sts2.Core.Models.Enchantments.PerfectFit;
                var type = readsType ? SimReaders.TypeOf(card) : SimCardType.None;
                bool? free = readsCost ? !card.EnergyCost.CostsX && card.EnergyCost.GetWithModifiers(CostModifiers.All) == 0 : null;
                var sly = readsCost && card.Keywords.Contains(CardKeyword.Sly);
                var key = $"{who}|{turnEnd}|{fading}{hexFading}{status}{ethereal}{strike}|{invincible}|{perfectFit}|{type}|{free}{sly}";
                SimHandCard? auto = null;
                if ((pickable || hellraised) && !autoByKey.TryGetValue(key, out auto))
                {
                    // How it plays once in your hand: as the adapter builds a hand card's auto-plays. A card in the draw pile counts as
                    // not in your hand there (its hand delta 0): one less, as for a card played from hand.
                    var plays = AutoPlays(card, me, cs, ids);
                    if (card.Pile?.Type == PileType.Draw) foreach (var play in plays) play.HandDelta -= 1;
                    auto = new SimHandCard { Name = card.GetType().Name, IsAttack = card.Type == CardType.Attack, Cost = cost, Plays = plays };
                    autoByKey[key] = auto;
                }
                return new SimDrawCard(key)
                {
                    TurnEnd = turnEnd, Fading = fading, HexFading = hexFading, Status = status, Ethereal = ethereal, Strike = strike,
                    Pickable = pickable, Auto = auto, InvincibleBlock = invincible, PerfectFit = perfectFit, Type = type, Free = free, Sly = sly,
                };
            }
            List<SimDrawGroup> Groups(IEnumerable<CardModel> cards) => cards.Select(Profile).GroupBy(c => c.Key)
                .Select(g => new SimDrawGroup(g.First(), g.Count())).OrderBy(g => g.Card.Key, StringComparer.Ordinal).ToList();
            // The cards known to be on top (placed there: decision #91), in order; the rest as a set.
            var known = SpireQoL.Combat.DrawPileMemory.KnownTop.Where(c => c.Pile?.Type == PileType.Draw && c.Owner == me).ToList();
            p.Draw = new SimDrawPile
            {
                Top = known.Select(Profile).ToList(),
                Rest = Groups(PileType.Draw.GetPile(me).Cards.Where(c => !known.Contains(c))),
                Discard = Groups(PileType.Discard.GetPile(me).Cards),
            };
            // Iteration counts the Status cards drawn this turn (CardDrawnEntry: you watched them come).
            if (iteration)
                p.StatusDrawn = CombatManager.Instance.History.Entries.OfType<MegaCrit.Sts2.Core.Combat.History.Entries.CardDrawnEntry>()
                    .Count(e => e.HappenedThisTurn(cs) && e.Actor == me.Creature && e.Card.Type == CardType.Status);
        }
        catch
        {
            p.Draw = null; // unreadable: draws are only counted, as before
        }
    }

    /// <summary>
    /// Cards that act on the cards their own draw gives (decision #118): what each reads ("type: ..." or "cost: ..."), from the
    /// result of its CardPileCmd.Draw. The draw profile carries it while one is in your hand, and the play runs as a DrawnCardAction.
    /// DrawScanTests (catalog tests) finds every card that uses what it draws, so a new one can't be read as a plain draw.
    /// </summary>
    internal static readonly IReadOnlyDictionary<string, DrawnCardReader> DrawnCardReaders = new Dictionary<string, DrawnCardReader>
    {
        ["EscapePlan"] = new(DrawnCardReader.Reads.Type, "its block only when the card it draws is a Skill"),
        ["Pillage"] = new(DrawnCardReader.Reads.Type, "draws again while the card it draws is an Attack and your hand has room", DrawsAgain: true, Counts: true),
        ["Scrape"] = new(DrawnCardReader.Reads.Cost, "discards the cards it draws unless they cost 0 (an X cost counts as not 0)", Counts: true),
    };

    /// <summary>
    /// What a card reads about the cards it draws (DrawnCardReaders). DrawsAgain: how many it draws depends on them. Counts: its chance
    /// tab shows the most likely number (Attacks drawn, cards drawn that cost 0) with its chance, not just the chance its effect happens
    /// (decision #119).
    /// </summary>
    internal sealed record DrawnCardReader(DrawnCardReader.Reads What, string Note, bool DrawsAgain = false, bool Counts = false)
    {
        internal enum Reads { Type, Cost }
    }

    /// <summary>
    /// Every game class that reacts to a card being drawn (AfterCardDrawn, AfterCardDrawnEarly), reviewed for the draw profile
    /// (decision #90): what about the drawn card it reads, and how the simulator covers it. DrawScanTests (catalog tests) fails a
    /// class that isn't here, so a new draw reaction can't merge cards the profile should keep apart.
    /// </summary>
    internal static readonly IReadOnlyDictionary<string, string> DrawReactions = new Dictionary<string, string>
    {
        ["AutomationPower"] = "any card: a count (CardEvent.Drawn)",
        ["CacophonyPower"] = "any card: a count (CardEvent.Drawn)",
        ["SpeedsterPower"] = "any card: a count (CardEvent.Drawn)",
        ["CorrosiveWavePower"] = "any card: a count (CardEvent.Drawn)",
        ["IterationPower"] = "a Status (the profile's Status, with Iteration): draws more",
        ["PagestormPower"] = "Ethereal (the profile's Ethereal, with Pagestorm): draws more",
        ["HellraiserPower"] = "a Strike (the profile's Strike, with Hellraiser): its auto-play isn't simulated; later auto-plays aren't predicted",
        ["ChainsOfBindingPower"] = "afflicts drawn cards Bound (playability, read live; nothing at end of turn)",
        ["ConfusedPower"] = "a random cost (costs matter to Simpleton only: read live in hand; a drawn card keeps the cost it had)",
        ["KinglyKick"] = "its own cost on draw (Simpleton: the cost it had in the pile)",
        ["KinglyPunch"] = "its own cost on draw (Simpleton: the cost it had in the pile)",
        ["Void"] = "loses energy when drawn (not simulated: energy only matters at end of turn to Pael's Tears)",
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
    /// A hand card's auto-plays: one per hittable enemy for a card aimed at one (CardCmd.AutoPlay picks one at random),
    /// else one. A card that draws or makes cards plays too (its hits land; the simulator stops predicting later auto-plays,
    /// whose hand it changed). None when it aims at a teammate.
    /// </summary>
    private static List<SimPlay> AutoPlays(CardModel card, Player me, ICombatState cs, IReadOnlyDictionary<Creature, int> ids)
    {
        var f = PlayFacts.For(card, me, cs, CardPlays.Count(card));
        if (card.TargetType == TargetType.AnyAlly) return new List<SimPlay>();
        if (card.TargetType != TargetType.AnyEnemy) return new List<SimPlay> { AutoPlay(card, me, cs, ids, -1, f) };
        return ids.Where(kv => kv.Key.IsAlive && kv.Key.IsHittable).Select(kv => AutoPlay(card, me, cs, ids, kv.Value, f)).ToList();
    }

    private static SimPlay AutoPlay(CardModel card, Player me, ICombatState cs, IReadOnlyDictionary<Creature, int> ids, int target, PlayFacts? f = null,
        int owner = Who.You)
    {
        var play = Play(card, f ?? PlayFacts.For(card, me, cs, CardPlays.Count(card)), me, cs, ids, target, owner);
        play.AutoPlay = true;
        if (play.Card != null) play.Card = play.Card with { AutoPlay = true };
        return play;
    }

    /// <summary>Explosive's damage on a player (ExplosivePower.AfterDeath: CreatureCmd.Damage, Unpowered | SkipHurtAnim, no dealer card).</summary>
    private static int ExplosiveOn(Creature enemy, Creature target, int amount, Player me, ICombatState cs)
        => SafeInt(() => Math.Max(0, (int)Hook.ModifyDamage(me.RunState, cs, target, enemy, amount,
            MegaCrit.Sts2.Core.ValueProps.ValueProp.Unpowered | MegaCrit.Sts2.Core.ValueProps.ValueProp.SkipHurtAnim, null, null,
            ModifyDamageHookType.All, CardPreviewMode.None, out _)), amount);

    /// <summary>
    /// Teammates are simulated (the "Multiplayer predictions (experimental)" setting, read each frame).
    /// Off: none are, so every prediction is for you alone.
    /// </summary>
    public static bool IncludeTeammates = true;

    /// <summary>Another living player in the fight: a simulated teammate (SimState.Teammates, in this order).</summary>
    internal static bool IsTeammate(Player p, Player me) => IncludeTeammates && p != me && p.Creature is { IsAlive: true } && p.PlayerCombatState != null;

    /// <summary>The simulated teammates, in player order (SimState.Teammates[k] is the k-th).</summary>
    public static List<Player> Teammates(Player me, ICombatState cs) => cs.Players.Where(p => IsTeammate(p, me)).ToList();

    /// <summary>
    /// A player's state for the simulator: block, powers, relics, hand, orbs, Osty. The same for you and
    /// for each teammate (multiplayer), so a teammate is modelled exactly like you.
    /// </summary>
    private static void FillPlayer(SimPlayer p, Player me, ICombatState cs, Func<Creature, int?> idOf)
    {
        var c = me.Creature;
        var pcs = me.PlayerCombatState!;
        var hand = Sts2State.Hand(me);
        p.Block = c.Block;
        // v2: every power and relic with the numbers its model keeps, and the turn's card plays (docs/SIMULATOR_V2.md).
        p.Powers = SimReaders.Powers(c, idOf);
        p.Relics = SimReaders.Relics(me);
        p.Turn = SimReaders.Turn(me, cs);
        p.Reflect = c.Powers.Any(pw => pw is ReflectPower);
        foreach (var r in me.Relics)
        {
            switch (r.GetType().Name)
            {
                case nameof(PaelsEye):
                    // Status Active: unused, and no card played this turn (BeforeCardPlayed sets it back); Whispering
                    // Earring's turn 1 counts as played (PaelsEye.AnyCardsPlayedThisTurn).
                    p.PaelsEyeExhaust = r.Status == MegaCrit.Sts2.Core.Entities.Relics.RelicStatus.Active
                        && !(Sts2State.TurnNumber == 1 && me.Relics.Any(x => x.GetType().Name == "WhisperingEarring"));
                    break;
                case nameof(GoldPlatedCables): p.FrontOrbBonusTriggers += 1; break; // +1 for the front orb (ModifyOrbPassiveTriggerCounts)
            }
        }

        p.HandCount = hand.Count;
        p.Energy = pcs.Energy;
        p.Arrows = SpireQoL.Procs.Fool.ArrowsAvailable(me);
        p.Fading = hand.Count(card => IsFading(card, cs));
        // Hex (decision #85): its cards' Ethereal goes when its caster dies.
        if (c.GetPower<HexPower>() is { Applier: { } hexer } && idOf(hexer) is { } hexId && hexId >= 0)
        {
            p.HexFrom = hexId;
            p.HexFading = hand.Count(card => IsFading(card, cs) && EtherealOnlyFromHex(card));
        }
        p.AttacksThisTurn = Sts2State.CountPlayedThisTurn(me, CardType.Attack);
        // Cards that act in hand at the end of the turn (the simulator's list): their Damage or HpLoss as the card shows it.
        foreach (var card in hand)
            if (TurnEndOf(card) is { } turnEnd) p.TurnEndCards.Add(turnEnd);

        p.Focus = c.GetPower<FocusPower>()?.Amount ?? 0;
        p.OrbCapacity = pcs.OrbQueue.Capacity;
        p.SlotOnFirstChannel = SafeInt(() => me.Character.BaseOrbSlotCount) == 0;
        p.Thunder = me.Creature.GetPower<ThunderPower>()?.Amount ?? 0;
        foreach (var orb in pcs.OrbQueue.Orbs) p.Orbs.Add(ToSim(orb, cs));
        // The game's trigger count includes Gold-Plated Cables' bonus on the current front orb; the
        // simulator gives that bonus to whichever orb is in front when passives fire.
        if (p.Orbs.Count > 0) p.Orbs[0].Triggers = Math.Max(1, p.Orbs[0].Triggers - p.FrontOrbBonusTriggers);

        var osty = me.Osty;
        p.OstyHp = osty != null && osty.IsAlive ? osty.CurrentHp : 0;
        p.OstyShields = osty != null && osty.HasPower<DieForYouPower>();
        p.WuppyHp = SafeInt(() => me.Creature.Pets.Where(c => c.IsAlive && c.Powers.Any(x => x.GetType().Name == "WuppysLoyaltyPower")).Sum(c => c.CurrentHp));
    }

    /// <summary>
    /// An enemy's attack hits on a player, through the game's damage math with that player as the target
    /// (their Vulnerable, Intangible, Tank...), as AttackIntent.GetSingleDamage does for you. Null if the
    /// intent can't be read.
    /// </summary>
    private static List<int>? HitsOn(Creature e, Creature target, Player me, ICombatState cs)
    {
        try
        {
            var hits = new List<int>();
            foreach (var attack in e.Monster!.NextMove.Intents.OfType<AttackIntent>())
            {
                if (attack.DamageCalc == null) return null;
                var v = Hook.ModifyDamage(me.RunState, cs, target, e, attack.DamageCalc(), MegaCrit.Sts2.Core.ValueProps.ValueProp.Move, null, null,
                    ModifyDamageHookType.All, CardPreviewMode.None, out _);
                var perHit = Math.Max(0, (int)v);
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

    private static bool IsFading(CardModel card, ICombatState cs)
    {
        try
        {
            return card.Keywords.Contains(CardKeyword.Ethereal) && (card.HasTurnEndInHandEffect || Hook.ShouldEtherealTrigger(cs, card));
        }
        catch
        {
            return false;
        }
    }

    private static SimOrb ToSim(OrbModel orb, ICombatState cs)
    {
        var kind = orb switch
        {
            LightningOrb => OrbKind.Lightning,
            FrostOrb => OrbKind.Frost,
            DarkOrb => OrbKind.Dark,
            GlassOrb => OrbKind.Glass,
            PlasmaOrb => OrbKind.Plasma,
            _ => OrbKind.Unknown,
        };
        var triggers = 1;
        try { triggers = Hook.ModifyOrbPassiveTriggerCount(cs, orb, 1, out _); } catch { /* 1 */ }
        var sim = new SimOrb { Kind = kind, Passive = (int)orb.PassiveVal, Evoke = (int)orb.EvokeVal, Triggers = triggers };
        if (orb is GlassOrb)
        {
            // Base and bonus separately: the base decays, the bonus (Focus) doesn't.
            sim.GlassBase = (int)Sts2State.Private(orb, "_passiveVal", 4m);
            sim.GlassBonus = sim.Passive - sim.GlassBase;
        }
        return sim;
    }

    // ------------------------------------------------------------------ card plays

    private static readonly Dictionary<Type, int> FocusSign = new();

    /// <summary>
    /// Focus the card gives (+) or takes (-) when played: it applies FocusPower or a temporary Focus
    /// power (sign from TemporaryFocusPower.IsPositive: Hyperbeam's is negative), by its FocusPower
    /// value or calculated focus (Synchronize). Read from the card's compiled code.
    /// </summary>
    private static int FocusChange(CardModel card)
    {
        var t = card.GetType();
        if (!FocusSign.TryGetValue(t, out var sign))
        {
            sign = 0;
            foreach (var applied in Diagnostics.CardCoverage.PowersApplied(t))
            {
                if (applied == typeof(FocusPower)) sign = 1;
                else if (typeof(TemporaryFocusPower).IsAssignableFrom(applied))
                {
                    try
                    {
                        var instance = System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(applied);
                        var positive = applied.GetProperty("IsPositive", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public)?.GetValue(instance);
                        sign = positive is false ? -1 : 1;
                    }
                    catch
                    {
                        sign = 1;
                    }
                }
            }
            FocusSign[t] = sign;
        }
        if (sign == 0) return 0;
        decimal amount = 0;
        try
        {
            if (card.DynamicVars.TryGetValue("FocusPower", out var v)) amount = v.BaseValue;
            else if (card.DynamicVars.TryGetValue("CalculatedFocus", out var cv) && cv is CalculatedVar calc) amount = calc.Calculate(null);
        }
        catch
        {
            amount = 0;
        }
        return sign * (int)amount;
    }

    /// <summary>
    /// The card's block through the game's block math. gainer: who gets it (you by default; a targeted teammate for
    /// Lift); calcTarget: the target a calculated value is worked out on (Mimic, Demonic Shield aimed at a teammate).
    /// </summary>
    public static int CardBlock(CardModel card, ICombatState cs, Player me, Creature? gainer = null, Creature? calcTarget = null)
    {
        try
        {
            decimal raw;
            if (card.DynamicVars.TryGetValue("CalculatedBlock", out var cv) && cv is CalculatedVar calc) raw = calc.Calculate(calcTarget);
            else if (card.DynamicVars.TryGetValue("Block", out var bv)) raw = bv.BaseValue;
            else return 0;
            if (raw <= 0) return 0;
            return Math.Max(0, (int)Hook.ModifyBlock(cs, gainer ?? me.Creature, raw, MegaCrit.Sts2.Core.ValueProps.ValueProp.Move, card, null, out _));
        }
        catch
        {
            return 0;
        }
    }

    private static int SafeInt(Func<int> f)
    {
        try { return f(); } catch { return 0; }
    }

    private static int SafeInt(Func<int> f, int fallback)
    {
        try { return f(); } catch { return fallback; }
    }

    private static string SafeCardTitle(CardModel c)
    {
        try { return c.Title; } catch { return c.GetType().Name; }
    }
}
