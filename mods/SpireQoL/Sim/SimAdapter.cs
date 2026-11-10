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

namespace SpireQoL.Sim;

/// <summary>
/// Builds the simulator's plain state from the live game, and turns a card in hand into a
/// <see cref="SimPlay"/>. Only knowable information goes in (CLAUDE.md): what's on screen, counters
/// a player could track, and the game's rules. Random targets stay random (the simulator branches).
/// </summary>
public static class SimAdapter
{
    // Blockable end-of-turn damage from cards in hand (DamageVar "Damage").
    private static readonly HashSet<string> BlockableHandCards = new() { "Burn", "Decay", "Infection", "Toxic", "Wither" };

    // Unblockable HP loss from cards in hand (HpLossVar "HpLoss"; Regret = cards in hand).
    private static readonly HashSet<string> UnblockableHandCards = new() { "BadLuck", "Beckon" };

    // Debuffs on you that deal blockable damage after your turn ends, before block clears.
    private static readonly HashSet<string> BlockableDebuffs = new() { "ConstrictPower", "MagicBombPower" };

    // Debuffs that land after Parrying Shield (AfterSideTurnEndLate).
    private static readonly HashSet<string> LateBlockableDebuffs = new() { "DisintegrationPower" };

    private static readonly HashSet<string> UnblockableDebuffs = new() { "DemisePower" };

    /// <summary>Orbs each card channels (OrbCmd.Channel&lt;T&gt; in its OnPlay; decompile v0.111.0). Chaos is random.</summary>
    private static readonly Dictionary<string, OrbKind[]> Channels = new()
    {
        ["BallLightning"] = new[] { OrbKind.Lightning }, ["Zap"] = new[] { OrbKind.Lightning },
        ["Chill"] = new[] { OrbKind.Frost }, ["ColdSnap"] = new[] { OrbKind.Frost }, ["Coolheaded"] = new[] { OrbKind.Frost },
        ["Glacier"] = new[] { OrbKind.Frost, OrbKind.Frost },
        ["Fusion"] = new[] { OrbKind.Plasma }, ["Ignition"] = new[] { OrbKind.Plasma },
        ["MeteorStrike"] = new[] { OrbKind.Plasma, OrbKind.Plasma, OrbKind.Plasma },
        ["Glasswork"] = new[] { OrbKind.Glass }, ["Spinner"] = new[] { OrbKind.Glass },
        ["Null"] = new[] { OrbKind.Dark }, ["ShadowShield"] = new[] { OrbKind.Dark }, ["Darkness"] = new[] { OrbKind.Dark },
        ["Rainbow"] = new[] { OrbKind.Lightning, OrbKind.Frost, OrbKind.Dark },
    };

    /// <summary>Cards that channel their Repeat value of one orb kind.</summary>
    private static readonly Dictionary<string, OrbKind> RepeatChannels = new()
    {
        ["ConsumingShadow"] = OrbKind.Dark, ["Hibernate"] = OrbKind.Frost, ["IceLance"] = OrbKind.Frost,
        ["Refract"] = OrbKind.Glass, ["Chaos"] = OrbKind.Unknown,
    };

    /// <summary>Your powers a card can give itself that the simulator models (PowerVar&lt;T&gt; on the card).</summary>
    private static readonly HashSet<string> SelfPowers = new() { "PlatingPower", "JuggernautPower", "FeelNoPainPower", "ThornsPower", "FlameBarrierPower" };

    // ------------------------------------------------------------------ state

    public static SimState Build(Player me, ICombatState cs, out Dictionary<Creature, int> ids)
    {
        ids = new Dictionary<Creature, int>();
        var s = new SimState();
        var targets = cs.Players.Select(p => p.Creature).ToList();

        var n = 0;
        foreach (var e in cs.Enemies)
        {
            var id = n++;
            ids[e] = id;
            var se = new SimEnemy
            {
                Id = id,
                Name = Combat.TurnEndModel.SafeTitle(e.Monster!),
                Hp = e.CurrentHp,
                Block = e.Block,
                Hittable = e.IsAlive && e.IsHittable,
                Stunned = e.IsStunned,
                Barricade = e.HasPower<BarricadePower>(),
                Imbalanced = e.HasPower<ImbalancedPower>(),
                Poison = SafeInt(() => e.GetPower<PoisonPower>()?.CalculateTotalDamageNextTurn() ?? 0),
                PoisonStacks = e.GetPower<PoisonPower>()?.Amount ?? 0,
                PoisonTriggers = e.GetPower<PoisonPower>() is { } poison ? Math.Max(1, Sts2State.Private(poison, "TriggerCount", 1)) : 1,
                CurlUp = e.GetPower<CurlUpPower>()?.Amount ?? 0,
                Sneaky = e.GetPower<SneakyPower>()?.Amount ?? 0,
                Skittish = e.GetPower<SkittishPower>() is { } sk && !Sts2State.Private(sk, "HasGainedBlockThisTurn", true) ? sk.Amount : 0,
            };
            if (!e.IsAlive) se.Hp = 0;
            if (e.GetPower<HardenedShellPower>() is { } shell)
            {
                se.ShellPerTurn = shell.Amount;
                se.ShellLeft = Math.Max(0, shell.DisplayAmount);
            }
            if (e.GetPower<HardToKillPower>() is { } htk) se.HitCap = Math.Min(se.HitCap, htk.Amount);
            if (e.HasPower<IntangiblePower>()) se.HitCap = 1;
            if (e.IsAlive && e.Monster != null)
            {
                try
                {
                    foreach (var attack in e.Monster.NextMove.Intents.OfType<AttackIntent>())
                    {
                        var perHit = attack.GetSingleDamage(targets, e);
                        for (var i = 0; i < Math.Max(1, attack.Repeats); i++) se.Hits.Add(perHit);
                    }
                }
                catch
                {
                    // intent unavailable: no attack
                }
            }
            s.Enemies.Add(se);
        }

        var p = s.P;
        var c = me.Creature;
        var pcs = me.PlayerCombatState!;
        var hand = Sts2State.Hand(me);
        p.Block = c.Block;
        foreach (var pw in c.Powers)
        {
            switch (pw.GetType().Name)
            {
                case "JuggernautPower": p.Juggernaut += pw.Amount; break;
                case "PlatingPower": p.Plating += pw.Amount; break;
                case "FeelNoPainPower": p.FeelNoPain += pw.Amount; break;
                case "HailstormPower":
                    p.Hailstorm += pw.Amount;
                    p.HailstormFrostNeeded = Sts2State.Var(pw, "FrostOrbs", 1);
                    break;
                case "TheBombPower":
                    if (pw.Amount <= 1) p.TheBombDamage += Sts2State.Var(pw, "Damage");
                    break;
                case "ConsumingShadowPower": p.ConsumingShadow += pw.Amount; break;
                case "ThornsPower": p.Thorns += pw.Amount; break;
                case "FlameBarrierPower": p.FlameBarrier += pw.Amount; break;
                case "ReflectPower": p.Reflect = true; break;
                case "BufferPower": p.Buffer += pw.Amount; break;
                case "EnvenomPower": p.Envenom += pw.Amount; break;
            }
            var type = pw.GetType().Name;
            if (pw.Amount > 0 && BlockableDebuffs.Contains(type)) p.DebuffBlockable.Add((Combat.TurnEndModel.SafeTitle(pw), pw.Amount));
            else if (pw.Amount > 0 && LateBlockableDebuffs.Contains(type)) p.DebuffLateBlockable.Add((Combat.TurnEndModel.SafeTitle(pw), pw.Amount));
            else if (pw.Amount > 0 && UnblockableDebuffs.Contains(type)) p.DebuffUnblockable.Add((Combat.TurnEndModel.SafeTitle(pw), pw.Amount));
        }
        foreach (var r in me.Relics)
        {
            switch (r.GetType().Name)
            {
                case "Orichalcum":
                case "FakeOrichalcum": p.OrichalcumBlock += Sts2State.Var(r, "Block"); break;
                case "CloakClasp": p.CloakClaspPerCard += Sts2State.Var(r, "Block"); break;
                case "RippleBasin": p.RippleBasinBlock += Sts2State.Var(r, "Block"); break;
                case "ScreamingFlagon": p.ScreamingFlagonDamage += Sts2State.Var(r, "Damage"); break;
                case "StoneCalendar":
                    if (Sts2State.TurnNumber == Sts2State.Var(r, "DamageTurn", 7)) p.StoneCalendarDamage += Sts2State.Var(r, "Damage");
                    break;
                case "CharonsAshes": p.CharonsAshesDamage += Sts2State.Var(r, "Damage"); break;
                case "ForgottenSoul": p.ForgottenSoulDamage += Sts2State.Var(r, "Damage"); break;
                case "ParryingShield":
                    p.ParryingShieldBlock = Sts2State.Var(r, "Block");
                    p.ParryingShieldDamage += Sts2State.Var(r, "Damage");
                    break;
                case "TungstenRod": p.TungstenRod += Sts2State.Var(r, "HpLossReduction", 1); break;
                case "SneckoSkull": p.PoisonBonus += Sts2State.Var(r, "PoisonPower", 1); break;
                case "GoldPlatedCables": p.FrontOrbBonusTriggers += 1; break; // +1 for the front orb (ModifyOrbPassiveTriggerCounts)
                case "BeatingRemnant":
                {
                    var cap = Sts2State.Var(r, "MaxHpLoss", int.MaxValue);
                    var used = (int)Sts2State.Private(r, "DamageReceivedThisTurn", 0m);
                    p.RemnantCap = cap;
                    p.RemnantLeftPlayerTurn = Math.Max(0, cap - used);
                    break;
                }
            }
        }

        p.HandCount = hand.Count;
        p.Fading = hand.Count(card => IsFading(card, cs));
        p.AttacksThisTurn = Sts2State.CountPlayedThisTurn(me, CardType.Attack);
        foreach (var card in hand)
        {
            var type = card.GetType().Name;
            if (BlockableHandCards.Contains(type)) p.HandBlockable.Add((SafeCardTitle(card), Sts2State.Var(card, "Damage")));
            else if (UnblockableHandCards.Contains(type)) p.HandUnblockable.Add((SafeCardTitle(card), Sts2State.Var(card, "HpLoss")));
            else if (type == "Regret") p.RegretCards++;
        }

        p.OrbCapacity = pcs.OrbQueue.Capacity;
        foreach (var orb in pcs.OrbQueue.Orbs) p.Orbs.Add(ToSim(orb, cs));
        // The game's trigger count includes Gold-Plated Cables' bonus on the current front orb; the
        // simulator gives that bonus to whichever orb is in front when passives fire.
        if (p.Orbs.Count > 0) p.Orbs[0].Triggers = Math.Max(1, p.Orbs[0].Triggers - p.FrontOrbBonusTriggers);

        var osty = me.Osty;
        p.OstyHp = osty != null && osty.IsAlive && osty.HasPower<DieForYouPower>() ? osty.CurrentHp : 0;
        return s;
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

    /// <summary>
    /// A newly channelled orb's values: the base values plus your Focus (FocusPower.ModifyOrbValue, not
    /// below 0), plus Infused Core's bonus on Lightning (InfusedCore.ModifyOrbValue).
    /// </summary>
    private static SimOrb Fresh(OrbKind kind, int focus, int lightningBonus)
    {
        int F(int v) => Math.Max(0, v + focus);
        return kind switch
        {
            OrbKind.Lightning => new SimOrb { Kind = kind, Passive = F(3) + lightningBonus, Evoke = F(8) + lightningBonus },
            OrbKind.Frost => new SimOrb { Kind = kind, Passive = F(2), Evoke = F(5) },
            OrbKind.Dark => new SimOrb { Kind = kind, Passive = F(6), Evoke = 6 },
            OrbKind.Glass => new SimOrb { Kind = kind, Passive = F(4), Evoke = F(4) * 2, GlassBase = 4, GlassBonus = focus },
            OrbKind.Plasma => new SimOrb { Kind = kind, Passive = 1, Evoke = 2 }, // energy; Focus doesn't change it (PlasmaOrb)
            _ => new SimOrb { Kind = kind },
        };
    }

    // ------------------------------------------------------------------ card plays

    /// <summary>
    /// The play of <paramref name="card"/> aimed at <paramref name="targetId"/> (for single-target cards),
    /// from its PlayFacts, plus the damage and block procs the proc rules predicted for it.
    /// </summary>
    public static SimPlay Play(CardModel card, PlayFacts f, Player me, ICombatState cs, IReadOnlyDictionary<Creature, int> ids,
        int targetId, List<ProcHit> procs, List<int> procBlock)
    {
        var name = card.GetType().Name;
        var plays = Math.Max(1, f.Plays);
        var play = new SimPlay
        {
            Name = name,
            IsAttack = card.Type == CardType.Attack,
            Plays = plays,
            HandDelta = -1 + f.Draws + f.Generated,
            FadingDelta = IsFading(card, cs) ? -1 : 0,
            Procs = procs,
            ProcBlock = procBlock,
        };

        if (f.AttackHits > 0)
        {
            foreach (var (e, id) in ids)
            {
                if (!f.CanHit(e)) continue;
                play.HitDamage[id] = f.DamagePerHit(e);
            }
            switch (card.TargetType)
            {
                case TargetType.AnyEnemy:
                    play.HitTarget = SimPlay.Target.Single;
                    play.TargetId = targetId;
                    var target = ids.FirstOrDefault(kv => kv.Value == targetId).Key;
                    play.Hits = target != null ? f.HitsOn(target) : f.AttackHits;
                    break;
                case TargetType.AllEnemies:
                    play.HitTarget = SimPlay.Target.All;
                    play.Hits = f.AttackHits;
                    break;
                case TargetType.RandomEnemy:
                    play.HitTarget = SimPlay.Target.Random;
                    play.Hits = f.AttackHits;
                    break;
            }
        }

        // Its own block: one gain per play.
        var blockGains = f.BlockGains;
        if (blockGains > 0)
        {
            var total = CardBlock(card, cs, me) * plays;
            play.BlockGains = blockGains;
            play.BlockAmount = blockGains > 0 ? total / blockGains : 0;
        }

        // Orbs: evoke cards, then channels.
        var focus = me.Creature.GetPower<FocusPower>()?.Amount ?? 0;
        var lightningBonus = me.Relics.Where(r => r.GetType().Name == "InfusedCore").Sum(r => Sts2State.Var(r, "ExtraDamage"));
        for (var i = 0; i < plays; i++)
        {
            switch (name)
            {
                case "Dualcast": play.OrbOps.Add(new OrbOp { Op = OrbOp.Kind.EvokeFront, Times = 2 }); break;
                case "Quadcast": play.OrbOps.Add(new OrbOp { Op = OrbOp.Kind.EvokeFront, Times = Math.Max(1, Sts2State.Var(card, "Repeat", 4)) }); break;
                case "MultiCast":
                {
                    var x = SafeInt(() => Hook.ModifyXValue(cs, card, card.EnergyCost.GetAmountToSpend())) + (card.IsUpgraded ? 1 : 0);
                    if (x > 0) play.OrbOps.Add(new OrbOp { Op = OrbOp.Kind.EvokeFront, Times = x });
                    break;
                }
                case "Shatter": play.OrbOps.Add(new OrbOp { Op = OrbOp.Kind.EvokeAllTwice }); break;
            }
            if (Channels.TryGetValue(name, out var kinds))
            {
                foreach (var k in kinds) play.OrbOps.Add(new OrbOp { Op = OrbOp.Kind.Channel, Orb = Fresh(k, focus, lightningBonus) });
            }
            else if (RepeatChannels.TryGetValue(name, out var rk))
            {
                for (var r = 0; r < Math.Max(1, Sts2State.Var(card, "Repeat", 1)); r++) play.OrbOps.Add(new OrbOp { Op = OrbOp.Kind.Channel, Orb = Fresh(rk, focus, lightningBonus) });
            }
            else if (name == "Tempest")
            {
                var x = SafeInt(() => Hook.ModifyXValue(cs, card, card.EnergyCost.GetAmountToSpend())) + (card.IsUpgraded ? 1 : 0); // Tempest+ channels one more
                for (var r = 0; r < x; r++) play.OrbOps.Add(new OrbOp { Op = OrbOp.Kind.Channel, Orb = Fresh(OrbKind.Lightning, focus, lightningBonus) });
            }
        }

        // Powers it gives you, and poison it applies.
        foreach (var (key, v) in card.DynamicVars)
        {
            var t = v.GetType();
            if (!t.IsGenericType || t.GetGenericTypeDefinition() != typeof(PowerVar<>)) continue;
            var power = t.GetGenericArguments()[0].Name;
            var amount = (int)v.BaseValue * plays;
            if (amount <= 0) continue;
            switch (power)
            {
                case "PlatingPower": play.AddPlating += amount; break;
                case "JuggernautPower": play.AddJuggernaut += amount; break;
                case "FeelNoPainPower": play.AddFeelNoPain += amount; break;
                case "ThornsPower": play.AddThorns += amount; break;
                case "FlameBarrierPower": play.AddFlameBarrier += amount; break;
                case "PoisonPower":
                    // Snecko Skull adds to every application (ModifyPowerAmountGivenAdditive).
                    amount += plays * me.Relics.Where(r => r.GetType().Name == "SneckoSkull").Sum(r => Sts2State.Var(r, "PoisonPower", 1));
                    if (card.TargetType == TargetType.AnyEnemy)
                    {
                        play.Poison += amount;
                    }
                    else if (card.TargetType == TargetType.AllEnemies)
                    {
                        play.Poison += amount;
                        play.PoisonAll = true;
                    }
                    break;
            }
        }
        if (play.Poison > 0 && !play.PoisonAll) play.TargetId = targetId;

        // Intents the play changes:
        //  - Weak lowers the intent damage of an enemy that isn't Weak yet (WeakPower.ModifyDamageMultiplicative).
        //    With Artifact a charge may take it instead (which debuff it takes depends on the order), so
        //    that intent is left as it is.
        //  - Enrage: every Skill you play gives it Strength (EnragePower.AfterCardPlayed).
        var appliesWeak = card.DynamicVars.Values.Any(v => v.GetType() is { IsGenericType: true } t && t.GetGenericTypeDefinition() == typeof(PowerVar<>)
                                                           && t.GetGenericArguments()[0] == typeof(WeakPower) && v.BaseValue > 0);
        var weakened = !appliesWeak
            ? new HashSet<Creature>()
            : card.TargetType switch
            {
                TargetType.AnyEnemy => ids.Where(kv => kv.Value == targetId).Select(kv => kv.Key).ToHashSet(),
                TargetType.AllEnemies => ids.Keys.Where(e => e.IsAlive && e.IsHittable).ToHashSet(),
                _ => new HashSet<Creature>(),
            };
        foreach (var (e, id) in ids)
        {
            if (!e.IsAlive) continue;
            var weak = weakened.Contains(e) && !e.HasPower<WeakPower>() && (e.GetPower<ArtifactPower>()?.Amount ?? 0) == 0;
            var strength = card.Type == CardType.Skill ? (e.GetPower<EnragePower>()?.Amount ?? 0) * plays : 0;
            if (!weak && strength == 0) continue;
            if (AdjustedHits(e, me, cs, strength, weak) is { } hits) play.SetHits[id] = hits;
        }
        return play;
    }

    /// <summary>
    /// An enemy's intent hits with <paramref name="strength"/> more Strength and/or once it's Weak: the
    /// game's damage math (Hook.ModifyDamage, before the int cast; Strength is additive, so it's added
    /// to the base) times Weak's multiplier (0.75; Paper Krane on you takes 0.15 more off, Debilitate
    /// on the enemy doubles the reduction), then any damage cap. Null when the intent can't be read.
    /// </summary>
    private static List<int>? AdjustedHits(Creature e, Player me, ICombatState cs, int strength, bool weak)
    {
        try
        {
            var m = 1m;
            if (weak)
            {
                m = 0.75m;
                if (me.Relics.Any(r => r.GetType().Name == "PaperKrane")) m -= 0.15m;
                if (e.Powers.Any(p => p.GetType().Name == "DebilitatePower")) m -= 1m - m;
            }
            var hits = new List<int>();
            foreach (var attack in e.Monster!.NextMove.Intents.OfType<AttackIntent>())
            {
                if (attack.DamageCalc == null) return null;
                var raw = attack.DamageCalc() + strength;
                var v = Hook.ModifyDamage(me.RunState, cs, me.Creature, e, raw, MegaCrit.Sts2.Core.ValueProps.ValueProp.Move, null, null,
                    ModifyDamageHookType.Additive | ModifyDamageHookType.Multiplicative, CardPreviewMode.None, out _);
                var capped = Hook.ModifyDamage(me.RunState, cs, me.Creature, e, raw, MegaCrit.Sts2.Core.ValueProps.ValueProp.Move, null, null,
                    ModifyDamageHookType.All, CardPreviewMode.None, out _);
                var adjusted = v * m;
                if (capped < v) adjusted = Math.Min(adjusted, capped);
                var perHit = Math.Max(0, (int)adjusted);
                for (var i = 0; i < Math.Max(1, attack.Repeats); i++) hits.Add(perHit);
            }
            return hits;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>The card's own block per play (its Block value through the game's block math).</summary>
    public static int CardBlock(CardModel card, ICombatState cs, Player me)
    {
        try
        {
            decimal raw;
            if (card.DynamicVars.TryGetValue("CalculatedBlock", out var cv) && cv is CalculatedVar calc) raw = calc.Calculate(null);
            else if (card.DynamicVars.TryGetValue("Block", out var bv)) raw = bv.BaseValue;
            else return 0;
            if (raw <= 0) return 0;
            return Math.Max(0, (int)Hook.ModifyBlock(cs, me.Creature, raw, MegaCrit.Sts2.Core.ValueProps.ValueProp.Move, card, null, out _));
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

    private static string SafeCardTitle(CardModel c)
    {
        try { return c.Title; } catch { return c.GetType().Name; }
    }
}
