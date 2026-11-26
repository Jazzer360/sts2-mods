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
    /// <summary>What the panel last showed (the forecast, its ?, the board probe): shown again only when it changes.</summary>
    private (TurnEndModel? Forecast, string? Mark, int Probe) _shownFor;

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
        // Frame-driven. The predictions remember the cards placed on top of your draw pile (observe-only; patched by the first feature that applies, again if its apply was undone).
        DrawPileMemory.Patch(harmony);
    }

    public void Attach(OverlayHost host)
    {
        _widget = _ctx.Widget(Id, "Incoming Damage", DefaultLayout);
        // Still up while you pick a card from a few mid-fight (a discovery, a potion's choice): what you take matters to the turn.
        _widget.ShowsOverCardChoice = true;
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
        // The held card's turn while you hold one (Play Preview, decision #183), else ending it now.
        var forecast = Sts2State.IsPlayerTurn ? PlayPreview.Model(_ctx) : null;
        if (forecast == null || (!forecast.Unknown && forecast.ExtraTurnFrom == null && forecast.BlockableTotal == 0 && forecast.UnblockableTotal == 0))
        {
            _widget!.HasContent = false;
            _tip?.Hide();
            _shownFor = default;
            return;
        }

        _widget!.HasContent = true;
        // A preview (the held card's turn) is tinted, so it isn't taken for the turn as it stands.
        var tint = forecast.IsPreview ? PlayPreview.Tint : Godot.Colors.White;
        if (_widget.Content.Modulate != tint) _widget.Content.Modulate = tint;
        // The search stopped at its memory cap (decision #107): ? in place of the damage, and no promise either way.
        if (forecast.Unknown)
        {
            _panel!.ShowUnknown();
            _tip?.Hide();
            _shownFor = default;
            return;
        }
        // Another turn of yours comes before the enemies act, in every outcome (decision #319): only that shows, with no sum to hover.
        var extraTurn = forecast.ExtraTurnFrom;
        // Hovering the sum: every way the enemy turn can go (decision #155). Polled: the sum takes no mouse input.
        var root = _panel!.Root;
        if (extraTurn == null && root.IsVisibleInTree() && root.GetGlobalRect().HasPoint(root.GetGlobalMousePosition())) _tip!.Show(root, forecast, _ctx.Settings.Chances);
        else _tip!.Hide();
        // The same forecast, its ? and the board (the settings among it) as last frame: the panel shows it already.
        var shownFor = (forecast, forecast.ShownMark, Sts2State.Me is { } player && Sts2State.Combat is { } combat ? BoardProbe.Hash(player, combat) : 0);
        if (shownFor == _shownFor) return;
        _shownFor = shownFor;
        if (extraTurn != null)
        {
            _panel!.ShowExtraTurn(Sts2State.Me is { } me ? ExtraTurnIcon(me, extraTurn) : null, forecast.ShownMark);
            return;
        }
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
        // Some outcomes give you another turn first, not all: its icon above the sum.
        var someExtraTurn = forecast.ExtraTurnInSome is { } some && Sts2State.Me is { } you ? ExtraTurnIcon(you, some) : null;
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
            _panel!.Show(incoming, block, osty, through, dies, revive, someExtraTurn, chance, forecast.ShownMark, unblockableLikely,
                outcomeChance: likely.Chance.Text(), reviveChance: reviveChance);
            return;
        }
        int? unblockable = forecast.UnblockableTotal > 0 ? forecast.UnblockableTaken : null;
        _panel!.Show(forecast.BlockableTotal, forecast.ExpectedBlock,
            forecast.OstyHp > 0 ? (forecast.OstyTaken, forecast.OstyDies) : null, forecast.Taken - (unblockable ?? 0), dies, revive, someExtraTurn, chance, forecast.ShownMark, unblockable);
    }

    /// <summary>The icons of the power classes an extra turn came from, by name (a play's or a potion's power, not on the board yet).</summary>
    private static readonly Dictionary<string, Godot.Texture2D?> ExtraTurnPowerIcons = new(StringComparer.Ordinal);

    /// <summary>
    /// The icon of what gives <paramref name="player"/> an extra turn (decision #319: the simulator's source, its model's name): their relic
    /// (Pael's Eye) or power by that name, else the power class's own (Extra Turn from a held The Hanged Man, or a potion about to be drunk).
    /// Asked only when what's shown changes (yours) or for a hovered teammate: by index, no enumerator.
    /// </summary>
    internal static Godot.Texture2D? ExtraTurnIcon(MegaCrit.Sts2.Core.Entities.Players.Player player, string source)
    {
        try
        {
            var relics = player.Relics;
            for (var i = 0; i < relics.Count; i++)
                if (relics[i].GetType().Name == source) return Cards.GameIcons.Relic(relics[i]);
            var powers = player.Creature.Powers;
            for (var i = 0; i < powers.Count; i++)
                if (powers[i].GetType().Name == source) return Cards.GameIcons.Power(powers[i]);
            if (!ExtraTurnPowerIcons.TryGetValue(source, out var icon))
            {
                var type = typeof(MegaCrit.Sts2.Core.Models.PowerModel).Assembly.GetType("MegaCrit.Sts2.Core.Models.Powers." + source);
                ExtraTurnPowerIcons[source] = icon = type == null ? null : Cards.GameIcons.Power(type);
            }
            return icon;
        }
        catch
        {
            return null;
        }
    }
}
