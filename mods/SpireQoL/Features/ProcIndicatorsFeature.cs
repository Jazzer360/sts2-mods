using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Afflictions;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.ValueProps;
using SpireQoL.Cards;
using SpireQoL.Procs;
using Sts2Mods.Core.Features;
using Sts2Mods.Core.Game;
using Sts2Mods.Core.Ui;

namespace SpireQoL.Features;

/// <summary>
/// "Does something happen the moment I play this card?" One tab per power or relic that playing
/// the card would set off, with that source's icon. Spec: docs/FEATURES.md#proc-indicators.
///
///  - Trigger: plain icon ("this sets off that").
///  - Block / damage / summon: icon plus the amount (QolSettings.ProcNumbers).
///  - Harmful (helps the enemy or hurts you): crossed.
///
/// Every rule mirrors the source's own hook in the decompile (v0.111.0). Facts about the card
/// (play count, cost, exhausts, discards, draws, debuffs, created cards) come from PlayFacts.
/// Counter relics (Kunai, Shuriken...) only show on the card that completes the count; the tray
/// shows progress.
/// </summary>
public sealed class ProcIndicatorsFeature : IFeature, IFrameFeature
{
    private readonly QolContext _ctx;

    public ProcIndicatorsFeature(QolContext ctx) => _ctx = ctx;

    public string Id => "proc-indicators";
    public string DisplayName => "Proc Indicators";
    public bool EnabledByDefault => true;

    public void Apply(Harmony harmony)
    {
        // Frame-driven; no patches.
    }

    public void Attach(OverlayHost host)
    {
    }

    private enum Kind { Trigger, Block, Damage, Summon }

    private readonly record struct Hit(Kind Kind, int Amount, bool Harmful = false)
    {
        public static Hit Trig(bool harmful = false) => new(Kind.Trigger, 0, harmful);
    }

    private readonly HashSet<string> _warned = new();

    private static bool CanEverPlayNow(CardModel card)
    {
        try
        {
            if (card.CanPlay(out var reason, out _)) return true;
            const UnplayableReason never = UnplayableReason.HasUnplayableKeyword | UnplayableReason.BlockedByHook
                                           | UnplayableReason.BlockedByCardLogic | UnplayableReason.NoLivingAllies;
            return (reason & never) == 0;
        }
        catch
        {
            return true;
        }
    }

    // Per-frame context.
    private Player _me = null!;
    private ICombatState _cs = null!;
    private bool _indirect;

    public void OnFrame(OverlayHost host)
    {
        var me = Sts2State.Me;
        var cs = Sts2State.Combat;
        if (me == null || cs == null || !Sts2State.IsPlayerTurn) return;
        var hand = Sts2State.Hand(me);
        if (hand.Count == 0) return;

        _me = me;
        _cs = cs;
        _indirect = _ctx.Settings.ProcIndirect;

        // Sources: your powers, your relics, and powers on enemies (theirs, or ones you applied).
        var sources = new List<AbstractModel>();
        sources.AddRange(me.Creature.Powers);
        sources.AddRange(me.Relics.Where(r => _ctx.Settings.RelicEnabled(r.GetType().Name)));
        // All living enemies: an untargetable one's powers still fire.
        foreach (var enemy in cs.Enemies.Where(e => e.IsAlive)) sources.AddRange(enemy.Powers);

        foreach (var card in hand)
        {
            // Nothing happens on play for a card that can't be played (Unplayable statuses and
            // curses, blocked cards, cards whose own condition isn't met). Cards you just can't
            // afford yet still show, since energy can come mid-turn.
            if (!CanEverPlayNow(card)) continue;

            PlayFacts f;
            try
            {
                f = PlayFacts.For(card, me, cs, CardIndicatorsFeature.PlayCount(card));
            }
            catch (Exception ex)
            {
                if (_warned.Add(card.GetType().Name)) ModEntry.Log.Warn($"proc facts failed for {card.GetType().Name}: {ex.Message}");
                continue;
            }

            var d = _ctx.Decor.For(card);
            // Several enemies can carry the same power: one tab, amounts summed.
            var merged = new Dictionary<string, (AbstractModel Src, Hit Hit)>();
            foreach (var src in sources)
            {
                Hit? hit;
                try { hit = Evaluate(src, f); }
                catch { hit = null; }
                if (hit is not { } h) continue;
                if (h.Kind != Kind.Trigger && h.Amount <= 0) continue;
                var key = "proc:" + src.GetType().Name;
                merged[key] = merged.TryGetValue(key, out var prev)
                    ? (prev.Src, prev.Hit with { Amount = prev.Hit.Amount + h.Amount })
                    : (src, h);
            }

            foreach (var (key, (src, h)) in merged)
            {
                var icon = src switch
                {
                    RelicModel r => GameIcons.Relic(r),
                    PowerModel p => GameIcons.Power(p),
                    _ => null,
                };
                int? number = h.Kind != Kind.Trigger && _ctx.Settings.ProcNumbers ? h.Amount : null;
                d.AddTab(key, icon, crossed: h.Harmful, number: number);
            }
        }
    }

    // ------------------------------------------------------------------ rules

    private Hit? Evaluate(AbstractModel src, PlayFacts f)
    {
        var card = f.Card;
        var type = card.Type;
        var plays = f.Plays;
        var exhausts = (f.ExhaustsSelf ? 1 : 0) + (_indirect ? f.ExhaustsOthers : 0);
        var discards = _indirect ? f.Discards : 0;
        var draws = _indirect ? f.Draws : 0;
        var shuffles = _indirect && f.Shuffles;
        var generated = _indirect ? f.Generated : 0;
        var statuses = _indirect ? f.GeneratedStatuses : 0;

        switch (src)
        {
            case PowerModel p:
            {
                var a = p.Amount;
                var mine = p.Owner == _me.Creature;
                var onEnemy = p.Owner.IsEnemy;
                switch (p.GetType().Name)
                {
                    // ---- your powers: on play
                    case "AfterimagePower" when mine: return Block(a * plays);
                    case "RagePower" when mine && type == CardType.Attack: return Block(a * plays);
                    case "SpiritOfAshPower" when mine && card.Keywords.Contains(CardKeyword.Ethereal): return Block(a * plays);
                    case "DanseMacabrePower" when mine && f.Energy >= Sts2State.Var(p, "Energy", 2): return Block(a * plays);
                    case "GravityPower" when mine: return Damage(a * plays);
                    case "SerpentFormPower" when mine: return Damage(a * plays);
                    case "PanachePower" when mine && plays >= Sts2State.Var(p, "CardsLeft", 5): return Damage(a);
                    case "BlackHolePower" when mine && f.Stars > 0: return Damage(a);
                    case "DevourLifePower" when mine && card is Soul: return new Hit(Kind.Summon, a * plays);
                    case "HauntPower" when mine && card is Soul: return new Hit(Kind.Damage, a * plays);
                    case "StormPower" when mine && type == CardType.Power: return Hit.Trig();
                    case "SubroutinePower" when mine && type == CardType.Power: return Hit.Trig();
                    case "CalamityPower" when mine && type == CardType.Attack: return Hit.Trig();
                    case "JugglingPower" when mine && type == CardType.Attack:
                    {
                        var attacks = Sts2State.CountPlayedThisTurn(_me, CardType.Attack);
                        return attacks < 3 && attacks + plays >= 3 ? Hit.Trig() : null;
                    }
                    case "MonologuePower" when mine: return Hit.Trig();
                    case "MasterPlannerPower" when mine && type == CardType.Skill: return Hit.Trig();
                    case "TheSealedThronePower" when mine: return Hit.Trig();
                    case "TenderPower" when mine: return Hit.Trig(harmful: true);
                    case "SmoggyPower" when mine && type == CardType.Skill: return Hit.Trig(harmful: true);
                    case "PaleBlueDotPower" when mine:
                    {
                        if (Sts2State.Private(p, "alreadyActivatedThisTurn", false)) return null;
                        var played = Sts2State.CountPlayedThisTurn(_me);
                        return played < 5 && played + plays >= 5 ? Hit.Trig() : null;
                    }

                    // ---- your powers: exhaust / discard / draw / stars / energy / debuffs / created cards
                    case "FeelNoPainPower" when mine && exhausts > 0: return Block(a * exhausts);
                    case "DarkEmbracePower" when mine && exhausts > 0: return Hit.Trig();
                    case "ChildOfTheStarsPower" when mine && f.Stars > 0: return Block(a * f.Stars);
                    case "OrbitPower" when mine && f.Energy > 0 && f.Energy >= p.DisplayAmount: return Hit.Trig();
                    case "AutomationPower" when mine && draws > 0 && draws >= p.DisplayAmount: return Hit.Trig();
                    case "CacophonyPower" when mine && draws > 0 && draws >= p.DisplayAmount: return Damage(a);
                    case "SpeedsterPower" when mine && draws > 0: return Damage(a * draws);
                    case "CorrosiveWavePower" when mine && draws > 0: return Hit.Trig();
                    case "StratagemPower" when mine && shuffles: return Hit.Trig();
                    case "SleightOfFleshPower" when mine: return Damage(a * f.DebuffsLandingTotal());
                    case "ViciousPower" when mine && f.DebuffsLandingTotal(t => t == typeof(VulnerablePower)) > 0: return Hit.Trig();
                    case "ShroudPower" when mine: return Block(a * f.DebuffsLandingTotal(t => t == typeof(DoomPower)));
                    case "JuggernautPower" when mine && f.BlockGains > 0: return Damage(a * f.BlockGains);
                    case "EnvenomPower" when mine && type == CardType.Attack: return Hit.Trig();
                    case "ConcoctPower" when mine && type == CardType.Attack: return Hit.Trig();
                    case "ReaperFormPower" when mine && type == CardType.Attack: return Hit.Trig();
                    case "MonarchsGazePower" when mine && type == CardType.Attack: return Hit.Trig();
                    case "ArsenalPower" when mine && generated > 0: return Hit.Trig();
                    case "PillarOfCreationPower" when mine && generated > 0: return Block(a * generated);
                    case "SmokestackPower" when mine && statuses > 0: return Damage(a * statuses);
                    case "TrashToTreasurePower" when mine && statuses > 0: return Hit.Trig();

                    // ---- powers on enemies
                    case "EnragePower" when onEnemy && type == CardType.Skill: return Hit.Trig(harmful: true);
                    case "SneakyPower" when onEnemy && type == CardType.Attack: return Hit.Trig(harmful: true);
                    case "ImitationLearningPower" when onEnemy && type == CardType.Power: return Hit.Trig(harmful: true);
                    case "SkittishPower" when onEnemy && type == CardType.Attack && !Sts2State.Private(p, "HasGainedBlockThisTurn", true):
                        return Hit.Trig(harmful: true);
                    case "WitheringPresencePower" when onEnemy && plays >= p.DisplayAmount: return Hit.Trig(harmful: true);
                    case "GalvanicPower" when card.Affliction is Galvanized: return new Hit(Kind.Damage, a * plays, Harmful: true);
                    case "VitalSparkPower" when card.Affliction is Tainted: return Hit.Trig(harmful: true);
                    case "StranglePower" when onEnemy && p.Applier?.Player == _me: return new Hit(Kind.Damage, a * plays);
                    case "OblivionPower" when onEnemy && p.Applier?.Player == _me: return Hit.Trig();
                    case "SlowPower" when onEnemy: return Hit.Trig();
                }
                return null;
            }

            case RelicModel r:
            {
                var name = r.GetType().Name;
                int N(string key, int fallback) => Sts2State.Var(r, key, fallback);
                // Counter relics fire on the card that completes the count (the tray shows progress).
                // DisplayAmount is the count mod N, except it reads N during the relic's activation
                // animation (right after it fired), hence the % N.
                bool Completes(bool matches)
                {
                    var n = N("Cards", 0);
                    return matches && n > 0 && r.DisplayAmount % n + plays >= n;
                }
                switch (name)
                {
                    // ---- on play
                    case "IntimidatingHelmet" when f.Energy >= N("Energy", 2): return RelicBlock(r, plays);
                    case "DaughterOfTheWind" when type == CardType.Attack: return RelicBlock(r, plays);
                    case "Permafrost" when type == CardType.Power && RelicCountersFeature.IsArmed(r, _me): return RelicBlock(r, 1);
                    case "LostWisp" when type == CardType.Power: return RelicDamage(r, plays);
                    case "GamePiece" when type == CardType.Power: return Hit.Trig();
                    case "MummifiedHand" when type == CardType.Power: return Hit.Trig();
                    case "HelicalDart" when card.Tags.Contains(CardTag.Shiv): return Hit.Trig();
                    case "IvoryTile" when f.Energy >= N("EnergyThreshold", 3): return Hit.Trig();
                    case "ChemicalX" when card.EnergyCost.CostsX || card.HasStarCostX: return Hit.Trig();
                    case "RazorTooth" when (type == CardType.Attack || type == CardType.Skill) && card.IsUpgradable: return Hit.Trig();
                    case "RainbowRing":
                    {
                        if (Sts2State.Private(r, "ActivationCountThisTurn", 0) >= 1) return null;
                        var played = Sts2State.CardsPlayedThisTurn(_me).Select(c => c.Type).Append(type).ToHashSet();
                        var before = Sts2State.CardsPlayedThisTurn(_me).Select(c => c.Type).ToHashSet();
                        bool All(HashSet<CardType> s) => s.Contains(CardType.Attack) && s.Contains(CardType.Skill) && s.Contains(CardType.Power);
                        return !All(before) && All(played) ? Hit.Trig() : null;
                    }

                    // ---- counters completed by this card
                    case "Kunai" or "Shuriken" or "Nunchaku" when Completes(type == CardType.Attack): return Hit.Trig();
                    case "OrnamentalFan" when Completes(type == CardType.Attack): return RelicBlock(r, 1);
                    case "Kusarigama" when Completes(type == CardType.Attack): return RelicDamage(r, 1);
                    case "LetterOpener" when Completes(type == CardType.Skill): return RelicDamage(r, 1);
                    case "TuningFork" when Completes(type == CardType.Skill): return RelicBlock(r, 1);
                    case "IronClub" when Completes(true): return Hit.Trig();

                    // ---- exhaust / discard / stars / created cards / shuffle / empty hand
                    case "CharonsAshes" when exhausts > 0: return RelicDamage(r, exhausts);
                    case "ForgottenSoul" when exhausts > 0: return RelicDamage(r, exhausts);
                    case "JossPaper" when exhausts > 0 && Sts2State.Private(r, "CardsExhausted", 0) + exhausts >= N("ExhaustAmount", 5): return Hit.Trig();
                    case "Tingsha" when discards > 0: return RelicDamage(r, discards);
                    case "ToughBandages" when discards > 0: return RelicBlock(r, discards);
                    case "MiniRegent" when f.Stars > 0 && !Sts2State.Private(r, "UsedThisTurn", true): return Hit.Trig();
                    case "GalacticDust" when f.Stars > 0:
                    {
                        var per = N("Stars", 10);
                        var times = per > 0 ? (Sts2State.Private(r, "StarsSpent", 0) + f.Stars) / per : 0;
                        return times > 0 ? RelicBlock(r, times) : null;
                    }
                    case "Regalite" when generated > 0 && !Sts2State.Private(r, "UsedThisTurn", true): return RelicBlock(r, 1);
                    case "TheAbacus" when shuffles: return RelicBlock(r, 1);
                    case "UnceasingTop" when Sts2State.Hand(_me).Count == 1 && draws == 0 && generated == 0: return Hit.Trig();
                }
                return null;
            }
        }
        return null;
    }

    // ------------------------------------------------------------------ amounts

    /// <summary>Block you'd actually gain: the game's own block math (Dexterity/Frail only apply to powered block).</summary>
    private Hit Block(int raw, ValueProp props = ValueProp.Unpowered)
    {
        if (raw <= 0) return new Hit(Kind.Block, 0);
        try
        {
            var v = Hook.ModifyBlock(_cs, _me.Creature, raw, props, null, null, out _);
            return new Hit(Kind.Block, Math.Max(0, (int)v));
        }
        catch
        {
            return new Hit(Kind.Block, raw);
        }
    }

    /// <summary>Damage per enemy hit, dealer side only (before each enemy's own modifiers such as Vulnerable).</summary>
    private Hit Damage(int raw, ValueProp props = ValueProp.Unpowered)
    {
        if (raw <= 0) return new Hit(Kind.Damage, 0);
        try
        {
            var v = Hook.ModifyDamage(_me.RunState, _cs, null, _me.Creature, raw, props, null, null, ModifyDamageHookType.All, CardPreviewMode.None, out _);
            return new Hit(Kind.Damage, Math.Max(0, (int)v));
        }
        catch
        {
            return new Hit(Kind.Damage, raw);
        }
    }

    private Hit RelicBlock(RelicModel r, int times)
    {
        var props = r.DynamicVars.TryGetValue("Block", out var v) && v is BlockVar bv ? bv.Props : ValueProp.Unpowered;
        var one = Block(Sts2State.Var(r, "Block"), props).Amount;
        return new Hit(Kind.Block, one * Math.Max(0, times));
    }

    private Hit RelicDamage(RelicModel r, int times)
    {
        var props = r.DynamicVars.TryGetValue("Damage", out var v) && v is DamageVar dv ? dv.Props : ValueProp.Unpowered;
        var one = Damage(Sts2State.Var(r, "Damage"), props).Amount;
        return new Hit(Kind.Damage, one * Math.Max(0, times));
    }
}
