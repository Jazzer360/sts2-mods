using Godot;
using MegaCrit.Sts2.Core.MonsterMoves.Intents;
using MegaCrit.Sts2.Core.Nodes.Combat;
using SpireQoL.Cards;
using SpireQoL.Sim;
using Sts2Mods.Core.Ui;

namespace SpireQoL.Features;

/// <summary>
/// The powers a move applies, as chips in the top-left corner of the intent they belong to (a buff on its buff intent, a debuff on its debuff
/// intent: decision #409), away from the number the game prints at the intent's foot. Read as part of that intent, not as another one.
/// Shared by Intent Details (the intent on screen) and Intent Forecast (later moves). Our node, under the intent's %IntentHolder,
/// so it bobs with the icon; the game's nodes aren't changed.
/// </summary>
internal static class IntentCorner
{
    private const string PowersName = "SpireQoLIntentPowers";
    private const int IconSize = 20;
    private const int FontSize = 15;
    private const int Outline = 5;
    private static bool _layoutLogged;

    /// <summary>Each power with the index of the intent it goes on: the move's buff intent for a buff, its debuff intent for a debuff, else its last.</summary>
    public static Dictionary<int, List<MovePowers.Applied>> Assign(IReadOnlyList<AbstractIntent> intents, IReadOnlyList<MovePowers.Applied> powers)
    {
        var at = new Dictionary<int, List<MovePowers.Applied>>();
        if (intents.Count == 0) return at;
        var buff = Index(intents, abstractIntent => abstractIntent is BuffIntent);
        var debuff = Index(intents, abstractIntent => abstractIntent is DebuffIntent);
        foreach (var applied in powers)
        {
            var corner = SimReaders.IsDebuffType(applied.Power) ? debuff ?? buff : buff ?? debuff;
            corner ??= intents.Count - 1;
            if (!at.TryGetValue(corner.Value, out var list)) at[corner.Value] = list = new List<MovePowers.Applied>();
            list.Add(applied);
        }
        return at;
    }

    private static int? Index(IReadOnlyList<AbstractIntent> intents, Func<AbstractIntent, bool> match)
    {
        for (var i = 0; i < intents.Count; i++) if (match(intents[i])) return i;
        return null;
    }

    /// <summary>The list each chip row was last built from or held to (Place): never changed in place by its callers.</summary>
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<GodotObject, IReadOnlyList<MovePowers.Applied>> BuiltFrom = new();

    /// <summary>What a row of chips shows, as text: each power and its amount.</summary>
    private static string Key(IReadOnlyList<MovePowers.Applied> powers) => string.Join(",", powers.Select(applied => $"{applied.Power.Name}:{applied.Amount}"));

    /// <summary>Puts these chips in the intent's corner (none: removes them). Rebuilt only when what they show changes.</summary>
    public static void Place(NIntent intent, IReadOnlyList<MovePowers.Applied>? powers)
    {
        if (intent.GetNodeOrNull<Control>(GodotNames.Path("%IntentHolder")) is not { } holder) return;
        var row = holder.GetNodeOrNull<HBoxContainer>(GodotNames.Path(PowersName));
        if (powers is not { Count: > 0 })
        {
            row.RemoveAndFree();
            return;
        }
        // The same list the row was built from: nothing to compare (its key, built as text, made garbage on every frame the forecast showed a
        // move that applies powers: a dev review).
        if (row != null && !(BuiltFrom.TryGetValue(row, out var from) && ReferenceEquals(from, powers)))
        {
            if (UiKit.KeyOf(row) != Key(powers))
            {
                row.QueueFree();
                row = null;
            }
            else BuiltFrom.AddOrUpdate(row, powers);
        }
        if (row == null)
        {
            row = UiKit.Row(separation: 2);
            row.Name = GodotNames.Name(PowersName);
            row.MouseFilter = Control.MouseFilterEnum.Ignore;
            UiKit.SetKey(row, Key(powers));
            BuiltFrom.AddOrUpdate(row, powers);
            foreach (var applied in powers) row.AddChild(Chip(applied));
            holder.AddChild(row);
        }
        var size = row.GetCombinedMinimumSize();
        row.Size = size;
        // The holder's top-left corner, on every intent (decision #409): the game prints an intent's number (an attack's damage, a status count) in
        // its %Value box along the holder's foot, Intent Details' block number and Play Preview's new damage sit there too, so the top is the one
        // corner no number reaches. Just above the %Value box, a 9×5 multi-hit's number still ran under the chip (a dev review).
        row.Position = Vector2.Zero;
        var value = intent.GetNodeOrNull<RichTextLabel>(GodotNames.Path("%Value"));
        if (!_layoutLogged && value is { Visible: true } && !string.IsNullOrWhiteSpace(value.Text))
        {
            // Once a launch: where the game lays out an intent with a number, so the chip's place can be checked against it (dev logs).
            _layoutLogged = true;
            var sprite = intent.GetNodeOrNull<Node2D>(GodotNames.Path("%Intent"));
            var spriteAt = sprite != null ? holder.GetGlobalTransform().AffineInverse() * sprite.GlobalPosition : (Vector2?)null;
            ModEntry.Log.Detail($"[intent-corner] holder {holder.Position} size {holder.Size}; value box {new Rect2(value.Position, value.Size)}, text \"{value.Text}\", content {value.GetContentWidth()}x{value.GetContentHeight()}, "
                + $"align {value.HorizontalAlignment}/{value.VerticalAlignment}; icon centre {spriteAt}; chips at {row.Position} size {size}");
        }
    }

    /// <summary>A power's icon with its amount beside it (the icon alone when the amount isn't a number the move keeps).</summary>
    private static Control Chip(MovePowers.Applied applied)
    {
        var chip = UiKit.Row(separation: 0);
        chip.MouseFilter = Control.MouseFilterEnum.Ignore;
        var icon = UiKit.Icon(GameIcons.Power(applied.Power), IconSize);
        icon.MouseFilter = Control.MouseFilterEnum.Ignore;
        chip.AddChild(icon);
        if (applied.Amount is { } otherAmount)
        {
            var amount = UiKit.Label(otherAmount.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture), FontSize, Colors.White);
            amount.AddThemeConstantOverride(GodotNames.Name("outline_size"), Outline);
            amount.AddThemeColorOverride(GodotNames.Name("font_outline_color"), new Color(0, 0, 0, 0.95f));
            amount.VerticalAlignment = VerticalAlignment.Bottom;
            amount.MouseFilter = Control.MouseFilterEnum.Ignore;
            chip.AddChild(amount);
        }
        else if (applied.Maybe)
        {
            // Only on some of the move's branches, and nothing tells which yet (decision #109).
            var maybe = UiKit.Label("?", FontSize, Colors.White);
            maybe.AddThemeConstantOverride(GodotNames.Name("outline_size"), Outline);
            maybe.AddThemeColorOverride(GodotNames.Name("font_outline_color"), new Color(0, 0, 0, 0.95f));
            maybe.VerticalAlignment = VerticalAlignment.Bottom;
            maybe.MouseFilter = Control.MouseFilterEnum.Ignore;
            chip.AddChild(maybe);
        }
        return chip;
    }
}
