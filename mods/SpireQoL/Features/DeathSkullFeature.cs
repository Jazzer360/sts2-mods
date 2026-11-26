using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using SpireQoL.Cards;
using SpireQoL.Combat;
using Sts2Mods.Core.Features;
using Sts2Mods.Core.Game;
using Sts2Mods.Core.Ui;

namespace SpireQoL.Features;

/// <summary>
/// The game's skull just left of an enemy's HP numbers when it dies before acting if you end your turn
/// now (the Incoming Damage simulation: poison, end-of-turn damage, orbs...). When a roll decides it
/// (a random Lightning target), the skull carries the chance instead (QolSettings.ShowDeathChance).
/// The card tabs still say what a card changes; this says what happens anyway.
/// Spec: docs/FEATURES.md#death-skull.
/// </summary>
public sealed class DeathSkullFeature : IFeature, IFrameFeature
{
    private readonly QolContext _ctx;
    private readonly Dictionary<Creature, Mark> _marks = new(ReferenceEqualityComparer.Instance);

    private sealed class Mark
    {
        public required Control Root;
        public required TextureRect Icon;
        public required Label Chance;
        /// <summary>What it was last placed for (its chance, the creature's HP, the label's size): placed again only when it changes.</summary>
        public (string? Chance, int Hp, int MaxHp, Vector2 Size) PlacedFor;
    }

    /// <summary>Enemy → the chance shown, worked out again only when what it reads changed (ShownFor): the same every frame otherwise.</summary>
    private readonly Dictionary<Creature, string?> _show = new(ReferenceEqualityComparer.Instance);
    private (TurnEndModel? Model, string? Mark, int Probe) _shownFor;
    private readonly List<Creature> _gone = new();

    public DeathSkullFeature(QolContext ctx) => _ctx = ctx;

    public string Id => "death-skull";
    public string DisplayName => "Death Skull";
    public bool EnabledByDefault => true;

    public void Apply(Harmony harmony)
    {
        // Frame-driven. The predictions remember the cards placed on top of your draw pile (observe-only; patched by the first feature that applies, again if its apply was undone).
        DrawPileMemory.Patch(harmony);
    }

    public void Attach(OverlayHost host)
    {
    }

    /// <summary>Each enemy's chance to die, worked out again only when the forecast or the board changes (its own method: decision #177).</summary>
    private void Chances(TurnEndModel model, Dictionary<Creature, string?> show)
    {
        show.Clear();
        foreach (var (enemy, id) in model.Ids)
        {
            if (!enemy.IsAlive || !enemy.IsEnemy) continue;
            var chance = model.Outcome.DeathSkullChance(id);
            if (chance.Certain)
            {
                show[enemy] = null;
                continue;
            }
            if (_ctx.Settings.ShowDeathChance && chance.Seen) show[enemy] = chance.Text() + (model.ShownMark ?? "");
        }
    }

    public void OnFrame(OverlayHost host)
    {
        // Enemy → the chance shown beside the skull (null: certain). Decision #88: once some outcome kills it; a range and a ? when the
        // search stopped at a cap (decision #107), counting the deaths settled before it.
        var shownModel = Sts2State.IsPlayerTurn ? PlayPreview.Model(_ctx) : null;
        var probe = Sts2State.Me is { } me && Sts2State.Combat is { } combat ? BoardProbe.Hash(me, combat) : 0;
        var shownFor = (shownModel, shownModel?.ShownMark, probe);
        var show = _show;
        if (shownFor != _shownFor && shownModel is { } model) Chances(model, show);
        else if (shownModel == null) show.Clear();
        _shownFor = shownFor;

        _gone.Clear();
        foreach (var (creature, mark) in _marks)
            if (!show.ContainsKey(creature) || !GodotObject.IsInstanceValid(mark.Root)) _gone.Add(creature);
        foreach (var creature in _gone)
        {
            if (GodotObject.IsInstanceValid(_marks[creature].Root)) _marks[creature].Root.QueueFree();
            _marks.Remove(creature);
        }
        foreach (var (creature, text) in show)
        {
            if (!_marks.TryGetValue(creature, out var mark))
            {
                if (HpLabel(creature) is not { } label) continue;
                mark = Create(label);
                _marks[creature] = mark;
            }
            if (mark.Root.GetParent() is Label parent && (text, creature.CurrentHp, creature.MaxHp, parent.Size) is var placedFor && placedFor != mark.PlacedFor)
            {
                Place(mark, text);
                mark.PlacedFor = placedFor;
            }
        }
    }

    private static Mark Create(Label label)
    {
        var size = label.GetThemeFontSize(GodotNames.Name("font_size"));
        var root = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        root.AddThemeConstantOverride(GodotNames.Name("separation"), 2);
        var icon = new TextureRect
        {
            Texture = GameIcons.Skull(),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        var chance = UiKit.Label("", size > 0 ? size : 16, Colors.White);
        chance.MouseFilter = Control.MouseFilterEnum.Ignore;
        root.AddChild(chance);
        root.AddChild(icon);
        label.AddChild(root);
        return new Mark { Root = root, Icon = icon, Chance = chance };
    }

    /// <summary>Just left of the HP numbers, centred on them (the label is centred in its bar).</summary>
    private static void Place(Mark mark, string? chance)
    {
        if (mark.Root.GetParent() is not Label label) return;
        mark.Chance.Visible = chance != null;
        mark.Chance.Text = chance ?? "";
        var height = Math.Max(12f, label.Size.Y * 0.9f);
        mark.Icon.CustomMinimumSize = new Vector2(height, height);
        var font = label.GetThemeFont(GodotNames.Name("font"));
        var fontSize = label.GetThemeFontSize(GodotNames.Name("font_size"));
        var textWidth = font?.GetStringSize(label.Text, HorizontalAlignment.Left, -1, fontSize).X ?? label.Size.X / 2;
        var textLeft = label.HorizontalAlignment == HorizontalAlignment.Center ? (label.Size.X - textWidth) / 2 : 0;
        var min = mark.Root.GetCombinedMinimumSize();
        mark.Root.Size = min;
        // A gap that scales with the numbers (the label's outline reaches past the measured text).
        var gap = Math.Max(6f, height * 0.4f);
        mark.Root.Position = new Vector2(textLeft - min.X - gap, (label.Size.Y - min.Y) / 2);
    }

    /// <summary>The HP numbers on a creature's health bar (NHealthBar %HpLabel).</summary>
    private static Label? HpLabel(Creature creature)
    {
        var room = NCombatRoom.Instance;
        if (room == null) return null;
        var node = Sts2State.NodeOf(creature);
        var display = node?.GetNodeOrNull<Control>(GodotNames.Path("%HealthBar"));
        if (display == null) return null;
        return display.GetNodeOrNull<Control>(GodotNames.Path("%HealthBar"))?.GetNodeOrNull<Label>(GodotNames.Path("%HpLabel"))
               ?? display.GetNodeOrNull<Label>(GodotNames.Path("%HpLabel"));
    }
}
