using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.Models.Powers;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Map;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.HoverTips;
using MegaCrit.Sts2.Core.Nodes.Screens.Map;
using MegaCrit.Sts2.Core.Rooms;
using SpireQoL.Data;
using Sts2Mods.Core.Game;
using Sts2Mods.Core.Ui;

namespace SpireQoL.Features;

/// <summary>
/// Pathing reminders (decision #65, docs/PATHING.md): on the map, beside each row of the game's legend (Unknown, Merchant,
/// Treasure, Rest, Enemy, Elite), the icons of your relics and quest cards that make that kind of room unusually good or bad
/// (Data/ChoiceReminders, Map). Hovering one shows its tooltip and highlights that kind of room on the map, as the legend does.
/// Hidden with the legend (its hotkey) and the map.
/// </summary>
public sealed partial class RunRemindersFeature
{
    private const string PathNodeName = "SpireQoL_Path";
    private const float PathIcon = 44f;
    private static readonly System.Reflection.FieldInfo? LegendItemsField = AccessTools.Field(typeof(NMapScreen), "_legendItems");
    private static readonly System.Reflection.FieldInfo? PointTypeField = AccessTools.Field(typeof(NMapLegendItem), "_pointType");
    private readonly Dictionary<NMapLegendItem, string> _pathShown = new(ReferenceEqualityComparer.Instance);
    private readonly List<Cards.TinyCardIcon> _pathCards = new();

    private void MapLegend()
    {
        foreach (var tiny in _pathCards) tiny.Fit();
        var screen = NMapScreen.Instance;
        if (screen == null || !GodotObject.IsInstanceValid(screen) || !screen.IsOpen) return;
        if (LegendItemsField?.GetValue(screen) is not Control items || !GodotObject.IsInstanceValid(items)) return;
        var me = RunMe();
        foreach (var item in items.GetChildren().OfType<NMapLegendItem>())
        {
            var type = PointTypeField?.GetValue(item) is MapPointType t ? t : MapPointType.Unassigned;
            var shown = me == null ? new List<PathMark>() : PathMarks(me, RoomOf(type));
            var signature = string.Join("|", shown.Select(s => $"{s.Model.GetType().Name}:{s.Crossed}:{s.Note}"));
            var row = item.GetNodeOrNull<HBoxContainer>(PathNodeName);
            if (!_pathShown.TryGetValue(item, out var was) || was != signature || (row == null) != (shown.Count == 0))
            {
                row?.QueueFree();
                row = null;
                _pathShown[item] = signature;
                _pathCards.RemoveAll(c => !GodotObject.IsInstanceValid(c.Root) || c.Root.IsQueuedForDeletion() || IsUnder(c.Root, item));
                if (shown.Count > 0)
                {
                    row = PathRow(shown, type);
                    item.AddChild(row);
                }
            }
            if (row == null) continue;
            // Left of the legend row, centered on the row's own icon (on the row when it can't be found); grows to the left (the
            // legend sits at the map's right edge), clear of the icons' backdrops.
            var width = row.GetCombinedMinimumSize().X;
            row.Position = new Vector2(-width - 10 - PathIcon * BackdropPad, LegendIconMiddle(item) - PathIcon / 2);
        }
    }

    /// <summary>The middle (local y) of a legend row's own icon, the game's picture in it; the row's middle when there's none.</summary>
    private static float LegendIconMiddle(Control item)
    {
        try
        {
            var scale = item.GetGlobalTransform().Scale.Y;
            foreach (var pic in Pictures(item))
            {
                if (item.GetNodeOrNull(PathNodeName) is { } ours && ours.IsAncestorOf(pic) || !pic.IsVisibleInTree() || pic.Size.Y < 2) continue;
                return (pic.GetGlobalRect().GetCenter().Y - item.GlobalPosition.Y) / (scale == 0 ? 1 : scale);
            }
        }
        catch { /* the row's middle */ }
        return item.Size.Y / 2;
    }

    private static bool IsUnder(Node node, Node parent)
    {
        for (var n = node.GetParent(); n != null; n = n.GetParent()) if (n == parent) return true;
        return false;
    }

    private sealed record PathMark(AbstractModel Model, bool Crossed, string? Note);

    private static PathRoom RoomOf(MapPointType type) => type switch
    {
        MapPointType.Monster => PathRoom.Monster,
        MapPointType.Elite => PathRoom.Elite,
        MapPointType.RestSite => PathRoom.RestSite,
        MapPointType.Unknown => PathRoom.Unknown,
        MapPointType.Shop => PathRoom.Shop,
        MapPointType.Treasure => PathRoom.Treasure,
        _ => PathRoom.None,
    };

    /// <summary>Your relics and the quest cards in your deck with a pathing rule for this kind of room that applies now.</summary>
    private List<PathMark> PathMarks(Player me, PathRoom room)
    {
        var marks = new List<PathMark>();
        if (room == PathRoom.None) return marks;
        var seen = new HashSet<string>();
        foreach (var model in me.Relics.Cast<AbstractModel>().Concat(me.Deck.Cards))
        {
            var name = model.GetType().Name;
            if (ChoiceReminders.For(name) is not { } rule || (rule.Map & room) == 0 || !seen.Add(name)) continue;
            if (!_ctx.Settings.TabEnabled(ChoiceReminders.Key(ReminderPlace.Map, name))) continue;
            if (!PathApplies(model)) continue;
            marks.Add(new PathMark(model, (rule.MapCrossed & room) != 0, PathNote(model, me)));
        }
        return marks;
    }

    private static readonly IReadOnlySet<RoomType> UnknownCanBe =
        new HashSet<RoomType> { RoomType.Monster, RoomType.Elite, RoomType.Event, RoomType.Shop, RoomType.Treasure };

    /// <summary>
    /// Whether it still changes that kind of room: not used up; Maw Bank until you buy something; a `?` changer while its own hook changes what a `?` can be (Golden Compass's act, Lantern Key's act 3).
    /// </summary>
    private static bool PathApplies(AbstractModel model)
    {
        try
        {
            if (model is RelicModel { IsUsedUp: true }) return false;
            switch (model.GetType().Name)
            {
                case nameof(MawBank):
                    return !Traverse.Create(model).Property("HasItemBeenBought").GetValue<bool>();
            }
            var hook = model.GetType().GetMethod("ModifyUnknownMapPointRoomTypes");
            if (hook != null && hook.DeclaringType == model.GetType())
            {
                // The hook is plain math on the set (no side effects): what a ? can be with it, against without it.
                var after = (IReadOnlySet<RoomType>)hook.Invoke(model, new object[] { UnknownCanBe })!;
                if (model is not Dowsing) return !after.SetEquals(UnknownCanBe);
            }
            return true;
        }
        catch (Exception ex)
        {
            Sts2Mods.Core.Diagnostics.ErrorReporter.Instance?.Exception(ex, "reminders-map:" + model.GetType().Name);
            return true;
        }
    }

    /// <summary>
    /// A number under the icon: the heal on entering (Eternal Feather, from your deck's size; Meal Ticket; Planisphere), the rooms
    /// Dowsing still needs, or the relic's own counter as the top bar shows it.
    /// </summary>
    private static string? PathNote(AbstractModel model, Player me)
    {
        try
        {
            switch (model.GetType().Name)
            {
                case nameof(EternalFeather):
                {
                    var every = Sts2State.Var(model, "Cards", 5);
                    return every > 0 ? $"+{Sts2State.Var(model, "Heal", 3) * (me.Deck.Cards.Count() / every)}" : null;
                }
                case nameof(MealTicket):
                case nameof(Planisphere):
                    return $"+{Sts2State.Var(model, "Heal", 0)}";
                case nameof(Dowsing):
                    return $"{Sts2State.Var(model, "Rooms", 4)}";
            }
            return model is RelicModel { ShowCounter: true } r ? $"{r.DisplayAmount}" : null;
        }
        catch
        {
            return null;
        }
    }

    private HBoxContainer PathRow(List<PathMark> marks, MapPointType type)
    {
        var row = UiKit.Row(6);
        row.Name = PathNodeName;
        row.MouseFilter = Control.MouseFilterEnum.Ignore;
        foreach (var mark in marks)
        {
            Control icon;
            IHoverTip tip;
            if (mark.Model is CardModel card)
            {
                var tiny = new Cards.TinyCardIcon(card, PathIcon);
                _pathCards.Add(tiny);
                icon = tiny.Root;
                tip = HoverTipFactory.FromCard(card);
            }
            else
            {
                var relic = (RelicModel)mark.Model;
                icon = Crossable(IconOf(relic), PathIcon, mark.Crossed);
                tip = relic.HoverTip;
            }
            icon.MouseFilter = Control.MouseFilterEnum.Stop;
            icon.MouseEntered += () =>
            {
                NHoverTipSet.CreateAndShow(icon, tip);
                NMapScreen.Instance?.HighlightPointType(type);
            };
            icon.MouseExited += () =>
            {
                NHoverTipSet.Remove(icon);
                NMapScreen.Instance?.HighlightPointType(MapPointType.Unassigned);
            };
            if (mark.Note != null) icon.AddChild(OddsLabel(mark.Note, PathIcon));
            row.AddChild(Backed(icon, PathIcon));
        }
        return row;
    }

    /// <summary>A legend row with its pathing icons, for the Guide page (decision #56): the icons as the map shows them.</summary>
    internal static Control ExamplePathIcons(IEnumerable<(RelicModel Relic, bool Crossed, string? Note)> icons)
    {
        var row = UiKit.Row(6);
        foreach (var (relic, crossed, note) in icons)
        {
            var icon = Crossable(IconOf(relic), PathIcon, crossed);
            if (note != null) icon.AddChild(OddsLabel(note, PathIcon));
            row.AddChild(Backed(icon, PathIcon));
        }
        return row;
    }
}
