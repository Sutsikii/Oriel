using System.Diagnostics;
using System.Text.Json;
using Oriel.Platform.Windows;
using Xunit.Abstractions;

[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace Oriel.Stress.Tests;

/// <summary>A test that loads real windows for minutes: it runs when ORIEL_STRESS=1.</summary>
public sealed class StressFactAttribute : FactAttribute
{
    public StressFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("ORIEL_STRESS") != "1")
            Skip = "takes minutes; set ORIEL_STRESS=1 to run";
        else if (!OperatingSystem.IsWindows())
            Skip = "Oriel runs on Windows for now";
    }
}

/// <summary>The services the stress pages call.</summary>
[Service(Name = "Stress")]
public sealed class Probe
{
    private TaskCompletionSource _loaded = NewSource();

    public TaskCompletionSource<string> Reported { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public Event<int> Tick { get; } = new();

    /// <summary>Completes when a page next says it loaded.</summary>
    public Task NextLoad => _loaded.Task;

    public void Loaded()
    {
        var loaded = Interlocked.Exchange(ref _loaded, NewSource());
        loaded.TrySetResult();
    }

    public int Echo(int value) => value;

    public void Report(string result) => Reported.TrySetResult(result);

    private static TaskCompletionSource NewSource() => new(TaskCreationOptions.RunContinuationsAsynchronously);
}

/// <summary>
/// Checks that windows, calls and events release what they hold: COM
/// references, GC handles of callbacks, handles and memory. Counts and
/// durations can be lowered for a quick run with ORIEL_STRESS_WINDOWS,
/// ORIEL_STRESS_CALLS and ORIEL_STRESS_EVENT_SECONDS.
/// </summary>
public sealed class StressTests(ITestOutputHelper output) : IDisposable
{
    private readonly string _assets = Path.Combine(Path.GetTempPath(), "oriel-stress-" + Guid.NewGuid().ToString("N"));
    private readonly string _name = "Oriel Stress " + Guid.NewGuid().ToString("N")[..8];

    private static int Setting(string name, int fallback) =>
        int.TryParse(Environment.GetEnvironmentVariable(name), out var value) ? value : fallback;

    public void Dispose()
    {
        if (Directory.Exists(_assets))
            Directory.Delete(_assets, recursive: true);
        var data = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), _name);
        for (var i = 0; i < 20 && Directory.Exists(data); i++)
        {
            try
            {
                Directory.Delete(data, recursive: true); // the webview's processes may hold files for a moment
            }
            catch (IOException)
            {
                Thread.Sleep(500);
            }
            catch (UnauthorizedAccessException)
            {
                Thread.Sleep(500);
            }
        }
    }

    [StressFact]
    public async Task ClosingWindowsReleasesWhatTheyHeld()
    {
        var windows = Setting("ORIEL_STRESS_WINDOWS", 500);
        await using var app = await StressApp.Start(_name, Page("await oriel.call('Stress.Loaded', []);"));

        // The first windows create the webview's environment and warm caches up.
        for (var i = 0; i < 20; i++)
            await app.OpenAndClose();
        var before = await Measure.Settled();
        output.WriteLine($"after warming up: {before}");

        for (var i = 1; i <= windows; i++)
        {
            await app.OpenAndClose();
            if (i % 100 == 0)
                output.WriteLine($"{i} windows: {Measure.Now()}");
        }
        var after = await Measure.Settled(before.Callbacks);
        output.WriteLine($"after {windows} windows: {after}");

        Assert.Empty(app.Errors);
        // Every callback of a window is released once it closed; handles and
        // memory may wobble, but must not grow with the number of windows.
        Assert.Equal(before.Callbacks, after.Callbacks);
        Assert.InRange(after.Handles - before.Handles, int.MinValue, 100);
        Assert.InRange(after.PrivateMegabytes - before.PrivateMegabytes, double.MinValue, 32);
    }

    [StressFact]
    public async Task AnswersThousandsOfCallsAtOnce()
    {
        var calls = Setting("ORIEL_STRESS_CALLS", 10_000);
        await using var app = await StressApp.Start(_name, Page($$"""
            const n = {{calls}};
            const started = performance.now();
            const results = await Promise.all(Array.from({ length: n }, (_, i) => oriel.call("Stress.Echo", [i])));
            const ms = performance.now() - started;
            const wrong = results.filter((v, i) => v !== i).length;
            await oriel.call("Stress.Report", [JSON.stringify({ n: results.length, wrong, ms })]);
            """), maxCallsInFlight: calls);

        await app.Open();
        using var report = JsonDocument.Parse(await app.Probe.Reported.Task.WaitAsync(TimeSpan.FromMinutes(2)));
        var root = report.RootElement;
        output.WriteLine($"{calls} calls in {root.GetProperty("ms").GetDouble():F0} ms");

        Assert.Empty(app.Errors);
        Assert.Equal(calls, root.GetProperty("n").GetInt32());
        Assert.Equal(0, root.GetProperty("wrong").GetInt32());
    }

    [StressFact]
    public async Task DeliversEventsAt120HzWithoutGrowing()
    {
        var seconds = Setting("ORIEL_STRESS_EVENT_SECONDS", 60);
        await using var app = await StressApp.Start(_name, Page("""
            let received = 0, outOfOrder = 0, last = -1;
            oriel.on("Stress.Tick", async (v) => {
              if (v === -1) {
                await oriel.call("Stress.Report", [JSON.stringify({ received, outOfOrder })]);
                return;
              }
              received++;
              if (v !== last + 1) outOfOrder++;
              last = v;
            });
            await oriel.call("Stress.Loaded", []);
            """));

        var loaded = app.Probe.NextLoad;
        await app.Open();
        await loaded.WaitAsync(TimeSpan.FromSeconds(30));

        // 120 a second on average: timers on Windows tick every 15 ms or so,
        // so each tick emits the events that are due.
        var clock = Stopwatch.StartNew();
        var sent = 0;
        Measure? early = null;
        while (clock.Elapsed < TimeSpan.FromSeconds(seconds))
        {
            var due = (int)(clock.Elapsed.TotalSeconds * 120);
            while (sent < due)
                app.Probe.Tick.Emit(sent++);
            if (early is null && clock.Elapsed > TimeSpan.FromSeconds(Math.Min(10, seconds / 2)))
                early = Measure.Now();
            await Task.Delay(1);
        }
        app.Probe.Tick.Emit(-1);
        using var report = JsonDocument.Parse(await app.Probe.Reported.Task.WaitAsync(TimeSpan.FromSeconds(30)));
        var late = Measure.Now();
        output.WriteLine($"sent {sent} in {seconds} s: {report.RootElement}; at the start {early}, at the end {late}");

        Assert.Empty(app.Errors);
        Assert.Equal(sent, report.RootElement.GetProperty("received").GetInt32());
        Assert.Equal(0, report.RootElement.GetProperty("outOfOrder").GetInt32());
        Assert.InRange(late.PrivateMegabytes - early!.Value.PrivateMegabytes, double.MinValue, 16);
    }

    private string Page(string script)
    {
        Directory.CreateDirectory(_assets);
        File.WriteAllText(Path.Combine(_assets, "index.html"), $$"""
            <!doctype html>
            <html><head><title>Stress</title></head><body>
            <script type="module">
            {{script}}
            </script>
            </body></html>
            """);
        return _assets;
    }

    /// <summary>What the test process holds, the webview's processes aside.</summary>
    private readonly record struct Measure(int Callbacks, int Handles, double PrivateMegabytes)
    {
        public static Measure Now()
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            using var self = Process.GetCurrentProcess();
            return new(ComCallback.Live, self.HandleCount, self.PrivateMemorySize64 / (1024.0 * 1024.0));
        }

        /// <summary>
        /// Measures once WebView2 released what it will: it lets go of a closed
        /// window's callbacks from its own processes, a moment later.
        /// </summary>
        public static async Task<Measure> Settled(int? callbacks = null)
        {
            var measure = Now();
            for (var i = 0; i < 50 && (callbacks is null || measure.Callbacks > callbacks); i++)
            {
                await Task.Delay(100);
                measure = Now();
            }
            await Task.Delay(500);
            return Now();
        }

        public override string ToString() => $"{Callbacks} callbacks, {Handles} handles, {PrivateMegabytes:F1} MB private";
    }

    /// <summary>An app running on its UI thread, whose windows the test opens and closes.</summary>
    private sealed class StressApp : IAsyncDisposable
    {
        private readonly Task<int> _run;

        private StressApp(OrielApp app, Probe probe, Task<int> run)
        {
            App = app;
            Probe = probe;
            _run = run;
        }

        public OrielApp App { get; }

        public Probe Probe { get; }

        public List<Exception> Errors { get; } = [];

        public static async Task<StressApp> Start(string name, string assets, int maxCallsInFlight = 1000)
        {
            var app = new OrielApp(new AppOptions
            {
                Name = name,
                AssetsDirectory = assets,
                QuitWhenLastWindowCloses = false,
                MaxCallsInFlight = maxCallsInFlight,
            });
            var probe = new Probe();
            app.Bind(probe);
            var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var run = Task.Run(() => app.Run(ready.SetResult));
            await ready.Task.WaitAsync(TimeSpan.FromSeconds(30));
            var started = new StressApp(app, probe, run);
            app.Error += e =>
            {
                lock (started.Errors)
                    started.Errors.Add(e);
            };
            return started;
        }

        public Task<OrielWindow> Open() => OnUiThread(() => App.OpenWindow(new WindowOptions { Width = 400, Height = 300 }));

        /// <summary>Opens a window, waits for its page to load, closes it and waits for it to be gone.</summary>
        public async Task OpenAndClose()
        {
            var loaded = Probe.NextLoad;
            var window = await Open();
            await loaded.WaitAsync(TimeSpan.FromSeconds(30));
            var closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            window.Closed += (_, _) => closed.TrySetResult();
            window.Close();
            await closed.Task.WaitAsync(TimeSpan.FromSeconds(30));
        }

        public async ValueTask DisposeAsync()
        {
            App.Quit();
            Assert.Equal(0, await _run.WaitAsync(TimeSpan.FromSeconds(30)));
        }

        private Task<T> OnUiThread<T>(Func<T> f)
        {
            var done = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
            App.Dispatch(() =>
            {
                try
                {
                    done.SetResult(f());
                }
                catch (Exception e)
                {
                    done.SetException(e);
                }
            });
            return done.Task;
        }
    }
}
