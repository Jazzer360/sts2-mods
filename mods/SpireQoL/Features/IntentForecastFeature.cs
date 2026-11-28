using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.MonsterMoves.Intents;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using SpireQoL.Sim;
using Sts2Mods.Core.Features;
using Sts2Mods.Core.Game;
using Sts2Mods.Core.Ui;

namespace SpireQoL.Features;

/// <summary>
/// Intent Forecast (decision #72): while you hover an enemy's intent, its next moves after that intent, stacked upward above it
/// (+1 nearest the intent).
/// Every turn its move rules make certain, then the first random pick with each move it can make and its chance; drawn with
/// the game's own intent nodes (NIntent: icon, animation, damage as things stand). Worked out from its move rules and the
/// moves it has made (IntentReader, IntentForecast); never from a roll. Nothing when its intent on screen is hidden.
/// Checked when each move is rolled (IntentForecastAudit). Spec: docs/FEATURES.md#intent-forecast.
/// </summary>
public sealed class IntentForecastFeature : IFeature, IFrameFeature
{
    private static readonly Color ChanceColor = new(1f, 0.9f, 0.6f);

    private readonly QolContext _ctx;
    private MovableWidget? _widget;
    private VBoxContainer? _rows;
    private Creature? _enemy;
    private string _key = "";
    /// <summary>The labels and power chips, each in a holder, moved every frame with the intents' bob.</summary>
    private readonly List<Control> _bobbing = new();
    /// <summary>
    /// Each row's spacers and what sits either side of its intents (the chance left, the power icons right): the narrower side
    /// gets a spacer, so the intents are centred over the intent on screen.
    /// </summary>
    private readonly List<(Control LeftSpacer, List<Control> Left, List<Control> Right, Control RightSpacer)> _balance = new();
    private const int RowGap = 6;
    /// <summary>Between a random pick's moves, side by side.</summary>
    private const int OptionGap = 20;
    /// <summary>Holders that only bob (a chance above its move), rather than sitting level with the intent icon beside them.</summary>
    private readonly HashSet<Control> _bobOnly = new();
    private float _bob;
    /// <summary>
    /// Each forecast intent with the powers in its corner and the block on a defend intent, placed again every frame (its layout settles after it's
    /// ready).
    /// </summary>
    private readonly List<(NIntent Node, List<MovePowers.Applied>? Powers, int? Block)> _corners = new();

    public IntentForecastFeature(QolContext ctx) => _ctx = ctx;

    public string Id => "intent-forecast";
    public string DisplayName => "Intent Forecast";
    public bool EnabledByDefault => true;

    public static readonly WidgetLayout DefaultLayout = new()
    {
        FollowGame = true, OffsetX = 0, OffsetY = 0,
        Anchor = ScreenAnchor.Left,
    };

    public void Apply(Harmony harmony) => IntentForecastAudit.Patch(harmony);

    public void Attach(OverlayHost host)
    {
        _widget = _ctx.Widget(Id, "Intent Forecast", DefaultLayout);
        _widget.Frameless = true;
        // Above the intent, the later turns higher up; beside it when there's no room above (an enemy near the top).
        _widget.AttachTo(() => IntentRect(_enemy), AttachSide.Above)
               .AttachTo(() => IntentRect(_enemy), AttachSide.RightOfBottom);
        // No backdrop: the intents float like the game's own (the labels carry an outline instead).
        _rows = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        _rows.AddThemeConstantOverride(GodotNames.Name("separation"), 2);
        _widget.Content.AddChild(_rows);
    }

    /// <summary>
    /// One read of the hovered enemy's forecast: its turns as shown, its moves' game states, and the projection of their hits (decision #304), searched
    /// on a worker (SimJob): while it's on its way every move is drawn as the game reads it now, and once it's over the key changes and the rows are
    /// built again with the projected hits.
    /// </summary>
    internal sealed class ForecastRead
    {
        public required List<ForecastTurn> Turns { get; init; }
        public required MoveStateMachineView View { get; init; }
        /// <summary>The forecast's moves and chances alone (what the game's rules give), the key's first part.</summary>
        public required string MovesKey { get; init; }
        /// <summary>The projection's search (null: none to make), and the enemy's simulator id in it.</summary>
        public SimJob? Job { get; init; }
        public int EnemyId { get; init; } = -1;
        /// <summary>The key Build is keyed by: the moves, with each move's projected hits once the search is over.</summary>
        public string Key { get; set; } = "";
        /// <summary>The key was made while the search was on its way: made again once it's over.</summary>
        public bool Pending { get; set; }

        /// <summary>Only the certain turns are shown (Show chances off: decision #122).</summary>
        public bool CertainOnly { get; init; }

        /// <summary>
        /// The turns shown: the projection's, once its search is over and it rolled this enemy's moves from its rules on the projected state (decision
        /// #309: a sleeper waking after its last stack), else as the game's lambdas answer now.
        /// </summary>
        public List<ForecastTurn> Shown { get; private set; } = new();

        /// <summary>A forecast move's hits on you as they'll land, or null: not known, or the search isn't over.</summary>
        public IReadOnlyList<int>? Projected(int turn, string move) => Job?.Projected?.Of(turn, EnemyId, move)?.Lands;

        /// <summary>The key from the moves shown and what's projected now, and whether the search is still on its way.</summary>
        public void MakeKey()
        {
            Pending = Job is { Ready.IsCompleted: false };
            var projected = Job is { Ready.IsCompletedSuccessfully: true } ? Job.Projected : null;
            var rolled = projected?.ForecastOf(EnemyId);
            // A roll the projection couldn't tell whose weights ask the board (decision #411) ends the forecast there: never the game's lambdas as they
            // answer now, before the moves that change what they read.
            Shown = rolled is { Count: > 0 } || rolled != null && projected!.UnknownRolls.Contains(EnemyId)
                ? (CertainOnly ? rolled.TakeWhile(forecastTurn => forecastTurn.Certain).ToList() : rolled) : Turns;
            Key = MovesKey + "|" + string.Join("/", Shown.Select((forecastTurn, turn) => string.Join(",", forecastTurn.Moves.Select(move =>
                move.Move + ":" + (Projected(turn, move.Move) is { } lands ? string.Join(".", lands) : "-")))));
        }

        /// <summary>
        /// The same forecast as <paramref name="other"/>: the same moves, and the same projected hits when both searches are over (one on its way and
        /// one over differ only in time, not in what they say).
        /// </summary>
        public static bool Same(ForecastRead? read, ForecastRead? other) =>
            read == null || other == null ? read == other
                : read.MovesKey == other.MovesKey && (read.Pending || other.Pending || read.Key == other.Key);
    }

    /// <summary>The hovered enemy's forecast as last read, what it was read for, and dev mode's check of it.</summary>
    private ForecastRead? _read;
    private Creature? _readFor;
    private int _readProbe, _readRevision;
    private bool _haveRead;
    private readonly SpireQoL.Combat.ReuseCheck _readReuse = new("Intent Forecast");

    /// <summary>
    /// The enemy's next turns, as shown (the certain ones only with chances off: decision #122), with the key Build is keyed by; null
    /// when there are none to show. Its projection (decision #304: each forecast move's hits on you as they'll land, with no plays of yours) starts
    /// from Incoming Damage's own state of the board (what's shown: without teammates' hands unless Count teammates' hands is on), read here on the
    /// game's thread, and is searched on a worker: the frame waits for it as for any search (SimWait), never past its budget. Not before the
    /// simulator's models are read (SimModels.Loaded: the frame never waits for them).
    /// </summary>
    private ForecastRead? Read(Creature enemy)
    {
        if (IntentReader.Forecast(enemy) is not { } forecast) return null;
        var (turns, view) = forecast;
        if (!_ctx.Settings.ShowIntentOdds) turns = turns.TakeWhile(forecastTurn => forecastTurn.Certain).ToList();
        if (turns.Count == 0) return null;
        var input = SimModels.Loaded && Sts2State.Me is { } me && Sts2State.Combat is { } combat
            ? SpireQoL.Combat.TurnEndModel.Compute() is { } forecastNow ? SimAdapter.ProjectionStart(me, combat, forecastNow.State, forecastNow.Ids)
                : SimAdapter.ProjectionStart(me, combat, teammateHands: SpireQoL.Combat.TurnEndModel.CountTeammateHands)
            : null;
        var job = input != null && input.Ids.TryGetValue(enemy, out _) ? SimAdapter.StartProjection(input, "the Intent Forecast's later turns") : null;
        if (job != null) SpireQoL.Combat.SimWait.Ready(job);
        var movesKey = enemy.GetHashCode() + "|" + string.Join("/", turns.Select(forecastTurn => string.Join(",", forecastTurn.Moves.Select(move => $"{move.Move}:{move.Chance:0.###}"))));
        var read = new ForecastRead { Turns = turns, View = view, MovesKey = movesKey, Job = job, EnemyId = job != null ? input!.Ids[enemy] : -1, CertainOnly = !_ctx.Settings.ShowIntentOdds };
        read.MakeKey();
        return read;
    }

    /// <summary>The width a row's side takes (each shown child and a gap), by index: every frame while shown.</summary>
    private static float Side(List<Control> controls)
    {
        var width = 0f;
        for (var i = 0; i < controls.Count; i++)
            if (GodotObject.IsInstanceValid(controls[i])) width += controls[i].CustomMinimumSize.X + RowGap;
        return width;
    }

    public void OnFrame(OverlayHost host)
    {
        var enemy = Sts2State.IsPlayerTurn ? HoveredIntentEnemy() : null;
        if (enemy != null)
        {
            // Read again only when the hovered enemy, the board (its moves, HP and powers: the probe) or the settings change: reading
            // its move rules every frame while you hovered it made garbage the whole time (decision #177).
            var probe = Sts2State.Me is { } me && Sts2State.Combat is { } combat ? SpireQoL.Combat.BoardProbe.Hash(me, combat) : 0;
            var reuse = _readReuse.Decide(_haveRead && ReferenceEquals(enemy, _readFor) && probe == _readProbe && QolSettings.Revision == _readRevision);
            if (reuse != SpireQoL.Combat.FrameReuse.Reuse)
            {
                var read = Read(enemy);
                if (reuse == SpireQoL.Combat.FrameReuse.WorkOutAndCheck) _readReuse.Compare(ForecastRead.Same(read, _read), "its forecast changed while the board probe didn't");
                if (_read?.Job is { Finished: false } replaced) replaced.Cancel(); // its board is gone: nobody reads it
                (_read, _readFor, _readProbe, _readRevision, _haveRead) = (read, enemy, probe, QolSettings.Revision, true);
            }
        }
        // The projection's search was on its way when the forecast was read: once it's over, the key takes its hits, and the rows are built again.
        if (_read is { Pending: true, Job.Ready.IsCompleted: true } finished)
        {
            finished.MakeKey();
            // The frame loop reports it, with the worker's stack kept; the rows stay as the game reads them.
            if (finished.Job!.Ready.IsFaulted) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(finished.Job.Ready.Exception!.GetBaseException()).Throw();
        }
        if (enemy == null || _read is not { } now)
        {
            _enemy = null;
            _key = "";
            _widget!.HasContent = false;
            return;
        }
        _enemy = enemy;
        if (now.Key != _key)
        {
            _key = now.Key;
            if (now.Shown.Count > 0) Build(enemy, now.Shown, now.View, now.Projected);
        }
        // The projection's rolls can leave nothing certain to show (Show chances off, a sleeper waking into a random pick).
        _widget!.HasContent = now.Shown.Count > 0;
        if (now.Shown.Count == 0) return;
        foreach (var (node, powers, block) in _corners)
            if (GodotObject.IsInstanceValid(node) && node.IsNodeReady()) Decorate(node, powers, block);
        // NIntent._Process's bob, for rows with no intent icon to follow.
        var offsetY = -(Mathf.Sin(Time.GetTicksMsec() * 0.001f * Mathf.Pi + _bob) * 10f + 8f);
        // The narrower side of each row's intents gets a spacer (each shown child adds its width and a gap): the intents sit centred.
        foreach (var (leftSpacer, left, right, rightSpacer) in _balance)
        {
            if (!GodotObject.IsInstanceValid(leftSpacer) || !GodotObject.IsInstanceValid(rightSpacer)) continue;
            var gap = Side(right) - Side(left);
            Pad(leftSpacer, gap);
            Pad(rightSpacer, -gap);
        }
        foreach (var control in _bobbing)
        {
            if (!GodotObject.IsInstanceValid(control) || control.GetParent() is not Control holder) continue;
            // Its size, known once it's on screen (a label's font): the holder takes it, so the row lays out around it.
            var size = control.GetCombinedMinimumSize();
            if (holder.CustomMinimumSize != size) holder.CustomMinimumSize = size;
            control.Size = size;
            // Level with the intent icon in its row as it is drawn this frame (bob included); else centred, then the bob.
            var top = !_bobOnly.Contains(control) && IconCentre(holder) is { } centre ? centre - size.Y / 2 : (holder.Size.Y - size.Y) / 2 + offsetY + 8f;
            control.Position = new Vector2(0, top);
        }
    }

    /// <summary>A spacer shown only when its side is short by <paramref name="amount"/>, as wide as that less its own gap.</summary>
    private static void Pad(Control spacer, float amount)
    {
        spacer.Visible = amount > 0.5f; // a hidden child takes no gap
        var width = Math.Max(0, amount - RowGap);
        if (spacer.Visible && !Mathf.IsEqualApprox(spacer.CustomMinimumSize.X, width)) spacer.CustomMinimumSize = new Vector2(width, 0);
    }

    /// <summary>The y, in the holder's own coordinates, of the centre of the intent icon drawn in its row (the NIntent's sprite).</summary>
    private static float? IconCentre(Control holder)
    {
        if (UiKit.FirstChild<NIntent>(holder.GetParent()) is not { } intent) return null;
        if (intent.GetNodeOrNull<Node2D>(GodotNames.Path("%Intent")) is not { } sprite) return null;
        return (holder.GetGlobalTransform().AffineInverse() * sprite.GlobalPosition).Y;
    }

    /// <summary>A control in a plain holder of its size (rows are containers, which would undo the bob), added to the bobbing list.</summary>
    private Control Bobbing(Control inner, bool followIcon = true)
    {
        if (!followIcon) _bobOnly.Add(inner);
        inner.MouseFilter = Control.MouseFilterEnum.Ignore;
        var holder = new Control { MouseFilter = Control.MouseFilterEnum.Ignore, CustomMinimumSize = inner.GetCombinedMinimumSize() };
        holder.AddChild(inner);
        inner.Size = holder.CustomMinimumSize;
        _bobbing.Add(inner);
        return holder;
    }

    private static Label Outlined(Label label)
    {
        label.AddThemeConstantOverride(GodotNames.Name("outline_size"), 6);
        label.AddThemeColorOverride(GodotNames.Name("font_outline_color"), new Color(0, 0, 0, 0.9f));
        return label;
    }

    private void Build(Creature enemy, List<ForecastTurn> turns, MoveStateMachineView view, Func<int, string, IReadOnlyList<int>?> projected)
    {
        UiKit.Clear(_rows!);
        _bobbing.Clear();
        _bobOnly.Clear();
        _corners.Clear();
        _balance.Clear();
        var targets = enemy.CombatState?.PlayerCreatures.ToList() ?? new List<Creature>();
        // The game's intents bob (NIntent._Process: a sine from _timeOffset); ours start in step with the intent on screen.
        var bob = _bob = BobOffset(enemy);
        for (var turnIndex = 0; turnIndex < turns.Count; turnIndex++)
        {
            var turn = turns[turnIndex];
            // One row per turn, each on top of the last, so the forecast reads upward from the intent (next turn just above it).
            var row = UiKit.Row(turn.Certain ? RowGap : OptionGap);
            row.Alignment = BoxContainer.AlignmentMode.Center;
            row.MouseFilter = Control.MouseFilterEnum.Ignore;
            // In the tree first: NIntent finds its parts in _Ready, and UpdateIntent before that throws.
            _rows!.AddChild(row);
            _rows.MoveChild(row, 0);
            if (turn.Certain)
            {
                // Its intents centred over the intent on screen, the power icons to their right (a spacer on the left balances them).
                var leftSpacer = new Control { MouseFilter = Control.MouseFilterEnum.Ignore };
                var rightSpacer = new Control { MouseFilter = Control.MouseFilterEnum.Ignore };
                row.AddChild(leftSpacer);
                var right = new List<Control>();
                AddMove(row, enemy, targets, bob, view, turn.Moves[0].Move, right, projected(turnIndex, turn.Moves[0].Move));
                row.AddChild(rightSpacer);
                _balance.Add((leftSpacer, new List<Control>(), right, rightSpacer));
                continue;
            }
            // A random pick: its moves side by side, each with its chance above it.
            foreach (var (moveId, chance) in turn.Moves)
            {
                var column = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore, Alignment = BoxContainer.AlignmentMode.Center };
                column.AddThemeConstantOverride(GodotNames.Name("separation"), 0);
                row.AddChild(column);
                var pct = Outlined(UiKit.Label($"{Math.Round(chance * 100)}%", size: 18, ChanceColor));
                pct.HorizontalAlignment = HorizontalAlignment.Center;
                var holder = Bobbing(pct, followIcon: false);
                holder.SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter;
                column.AddChild(holder);
                var line = UiKit.Row(RowGap);
                line.Alignment = BoxContainer.AlignmentMode.Center;
                line.MouseFilter = Control.MouseFilterEnum.Ignore;
                column.AddChild(line);
                AddMove(line, enemy, targets, bob, view, moveId, new List<Control>(), projected(turnIndex, moveId));
            }
        }
    }

    /// <summary>
    /// A move's intents (the game's own nodes, bobbing in step with the intent on screen) and the powers it applies. Each attack intent with its hits
    /// projected (<paramref name="projected"/>: each hit on you as it'll land, in order, decision #304) is drawn with that hit, when all of its hits
    /// agree (ProjectedAttackIntent); else as the game reads it now.
    /// </summary>
    private void AddMove(HBoxContainer line, Creature enemy, List<Creature> targets, float bob, MoveStateMachineView view, string moveId, List<Control> right,
        IReadOnlyList<int>? projected)
    {
        if (view.Move(moveId) is not { } move) return;
        // The powers the move applies (Frail 2), from its code, each in the corner of the buff or debuff intent it belongs to
        // (IntentCorner, as on the intent on screen: decision #99).
        var corners = enemy.Monster is { } monster ? IntentCorner.Assign(move.Intents, MovePowers.Of(monster, move, now: false)) : new();
        // The block it gains, on its defend intent as on the intent on screen (Intent Details): the move's own amounts, which an enemy's block
        // math leaves as they are (decision #399).
        var block = enemy.Monster is { } blocker && move.Intents.Any(abstractIntent => abstractIntent is DefendIntent) ? MovePowers.BlockOf(blocker, move, now: false) : null;
        var firstHit = 0;
        for (var intentIndex = 0; intentIndex < move.Intents.Count; intentIndex++)
        {
            var intent = move.Intents[intentIndex];
            if (intent is AttackIntent attack)
            {
                var hits = Math.Max(1, attack.Repeats);
                if (projected != null && firstHit + hits <= projected.Count && Enumerable.Range(firstHit, hits).All(hit => projected[hit] == projected[firstHit]))
                    intent = new ProjectedAttackIntent(attack, projected[firstHit]);
                firstHit += hits;
            }
            var powers = corners.TryGetValue(intentIndex, out var mine) ? mine : null;
            var blocks = intent is DefendIntent && block is > 0 ? block : null;
            var node = NIntent.Create(bob);
            node.MouseFilter = Control.MouseFilterEnum.Ignore;
            line.AddChild(node);
            if (powers != null || blocks != null) _corners.Add((node, powers, blocks));
            void Show()
            {
                node.UpdateIntent(intent, targets, enemy);
                Decorate(node, powers, blocks);
            }
            if (node.IsNodeReady()) Show();
            else node.Ready += Show;
        }
    }

    /// <summary>A forecast intent's block number, then its power chips (in that order, as on the intent on screen).</summary>
    private static void Decorate(NIntent node, List<MovePowers.Applied>? powers, int? block)
    {
        if (block != null && node.GetNodeOrNull<Control>(GodotNames.Path("%IntentHolder")) is { } holder) IntentDetailsFeature.Block(holder, node, block);
        IntentCorner.Place(node, powers);
    }

    /// <summary>The bob timing (NIntent._timeOffset) of the enemy's first intent on screen, or 0.</summary>
    private static float BobOffset(Creature enemy)
    {
        var node = Sts2State.NodeOf(enemy);
        var first = UiKit.FirstChild<NIntent>(node?.IntentContainer);
        return first != null ? Sts2State.Private(first, "_timeOffset", fallback: 0f) : 0f;
    }

    /// <summary>The living enemy whose intent is under the mouse.</summary>
    private static Creature? HoveredIntentEnemy()
    {
        if (NCombatRoom.Instance is not { } room) return null;
        var mouse = room.GetViewport().GetMousePosition();
        foreach (var creatureNode in Sts2State.CreatureNodesOf(room))
        {
            if (!GodotObject.IsInstanceValid(creatureNode) || creatureNode.Entity is not { IsEnemy: true, IsAlive: true } creature) continue;
            if (IntentRect(creature) is { } rect && rect.Grow(6).HasPoint(mouse)) return creature;
        }
        return null;
    }

    /// <summary>
    /// Screen rect of an enemy's intents on screen (the intent icons themselves, joined: the container doesn't always size to
    /// them), or null.
    /// </summary>
    private static Rect2? IntentRect(Creature? enemy)
    {
        if (enemy == null || NCombatRoom.Instance == null) return null;
        var node = Sts2State.NodeOf(enemy);
        var intents = node?.IntentContainer;
        if (intents == null || !GodotObject.IsInstanceValid(intents) || !intents.IsVisibleInTree()) return null;
        Rect2? rect = null;
        for (var i = 0; i < intents.GetChildCount(); i++)
        {
            if (intents.GetChild(i) is not NIntent nIntent || !nIntent.IsVisibleInTree() || nIntent.Size.X < 1) continue;
            var otherRect = Sts2State.ScreenRect(nIntent);
            rect = rect is { } nextRect ? nextRect.Merge(otherRect) : otherRect;
        }
        if (rect == null && intents.Size.X >= 1) rect = Sts2State.ScreenRect(intents);
        return rect;
    }
}
