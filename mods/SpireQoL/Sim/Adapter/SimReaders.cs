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
/// Reads the game into the simulator's v2 inputs (docs/SIMULATOR_V2.md "The adapter"): creatures' powers and players' relics
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
    public static PowerSet Powers(Creature c, Func<Creature, int?> idOf)
    {
        var set = PowerSet.Empty;
        foreach (var pw in c.Powers)
        {
            // A temporary Thorns power (Cho Barbs) only tracks how much Thorns to take back after the enemy turn: the Thorns
            // itself is already in the owner's ThornsPower (TemporaryThornsPower.BeforeApplied applies it).
            if (SimAdapter.IsTemporaryThorns(pw.GetType())) continue;
            var name = SimAdapter.SimPowerName(pw.GetType());
            // Withering Presence: Aeonglass has one per player, each counting its Target's cards (AfterCardPlayed:
            // cardPlay.Card.Owner == Target.Player). Only yours reacts to your plays; a teammate's would mix in their count.
            if (name == "WitheringPresencePower" && pw.Target is { } wt && idOf(wt) != Who.You) continue;
            int amount = pw.Amount, data = 0;
            switch (name)
            {
                // Your arrows in it (Amount) and its hit counter (Data); a teammate's Impaled only makes it Impaled.
                case "ImpaledPower" when pw.Applier is { } ia && idOf(ia) == Who.You:
                    amount = SpireQoL.Procs.Fool.ArrowsIn(pw);
                    data = SpireQoL.Procs.Fool.HitCounter(pw);
                    break;
                // A teammate's arrows in it: 4 bits of Data per teammate (index from their id); amount: arrows in all (it stays Impaled).
                case "ImpaledPower":
                    name = "ImpaledPowerOther";
                    amount = SpireQoL.Procs.Fool.ArrowsIn(pw);
                    data = pw.Applier is { } ta && idOf(ta) is { } taId && Who.TeammateIndex(taId) is var tk and >= 0 and < 7 ? Math.Min(15, amount) << (4 * tk) : 0;
                    amount = Math.Max(1, amount);
                    break;
                case "HailstormPower": data = Sts2State.Var(pw, "FrostOrbs", 1); break;
                case "TheBombPower": data = Sts2State.Var(pw, "Damage"); break;
                case "SlowPower": data = Sts2State.Var(pw, "SlowAmount", 0); break;
                case "SkittishPower": data = Sts2State.Private(pw, "HasGainedBlockThisTurn", false) ? 1 : 0; break;
                case "HardenedShellPower": data = Math.Max(0, pw.DisplayAmount); break;
                case "FlutterPower": data = Sts2State.Var(pw, "DamageDecrease", 50); break;
                case "SurroundedPower": data = Sts2State.Private<object?>(pw, "Facing", null)?.ToString() == "Left" ? 1 : 0; break;
                case "CoveredPower": data = pw.Applier is { } ap && idOf(ap) is { } apId ? apId + 100 : 0; break;
                // Counters the proc models count down (the power's own number: DisplayAmount, or its CardsLeft value).
                case "FeralPower" or "OrbitPower" or "AutomationPower" or "CacophonyPower": data = Math.Max(0, pw.DisplayAmount); break;
                case "PanachePower" or "WitheringPresencePower": data = Math.Max(1, Sts2State.Var(pw, "CardsLeft", 5)); break;
                // Once a turn (Data 1: already this turn).
                case "PaleBlueDotPower": data = Internal(pw, "alreadyActivatedThisTurn") ? 1 : 0; break;
                case "LullabyPower": data = Sts2State.Private(pw, "_proccdThisTurn", true) ? 1 : 0; break;
                case "YahPower": data = Internal(pw, "triggeredThisTurn") ? 1 : 0; break;
                // Bound to the attack under way (BeforeAttack set commandToModify): every other card's damage gets nothing from it.
                case "VigorPower" or "AimPower" or "QuietusPower" or "GigantificationPower":
                    data = Sts2State.Private<object?>(pw, "_internalData", null) is { } bd && Sts2State.Private<object?>(bd, "commandToModify", null) != null
                        ? Models.AttackBoundPower.BoundOutside : 0;
                    break;
                case "JugglingPower": data = Sts2State.Private<object?>(pw, "_internalData", null) is { } jd ? Sts2State.Private(jd, "attacksPlayedThisTurn", 0) : 0; break;
                // Whose plays it acts on (+ 100, so 0 stays "nobody").
                // Gone with their applier (AfterDeath), Magic Bomb only while it lives: its id + 100.
                case "ConstrictPower" or "MagicBombPower": data = pw.Applier is { } ca && idOf(ca) is { } caId ? caId + 100 : 0; break;
                case "StranglePower" or "OblivionPower": data = pw.Applier is { } sa && idOf(sa) is { } saId ? saId + 100 : 0; break;
                case "ImitationLearningPower":
                    data = pw is MegaCrit.Sts2.Core.Models.Powers.ImitationLearningPower il
                           && Combat.CardPredictions.CopiesFrom(il)?.Creature is { } copied && idOf(copied) is { } cId ? cId + 100 : 0;
                    break;
                case "InterceptPower":
                {
                    // The players it covers (InterceptPower's internal coveredCreatures): Intercept multiplies by count + 1.
                    var internalData = Sts2State.Private<object?>(pw, "_internalData", null);
                    var covered = internalData == null ? null : Sts2State.Private<System.Collections.ICollection?>(internalData, "coveredCreatures", null);
                    amount = covered?.Count ?? 1;
                    break;
                }
            }
            set = set.Has(name) ? set.With(name, set[name] + amount, name == "ImpaledPowerOther" ? set.DataOf(name) | data : data) : set.With(name, amount, data);
        }
        return set;
    }

    /// <summary>A flag in a power's internal data (PowerModel._internalData), true when unreadable (so a once-a-turn proc isn't promised).</summary>
    private static bool Internal(PowerModel pw, string field) =>
        Sts2State.Private<object?>(pw, "_internalData", null) is not { } d || Sts2State.Private(d, field, true);

    /// <summary>
    /// The values of every relic and power of these creatures and players (their DynamicVars) for SimState.Vars: "id:Name:Var",
    /// and its name as the game shows it (SimState.Titles),
    /// with a BlockVar's or DamageVar's props as "id:Name:Var.Props".
    /// </summary>
    public static void AddVars(SimState s, int id, AbstractModel model)
    {
        var vars = s.Vars;
        var name = ModelName(model);
        try { s.Titles[$"{id}:{name}"] = Combat.TurnEndModel.SafeTitle(model); } catch { /* no title: its class name */ }
        try
        {
            var dv0 = model switch { RelicModel r => r.DynamicVars, PowerModel p => p.DynamicVars, _ => null };
            if (dv0 == null) return;
            foreach (var (key, v) in dv0)
            {
                vars[$"{id}:{name}:{key}"] = v.IntValue;
                if (v is MegaCrit.Sts2.Core.Localization.DynamicVars.BlockVar bv) vars[$"{id}:{name}:{key}.Props"] = (int)bv.Props;
                else if (v is MegaCrit.Sts2.Core.Localization.DynamicVars.DamageVar dv) vars[$"{id}:{name}:{key}.Props"] = (int)dv.Props;
            }
        }
        catch
        {
            // no values readable: the models' fallbacks
        }
    }

    /// <summary>A player's relics in relic order (melted ones don't listen), each with the numbers its model keeps.</summary>
    public static PowerSet Relics(Player p)
    {
        var set = PowerSet.Empty;
        foreach (var r in p.Relics)
        {
            if (r.IsMelted) continue;
            var name = r.GetType().Name;
            int amount = 0, data = 0;
            switch (name)
            {
                case "PenNib":
                    amount = Sts2State.Private(r, "AttacksPlayed", 0);
                    data = Sts2State.Private<object?>(r, "AttackToDouble", null) != null ? 1 : 0;
                    break;
                case "MightyWings": amount = Sts2State.Var(r, "StrengthPower", 2); data = Sts2State.Var(r, "DamageThreshold", 30); break;
                case "Vambrace":
                    data = (Sts2State.Private<object?>(r, "TriggeringCard", null) != null ? 1 : 0) | (Sts2State.Private(r, "BlockGainedThisCombat", false) ? 2 : 0);
                    break;
                case "PaelsLegion":
                    amount = Sts2State.Private(r, "Cooldown", 0);
                    data = Sts2State.Private<object?>(r, "AffectedCardPlay", null) != null ? 1 : 0;
                    break;
                case "TungstenRod": amount = Sts2State.Var(r, "HpLossReduction", 1); break;
                case "CentennialPuzzle": amount = Sts2State.Private(r, "UsedThisCombat", false) ? 1 : 0; break; // used up this combat
                case "Pocketwatch": amount = Sts2State.Private(r, "_cardsPlayedThisTurn", 0); data = Sts2State.Var(r, "CardThreshold", 3); break;
                case "BeatingRemnant":
                    amount = Sts2State.Var(r, "MaxHpLoss", int.MaxValue);
                    data = (int)Sts2State.Private(r, "DamageReceivedThisTurn", 0m);
                    break;
                case "TheBoot": amount = Sts2State.Var(r, "DamageMinimum", 5); break;
                case "SneckoSkull": amount = Sts2State.Var(r, "PoisonPower", 1); break;
                // Counters the models keep counting (their tray number, or the private count behind it).
                case "JossPaper": amount = Sts2State.Private(r, "CardsExhausted", 0); break;
                case "GalacticDust": amount = Sts2State.Private(r, "StarsSpent", 0); break;
                case "Metronome": amount = Sts2State.Private(r, "OrbsChanneled", int.MaxValue / 2); break;
                // Once a turn: armed until used (bit 1), done this turn (bit 2).
                case "MiniRegent" or "Regalite": data = Sts2State.Private(r, "UsedThisTurn", true) ? 0 : 1; break;
                case "RainbowRing": data = Sts2State.Private(r, "ActivationCountThisTurn", 0) >= 1 ? 2 : 0; break;
                case "ConductorsBaton" or "DemonTongue": data = Sts2State.Private(r, "_triggeredThisTurn", true) ? 2 : 0; break;
                // The HP at or below which it gives Strength (its threshold of your max HP).
                case "RedSkull": amount = (int)Math.Floor(p.Creature.MaxHp * Sts2State.Var(r, "HpThreshold", 50) / 100m); break;
                // End-of-turn relics: their block or damage (amount), and what they wait for (Data).
                case "Orichalcum" or "FakeOrichalcum" or "CloakClasp" or "RippleBasin": amount = Sts2State.Var(r, "Block"); break;
                case "ScreamingFlagon" or "CharonsAshes" or "ForgottenSoul": amount = Sts2State.Var(r, "Damage"); break;
                case "ObsidianVice": amount = Sts2State.Var(r, "Damage", 1); break;
                case "StoneCalendar":
                    amount = Sts2State.Var(r, "Damage");
                    data = Math.Max(0, Sts2State.Var(r, "DamageTurn", 7) - Sts2State.TurnNumber); // turns until it fires (0: this one)
                    if (Sts2State.TurnNumber > Sts2State.Var(r, "DamageTurn", 7)) amount = 0; // its turn has passed
                    break;
                case "ParryingShield": amount = Sts2State.Var(r, "Damage"); data = Sts2State.Var(r, "Block"); break;
                case "LunarPastry": amount = Sts2State.Var(r, "Stars", 1); break;
                // Its Data is the model's own: energy spent during the play being simulated (PaelsTears), so none yet.
                case "PaelsTears": data = 0; break;
                case "UnsettlingLamp":
                    data = Sts2State.Private<object?>(r, "TriggeringCard", null) == null && !Sts2State.Private(r, "IsFinishedTriggering", true) ? 1 : 0;
                    break;
                default:
                    amount = Math.Max(0, r.DisplayAmount);
                    data = Features.RelicCountersFeature.IsArmed(r) ? 1 : 0;
                    break;
            }
            set = set.With(name, amount, data);
        }
        return set;
    }

    /// <summary>
    /// A player's turn counters (SimPlayer.Turn) from the combat history: card plays started and finished this turn (all, by
    /// type, by tag), and block gains from their card plays (Unmovable).
    /// </summary>
    public static PowerSet Turn(Player p, ICombatState cs)
    {
        var t = PowerSet.Empty;
        try
        {
            var history = CombatManager.Instance.History;
            foreach (var e in history.CardPlaysStarted)
            {
                if (!e.HappenedThisTurn(cs) || e.CardPlay.Player != p) continue;
                t = SimKeys.CountPlay(t, PlayStage.Started, TypeOf(e.CardPlay.Card), TagsOf(e.CardPlay.Card));
            }
            foreach (var e in history.CardPlaysFinished)
            {
                if (!e.HappenedThisTurn(cs) || e.CardPlay.Player != p) continue;
                t = SimKeys.CountPlay(t, PlayStage.Finished, TypeOf(e.CardPlay.Card), TagsOf(e.CardPlay.Card));
            }
            var blocks = history.Entries.OfType<BlockGainedEntry>()
                .Count(e => e.HappenedThisTurn(cs) && e.CardPlay != null && e.CardPlay.Player == p && e.Props.IsCardOrMonsterMove());
            if (blocks > 0) t = t.Add(SimKeys.CardBlock, blocks);
        }
        catch
        {
            // history unavailable: no counters
        }
        return t;
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
    public static SimCard Card(CardModel card, SpireQoL.Procs.PlayFacts f, Player me)
    {
        var c = Card(card, f.Energy);
        bool Safe(Func<bool> q) { try { return q(); } catch { return false; } }
        return c with
        {
            Stars = f.Stars,
            StarCostX = Safe(() => card.HasStarCostX),
            ExhaustsSelf = f.ExhaustsSelf,
            Dupe = Safe(() => card.IsDupe),
            Upgradable = Safe(() => card.IsUpgradable),
            OthersInHandCost = Safe(() => Sts2State.Hand(me).Any(x => x != card && x.CostsEnergyOrStars(includeGlobalModifiers: true))),
        };
    }

    /// <summary>A card's tags as the simulator's enum, matched by name (the game's CardTag numbers differ between its branches).</summary>
    public static IEnumerable<SimCardTag> TagsOf(CardModel card)
    {
        var tags = new List<SimCardTag>();
        try
        {
            foreach (var t in card.Tags)
                if (Enum.TryParse<SimCardTag>(t.ToString(), out var tag)) tags.Add(tag);
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
        try { foreach (var (k, v) in card.DynamicVars) vars[k] = v.BaseValue; } catch { /* none */ }
        var tags = TagsOf(card).ToHashSet();
        var affliction = card.Affliction == null ? SimAffliction.None
            : Enum.TryParse<SimAffliction>(card.Affliction.GetType().Name, out var a) ? a : SimAffliction.Other;
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

    /// <summary>
    /// The terms of one damage calculation that the simulator doesn't model (docs/SIMULATOR_V2.md "Pipelines"): the card's
    /// enchantment, and every hook listener whose class isn't modelled, each asked for its own term the way Hook.ModifyDamage
    /// asks it (with this card, dealer and target).
    /// </summary>
    public static CapturedTerms Damage(IRunState rs, ICombatState cs, CardModel? card, Creature? dealer, Creature? target, decimal baseDamage, ValueProp props)
    {
        decimal enchantAdd = 0m, enchantMul = 1m, add = 0m, mul = 1m, late = 1m, cap = decimal.MaxValue;
        try
        {
            if (card?.Enchantment is { } ench)
            {
                enchantAdd = ench.EnchantDamageAdditive(baseDamage, props);
                enchantMul = ench.EnchantDamageMultiplicative(baseDamage + enchantAdd, props);
            }
            var listeners = rs.IterateHookListeners(cs).Where(l => !SimModels.Has(ModelName(l))).ToList();
            foreach (var l in listeners) add += l.ModifyDamageAdditive(target, baseDamage, props, dealer, card, null);
            foreach (var l in listeners) mul *= l.ModifyDamageMultiplicative(target, baseDamage, props, dealer, card, null);
            foreach (var l in listeners) late *= l.ModifyDamageMultiplicativeLate(target, baseDamage, props, dealer, card, null);
            foreach (var l in listeners) cap = Math.Min(cap, l.ModifyDamageCap(target, props, dealer, card, null));
        }
        catch
        {
            // a listener that can't answer outside a real calculation: nothing captured from it
        }
        return new CapturedTerms { EnchantAdd = enchantAdd, EnchantMul = enchantMul, Add = add, Mul = mul, Late = late, Cap = cap };
    }

    /// <summary>The same for block (Hook.ModifyBlock): the enchantment's, and every unmodelled listener's.</summary>
    public static CapturedTerms Block(ICombatState cs, CardModel? card, Creature gainer, decimal baseBlock, ValueProp props)
    {
        decimal enchantAdd = 0m, enchantMul = 1m, add = 0m, mul = 1m;
        try
        {
            if (card?.Enchantment is { } ench)
            {
                enchantAdd = ench.EnchantBlockAdditive(baseBlock);
                enchantMul = ench.EnchantBlockMultiplicative(baseBlock + enchantAdd);
            }
            var listeners = cs.IterateHookListeners().Where(l => !SimModels.Has(ModelName(l))).ToList();
            foreach (var l in listeners) add += l.ModifyBlockAdditive(gainer, baseBlock, props, card, null);
            foreach (var l in listeners) mul *= l.ModifyBlockMultiplicative(gainer, baseBlock, props, card, null);
        }
        catch
        {
            // nothing captured
        }
        return new CapturedTerms { EnchantAdd = enchantAdd, EnchantMul = enchantMul, Add = add, Mul = mul };
    }

    /// <summary>The name a game model has in the simulator (temporary Thorns is Thorns).</summary>
    public static string ModelName(AbstractModel m) => m is PowerModel ? SimAdapter.SimPowerName(m.GetType()) : m.GetType().Name;

    public static SimProps Props(ValueProp p) => (SimProps)(int)p;
}
