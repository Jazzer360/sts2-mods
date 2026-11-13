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
public static class SimAdapter
{
    // Blockable end-of-turn damage from cards in hand (DamageVar "Damage").
    private static readonly HashSet<string> BlockableHandCards = new() { nameof(Burn), nameof(Decay), nameof(Infection), nameof(Toxic), nameof(Wither) };

    // Unblockable HP loss from cards in hand (HpLossVar "HpLoss"; Regret = cards in hand).
    private static readonly HashSet<string> UnblockableHandCards = new() { nameof(BadLuck), nameof(Beckon) };

    // Debuffs on you that deal blockable damage after your turn ends, before block clears.
    private static readonly HashSet<string> BlockableDebuffs = new() { nameof(ConstrictPower), nameof(MagicBombPower) };

    // Debuffs that land after Parrying Shield (AfterSideTurnEndLate).
    private static readonly HashSet<string> LateBlockableDebuffs = new() { nameof(DisintegrationPower) };

    private static readonly HashSet<string> UnblockableDebuffs = new() { nameof(DemisePower) };

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

    /// <summary>The simulator channels this card's orbs (for the coverage check).</summary>
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
    };

    /// <summary>
    /// A tracked power the card's code applies without a PowerVar for it: its plain value ("Power":
    /// Feel No Pain; "DamageBack": Flame Barrier), 0 when it has neither (the card scan reports it), or
    /// null (none, or it has a PowerVar, read with the others).
    /// </summary>
    private static readonly string[] PlainPowerVars = { "Power", "DamageBack" };

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

    /// <summary>
    /// The power the simulator models for a power type: a temporary Thorns power (Cho Barbs, v0.113.0) gives real
    /// Thorns until the end of the enemy turn (TemporaryThornsPower.BeforeApplied applies ThornsPower), so it's Thorns.
    /// </summary>
    internal static string SimPowerName(Type power) => power.BaseType?.Name == "TemporaryThornsPower" ? nameof(ThornsPower) : power.Name;

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
    private static readonly Dictionary<string, (string? Var, bool All, int Sign)> EnemyStrengthCards = new()
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
                Burrowed = e.HasPower<BurrowedPower>(),
                Asleep = e.HasPower<AsleepPower>(),
                Flutter = e.GetPower<FlutterPower>()?.Amount ?? 0,
                StunAtHp = Math.Max(e.GetPower<PlowPower>()?.Amount ?? 0, e.GetPower<ShriekPower>()?.Amount ?? 0),
                Gambit = e.HasPower<TheGambitPower>(),
                Slippery = e.GetPower<SlipperyPower>()?.Amount ?? 0,
            };
            // Thorns hits you back (unpowered, blockable) before each of your card's attack hits on it.
            if (e.GetPower<ThornsPower>() is { Amount: > 0 } thorns)
            {
                se.Thorns = SafeInt(() => Math.Max(0, (int)Hook.ModifyDamage(me.RunState, cs, me.Creature, e, thorns.Amount,
                    MegaCrit.Sts2.Core.ValueProps.ValueProp.Unpowered | MegaCrit.Sts2.Core.ValueProps.ValueProp.SkipHurtAnim, null, null,
                    ModifyDamageHookType.All, CardPreviewMode.None, out _)));
            }
            // Burrowed keeps its block between turns (BurrowedPower.ShouldClearBlock), like Barricade.
            if (se.Burrowed) se.Barricade = true;
            se.StartedAlive = e.IsAlive;
            // An ally dying: Ravenous stuns it (and gives Strength for later); Crab Rage gives it Strength once.
            se.Ravenous = e.HasPower<RavenousPower>();
            if (e.GetPower<CrabRagePower>() is { } crab && AdjustedHits(e, me, cs, Sts2State.Var(crab, "StrengthPower"), false) is { } enraged)
            {
                se.HitsAfterAllyDies = enraged;
                se.CrabRageBlock = Sts2State.Var(crab, "Block");
            }
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

        FillPlayer(s.P, me, cs);
        s.P.BeaconOfHope = me.Creature.HasPower<BeaconOfHopePower>();

        // Teammates (multiplayer): built like you (FillPlayer), in player order. Decision #25: their hand is
        // used too (it only changes their own HP in the simulator; nothing shown depends on it yet).
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
            FillPlayer(t, other, cs);
            t.Hp = oc.CurrentHp;
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
            }
        }
        return s;
    }

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
    private static void FillPlayer(SimPlayer p, Player me, ICombatState cs)
    {
        var c = me.Creature;
        var pcs = me.PlayerCombatState!;
        var hand = Sts2State.Hand(me);
        p.Block = c.Block;
        foreach (var pw in c.Powers)
        {
            switch (pw.GetType().Name)
            {
                case nameof(JuggernautPower): p.Juggernaut += pw.Amount; break;
                case nameof(PlatingPower): p.Plating += pw.Amount; break;
                case nameof(FeelNoPainPower): p.FeelNoPain += pw.Amount; break;
                case nameof(HailstormPower):
                    p.Hailstorm += pw.Amount;
                    p.HailstormFrostNeeded = Sts2State.Var(pw, "FrostOrbs", 1);
                    break;
                case nameof(TheBombPower):
                    if (pw.Amount <= 1) p.TheBombDamage += Sts2State.Var(pw, "Damage");
                    break;
                case nameof(ConsumingShadowPower): p.ConsumingShadow += pw.Amount; break;
                case nameof(ThornsPower): p.Thorns += pw.Amount; break;
                case nameof(FlameBarrierPower): p.FlameBarrier += pw.Amount; break;
                case nameof(ReflectPower): p.Reflect = true; break;
                case nameof(InfernoPower): p.Inferno += pw.Amount; break; // only yours is simulated (SimEngine.Inferno)
                case nameof(BufferPower): p.Buffer += pw.Amount; break;
                // Envenom, and Concoct this turn: poison on each attack hit that gets through block (AfterDamageGiven).
                case nameof(EnvenomPower) or nameof(ConcoctPower): p.Envenom += pw.Amount; break;
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
                case nameof(Orichalcum):
                case nameof(FakeOrichalcum): p.OrichalcumBlock += Sts2State.Var(r, "Block"); break;
                case nameof(CloakClasp): p.CloakClaspPerCard += Sts2State.Var(r, "Block"); break;
                case nameof(RippleBasin): p.RippleBasinBlock += Sts2State.Var(r, "Block"); break;
                case nameof(ScreamingFlagon): p.ScreamingFlagonDamage += Sts2State.Var(r, "Damage"); break;
                case "ObsidianVice": p.ObsidianViceDamage += Sts2State.Var(r, "Damage", 1); break;
                case nameof(StoneCalendar):
                    if (Sts2State.TurnNumber == Sts2State.Var(r, "DamageTurn", 7)) p.StoneCalendarDamage += Sts2State.Var(r, "Damage");
                    break;
                case nameof(CharonsAshes): p.CharonsAshesDamage += Sts2State.Var(r, "Damage"); break;
                case nameof(ForgottenSoul): p.ForgottenSoulDamage += Sts2State.Var(r, "Damage"); break;
                case nameof(ParryingShield):
                    p.ParryingShieldBlock = Sts2State.Var(r, "Block");
                    p.ParryingShieldDamage += Sts2State.Var(r, "Damage");
                    break;
                case nameof(TungstenRod): p.TungstenRod += Sts2State.Var(r, "HpLossReduction", 1); break;
                case nameof(TheBoot): p.BootMinimum = Math.Max(p.BootMinimum, Sts2State.Var(r, "DamageMinimum", 5)); break;
                case nameof(SneckoSkull): p.PoisonBonus += Sts2State.Var(r, nameof(PoisonPower), 1); break;
                case nameof(GoldPlatedCables): p.FrontOrbBonusTriggers += 1; break; // +1 for the front orb (ModifyOrbPassiveTriggerCounts)
                case nameof(BeatingRemnant):
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
            else if (type == nameof(Regret)) p.RegretCards++;
        }

        p.Focus = c.GetPower<FocusPower>()?.Amount ?? 0;
        p.OrbCapacity = pcs.OrbQueue.Capacity;
        p.SlotOnFirstChannel = SafeInt(() => me.Character.BaseOrbSlotCount) == 0;
        p.Thunder = me.Creature.GetPower<ThunderPower>()?.Amount ?? 0;
        foreach (var orb in pcs.OrbQueue.Orbs) p.Orbs.Add(ToSim(orb, cs));
        // The game's trigger count includes Gold-Plated Cables' bonus on the current front orb; the
        // simulator gives that bonus to whichever orb is in front when passives fire.
        if (p.Orbs.Count > 0) p.Orbs[0].Triggers = Math.Max(1, p.Orbs[0].Triggers - p.FrontOrbBonusTriggers);

        var osty = me.Osty;
        p.OstyHp = osty != null && osty.IsAlive && osty.HasPower<DieForYouPower>() ? osty.CurrentHp : 0;
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
                if (f.LaterHitDamage(e) is { } later) play.HitDamageLater[id] = later;
            }
            switch (card.TargetType)
            {
                case TargetType.AnyEnemy:
                    play.HitTarget = SimPlay.Target.Single;
                    play.TargetId = targetId;
                    var target = ids.FirstOrDefault(kv => kv.Value == targetId).Key;
                    play.Hits = target != null ? f.HitsOn(target) : f.AttackHits;
                    play.SplashFromTarget = name == "RainShot"; // RainShot.OnPlay: the target's damage to every other enemy
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

        // Its own block, each gain its full amount (Second Wind per card it exhausts, Cho Limbs and Cho Firm twice).
        // Fisticuffs: the damage it dealt (overkill included); Entrench: your current block again.
        var blockGains = f.BlockGains;
        if (blockGains > 0)
        {
            var aimed = ids.FirstOrDefault(kv => kv.Value == targetId).Key;
            var each = name switch
            {
                nameof(Fisticuffs) => aimed != null ? f.TotalDamage(aimed) / Math.Max(1, plays) : 0,
                nameof(Entrench) => me.Creature.Block,
                _ => CardBlock(card, cs, me),
            };
            play.BlockGains = blockGains;
            play.BlockAmount = each;
            // Stretch (v0.113.0) boosts one card play's block, then it's spent: a replay's later plays gain without it.
            // Its bonus is additive, so the game's block math on (Block - Stretch) gives the block without it.
            var stretch = me.Creature.Powers.Where(x => x.GetType().Name == "StretchPower").Sum(x => x.Amount);
            if (stretch > 0 && plays > 1 && name is not (nameof(Fisticuffs) or nameof(Entrench)) && !KeepsStretchCards.Contains(name) && card.DynamicVars.ContainsKey("Block"))
            {
                var perPlay = blockGains / plays;
                var without = SafeInt(() => Math.Max(0, (int)Hook.ModifyBlock(cs, me.Creature, Sts2State.Var(card, "Block") - stretch,
                    MegaCrit.Sts2.Core.ValueProps.ValueProp.Move, card, null, out _)));
                if (perPlay > 0 && without < each) play.BlockLater = (perPlay, without);
            }
        }

        play.KillsOsty = KillsOstyCards.Contains(name);

        // Orbs: evoke cards, then channels.
        var focus = me.Creature.GetPower<FocusPower>()?.Amount ?? 0;
        var lightningBonus = me.Relics.Where(r => r.GetType().Name == nameof(InfusedCore)).Sum(r => Sts2State.Var(r, "ExtraDamage"));
        for (var i = 0; i < plays; i++)
        {
            // Slot changes come first in these cards' plays.
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
                // Spinner channels only when upgraded; Chill channels one Frost per hittable enemy.
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
                var x = SafeInt(() => Hook.ModifyXValue(cs, card, card.EnergyCost.GetAmountToSpend())) + (card.IsUpgraded ? 1 : 0); // Tempest+ channels one more
                for (var r = 0; r < x; r++) play.OrbOps.Add(new OrbOp { Op = OrbOp.Kind.Channel, Orb = Fresh(OrbKind.Lightning, focus, lightningBonus) });
            }
        }

        play.FocusDelta = FocusChange(card) * plays;

        // Storm: Lightning per Power played (StormPower.AfterCardPlayed). Trash to Treasure: a random orb
        // per status the play creates (its kind is a roll: Unknown, so only the evoke it causes and the
        // slot it takes count).
        var storm = me.Creature.Powers.Where(p => p is StormPower).Sum(p => p.Amount);
        if (card.Type == CardType.Power)
            for (var i = 0; i < storm * plays; i++) play.OrbOps.Add(new OrbOp { Op = OrbOp.Kind.Channel, Orb = Fresh(OrbKind.Lightning, focus, lightningBonus) });
        var trash = me.Creature.Powers.Where(p => p is TrashToTreasurePower).Sum(p => p.Amount);
        for (var i = 0; i < trash * f.GeneratedStatuses; i++) play.OrbOps.Add(new OrbOp { Op = OrbOp.Kind.Channel, Orb = new SimOrb { Kind = OrbKind.Unknown } });

        // Powers it gives you, and poison it applies.
        foreach (var (key, v) in card.DynamicVars)
        {
            var t = v.GetType();
            if (!t.IsGenericType || t.GetGenericTypeDefinition() != typeof(PowerVar<>)) continue;
            var power = SimPowerName(t.GetGenericArguments()[0]);
            var amount = (int)v.BaseValue * plays;
            if (amount <= 0) continue;
            switch (power)
            {
                case nameof(PlatingPower): play.AddPlating += amount; break;
                case nameof(JuggernautPower): play.AddJuggernaut += amount; break;
                case nameof(FeelNoPainPower): play.AddFeelNoPain += amount; break;
                case nameof(ThornsPower): play.AddThorns += amount; break;
                case nameof(FlameBarrierPower): play.AddFlameBarrier += amount; break;
                case nameof(PoisonPower):
                {
                    // Snecko Skull adds to every application (ModifyPowerAmountGivenAdditive).
                    var skull = me.Relics.Where(r => r.GetType().Name == nameof(SneckoSkull)).Sum(r => Sts2State.Var(r, nameof(PoisonPower), 1));
                    if (card.TargetType == TargetType.RandomEnemy)
                    {
                        // A random enemy each time, Repeat times (Bouncing Flask).
                        play.RandomPoison = (int)v.BaseValue + skull;
                        play.RandomPoisonTimes += Math.Max(1, Sts2State.Var(card, "Repeat", 1)) * plays;
                        break;
                    }
                    amount += plays * skull;
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
        }
        // Your powers its code applies with a plain value (Feel No Pain, Flame Barrier).
        foreach (var applied in Diagnostics.CardCoverage.PowersApplied(card.GetType()))
        {
            if (PlainSelfPowerAmount(card, applied) is not { } each) continue;
            var amount = each * plays;
            switch (applied.Name)
            {
                case nameof(PlatingPower): play.AddPlating += amount; break;
                case nameof(JuggernautPower): play.AddJuggernaut += amount; break;
                case nameof(FeelNoPainPower): play.AddFeelNoPain += amount; break;
                case nameof(ThornsPower): play.AddThorns += amount; break;
                case nameof(FlameBarrierPower): play.AddFlameBarrier += amount; break;
            }
        }
        if (play.Poison > 0 && !play.PoisonAll) play.TargetId = targetId;

        // Intents the play changes:
        //  - Weak lowers the intent damage of an enemy that isn't Weak yet (WeakPower.ModifyDamageMultiplicative).
        //    With Artifact a charge may take it instead (which debuff it takes depends on the order), so
        //    that intent is left as it is.
        //  - Enrage: every Skill you play gives it Strength (EnragePower.AfterCardPlayed).
        HashSet<Creature> Debuffed(Type debuff) => !f.EnemyDebuffs.Contains(debuff)
                                                   // Nail does nothing to an enemy that isn't Impaled (Nail.OnPlay).
                                                   || (name == "Nail" && ids.FirstOrDefault(kv => kv.Value == targetId).Key is { } nt && !Procs.Fool.IsImpaled(nt))
            ? new HashSet<Creature>()
            : card.TargetType switch
            {
                TargetType.AnyEnemy => ids.Where(kv => kv.Value == targetId).Select(kv => kv.Key).ToHashSet(),
                TargetType.AllEnemies => ids.Keys.Where(e => e.IsAlive && e.IsHittable).ToHashSet(),
                _ => new HashSet<Creature>(),
            };
        var weakened = Debuffed(typeof(WeakPower));
        //  - Colossus halves powered attacks on you from Vulnerable enemies (ColossusPower.ModifyDamageMultiplicative,
        //    not stacking): playing it halves the intents of enemies already Vulnerable; while you have it,
        //    Vulnerable the card applies halves that enemy's.
        var hasColossus = me.Creature.HasPower<ColossusPower>();
        var gainsColossus = !hasColossus && name == nameof(Colossus);
        var vulnerabled = hasColossus ? Debuffed(typeof(VulnerablePower)) : new HashSet<Creature>();
        //  - Strength the card takes away (this turn: Piercing Wail, Dark Shackles...; for good: Shared
        //    Fate, Resonance) or gives (Fight Me). Losing Strength is a debuff Artifact may take, so an
        //    enemy with Artifact keeps its intent.
        EnemyStrengthCards.TryGetValue(name, out var str);
        var gaze = me.Creature.Powers.Where(p => p is MonarchsGazePower).Sum(p => p.Amount);
        //  - Vital Spark (enemy): playing a Tainted card gives you Tainted (VitalSparkPower.AfterCardPlayed), which adds
        //    to every powered attack on you (TaintedPower.ModifyDamageAdditive): additive like the attacker's Strength,
        //    so the game's math with that much more Strength gives your new hits. Not on teammates.
        var tainted = card.Affliction is MegaCrit.Sts2.Core.Models.Afflictions.Tainted
            ? cs.Enemies.Where(x => x.IsAlive).Sum(x => x.GetPower<VitalSparkPower>()?.Amount ?? 0) * plays
            : 0;
        var strengthTarget = ids.FirstOrDefault(kv => kv.Value == targetId).Key;
        //  - Surrounded (Kaiser Crab): a card aimed at the crab behind you turns you to face it
        //    (SurroundedPower.BeforeCardPlayed), and the one now behind you hits 50% harder
        //    (ModifyDamageMultiplicative, on you only).
        var surrounded = me.Creature.GetPower<SurroundedPower>();
        var facing = surrounded?.Facing;
        if (surrounded != null && strengthTarget != null)
        {
            if (facing == SurroundedPower.Direction.Right && strengthTarget.HasPower<BackAttackLeftPower>()) facing = SurroundedPower.Direction.Left;
            else if (facing == SurroundedPower.Direction.Left && strengthTarget.HasPower<BackAttackRightPower>()) facing = SurroundedPower.Direction.Right;
        }
        bool Behind(Creature e, SurroundedPower.Direction? d) => d == SurroundedPower.Direction.Right ? e.HasPower<BackAttackLeftPower>()
            : d == SurroundedPower.Direction.Left && e.HasPower<BackAttackRightPower>();
        foreach (var (e, id) in ids)
        {
            if (!e.IsAlive) continue;
            var artifact = (e.GetPower<ArtifactPower>()?.Amount ?? 0) > 0;
            var weak = weakened.Contains(e) && !e.HasPower<WeakPower>() && !artifact;
            var halved = (gainsColossus && e.HasPower<VulnerablePower>())
                         || (vulnerabled.Contains(e) && !e.HasPower<VulnerablePower>() && !artifact);
            var strength = card.Type == CardType.Skill ? (e.GetPower<EnragePower>()?.Amount ?? 0) * plays : 0;
            if (str.Var != null && (str.All ? e.IsHittable : e == strengthTarget) && (str.Sign > 0 || !artifact))
                strength += str.Sign * (str.Var == "" ? 1 : Sts2State.Var(card, str.Var)) * plays;
            // Monarch's Gaze: each of your attack's hits takes Strength from the enemy it hits this turn
            // (MonarchsGazeStrengthDownPower). Random-target attacks are left out (which enemy is a roll).
            if (gaze > 0 && !artifact && f.CanHit(e) && (card.TargetType == TargetType.AllEnemies || (card.TargetType == TargetType.AnyEnemy && e == strengthTarget)))
                strength -= gaze * f.HitsOn(e);
            var wasBehind = surrounded != null && Behind(e, surrounded.Facing);
            var nowBehind = surrounded != null && Behind(e, facing);
            if (!weak && !halved && strength == 0 && tainted == 0 && wasBehind == nowBehind) continue;
            var turn = (halved ? 0.5m : 1m) * (nowBehind && !wasBehind ? 1.5m : 1m);
            if (AdjustedHits(e, me, cs, strength + tainted, weak, turn, divisor: wasBehind && !nowBehind ? 1.5m : 1m) is { } hits) play.SetHits[id] = hits;
            // Teammates (same order as Build): Weak and Strength change their hits too; Colossus only protects you.
            if (!weak && strength == 0) continue;
            var mates = Teammates(me, cs);
            if (mates.Count == 0) continue;
            var teamHits = new List<List<int>>();
            foreach (var mate in mates)
            {
                if (AdjustedHits(e, me, cs, strength, weak, 1m, mate) is not { } th) { teamHits = null; break; }
                teamHits.Add(th);
            }
            if (teamHits != null) play.SetTeammateHits[id] = teamHits;
        }
        return play;
    }

    /// <summary>
    /// An enemy's intent hits with <paramref name="strength"/> more Strength and/or once it's Weak: the
    /// game's damage math (Hook.ModifyDamage, before the int cast; Strength is additive, so it's added
    /// to the base) times Weak's multiplier (0.75; Paper Krane on you takes 0.15 more off, Debilitate
    /// on the enemy doubles the reduction) and <paramref name="multiplier"/> (Colossus: 0.5), then any damage
    /// cap. Null when the intent can't be read.
    /// </summary>
    /// <param name="target">The player hit (a teammate); you when null.</param>
    /// <param name="divisor">Divides the game's current math (Surrounded: no longer behind you takes its 1.5 back out, exactly).</param>
    private static List<int>? AdjustedHits(Creature e, Player me, ICombatState cs, int strength, bool weak, decimal multiplier = 1m, Player? target = null, decimal divisor = 1m)
    {
        try
        {
            var who = target ?? me;
            var m = multiplier;
            if (weak)
            {
                var w = 0.75m;
                if (who.Relics.Any(r => r.GetType().Name == nameof(PaperKrane))) w -= 0.15m;
                if (e.Powers.Any(p => p.GetType().Name == nameof(DebilitatePower))) w -= 1m - w;
                m *= w;
            }
            var hits = new List<int>();
            foreach (var attack in e.Monster!.NextMove.Intents.OfType<AttackIntent>())
            {
                if (attack.DamageCalc == null) return null;
                var raw = attack.DamageCalc() + strength;
                var v = Hook.ModifyDamage(me.RunState, cs, who.Creature, e, raw, MegaCrit.Sts2.Core.ValueProps.ValueProp.Move, null, null,
                    ModifyDamageHookType.Additive | ModifyDamageHookType.Multiplicative, CardPreviewMode.None, out _);
                var capped = Hook.ModifyDamage(me.RunState, cs, who.Creature, e, raw, MegaCrit.Sts2.Core.ValueProps.ValueProp.Move, null, null,
                    ModifyDamageHookType.All, CardPreviewMode.None, out _);
                var adjusted = v / divisor * m;
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
