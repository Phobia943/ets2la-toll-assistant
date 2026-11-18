using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Etlx;

// Offline reader only. The fixture mode is a separate, deliberately allocating
// process; it never loads ETS2LA, opens its channels or sends vehicle inputs.
if (args is ["--fixture"])
{
    Console.WriteLine($"Fixture PID {Environment.ProcessId}, UTC {DateTime.UtcNow:o}");
    var timer = Stopwatch.StartNew();
    while (timer.Elapsed.TotalSeconds < 7)
    {
        for (var i = 0; i < 500; ++i) GC.KeepAlive(new byte[90000]);
        GC.Collect(2, GCCollectionMode.Forced, true, true);
        Thread.Sleep(100);
    }
    return;
}
if (args.Length is < 2 or > 4)
    throw new ArgumentException("RuntimeTrace <capture.nettrace> <new-output-directory> [from-UTC] [to-UTC]");
var input = Path.GetFullPath(args[0]);
var directory = Path.GetFullPath(args[1]);
if (!File.Exists(input) || Directory.Exists(directory))
    throw new ArgumentException("Input must exist and output directory must be new.");
DateTime Bound(int i, DateTime fallback) => args.Length > i
    ? DateTimeOffset.Parse(args[i], CultureInfo.InvariantCulture).UtcDateTime : fallback;
var from = Bound(2, DateTime.MinValue);
var to = Bound(3, DateTime.MaxValue);
if (to <= from) throw new ArgumentException("Invalid time window.");
Directory.CreateDirectory(directory);
var lost = 0;
var options = new TraceLogOptions { OnLostEvents = (_, count, _) => lost += count };
var etlx = TraceLog.CreateFromEventPipeDataFile(input, Path.Combine(directory, "runtime.etlx"), options);
using var log = new TraceLog(etlx);
using var runtime = new StreamWriter(Path.Combine(directory, "runtime-events.jsonl"));
using var samples = new StreamWriter(Path.Combine(directory, "thread-samples.jsonl"));
var counts = new SortedDictionary<string, long>();
long sampleCount = 0, relevantCount = 0, unresolved = 0;
foreach (var item in log.Events)
{
    var utc = item.TimeStamp.ToUniversalTime();
    if (utc < from || utc > to) continue;
    var key = item.ProviderName + "/" + item.EventName;
    counts[key] = counts.GetValueOrDefault(key) + 1;
    var isSample = item.ProviderName.Contains("SampleProfiler", StringComparison.Ordinal);
    var relevant = item.ProviderName == "Microsoft-Windows-DotNETRuntime" &&
        (item.EventName.StartsWith("GC/", StringComparison.Ordinal) ||
         item.EventName.Contains("Thread", StringComparison.Ordinal) ||
         item.EventName.Contains("Contention", StringComparison.Ordinal) ||
         item.EventName.Contains("Exception", StringComparison.Ordinal));
    if (!isSample && !relevant) continue;
    var stack = new List<string>();
    for (var frame = item.CallStack(); frame != null && stack.Count < 80; frame = frame.Caller)
        stack.Add(frame.CodeAddress.FullMethodName);
    if (isSample)
    {
        sampleCount++;
        if (stack.Count == 0) unresolved++;
        samples.WriteLine(JsonSerializer.Serialize(new { utc, item.ThreadID, stack }));
    }
    else
    {
        relevantCount++;
        // Payload values become strings to avoid unsupported native address types.
        var payload = item.PayloadNames.ToDictionary(n => n, n => item.PayloadByName(n)?.ToString());
        runtime.WriteLine(JsonSerializer.Serialize(new { utc, item.ThreadID, item.EventName, payload, stack }));
    }
}
var summary = new { input, from, to, eventsLost = lost, sampleCount,
    unresolvedSampleStacks = unresolved, runtimeEventCount = relevantCount, eventCounts = counts,
    limitations = new[] {
        "Samples show managed wall-time stacks, not proof of CPU usage or kernel scheduling.",
        "Collection has overhead; correlate UTC with the separate original-timestamp control recording.",
        "Missing events or absence of a sampled stack do not prove that a function did not execute.",
        "This reader does not diagnose or repair steering automatically."
    }};
File.WriteAllText(Path.Combine(directory, "summary.json"), JsonSerializer.Serialize(summary, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine(JsonSerializer.Serialize(new { eventsLost = lost, sampleCount, runtimeEventCount = relevantCount, directory }));
