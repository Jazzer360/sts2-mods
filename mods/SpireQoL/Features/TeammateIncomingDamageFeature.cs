using HarmonyLib;
using SpireQoL.Combat;
using Sts2Mods.Core.Features;
using Sts2Mods.Core.Game;
using Sts2Mods.Core.Ui;

namespace SpireQoL.Features;

/// <summary>
/// Multiplayer: hovering a teammate shows their Incoming Damage beside their HP bar (the game shows
/// it while you hover them), the same math as yours (DamageMathPanel). From what you can see
/// (decision #25): their block, powers, orbs, relics and the enemies' intents, not the cards in their
/// hand (TurnEndModel.TeammateView). Their Osty's row shows while he's out, as yours does.
/// Checked by the forecast audit against the HP they lose. Spec: docs/FEATURES.md#incoming-damage.
/// </summary>
public sealed class TeammateIncomingDamageFeature : IFeature, IFrameFeature
{
    private readonly QolContext _ctx;
    private MovableWidget? _widget;
    private DamageMathPanel? _panel;

    public TeammateIncomingDamageFeature(QolContext ctx) => _ctx = ctx;

    public string Id => "teammate-incoming-damage";
    public string DisplayName => "Teammate Incoming Damage";
    public bool EnabledByDefault => true;

    public static readonly WidgetLayout DefaultLayout = new()
    {
        FollowGame = true, OffsetX = 0, OffsetY = 0,
        Anchor = ScreenAnchor.Left,
    };

    public void Apply(Harmony harmony)
    {
        // Frame-driven; no patches.
    }

    public void Attach(OverlayHost host)
    {
        _widget = _ctx.Widget(Id, "Teammate Incoming Damage", DefaultLayout);
        _widget.AttachTo(Sts2State.FocusedTeammateBlockRect, AttachSide.LeftOf);
        _panel = new DamageMathPanel(_widget.Content);
    }

    public void OnFrame(OverlayHost host)
    {
        var mate = Sts2State.IsPlayerTurn ? Sts2State.FocusedTeammate() : null;
        var view = mate != null ? TurnEndModel.Compute()?.TeammateView.FirstOrDefault(v => v.Player == mate) : null;
        if (view == null || view.Incoming == 0)
        {
            _widget!.HasContent = false;
            return;
        }
        _widget!.HasContent = true;
        _panel!.Show(view.Incoming, view.Block, view.OstyHp > 0 ? (view.OstyTaken, view.OstyDies) : null, view.TakenMax);
    }
}
