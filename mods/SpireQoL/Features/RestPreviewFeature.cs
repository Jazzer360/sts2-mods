using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Relics;
using MegaCrit.Sts2.Core.Entities.RestSite;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Modifiers;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.Nodes.RestSite;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using SpireQoL.Cards;
using Sts2Mods.Core.Features;
using Sts2Mods.Core.Game;
using Sts2Mods.Core.Ui;

namespace SpireQoL.Features;

/// <summary>
/// Rest sites (docs/FEATURES.md "Rest Preview"), from the game's own rules (decompile v0.113.1):
///  - Rest heals HealRestSiteOption.GetHealAmount (30% of max HP through ModifyRestSiteHealAmount: Regal Pillow +15, Night
///    Terrors to full); HP becomes the whole part of HP + heal, capped at max (Creature.SetCurrentHpInternal). Then
///    AfterRestSiteHeal: Stone Humidifier +5 max HP (and HP), Night Terrors -5 max HP (HP capped).
///  - At the last rest site before the boss (the boss map point's parent: what Pantograph and Bath Robes mark Active), the
///    boss fight starts with Pantograph's heal (BeforeCombatStart: 25) and an unused Bath Robes' (to full).
/// One panel at the right edge of the screen (a movable widget, not a line per option): resting's HP and the heal wasted over your max; with
/// Pantograph or an unused Bath Robes, the boss fight's HP if you rest (rest heal, then the relic's: what's wasted) and if not.
/// Checked: HP after resting, and HP when the boss fight starts.
/// </summary>
public sealed class RestPreviewFeature : IFeature, IFrameFeature
{
    private static readonly Color Over = new(1f, 0.55f, 0.25f);
    private static readonly Color Plain = new(0.95f, 0.92f, 0.85f);
    private static readonly Color Accent = new(1f, 0.55f, 0.1f);
    private static readonly Color Muted = new(0.68f, 0.66f, 0.62f);

    private readonly QolContext _ctx;

    public RestPreviewFeature(QolContext ctx) => _ctx = ctx;

    public string Id => "rest-preview";
    public string DisplayName => "Rest Preview";
    public bool EnabledByDefault => true;

    public void Apply(Harmony harmony)
    {
        // Observe-only: the rest check (what resting left you with against the preview).
        harmony.Patch(AccessTools.Method(typeof(HealRestSiteOption), nameof(HealRestSiteOption.OnSelect)),
            prefix: new HarmonyMethod(typeof(RestPreviewFeature), nameof(BeforeRest)));
    }

    private static (int Hp, int Max)? _pendingRest;

    private static void BeforeRest(HealRestSiteOption __instance)
    {
        try
        {
            if (Sts2State.Private<Player?>(__instance, "Owner", null) is not { } owner || RunMe() != owner) return;
            if (Compute(owner) is { } p) _pendingRest = (p.RestedHp, p.RestedMax);
        }
        catch (Exception ex)
        {
            ModEntry.Log.Warn($"rest preview check: {ex.Message}");
        }
    }

    private MovableWidget? _widget;

    public void Attach(OverlayHost host)
    {
        // The right edge, vertically centered: clear of the options, the description the game shows under a hovered option,
        // and your character (bottom left). Movable like every panel.
        _widget = _ctx.Widget(Id, "Rest Preview", new WidgetLayout { Anchor = ScreenAnchor.Right, OffsetX = -40, OffsetY = 0 });
    }

    /// <summary>What resting now leaves you with, and the boss fight's start with or without resting.</summary>
    public sealed record Preview(int Hp, int MaxHp, decimal Heal, int RestedHp, int RestedMax, int Over,
        List<RelicModel> BossRelics, int BossIfRested, int BossIfNot, int BossOver);

    public static Preview? Compute(Player me)
    {
        var c = me.Creature;
        int hp = c.CurrentHp, max = c.MaxHp;
        if (max <= 0) return null;
        decimal heal;
        try { heal = HealRestSiteOption.GetHealAmount(me); } catch { return null; }
        // CreatureCmd.Heal: HealInternal → SetCurrentHpInternal((int)min(hp + heal, max)).
        var rested = (int)Math.Min(hp + heal, max);
        var over = (int)Math.Max(0, hp + heal - max);
        var restedMax = max;
        // AfterRestSiteHeal: Stone Humidifier gains max HP (GainMaxHp heals by what it adds); Night Terrors loses 5 (at least 1 left).
        foreach (var r in me.Relics.OfType<StoneHumidifier>())
        {
            var gain = Sts2State.Var(r, "MaxHp", 5);
            restedMax += gain;
            rested = Math.Min(rested + gain, restedMax);
        }
        if (me.RunState.Modifiers.Any(m => m is NightTerrors))
        {
            restedMax -= Math.Min(5, restedMax - 1);
            rested = Math.Min(rested, restedMax);
        }

        // The boss fight's start: the relics mark themselves Active at the boss's parent map point (AfterRoomEntered).
        var bossRelics = me.Relics.Where(r => r.Status == RelicStatus.Active && r is Pantograph or BathRobes { IsUsedUp: false }).ToList();
        int Boss(int from, int top)
        {
            var h = from;
            foreach (var r in bossRelics)
            {
                if (r is BathRobes) h = top;
                else if (r is Pantograph) h = Math.Min(top, h + Sts2State.Var(r, "Heal", 25));
            }
            return h;
        }
        var bossIfRested = Boss(rested, restedMax);
        var bossIfNot = Boss(hp, max);
        // The rest heal that doesn't reach the boss fight: what it would heal, less what it adds to the fight's start.
        var bossOver = bossRelics.Count == 0 ? 0 : Math.Max(0, (int)heal - (bossIfRested - bossIfNot));
        return new Preview(hp, max, heal, rested, restedMax, over, bossRelics, bossIfRested, bossIfNot, bossOver);
    }

    private int _restCheckFrames;
    private int? _bossPredicted;
    private int _bossFrames;

    public void OnFrame(OverlayHost host)
    {
        var w = _widget!;
        var room = NRestSiteRoom.Instance;
        var me = RunMe();
        Check(me);
        var preview = room != null && GodotObject.IsInstanceValid(room) && room.IsVisibleInTree() && me != null ? Compute(me) : null;
        // Rest is on offer (not used yet, not disabled): its lines show; otherwise only what you'll have anyway.
        var canRest = preview != null && RestOption(room!) is { IsEnabled: true };
        var show = preview != null && (canRest || preview.BossRelics.Count > 0);
        w.HasContent = show;
        if (!show)
        {
            _shownKey = "";
            return;
        }
        var key = $"{preview}|{canRest}|{string.Join(",", preview!.BossRelics.Select(r => r.GetType().Name))}";
        if (key == _shownKey) return;
        _shownKey = key;
        foreach (var child in w.Content.GetChildren()) child.QueueFree();
        Fill(w.Content, preview, canRest);
    }

    private string _shownKey = "";

    /// <summary>The Rest option on offer, if any (HealRestSiteOption).</summary>
    private static RestSiteOption? RestOption(NRestSiteRoom room)
    {
        if (room.GetNodeOrNull<Control>("%ChoicesContainer") is not { } choices) return null;
        foreach (var button in choices.GetChildren().OfType<NRestSiteButton>())
        {
            if (!GodotObject.IsInstanceValid(button)) continue;
            try { if (button.Option is HealRestSiteOption heal) return heal; } catch { }
        }
        return null;
    }

    /// <summary>
    /// One panel for the whole rest site (not a line per option): resting's HP and what's wasted; before the boss, the boss
    /// fight's HP if you rest (rest heal, then the relic's) and if you don't.
    /// </summary>
    private static void Fill(VBoxContainer content, Preview p, bool canRest)
    {
        if (p.BossRelics.Count == 0)
        {
            var line = Row();
            line.AddChild(Text("Rest", Accent));
            line.AddChild(Text($"HP {p.Hp} → {p.RestedHp}/{p.RestedMax}", Plain));
            if (p.Over > 0) line.AddChild(Text($"({p.Over} over)", Over));
            content.AddChild(line);
            return;
        }
        var head = Row();
        foreach (var r in p.BossRelics)
        {
            var icon = UiKit.Icon(GameIcons.Relic(r), 30);
            icon.MouseFilter = Control.MouseFilterEnum.Ignore;
            head.AddChild(icon);
        }
        head.AddChild(Text("Boss fight HP", Accent));
        content.AddChild(head);
        if (canRest)
        {
            var rest = Row();
            rest.AddChild(Text("Rest:", Muted));
            rest.AddChild(Text($"{p.Hp} → {p.RestedHp} → {p.BossIfRested}/{p.RestedMax}", Plain));
            if (p.BossOver > 0) rest.AddChild(Text($"({p.BossOver} over)", Over));
            content.AddChild(rest);
        }
        var skip = Row();
        skip.AddChild(Text(canRest ? "Don't rest:" : "Boss:", Muted));
        skip.AddChild(Text($"{p.Hp} → {p.BossIfNot}/{p.MaxHp}", Plain));
        content.AddChild(skip);
    }

    private static HBoxContainer Row()
    {
        var row = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        row.AddThemeConstantOverride("separation", 6);
        return row;
    }

    private static Label Text(string text, Color color)
    {
        var l = UiKit.Label(text, 20, color);
        l.MouseFilter = Control.MouseFilterEnum.Ignore;
        return l;
    }

    // ------------------------------------------------------------------ check (audit)

    /// <summary>
    /// After resting: HP and max HP against the preview (once the heal and AfterRestSiteHeal settle). At the boss: HP once the
    /// fight has started against what the preview said for how you left the last rest site.
    /// </summary>
    private void Check(Player? me)
    {
        if (me == null) return;
        var c = me.Creature;
        if (_pendingRest is { } want && ++_restCheckFrames >= 90)
        {
            var what = $"rest: HP {c.CurrentHp}/{c.MaxHp}, predicted {want.Hp}/{want.Max}";
            var ok = c.CurrentHp == want.Hp && c.MaxHp == want.Max;
            ModEntry.Log.Info($"[audit] [rest] {what}{(ok ? ", matched" : "")}");
            if (!ok) Sts2Mods.Core.Diagnostics.ErrorReporter.Instance?.Mismatch("rest", "HP after resting didn't match the preview", new[] { "rest-heal" }, what);
            _pendingRest = null;
        }
        if (_pendingRest == null) _restCheckFrames = 0;

        if (NRestSiteRoom.Instance is { } r && GodotObject.IsInstanceValid(r))
        {
            // How you leave the rest site (rested or not): with Pantograph or Bath Robes ready, the boss fight's start.
            var p = Compute(me);
            _bossPredicted = p is { BossRelics.Count: > 0 } ? p.BossIfNot : null;
            _bossFrames = 0;
            return;
        }
        if (_bossPredicted is not { } boss) return;
        if (!Sts2State.InCombat) return;
        if (me.RunState.CurrentRoom?.RoomType != RoomType.Boss) { _bossPredicted = null; return; }
        // BeforeCombatStart has run by the time your first turn's cards are dealt; nothing hits you before you act.
        if (++_bossFrames < 45) return;
        var line = $"boss start: HP {c.CurrentHp}/{c.MaxHp}, predicted {boss}";
        ModEntry.Log.Info($"[audit] [rest] {line}{(c.CurrentHp == boss ? ", matched" : "")}");
        if (c.CurrentHp != boss)
            Sts2Mods.Core.Diagnostics.ErrorReporter.Instance?.Mismatch("rest", "HP at the boss fight's start didn't match the rest preview", new[] { "rest-boss" }, line);
        _bossPredicted = null;
    }

    private static Player? RunMe()
    {
        if (Sts2State.Me is { } me) return me;
        var state = Sts2State.Private<RunState?>(RunManager.Instance, "State", null);
        return state == null ? null : LocalContext.GetMe(state);
    }
}
