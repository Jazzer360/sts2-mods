using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Combat.History.Entries;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.ValueProps;
using Sts2Mods.Core.Game;

namespace SpireQoL.Sim;

/// <summary>
/// Reads the game into the simulator's inputs (docs/SIMULATOR.md "The adapter"): creatures' powers and players' relics
/// with the numbers each model keeps, players' turn counters, cards, and the captured terms of every damage or block source
/// the simulator doesn't model. State only: the rules are the models'.
/// </summary>
public static class SimReaders
{
    // ------------------------------------------------------------------ creatures

    /// <summary>
    /// A creature's powers, in order, each with the Data its model keeps. Every power goes in (a model reads others by name:
    /// Vulnerable reads Debilitate); unmodelled ones are only names and amounts.
    /// </summary>
    public static PowerSet Powers(Creature creature, Func<Creature, int?> idOf)
    {
        var set = PowerSet.Empty;
        foreach (var power in creature.Powers)
        {
            // A temporary Thorns power (Cho Barbs) only tracks how much Thorns to take back after the enemy turn: the Thorns
            // itself is already in the owner's ThornsPower (TemporaryThornsPower.BeforeApplied applies it).
            if (SimAdapter.IsTemporaryThorns(power.GetType())) continue;
            var name = SimAdapter.SimPowerName(power.GetType());
            // Withering Presence: Aeonglass has one per player, each counting its Target's cards (AfterCardPlayed:
            // cardPlay.Card.Owner == Target.Player). Only yours reacts to your plays; a teammate's would mix in their count.
            if (name == nameof(WitheringPresencePower) && power.Target is { } otherCreature && idOf(otherCreature) != Who.You) continue;
            // Sic 'Em is one instance per applier (InstancedPerApplier): only yours summons for your Osty's hits. A teammate's reacts to their
            // Osty, whose hits aren't part of your turn's simulation, and would otherwise be read as yours.
            if (name == nameof(SicEmPower) && (power.Applier is not { } sicEmApplier || idOf(sicEmApplier) != Who.You)) continue;
            var (readName, amount, data) = Read(power, name, idOf);
            // After the read: a teammate's Impaled is its own power (ImpaledPowerOther).
            var id = new PowerId(readName);
            // Copies whose Data holds each one's own slot (a teammate's Impaled, each Tag Team's applier) are or'd together.
            set = set.Has(id) ? set.With(id, set[id] + amount, id == SimEngine.ImpaledOther || id == PowerId.TagTeam ? set.DataOf(id) | data : data) : set.With(id, amount, data);
        }
        return set;
    }

    /// <summary>
    /// One power as the simulator reads it: its name (a teammate's Impaled apart: ImpaledPowerOther), its amount and the Data its model
    /// keeps (a flag, a count, a creature). The board probe hashes the same read (decision #130), so a change in what's read here is a
    /// change it sees.
    /// </summary>
    internal static (string Name, int Amount, int Data) Read(PowerModel power, string name, Func<Creature, int?> idOf)
    {
        int amount = power.Amount, data = 0;
        switch (name)
        {
            // Its applier's amount in their slot (one copy per applier, instanced): their own Attacks don't play again (TagTeamPower.ModifyCardPlayCount).
            case nameof(TagTeamPower):
                data = Models.TagTeamPower.DataFor(power.Applier is { } tagger && idOf(tagger) is { } taggerId ? taggerId : Who.None, amount);
                break;
            // Your arrows in it (Amount) and its hit counter (Data); a teammate's Impaled only makes it Impaled.
            case nameof(ImpaledPower) when power.Applier is { } impaler && idOf(impaler) == Who.You:
                amount = SpireQoL.Procs.Fool.ArrowsIn(power);
                data = SpireQoL.Procs.Fool.HitCounter(power);
                break;
            // A teammate's arrows in it: 4 bits of Data per teammate (index from their id); amount: arrows in all (it stays Impaled).
            case nameof(ImpaledPower):
                name = "ImpaledPowerOther";
                amount = SpireQoL.Procs.Fool.ArrowsIn(power);
                data = power.Applier is { } nextCreature && idOf(nextCreature) is { } taId && Who.TeammateIndex(taId) is var teammateIndex and >= 0 and < 7 ? Math.Min(15, amount) << (4 * teammateIndex) : 0;
                amount = Math.Max(1, amount);
                break;
            case nameof(SlowPower): data = Sts2State.Var(power, "SlowAmount", fallback: 0); break;
            case nameof(SkittishPower): data = Sts2State.Private(power, "HasGainedBlockThisTurn", fallback: false) ? Models.SkittishPower.UsedThisTurn : 0; break;
            case nameof(HardenedShellPower): data = Math.Max(0, power.DisplayAmount); break;
            case nameof(FlutterPower): data = Sts2State.Var(power, "DamageDecrease", fallback: 50); break;
            case nameof(SurroundedPower): data = Sts2State.Private<object?>(power, "Facing", fallback: null)?.ToString() == "Left" ? Models.SurroundedPower.FacingLeft : Models.SurroundedPower.FacingRight; break;
            // The players covering it, over every copy (one per Intercept played on it): a bit each.
            case nameof(CoveredPower):
                data = power.Owner.Powers.Where(copy => copy is CoveredPower && copy.Applier != null)
                    .Aggregate(0, (bits, copy) => bits | (idOf(copy.Applier!) is { } coverer ? Models.CoveredPower.CovererBit(coverer) : 0));
                break;
            // Counters the proc models count down (the power's own number: DisplayAmount, or its CardsLeft value).
            // Feral: its zeroCostAttacksPlayed (its own count: the model compares it with its amount, which more Feral raises).
            case nameof(FeralPower): data = Sts2State.Private<object?>(power, "_internalData", fallback: null) is { } feral ? Sts2State.Private(feral, "zeroCostAttacksPlayed", fallback: 0) : Math.Max(0, power.Amount - power.DisplayAmount); break;
            case nameof(WitheringPresencePower): data = Math.Max(1, Sts2State.Var(power, "CardsLeft", fallback: 5)); break;
            // Once a turn (PowerUse.UsedThisTurn: already this turn).
            case nameof(PaleBlueDotPower): data = Internal(power, "alreadyActivatedThisTurn") ? (int)PowerUse.UsedThisTurn : 0; break;
            case nameof(LullabyPower): data = Sts2State.Private(power, "_proccdThisTurn", fallback: true) ? (int)PowerUse.UsedThisTurn : 0; break;
            case nameof(YahPower): data = Internal(power, "triggeredThisTurn") ? (int)PowerUse.UsedThisTurn : 0; break;
            // Bound to the attack under way (BeforeAttack set commandToModify): every other card's damage gets nothing from it.
            case nameof(VigorPower) or nameof(AimPower) or nameof(QuietusPower) or nameof(GigantificationPower):
                data = Sts2State.Private<object?>(power, "_internalData", fallback: null) is { } bound && Sts2State.Private<object?>(bound, "commandToModify", fallback: null) != null
                    ? Models.AttackBoundPower.BoundOutside : 0;
                break;
            case nameof(JugglingPower): data = Sts2State.Private<object?>(power, "_internalData", fallback: null) is { } juggled ? Sts2State.Private(juggled, "attacksPlayedThisTurn", fallback: 0) : 0; break;
            // Whose plays it acts on (DataCreature: 0 is no one).
            // Gone with their applier (AfterDeath), Magic Bomb only while it lives: the applier.
            case nameof(MagicBombPower): data = DataCreature.Of(power.Applier is { } caster ? idOf(caster) : null); break;
            case nameof(OblivionPower): data = DataCreature.Of(power.Applier is { } strangler ? idOf(strangler) : null); break;
            // One copy per player it copies (ImitationLearning adds to the copy already copying its target), merged into one entry:
            // you when any copy copies you, so your Power cards set it off whichever copy is read last.
            case nameof(ImitationLearningPower):
            {
                int? CopiedBy(PowerModel copy) => copy is MegaCrit.Sts2.Core.Models.Powers.ImitationLearningPower imitationLearningPower
                    && Combat.CardPredictions.CopiesFrom(imitationLearningPower)?.Creature is { } copied ? idOf(copied) : null;
                var copiesYou = power.Owner.Powers.Any(copy => copy is MegaCrit.Sts2.Core.Models.Powers.ImitationLearningPower && CopiedBy(copy) == Who.You);
                data = DataCreature.Of(copiesYou ? Who.You : CopiedBy(power));
                break;
            }
            case nameof(InterceptPower):
            {
                // The players it covers (InterceptPower's internal coveredCreatures): Intercept multiplies by count + 1.
                var internalData = Sts2State.Private<object?>(power, "_internalData", fallback: null);
                var covered = internalData == null ? null : Sts2State.Private<System.Collections.ICollection?>(internalData, "coveredCreatures", fallback: null);
                amount = covered?.Count ?? 1;
                break;
            }
        }
        // A power read from its code that asks who applied it (base.Applier: Constrict's AfterDeath) keeps its applier as its Data (decision #195).
        if (SimModels.Get(name) is ReadModel { UsesApplier: true }) data = DataCreature.Of(power.Applier is { } applier ? idOf(applier) : null);
        return (name, amount, data);
    }

    /// <summary>A flag in a power's internal data (PowerModel._internalData), true when unreadable (so a once-a-turn proc isn't promised).</summary>
    private static bool Internal(PowerModel power, string field) =>
        Sts2State.Private<object?>(power, "_internalData", fallback: null) is not { } internalData || Sts2State.Private(internalData, field, fallback: true);

    /// <summary>
    /// The values of every relic and power of these creatures and players (their DynamicVars) for SimState.Vars: "id:Name:Var",
    /// and its name as the game shows it (SimState.Titles),
    /// with a BlockVar's or DamageVar's props as "id:Name:Var.Props". An instanced power's copies (each Monologue played) each
    /// have their own values: "id:Name:Var" is the last copy's, "id:Name:Var.Total" the sum over them (SimEngine.VarTotal).
    /// Called once for each of a creature's powers, so the totals count each copy once.
    /// </summary>
    public static void AddVars(SimState state, int id, AbstractModel model)
    {
        var vars = state.Vars;
        var name = ModelName(model);
        try { state.Titles[$"{id}:{name}"] = Combat.TurnEndModel.SafeTitle(model); } catch { /* no title: its class name */ }
        try
        {
            var dv0 = model switch { RelicModel relic => relic.DynamicVars, PowerModel power => power.DynamicVars, _ => null };
            if (dv0 == null) return;
            var instanced = model is PowerModel { InstanceType: MegaCrit.Sts2.Core.Entities.Powers.PowerInstanceType.Instanced };
            foreach (var (key, dynamicVar) in dv0)
            {
                vars[$"{id}:{name}:{key}"] = dynamicVar.IntValue;
                if (dynamicVar.BaseValue != dynamicVar.IntValue) state.Fractions[$"{id}:{name}:{key}"] = dynamicVar.BaseValue;
                if (instanced)
                {
                    var totalKey = $"{id}:{name}:{key}{SimEngine.TotalSuffix}";
                    vars[totalKey] = vars.GetValueOrDefault(totalKey) + dynamicVar.IntValue;
                }
                if (dynamicVar is MegaCrit.Sts2.Core.Localization.DynamicVars.BlockVar blockVar) vars[$"{id}:{name}:{key}.Props"] = (int)blockVar.Props;
                else if (dynamicVar is MegaCrit.Sts2.Core.Localization.DynamicVars.DamageVar damageVar) vars[$"{id}:{name}:{key}.Props"] = (int)damageVar.Props;
            }
        }
        catch
        {
            // no values readable: the models' fallbacks
        }
    }

    /// <summary>
    /// Relics whose model's amount is one of the relic's values (DynamicVars, with the fallback when it can't be read): Relics reads
    /// that value into the amount, and ModelAmountCheck reads the model's Amount as it.
    /// </summary>
    internal static readonly Dictionary<string, (string Var, int Fallback)> RelicAmountVars = new()
    {
        [nameof(MightyWings)] = Var("StrengthPower", fallback: 2),
        [nameof(TungstenRod)] = Var("HpLossReduction", fallback: 1),
        [nameof(BeatingRemnant)] = Var("MaxHpLoss", fallback: int.MaxValue),
        [nameof(TheBoot)] = Var("DamageMinimum", fallback: 5),
        [nameof(SneckoSkull)] = Var("PoisonPower", fallback: 1),
        // End-of-turn relics: their block, damage or stars.
        [nameof(RippleBasin)] = Var("Block"),
        [nameof(ParryingShield)] = Var("Damage"),
    };

    /// <summary>A relic's value by its DynamicVars key, and what to take when it can't be read.</summary>
    private static (string Var, int Fallback) Var(string key, int fallback = 0) => (key, fallback);

    /// <summary>A player's relics in relic order (melted ones don't listen), each with the numbers its model keeps.</summary>
    public static RelicSet Relics(Player player)
    {
        var set = RelicSet.Empty;
        foreach (var relic in player.Relics)
        {
            if (relic.IsMelted) continue;
            var name = relic.GetType().Name;
            var amountIsVar = RelicAmountVars.TryGetValue(name, out var amountVar);
            int amount = amountIsVar ? Sts2State.Var(relic, amountVar.Var, amountVar.Fallback) : 0, data = 0;
            switch (name)
            {
                case nameof(PenNib):
                    amount = Sts2State.Private(relic, "AttacksPlayed", fallback: 0);
                    data = Sts2State.Private<object?>(relic, "AttackToDouble", fallback: null) != null ? Models.PenNib.GameCardBound : 0;
                    break;
                case nameof(MightyWings): data = Sts2State.Var(relic, "DamageThreshold", fallback: 30); break;
                case nameof(Vambrace):
                    data = (Sts2State.Private<object?>(relic, "TriggeringCard", fallback: null) != null ? Models.Vambrace.Bound : 0) | (Sts2State.Private(relic, "BlockGainedThisCombat", fallback: false) ? Models.Vambrace.GainedThisCombat : 0);
                    break;
                case nameof(PaelsLegion):
                    amount = Sts2State.Private(relic, "Cooldown", fallback: 0);
                    data = Sts2State.Private<object?>(relic, "AffectedCardPlay", fallback: null) != null ? Models.PaelsLegion.BoundToPlay : 0;
                    break;
                case nameof(CentennialPuzzle): amount = Sts2State.Private(relic, "UsedThisCombat", fallback: false) ? 1 : 0; break; // used up this combat
                case nameof(Pocketwatch): amount = Sts2State.Private(relic, "_cardsPlayedThisTurn", fallback: 0); data = Sts2State.Var(relic, "CardThreshold", fallback: 3); break;
                case nameof(BeatingRemnant): data = (int)Sts2State.Private(relic, "DamageReceivedThisTurn", fallback: 0m); break;
                // Counters the models keep counting (their tray number, or the private count behind it).
                case nameof(JossPaper): amount = Sts2State.Private(relic, "CardsExhausted", fallback: 0); break;
                case nameof(Metronome): amount = Sts2State.Private(relic, "OrbsChanneled", int.MaxValue / 2); break;
                // Once a turn: armed until used, used this turn (RelicUse).
                case nameof(MiniRegent) or nameof(Regalite): data = Sts2State.Private(relic, "UsedThisTurn", fallback: true) ? 0 : (int)RelicUse.Armed; break;
                case nameof(RainbowRing): data = Sts2State.Private(relic, "ActivationCountThisTurn", fallback: 0) >= 1 ? (int)RelicUse.UsedThisTurn : 0; break;
                case nameof(DemonTongue): data = Sts2State.Private(relic, "_triggeredThisTurn", fallback: true) ? (int)RelicUse.UsedThisTurn : 0; break;
                // The HP at or below which it gives Strength (its threshold of your max HP).
                case nameof(RedSkull):
                    amount = (int)Math.Floor(player.Creature.MaxHp * Sts2State.Var(relic, "HpThreshold", fallback: 50) / 100m);
                    data = Sts2State.Private(relic, "StrengthApplied", fallback: true) ? Models.RedSkull.StrengthGiven : 0; // its Strength already given
                    break;
                // End-of-turn relics (their block, damage or stars: RelicAmountVars), and what they wait for (Data).
                case nameof(ParryingShield): data = Sts2State.Var(relic, "Block"); break;
                // Its Data is the model's own: energy spent during the play being simulated (PaelsTears), so none yet.
                case nameof(PaelsTears): data = 0; break;
                case nameof(UnsettlingLamp):
                    data = Sts2State.Private<object?>(relic, "TriggeringCard", fallback: null) == null && !Sts2State.Private(relic, "IsFinishedTriggering", fallback: true) ? (int)RelicUse.Armed : 0;
                    break;
                default:
                    if (amountIsVar) break; // its amount is its value (RelicAmountVars), and it keeps no data
                    amount = Math.Max(0, relic.DisplayAmount);
                    data = Features.RelicCountersFeature.IsArmed(relic) ? (int)RelicUse.Armed : 0;
                    break;
            }
            set = set.With(new RelicId(name), amount, data);
        }
        return set;
    }

    /// <summary>
    /// The cards in a player's piles that listen for a hook the simulator models (SimPlayer.Cards), in the game's listener order
    /// (every pile, hand first: PlayerCombatState.AllCards), with how many copies.
    /// </summary>
    public static CardSet Cards(Player player)
    {
        var set = CardSet.Empty;
        foreach (var card in player.PlayerCombatState?.AllCards ?? Enumerable.Empty<CardModel>())
            if (CardListenerOf(card) is { } listener) set = set.Add(listener, delta: 1);
        return set;
    }

    /// <summary>
    /// The card model a card listens as: its class's (a CardListener named after it), or the model of a card whose own code does what
    /// its code does (it loses energy when drawn: Void's, CardEnergy.LostWhenDrawn); null for a card with neither.
    /// </summary>
    public static CardId? CardListenerOf(CardModel card)
    {
        var name = card.GetType().Name;
        if (SimModels.Get(name) is CardListener) return new CardId(name);
        return CardEnergy.LostWhenDrawn(card) > 0 ? CardId.Void : null;
    }

    /// <summary>
    /// A player's turn counters (SimPlayer.Turn) from the combat history: card plays started and finished this turn (all, by
    /// type, by tag), and block gains from their card plays (Unmovable).
    /// </summary>
    public static NamedCounts Turn(Player player, ICombatState combat)
    {
        var counts = NamedCounts.Empty;
        try
        {
            var history = CombatManager.Instance.History;
            foreach (var cardPlayStartedEntry in history.CardPlaysStarted)
            {
                if (!cardPlayStartedEntry.HappenedThisTurn(combat) || cardPlayStartedEntry.CardPlay.Player != player) continue;
                counts = SimKeys.CountPlay(counts, PlayStage.Started, TypeOf(cardPlayStartedEntry.CardPlay.Card), TagsOf(cardPlayStartedEntry.CardPlay.Card));
            }
            foreach (var cardPlayFinishedEntry in history.CardPlaysFinished)
            {
                if (!cardPlayFinishedEntry.HappenedThisTurn(combat) || cardPlayFinishedEntry.CardPlay.Player != player) continue;
                counts = SimKeys.CountPlay(counts, PlayStage.Finished, TypeOf(cardPlayFinishedEntry.CardPlay.Card), TagsOf(cardPlayFinishedEntry.CardPlay.Card));
            }
            var blocks = history.Entries.OfType<BlockGainedEntry>()
                .Count(blockGainedEntry => blockGainedEntry.HappenedThisTurn(combat) && blockGainedEntry.CardPlay != null && blockGainedEntry.CardPlay.Player == player && blockGainedEntry.Props.IsCardOrMonsterMove());
            if (blocks > 0) counts = counts.Add(SimKeys.CardBlock, blocks);
        }
        catch
        {
            // history unavailable: no counters
        }
        return counts;
    }

    // ------------------------------------------------------------------ cards

    public static SimCardType TypeOf(CardModel card) => card.Type switch
    {
        CardType.Attack => SimCardType.Attack,
        CardType.Skill => SimCardType.Skill,
        CardType.Power => SimCardType.Power,
        CardType.Status => SimCardType.Status,
        CardType.Curse => SimCardType.Curse,
        _ => SimCardType.None,
    };

    /// <summary>The card as models see it, with what its play does that models ask about (stars, exhaust, a shot with an arrow, the rest of your hand).</summary>
    public static SimCard Card(CardModel card, Player me)
    {
        var simCard = Card(card, SpireQoL.Procs.CardPlays.EnergyOf(card));
        bool Safe(Func<bool> read) { try { return read(); } catch { return false; } }
        return simCard with
        {
            Stars = SpireQoL.Procs.CardPlays.StarsOf(card),
            StarCostX = Safe(() => card.HasStarCostX),
            // Its own exhaust (the Exhaust keyword, added ones included; not a Power or a copy): a model that sends it there (Corruption) is
            // the simulator's (decision #192).
            ExhaustsSelf = Safe(() => card.Type != CardType.Power && !card.IsDupe && (card.Keywords.Contains(CardKeyword.Exhaust) || card.ExhaustOnNextPlay)),
            ExhaustEnergy = CardEnergy.WhenExhausted(card),
            Targets = card.TargetType switch
            {
                TargetType.AnyEnemy => SimPlay.Target.Single,
                TargetType.AllEnemies => SimPlay.Target.All,
                TargetType.RandomEnemy => SimPlay.Target.Random,
                _ => SimPlay.Target.None,
            },
            Dupe = Safe(() => card.IsDupe),
            Upgradable = Safe(() => card.IsUpgradable),
            OthersInHandCost = Safe(() => Sts2State.Hand(me).Any(nextCard => nextCard != card && nextCard.CostsEnergyOrStars(includeGlobalModifiers: true))),
        };
    }

    /// <summary>A card's tags as the simulator's enum, matched by name (the game's CardTag numbers differ between its branches).</summary>
    public static IEnumerable<SimCardTag> TagsOf(CardModel card)
    {
        var tags = new List<SimCardTag>();
        try
        {
            foreach (var cardTag in card.Tags)
                if (Enum.TryParse<SimCardTag>(cardTag.ToString(), out var tag)) tags.Add(tag);
        }
        catch
        {
            // none
        }
        return tags;
    }

    /// <summary>The card as models see it: its class name, type, tags, values, and flags.</summary>
    public static SimCard Card(CardModel card, int energy)
    {
        var vars = new Dictionary<string, decimal>();
        try { foreach (var (k, dynamicVar) in card.DynamicVars) vars[k] = dynamicVar.BaseValue; } catch { /* none */ }
        var tags = TagsOf(card).ToHashSet();
        var affliction = card.Affliction == null ? SimAffliction.None
            : Enum.TryParse<SimAffliction>(card.Affliction.GetType().Name, out var simAffliction) ? simAffliction : SimAffliction.Other;
        bool gainsBlock;
        // Read by name (Fool.StretchCard): EnchantmentModel.GainsBlock is beta-only (v0.113.1), and naming it here made this whole
        // method fail to compile on main (MissingMethodException: no card simulated at all).
        try { gainsBlock = SpireQoL.Procs.Fool.StretchCard(card); } catch { gainsBlock = false; }
        return new SimCard(card.GetType().Name, TypeOf(card))
        {
            Tags = tags,
            Affliction = affliction,
            Upgraded = card.IsUpgraded,
            Enchanted = card.Enchantment != null,
            Ethereal = card.Keywords.Contains(CardKeyword.Ethereal),
            Energy = energy,
            CostsX = card.EnergyCost.CostsX,
            HandId = card.Pile?.Type == PileType.Hand ? SimAdapter.HandId(card) : 0,
            Vars = vars,
            GainsBlock = gainsBlock,
        };
    }

    // ------------------------------------------------------------------ captured terms

    /// <summary>A model's late damage multiplier (AbstractModel.ModifyDamageMultiplicativeLate): beta only (decision #163); main has no such hook.</summary>
    [BetaOnly, System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static decimal LateTerm(AbstractModel model, Creature? target, decimal amount, ValueProp props, Creature? dealer, CardModel? card) =>
        model.ModifyDamageMultiplicativeLate(target, amount, props, dealer, card, null);

    internal static readonly Func<AbstractModel, Creature?, decimal, ValueProp, Creature?, CardModel?, decimal>? LateMultiplier =
        BetaOnly.Gate<Func<AbstractModel, Creature?, decimal, ValueProp, Creature?, CardModel?, decimal>>(LateTerm);

    /// <summary>
    /// The terms of one damage calculation that the simulator doesn't model (docs/SIMULATOR.md "Pipelines"): the card's
    /// enchantment, and every hook listener whose class isn't modelled, each asked for its own term the way Hook.ModifyDamage
    /// asks it (with this card, dealer and target).
    /// </summary>
    public static CapturedTerms Damage(IRunState runState, ICombatState combat, CardModel? card, Creature? dealer, Creature? target, decimal baseDamage, ValueProp props)
    {
        decimal enchantAdd = 0m, enchantMul = 1m, add = 0m, mul = 1m, late = 1m, cap = decimal.MaxValue;
        try
        {
            if (card?.Enchantment is { } ench)
            {
                enchantAdd = ench.EnchantDamageAdditive(baseDamage, props);
                enchantMul = ench.EnchantDamageMultiplicative(baseDamage + enchantAdd, props);
            }
            var listeners = runState.IterateHookListeners(combat).Where(model => !SimModels.Has(ModelName(model))).ToList();
            foreach (var model in listeners) add += model.ModifyDamageAdditive(target, baseDamage, props, dealer, card, null);
            foreach (var model in listeners) mul *= model.ModifyDamageMultiplicative(target, baseDamage, props, dealer, card, null);
            if (LateMultiplier is { } lateMultiplier) foreach (var model in listeners) late *= lateMultiplier(model, target, baseDamage, props, dealer, card);
            foreach (var model in listeners) cap = Math.Min(cap, model.ModifyDamageCap(target, props, dealer, card, null));
        }
        catch
        {
            // a listener that can't answer outside a real calculation: nothing captured from it
        }
        return new CapturedTerms { EnchantAdd = enchantAdd, EnchantMul = enchantMul, Add = add, Mul = mul, Late = late, Cap = cap };
    }

    /// <summary>The same for block (Hook.ModifyBlock): the enchantment's, and every unmodelled listener's.</summary>
    public static CapturedTerms Block(ICombatState combat, CardModel? card, Creature gainer, decimal baseBlock, ValueProp props)
    {
        decimal enchantAdd = 0m, enchantMul = 1m, add = 0m, mul = 1m;
        try
        {
            if (card?.Enchantment is { } ench)
            {
                enchantAdd = ench.EnchantBlockAdditive(baseBlock);
                enchantMul = ench.EnchantBlockMultiplicative(baseBlock + enchantAdd);
            }
            var listeners = combat.IterateHookListeners().Where(model => !SimModels.Has(ModelName(model))).ToList();
            foreach (var model in listeners) add += model.ModifyBlockAdditive(gainer, baseBlock, props, card, null);
            foreach (var model in listeners) mul *= model.ModifyBlockMultiplicative(gainer, baseBlock, props, card, null);
        }
        catch
        {
            // nothing captured
        }
        return new CapturedTerms { EnchantAdd = enchantAdd, EnchantMul = enchantMul, Add = add, Mul = mul };
    }

    /// <summary>
    /// What the models read from their code keep of their own (SimPlayer.Fields, SimEnemy.Fields, decision #195): each field or property a read
    /// model's programs read or store, read from the game's object (a count or a flag a player could keep: Kunai's attacks this turn).
    /// </summary>
    public static NamedCounts Fields(IEnumerable<AbstractModel> models, Func<Creature, int?>? idOf = null)
    {
        var fields = NamedCounts.Empty;
        var copies = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var model in models)
        {
            if (SimModels.Get(ModelName(model)) is not ReadModel read) continue;
            // A power kept in copies: each instance on the creature, in the game's order, is a copy of its own (its amount, its applier, its state).
            var copy = 0;
            if (read.HasCopies && model is PowerModel power)
            {
                copy = copies[read.Name] = copies.GetValueOrDefault(read.Name) + 1;
                var applier = power.Applier is { } applierCreature && idOf?.Invoke(applierCreature) is { } applierId ? applierId : Who.None;
                fields = fields.With(read.Name + "@" + copy.ToString(System.Globalization.CultureInfo.InvariantCulture), power.Amount, DataCreature.Of(applier));
            }
            foreach (var field in read.Spec.Fields())
                fields = fields.With(copy > 0 ? read.CopyFieldKey(copy, field) : read.FieldKey(field), FieldOf(model, field) switch
                {
                    int number => number,
                    bool flag => flag ? 1 : 0,
                    decimal number => (int)number,
                    _ => 0,
                });
        }
        return fields;
    }

    /// <summary>
    /// A read model's field on the game's object: its own, a plain field of its own data ("Data.etherealCount": its _internalData's), or one of
    /// its own values its code stores ("Var.CardsLeft": DynamicVars["CardsLeft"]).
    /// </summary>
    private static object? FieldOf(AbstractModel model, string field) =>
        field.StartsWith(Procs.Programs.ProgramReader.OwnDataPrefix, StringComparison.Ordinal)
            ? Sts2State.Private<object?>(model, "_internalData", fallback: null) is { } data ? Sts2State.Private<object?>(data, field[Procs.Programs.ProgramReader.OwnDataPrefix.Length..], fallback: null) : null
            : field.StartsWith(HookReader.VarFieldPrefix, StringComparison.Ordinal) ? Sts2State.Var(model, field[HookReader.VarFieldPrefix.Length..])
            : Sts2State.Private<object?>(model, field, fallback: null);

    /// <summary>The name a game model has in the simulator (temporary Thorns is Thorns).</summary>
    public static string ModelName(AbstractModel model) => model is PowerModel ? SimAdapter.SimPowerName(model.GetType()) : model.GetType().Name;

    public static SimProps Props(ValueProp valueProp) => (SimProps)(int)valueProp;

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<Type, bool> DebuffCache = new();

    public static bool IsDebuff(Type powerType) => !typeof(ITemporaryPower).IsAssignableFrom(powerType) && IsDebuffType(powerType);

    public static bool IsTemporaryDebuff(Type powerType) => typeof(ITemporaryPower).IsAssignableFrom(powerType) && IsDebuffType(powerType);

    /// <summary>
    /// The power is a debuff by type (PowerModel.Type), temporary ones included (Dark Shackles):
    /// what Artifact blocks (ArtifactPower.TryModifyPowerAmountReceived).
    /// </summary>
    public static bool IsDebuffType(Type powerType)
    {
        if (DebuffCache.TryGetValue(powerType, out var isDebuff)) return isDebuff;
        isDebuff = false;
        try
        {
            var canonical = typeof(ModelDb).GetMethod(nameof(ModelDb.Power))!.MakeGenericMethod(powerType).Invoke(null, null) as PowerModel;
            isDebuff = canonical?.Type == PowerType.Debuff;
        }
        catch
        {
            // unknown: not a debuff
        }
        DebuffCache[powerType] = isDebuff;
        return isDebuff;
    }
}
