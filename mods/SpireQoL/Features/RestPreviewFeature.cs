using System.Runtime.CompilerServices;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Relics;
using MegaCrit.Sts2.Core.Entities.RestSite;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Modifiers;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using SpireQoL.Cards;
using SpireQoL.Sim;
using Sts2Mods.Core.Features;
using Sts2Mods.Core.Game;
using Sts2Mods.Core.Ui;

namespace SpireQoL.Features;

/// <summary>
/// Rest sites (docs/FEATURES.md "Rest Preview", decision #57): a sum beside your character, drawn like Incoming Damage's
/// (RestMathPanel): your HP, the rest heal, a line per relic that changes it and per boss relic the boss fight starts with,
/// and the result, with the part of the heal that's wasted. In multiplayer, hovering a teammate while Mend is on offer
/// shows the same sum for them (Mend heals them as Rest would: MendRestSiteOption). The math is RestMath (tested).
/// Rules (decompile v0.113.1):
///  - Rest and Mend heal GetHealAmount (30% of max HP through ModifyRestSiteHealAmount: Regal Pillow +15, Night
///    Terrors to full); then AfterRestSiteHeal: Stone Humidifier +5 max HP (and HP), Night Terrors −5 max HP.
///  - At the last rest site before the boss (what Pantograph and Bath Robes mark Active), the boss fight starts with
///    Pantograph's heal (BeforeCombatStart: 25) and an unused Bath Robes' (to full).
/// The rest lines show only while the option is on offer (RestSiteSynchronizer's local options) and not already chosen.
/// Checked: HP after resting, after a Mend (the teammate's), and when the boss fight starts.
/// </summary>
public sealed class RestPreviewFeature : IFeature, IFrameFeature
{
    private readonly QolContext _ctx;

    public RestPreviewFeature(QolContext ctx) => _ctx = ctx;

    public string Id => "rest-preview";
    public string DisplayName => "Rest Preview";
    public bool EnabledByDefault => true;

    public void Apply(Harmony harmony)
    {
        // Observe-only: the checks (what resting or mending left the player with against the preview), and which option
        // is already being used (its heal is under way; it's no longer on offer).
        harmony.Patch(AccessTools.Method(typeof(HealRestSiteOption), nameof(HealRestSiteOption.OnSelect)),
            prefix: new HarmonyMethod(typeof(RestPreviewFeature), nameof(BeforeRest)));
        harmony.Patch(AccessTools.Method(typeof(MendRestSiteOption), nameof(MendRestSiteOption.OnSelect)),
            prefix: new HarmonyMethod(typeof(RestPreviewFeature), nameof(BeforeMend)),
            postfix: new HarmonyMethod(typeof(RestPreviewFeature), nameof(AfterMend)));
    }

    /// <summary>Options already chosen (their task is running or done): the preview stops offering them.</summary>
    private static readonly ConditionalWeakTable<RestSiteOption, object> Used = new();

    private static (int Hp, int Max)? _pendingRest;

    private static void BeforeRest(HealRestSiteOption __instance)
    {
        using var timing = OverlayHost.TimeHook("rest-preview");
        try
        {
            if (Sts2State.Private<Player?>(__instance, "Owner", fallback: null) is not { } owner || RunMe() != owner) return;
            Used.AddOrUpdate(__instance, true);
            if (Compute(owner, rest: true) is { } preview) _pendingRest = (preview.Outcome.RestedHp, preview.Outcome.RestedMax);
        }
        catch (Exception ex)
        {
            ModEntry.Log.Warn($"rest preview check: {ex.Message}");
        }
    }

    /// <summary>A Mend you chose: each teammate's HP before and the preview, to check the one whose HP changes.</summary>
    private static Dictionary<Player, (int Hp, int Max, int WantHp, int WantMax)>? _pendingMend;

    private static void BeforeMend(MendRestSiteOption __instance)
    {
        using var timing = OverlayHost.TimeHook("rest-preview");
        try
        {
            if (Sts2State.Private<Player?>(__instance, "Owner", fallback: null) is not { } owner || RunMe() != owner) return;
            Used.AddOrUpdate(__instance, true);
            var pending = new Dictionary<Player, (int, int, int, int)>();
            foreach (var mate in owner.RunState.Players)
            {
                if (mate == owner || Compute(mate, rest: true) is not { } preview) continue;
                pending[mate] = (mate.Creature.CurrentHp, mate.Creature.MaxHp, preview.Outcome.RestedHp, preview.Outcome.RestedMax);
            }
            _pendingMend = pending;
        }
        catch (Exception ex)
        {
            ModEntry.Log.Warn($"mend preview check: {ex.Message}");
        }
    }

    /// <summary>Targeting cancelled (OnSelect returns false): Mend is on offer again, and there's nothing to check.</summary>
    private static void AfterMend(MendRestSiteOption __instance, Task<bool> __result)
    {
        using var timing = OverlayHost.TimeHook("rest-preview");
        try
        {
            __result?.ContinueWith(task =>
            {
                if (task.IsCompletedSuccessfully && task.Result) return;
                Used.Remove(__instance);
                _pendingMend = null;
            });
        }
        catch (Exception ex)
        {
            Sts2Mods.Core.Diagnostics.ErrorReporter.Instance?.Exception(ex, "rest-mend");
        }
    }

    private MovableWidget? _mine;
    private MovableWidget? _mate;
    private RestMathPanel? _minePanel;
    private RestMathPanel? _matePanel;
    private Player? _hoveredMate;

    public void Attach(OverlayHost host)
    {
        // Beside your character at the fire, like Incoming Damage beside you in a fight; the right edge when the character
        // isn't found. Movable like every panel.
        _mine = _ctx.Widget("rest-hp", "Rest Preview", new WidgetLayout { FollowGame = true, Anchor = ScreenAnchor.Right, OffsetX = 0, OffsetY = 0 });
        // On the outer side of the character (decision #100): left of one sitting left of the fire, right of one sitting right of
        // it (in multiplayer the players sit on both sides); the other side when there's no room.
        _mine.AttachTo(() => Side(CharacterRect(RunMe()), right: false), AttachSide.LeftOf)
             .AttachTo(() => Side(CharacterRect(RunMe()), right: true), AttachSide.RightOfBottom)
             .AttachTo(() => CharacterRect(RunMe()), AttachSide.LeftOf)
             .AttachTo(() => CharacterRect(RunMe()), AttachSide.RightOfBottom);
        _minePanel = new RestMathPanel(_mine.Content);
        _mate = _ctx.Widget("rest-mend", "Mend Preview", new WidgetLayout { FollowGame = true, Anchor = ScreenAnchor.Right, OffsetX = 0, OffsetY = 0 });
        _mate.AttachTo(() => Side(CharacterRect(_hoveredMate), right: false), AttachSide.LeftOf)
             .AttachTo(() => Side(CharacterRect(_hoveredMate), right: true), AttachSide.RightOfBottom)
             .AttachTo(() => CharacterRect(_hoveredMate), AttachSide.LeftOf)
             .AttachTo(() => CharacterRect(_hoveredMate), AttachSide.RightOfBottom);
        _matePanel = new RestMathPanel(_mate.Content);
    }

    private static readonly HashSet<string> SidesLogged = new();

    /// <summary>
    /// The character's rect when it sits on that half of the screen (right: its centre right of the screen's middle), else null:
    /// the panel goes on the character's outer side. Logged once per character and place.
    /// </summary>
    private static Rect2? Side(Rect2? character, bool right)
    {
        if (character is not { } rect || NRestSiteRoom.Instance is not { } room || !GodotObject.IsInstanceValid(room)) return null;
        var middle = room.GetViewportRect().Size.X / 2;
        var onRight = rect.GetCenter().X > middle;
        var key = $"{rect.Position.X:0},{rect.Position.Y:0}";
        if (SidesLogged.Add(key)) ModEntry.Log.Detail($"rest preview: character at {rect.Position}, {rect.Size} (screen middle {middle:0}): panel on its {(onRight ? "right" : "left")}");
        return onRight == right ? rect : null;
    }

    /// <summary>A player's character at the fire (NRestSiteCharacter's hitbox), on screen.</summary>
    private static Rect2? CharacterRect(Player? who)
    {
        if (who == null || NRestSiteRoom.Instance is not { } room || !GodotObject.IsInstanceValid(room)) return null;
        var nRestSiteCharacter = room.GetCharacterForPlayer(who);
        if (nRestSiteCharacter == null || !GodotObject.IsInstanceValid(nRestSiteCharacter) || nRestSiteCharacter.Hitbox is not { } hit || !hit.IsVisibleInTree()) return null;
        var rect = Sts2State.ScreenRect(hit);
        return rect.Size.X >= 1 ? rect : null;
    }

    /// <summary>A sum, and its icons (the game's own: the option, the relics, the modifier).</summary>
    public sealed record Preview(RestOutcome Outcome, List<(Texture2D? Icon, int Hp)> Lines, List<RelicModel> BossRelics);

    /// <summary>Bath Robes (the boss fight heals you to full): beta only (decision #163); main has no Bath Robes.</summary>
    [BetaOnly, System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static bool IsBathRobesRelic(RelicModel relic) => relic is BathRobes;

    private static readonly Func<RelicModel, bool>? BathRobesIs = BetaOnly.Gate<Func<RelicModel, bool>>(IsBathRobesRelic);

    /// <summary>What Rest (or a Mend on <paramref name="who"/>) leaves them with; rest false: only the boss relics.</summary>
    public static Preview? Compute(Player who, bool rest, bool mend = false)
    {
        var creature = who.Creature;
        if (creature.MaxHp <= 0) return null;
        decimal heal, baseHeal;
        try
        {
            heal = HealRestSiteOption.GetHealAmount(who);
            baseHeal = HealRestSiteOption.GetBaseHealAmount(creature);
        }
        catch { return null; }
        var nightTerrors = who.RunState.Modifiers.FirstOrDefault(modifierModel => modifierModel is NightTerrors);
        var pillow = who.Relics.OfType<RegalPillow>().FirstOrDefault();
        var humidifiers = who.Relics.OfType<StoneHumidifier>().ToList();
        // The boss fight's start: the relics mark themselves Active at the boss's parent map point (AfterRoomEntered).
        static bool IsBathRobes(RelicModel relic) => BathRobesIs?.Invoke(relic) == true;
        var bossRelics = who.Relics.Where(relic => relic.Status == RelicStatus.Active && (relic is Pantograph || (IsBathRobes(relic) && !relic.IsUsedUp))).ToList();
        var boss = bossRelics.Select(relic => IsBathRobes(relic) ? (RestStep.BathRobes, 0) : (RestStep.Pantograph, Sts2State.Var(relic, "Heal", fallback: 25))).ToList();
        var outcome = RestMath.Compute(creature.CurrentHp, creature.MaxHp, heal, pillow != null ? heal - baseHeal : 0,
            humidifiers.Select(stoneHumidifier => Sts2State.Var(stoneHumidifier, "MaxHp", fallback: 5)).ToList(), nightTerrors != null, boss, rest);

        var lines = new List<(Texture2D?, int)>();
        int humid = 0, bossAt = 0;
        foreach (var line in outcome.Lines)
        {
            Texture2D? icon = line.Step switch
            {
                RestStep.Rest => GameIcons.RestOption(mend ? "mend" : "heal"),
                RestStep.RegalPillow => pillow != null ? GameIcons.Relic(pillow) : null,
                RestStep.StoneHumidifier => GameIcons.Relic(humidifiers[Math.Min(humid++, humidifiers.Count - 1)]),
                RestStep.NightTerrors => nightTerrors != null ? GameIcons.Modifier(nightTerrors) : null,
                _ => GameIcons.Relic(bossRelics[Math.Min(bossAt++, bossRelics.Count - 1)]),
            };
            lines.Add((icon, line.Hp));
        }
        return new Preview(outcome, lines, bossRelics);
    }

    private int _restCheckFrames;
    private int? _bossPredicted;
    private int _bossFrames;
    private int _mendFrames;

    /// <summary>Outside a rest site: no options, one list for every frame (it's only read).</summary>
    private static readonly List<RestSiteOption> NoOptions = new();

    public void OnFrame(OverlayHost host)
    {
        var room = NRestSiteRoom.Instance;
        var me = RunMe();
        Check(me);
        CheckMend();
        var inRoom = room != null && GodotObject.IsInstanceValid(room) && room.IsVisibleInTree() && me != null;
        var options = inRoom ? LocalOptions() : NoOptions;

        // Yours: Rest on offer (not chosen yet, not disabled); otherwise only what the boss relics will do anyway.
        var canRest = options.Any(restSiteOption => restSiteOption is HealRestSiteOption && restSiteOption.IsEnabled && !Used.TryGetValue(restSiteOption, out _));
        var mine = inRoom ? Compute(me!, canRest) : null;
        var showMine = mine != null && (canRest || mine.BossRelics.Count > 0);
        _mine!.HasContent = showMine;
        if (showMine) Show(_minePanel!, mine!, canRest);

        // A teammate you hover while Mend is on offer: what Mend would leave them with.
        var canMend = options.Any(restSiteOption => restSiteOption is MendRestSiteOption && restSiteOption.IsEnabled && !Used.TryGetValue(restSiteOption, out _));
        _hoveredMate = inRoom && canMend ? HoveredMate(room!, me!) : null;
        var theirs = _hoveredMate != null ? Compute(_hoveredMate, rest: true, mend: true) : null;
        _mate!.HasContent = theirs != null;
        if (theirs != null) Show(_matePanel!, theirs, rest: true);
    }

    private static void Show(RestMathPanel panel, Preview preview, bool rest) =>
        panel.Show(preview.Outcome.Hp, preview.Lines, preview.Outcome.ResultHp, preview.Outcome.ResultMax, rest ? preview.Outcome.Over : 0);

    private static List<RestSiteOption> LocalOptions()
    {
        try { return RunManager.Instance.RestSiteSynchronizer.GetLocalOptions().ToList(); }
        catch { return new List<RestSiteOption>(); }
    }

    /// <summary>The teammate whose character at the fire is under the mouse (the game highlights it: NRestSiteCharacter.OnFocus).</summary>
    private static Player? HoveredMate(NRestSiteRoom room, Player me)
    {
        var mouse = room.GetViewport().GetMousePosition();
        foreach (var nRestSiteCharacter in room.characterAnims)
        {
            if (!GodotObject.IsInstanceValid(nRestSiteCharacter) || nRestSiteCharacter.Player == me || nRestSiteCharacter.Hitbox is not { } hit || !hit.IsVisibleInTree()) continue;
            if (Sts2State.ScreenRect(hit).HasPoint(mouse)) return nRestSiteCharacter.Player;
        }
        return null;
    }

    // ------------------------------------------------------------------ checks (audit)

    /// <summary>
    /// After resting: HP and max HP against the preview (once the heal and AfterRestSiteHeal settle). At the boss: HP once the
    /// fight has started against what the preview said for how you left the last rest site.
    /// </summary>
    private void Check(Player? me)
    {
        if (me == null) return;
        var creature = me.Creature;
        if (_pendingRest is { } want && ++_restCheckFrames >= 90)
        {
            var what = $"rest: HP {creature.CurrentHp}/{creature.MaxHp}, predicted {want.Hp}/{want.Max}";
            var ok = creature.CurrentHp == want.Hp && creature.MaxHp == want.Max;
            ModEntry.Log.Detail($"[audit] [rest] {what}{(ok ? ", matched" : "")}");
            Telemetry.Audits.Done(Telemetry.Audits.Rest, ok, "rest-heal");
            if (!ok) Sts2Mods.Core.Diagnostics.ErrorReporter.Instance?.Mismatch("rest", "HP after resting didn't match the preview", new[] { "rest-heal" }, what);
            _pendingRest = null;
        }
        if (_pendingRest == null) _restCheckFrames = 0;

        if (NRestSiteRoom.Instance is { } nRestSiteRoom && GodotObject.IsInstanceValid(nRestSiteRoom))
        {
            // How you leave the rest site (rested or not): with Pantograph or Bath Robes ready, the boss fight's start.
            var preview = Compute(me, rest: false);
            _bossPredicted = preview is { BossRelics.Count: > 0 } ? preview.Outcome.ResultHp : null;
            _bossFrames = 0;
            return;
        }
        if (_bossPredicted is not { } boss) return;
        if (!Sts2State.InCombat) return;
        if (me.RunState.CurrentRoom?.RoomType != RoomType.Boss)
        {
            // A fight that isn't the boss came first: the prediction was for the boss's start, so nothing to hold it to.
            Telemetry.Audits.Skipped(Telemetry.Audits.Rest, "boss start: another fight came first", "rest-boss");
            _bossPredicted = null;
            return;
        }
        // BeforeCombatStart has run by the time your first turn's cards are dealt; nothing hits you before you act.
        if (++_bossFrames < 45) return;
        var line = $"boss start: HP {creature.CurrentHp}/{creature.MaxHp}, predicted {boss}";
        ModEntry.Log.Detail($"[audit] [rest] {line}{(creature.CurrentHp == boss ? ", matched" : "")}");
        Telemetry.Audits.Done(Telemetry.Audits.Rest, creature.CurrentHp == boss, "rest-boss");
        if (creature.CurrentHp != boss)
            Sts2Mods.Core.Diagnostics.ErrorReporter.Instance?.Mismatch("rest", "HP at the boss fight's start didn't match the rest preview", new[] { "rest-boss" }, line);
        _bossPredicted = null;
    }

    /// <summary>
    /// After your Mend: the teammate whose HP or max HP changed first, once it settles (about a second and a half),
    /// against the preview for them. Leaving the rest site ends the watch.
    /// </summary>
    private void CheckMend()
    {
        if (_pendingMend is not { } pending) { _mendFrames = 0; return; }
        if (NRestSiteRoom.Instance is not { } room || !GodotObject.IsInstanceValid(room))
        {
            // Left the rest site before the Mend's heal settled on anyone: nothing seen to hold the preview to.
            Telemetry.Audits.Skipped(Telemetry.Audits.Rest, "mend: left the rest site before the heal landed", "rest-mend");
            _pendingMend = null;
            return;
        }
        var changed = pending.FirstOrDefault(entry => entry.Key.Creature.CurrentHp != entry.Value.Hp || entry.Key.Creature.MaxHp != entry.Value.Max);
        if (changed.Key == null) { _mendFrames = 0; return; }
        if (++_mendFrames < 90) return;
        var creature = changed.Key.Creature;
        var want = changed.Value;
        var what = $"mend: teammate HP {creature.CurrentHp}/{creature.MaxHp}, predicted {want.WantHp}/{want.WantMax}";
        var ok = creature.CurrentHp == want.WantHp && creature.MaxHp == want.WantMax;
        ModEntry.Log.Detail($"[audit] [rest] {what}{(ok ? ", matched" : "")}");
        Telemetry.Audits.Done(Telemetry.Audits.Rest, ok, "rest-mend");
        if (!ok) Sts2Mods.Core.Diagnostics.ErrorReporter.Instance?.Mismatch("rest", "A teammate's HP after Mend didn't match the preview", new[] { "rest-mend" }, what);
        _pendingMend = null;
    }

    private static Player? RunMe()
    {
        if (Sts2State.Me is { } me) return me;
        var state = Sts2State.Private<RunState?>(RunManager.Instance, "State", fallback: null);
        return state == null ? null : LocalContext.GetMe(state);
    }
}
