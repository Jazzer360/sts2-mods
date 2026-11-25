using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Relics;
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
public sealed partial class RunReminders
{
    private const string PathNodeName = "SpireQoL_Path";
    private const float PathIcon = 44f;
    private static readonly System.Reflection.FieldInfo? LegendItemsField = AccessTools.Field(typeof(NMapScreen), "_legendItems");
    private static readonly System.Reflection.FieldInfo? PointTypeField = AccessTools.Field(typeof(NMapLegendItem), "_pointType");
    private readonly Dictionary<NMapLegendItem, string> _pathShown = new(ReferenceEqualityComparer.Instance);
    private readonly List<Cards.TinyCardIcon> _pathCards = new();
    /// <summary>What the legend's rows were last built from (LegendKey; 0: the map was closed), and dev mode's check of it.</summary>
    private int _legendKey;
    private readonly SpireQoL.Combat.ReuseCheck _legendReuse = new("the map legend's reminders");

    /// <summary>
    /// Everything the legend's rows are worked out from, as one number, read without making anything: the settings, your relics and
    /// deck (how many, and each relic as it is: used up or not), gold and HP, and the legend's rows, where they are and our rows' sizes. Working the rows
    /// out every few frames made garbage the whole time the map was open (decision #177).
    /// </summary>
    private static int LegendKey(Control items, Player? me)
    {
        var hashCode = new HashCode();
        hashCode.Add(QolSettings.Revision);
        for (var i = 0; i < items.GetChildCount(); i++)
            if (items.GetChild(i) is Control item)
            {
                hashCode.Add(System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(item));
                hashCode.Add(item.GlobalPosition);
                hashCode.Add(item.Size);
                hashCode.Add(item.IsVisibleInTree());
                // Our own row's size: known only once Godot has laid it out, and the row is placed from it.
                if (item.GetNodeOrNull<Control>(GodotNames.Path(PathNodeName)) is { } row) hashCode.Add(row.GetCombinedMinimumSize());
            }
        if (me != null) AddRunState(ref hashCode, me);
        var key = hashCode.ToHashCode();
        return key == 0 ? 1 : key;
    }

    /// <summary>What the reminders read of you, without making anything: gold, HP, the deck's size, each relic as it is (used up or not, its counter).</summary>
    private static void AddRunState(ref HashCode hashCode, Player me)
    {
        hashCode.Add(me.Gold);
        hashCode.Add(me.Creature.CurrentHp);
        hashCode.Add(me.Creature.MaxHp);
        hashCode.Add(me.Deck.Cards.Count);
        var relics = me.Relics;
        for (var i = 0; i < relics.Count; i++)
        {
            hashCode.Add(System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(relics[i]));
            hashCode.Add(relics[i].IsUsedUp);
            hashCode.Add(relics[i].DisplayAmount);
        }
    }

    internal void MapLegend()
    {
        foreach (var tiny in _pathCards) tiny.Fit();
        var screen = NMapScreen.Instance;
        if (screen == null || !GodotObject.IsInstanceValid(screen) || !screen.IsOpen || LegendItemsField?.GetValue(screen) is not Control items || !GodotObject.IsInstanceValid(items))
        {
            _legendKey = 0; // opened again: worked out again
            return;
        }
        var me = RunMe();
        var key = LegendKey(items, me);
        var reuse = _legendReuse.Decide(key == _legendKey);
        if (reuse == SpireQoL.Combat.FrameReuse.Reuse) return;
        _legendKey = key;
        LegendRows(items, me, check: reuse == SpireQoL.Combat.FrameReuse.WorkOutAndCheck);
    }

    /// <summary>The legend's rows worked out and placed (on a change; dev mode's check compares them with what it kept, and logs a difference).</summary>
    private void LegendRows(Control items, Player? me, bool check)
    {
        foreach (var item in items.GetChildren().OfType<NMapLegendItem>())
        {
            var type = PointTypeField?.GetValue(item) is MapPointType mapPointType ? mapPointType : MapPointType.Unassigned;
            var shown = me == null ? new List<PathMark>() : PathMarks(me, RoomOf(type));
            var signature = string.Join("|", shown.Select(pathMark => $"{pathMark.Model.GetType().Name}:{pathMark.Crossed}:{pathMark.Note}"));
            var row = item.GetNodeOrNull<HBoxContainer>(GodotNames.Path(PathNodeName));
            if (!_pathShown.TryGetValue(item, out var was) || was != signature || (row == null) != (shown.Count == 0))
            {
                if (check) _legendReuse.Compare(same: false, "a legend row changed while its key didn't");
                row.RemoveAndFree();
                row = null;
                _pathShown[item] = signature;
                _pathCards.RemoveAll(tinyCardIcon => !GodotObject.IsInstanceValid(tinyCardIcon.Root) || tinyCardIcon.Root.IsQueuedForDeletion() || IsUnder(tinyCardIcon.Root, item));
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
                if (item.GetNodeOrNull(GodotNames.Path(PathNodeName)) is { } ours && ours.IsAncestorOf(pic) || !pic.IsVisibleInTree() || pic.Size.Y < 2) continue;
                return (pic.GetGlobalRect().GetCenter().Y - item.GlobalPosition.Y) / (scale == 0 ? 1 : scale);
            }
        }
        catch { /* the row's middle */ }
        return item.Size.Y / 2;
    }

    private static bool IsUnder(Node node, Node parent)
    {
        for (var otherNode = node.GetParent(); otherNode != null; otherNode = otherNode.GetParent()) if (otherNode == parent) return true;
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
            if ((room & PathRoom.RestSite) != 0 && model is RelicModel && !OffersRestSiteOption(model.GetType(), me.Deck.Cards)) continue;
            marks.Add(new PathMark(model, (rule.MapCrossed & room) != 0, PathNote(model, me)));
        }
        return marks;
    }

    /// <summary>
    /// Whether the relic still adds its rest-site option (decision #296): its own class does (an override of TryModifyRestSiteOptions), or none of
    /// the enchantments its code gives a card does; else only while a card in your deck holds one that does (v0.114.0 moved Pael's Growth's Clone
    /// option onto the Clone enchantment: Clone.TryModifyRestSiteOptions, offered while your deck has a card it enchants).
    /// </summary>
    internal static bool OffersRestSiteOption(Type relic, IEnumerable<CardModel> deck)
    {
        if (Overrides(relic, nameof(AbstractModel.TryModifyRestSiteOptions))) return true;
        var enchantments = RestSiteEnchantmentsOf(relic);
        return enchantments.Count == 0 || deck.Any(card => card.Enchantment is { } enchantment && enchantments.Contains(enchantment.GetType()));
    }

    /// <summary>
    /// The enchantments the relic's code gives a card (CardCmd.Enchant&lt;T&gt; in any of its methods, the compiler's async bodies included) whose
    /// class adds a rest-site option (overrides TryModifyRestSiteOptions). Read once per class.
    /// </summary>
    internal static IReadOnlySet<Type> RestSiteEnchantmentsOf(Type relic) => RestSiteEnchantments.GetOrAdd(relic, type =>
    {
        var found = new HashSet<Type>();
        const System.Reflection.BindingFlags every = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Static
            | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.DeclaredOnly;
        foreach (var owner in type.GetNestedTypes(System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public).Append(type))
            foreach (var method in owner.GetMethods(every))
            {
                try
                {
                    foreach (var instruction in Diagnostics.GameCode.Instructions(method))
                        if (instruction.operand is System.Reflection.MethodInfo { IsGenericMethod: true, Name: "Enchant" } called
                            && called.DeclaringType == typeof(MegaCrit.Sts2.Core.Commands.CardCmd)
                            && called.GetGenericArguments()[0] is { } enchantment && Overrides(enchantment, nameof(AbstractModel.TryModifyRestSiteOptions)))
                            found.Add(enchantment);
                }
                catch
                {
                    // a method with no body to read (abstract, extern): nothing it gives
                }
            }
        return found;
    });

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<Type, IReadOnlySet<Type>> RestSiteEnchantments = new();

    /// <summary>The class (or a base class of it other than AbstractModel) declares the virtual method.</summary>
    private static bool Overrides(Type type, string method) =>
        type.GetMethod(method, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public) is { } found && found.DeclaringType != typeof(AbstractModel);

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
                    var every = Sts2State.Var(model, "Cards", fallback: 5);
                    return every > 0 ? $"+{Sts2State.Var(model, "Heal", fallback: 3) * (me.Deck.Cards.Count / every)}" : null;
                }
                case nameof(MealTicket):
                case nameof(Planisphere):
                    return $"+{Sts2State.Var(model, "Heal", fallback: 0)}";
                case nameof(Dowsing):
                    return $"{Sts2State.Var(model, "Rooms", fallback: 4)}";
            }
            return model is RelicModel { ShowCounter: true } relic ? $"{relic.DisplayAmount}" : null;
        }
        catch
        {
            return null;
        }
    }

    private HBoxContainer PathRow(List<PathMark> marks, MapPointType type)
    {
        var row = UiKit.Row(separation: 6);
        row.Name = GodotNames.Name(PathNodeName);
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
        var row = UiKit.Row(separation: 6);
        foreach (var (relic, crossed, note) in icons)
        {
            var icon = Crossable(IconOf(relic), PathIcon, crossed);
            if (note != null) icon.AddChild(OddsLabel(note, PathIcon));
            row.AddChild(Backed(icon, PathIcon));
        }
        return row;
    }
}
