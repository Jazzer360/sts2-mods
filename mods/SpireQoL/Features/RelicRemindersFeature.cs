using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.Entities.Relics;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using SpireQoL.Cards;
using Sts2Mods.Core.Features;
using Sts2Mods.Core.Game;
using Sts2Mods.Core.Ui;

namespace SpireQoL.Features;

/// <summary>
/// The relic's icon as an on-card tab on the cards in hand it cares about (the same tab language
/// as every other on-card indicator). Spec: docs/FEATURES.md#relic-reminders.
///  - Crossed out: playing this card stops the relic working this turn (Art of War, Ripple Basin,
///    Pael's Tears).
///  - Plain: playing this card uses the relic (Pen Nib when ready; one-shot relics while still
///    armed: Vambrace, Burning Sticks, Unsettling Lamp, Music Box; Throwing Axe is a double-play tab, Permafrost a proc tab).
/// </summary>
public sealed class RelicRemindersFeature : IFeature, IFrameFeature
{
    private readonly QolContext _ctx;
    private readonly Dictionary<Type, bool> _debuffVarCache = new();

    public RelicRemindersFeature(QolContext ctx) => _ctx = ctx;

    public string Id => "relic-reminders";
    public string DisplayName => "Relic Reminders";
    public bool EnabledByDefault => true;

    public void Apply(Harmony harmony)
    {
        // Frame-driven; no patches.
    }

    public void Attach(OverlayHost host)
    {
    }

    public void OnFrame(OverlayHost host)
    {
        var me = Sts2State.Me;
        if (me == null || !Sts2State.IsPlayerTurn) return;
        var hand = Sts2State.Hand(me);
        if (hand.Count == 0) return;

        var attacksPlayed = Sts2State.CountPlayedThisTurn(me, CardType.Attack);

        foreach (var relic in me.Relics)
        {
            var name = relic.GetType().Name;
            if (!_ctx.Settings.RelicEnabled(name)) continue;

            switch (name)
            {
                case "PenNib":
                    if (relic.Status == RelicStatus.Active)
                    {
                        foreach (var c in hand.Where(c => c.Type == CardType.Attack)) Mark(c, relic, crossed: false);
                    }
                    break;

                case "PaelsTears":
                {
                    // Ending the turn with energy left gives energy next turn. Cross out cards that
                    // would spend your last energy (X-cost cards spend it all).
                    var energy = me.PlayerCombatState?.Energy ?? 0;
                    if (energy > 0)
                    {
                        foreach (var c in hand)
                        {
                            if (c.EnergyCost.GetAmountToSpend() >= energy && c.CanPlay(out _, out _)) Mark(c, relic, crossed: true);
                        }
                    }
                    break;
                }

                case "ArtOfWar":
                case "RippleBasin":
                    if (attacksPlayed == 0)
                    {
                        foreach (var c in hand.Where(c => c.Type == CardType.Attack)) Mark(c, relic, crossed: true);
                    }
                    break;

                case "Vambrace":
                    if (RelicCountersFeature.IsArmed(relic, me))
                        foreach (var c in hand.Where(GrantsBlock)) Mark(c, relic, false);
                    break;

                case "BurningSticks":
                    if (RelicCountersFeature.IsArmed(relic, me))
                        foreach (var c in hand.Where(WillExhaustSkill)) Mark(c, relic, false);
                    break;

                case "UnsettlingLamp":
                    if (RelicCountersFeature.IsArmed(relic, me))
                        foreach (var c in hand.Where(AppliesDebuff)) Mark(c, relic, false);
                    break;

                case "MusicBox":
                    if (RelicCountersFeature.IsArmed(relic, me))
                        foreach (var c in hand.Where(c => c.Type == CardType.Attack)) Mark(c, relic, false);
                    break;
            }
        }
    }

    private void Mark(CardModel card, RelicModel relic, bool crossed)
        => _ctx.Decor.For(card).AddTab("relic:" + relic.GetType().Name, GameIcons.Relic(relic), crossed: crossed);

    private static bool GrantsBlock(CardModel c) => c.DynamicVars.ContainsKey("Block") || c.DynamicVars.ContainsKey("CalculatedBlock");

    /// <summary>Skills that exhaust when played, plus Ethereal skills (they exhaust at end of turn).</summary>
    private static bool WillExhaustSkill(CardModel c)
    {
        if (c.Type != CardType.Skill) return false;
        var kw = c.Keywords;
        return kw.Contains(CardKeyword.Exhaust) || kw.Contains(CardKeyword.Ethereal)
               || c.Owner.Creature.Powers.Any(p => p is MegaCrit.Sts2.Core.Models.Powers.CorruptionPower);
    }

    /// <summary>Cards carrying a PowerVar&lt;T&gt; for a debuff power (Weak, Vulnerable, Poison...).</summary>
    private bool AppliesDebuff(CardModel c)
    {
        foreach (var v in c.DynamicVars.Values)
        {
            var t = v.GetType();
            if (!t.IsGenericType || t.GetGenericTypeDefinition() != typeof(PowerVar<>)) continue;
            var powerType = t.GetGenericArguments()[0];
            if (!_debuffVarCache.TryGetValue(powerType, out var isDebuff))
            {
                isDebuff = false;
                try
                {
                    var canonical = typeof(ModelDb).GetMethod(nameof(ModelDb.Power))!.MakeGenericMethod(powerType).Invoke(null, null) as PowerModel;
                    isDebuff = canonical?.Type == PowerType.Debuff;
                }
                catch
                {
                    // unknown power type: treat as not a debuff
                }
                _debuffVarCache[powerType] = isDebuff;
            }
            if (isDebuff) return true;
        }
        return false;
    }
}
