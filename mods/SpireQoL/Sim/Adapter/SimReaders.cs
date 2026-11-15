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
            var name = SimAdapter.SimPowerName(pw.GetType());
            int amount = pw.Amount, data = 0;
            switch (name)
            {
                case "SlowPower": data = Sts2State.Var(pw, "SlowAmount", 0); break;
                case "SkittishPower": data = Sts2State.Private(pw, "HasGainedBlockThisTurn", false) ? 1 : 0; break;
                case "HardenedShellPower": data = Math.Max(0, pw.DisplayAmount); break;
                case "FlutterPower": data = Sts2State.Var(pw, "DamageDecrease", 50); break;
                case "SurroundedPower": data = Sts2State.Private<object?>(pw, "Facing", null)?.ToString() == "Left" ? 1 : 0; break;
                case "CoveredPower": data = pw.Applier is { } ap && idOf(ap) is { } apId ? apId + 100 : 0; break;
                case "InterceptPower":
                {
                    // The players it covers (InterceptPower's internal coveredCreatures): Intercept multiplies by count + 1.
                    var internalData = Sts2State.Private<object?>(pw, "_internalData", null);
                    var covered = internalData == null ? null : Sts2State.Private<System.Collections.ICollection?>(internalData, "coveredCreatures", null);
                    amount = covered?.Count ?? 1;
                    break;
                }
            }
            set = set.Has(name) ? set.With(name, set[name] + amount, data) : set.With(name, amount, data);
        }
        return set;
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
                case "BeatingRemnant":
                    amount = Sts2State.Var(r, "MaxHpLoss", int.MaxValue);
                    data = (int)Sts2State.Private(r, "DamageReceivedThisTurn", 0m);
                    break;
                case "TheBoot": amount = Sts2State.Var(r, "DamageMinimum", 5); break;
                case "SneckoSkull": amount = Sts2State.Var(r, "PoisonPower", 1); break;
                default: amount = Math.Max(0, r.DisplayAmount); break;
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
                t = t.Add("Started", 1).Add($"Started:{TypeOf(e.CardPlay.Card)}", 1);
                foreach (var tag in e.CardPlay.Card.Tags) t = t.Add($"Started#{tag}", 1);
            }
            foreach (var e in history.CardPlaysFinished)
            {
                if (!e.HappenedThisTurn(cs) || e.CardPlay.Player != p) continue;
                t = t.Add("Finished", 1).Add($"Finished:{TypeOf(e.CardPlay.Card)}", 1);
                foreach (var tag in e.CardPlay.Card.Tags) t = t.Add($"Finished#{tag}", 1);
            }
            var blocks = history.Entries.OfType<BlockGainedEntry>()
                .Count(e => e.HappenedThisTurn(cs) && e.CardPlay != null && e.CardPlay.Player == p && e.Props.IsCardOrMonsterMove());
            if (blocks > 0) t = t.Add("CardBlock", blocks);
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

    /// <summary>The card as models see it: its class name, type, tags, values, and flags.</summary>
    public static SimCard Card(CardModel card, int energy)
    {
        var vars = new Dictionary<string, decimal>();
        try { foreach (var (k, v) in card.DynamicVars) vars[k] = v.BaseValue; } catch { /* none */ }
        var tags = new HashSet<string>();
        try { foreach (var t in card.Tags) tags.Add(t.ToString()); } catch { /* none */ }
        if (card.Affliction != null) tags.Add(card.Affliction.GetType().Name);
        bool gainsBlock;
        try { gainsBlock = card.GainsBlock || (card.Enchantment?.GainsBlock ?? false); } catch { gainsBlock = false; }
        return new SimCard(card.GetType().Name, TypeOf(card))
        {
            Tags = tags,
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
