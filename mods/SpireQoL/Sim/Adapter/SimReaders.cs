using System.Reflection;
using MegaCrit.Sts2.Core.Commands.Builders;
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
            case nameof(SkittishPower): data = Sts2State.Private(power, "HasGainedBlockThisTurn", fallback: false) ? Models.SkittishPower.UsedThisTurn : 0; break;
            case nameof(SurroundedPower): data = Sts2State.Private<object?>(power, "Facing", fallback: null)?.ToString() == "Left" ? Models.SurroundedPower.FacingLeft : Models.SurroundedPower.FacingRight; break;
            // The players covering it, over every copy (one per Intercept played on it): a bit each. A loop: a lambda here capturing idOf made a
            // closure on every read of every power (the board probe reads each power every frame: a dev review's steady garbage).
            case nameof(CoveredPower):
                var copies = power.Owner.Powers;
                for (var i = 0; i < copies.Count; i++)
                    if (copies[i] is CoveredPower copy && copy.Applier is { } coverApplier && idOf(coverApplier) is { } coverer) data |= Models.CoveredPower.CovererBit(coverer);
                break;
            // Counters the proc models count down (the power's own number: DisplayAmount, or its CardsLeft value).
            // Feral: its zeroCostAttacksPlayed (its own count: the model compares it with its amount, which more Feral raises).
            case nameof(FeralPower): data = Sts2State.Private<object?>(power, "_internalData", fallback: null) is { } feral ? Sts2State.Private(feral, "zeroCostAttacksPlayed", fallback: 0) : Math.Max(0, power.Amount - power.DisplayAmount); break;
            case nameof(WitheringPresencePower): data = Math.Max(1, Sts2State.Var(power, "CardsLeft", fallback: 5)); break;
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

    /// <summary>A player's relics in relic order (melted ones don't listen), each with the numbers its model keeps.</summary>
    public static RelicSet Relics(Player player)
    {
        var set = RelicSet.Empty;
        foreach (var relic in player.Relics)
        {
            if (relic.IsMelted) continue;
            var name = relic.GetType().Name;
            int amount = 0, data = 0;
            switch (name)
            {
                case nameof(Pocketwatch): amount = Sts2State.Private(relic, "_cardsPlayedThisTurn", fallback: 0); data = Sts2State.Var(relic, "CardThreshold", fallback: 3); break;
                // The HP at or below which it gives Strength (its threshold of your max HP).
                case nameof(RedSkull):
                    amount = (int)Math.Floor(player.Creature.MaxHp * Sts2State.Var(relic, "HpThreshold", fallback: 50) / 100m);
                    data = Sts2State.Private(relic, "StrengthApplied", fallback: true) ? Models.RedSkull.StrengthGiven : 0; // its Strength already given
                    break;
                // Its Data is the model's own: energy spent during the play being simulated (PaelsTears), so none yet.
                case nameof(PaelsTears): data = 0; break;
                case nameof(UnsettlingLamp):
                    data = Sts2State.Private<object?>(relic, "TriggeringCard", fallback: null) == null && !Sts2State.Private(relic, "IsFinishedTriggering", fallback: true) ? (int)RelicUse.Armed : 0;
                    break;
                default:
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
            // Each card play that gained block once (UnmovablePower, v0.114.0: DistinctBy the entries' CardPlay).
            var blocks = history.Entries.OfType<BlockGainedEntry>()
                .Where(blockGainedEntry => blockGainedEntry.HappenedThisTurn(combat) && blockGainedEntry.CardPlay != null && blockGainedEntry.CardPlay.Player == player && blockGainedEntry.Props.IsCardOrMonsterMove()).Select(blockGainedEntry => blockGainedEntry.CardPlay).Distinct().Count();
            if (blocks > 0) counts = counts.Add(SimKeys.CardBlock, blocks);
        }
        catch
        {
            // history unavailable: no counters
        }
        return counts;
    }

    // ------------------------------------------------------------------ cards

    /// <summary>
    /// A shuffle puts the card on top: its enchantment's code moves it there (ModelSpec.OnTopWhenShuffled: Perfect Fit's ModifyShuffleOrder). False
    /// before the models are read (a moment after loading): not known, never assumed.
    /// </summary>
    public static bool OnTopWhenShuffled(CardModel card) =>
        card.Enchantment is { } enchantment && SimModels.Loaded && SimModels.Enchantment(enchantment.GetType().Name) is { Spec.OnTopWhenShuffled: true };

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

    /// <summary>
    /// What the cost passes test of a card (SimCostedCard, decision #334): its class name, type, Ethereal and affliction; nothing else, so copies
    /// that cost alike stay one hand card.
    /// </summary>
    public static SimCard CostedFacts(CardModel card) => new(card.GetType().Name, TypeOf(card))
    {
        Ethereal = card.Keywords.Contains(CardKeyword.Ethereal),
        Affliction = card.Affliction == null ? SimAffliction.None
            : Enum.TryParse<SimAffliction>(card.Affliction.GetType().Name, out var simAffliction) ? simAffliction : SimAffliction.Other,
    };

    /// <summary>The card as models see it: its class name, type, tags, values, and flags.</summary>
    public static SimCard Card(CardModel card, int energy)
    {
        var vars = new Dictionary<string, decimal>();
        try { foreach (var (k, dynamicVar) in card.DynamicVars) vars[k] = dynamicVar.BaseValue; } catch { /* none */ }
        var tags = TagsOf(card).ToHashSet();
        var affliction = card.Affliction == null ? SimAffliction.None
            : Enum.TryParse<SimAffliction>(card.Affliction.GetType().Name, out var simAffliction) ? simAffliction : SimAffliction.Other;
        bool enchantmentGainsBlock;
        // Read through its gate (Fool.EnchantmentGainsBlock): EnchantmentModel.GainsBlock is beta-only (v0.113.1), and naming it here made this
        // whole method fail to compile on main (MissingMethodException: no card simulated at all).
        try { enchantmentGainsBlock = SpireQoL.Procs.Fool.EnchantmentGainsBlock(card); } catch { enchantmentGainsBlock = false; }
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
            GainsBlock = card.GainsBlock,
            EnchantmentGainsBlock = enchantmentGainsBlock,
            Enchantment = EnchantmentOf(card),
        };
    }

    /// <summary>A card with only its enchantment as the engine runs it (EnchantmentOf), for a read enchantment's values asked outside a simulation.</summary>
    internal static SimCard? EnchantedCard(CardModel card) =>
        EnchantmentOf(card) is { } enchantment ? new SimCard(card.GetType().Name, TypeOf(card)) { Enchantment = enchantment } : null;

    /// <summary>
    /// A card's enchantment as the engine runs it (decision #217): its class, its amount and its own state its read programs read or store (its
    /// Status), read from the game's object. Null: none.
    /// </summary>
    private static SimEnchantment? EnchantmentOf(CardModel card)
    {
        if (card.Enchantment is not { } enchantment) return null;
        var name = enchantment.GetType().Name;
        var fields = NamedCounts.Empty;
        if (SimModels.Enchantment(name) is { } read)
            foreach (var (field, _) in read.OwnFields)
                fields = fields.With(field, FieldOf(enchantment, field) switch
                {
                    int number => number,
                    bool flag => flag ? 1 : 0,
                    decimal number => (int)number,
                    Enum value => Convert.ToInt32(value, System.Globalization.CultureInfo.InvariantCulture),
                    _ => 0,
                });
        return new SimEnchantment(name, enchantment.Amount) { Fields = fields };
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
            // A copy of a power aimed at a player (decision #321): its amount under that player, beside every copy's together in the power's entry.
            if (model is PowerModel aimed && AimOf(aimed) is { } aim && idOf?.Invoke(aim) is { } aimId && Who.IsPlayer(aimId))
            {
                var aimedKey = SimKeys.Aimed(ModelName(model), aimId);
                fields = fields.With(aimedKey, fields[aimedKey] + aimed.Amount);
            }
            // A hand-written model's own data (decisions #326, #328): a collection keyed by creature, each entry under its creature (Possess Strength's
            // stolenStrength: its amount; Dampen's casters: 1); any other collection by its count (Dampen's downgraded cards).
            if (SimModels.Get(ModelName(model)) is { OwnData.Count: > 0 } keyed
                && Sts2State.Private<object?>(model, "_internalData", fallback: null) is { } ownData)
                foreach (var data in keyed.OwnData)
                    switch (Sts2State.Private<object?>(ownData, data, fallback: null))
                    {
                        case System.Collections.IDictionary entries when entries.Keys.Cast<object>().All(key => key is Creature):
                            foreach (System.Collections.DictionaryEntry entry in entries)
                                if (idOf?.Invoke((Creature)entry.Key) is { } keyId && entry.Value is decimal or int)
                                    fields = fields.With(SimKeys.PerCreature(keyed.Name, data, keyId), Convert.ToInt32(entry.Value, System.Globalization.CultureInfo.InvariantCulture));
                            break;
                        case IEnumerable<Creature> creatures:
                            foreach (var listed in creatures)
                                if (idOf?.Invoke(listed) is { } listedId) fields = fields.With(SimKeys.PerCreature(keyed.Name, data, listedId), 1);
                            break;
                        case System.Collections.ICollection collection:
                            fields = fields.With(SimKeys.OwnDataCount(keyed.Name, data), collection.Count);
                            break;
                    }
            if (SimModels.Get(ModelName(model)) is not ReadModel read) continue;
            // A power kept in copies: each instance on the creature, in the game's order, is a copy of its own (its amount, its applier, its state).
            var copy = 0;
            if (read.HasCopies && model is PowerModel power)
            {
                copy = copies[read.Name] = copies.GetValueOrDefault(read.Name) + 1;
                var applier = power.Applier is { } applierCreature && idOf?.Invoke(applierCreature) is { } applierId ? applierId : Who.None;
                fields = fields.With(read.Name + "@" + copy.ToString(System.Globalization.CultureInfo.InvariantCulture), power.Amount, DataCreature.Of(applier));
            }
            foreach (var (field, fieldKey) in read.OwnFields)
                fields = fields.With(copy > 0 ? read.CopyFieldKey(copy, field) : fieldKey, FieldOf(model, field) switch
                {
                    int number => number,
                    bool flag => flag ? 1 : 0,
                    decimal number => (int)number,
                    // What an attack it keeps names (decision #228): its card by its identity (the HandId the simulator's cards carry), its attacker by id.
                    CardModel card when field.EndsWith(".ModelSource", StringComparison.Ordinal) => SimAdapter.HandId(card),
                    // A card it keeps (Pen Nib's AttackToDouble, Vambrace's TriggeringCard: decision #272), the identity of the card play it was kept from:
                    // one kept as the state was read is there, and no simulated card play's.
                    CardModel => KeptOutside,
                    Creature creature => idOf?.Invoke(creature) ?? Who.None,
                    // A card play's player it keeps (Stretch's cardPlay.Player), by its creature's id.
                    MegaCrit.Sts2.Core.Entities.Players.Player keptPlayer => idOf?.Invoke(keptPlayer.Creature) ?? Who.None,
                    // An attack it keeps (Vigor's commandToModify), one under way as the state was read: there, and no simulated attack's id.
                    AttackCommand => KeptOutside,
                    // A card play it keeps (Stretch's cardPlay, Pael's Legion's AffectedCardPlay: decision #268), one under way as the state was read: there,
                    // and no simulated card play's identity.
                    MegaCrit.Sts2.Core.Entities.Cards.CardPlay => KeptOutside,
                    // None kept: its player is no one.
                    null when field.EndsWith(".Player", StringComparison.Ordinal) => Who.None,
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
            ? Sts2State.Private<object?>(model, "_internalData", fallback: null) is { } data ? DataPath(data, field[Procs.Programs.ProgramReader.OwnDataPrefix.Length..]) : null
            : field.StartsWith(HookReader.VarFieldPrefix, StringComparison.Ordinal) ? Sts2State.Var(model, field[HookReader.VarFieldPrefix.Length..])
            // What a member of its own names ("AffectedCardPlay.Player": the player of the card play it keeps), read in turn.
            : field.Contains('.', StringComparison.Ordinal) ? DataPath(model, field)
            : Sts2State.Private<object?>(model, field, fallback: null);

    /// <summary>
    /// What a kept attack's field names in the simulator when the game's attack is under way as the state is read (Data.commandToModify set): there,
    /// and never the id of an attack a simulation makes (they count from 1), so no simulated attack is it.
    /// </summary>
    public const int KeptOutside = int.MaxValue;

    /// <summary>
    /// A field of a model's own data, or what a member of it names ("commandToModify.ModelSource": the card of the attack it keeps, its properties
    /// read in turn); null where one along the way is null.
    /// </summary>
    private static object? DataPath(object data, string path)
    {
        object? value = data;
        foreach (var member in path.Split('.'))
        {
            if (value == null) return null;
            var type = value.GetType();
            value = type.GetProperty(member, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic) is { } property ? property.GetValue(value)
                : type.GetField(member, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(value);
        }
        return value;
    }

    /// <summary>The name a game model has in the simulator (temporary Thorns is Thorns).</summary>
    public static string ModelName(AbstractModel model) => model is PowerModel ? SimAdapter.SimPowerName(model.GetType()) : model.GetType().Name;

    /// <summary>
    /// The player's creature a power's instance is aimed at (decision #321): its Target (PowerModel.Target: The Insatiable's Sandpit, one per player)
    /// when that's a player, else the Player its class keeps (a property of its own of the game's Player type: Imitation Learning's PlayerTarget, which
    /// the game keeps apart from Target); null for one aimed at no player, or one whose player isn't set.
    /// </summary>
    internal static Creature? AimOf(PowerModel power)
    {
        if (power.Target is { IsPlayer: true } target) return target;
        if (AimProperty.GetOrAdd(power.GetType(), type => type.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly)
                .FirstOrDefault(property => property.PropertyType == typeof(Player) && property.CanRead)) is not { } property) return null;
        try { return (property.GetValue(power) as Player)?.Creature; }
        catch (TargetInvocationException) { return null; } // its player not set yet (ImitationLearningPower.PlayerTarget throws)
    }

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<Type, PropertyInfo?> AimProperty = new();

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
