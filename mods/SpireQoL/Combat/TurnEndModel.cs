using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Orbs;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.MonsterMoves.Intents;
using MegaCrit.Sts2.Core.ValueProps;
using Sts2Mods.Core.Game;

namespace SpireQoL.Combat;

public sealed record Source(string Name, int Amount);

/// <summary>
/// Predicts what happens between now and the start of your next turn: block you'll gain
/// automatically, damage that will actually land on you, and what gets through your block.
///
/// Enemy attacks are simulated hit by hit, in turn order, because:
///  - Poison ticks at the start of the enemy's turn, before it acts (PoisonPower.AfterSideTurnStart).
///    An enemy it kills never attacks. (Doom does NOT stop attacks: doomed enemies die at the end
///    of their own turn, DoomPower.BeforeSideTurnEnd.)
///  - Retaliation can kill an attacker mid-combo. The attack loop checks Attacker.IsDead before
///    each hit (AttackCommand), so later hits are cancelled. The hit during which Thorns kills
///    still lands: Thorns fires in BeforeDamageReceived, and CreatureCmd.Damage doesn't re-check
///    the dealer.
///  - Damage caps: Hard To Kill (Exoskeleton) caps each damage instance, and the game's own
///    numbers already include it (PoisonPower.CalculateTotalDamageNextTurn and Hook.ModifyDamage
///    both run the cap step). Hardened Shell (Skulking Colony) caps total HP lost per turn
///    (reset at turn start), which the game's poison preview doesn't include, so we apply it.
/// Every source is from the v0.111.0 decompile; see docs/TRACKER.md §4–5.
/// Values are raw (no Dexterity/Frail on end-of-turn block, which is Unpowered in the game).
/// </summary>
public sealed class TurnEndModel
{
    public List<Source> EndOfTurnBlock { get; } = new();

    /// <summary>Blockable damage that will actually land (self damage + enemy hits that happen).</summary>
    public List<Source> Blockable { get; } = new();
    public List<Source> Unblockable { get; } = new();

    public int CurrentBlock { get; private set; }

    /// <summary>Attacking enemies left out because Poison kills them before they act.</summary>
    public int SkippedPoisoned { get; private set; }

    /// <summary>Enemy hits cancelled because retaliation (Thorns etc.) kills the attacker first.</summary>
    public int CancelledHits { get; private set; }

    public int BlockGain => EndOfTurnBlock.Sum(s => s.Amount);
    public int BlockableTotal => Blockable.Sum(s => s.Amount);
    public int UnblockableTotal => Unblockable.Sum(s => s.Amount);
    public int ExpectedBlock => CurrentBlock + BlockGain;

    /// <summary>Blockable damage that gets through block (from the hit-by-hit simulation).</summary>
    public int DamageThrough { get; private set; }

    public int Overblock => Math.Max(0, ExpectedBlock - BlockableTotal);

    /// <summary>Necrobinder: Osty's HP now (0 if Osty isn't out), and how much of the enemy turn it absorbs.</summary>
    public int OstyHp { get; private set; }
    public int OstyTaken { get; private set; }
    public bool OstyDies => OstyHp > 0 && OstyTaken >= OstyHp;

    // Blockable end-of-turn damage from cards in hand (DamageVar "Damage").
    private static readonly HashSet<string> BlockableHandCards = new() { "Burn", "Decay", "Infection", "Toxic", "Wither" };

    // Unblockable HP loss from cards in hand (HpLossVar "HpLoss"; Regret = cards in hand).
    private static readonly HashSet<string> UnblockableHandCards = new() { "BadLuck", "Beckon" };

    // Debuffs on you that deal blockable damage after your turn ends, before block clears.
    private static readonly HashSet<string> BlockableDebuffs = new() { "ConstrictPower", "MagicBombPower", "DisintegrationPower" };

    private static readonly HashSet<string> UnblockableDebuffs = new() { "DemisePower" };

    public static TurnEndModel? Compute()
    {
        var me = Sts2State.Me;
        var cs = Sts2State.Combat;
        if (me?.PlayerCombatState == null || cs == null) return null;

        var m = new TurnEndModel { CurrentBlock = me.Creature.Block };
        var hand = Sts2State.Hand(me);
        var attacksThisTurn = Sts2State.CountPlayedThisTurn(me, CardType.Attack);

        // ---- End-of-turn block ----
        foreach (var power in me.Creature.Powers)
        {
            if (power is PlatingPower && power.Amount > 0)
            {
                m.EndOfTurnBlock.Add(new Source("Plating", power.Amount));
            }
        }

        var frost = 0m;
        foreach (var orb in me.PlayerCombatState.OrbQueue.Orbs)
        {
            if (orb is FrostOrb)
            {
                var triggers = Hook.ModifyOrbPassiveTriggerCount(cs, orb, 1, out _);
                frost += orb.PassiveVal * triggers;
            }
        }
        if (frost > 0) m.EndOfTurnBlock.Add(new Source("Frost", (int)frost));

        foreach (var relic in me.Relics)
        {
            switch (relic.GetType().Name)
            {
                case "Orichalcum":
                case "FakeOrichalcum":
                    // Checked before other end-of-turn block lands, against current block only.
                    if (me.Creature.Block <= 0) m.EndOfTurnBlock.Add(new Source(SafeTitle(relic), Sts2State.Var(relic, "Block")));
                    break;
                case "CloakClasp":
                    if (hand.Count > 0) m.EndOfTurnBlock.Add(new Source($"Cloak Clasp ×{hand.Count}", hand.Count * Sts2State.Var(relic, "Block")));
                    break;
                case "RippleBasin":
                    if (attacksThisTurn == 0) m.EndOfTurnBlock.Add(new Source(SafeTitle(relic), Sts2State.Var(relic, "Block")));
                    break;
                // Horn Cleat / Captain's Wheel give block at the START of turn 2/3, after the
                // enemy has attacked, so they don't belong here. Relic Counters shows their countdown.
            }
        }

        // Feel No Pain: Ethereal cards still in hand exhaust at end of turn (CombatManager.DoTurnEnd,
        // after Orichalcum has already checked your block), each giving its block (Unpowered).
        var fnp = me.Creature.Powers.Where(p => p is FeelNoPainPower).Sum(p => p.Amount);
        if (fnp > 0)
        {
            var fading = hand.Count(c => c.Keywords.Contains(CardKeyword.Ethereal)
                                         && (c.HasTurnEndInHandEffect || Hook.ShouldEtherealTrigger(cs, c)));
            if (fading > 0) m.EndOfTurnBlock.Add(new Source($"Feel No Pain ×{fading}", fading * fnp));
        }

        // ---- Your own end-of-turn damage (happens before the enemy turn) ----
        var block = m.ExpectedBlock;
        var regret = 0;
        foreach (var card in hand)
        {
            var type = card.GetType().Name;
            if (BlockableHandCards.Contains(type))
            {
                m.LandBlockable(SafeTitle(card), Sts2State.Var(card, "Damage"), ref block);
            }
            else if (UnblockableHandCards.Contains(type))
            {
                m.Unblockable.Add(new Source(SafeTitle(card), Sts2State.Var(card, "HpLoss")));
            }
            else if (type == "Regret")
            {
                regret++;
            }
        }
        if (regret > 0) m.Unblockable.Add(new Source(regret > 1 ? $"Regret ×{regret}" : "Regret", regret * hand.Count));

        foreach (var power in me.Creature.Powers)
        {
            var type = power.GetType().Name;
            if (power.Amount <= 0) continue;
            if (BlockableDebuffs.Contains(type)) m.LandBlockable(SafeTitle(power), power.Amount, ref block);
            else if (UnblockableDebuffs.Contains(type)) m.Unblockable.Add(new Source(SafeTitle(power), power.Amount));
        }

        // ---- Enemy turn, hit by hit ----
        var thorns = me.Creature.GetPower<ThornsPower>()?.Amount ?? 0;
        var flameBarrier = me.Creature.GetPower<FlameBarrierPower>()?.Amount ?? 0;
        var hasReflect = me.Creature.HasPower<ReflectPower>();
        var targets = cs.Players.Select(p => p.Creature).ToList();

        // Osty (Necrobinder): enemies never target it (monster attacks target PlayerCreatures), but
        // its Die For You power redirects a powered attack's unblocked damage on you to Osty
        // (DieForYouPower.ModifyUnblockedDamageTarget). Hits on you use your block first; Osty
        // takes what gets through, up to its HP, and the overkill comes back to you
        // (CreatureCmd.Damage). Unpowered damage (Burn, Constrict...) isn't redirected.
        var osty = me.Osty;
        var ostyHp = osty != null && osty.IsAlive && osty.HasPower<DieForYouPower>() ? osty.CurrentHp : 0;
        m.OstyHp = ostyHp;

        foreach (var enemy in cs.Enemies)
        {
            if (!enemy.IsAlive || enemy.Monster == null || enemy.IsStunned) continue;

            var attacks = enemy.Monster.NextMove.Intents.OfType<AttackIntent>().ToList();
            if (attacks.Count == 0) continue;

            // Enemy HP and Hardened Shell budget after its poison tick.
            var hp = enemy.CurrentHp;
            var shellLeft = enemy.GetPower<HardenedShellPower>()?.Amount ?? int.MaxValue;
            var poison = enemy.GetPower<PoisonPower>()?.CalculateTotalDamageNextTurn() ?? 0;
            var poisonTaken = Math.Min(poison, shellLeft);
            hp -= poisonTaken;
            if (shellLeft != int.MaxValue) shellLeft -= poisonTaken;
            if (hp <= 0)
            {
                m.SkippedPoisoned++;
                continue;
            }
            // Enemy block is cleared at the start of its turn unless it has Barricade.
            var enemyBlock = enemy.HasPower<BarricadePower>() ? enemy.Block : 0;

            var landed = 0;
            var hitsLanded = 0;
            var dead = false;
            foreach (var attack in attacks)
            {
                var perHit = attack.GetSingleDamage(targets, enemy);
                var hits = Math.Max(1, attack.Repeats);
                for (var i = 0; i < hits; i++)
                {
                    if (dead)
                    {
                        m.CancelledHits++;
                        continue;
                    }

                    // Thorns: before the hit lands. The hit still lands even if this kills.
                    if (thorns > 0) dead |= HitEnemy(Retaliate(me.Creature, enemy, thorns), ref hp, ref enemyBlock, ref shellLeft);

                    var blocked = Math.Min(block, perHit);
                    block -= blocked;
                    var through = perHit - blocked;
                    var toOsty = Math.Min(through, ostyHp);
                    ostyHp -= toOsty;
                    m.OstyTaken += toOsty;
                    m.DamageThrough += through - toOsty;
                    landed += perHit;
                    hitsLanded++;

                    // After the hit: Flame Barrier, and Reflect (damage = amount blocked).
                    if (!dead && flameBarrier > 0) dead |= HitEnemy(Retaliate(me.Creature, enemy, flameBarrier), ref hp, ref enemyBlock, ref shellLeft);
                    if (!dead && hasReflect && blocked > 0) dead |= HitEnemy(Retaliate(me.Creature, enemy, blocked), ref hp, ref enemyBlock, ref shellLeft);
                }
            }

            if (landed > 0)
            {
                var name = SafeTitle(enemy.Monster);
                m.Blockable.Add(new Source(hitsLanded > 1 ? $"{name} ({hitsLanded} hits)" : name, landed));
            }
        }

        return m;
    }

    private void LandBlockable(string name, int amount, ref int block)
    {
        if (amount <= 0) return;
        Blockable.Add(new Source(name, amount));
        var blocked = Math.Min(block, amount);
        block -= blocked;
        DamageThrough += amount - blocked;
    }

    /// <summary>
    /// A retaliation hit's damage after the game's own modifiers (Hook.ModifyDamage with all hook
    /// types). That includes damage caps: Exoskeleton's Hard To Kill caps each damage instance
    /// (ModifyDamageCap), so three 12-damage hits deal 9 each, 27 total.
    /// </summary>
    private static int Retaliate(Creature me, Creature enemy, int amount)
    {
        try
        {
            var dmg = Hook.ModifyDamage(me.Player!.RunState, enemy.CombatState, enemy, me, amount,
                ValueProp.Unpowered, null, null, ModifyDamageHookType.All, CardPreviewMode.None, out _);
            return Math.Max(0, (int)dmg);
        }
        catch
        {
            return amount;
        }
    }

    /// <summary>
    /// Retaliation damage to an enemy: through its block, then HP. Hardened Shell (Skulking
    /// Colony) is different from Hard To Kill: it caps the *total* HP lost per turn
    /// (HardenedShellPower.ModifyHpLostBeforeOstyLate), so we track what's left of it.
    /// </summary>
    private static bool HitEnemy(int amount, ref int hp, ref int enemyBlock, ref int shellLeft)
    {
        var absorbed = Math.Min(enemyBlock, amount);
        enemyBlock -= absorbed;
        var toHp = amount - absorbed;
        if (shellLeft != int.MaxValue)
        {
            toHp = Math.Min(toHp, Math.Max(0, shellLeft));
            shellLeft -= toHp;
        }
        hp -= toHp;
        return hp <= 0;
    }

    public static string SafeTitle(AbstractModel model)
    {
        try
        {
            var title = model switch
            {
                RelicModel r => r.Title.GetFormattedText(),
                CardModel c => c.Title,
                PowerModel p => p.Title.GetFormattedText(),
                MonsterModel mo => mo.Title.GetFormattedText(),
                PotionModel po => po.Title.GetFormattedText(),
                _ => null,
            };
            if (!string.IsNullOrWhiteSpace(title)) return title;
        }
        catch
        {
            // fall through to the class name
        }
        var name = model.GetType().Name;
        return name.EndsWith("Power") ? name[..^5] : name;
    }
}
