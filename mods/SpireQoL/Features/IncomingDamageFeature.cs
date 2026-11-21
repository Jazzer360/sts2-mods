using MegaCrit.Sts2.Core.Models.Relics;
using HarmonyLib;
using SpireQoL.Combat;
using Sts2Mods.Core.Features;
using Sts2Mods.Core.Game;
using Sts2Mods.Core.Ui;

namespace SpireQoL.Features;

/// <summary>
/// A small stacked "math problem" just left of the player's block indicator (DamageMathPanel).
/// Incoming is damage that will actually land: enemies Poison kills before acting are left out,
/// and hits after retaliation (Thorns etc.) kills an attacker are cancelled (TurnEndModel).
/// Block is your block at end of turn. The result is what gets through it; HP loss no block stops (Regret, Beckon, Bad Luck)
/// is a line of its own under it, so it never reads as block you're short of. Spec: docs/FEATURES.md#incoming-damage.
/// </summary>
public sealed class IncomingDamageFeature : IFeature, IFrameFeature
{
    private readonly QolContext _ctx;
    private MovableWidget? _widget;
    private DamageMathPanel? _panel;
    private OutcomeTip? _tip;

    public IncomingDamageFeature(QolContext ctx) => _ctx = ctx;

    public string Id => "incoming-damage";
    public string DisplayName => "Incoming Damage";
    public bool EnabledByDefault => true;

    public static readonly WidgetLayout DefaultLayout = new()
    {
        FollowGame = true, OffsetX = 0, OffsetY = 0,
        Anchor = ScreenAnchor.Left, // fallback when the health bar isn't on screen
    };

    public void Apply(Harmony harmony)
    {
        // Frame-driven. The predictions remember the cards placed on top of your draw pile (observe-only; patched once).
        DrawPileMemory.Patch(harmony);
    }

    public void Attach(OverlayHost host)
    {
        _widget = _ctx.Widget(Id, "Incoming Damage", DefaultLayout);
        // Left of the game's block indicator (it's about block). When the character stands too
        // close to the screen edge for that, fall back to below the HP bar and every row of
        // power icons (they can wrap to several rows).
        _widget.AttachTo(Sts2State.PlayerBlockRect, AttachSide.LeftOf)
               .AttachTo(Sts2State.PlayerStatusRect, AttachSide.Below);
        _panel = new DamageMathPanel(_widget.Content);
        _tip = new OutcomeTip(_widget.Content);
    }

    public void OnFrame(OverlayHost host)
    {
        var forecast = Sts2State.IsPlayerTurn ? TurnEndModel.Shown() : null;
        if (forecast == null || (!forecast.Unknown && forecast.BlockableTotal == 0 && forecast.UnblockableTotal == 0))
        {
            _widget!.HasContent = false;
            _tip?.Hide();
            return;
        }

        _widget!.HasContent = true;
        // The search stopped at its memory cap (decision #107): ? in place of the damage, and no promise either way.
        if (forecast.Unknown)
        {
            _panel!.ShowUnknown();
            _tip?.Hide();
            return;
        }
        // Hovering the sum: every way the enemy turn can go (decision #155). Polled: the sum takes no mouse input.
        var root = _panel!.Root;
        if (root.IsVisibleInTree() && root.GetGlobalRect().HasPoint(root.GetGlobalMousePosition())) _tip!.Show(root, forecast, _ctx.Settings.Chances);
        else _tip!.Hide();
        // The most likely way the enemy turn goes, when a roll decides between several (decision #155); else the worst, as before.
        var likely = _ctx.Settings.ShowMostLikely && forecast.Outcomes.Count > 1 ? forecast.MostLikely : null;
        // A lethal enemy turn: the skull. One something keeps you alive through (Fairy in a Bottle first, then
        // Lizard Tail, the order the game asks them): its icon and your HP after. With the most likely outcome shown, the line shows
        // whenever some outcome uses it, with its chance (using it up costs you the potion or the relic's charge).
        (Godot.Texture2D? Icon, int HpAfter)? revive = null;
        string? reviveChance = null;
        var revived = likely == null ? null : forecast.Outcomes.FirstOrDefault(outcome => outcome.Revived);
        var fairies = likely == null ? forecast.FairiesUsed : revived?.FairiesUsed ?? 0;
        var tailUsed = likely == null ? forecast.LizardTailUsed : revived?.LizardTailUsed ?? false;
        if (fairies > 0 || tailUsed)
        {
            var me = Sts2State.Me;
            var icon = fairies > 0
                ? me?.Potions.FirstOrDefault(potion => potion.GetType().Name == "FairyInABottle") is { } fairy ? Cards.GameIcons.Potion(fairy) : null
                : me?.Relics.FirstOrDefault(relic => relic is LizardTail) is { } tail ? Cards.GameIcons.Relic(tail) : null;
            revive = (icon, revived?.HpAfter ?? forecast.HpAfter);
            if (revived != null && forecast.ReviveChance is { Certain: false } saved) reviveChance = saved.Text();
        }
        // The skull whenever an outcome might kill you (decision #87), with the chance unless it's certain or chances are off.
        var death = forecast.DeathChance;
        var dies = death.Possible || forecast.Dies;
        var chance = dies && !death.Certain && _ctx.Settings.ShowDeathChance ? death.Text() : null;
        // The skull and the chance come from the whole outcome (both lines): the split is only how it's shown.
        if (likely != null)
        {
            var (incoming, block, osty, through, unblockableLikely) = forecast.Sum(likely);
            _panel!.Show(incoming, block, osty, through, dies, revive, ExtraTurnIcon(), chance, forecast.ShownMark, unblockableLikely,
                outcomeChance: likely.Chance.Text(), reviveChance: reviveChance);
            return;
        }
        int? unblockable = forecast.UnblockableTotal > 0 ? forecast.UnblockableTaken : null;
        _panel!.Show(forecast.BlockableTotal, forecast.ExpectedBlock,
            forecast.OstyHp > 0 ? (forecast.OstyTaken, forecast.OstyDies) : null, forecast.Taken - (unblockable ?? 0), dies, revive, ExtraTurnIcon(), chance, forecast.ShownMark, unblockable);
    }

    /// <summary>
    /// Ending your turn now gives you another turn before the enemies act (Hook.ShouldTakeExtraTurn, a pure query:
    /// Pael's Eye when you've played nothing, Extra Turn): its source's icon. The forecast is still the enemies' next
    /// turn, which comes after that one (your end-of-turn block will be gone by then).
    /// </summary>
    private static Godot.Texture2D? ExtraTurnIcon()
    {
        try
        {
            if (Sts2State.Me is not { } me || Sts2State.Combat is not { } combat) return null;
            if (!MegaCrit.Sts2.Core.Hooks.Hook.ShouldTakeExtraTurn(combat, me, out var source) || source == null) return null;
            return source switch
            {
                MegaCrit.Sts2.Core.Models.RelicModel relic => Cards.GameIcons.Relic(relic),
                MegaCrit.Sts2.Core.Models.PowerModel power => Cards.GameIcons.Power(power),
                _ => null,
            };
        }
        catch
        {
            return null;
        }
    }
}
