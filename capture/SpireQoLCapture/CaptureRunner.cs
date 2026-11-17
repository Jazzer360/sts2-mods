using System.Text.Json;
using Godot;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Debug;
using MegaCrit.Sts2.Core.DevConsole;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Rewards;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Settings;
using MegaCrit.Sts2.Core.Unlocks;
using GodotPath = System.IO.Path;

namespace SpireQoLCapture;

/// <summary>
/// Runs every scene file in the scenes folder (in name order), each in a new run that is never saved
/// (<c>shouldSave: false</c>: no run save, run history, progress, achievements or metrics), and writes
/// the screenshots plus <c>results.json</c> to the output folder. Then quits the game.
///
/// Lines are either one of the directives below or a dev console command (fight, relic add, power,
/// potion, energy, block, damage, enchant, event, room...), run as the local player.
/// </summary>
public sealed class CaptureRunner
{
    private static readonly TimeSpan CommandTimeout = TimeSpan.FromSeconds(45);
    private static readonly TimeSpan WaitTimeout = TimeSpan.FromSeconds(60);

    private readonly string _scenesDir;
    private readonly string _outDir;
    private readonly string? _only;
    private readonly List<ShotResult> _shots = new();
    private readonly List<SceneResult> _scenes = new();
    private StreamWriter? _log;
    private DevConsole _console = null!;
    private string _caption = "";

    public CaptureRunner(string scenesDir, string outDir, string? only)
    {
        _scenesDir = scenesDir;
        _outDir = outDir;
        _only = only;
    }

    private static SceneTree Tree => (SceneTree)Engine.GetMainLoop();

    public async Task RunAsync()
    {
        var exitCode = 1;
        try
        {
            Directory.CreateDirectory(_outDir);
            Directory.CreateDirectory(GodotPath.Combine(_outDir, "full"));
            _log = new StreamWriter(GodotPath.Combine(_outDir, "capture.log"), append: false) { AutoFlush = true };
            Log($"scenes: {_scenesDir}; output: {_outDir}");

            await Until(() => NGame.Instance != null, "game start");
            await NGame.Instance!.GameStartupComplete.WaitAsync(TimeSpan.FromSeconds(120));
            await Frames(30);
            Preflight();

            var files = Directory.GetFiles(_scenesDir, "*.scene").OrderBy(f => f, StringComparer.Ordinal).ToList();
            if (_only != null)
            {
                var wanted = _only.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                files = files.Where(f => wanted.Any(w => GodotPath.GetFileNameWithoutExtension(f).Contains(w, StringComparison.OrdinalIgnoreCase))).ToList();
            }
            foreach (var file in files)
                await RunSceneFile(file);

            exitCode = _scenes.All(s => s.Ok) ? 0 : 2;
        }
        catch (Exception ex)
        {
            Log($"FATAL {ex}");
        }
        finally
        {
            try
            {
                var results = new { scenes = _scenes, shots = _shots };
                File.WriteAllText(GodotPath.Combine(_outDir, "results.json"),
                    JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
            }
            catch (Exception ex) { Log($"couldn't write results.json: {ex.Message}"); }
            Log($"done, exit code {exitCode}");
            _log?.Dispose();
            Tree.Quit(exitCode);
        }
    }

    /// <summary>
    /// Everything here lands in this capture's own save folder (the game runs with --force-steam=off, so its
    /// saves live under user://default/, apart from the Steam profile).
    /// </summary>
    private void Preflight()
    {
        SaveManager.Instance.PrefsSave.FastMode = FastModeType.Fast;
        SaveManager.Instance.SetFtuesEnabled(enabled: false);
        // The main menu shows the Early Access note as a popup until it's been seen, and it stays up over the next run.
        SaveManager.Instance.SettingsSave.SeenEaDisclaimer = true;
        SaveManager.Instance.SaveSettings();
        _console = new DevConsole(shouldAllowDebugCommands: true);
        var unlock = _console.ProcessCommand("unlock all");
        Log($"unlock all: {unlock.success} {unlock.msg}");
        NModalContainer.Instance?.Clear();
    }

    private async Task RunSceneFile(string file)
    {
        Scene scene;
        try { scene = Scene.Parse(file); }
        catch (Exception ex)
        {
            _scenes.Add(new SceneResult(GodotPath.GetFileNameWithoutExtension(file), "", "", false, $"parse: {ex.Message}"));
            return;
        }
        Log($"=== scene {scene.Name}: {scene.Title}");
        _caption = "";
        try
        {
            await LeaveRun();
            if (!scene.NoRun) await StartRun(scene);
            foreach (var line in scene.Lines)
            {
                Log($"  {line.Number}: {line.Text}");
                try { await Execute(scene, line); }
                catch (Exception ex) { throw new Exception($"line {line.Number} '{line.Text}': {ex.Message}", ex); }
            }
            _scenes.Add(new SceneResult(scene.Name, scene.Title, scene.Section, true, null));
        }
        catch (Exception ex)
        {
            Log($"  FAILED {ex.Message}");
            _scenes.Add(new SceneResult(scene.Name, scene.Title, scene.Section, false, ex.Message));
            NModalContainer.Instance?.Clear();
        }
    }

    private static async Task LeaveRun()
    {
        if (!RunManager.Instance.IsInProgress) return;
        // Leave the way a player does, through the pause menu: while a screen covers combat, Spire QoL takes
        // its widgets back out of the combat UI, which is freed with the run.
        NModalContainer.Instance?.Clear();
        await Key("Escape");
        await Seconds(0.5);
        // Leaving through the main menu ends nothing: the run was never saved, so no history entry is made.
        await NGame.Instance!.ReturnToMainMenu();
        await Frames(10);
    }

    /// <summary>Moves the pointer to empty background (below the top bar, between the fighters), so nothing is hovered.</summary>
    private static async Task ParkMouse()
    {
        var size = (Vector2)DisplayServer.WindowGetSize();
        MoveMouse(new Vector2(size.X * 0.5f, size.Y * 0.2f));
        await Frames(3);
    }

    private async Task StartRun(Scene scene)
    {
        var id = scene.Character.Trim().ToUpperInvariant();
        var character = ModelDb.AllCharacters.FirstOrDefault(c => c.Id.Entry == id)
                        ?? throw new Exception($"no character '{scene.Character}' (have: {string.Join(", ", ModelDb.AllCharacters.Select(c => c.Id.Entry))})");
        var player = Player.CreateForNewRun(character, new UnlockState(SaveManager.Instance.Progress), 1uL);
        var seed = scene.Seed ?? SeedHelper.GetRandomSeed();
        var runState = await NGame.Instance!.StartNewSingleplayerRun(player, shouldSave: false, ActModel.GetDefaultList(),
            Array.Empty<ModifierModel>(), seed, GameMode.Standard, isCustomGame: true).WaitAsync(TimeSpan.FromSeconds(120));
        await Until(() => runState.CurrentRoom != null && runState.CurrentRoom.RoomType != RoomType.Unassigned, "room assigned");
        await Frames(10);
        NModalContainer.Instance?.Clear();
        await ParkMouse();
        Log($"  run started: {character.Id.Entry}, seed {seed}");
    }

    private static Player Me => LocalContext.GetMe(RunManager.Instance.DebugOnlyGetState()) ?? throw new Exception("no local player (is a run in progress?)");

    private async Task Execute(Scene scene, Scene.Line line)
    {
        var t = line.Tokens;
        switch (line.Verb)
        {
            case "wait": await Wait(t); break;
            case "caption": _caption = line.Rest; break;
            case "hand": await SetHand(t.Skip(1)); break;
            case "clear-hand": await ClearPile(PileType.Hand); break;
            case "clear-pile": await ClearPile(Enum.Parse<PileType>(t[1], true)); break;
            case "move-pile": await MovePile(Enum.Parse<PileType>(t[1], true), Enum.Parse<PileType>(t[2], true)); break;
            case "add": await AddCards(Enum.Parse<PileType>(t[1], true), t.Skip(2)); break;
            case "hover": await Hover(t.Skip(1).ToArray()); break;
            case "click": await Click(t.Skip(1).ToArray()); break;
            case "key": await Key(t[1]); break;
            case "card-reward": await CardRewardScreen(t.Skip(1)); break;
            case "hp": await SetHp(t); break;
            case "endturn": await EndTurn(); break;
            case "play": await Play(t); break;
            case "close-modals": NModalContainer.Instance?.Clear(); await Frames(5); break;
            case "shot": await Shot(scene, t.Skip(1).ToArray()); break;
            default: await Console(line.Text); break;
        }
    }

    // ---- console ---------------------------------------------------------------------------------

    private async Task Console(string text)
    {
        var result = _console.ProcessNetCommand(Me, text);
        if (!result.success) throw new Exception($"console: {result.msg}");
        if (result.task != null) await result.task.WaitAsync(CommandTimeout);
        await Frames(2);
    }

    // ---- waits -----------------------------------------------------------------------------------

    private static async Task Wait(string[] t)
    {
        var what = t.Length > 1 ? t[1].ToLowerInvariant() : "1";
        switch (what)
        {
            case "combat-started":
                await Until(() => CombatManager.Instance.IsInProgress && Me.PlayerCombatState?.Phase == PlayerTurnPhase.Play
                                  && NPlayerHand.Instance != null, "combat started");
                await Seconds(1.5);
                break;
            case "turn-started":
                await Until(() => Me.PlayerCombatState is { Phase: PlayerTurnPhase.Play } s && s.TurnNumber > _endTurnFrom
                                  && NPlayerHand.Instance?.CurrentMode == NPlayerHand.Mode.Play, "next turn");
                await Seconds(1.5);
                break;
            case "room":
                await Until(() => RunManager.Instance.DebugOnlyGetState()?.CurrentRoom != null, "room");
                await Seconds(1.5);
                break;
            default:
                await Seconds(double.Parse(what, System.Globalization.CultureInfo.InvariantCulture));
                break;
        }
    }

    /// <summary>
    /// play &lt;CARD_ID&gt; [enemy N]: plays the leftmost copy in your hand the way the game does when you drop it
    /// (CardModel.TryManualPlay: energy, targeting and every hook), then waits for it to resolve.
    /// </summary>
    private static async Task Play(string[] t)
    {
        var id = t[1].ToUpperInvariant();
        var card = PileType.Hand.GetPile(Me).Cards.FirstOrDefault(c => c.Id.Entry == id) ?? throw new Exception($"no {id} in hand");
        MegaCrit.Sts2.Core.Entities.Creatures.Creature? target = null;
        if (t.Length >= 4 && t[2].Equals("enemy", StringComparison.OrdinalIgnoreCase))
            target = Regions.EnemyNodes().ElementAtOrDefault(int.Parse(t[3]) - 1)?.Entity ?? throw new Exception($"no enemy {t[3]}");
        if (!card.TryManualPlay(target)) throw new Exception($"{id} can't be played{(target == null ? " (needs a target?)" : "")}");
        await Until(() => !PileType.Hand.GetPile(Me).Cards.Contains(card), $"{id} to leave the hand");
        await Until(() => !RunManager.Instance.ActionExecutor.IsRunning, $"{id} to resolve");
        await Seconds(1.0);
    }

    private static int _endTurnFrom;

    /// <summary>Ends the turn the way the end-turn button does (the game's own end-turn action).</summary>
    private static async Task EndTurn()
    {
        var me = Me;
        var turn = me.PlayerCombatState?.TurnNumber ?? throw new Exception("not in combat");
        _endTurnFrom = turn;
        RunManager.Instance.ActionQueueSynchronizer.RequestEnqueue(new EndPlayerTurnAction(me, turn));
        await Frames(5);
    }

    private static async Task Until(Func<bool> condition, string what)
    {
        var deadline = DateTime.UtcNow + WaitTimeout;
        while (!condition())
        {
            if (DateTime.UtcNow > deadline) throw new TimeoutException($"timed out waiting for {what}");
            await Frames(1);
        }
    }

    private static async Task Frames(int count)
    {
        for (var i = 0; i < count; i++)
            await Tree.ToSignal(Tree, SceneTree.SignalName.ProcessFrame);
    }

    private static async Task Seconds(double s)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(s);
        while (DateTime.UtcNow < deadline) await Frames(1);
    }

    // ---- piles -----------------------------------------------------------------------------------

    private static CombatState Combat => CombatManager.Instance.DebugOnlyGetState() ?? throw new Exception("not in combat");

    /// <summary>Replaces the hand with these cards, left to right. NAME+ is the upgraded card.</summary>
    private static async Task SetHand(IEnumerable<string> cards)
    {
        await ClearPile(PileType.Hand);
        await AddCards(PileType.Hand, cards);
        await ParkMouse();
        await Seconds(1.0);
    }

    private static async Task AddCards(PileType pile, IEnumerable<string> cards)
    {
        ICardScope scope = pile.IsCombatPile() ? Combat : RunManager.Instance.DebugOnlyGetState()!;
        foreach (var name in cards)
        {
            var card = CreateCard(scope, name);
            await CardPileCmd.Add(card, pile);
            await Frames(2);
        }
    }

    private static CardModel CreateCard(ICardScope scope, string name)
    {
        var upgraded = name.EndsWith('+');
        var id = name.TrimEnd('+').ToUpperInvariant();
        var canonical = ModelDb.AllCards.FirstOrDefault(c => c.Id.Entry == id) ?? throw new Exception($"no card '{id}'");
        var card = scope.CreateCard(canonical, Me);
        if (upgraded) CardCmd.Upgrade(card, CardPreviewStyle.None);
        return card;
    }

    private static async Task ClearPile(PileType pile)
    {
        var cards = pile.GetPile(Me).Cards.ToList();
        if (cards.Count == 0) return;
        // With visuals: skipping them leaves the cards' nodes on screen.
        await CardPileCmd.RemoveFromCombat(cards);
        await Seconds(0.5);
    }

    private static async Task MovePile(PileType from, PileType to)
    {
        var cards = from.GetPile(Me).Cards.ToList();
        if (cards.Count == 0) return;
        await CardPileCmd.Add(cards, to);
        await Frames(5);
    }

    /// <summary>hp player &lt;n&gt; | hp enemy &lt;index&gt; &lt;n&gt;: sets current HP (enemies count from 1, left to right).</summary>
    private static async Task SetHp(string[] t)
    {
        var who = t[1].ToLowerInvariant();
        var creature = who == "player"
            ? Me.Creature
            : Regions.EnemyNodes().ElementAtOrDefault(int.Parse(t[2]) - 1)?.Entity ?? throw new Exception($"no enemy {t[2]}");
        var amount = decimal.Parse(t[^1], System.Globalization.CultureInfo.InvariantCulture);
        await CreatureCmd.SetCurrentHp(creature, amount);
        await Frames(5);
    }

    // ---- screens ---------------------------------------------------------------------------------

    /// <summary>Opens a card reward: the cards named, or three from the usual reward pool.</summary>
    private static async Task CardRewardScreen(IEnumerable<string> cards)
    {
        var player = Me;
        var names = cards.ToList();
        var options = CardCreationOptions.ForRoom(player, RoomType.Monster);
        CardReward reward;
        if (names.Count == 0)
        {
            reward = new CardReward(options, 3, player);
            reward.Populate();
        }
        else
        {
            ICardScope scope = RunManager.Instance.DebugOnlyGetState()!;
            reward = new CardReward(names.Select(n => CreateCard(scope, n)).ToList(), CardCreationSource.Encounter, player, options);
        }
        // Not awaited: it finishes only when a card is picked or skipped.
        _ = reward.SelectUnsynchronized();
        await Seconds(1.5);
    }

    // ---- input -----------------------------------------------------------------------------------

    private static async Task Hover(string[] target)
    {
        var rect = Regions.Resolve(target) ?? throw new Exception($"nothing to hover at '{string.Join(' ', target)}'");
        MoveMouse(rect.GetCenter());
        await Seconds(0.8);
    }

    private static async Task Click(string[] target)
    {
        var rect = Regions.Resolve(target) ?? throw new Exception($"nothing to click at '{string.Join(' ', target)}'");
        var at = rect.GetCenter();
        MoveMouse(at);
        await Frames(3);
        foreach (var pressed in new[] { true, false })
        {
            Input.ParseInputEvent(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = pressed, Position = at, GlobalPosition = at });
            await Frames(2);
        }
        await Seconds(0.8);
    }

    private static void MoveMouse(Vector2 at)
    {
        Input.WarpMouse(at);
        Input.ParseInputEvent(new InputEventMouseMotion { Position = at, GlobalPosition = at });
    }

    private static async Task Key(string name)
    {
        var key = Enum.Parse<Godot.Key>(name, ignoreCase: true);
        foreach (var pressed in new[] { true, false })
        {
            Input.ParseInputEvent(new InputEventKey { Keycode = key, PhysicalKeycode = key, Pressed = pressed });
            await Frames(2);
        }
        await Seconds(0.8);
    }

    // ---- screenshots -----------------------------------------------------------------------------

    /// <summary>
    /// shot &lt;name&gt; [region] [+ region...] [pad N]: saves the whole frame (full/&lt;name&gt;.png) and the crop
    /// around the union of the regions (&lt;name&gt;.png). Pad is in pixels at 1080p and scales with the window.
    /// </summary>
    private async Task Shot(Scene scene, string[] args)
    {
        if (args.Length == 0) throw new Exception("shot needs a name");
        var name = args[0];
        var rest = args.Skip(1).ToList();
        var pad = 24f;
        var padAt = rest.FindIndex(a => a.Equals("pad", StringComparison.OrdinalIgnoreCase));
        if (padAt >= 0)
        {
            pad = float.Parse(rest[padAt + 1], System.Globalization.CultureInfo.InvariantCulture);
            rest.RemoveRange(padAt, 2);
        }

        await Seconds(0.6);
        var capture = await ViewportCapture.GrabAsync();
        if (capture.Status != CaptureStatus.Captured || capture.Image == null)
            throw new Exception($"screenshot: {capture.Status} {capture.Detail}");
        var image = capture.Image;
        var size = new Vector2(image.GetWidth(), image.GetHeight());
        image.SavePng(GodotPath.Combine(_outDir, "full", name + ".png"));

        Rect2? crop = null;
        var groups = SplitOn(rest, "+");
        foreach (var group in groups.Where(g => g.Count > 0))
        {
            var r = Regions.Resolve(group.ToArray()) ?? throw new Exception($"region '{string.Join(' ', group)}' not found");
            crop = crop is { } c ? c.Merge(r) : r;
        }
        var scale = size.Y / 1080f;
        var rect = (crop ?? new Rect2(Vector2.Zero, size)).Grow(crop == null ? 0 : pad * scale);
        rect = rect.Intersection(new Rect2(Vector2.Zero, size));
        var ri = new Rect2I((int)rect.Position.X, (int)rect.Position.Y, (int)rect.Size.X, (int)rect.Size.Y);
        var cropped = image.GetRegion(ri);
        // The site's images: at most 1600 px wide, as WebP.
        const int maxWidth = 1600;
        if (cropped.GetWidth() > maxWidth)
            cropped.Resize(maxWidth, (int)Math.Round(cropped.GetHeight() * (double)maxWidth / cropped.GetWidth()), Image.Interpolation.Lanczos);
        var err = cropped.SaveWebp(GodotPath.Combine(_outDir, name + ".webp"), true, 0.88f);
        if (err != Error.Ok) throw new Exception($"saving {name}.webp: {err}");
        ri = new Rect2I(ri.Position, new Vector2I(cropped.GetWidth(), cropped.GetHeight()));

        _shots.Add(new ShotResult(name, scene.Name, scene.Title, scene.Section, _caption, ri.Position.X, ri.Position.Y, ri.Size.X, ri.Size.Y, (int)size.X, (int)size.Y));
        Log($"  shot {name}: {ri.Size.X}x{ri.Size.Y} at {ri.Position.X},{ri.Position.Y} of {size.X}x{size.Y}");
        _caption = "";
    }

    private static List<List<string>> SplitOn(List<string> tokens, string separator)
    {
        var groups = new List<List<string>> { new() };
        foreach (var t in tokens)
        {
            if (t == separator) groups.Add(new List<string>());
            else groups[^1].Add(t);
        }
        return groups;
    }

    private void Log(string message)
    {
        GD.Print("[SpireQoLCapture] " + message);
        _log?.WriteLine($"{DateTime.Now:HH:mm:ss} {message}");
    }

    public sealed record SceneResult(string Name, string Title, string Section, bool Ok, string? Error);

    public sealed record ShotResult(string Name, string Scene, string Title, string Section, string Caption,
        int X, int Y, int Width, int Height, int FrameWidth, int FrameHeight);
}
