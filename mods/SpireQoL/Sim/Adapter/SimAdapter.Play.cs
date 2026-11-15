using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.ValueProps;
using SpireQoL.Procs;
using Sts2Mods.Core.Game;

namespace SpireQoL.Sim;

public static partial class SimAdapter
{
    /// <summary>
    /// The play of <paramref name="card"/> aimed at <paramref name="targetId"/> (docs/SIMULATOR_V2.md "Card scripts"): the card
    /// play with what each play does as actions (HP cost, attack, block, powers), worked out by the simulator from the card's
    /// values, the models and the captured terms; plus, as summary fields, what isn't an action yet (orbs, procs, hand changes).
    /// </summary>
    public static SimPlay Play(CardModel card, PlayFacts f, Player me, ICombatState cs, IReadOnlyDictionary<Creature, int> ids,
        int targetId)
    {
        var name = card.GetType().Name;
        var plays = Math.Max(1, f.Plays);
        var play = new SimPlay
        {
            Name = name,
            IsAttack = card.Type == CardType.Attack,
            Plays = plays,
            HandDelta = -1 + f.Draws + f.Generated + f.ReturnsToHand,
            FadingDelta = IsFading(card, cs) ? -1 : 0,
        };
        // Aimed at a teammate: what lands on them goes to them (never to you, even with no teammate to aim at).
        Creature? ally = null;
        var allyId = -1;
        if (card.TargetType == TargetType.AnyAlly && targetId <= -2)
        {
            var allies = Teammates(me, cs);
            var k = -2 - targetId;
            if (k < allies.Count) { play.AllyTarget = k; ally = allies[k].Creature; allyId = Who.Teammate(k); }
        }
        play.HandCard = card.Pile?.Type == PileType.Hand ? HandId(card) : 0;
        // Covering Fire: block for every teammate on each shot you try (one per play of a Shoot card, arrow or not; none from an X shot at X 0).
        if (Fool.TriesShot(card) && me.Creature.Powers.FirstOrDefault(x => x is CoveringFirePower) is { Amount: > 0 } coveringFire)
            play.TeammateBlock = (coveringFire.Amount, plays);
        play.Draws = f.Draws;
        play.BlockToAlly = BlockToAllyCards.Contains(name);
        play.ChannelToAlly = ChannelToAllyCards.Contains(name);

        // ---- v2: the card play and what each play does
        var target = card.TargetType == TargetType.AnyAlly ? allyId : targetId;
        play.Card = new SimCardPlay(SimReaders.Card(card, f, me), Who.You, target, 0, plays, f.AutoPlayed);
        if (f.LosesHp) play.Body.Add(new LoseHpAction(Sts2State.Var(card, "HpLoss")));
        if (f.AttackHits > 0 && AttackOf(card, f, me, cs, ids, targetId, plays, name) is { } attack) play.Body.Add(attack);
        if (f.BlockGains > 0 && BlockOf(card, f, me, cs, ids, targetId, plays, name, ally, allyId) is { } block) play.Body.Add(block);
        // Adroit (enchantment): its block on every play, after the card's own (EnchantmentModel.OnPlay → CreatureCmd.GainBlock).
        if (card.Enchantment is MegaCrit.Sts2.Core.Models.Enchantments.Adroit adroit)
        {
            var b = adroit.DynamicVars.Block.BaseValue;
            play.Body.Add(new GainBlockAction(1, b, SimProps.Move) { Captured = SimReaders.Block(cs, card, me.Creature, b, ValueProp.Move) });
        }
        play.Body.AddRange(PowersOf(card, f, me, ids, targetId, name, allyId));
        AddCardEvents(play, card, f, me, cs, ids, targetId, plays);

        // ---- summary fields (not actions yet)
        play.ByOsty = f.ByOsty;
        play.KillsOsty = KillsOstyCards.Contains(name);
        // Necro Mastery: Osty losing HP (here: all of it, killed by the card) hits every enemy for that much times its amount.
        if (play.KillsOsty && me.IsOstyAlive && me.Osty is { } dying && me.Creature.Powers.FirstOrDefault(x => x.GetType().Name == nameof(NecroMasteryPower)) is { Amount: > 0 } mastery)
            play.AllEnemiesUnblockable = dying.CurrentHp * mastery.Amount;
        // Capture Spirit: unblockable, unpowered damage to its target through the game's damage math.
        if (TargetDamageCards.Contains(name) && targetId >= 0 && card.DynamicVars.TryGetValue("Damage", out var tdVar) && tdVar is DamageVar tdv
            && ids.FirstOrDefault(kv => kv.Value == targetId).Key is { } capt)
        {
            play.TargetId = targetId;
            play.TargetDamage[targetId] = SafeInt(() => Math.Max(0, (int)Hook.ModifyDamage(me.RunState, cs, capt, me.Creature, tdv.BaseValue, tdv.Props,
                card, null, ModifyDamageHookType.All, CardPreviewMode.None, out _)));
        }
        if (ExecuteCards.Contains(name) && targetId >= 0)
        {
            play.TargetId = targetId;
            play.ExecuteAtOrBelow = Sts2State.Var(card, "Threshold");
        }
        AddOrbs(play, card, f, me, cs, plays, name);
        play.FocusDelta = FocusChange(card) * plays;
        // Bouncing Flask: poison on a random enemy, Repeat times (Snecko Skull adds to each).
        if (card.TargetType == TargetType.RandomEnemy && card.DynamicVars.Values.FirstOrDefault(IsPowerVar<PoisonPower>) is { } flask)
        {
            var skull = me.Relics.Where(r => r is SneckoSkull).Sum(r => Sts2State.Var(r, nameof(PoisonPower), 1));
            play.RandomPoison = (int)flask.BaseValue + skull;
            play.RandomPoisonTimes = Math.Max(1, Sts2State.Var(card, "Repeat", 1)) * plays;
        }
        if (play.Poison > 0 && !play.PoisonAll) play.TargetId = targetId;
        return play;
    }

    /// <summary>
    /// The card events the play causes (PlayFacts' counts), for the models that react to them: the energy it spends and gives back
    /// (before the play), each play's draws, discards, other exhausts, created cards and star gains (indirect, but star gains), the
    /// stars it spends (first play), its own exhaust and a shuffle (after), and the arrows it dislodges at this aim.
    /// </summary>
    private static void AddCardEvents(SimPlay play, CardModel card, PlayFacts f, Player me, ICombatState cs, IReadOnlyDictionary<Creature, int> ids,
        int targetId, int plays)
    {
        void Each(List<SimAction> to, CardEvent ev, int total, bool indirect, bool perPlay = true)
        {
            var n = perPlay ? total / plays : total;
            if (n > 0) to.Add(new CardEventAction(ev, n) { Indirect = indirect });
        }
        Each(play.Before, CardEvent.EnergyGained, f.EnergyBack, false, perPlay: false);
        Each(play.Before, CardEvent.EnergySpent, f.EnergySpent, false, perPlay: false);
        Each(play.Body, CardEvent.Drawn, Math.Min(f.OwnDraws, f.Draws), true);
        Each(play.Body, CardEvent.Discarded, f.Discards, true);
        Each(play.Body, CardEvent.Exhausted, f.ExhaustsOthers, true);
        var sadness = f.SadnessCreated;
        var statuses = Math.Max(0, f.GeneratedStatuses - sadness);
        Each(play.Body, CardEvent.Generated, Math.Max(0, f.Generated + f.TransformsCertain - statuses - sadness), true);
        Each(play.Body, CardEvent.GeneratedStatus, statuses, true);
        Each(play.Body, CardEvent.GeneratedSadness, sadness, true);
        Each(play.Body, CardEvent.StarsGained, f.StarGains, false);
        if (f.Stars > 0) play.Body.Add(new CardEventAction(CardEvent.StarsSpent, 1) { FirstPlayOnly = true });
        if (f.ExhaustsSelf) play.After.Add(new CardEventAction(CardEvent.Exhausted, 1) { Card = play.Card!.Card });
        if (f.Shuffles) play.After.Add(new CardEventAction(CardEvent.Shuffled, 1) { Indirect = true });
        // Arrows it pulls loose (Fool.DislodgesOn): at its target, or at every enemy for an all-enemy card.
        int dislodged;
        try
        {
            dislodged = card.TargetType switch
            {
                TargetType.AnyEnemy => ids.FirstOrDefault(kv => kv.Value == targetId).Key is { } aimed ? Fool.DislodgesOn(f, aimed, me) : 0,
                TargetType.AllEnemies => cs.HittableEnemies.Where(x => x.IsAlive).Sum(x => Fool.DislodgesOn(f, x, me)),
                _ => 0,
            };
        }
        catch
        {
            dislodged = 0;
        }
        Each(play.After, CardEvent.ArrowDislodged, dislodged, false, perPlay: false);
    }

    private static bool IsPowerVar<T>(DynamicVar v) where T : PowerModel =>
        v.GetType() is { IsGenericType: true } t && t.GetGenericTypeDefinition() == typeof(PowerVar<>) && t.GetGenericArguments()[0] == typeof(T);

    /// <summary>
    /// The card's attack, each play: its hits per play, its damage value (calculated on each target when it's a calculated
    /// value) and props, and the captured terms on each enemy. Osty's attacks keep the game's final numbers (his own powers
    /// aren't simulated). Timber's kill attack and Rain Shot's splash ride along.
    /// </summary>
    private static AttackAction? AttackOf(CardModel card, PlayFacts f, Player me, ICombatState cs, IReadOnlyDictionary<Creature, int> ids,
        int targetId, int plays, string name)
    {
        var mode = card.TargetType switch
        {
            TargetType.AnyEnemy => SimPlay.Target.Single,
            TargetType.AllEnemies => SimPlay.Target.All,
            TargetType.RandomEnemy => SimPlay.Target.Random,
            _ => SimPlay.Target.None,
        };
        if (mode == SimPlay.Target.None) return null;
        var aimed = ids.FirstOrDefault(kv => kv.Value == targetId).Key;
        var hits = (mode == SimPlay.Target.Single && aimed != null ? f.HitsOn(aimed) : f.AttackHits) / plays;
        if (hits <= 0) return null;

        var baseOn = new Dictionary<int, decimal>();
        var captured = new Dictionary<int, CapturedTerms>();
        decimal baseDamage = 0m;
        var props = ValueProp.Move;
        CalculatedVar? calc = null;
        if (card.DynamicVars.TryGetValue("CalculatedDamage", out var cv) && cv is CalculatedVar c)
        {
            calc = c;
            if (cv is CalculatedDamageVar cdv) props = cdv.Props;
        }
        else if ((card.DynamicVars.TryGetValue("Damage", out var dv) || card.DynamicVars.TryGetValue("OstyDamage", out dv)) && dv is DamageVar d)
        {
            baseDamage = d.BaseValue;
            props = d.Props;
        }
        var fixedNumbers = f.ByOsty || (calc == null && baseDamage == 0m);
        foreach (var (e, id) in ids)
        {
            if (!f.CanHit(e)) continue;
            if (fixedNumbers)
            {
                baseOn[id] = f.DamagePerHit(e);
                continue;
            }
            var b = calc != null ? SafeDecimal(() => calc.Calculate(e)) : baseDamage;
            baseOn[id] = b;
            captured[id] = SimReaders.Damage(me.RunState, cs, card, me.Creature, e, b, props);
        }
        DamageSpec? onKill = null;
        if (OnKillHitAllCards.Contains(name) && card.DynamicVars.TryGetValue("OnKillDamage", out var kv2) && kv2 is DamageVar kdv)
        {
            var killTerms = new Dictionary<int, CapturedTerms>();
            foreach (var (e, id) in ids)
                if (e.IsAlive && e.IsHittable) killTerms[id] = SimReaders.Damage(me.RunState, cs, card, me.Creature, e, kdv.BaseValue, kdv.Props);
            onKill = new DamageSpec(kdv.BaseValue, SimReaders.Props(kdv.Props)) { Captured = killTerms };
        }
        return new AttackAction(hits, mode, new DamageSpec(baseDamage, SimReaders.Props(props)) { BaseOn = baseOn, Captured = captured, Fixed = fixedNumbers })
        {
            TargetId = mode == SimPlay.Target.Single ? targetId : -1,
            ByOsty = f.ByOsty,
            OnKill = onKill,
            Splash = name == "RainShot", // RainShot.OnPlay: the target's damage to every other enemy
        };
    }

    /// <summary>
    /// The card's own block, each play: its block value through the block pipeline (models and captured terms), to you or the
    /// targeted teammate. Cards whose block isn't a value (Fisticuffs: the damage dealt; Entrench: your block; Mimic and a
    /// calculated block aimed at a teammate) keep the game's number.
    /// </summary>
    private static GainBlockAction? BlockOf(CardModel card, PlayFacts f, Player me, ICombatState cs, IReadOnlyDictionary<Creature, int> ids,
        int targetId, int plays, string name, Creature? ally, int allyId)
    {
        var gains = f.BlockGains / plays;
        if (gains <= 0) return null;
        var toAlly = BlockToAllyCards.Contains(name);
        var to = toAlly ? allyId : Who.You;
        if (toAlly && allyId == -1) return null; // no teammate to aim at: the block goes to no one
        var aimed = ids.FirstOrDefault(kv => kv.Value == targetId).Key;
        int? fixedEach = name switch
        {
            nameof(Fisticuffs) => aimed != null ? f.TotalDamage(aimed) / plays : 0,
            nameof(Entrench) => me.Creature.Block,
            _ when toAlly && card.DynamicVars.ContainsKey("CalculatedBlock") => ally != null ? CardBlock(card, cs, me, ally, ally) : 0,
            nameof(Mimic) when ally != null => CardBlock(card, cs, me, me.Creature, ally),
            _ => null,
        };
        if (fixedEach is { } each) return new GainBlockAction(gains, each, SimProps.Move) { Fixed = true, To = to };
        decimal b;
        if (card.DynamicVars.TryGetValue("CalculatedBlock", out var cv) && cv is CalculatedVar calc) b = SafeDecimal(() => calc.Calculate(null));
        else if (card.DynamicVars.TryGetValue("Block", out var bv)) b = bv.BaseValue;
        else return new GainBlockAction(gains, CardBlock(card, cs, me), SimProps.Move) { Fixed = true, To = to };
        var gainer = toAlly && ally != null ? ally : me.Creature;
        return new GainBlockAction(gains, b, SimProps.Move) { Captured = SimReaders.Block(cs, card, gainer, b, ValueProp.Move), To = to };
    }

    /// <summary>
    /// The powers the card's code applies (CardCoverage.PowersApplied), each play: buffs to you (or the targeted teammate:
    /// Intercept's Covered), the debuffs PlayFacts finds it lands on enemies to its target or every enemy, and the Strength it
    /// takes or gives (EnemyStrengthCards). Amounts from the card's PowerVar, its plain value, or 1. Only powers the simulator
    /// reads: modelled ones, the ones it keeps as numbers, and poison.
    /// </summary>
    private static IEnumerable<SimAction> PowersOf(CardModel card, PlayFacts f, Player me, IReadOnlyDictionary<Creature, int> ids, int targetId,
        string name, int allyId)
    {
        var actions = new List<SimAction>();
        int AmountOf(Type t)
        {
            foreach (var v in card.DynamicVars.Values)
            {
                var vt = v.GetType();
                if (vt.IsGenericType && vt.GetGenericTypeDefinition() == typeof(PowerVar<>) && vt.GetGenericArguments()[0] == t) return (int)v.BaseValue;
            }
            return PlainSelfPowerAmount(card, t) ?? 1;
        }
        bool Simulated(string n) => SimModels.Has(n) || SelfPowers.Contains(n) || n == nameof(PoisonPower) || n == nameof(ArtifactPower);
        var debuffs = f.EnemyDebuffs.Concat(f.EnemyTemporaryDebuffs).ToHashSet();
        var whereEnemies = card.TargetType switch
        {
            TargetType.AnyEnemy => ApplyPowerAction.Where.Target,
            TargetType.AllEnemies => ApplyPowerAction.Where.AllEnemies,
            _ => (ApplyPowerAction.Where?)null,
        };
        foreach (var t in Diagnostics.CardCoverage.PowersApplied(card.GetType()))
        {
            var n = SimPowerName(t);
            if (!Simulated(n) && !(PlayFacts.IsDebuffType(t) && debuffs.Contains(t))) continue;
            if (n == nameof(PoisonPower) && card.TargetType == TargetType.RandomEnemy) continue; // Bouncing Flask: random, a summary field
            if (PlayFacts.IsDebuffType(t))
            {
                if (!debuffs.Contains(t) || whereEnemies is not { } w) continue;
                actions.Add(new ApplyPowerAction(n, AmountOf(t)) { To = w, Debuff = true });
            }
            else if (card.TargetType == TargetType.AnyAlly && allyId != -1 && n == nameof(CoveredPower))
            {
                actions.Add(new ApplyPowerAction(n, AmountOf(t)) { To = ApplyPowerAction.Where.Creature, Creature = allyId });
            }
            else if (n == "StretchPower" && KeepsStretchCards.Contains(name))
            {
                // Cho Ki: your Stretch again, as much as you have (so the play keeps it).
                actions.Add(new ApplyPowerAction(n, 0) { To = ApplyPowerAction.Where.You, CopiesOwn = true });
            }
            else
            {
                actions.Add(new ApplyPowerAction(n, AmountOf(t)) { To = ApplyPowerAction.Where.You });
            }
        }
        // Strength the card takes from enemies (this turn or for good) or gives them (Fight Me).
        if (EnemyStrengthCards.TryGetValue(name, out var str) && str.Var != null)
        {
            var amount = str.Sign * (str.Var == "" ? 1 : Sts2State.Var(card, str.Var));
            var where = str.All ? ApplyPowerAction.Where.AllEnemies : ApplyPowerAction.Where.Target;
            actions.Add(new ApplyPowerAction(nameof(StrengthPower), amount) { To = where, Debuff = amount < 0 });
        }
        return actions;
    }

    /// <summary>Orbs: evoke cards, then channels (per play), Storm's Lightning, Trash to Treasure's orbs (summary fields).</summary>
    private static void AddOrbs(SimPlay play, CardModel card, PlayFacts f, Player me, ICombatState cs, int plays, string name)
    {
        var focus = me.Creature.GetPower<FocusPower>()?.Amount ?? 0;
        var lightningBonus = me.Relics.Where(r => r.GetType().Name == nameof(InfusedCore)).Sum(r => Sts2State.Var(r, "ExtraDamage"));
        for (var i = 0; i < plays; i++)
        {
            var slots = SlotChange(card, name);
            if (slots != 0) play.OrbOps.Add(new OrbOp { Op = OrbOp.Kind.Slots, Slots = slots });
            switch (name)
            {
                case nameof(Dualcast): play.OrbOps.Add(new OrbOp { Op = OrbOp.Kind.EvokeFront, Times = 2 }); break;
                case nameof(Quadcast): play.OrbOps.Add(new OrbOp { Op = OrbOp.Kind.EvokeFront, Times = Math.Max(1, Sts2State.Var(card, "Repeat", 4)) }); break;
                case nameof(MultiCast):
                {
                    var x = SafeInt(() => Hook.ModifyXValue(cs, card, card.EnergyCost.GetAmountToSpend())) + (card.IsUpgraded ? 1 : 0);
                    if (x > 0) play.OrbOps.Add(new OrbOp { Op = OrbOp.Kind.EvokeFront, Times = x });
                    break;
                }
                case nameof(Shatter): play.OrbOps.Add(new OrbOp { Op = OrbOp.Kind.EvokeAllTwice }); break;
            }
            if (Channels.TryGetValue(name, out var kinds))
            {
                var times = name is nameof(Spinner) or nameof(Chill) ? PlayFacts.ChannelCount(card, name, me) : 1;
                for (var t = 0; t < times; t++)
                    foreach (var k in kinds) play.OrbOps.Add(new OrbOp { Op = OrbOp.Kind.Channel, Orb = Fresh(k, focus, lightningBonus) });
            }
            else if (RepeatChannels.TryGetValue(name, out var rk))
            {
                for (var r = 0; r < Math.Max(1, Sts2State.Var(card, "Repeat", 1)); r++) play.OrbOps.Add(new OrbOp { Op = OrbOp.Kind.Channel, Orb = Fresh(rk, focus, lightningBonus) });
            }
            else if (name == nameof(Voltaic))
            {
                for (var r = 0; r < PlayFacts.ChannelCount(card, name, me); r++) play.OrbOps.Add(new OrbOp { Op = OrbOp.Kind.Channel, Orb = Fresh(OrbKind.Lightning, focus, lightningBonus) });
            }
            else if (name == nameof(Tempest))
            {
                var x = SafeInt(() => Hook.ModifyXValue(cs, card, card.EnergyCost.GetAmountToSpend())) + (card.IsUpgraded ? 1 : 0);
                for (var r = 0; r < x; r++) play.OrbOps.Add(new OrbOp { Op = OrbOp.Kind.Channel, Orb = Fresh(OrbKind.Lightning, focus, lightningBonus) });
            }
        }
        var storm = me.Creature.Powers.Where(p => p is StormPower).Sum(p => p.Amount);
        if (card.Type == CardType.Power)
            for (var i = 0; i < storm * plays; i++) play.OrbOps.Add(new OrbOp { Op = OrbOp.Kind.Channel, Orb = Fresh(OrbKind.Lightning, focus, lightningBonus) });
        var trash = me.Creature.Powers.Where(p => p is TrashToTreasurePower).Sum(p => p.Amount);
        for (var i = 0; i < trash * f.GeneratedStatuses; i++) play.OrbOps.Add(new OrbOp { Op = OrbOp.Kind.Channel, Orb = new SimOrb { Kind = OrbKind.Unknown } });
    }

    private static decimal SafeDecimal(Func<decimal> f)
    {
        try { return f(); } catch { return 0m; }
    }
}
