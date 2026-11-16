using System.Runtime.CompilerServices;
using Godot;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Events;
using MegaCrit.Sts2.Core.Nodes.Events;

namespace SpireQoL.Features;

/// <summary>
/// The Inspect button (decision #64): the game's inspect cursor art right of an event option whose cards are drawn at random
/// from a known pool (a reward, a transform); it opens the Inspect screen on what the option draws from.
/// </summary>
public sealed partial class RunRemindersFeature
{
    private const string InspectNodeName = "SpireQoL_Inspect";
    private const float InspectSize = 40f;
    private SpireQoL.Menu.InspectScreen? _inspect;
    private static readonly ConditionalWeakTable<EventOption, StrongBox<List<DrawGroup>?>> GroupsCache = new();

    /// <summary>What an option draws from (cached per option), or null when its cards can't be read.</summary>
    private static List<DrawGroup>? GroupsOf(EventOption option, Player me)
    {
        if (GroupsCache.TryGetValue(option, out var box)) return box.Value;
        EffectsOf(option);
        List<DrawGroup>? groups = null;
        try { if (CardsRead.TryGetValue(option, out var read)) groups = read.Groups(me); }
        catch (Exception ex) { Sts2Mods.Core.Diagnostics.ErrorReporter.Instance?.Exception(ex, "reminders-inspect:" + option.TextKey); }
        GroupsCache.AddOrUpdate(option, new StrongBox<List<DrawGroup>?>(groups));
        return groups;
    }

    private void ShowInspect(NEventOptionButton button, Player? me)
    {
        var existing = button.GetNodeOrNull<TextureButton>(InspectNodeName);
        var option = me != null && button.Option is { IsLocked: false, IsProceed: false } o ? o : null;
        var groups = option != null && _ctx.Settings.EventInspect ? GroupsOf(option, me!) : null;
        if (groups == null || !groups.Any(g => g.IsRandom))
        {
            existing?.QueueFree();
            return;
        }
        if (existing == null)
        {
            existing = new TextureButton
            {
                Name = InspectNodeName,
                TextureNormal = InspectIcon(),
                IgnoreTextureSize = true,
                StretchMode = TextureButton.StretchModeEnum.KeepAspectCentered,
                Size = new Vector2(InspectSize, InspectSize),
                CustomMinimumSize = new Vector2(InspectSize, InspectSize),
                TooltipText = "Inspect: every card this option can give, and your chances",
                MouseDefaultCursorShape = Control.CursorShape.PointingHand,
                FocusMode = Control.FocusModeEnum.None,
            };
            existing.MouseEntered += () => existing.Modulate = new Color(1.3f, 1.3f, 1.3f);
            existing.MouseExited += () => existing.Modulate = Colors.White;
            existing.Pressed += () =>
            {
                if (RunMe() is not { } who || button.Option is not { } opt || GroupsOf(opt, who) is not { } g) return;
                _inspect?.Open(OptionTitle(opt), g, who);
            };
            button.AddChild(existing);
        }
        // Right of the option, vertically centered (the relic icons are on its left).
        existing.Position = new Vector2(button.Size.X + 10, (button.Size.Y - InspectSize) / 2);
    }

    private static string OptionTitle(EventOption option)
    {
        try { return System.Text.RegularExpressions.Regex.Replace(option.Title.GetFormattedText(), @"\[[^\]]*\]", ""); }
        catch { return option.TextKey; }
    }

    private static Texture2D? _inspectIcon;

    /// <summary>The game's inspect cursor (packed/common_ui/cursor_inspect.png): a magnifier.</summary>
    internal static Texture2D? InspectIcon()
    {
        if (_inspectIcon != null && GodotObject.IsInstanceValid(_inspectIcon)) return _inspectIcon;
        const string path = "res://images/packed/common_ui/cursor_inspect.png";
        try { _inspectIcon = ResourceLoader.Exists(path) ? ResourceLoader.Load<Texture2D>(path) : null; } catch { _inspectIcon = null; }
        return _inspectIcon;
    }
}
