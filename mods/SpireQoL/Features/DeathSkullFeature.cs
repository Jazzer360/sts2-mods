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
/// (a random Lightning target), the skull carries the chance instead (QolSettings.DeathChance).
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
    }

    public DeathSkullFeature(QolContext ctx) => _ctx = ctx;

    public string Id => "death-skull";
    public string DisplayName => "Death Skull";
    public bool EnabledByDefault => true;

    public void Apply(Harmony harmony)
    {
        // Frame-driven; no patches.
    }

    public void Attach(OverlayHost host)
    {
    }

    public void OnFrame(OverlayHost host)
    {
        // Enemy → chance it dies before acting (1 = certain).
        var show = new Dictionary<Creature, double>(ReferenceEqualityComparer.Instance);
        if (Sts2State.IsPlayerTurn && TurnEndModel.Compute() is { } model)
        {
            foreach (var (enemy, id) in model.Ids)
            {
                if (!enemy.IsAlive || !enemy.IsEnemy) continue;
                if (model.Outcome.DeadInAll(id))
                {
                    show[enemy] = 1;
                    continue;
                }
                var p = model.Outcome.DeadChance(id);
                if (_ctx.Settings.DeathChance && !double.IsNaN(p) && p > 0) show[enemy] = Math.Min(p, 0.99);
            }
        }

        foreach (var (creature, mark) in _marks.ToList())
        {
            if (show.ContainsKey(creature) && GodotObject.IsInstanceValid(mark.Root)) continue;
            if (GodotObject.IsInstanceValid(mark.Root)) mark.Root.QueueFree();
            _marks.Remove(creature);
        }
        foreach (var (creature, p) in show)
        {
            if (!_marks.TryGetValue(creature, out var mark))
            {
                if (HpLabel(creature) is not { } label) continue;
                mark = Create(label);
                _marks[creature] = mark;
            }
            Place(mark, p);
        }
    }

    private static Mark Create(Label label)
    {
        var size = label.GetThemeFontSize("font_size");
        var root = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        root.AddThemeConstantOverride("separation", 2);
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
    private static void Place(Mark mark, double p)
    {
        if (mark.Root.GetParent() is not Label label) return;
        var certain = p >= 1;
        mark.Chance.Visible = !certain;
        mark.Chance.Text = certain ? "" : $"{Math.Max(1, (int)Math.Round(p * 100))}%";
        var h = Math.Max(12f, label.Size.Y * 0.9f);
        mark.Icon.CustomMinimumSize = new Vector2(h, h);
        var font = label.GetThemeFont("font");
        var fontSize = label.GetThemeFontSize("font_size");
        var textWidth = font?.GetStringSize(label.Text, HorizontalAlignment.Left, -1, fontSize).X ?? label.Size.X / 2;
        var textLeft = label.HorizontalAlignment == HorizontalAlignment.Center ? (label.Size.X - textWidth) / 2 : 0;
        var min = mark.Root.GetCombinedMinimumSize();
        mark.Root.Size = min;
        // A gap that scales with the numbers (the label's outline reaches past the measured text).
        var gap = Math.Max(6f, h * 0.4f);
        mark.Root.Position = new Vector2(textLeft - min.X - gap, (label.Size.Y - min.Y) / 2);
    }

    /// <summary>The HP numbers on a creature's health bar (NHealthBar %HpLabel).</summary>
    private static Label? HpLabel(Creature creature)
    {
        var room = NCombatRoom.Instance;
        if (room == null) return null;
        var node = room.CreatureNodes.FirstOrDefault(n => GodotObject.IsInstanceValid(n) && n.Entity == creature);
        var display = node?.GetNodeOrNull<Control>("%HealthBar");
        if (display == null) return null;
        return display.GetNodeOrNull<Control>("%HealthBar")?.GetNodeOrNull<Label>("%HpLabel")
               ?? display.GetNodeOrNull<Label>("%HpLabel");
    }
}
