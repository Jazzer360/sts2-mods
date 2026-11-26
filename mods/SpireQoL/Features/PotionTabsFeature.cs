using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Combat.History.Entries;
using MegaCrit.Sts2.Core.Models;
using SpireQoL.Combat;
using SpireQoL.Sim;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Potions;
using SpireQoL.Cards;
using Sts2Mods.Core.Features;
using Sts2Mods.Core.Game;
using Sts2Mods.Core.Ui;

namespace SpireQoL.Features;

/// <summary>
/// Potion Tags (docs/FEATURES.md "Potion Tags"): in fights, the on-card tag's design hanging under each potion in the top
/// bar, point down like a banner, with the icon of a relic that drinking that potion sets off. Keys "potion:&lt;Relic&gt;".
///  - A relic of yours whose code listens to a potion's use (Hook.BeforePotionUsed, AfterPotionUsed: Reptile Trinket, Belt Buckle), where the
///    potion's simulated use shows it acting (decision #318: Sim/PotionTabs, from the use's log as a card's proc tabs are): the use alone searched
///    on a worker (SimSearch.PlayOnly, decision #107) once per potion, aim and board, never per frame.
///  - White Beast Statue: every potion while your slots are full (a potion reward comes after every fight:
///    ShouldForcePotionReward; with no room it's lost): outside the fight, so not the use's.
/// The tabs are copies of a hand card's enchantment tab (the same art as the card tabs). Check: after you drink a potion, which of those
/// relics flashed (the game showing it acted) against the tabs it had.
/// </summary>
public sealed class PotionTabsFeature : IFeature, IFrameFeature
{
    private const string TabName = "SpireQoL_PotionTab";
    private const float TabOverlap = 0.2f; // fraction of the tag length tucked under the potion
    private const float IconScale = 0.85f;
    private static readonly Color SlashColor = new(1f, 0.15f, 0.1f, 0.95f);

    private readonly QolContext _ctx;
    /// <summary>A copy of a card's enchantment tab, kept to make potion tabs from (null until a card is seen).</summary>
    private Control? _proto;
    private readonly Dictionary<ulong, List<Control>> _tabs = new();
    /// <summary>
    /// Each potion's tabs on the last frame it was on the belt (the check reads them once it's drunk). Kept after it leaves the
    /// belt: it does before the game logs it as used. Cleared out of combat.
    /// </summary>
    private readonly Dictionary<PotionModel, List<string>> _last = new(ReferenceEqualityComparer.Instance);
    private int _historySeen = -1;
    private Pending? _pending;

    /// <summary>A potion's tags as a pending check holds them: the tabs it had, each potion-listening relic's flashes as of last frame.</summary>
    private sealed record Pending(PotionModel Potion, List<string> Tabs, Dictionary<RelicModel, int> Flashes, int Frames);

    /// <summary>
    /// A relic's flashes this fight (RelicModel.Flashed), and as of last frame: the baseline for a potion drunk since (its relic may already have
    /// flashed by the frame the game's log shows the potion used). A class, so a frame updates it in place.
    /// </summary>
    private sealed class FlashCount
    {
        public int Now;
        public int LastFrame;
    }

    /// <summary>Your relics that listen to a potion's use (PotionTabs.ListensToPotions), each with its flashes, subscribed while in a fight.</summary>
    private readonly Dictionary<RelicModel, FlashCount> _flashes = new(ReferenceEqualityComparer.Instance);

    /// <summary>A potion's use searched for its tags: the board's key and the aim it was built for, and its search.</summary>
    private sealed record Use(long StateKey, int Aim, SimJob Job);

    private readonly Dictionary<PotionModel, Use> _uses = new(ReferenceEqualityComparer.Instance);

    /// <summary>
    /// The tags the feature decides on the board, not from a potion's use (outside the fight: White Beast Statue's potion reward with your slots
    /// full): the catalog check holds the potion rows to these and to the relics listening to a potion's use (decision #318).
    /// </summary>
    internal static readonly IReadOnlySet<string> BoardRules = new HashSet<string>(StringComparer.Ordinal) { nameof(WhiteBeastStatue) };

    public PotionTabsFeature(QolContext ctx) => _ctx = ctx;

    public string Id => "potion-tabs";
    public string DisplayName => "Potion Tags";
    public bool EnabledByDefault => true;

    public void Apply(Harmony harmony)
    {
        // Frame-driven; no patches.
    }

    public void Attach(OverlayHost host)
    {
    }

    /// <summary>The potion slots (NPotionContainer.Holders): beta only (decision #163).</summary>
    [BetaOnly, System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static IReadOnlyList<NPotionHolder> HoldersOf(NPotionContainer container) => container.Holders;

    private static readonly Func<NPotionContainer, IReadOnlyList<NPotionHolder>>? Holders = BetaOnly.Gate<Func<NPotionContainer, IReadOnlyList<NPotionHolder>>>(HoldersOf);

    /// <summary>Main's potion slots: its private list (main has no Holders).</summary>
    private static readonly System.Reflection.FieldInfo? MainHoldersField = AccessTools.Field(typeof(NPotionContainer), "_holders");

    /// <summary>What the tabs were last drawn for (OnFrame): drawn again only when it changes.</summary>
    private int _drawnFor;

    public void OnFrame(OverlayHost host)
    {
        var me = Sts2State.Me;
        // Only in a fight, where there's a hand to copy from: looking every frame before the first fight made garbage on the menu.
        if (_proto == null && Sts2State.InCombat) TakeProto();
        Check(me);
        // The same potions in the same slots, the same board (settings among it) and container as last frame: the tabs stand as drawn.
        var drawnFor = new HashCode();
        drawnFor.Add(host.HideAll);
        drawnFor.Add(Sts2State.InCombat);
        drawnFor.Add(NRun.Instance?.GlobalUi?.TopBar?.PotionContainer is { } shownContainer && GodotObject.IsInstanceValid(shownContainer) ? shownContainer.GetInstanceId() : 0);
        if (me != null)
        {
            if (Sts2State.Combat is { } combat) drawnFor.Add(SpireQoL.Combat.BoardProbe.Hash(me, combat));
            var slots = me.PotionSlots;
            for (var i = 0; i < slots.Count; i++) drawnFor.Add(slots[i] == null ? 0 : System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(slots[i]));
        }
        // A use's search finishing draws its tags (each is started on a board change, never per frame).
        foreach (var use in _uses.Values) drawnFor.Add(use.Job.Finished);
        if (drawnFor.ToHashCode() == _drawnFor) return;
        _drawnFor = drawnFor.ToHashCode();
        Draw(me, host);
    }

    /// <summary>Every holder's tabs, drawn again: only when the potions, the slots or the board changed (its own method: decision #177).</summary>
    private void Draw(MegaCrit.Sts2.Core.Entities.Players.Player? me, OverlayHost host)
    {
        // Read only when drawing: the holders' list is made fresh on each read.
        var holders = NRun.Instance?.GlobalUi?.TopBar?.PotionContainer is { } container
            ? Holders?.Invoke(container) ?? MainHoldersField?.GetValue(container) as IReadOnlyList<NPotionHolder>
            : null;
        var seen = new HashSet<ulong>();
        var onBelt = new HashSet<PotionModel>(ReferenceEqualityComparer.Instance);
        if (me != null && holders != null && Sts2State.InCombat && Sts2State.Combat is { } combat && !host.HideAll)
        {
            var full = !me.HasOpenPotionSlots;
            // Your relics whose code listens to a potion's use: what its simulated use's log can show acting (decision #318).
            var listening = new List<string>();
            foreach (var relic in me.Relics)
                if (SimModels.Loaded && SimModels.Get(SimReaders.ModelName(relic)) is { } model && PotionTabs.ListensToPotions(model))
                {
                    listening.Add(SimReaders.ModelName(relic));
                    if (!_flashes.ContainsKey(relic))
                    {
                        _flashes[relic] = new FlashCount();
                        relic.Flashed += OnRelicFlashed;
                    }
                }
            var start = listening.Count > 0 ? TurnEndModel.Begin() : null;
            foreach (var holder in holders)
            {
                if (!GodotObject.IsInstanceValid(holder) || holder.Potion is not { } node || !GodotObject.IsInstanceValid(node)) continue;
                PotionModel? potion;
                try { potion = node.Model; } catch { continue; }
                onBelt.Add(potion);
                var setOff = start != null ? SetOffBy(potion, me, combat, start, listening) : null;
                var specs = new List<(string Key, RelicModel Relic, bool Crossed)>();
                foreach (var relic in me.Relics)
                {
                    if (relic is WhiteBeastStatue)
                    {
                        if (full) specs.Add(("potion:" + relic.GetType().Name, relic, false));
                        continue;
                    }
                    if (setOff == null) continue;
                    var name = SimReaders.ModelName(relic);
                    foreach (var (model, crossed) in setOff)
                        if (model == name) specs.Add(("potion:" + relic.GetType().Name, relic, crossed));
                }
                _last[potion] = specs.Select(spec => spec.Key).ToList();
                var shown = specs.Where(spec => _ctx.Settings.TabEnabled(spec.Key) && (spec.Crossed ? _ctx.Settings.CrossedTabs : _ctx.Settings.PlainTabs)).ToList();
                seen.Add(holder.GetInstanceId());
                Show(holder, shown);
            }
        }
        // Holders no longer showing a potion (or out of combat): hide their tabs.
        foreach (var (id, tabs) in _tabs)
        {
            if (seen.Contains(id)) continue;
            foreach (var control in tabs) if (GodotObject.IsInstanceValid(control)) control.Visible = false;
        }
        // A potion no longer on your belt: its search isn't wanted.
        foreach (var potion in _uses.Keys.Where(potion => !onBelt.Contains(potion)).ToList())
        {
            _uses[potion].Job.Cancel();
            _uses.Remove(potion);
        }
    }

    /// <summary>
    /// The relics of <paramref name="listening"/> the potion's use sets off (decision #318), from its use alone searched on a worker (SimJob): started
    /// once for the potion, its aim and the board (a board change, never a frame), waited for out of the frame's shared budget on the first frame
    /// only; null until it's done.
    /// </summary>
    private List<(string Model, bool Crossed)>? SetOffBy(PotionModel potion, MegaCrit.Sts2.Core.Entities.Players.Player me, ICombatState combat,
        TurnEndModel.ForecastStart start, List<string> listening)
    {
        // Its aim as the game would take it unaimed: an enemy you can hit for one aimed at an enemy (what sets these relics off doesn't depend on
        // which), else you when you're a valid target, else none.
        var target = potion.TargetType is MegaCrit.Sts2.Core.Entities.Cards.TargetType.AnyEnemy ? (combat.HittableEnemies.Count > 0 ? combat.HittableEnemies[0] : null)
            : potion.IsValidTarget(me.Creature) ? me.Creature : null;
        var aim = SimAdapter.AimOf(target, me, combat, start.Ids);
        if (!_uses.TryGetValue(potion, out var use) || use.StateKey != start.StateKey || use.Aim != aim)
        {
            use?.Job.Cancel();
            // A use that auto-plays your draw pile's top (Distilled Chaos) from its own read of the board, as its preview (decision #338).
            var own = SimAdapter.PlaysFromDrawPile(potion, aim) ? TurnEndModel.WithPileAutoPlays(start) ?? start : start;
            var play = SimAdapter.Potion(potion, me, combat, own.Ids, aim);
            _uses[potion] = use = new Use(start.StateKey, aim, SimJob.Start(SimSearch.PlayOnly(own.State, play), $"{potion.GetType().Name} (potion tags)"));
        }
        if (!use.Job.Waited) SimWait.Ready(use.Job);
        return use.Job.Latest is { } outcome ? PotionTabs.SetOff(outcome, listening) : null;
    }

    private void OnRelicFlashed(RelicModel relic, IEnumerable<MegaCrit.Sts2.Core.Entities.Creatures.Creature> _)
    {
        if (_flashes.TryGetValue(relic, out var count)) count.Now++;
    }

    /// <summary>A copy of a hand card's enchantment tab (the card tabs' art), once.</summary>
    private readonly List<(MegaCrit.Sts2.Core.Nodes.Cards.NCard Node, MegaCrit.Sts2.Core.Models.CardModel Card)> _hand = new();

    private void TakeProto()
    {
        Sts2State.HandNodes(_hand);
        foreach (var (node, _) in _hand)
        {
            if (node.EnchantmentTab is { } control && GodotObject.IsInstanceValid(control))
            {
                _proto = (Control)control.Duplicate();
                return;
            }
        }
    }

    private void Show(NPotionHolder holder, List<(string Key, RelicModel Relic, bool Crossed)> specs)
    {
        var id = holder.GetInstanceId();
        if (!_tabs.TryGetValue(id, out var tabs)) _tabs[id] = tabs = new List<Control>();
        tabs.RemoveAll(control => !GodotObject.IsInstanceValid(control));
        while (tabs.Count < specs.Count && _proto != null) tabs.Add(MakeTab(holder));
        if (_proto == null) return;

        // The tab art is long along x and thin along y; turned a quarter turn counter-clockwise, its outer end (x = 0, the
        // point) hangs down and its inner end tucks under the potion. Sized to the potion slot.
        var size = _proto.Size;
        var natural = size.Y * _proto.Scale.Y;
        var fit = Math.Min(1f, holder.Size.X * 0.55f / Math.Max(1f, natural));
        var scale = _proto.Scale * fit;
        var tagW = size.Y * scale.Y;
        var tagH = size.X * scale.X;
        const float gap = 2f;
        var step = tagW + gap;
        var width = specs.Count * tagW + Math.Max(0, specs.Count - 1) * gap;
        if (width > holder.Size.X && specs.Count > 1)
        {
            step = Math.Max(0f, (holder.Size.X - tagW) / (specs.Count - 1));
            width = step * (specs.Count - 1) + tagW;
        }
        var left = (holder.Size.X - width) / 2f;
        var top = holder.Size.Y - tagH * TabOverlap; // the tucked end starts just above the slot's bottom edge

        for (var i = 0; i < tabs.Count; i++)
        {
            var tab = tabs[i];
            tab.Visible = i < specs.Count;
            if (!tab.Visible) continue;
            var (_, relic, crossed) = specs[i];
            // Turning -90° about the top-left puts the visual box at y ∈ [-length, 0]: shift down by the length.
            tab.PivotOffset = Vector2.Zero;
            tab.Rotation = -Mathf.Pi / 2f;
            tab.Scale = scale;
            tab.Position = new Vector2(left + i * step, top + tagH);
            tab.Modulate = Colors.White;
            tab.SelfModulate = Colors.White;
            if (tab.GetNodeOrNull<Label>(GodotNames.Path("Label")) is { } label) label.Visible = false;
            if (tab.GetNodeOrNull<TextureRect>(GodotNames.Path("Icon")) is { } icon)
            {
                // Upright and centered in the part that hangs below the potion (x ∈ [0, length × (1 − overlap)]).
                var center = new Vector2(tab.Size.X * (1f - TabOverlap) / 2f, tab.Size.Y / 2f);
                icon.PivotOffset = icon.Size / 2f;
                icon.Rotation = Mathf.Pi / 2f;
                icon.Scale = new Vector2(IconScale, IconScale);
                icon.Position = center - icon.Size / 2f;
                Texture2D? tex = null;
                try { tex = relic.Icon; } catch { /* missing art */ }
                icon.Texture = GameIcons.Valid(tex) ? tex : null;
                icon.UseParentMaterial = false;
                icon.Modulate = crossed ? new Color(1, 1, 1, 0.8f) : Colors.White;
                if (icon.GetNodeOrNull<ColorRect>(GodotNames.Path("SpireQoL_Slash")) is { } slash)
                {
                    slash.Visible = crossed;
                    if (crossed) CardOverlayRenderer.LayoutSlash(slash, icon.Size);
                }
            }
        }
    }

    private Control MakeTab(NPotionHolder holder)
    {
        var tab = (Control)_proto!.Duplicate();
        tab.Name = GodotNames.Name(TabName);
        ClearUniqueNames(tab);
        tab.MouseFilter = Control.MouseFilterEnum.Ignore;
        if (tab.Material is ShaderMaterial shaderMaterial)
        {
            var own = (ShaderMaterial)shaderMaterial.Duplicate();
            own.SetShaderParameter(GodotNames.Name("h"), 0.25);
            own.SetShaderParameter(GodotNames.Name("s"), 0.4);
            own.SetShaderParameter(GodotNames.Name("v"), 0.6);
            tab.Material = own;
        }
        if (tab.GetNodeOrNull<TextureRect>(GodotNames.Path("Icon")) is { } icon)
        {
            icon.AddChild(new ColorRect { Name = GodotNames.Name("SpireQoL_Slash"), Color = SlashColor, MouseFilter = Control.MouseFilterEnum.Ignore, Visible = false });
        }
        holder.AddChild(tab);
        holder.MoveChild(tab, 0); // drawn first: the potion covers the tucked end
        return tab;
    }

    private static void ClearUniqueNames(Node node)
    {
        node.UniqueNameInOwner = false;
        foreach (var child in node.GetChildren()) ClearUniqueNames(child);
    }

    // ------------------------------------------------------------------ check

    /// <summary>
    /// A potion you drank (a new PotionUsedEntry of yours): a second later, which of your potion-listening relics flashed since the frame
    /// before (the game showing it acted) against the tabs it had on its last frame.
    /// </summary>
    private void Check(MegaCrit.Sts2.Core.Entities.Players.Player? me)
    {
        if (_pending is { } pending)
        {
            if (pending.Frames < 60) _pending = pending with { Frames = pending.Frames + 1 };
            else
            {
                _pending = null;
                Compare(pending);
            }
        }
        var entries = CombatManager.Instance?.History?.Entries;
        if (me == null || entries == null || !Sts2State.InCombat)
        {
            _historySeen = -1;
            _last.Clear();
            if (_flashes.Count > 0)
            {
                foreach (var relic in _flashes.Keys) relic.Flashed -= OnRelicFlashed;
                _flashes.Clear();
            }
            return;
        }
        // The game's own list when it is one (it is): copying the whole fight's history every frame was garbage that grew all fight.
        var list = entries as IReadOnlyList<MegaCrit.Sts2.Core.Combat.History.CombatHistoryEntry> ?? entries.ToList();
        if (_historySeen < 0 || _historySeen > list.Count) _historySeen = list.Count;
        for (var i = _historySeen; i < list.Count; i++)
        {
            if (list[i] is not PotionUsedEntry used || used.Actor != me.Creature) continue;
            var tabs = _last.Remove(used.Potion, out var previous) ? previous : new List<string>();
            var flashes = new Dictionary<RelicModel, int>(ReferenceEqualityComparer.Instance);
            foreach (var (relic, count) in _flashes) flashes[relic] = count.LastFrame;
            _pending = new Pending(used.Potion, tabs, flashes, Frames: 0);
        }
        _historySeen = list.Count;
        foreach (var count in _flashes.Values) count.LastFrame = count.Now;
    }

    private void Compare(Pending pending)
    {
        var problems = new List<(string Key, string Text)>();
        foreach (var (relic, before) in pending.Flashes)
        {
            if (!_flashes.TryGetValue(relic, out var count)) continue;
            var name = relic.GetType().Name;
            var flashed = count.Now > before;
            var predicted = pending.Tabs.Contains("potion:" + name);
            if (flashed != predicted) problems.Add((name, $"{name}: {(flashed ? "flashed" : "didn't flash")}, tab {(predicted ? "shown" : "not shown")}"));
        }
        var what = $"potion {pending.Potion.GetType().Name}: tabs [{string.Join(", ", pending.Tabs)}]";
        ModEntry.Log.Detail($"[audit] [potion] {what}{(problems.Count == 0 ? ", matched" : "; " + string.Join("; ", problems.Select(problem => problem.Text)))}");
        Telemetry.Audits.Done(Telemetry.Audits.Potion, problems.Count == 0, problems.Count == 0 ? pending.Potion.GetType().Name : problems[0].Key);
        if (problems.Count > 0)
        {
            Sts2Mods.Core.Diagnostics.ErrorReporter.Instance?.Mismatch("potion", $"Potion tabs were wrong: {string.Join(", ", problems.Select(problem => problem.Key))}",
                new[] { "potion-tabs" }.Concat(problems.Select(problem => problem.Key)), what + "\n" + string.Join("\n", problems.Select(problem => problem.Text)));
        }
    }
}
