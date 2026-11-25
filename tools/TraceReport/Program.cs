using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using TraceReport;

// Reads a dev-mode performance trace (decision #258; docs/DEVELOPMENT.md "Performance traces") and writes beside it:
//   trace-....txt   the summary (also printed),
//   trace-....json  the same for the dev dashboard's Traces page,
//   trace-....speedscope.json  with --speedscope, a Full trace's CPU samples for https://www.speedscope.app.
// Usage: TraceReport <trace.nettrace> [--speedscope]
//        TraceReport --record-self <folder> [--seconds N]   records this program's own process (checks the reading without the game)

var arguments = args.ToList();
if (arguments.Count == 0 || arguments.Contains("--help"))
{
    Console.WriteLine("TraceReport <trace.nettrace> [--speedscope]   |   TraceReport --record-self <folder> [--seconds N]");
    return arguments.Count == 0 ? 1 : 0;
}

string tracePath;
if (arguments[0] == "--record-self")
{
    var seconds = arguments.IndexOf("--seconds") is var at and >= 0 && at + 1 < arguments.Count ? double.Parse(arguments[at + 1], CultureInfo.InvariantCulture) : 5;
    tracePath = SelfRecord.Run(arguments.Count > 1 ? arguments[1] : ".", TimeSpan.FromSeconds(seconds));
    Console.WriteLine($"Recorded {tracePath}");
}
else tracePath = arguments[0];

if (!File.Exists(tracePath))
{
    Console.Error.WriteLine($"No trace at {tracePath}");
    return 1;
}

var analysis = TraceAnalysis.Read(tracePath);
var launch = Launch.Find(tracePath);
var level = LevelOf(tracePath, analysis);
var json = ReportJson.Build(tracePath, level, analysis, launch);
var stem = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(tracePath))!, Path.GetFileNameWithoutExtension(tracePath));
File.WriteAllText(stem + ".json", json.ToJsonString(new JsonSerializerOptions { WriteIndented = false }));
var text = ReportText.Build(json);
File.WriteAllText(stem + ".txt", text);
Console.Write(text);
Console.WriteLine($"Wrote {stem}.json and {stem}.txt");

if (arguments.Contains("--speedscope"))
{
    if (analysis.Samples == 0) Console.WriteLine("No CPU samples in this trace (a Memory trace): no speedscope file.");
    else
    {
        Speedscope.Write(tracePath, stem + ".speedscope.json");
        Console.WriteLine($"Wrote {stem}.speedscope.json (open it at https://www.speedscope.app)");
    }
}
return 0;

// The level from the file's name (trace-...-memory.nettrace), or from what it holds.
static string LevelOf(string tracePath, TraceAnalysis analysis)
{
    var name = Path.GetFileNameWithoutExtension(tracePath);
    foreach (var known in new[] { "memory", "full" })
        if (name.EndsWith("-" + known, StringComparison.Ordinal)) return known;
    return analysis.Samples > 0 ? "full" : "memory";
}

/// <summary>The report as JSON (the dashboard's Traces page reads it: tools/dashboard/index.html, renderTraces).</summary>
internal static class ReportJson
{
    public const int Format = 1;
    private const double Megabyte = 1048576;

    private static double Round(double value, int digits = 2) => Math.Round(value, digits);

    private static JsonObject OwnerValues(Func<Owner, double> value, int digits = 2)
    {
        var values = new JsonObject();
        foreach (var owner in Owners.All) values[Owners.Name(owner)] = Round(value(owner), digits);
        return values;
    }

    public static JsonObject Build(string tracePath, string level, TraceAnalysis analysis, Launch? launch)
    {
        // Times on the launch's clock (the dev stream's seconds) when the launch is known; else seconds since the trace began.
        var offset = launch != null && launch.StartedUtc != default ? (analysis.StartedUtc - launch.StartedUtc).TotalSeconds : 0;
        var report = new JsonObject
        {
            ["format"] = Format,
            ["trace"] = Path.GetFileName(tracePath),
            ["level"] = level,
            ["mb"] = Round(new FileInfo(tracePath).Length / Megabyte, 1),
            ["startedUtc"] = analysis.StartedUtc.ToString("o", CultureInfo.InvariantCulture),
            ["seconds"] = Round(analysis.Seconds, 1),
            ["timeBase"] = launch != null && launch.StartedUtc != default ? "launch" : "trace",
            ["from"] = Round(offset, 2),
            ["to"] = Round(offset + analysis.Seconds, 2),
        };
        if (launch != null)
        {
            report["launch"] = new JsonObject { ["stream"] = launch.Stream, ["startedUtc"] = launch.StartedUtc.ToString("o", CultureInfo.InvariantCulture), ["end"] = Round(launch.End, 1) };
            report["stretches"] = new JsonArray(launch.Stretches.Select(stretch => (JsonNode)new JsonObject { ["level"] = stretch.Level, ["from"] = Round(stretch.From), ["to"] = Round(stretch.To) }).ToArray());
            report["away"] = new JsonArray(launch.Away.Select(away => (JsonNode)new JsonObject { ["from"] = Round(away.From), ["to"] = Round(away.To) }).ToArray());
            var levels = new JsonObject();
            foreach (var (name, frames) in launch.Levels)
            {
                var medians = frames.ModMedians.OrderBy(value => value).ToList();
                levels[name] = new JsonObject
                {
                    ["seconds"] = Round(frames.Seconds, 1),
                    ["fps"] = Round(frames.Seconds > 0 ? frames.Frames / frames.Seconds : 0, 1),
                    ["modP50"] = Round(medians.Count > 0 ? medians[medians.Count / 2] : 0, 3),
                    ["modP95"] = Round(frames.Rollups > 0 ? frames.ModP95Sum / frames.Rollups : 0, 3),
                    ["allocatedMbPerSecond"] = Round(frames.Seconds > 0 ? frames.AllocatedMb / frames.Seconds : 0, 2),
                    ["gcPauseMsPerSecond"] = Round(frames.Seconds > 0 ? frames.GcPauseMs / frames.Seconds : 0, 3),
                };
            }
            report["levels"] = levels;
        }
        report["allocations"] = Allocations(analysis);
        report["cpu"] = analysis.Samples > 0 ? Cpu(analysis) : null;
        report["gc"] = Gc(analysis, offset);
        return report;
    }

    private static JsonObject Allocations(TraceAnalysis analysis)
    {
        var byOwner = new double[Owners.All.Length];
        foreach (var bytes in analysis.TypeBytes.Values)
            for (var i = 0; i < bytes.Length; i++) byOwner[i] += bytes[i];
        var total = byOwner.Sum();
        var types = analysis.TypeBytes.OrderByDescending(entry => entry.Value.Sum()).Take(80)
            .Select(entry => (JsonNode)new JsonObject
            {
                ["type"] = entry.Key,
                ["mb"] = Round(entry.Value.Sum() / Megabyte, 3),
                ["owners"] = OwnerValues(owner => entry.Value[(int)owner] / Megabyte, digits: 3),
            }).ToArray();
        JsonArray Sites(Owner owner, int count) => new(analysis.SiteBytes.Where(entry => entry.Key.Owner == owner)
            .OrderByDescending(entry => entry.Value.Bytes).Take(count)
            .Select(entry => (JsonNode)new JsonObject
            {
                ["type"] = entry.Key.Type,
                ["site"] = entry.Key.Site,
                ["mb"] = Round(entry.Value.Bytes / Megabyte, 3),
                ["ticks"] = entry.Value.Ticks,
            }).ToArray());
        var modMethods = analysis.SiteBytes.Where(entry => entry.Key.Owner == Owner.Mod).GroupBy(entry => entry.Key.Site)
            .Select(site => (Site: site.Key, Bytes: site.Sum(entry => entry.Value.Bytes), Types: site.OrderByDescending(entry => entry.Value.Bytes).Select(entry => entry.Key.Type).Take(3).ToList()))
            .OrderByDescending(site => site.Bytes).Take(60)
            .Select(site => (JsonNode)new JsonObject { ["site"] = site.Site, ["mb"] = Round(site.Bytes / Megabyte, 3), ["types"] = new JsonArray(site.Types.Select(type => (JsonNode)type!).ToArray()) })
            .ToArray();
        return new JsonObject
        {
            ["ticks"] = analysis.AllocationTicks,
            ["mb"] = Round(total / Megabyte, 1),
            ["mbPerSecond"] = Round(analysis.Seconds > 0 ? total / Megabyte / analysis.Seconds : 0, 2),
            ["owners"] = OwnerValues(owner => byOwner[(int)owner] / Megabyte),
            ["types"] = new JsonArray(types),
            ["modSites"] = Sites(Owner.Mod, count: 150),
            ["modMethods"] = new JsonArray(modMethods),
            ["sites"] = new JsonObject
            {
                ["game"] = Sites(Owner.Game, count: 40),
                ["godot"] = Sites(Owner.Godot, count: 40),
                ["runtime"] = Sites(Owner.Runtime, count: 25),
                ["other"] = Sites(Owner.Other, count: 25),
            },
        };
    }

    private static JsonObject Cpu(TraceAnalysis analysis)
    {
        JsonArray Methods(IEnumerable<KeyValuePair<string, (Owner Owner, int Samples)>> methods, int count) => new(methods
            .OrderByDescending(entry => entry.Value.Samples).Take(count)
            .Select(entry => (JsonNode)new JsonObject { ["method"] = entry.Key, ["owner"] = Owners.Name(entry.Value.Owner), ["samples"] = entry.Value.Samples }).ToArray());
        return new JsonObject
        {
            ["samples"] = analysis.Samples,
            ["busy"] = analysis.BusySamples,
            ["owners"] = OwnerValues(owner => analysis.ChargedSamples[(int)owner], digits: 0),
            ["leafOwners"] = OwnerValues(owner => analysis.LeafSamples[(int)owner], digits: 0),
            ["exclusive"] = Methods(analysis.Exclusive, count: 80),
            ["inclusive"] = Methods(analysis.Inclusive, count: 80),
            ["modExclusive"] = Methods(analysis.Exclusive.Where(entry => entry.Value.Owner == Owner.Mod), count: 60),
            ["modInclusive"] = Methods(analysis.Inclusive.Where(entry => entry.Value.Owner == Owner.Mod), count: 60),
            ["waiting"] = Methods(analysis.Waiting, count: 30),
        };
    }

    private static JsonObject Gc(TraceAnalysis analysis, double offset)
    {
        var pauses = analysis.Collections.Select(collection => collection.PauseMs).OrderBy(pause => pause).ToList();
        double At(double percentile) => pauses.Count == 0 ? 0 : pauses[Math.Clamp((int)Math.Ceiling(percentile * pauses.Count) - 1, 0, pauses.Count - 1)];
        var heaps = analysis.Collections.SelectMany(collection => new[] { collection.HeapBeforeMb, collection.HeapAfterMb }).Where(heap => heap > 0).ToList();
        var count = new JsonObject();
        foreach (var generation in analysis.Collections.GroupBy(collection => collection.Generation).OrderBy(generation => generation.Key))
            count[generation.Key.ToString(CultureInfo.InvariantCulture)] = generation.Count();
        return new JsonObject
        {
            ["count"] = count,
            ["pauseMs"] = new JsonObject { ["p50"] = Round(At(percentile: 0.5), 3), ["p95"] = Round(At(percentile: 0.95), 3), ["max"] = Round(pauses.Count > 0 ? pauses[^1] : 0, 3), ["total"] = Round(pauses.Sum(), 1) },
            ["heapMb"] = new JsonObject { ["min"] = Round(heaps.Count > 0 ? heaps.Min() : 0, 1), ["max"] = Round(heaps.Count > 0 ? heaps.Max() : 0, 1) },
            ["collections"] = new JsonArray(analysis.Collections.Select(collection => (JsonNode)new JsonObject
            {
                ["t"] = Round(offset + collection.AtMs / 1000, 3),
                ["gen"] = collection.Generation,
                ["pauseMs"] = Round(collection.PauseMs, 3),
                ["heapBeforeMb"] = Round(collection.HeapBeforeMb, 1),
                ["heapAfterMb"] = Round(collection.HeapAfterMb, 1),
                ["reason"] = collection.Reason,
                ["type"] = collection.Type,
            }).ToArray()),
        };
    }
}

/// <summary>The report as text, from its JSON.</summary>
internal static class ReportText
{
    private static string Mb(double megabytes) => megabytes >= 100 ? $"{megabytes:0} MB" : megabytes >= 1 ? $"{megabytes:0.0} MB" : $"{megabytes * 1024:0} KB";

    private static string Share(double part, double whole) => whole > 0 ? $"{100 * part / whole:0.0}%" : "-";

    private static string Cut(string text, int width) => text.Length <= width ? text : text[..(width - 1)] + "…";

    public static string Build(JsonObject report)
    {
        var text = new StringBuilder();
        var culture = CultureInfo.InvariantCulture;
        text.AppendLine(culture, $"Trace {report["trace"]} ({report["level"]}, {report["mb"]} MB, {report["seconds"]} s, from {report["startedUtc"]})");
        if (report["levels"] is JsonObject levels && levels.Count > 0)
        {
            text.AppendLine().AppendLine("Frames at each tracing level this launch (the mod's time per frame; what tracing costs):");
            text.AppendLine("  level     seconds     fps   mod p50   mod p95   alloc MB/s   GC pause ms/s");
            foreach (var (name, node) in levels)
            {
                var level = node!.AsObject();
                text.AppendLine(culture, $"  {name,-8} {level["seconds"],8} {level["fps"],7} {level["modP50"],9} {level["modP95"],9} {level["allocatedMbPerSecond"],12} {level["gcPauseMsPerSecond"],15}");
            }
        }

        var allocations = report["allocations"]!.AsObject();
        var allocatedMb = allocations["mb"].AsNumber();
        text.AppendLine().AppendLine(culture, $"Allocations: {Mb(allocatedMb)} sampled ({allocations["ticks"]} ticks, about one per 100 KB; {allocations["mbPerSecond"]} MB/s)");
        text.AppendLine("  charged to (the mod when any of its frames is on the stack):");
        foreach (var (owner, value) in allocations["owners"]!.AsObject())
            text.AppendLine(culture, $"    {owner,-8} {Mb(value.AsNumber()),10}  {Share(value.AsNumber(), allocatedMb),6}");
        text.AppendLine().AppendLine("  By type (and the mod's share of each):");
        foreach (var node in allocations["types"]!.AsArray().Take(25))
        {
            var type = node!.AsObject();
            var mb = type["mb"].AsNumber();
            var mod = type["owners"]!["mod"].AsNumber();
            text.AppendLine(culture, $"    {Mb(mb),10}  mod {Share(mod, mb),6}  {Cut(type["type"]!.GetValue<string>(), 90)}");
        }
        text.AppendLine().AppendLine("  The mod's call sites (the mod's frame nearest the allocation; type, amount):");
        foreach (var node in allocations["modSites"]!.AsArray().Take(40))
            text.AppendLine(culture, $"    {Mb(node!["mb"].AsNumber()),10}  {Cut(node["type"]!.GetValue<string>(), 45),-45}  {Cut(node["site"]!.GetValue<string>(), 110)}");
        foreach (var owner in new[] { "game", "godot" })
        {
            text.AppendLine().AppendLine(culture, $"  The {owner}'s top call sites, for context:");
            foreach (var node in allocations["sites"]![owner]!.AsArray().Take(12))
                text.AppendLine(culture, $"    {Mb(node!["mb"].AsNumber()),10}  {Cut(node["type"]!.GetValue<string>(), 45),-45}  {Cut(node["site"]!.GetValue<string>(), 110)}");
        }

        if (report["cpu"] is JsonObject cpu)
        {
            var busy = cpu["busy"].AsNumber();
            text.AppendLine().AppendLine(culture, $"CPU: {cpu["samples"]} samples (about 1 ms each per thread), {cpu["busy"]} busy (the rest a thread waiting: a wait, a sleep, a read)");
            text.AppendLine("  charged to (any of its frames on the stack) / where the time was spent (the frame itself):");
            foreach (var (owner, value) in cpu["owners"]!.AsObject())
                text.AppendLine(culture, $"    {owner,-8} {Share(value.AsNumber(), busy),7}   {Share(cpu["leafOwners"]![owner].AsNumber(), busy),7}");
            void Methods(string title, string key, int count)
            {
                text.AppendLine().AppendLine(culture, $"  {title}:");
                foreach (var node in cpu[key]!.AsArray().Take(count))
                    text.AppendLine(culture, $"    {Share(node!["samples"].AsNumber(), busy),7}  {node["owner"],-8} {Cut(node["method"]!.GetValue<string>(), 120)}");
            }
            Methods("By method, exclusive (the frame itself)", "exclusive", count: 25);
            Methods("By method, inclusive (the frame and what it calls)", "inclusive", count: 25);
            Methods("The mod's methods, inclusive", "modInclusive", count: 30);
            text.AppendLine().AppendLine("  Threads waiting (not counted above), by the frame they wait in:");
            foreach (var node in cpu["waiting"]!.AsArray().Take(10))
                text.AppendLine(culture, $"    {node!["samples"],7}  {node["owner"],-8} {Cut(node["method"]!.GetValue<string>(), 120)}");
        }
        else text.AppendLine().AppendLine("CPU: none in this trace (Memory level). Set Record a performance trace to Full for CPU samples.");

        var gc = report["gc"]!.AsObject();
        var counts = string.Join(", ", gc["count"]!.AsObject().Select(entry => $"gen {entry.Key}: {entry.Value}"));
        text.AppendLine().AppendLine(culture, $"Collections: {(counts.Length > 0 ? counts : "none")}");
        var pause = gc["pauseMs"]!;
        text.AppendLine(culture, $"  pauses: p50 {pause["p50"]} ms, p95 {pause["p95"]} ms, max {pause["max"]} ms, total {pause["total"]} ms");
        text.AppendLine(culture, $"  heap: {gc["heapMb"]!["min"]} to {gc["heapMb"]!["max"]} MB");
        var collections = gc["collections"]!.AsArray();
        var longest = collections.OrderByDescending(node => node!["pauseMs"].AsNumber()).Take(10).ToList();
        if (longest.Count > 0)
        {
            text.AppendLine(culture, $"  the longest pauses (t in {report["timeBase"]} seconds):");
            foreach (var node in longest)
                text.AppendLine(culture, $"    t {node!["t"],9}  gen {node["gen"]}  {node["pauseMs"],8} ms  heap {node["heapBeforeMb"]} -> {node["heapAfterMb"]} MB  {node["reason"]}");
        }
        return text.ToString();
    }
}

internal static class JsonNumbers
{
    /// <summary>A number in the report, whether it was written as a whole number or not.</summary>
    public static double AsNumber(this JsonNode? node) => node is JsonValue value && value.TryGetValue<double>(out var number) ? number
        : node is JsonValue whole && whole.TryGetValue<long>(out var count) ? count
        : node is JsonValue integer && integer.TryGetValue<int>(out var small) ? small : 0;
}
