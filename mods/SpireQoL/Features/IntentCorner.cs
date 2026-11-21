using Godot;
using MegaCrit.Sts2.Core.MonsterMoves.Intents;
using MegaCrit.Sts2.Core.Nodes.Combat;
using SpireQoL.Cards;
using SpireQoL.Procs;
using SpireQoL.Sim;
using Sts2Mods.Core.Ui;

namespace SpireQoL.Features;

/// <summary>
/// The powers a move applies, as small chips in the bottom-right corner of the intent they belong to (a buff on its buff intent,
/// a debuff on its debuff intent), where the game puts an attack's damage: read as part of that intent, not as another one.
/// Shared by Intent Details (the intent on screen) and Intent Forecast (later moves). Our node, under the intent's %IntentHolder,
/// so it bobs with the icon; the game's nodes aren't changed.
/// </summary>
internal static class IntentCorner
{
    private const string PowersName = "SpireQoLIntentPowers";
    private const int IconSize = 15;
    private const int FontSize = 11;

    /// <summary>Each power with the index of the intent it goes on: the move's buff intent for a buff, its debuff intent for a debuff, else its last.</summary>
    public static Dictionary<int, List<MovePowers.Applied>> Assign(IReadOnlyList<AbstractIntent> intents, IReadOnlyList<MovePowers.Applied> powers)
    {
        var at = new Dictionary<int, List<MovePowers.Applied>>();
        if (intents.Count == 0) return at;
        var buff = Index(intents, abstractIntent => abstractIntent is BuffIntent);
        var debuff = Index(intents, abstractIntent => abstractIntent is DebuffIntent);
        foreach (var applied in powers)
        {
            var corner = PlayFacts.IsDebuffType(applied.Power) ? debuff ?? buff : buff ?? debuff;
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

    /// <summary>Puts these chips in the intent's corner (none: removes them). Rebuilt only when what they show changes.</summary>
    public static void Place(NIntent intent, IReadOnlyList<MovePowers.Applied>? powers)
    {
        if (intent.GetNodeOrNull<Control>("%IntentHolder") is not { } holder) return;
        var row = holder.GetNodeOrNull<HBoxContainer>(PowersName);
        if (powers is not { Count: > 0 })
        {
            row?.QueueFree();
            return;
        }
        var key = string.Join(",", powers.Select(applied => $"{applied.Power.Name}:{applied.Amount}"));
        if (row != null && row.GetMeta("key", "").AsString() != key)
        {
            row.QueueFree();
            row = null;
        }
        if (row == null)
        {
            row = UiKit.Row(2);
            row.Name = PowersName;
            row.MouseFilter = Control.MouseFilterEnum.Ignore;
            row.SetMeta("key", key);
            foreach (var applied in powers) row.AddChild(Chip(applied));
            holder.AddChild(row);
        }
        var size = row.GetCombinedMinimumSize();
        row.Size = size;
        // The bottom-right corner, as the game's %Value label (an attack's damage); else the holder's own corner.
        if (intent.GetNodeOrNull<Control>("%Value") is { } value)
            row.Position = new Vector2(value.Position.X + value.Size.X - size.X, value.Position.Y + value.Size.Y - size.Y);
        else
            row.Position = new Vector2(holder.Size.X - size.X, holder.Size.Y - size.Y);
    }

    /// <summary>A power's icon with its amount beside it (the icon alone when the amount isn't a number the move keeps).</summary>
    private static Control Chip(MovePowers.Applied applied)
    {
        var chip = UiKit.Row(0);
        chip.MouseFilter = Control.MouseFilterEnum.Ignore;
        var icon = UiKit.Icon(GameIcons.Power(applied.Power), IconSize);
        icon.MouseFilter = Control.MouseFilterEnum.Ignore;
        chip.AddChild(icon);
        if (applied.Amount is { } otherAmount)
        {
            var amount = UiKit.Label(otherAmount.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture), FontSize, Colors.White);
            amount.AddThemeConstantOverride("outline_size", 4);
            amount.AddThemeColorOverride("font_outline_color", new Color(0, 0, 0, 0.95f));
            amount.VerticalAlignment = VerticalAlignment.Bottom;
            amount.MouseFilter = Control.MouseFilterEnum.Ignore;
            chip.AddChild(amount);
        }
        else if (applied.Maybe)
        {
            // Only on some of the move's branches, and nothing tells which yet (decision #109).
            var maybe = UiKit.Label("?", FontSize, Colors.White);
            maybe.AddThemeConstantOverride("outline_size", 4);
            maybe.AddThemeColorOverride("font_outline_color", new Color(0, 0, 0, 0.95f));
            maybe.VerticalAlignment = VerticalAlignment.Bottom;
            maybe.MouseFilter = Control.MouseFilterEnum.Ignore;
            chip.AddChild(maybe);
        }
        return chip;
    }
}
