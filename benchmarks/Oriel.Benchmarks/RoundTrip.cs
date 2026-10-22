using System.Text.Json;

namespace Oriel.Benchmarks;

/// <summary>The page of the round trip reports through it.</summary>
[Service(Name = "RoundTrip")]
public sealed class RoundTripProbe
{
    public TaskCompletionSource<string> Reported { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public int Echo(int value) => value;

    public void Report(string result) => Reported.TrySetResult(result);
}

/// <summary>
/// Calls made by a page in a real window, timed by the page: the latency of
/// one call after another (p50, p99), then how many calls a second go
/// through when many are in flight.
/// </summary>
/// <remarks>
/// A page's clock ticks every 0.1 ms, longer than a call takes; the calls
/// are timed in groups of ten in a row, and each group gives the latency of
/// its calls.
/// </remarks>
internal static class RoundTrip
{
    public sealed record Result(int Calls, double P50Ms, double P99Ms, double MaxMs, int Batch, double CallsPerSecond);

    public static async Task<Result> Run(int calls = 2000, int batch = 10_000)
    {
        var assets = Directory.CreateTempSubdirectory("oriel-roundtrip-").FullName;
        var name = "Oriel RoundTrip " + Guid.NewGuid().ToString("N")[..8];
        File.WriteAllText(Path.Combine(assets, "index.html"), $$"""
            <!doctype html>
            <script type="module">
            for (let i = 0; i < 200; i++) await oriel.call("RoundTrip.Echo", [i]); // warm up
            const times = [];
            for (let i = 0; i < {{calls}}; i += 10) {
              const started = performance.now();
              for (let j = 0; j < 10; j++) await oriel.call("RoundTrip.Echo", [i + j]);
              times.push((performance.now() - started) / 10);
            }
            times.sort((a, b) => a - b);
            const at = (q) => times[Math.min(times.length - 1, Math.floor(q * times.length))];
            const started = performance.now();
            await Promise.all(Array.from({ length: {{batch}} }, (_, i) => oriel.call("RoundTrip.Echo", [i])));
            const seconds = (performance.now() - started) / 1000;
            await oriel.call("RoundTrip.Report", [JSON.stringify({
              calls: times.length * 10, p50Ms: at(0.5), p99Ms: at(0.99), maxMs: times[times.length - 1],
              batch: {{batch}}, callsPerSecond: {{batch}} / seconds,
            })]);
            </script>
            """);
        try
        {
            var app = new OrielApp(new AppOptions { Name = name, AssetsDirectory = assets, MaxCallsInFlight = batch });
            var probe = new RoundTripProbe();
            app.Bind(probe);
            var run = Task.Run(() => app.Run(() => app.OpenWindow(new WindowOptions { Width = 400, Height = 300 })));
            var report = await probe.Reported.Task.WaitAsync(TimeSpan.FromMinutes(5));
            foreach (var window in app.Windows)
                window.Close();
            await run.WaitAsync(TimeSpan.FromSeconds(30));
            return JsonSerializer.Deserialize<Result>(report, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        }
        finally
        {
            TryDelete(assets);
            TryDelete(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), name));
        }
    }

    private static void TryDelete(string folder)
    {
        for (var i = 0; i < 10 && Directory.Exists(folder); i++)
        {
            try
            {
                Directory.Delete(folder, recursive: true);
            }
            catch (IOException)
            {
                Thread.Sleep(500); // the webview's processes may hold files for a moment
            }
            catch (UnauthorizedAccessException)
            {
                Thread.Sleep(500);
            }
        }
    }
}
