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
/// Block is your block at end of turn. The result adds unblockable HP loss (Regret, Demise...),
/// since that's what you actually lose. Spec: docs/FEATURES.md#incoming-damage.
/// </summary>
public sealed class IncomingDamageFeature : IFeature, IFrameFeature
{
    private readonly QolContext _ctx;
    private MovableWidget? _widget;
    private DamageMathPanel? _panel;

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
        // Frame-driven; no patches.
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
    }

    public void OnFrame(OverlayHost host)
    {
        var m = Sts2State.IsPlayerTurn ? TurnEndModel.Compute() : null;
        if (m == null || (m.BlockableTotal == 0 && m.UnblockableTotal == 0))
        {
            _widget!.HasContent = false;
            return;
        }

        _widget!.HasContent = true;
        // A lethal enemy turn: the skull. One something keeps you alive through (Fairy in a Bottle first, then
        // Lizard Tail, the order the game asks them): its icon and your HP after.
        (Godot.Texture2D? Icon, int HpAfter)? revive = null;
        if (m.FairiesUsed > 0 || m.LizardTailUsed)
        {
            var me = Sts2State.Me;
            var icon = m.FairiesUsed > 0
                ? me?.Potions.FirstOrDefault(x => x.GetType().Name == "FairyInABottle") is { } fairy ? Cards.GameIcons.Potion(fairy) : null
                : me?.Relics.FirstOrDefault(x => x.GetType().Name == "LizardTail") is { } tail ? Cards.GameIcons.Relic(tail) : null;
            revive = (icon, m.HpAfter);
        }
        _panel!.Show(m.BlockableTotal + m.UnblockableTotal, m.ExpectedBlock,
            m.OstyHp > 0 ? (m.OstyTaken, m.OstyDies) : null, m.Taken, m.Dies, revive);
    }
}
