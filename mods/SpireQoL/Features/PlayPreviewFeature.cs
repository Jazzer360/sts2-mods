using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.MonsterMoves.Intents;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using SpireQoL.Combat;
using SpireQoL.Sim;
using Sts2Mods.Core.Features;
using Sts2Mods.Core.Game;
using Sts2Mods.Core.Ui;

namespace SpireQoL.Features;

/// <summary>
/// Play Preview (decision #183): while you hold a card (aimed, for one that needs a target), the turn as it would go after playing it.
/// Incoming Damage, Teammate Incoming Damage and the Death Skull read it from PlayPreview; this draws the enemies' intents: an
/// attack the play changes gets a line through the game's damage number and the new one above it, in the same font (the game's
/// nodes aren't changed). An enemy the play kills in every outcome gets the line alone. Spec: docs/FEATURES.md#play-preview.
/// </summary>
public sealed class PlayPreviewFeature : IFeature, IFrameFeature
{
    private const string StrikeName = "SpireQoLPreviewStrike";
    private const string ValueName = "SpireQoLPreviewValue";

    private readonly QolContext _ctx;

    /// <summary>Each enemy's intents as last drawn (the preview shown, its intent nodes): drawn again only when that changes.</summary>
    private readonly Dictionary<NCreature, int> _drawnFor = new(ReferenceEqualityComparer.Instance);

    public PlayPreviewFeature(QolContext ctx) => _ctx = ctx;

    public string Id => PlayPreview.FeatureId;
    public string DisplayName => "Play Preview";
    public bool EnabledByDefault => true;

    public void Apply(Harmony harmony) { }

    public void Attach(OverlayHost host) { }

    public void OnFrame(OverlayHost host)
    {
        if (NCombatRoom.Instance is not { } room) return;
        var shown = PlayPreview.Current(_ctx);
        foreach (var node in Sts2State.CreatureNodesOf(room))
        {
            if (!GodotObject.IsInstanceValid(node) || node.IntentContainer is not { } container || !GodotObject.IsInstanceValid(container)) continue;
            IntentAfterPlay? after = null;
            if (shown != null && node.Entity is { } creature && shown.Ids.TryGetValue(creature, out var id)
                && shown.Outcome.IntentsAfterPlay.TryGetValue(id, out var intent) && intent.Changed)
                after = intent;
            var drawnFor = new HashCode();
            drawnFor.Add(after);
            for (var i = 0; i < container.GetChildCount(); i++) drawnFor.Add(container.GetChild(i).GetInstanceId());
            var key = drawnFor.ToHashCode();
            if (_drawnFor.TryGetValue(node, out var drawn) && drawn == key) continue;
            _drawnFor[node] = key;
            if (_drawnFor.Count > 64) _drawnFor.Clear();
            // The first attack intent carries the damage (an enemy's hits are one intent).
            var attackShown = false;
            for (var i = 0; i < container.GetChildCount(); i++)
            {
                if (container.GetChild(i) is not NIntent nIntent || !GodotObject.IsInstanceValid(nIntent)) continue;
                var isAttack = Sts2State.Private<AbstractIntent?>(nIntent, "_intent", fallback: null) is AttackIntent;
                Draw(nIntent, isAttack && !attackShown ? after : null);
                attackShown |= isAttack;
            }
        }
    }

    /// <summary>The line through the game's damage number and the new one above it, or neither.</summary>
    private static void Draw(NIntent intent, IntentAfterPlay? after)
    {
        var holder = intent.GetNodeOrNull<Control>(GodotNames.Path("%IntentHolder"));
        var value = intent.GetNodeOrNull<RichTextLabel>(GodotNames.Path("%Value"));
        if (holder == null) return;
        var strike = holder.GetNodeOrNull<ColorRect>(GodotNames.Path(StrikeName));
        var label = holder.GetNodeOrNull<Label>(GodotNames.Path(ValueName));
        if (after == null || value == null)
        {
            strike.RemoveAndFree();
            label.RemoveAndFree();
            return;
        }
        // The game's number: its text's width, where the label puts it (centred in its rect), and its font.
        var width = Math.Max(12f, value.GetContentWidth());
        var left = value.Position.X + (value.Size.X - width) / 2;
        var height = Math.Max(value.GetContentHeight(), 16f);
        var top = value.Position.Y + (value.Size.Y - height) / 2;
        if (strike == null)
        {
            strike = new ColorRect { Name = GodotNames.Name(StrikeName), Color = PlayPreview.Tint, MouseFilter = Control.MouseFilterEnum.Ignore };
            holder.AddChild(strike);
        }
        strike.Position = new Vector2(left - 2, top + height / 2 - 1.5f);
        strike.Size = new Vector2(width + 4, 3);
        if (after.DeadInAll)
        {
            label.RemoveAndFree();
            return;
        }
        if (label == null)
        {
            label = UiKit.Label("", size: value.GetThemeFontSize(GodotNames.Name("normal_font_size")), PlayPreview.Tint);
            label.Name = GodotNames.Name(ValueName);
            if (value.GetThemeFont(GodotNames.Name("normal_font")) is { } font) label.AddThemeFontOverride(GodotNames.Name("font"), font);
            label.AddThemeConstantOverride(GodotNames.Name("outline_size"), value.GetThemeConstant(GodotNames.Name("outline_size")));
            label.AddThemeColorOverride(GodotNames.Name("font_outline_color"), value.GetThemeColor(GodotNames.Name("font_outline_color")));
            label.HorizontalAlignment = HorizontalAlignment.Center;
            label.VerticalAlignment = VerticalAlignment.Bottom;
            holder.AddChild(label);
        }
        var damage = after.Least == after.Most ? after.Most.ToString() : $"{after.Least}-{after.Most}";
        var text = after.Hits > 1 ? $"{damage}x{after.Hits}" : damage;
        if (label.Text != text) label.Text = text;
        label.Position = new Vector2(value.Position.X, top - height);
        label.Size = new Vector2(value.Size.X, height);
    }
}
