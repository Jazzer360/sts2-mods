using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Orbs;
using MegaCrit.Sts2.Core.MonsterMoves.Intents;
using Sts2Mods.Core.Game;

namespace SpireQoL.Combat;

public sealed record Source(string Name, int Amount);

/// <summary>
/// Predicts what happens between now and the start of your next turn: block you'll gain
/// automatically, blockable damage you'll take, and unblockable HP loss.
/// Every source is from the v0.111.0 decompile; see docs/TRACKER.md §4–5.
/// Values are raw (no Dexterity/Frail on end-of-turn block, which is Unpowered in the game).
/// </summary>
public sealed class TurnEndModel
{
    public List<Source> EndOfTurnBlock { get; } = new();
    public List<Source> NextTurnBlock { get; } = new();
    public List<Source> Blockable { get; } = new();
    public List<Source> Unblockable { get; } = new();

    public int CurrentBlock { get; private set; }
    public int BlockGain => EndOfTurnBlock.Sum(s => s.Amount);
    public int BlockableTotal => Blockable.Sum(s => s.Amount);
    public int UnblockableTotal => Unblockable.Sum(s => s.Amount);
    public int ExpectedBlock => CurrentBlock + BlockGain;
    public int DamageThrough => Math.Max(0, BlockableTotal - ExpectedBlock);
    public int Overblock => Math.Max(0, ExpectedBlock - BlockableTotal);

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
            if (power.GetType().Name == "PlatingPower" && power.Amount > 0)
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
                    if (me.Creature.Block <= 0) m.EndOfTurnBlock.Add(new Source(Title(relic), Sts2State.Var(relic, "Block")));
                    break;
                case "CloakClasp":
                    if (hand.Count > 0) m.EndOfTurnBlock.Add(new Source($"Cloak Clasp ×{hand.Count}", hand.Count * Sts2State.Var(relic, "Block")));
                    break;
                case "RippleBasin":
                    if (attacksThisTurn == 0) m.EndOfTurnBlock.Add(new Source(Title(relic), Sts2State.Var(relic, "Block")));
                    break;
                case "HornCleat":
                    if (Sts2State.TurnNumber == 1) m.NextTurnBlock.Add(new Source(Title(relic), Sts2State.Var(relic, "Block")));
                    break;
                case "CaptainsWheel":
                    if (Sts2State.TurnNumber == 2) m.NextTurnBlock.Add(new Source(Title(relic), Sts2State.Var(relic, "Block")));
                    break;
            }
        }

        // ---- Enemy attacks ----
        var targets = cs.Players.Select(p => p.Creature).ToList();
        foreach (var enemy in cs.Enemies)
        {
            if (!enemy.IsAlive || enemy.Monster == null || enemy.IsStunned) continue;
            var total = 0;
            var hits = 0;
            foreach (var intent in enemy.Monster.NextMove.Intents)
            {
                if (intent is AttackIntent attack)
                {
                    total += attack.GetTotalDamage(targets, enemy);
                    hits += Math.Max(1, attack.Repeats);
                }
            }
            if (total > 0)
            {
                var name = SafeTitle(enemy.Monster);
                m.Blockable.Add(new Source(hits > 1 ? $"{name} ({hits} hits)" : name, total));
            }
        }

        // ---- Your own end-of-turn damage ----
        var regret = 0;
        foreach (var card in hand)
        {
            var type = card.GetType().Name;
            if (BlockableHandCards.Contains(type))
            {
                m.Blockable.Add(new Source(SafeTitle(card), Sts2State.Var(card, "Damage")));
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
            if (BlockableDebuffs.Contains(type)) m.Blockable.Add(new Source(SafeTitle(power), power.Amount));
            else if (UnblockableDebuffs.Contains(type)) m.Unblockable.Add(new Source(SafeTitle(power), power.Amount));
        }

        return m;
    }

    private static string Title(RelicModel relic) => SafeTitle(relic);

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
