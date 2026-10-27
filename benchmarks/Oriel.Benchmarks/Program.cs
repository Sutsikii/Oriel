using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Exporters.Json;
using BenchmarkDotNet.Running;
using Oriel.Benchmarks;

// dotnet run -c Release -- [BenchmarkDotNet arguments]      the in-process benchmarks
// dotnet run -c Release -- roundtrip <out.json>             the round trip in a real window
// dotnet run -c Release -- binary                          binary results against JSON, in a real window
// dotnet run -c Release -- check <results> <roundtrip.json> <baseline.json> [--update]
//                                                           compares with the baseline
CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;

return args switch
{
    ["roundtrip", var output] => await RunRoundTrip(output),
    ["binary"] => await RunBinary(),
    ["check", var results, var roundTrip, var baseline, .. var rest] => Check(results, roundTrip, baseline, update: rest.Contains("--update")),
    _ => RunBenchmarks(args),
};

static int RunBenchmarks(string[] args)
{
    var config = DefaultConfig.Instance.AddExporter(JsonExporter.Full);
    var summaries = BenchmarkSwitcher.FromAssembly(typeof(CallBenchmarks).Assembly).Run(args, config);
    return summaries.Any(s => s.HasCriticalValidationErrors || s.Reports.Any(r => !r.Success)) ? 1 : 0;
}

static async Task<int> RunRoundTrip(string output)
{
    var result = await RoundTrip.Run();
    File.WriteAllText(output, JsonSerializer.Serialize(result, new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true }));
    Console.WriteLine($"Round trip: p50 {result.P50Ms:F3} ms, p99 {result.P99Ms:F3} ms, max {result.MaxMs:F3} ms over {result.Calls} calls; " +
        $"{result.CallsPerSecond:F0} calls/s with {result.Batch} in flight");
    return 0;
}

static async Task<int> RunBinary()
{
    Console.WriteLine("| Size | Binary (ms) | JSON, base64 (ms) | Faster |");
    Console.WriteLine("|---|---|---|---|");
    foreach (var row in await BinaryResults.Run())
        Console.WriteLine($"| {row.Megabytes} MB | {row.BinaryMs:F1} | {row.JsonMs:F1} | {row.JsonMs / row.BinaryMs:F1}x |");
    return 0;
}

/// <summary>
/// Compares the means of BenchmarkDotNet's JSON reports and the round trip
/// with the baseline: a benchmark slower than its baseline times the
/// factor, or a round trip slower or with fewer calls a second, fails.
/// Prints a Markdown table, to the job summary too in GitHub Actions.
/// </summary>
static int Check(string resultsFolder, string roundTripFile, string baselineFile, bool update)
{
    var current = new Dictionary<string, double>();
    foreach (var report in Directory.GetFiles(resultsFolder, "*-report-full.json", SearchOption.AllDirectories))
    {
        var doc = JsonNode.Parse(File.ReadAllText(report))!;
        foreach (var benchmark in doc["Benchmarks"]!.AsArray())
            current[benchmark!["Method"]!.GetValue<string>()] = benchmark["Statistics"]!["Mean"]!.GetValue<double>();
    }
    var roundTrip = JsonNode.Parse(File.ReadAllText(roundTripFile))!;
    current["RoundTrip.p50Ms"] = roundTrip["p50Ms"]!.GetValue<double>();
    current["RoundTrip.p99Ms"] = roundTrip["p99Ms"]!.GetValue<double>();
    current["RoundTrip.callsPerSecond"] = roundTrip["callsPerSecond"]!.GetValue<double>();

    var baseline = JsonNode.Parse(File.ReadAllText(baselineFile))!;
    var factor = baseline["factor"]!.GetValue<double>();
    var values = baseline["values"]!.AsObject();
    if (update)
    {
        var updated = new JsonObject { ["factor"] = factor, ["values"] = new JsonObject() };
        foreach (var (name, value) in current.OrderBy(p => p.Key, StringComparer.Ordinal))
            updated["values"]!.AsObject()[name] = Math.Round(value, 3);
        File.WriteAllText(baselineFile, updated.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + "\n");
        Console.WriteLine($"Updated {baselineFile}.");
        return 0;
    }

    var table = new StringBuilder("| Measure | Baseline | Now | |\n|---|---:|---:|---|\n");
    var failed = false;
    foreach (var (name, value) in current.OrderBy(p => p.Key, StringComparer.Ordinal))
    {
        var unit = name.EndsWith("Ms", StringComparison.Ordinal) ? " ms" : name.EndsWith("callsPerSecond", StringComparison.Ordinal) ? "/s" : " ns";
        if (values[name] is not { } known)
        {
            table.Append(CultureInfo.InvariantCulture, $"| {name} | none | {Format(value, unit)} | new |\n");
            continue;
        }
        var base_ = known.GetValue<double>();
        // Fewer calls a second is worse; a longer time is worse. p99 only reports: it is too noisy on shared runners.
        var higherIsBetter = name.EndsWith("callsPerSecond", StringComparison.Ordinal);
        var checkedHere = !name.EndsWith("p99Ms", StringComparison.Ordinal);
        var regressed = checkedHere && (higherIsBetter ? value < base_ / factor : value > base_ * factor);
        failed |= regressed;
        table.Append(CultureInfo.InvariantCulture,
            $"| {name} | {Format(base_, unit)} | {Format(value, unit)} | {(regressed ? $"**regressed** (more than {factor}×)" : checkedHere ? "ok" : "reported")} |\n");
    }
    Console.WriteLine(table);
    if (Environment.GetEnvironmentVariable("GITHUB_STEP_SUMMARY") is { Length: > 0 } summary)
        File.AppendAllText(summary, "## Benchmarks\n\n" + table + "\n");
    return failed ? 1 : 0;
}

static string Format(double value, string unit) =>
    value.ToString(unit == " ms" ? "F3" : "F1", CultureInfo.InvariantCulture) + unit;
