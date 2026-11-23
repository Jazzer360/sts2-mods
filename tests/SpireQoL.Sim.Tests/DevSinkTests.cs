using System.Text.Json;
using SpireQoL.Telemetry;
using Xunit;

namespace SpireQoL.Sim.Tests;

/// <summary>
/// The dev dashboard's files (decision #108): the line format the page reads, the stream and state files, the reports and session
/// summaries kept instead of sent, the frame rollup and the stream files' rotation.
/// </summary>
public sealed class DevSinkTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "spireqol-devsink-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    private DevSink Sink(DateTime? now = null) => new(_dir, () => 12.3456, now ?? new DateTime(2026, 11, 19, 12, 0, 0, DateTimeKind.Utc), start: false);

    [Fact]
    public void AStateFileTheReaderHasOpenIsWrittenOnTheNextPassWithoutAWarning()
    {
        var warned = new List<string>();
        using var sink = new DevSink(_dir, () => 0, DateTime.UtcNow, start: false, warn: warned.Add);
        sink.SetState("{\"v\":1}");
        sink.Flush();
        var path = Path.Combine(_dir, DevSink.StatePath);
        using (new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite)) // the page reading it: no delete share
        {
            sink.SetState("{\"v\":2}");
            sink.Flush();
            // Windows won't replace a file another handle has open (the game's platform); Linux (GitHub's runners) does at once.
            if (OperatingSystem.IsWindows()) Assert.Equal("{\"v\":1}", File.ReadAllText(path));
        }
        sink.Flush();
        Assert.Equal("{\"v\":2}", File.ReadAllText(path));
        Assert.Empty(warned);
    }

    [Fact]
    public void ALineIsTheTimeTheKindAndTheFieldsInCamelCase()
    {
        var line = DevSink.Line(seconds: 1.23456, new SearchDone("Bash (target 0)", SearchDone.ContextOf(combat: 2, turn: 3), Steps: 5, Copies: 100, PeakHeld: 10, Ms: 1.5, Background: true, "the memory cap", Cancelled: false));
        using var doc = JsonDocument.Parse(line);
        var jsonElement = doc.RootElement;
        Assert.Equal(1.235, jsonElement.GetProperty("t").GetDouble());
        Assert.Equal("search-done", jsonElement.GetProperty("kind").GetString());
        Assert.Equal("Bash (target 0)", jsonElement.GetProperty("label").GetString());
        Assert.Equal(2, jsonElement.GetProperty("combat").GetInt32());
        Assert.Equal(3, jsonElement.GetProperty("turn").GetInt32());
        Assert.Equal("the memory cap", jsonElement.GetProperty("cap").GetString());
        Assert.Equal("{\"t\":0,\"kind\":\"combat-ended\"}", DevSink.Line(seconds: 0, new CombatEnded()));
    }

    [Fact]
    public void EventsGoToTheStreamFileOneLineEachAndTheStateIsReplaced()
    {
        using var sink = Sink();
        sink.Post(new TurnStarted(Turn: 1));
        sink.Post(new LogLine("INFO", "it's a \"quoted\" line\nwith a break"));
        sink.SetState("{\"v\":1}");
        sink.Flush();
        sink.Post(new TurnStarted(Turn: 2));
        sink.SetState("{\"v\":2}");
        sink.Flush();
        var lines = File.ReadAllLines(sink.StreamPath);
        Assert.Equal(3, lines.Length);
        Assert.All(lines, line => JsonDocument.Parse(line).Dispose());
        Assert.Contains("it's", lines[1]);
        Assert.Equal("{\"v\":2}", File.ReadAllText(Path.Combine(_dir, DevSink.StatePath)));
        Assert.EndsWith("stream-" + new DateTime(2026, 11, 19, 12, 0, 0, DateTimeKind.Utc).ToLocalTime().ToString("yyyyMMdd-HHmmss") + ".jsonl", sink.StreamPath);
    }

    [Fact]
    public void ReportsAreFilesAndSessionsAreLines()
    {
        using var sink = Sink();
        sink.SaveReport("20261119-120000-abc123", "{\n  \"title\": \"Proc audit: no tab\"\n}");
        sink.SaveReport("../escape", "{}"); // an id never leaves the reports folder
        sink.SaveSession("{\"v\":1,\n\"turns\":3}");
        sink.SaveSession("{\"v\":1,\"turns\":5}");
        sink.Flush();
        var reports = Path.Combine(_dir, DevSink.ReportsDir);
        Assert.Contains("Proc audit: no tab", File.ReadAllText(Path.Combine(reports, "20261119-120000-abc123.json")));
        Assert.True(File.Exists(Path.Combine(reports, "___escape.json")));
        var sessions = File.ReadAllLines(Path.Combine(_dir, DevSink.SessionsPath));
        Assert.Equal(2, sessions.Length); // one line each, line breaks inside a summary flattened
        Assert.All(sessions, line => JsonDocument.Parse(line).Dispose());
    }

    [Fact]
    public void OnlyTheNewestStreamFilesAreKept()
    {
        Directory.CreateDirectory(_dir);
        for (var i = 0; i < DevSink.KeepStreams + 5; i++) File.WriteAllText(Path.Combine(_dir, $"stream-20260101-{i:000000}.jsonl"), "");
        File.WriteAllText(Path.Combine(_dir, DevSink.FeedbackPath), "{}\n");
        using var sink = Sink();
        var left = Directory.GetFiles(_dir, "stream-*.jsonl").Select(Path.GetFileName).ToList();
        Assert.Equal(DevSink.KeepStreams - 1, left.Count); // and this launch's makes KeepStreams
        Assert.Contains($"stream-20260101-{DevSink.KeepStreams + 4:000000}.jsonl", left);
        Assert.True(File.Exists(Path.Combine(_dir, DevSink.FeedbackPath))); // the feedback is never rotated away
    }

    [Fact]
    public void AFrameRollupHasPercentilesFeatureTimesMemoryAndStartsAgain()
    {
        var frameWindow = new FrameWindow();
        for (var i = 1; i <= 20; i++) frameWindow.Add(i, gapMs: 16, new[] { ("incoming-damage", 0.5), ("proc-indicators", i / 2.0) }, new[] { ("proc-indicators", 42.0) });
        frameWindow.Add(modMs: 40, gapMs: 70, Array.Empty<(string, double)>(), Array.Empty<(string, double)>());
        var frameRollup = frameWindow.Take(seconds: 0.25, (100, 3, 1, 0, 0, 2, 1234, 2), (SimMb: 1.5, Compiles: 4, Nodes: 900, OrphanNodes: 3, Quiet: true, DevChecks: 2));
        Assert.Equal(21, frameRollup.Frames);
        Assert.Equal(11, frameRollup.ModP50);
        Assert.Equal(20, frameRollup.ModP95);
        Assert.Equal(40, frameRollup.ModMax);
        Assert.Equal(70, frameRollup.GapMax);
        Assert.Equal(1, frameRollup.Slow);
        Assert.Equal(10, frameRollup.FeatureMax["proc-indicators"]);
        Assert.Equal(Math.Round(20 * 0.5 / 21, 2), frameRollup.FeatureMean["incoming-damage"]);
        Assert.Equal(Math.Round(20 * 42.0 / 21, 2), frameRollup.FeatureKb["proc-indicators"]); // KB per frame, over every frame
        Assert.Equal(1234, frameRollup.HeldStates);
        Assert.Equal((1.5, 4, 900, 3, true, 2), (frameRollup.SimMb, frameRollup.Compiles, frameRollup.Nodes, frameRollup.OrphanNodes, frameRollup.Quiet, frameRollup.DevChecks));
        Assert.Equal(0, frameWindow.Take(seconds: 0.25, extra: default, engine: default).Frames);
    }

    [Fact]
    public void FramesWhileTheWindowWasAwayAreDropped()
    {
        // Decision #179: back in front, the frames from the background aren't in the next rollup.
        var frameWindow = new FrameWindow();
        frameWindow.Add(modMs: 3, gapMs: 33, new[] { ("card-tabs", 0.2) }, new[] { ("card-tabs", 5.0) });
        frameWindow.Clear();
        frameWindow.Add(modMs: 1, gapMs: 6, new[] { ("card-tabs", 0.1) }, Array.Empty<(string, double)>());
        var frameRollup = frameWindow.Take(seconds: 0.25, extra: default, engine: default);
        Assert.Equal(1, frameRollup.Frames);
        Assert.Equal(6, frameRollup.GapMax);
        Assert.False(frameRollup.FeatureKb.ContainsKey("card-tabs"));
        Assert.Equal(0.1, frameRollup.FeatureMax["card-tabs"]);
    }
}
