using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Oriel.Generated;
using Oriel.Hosting;
using Oriel.Tests.Fakes;

namespace Oriel.Tests;

public class HostingTests
{
    /// <summary>What services depend on: disposed with their scope.</summary>
    public sealed class Tracker : IDisposable
    {
        private static int s_made;

        public Tracker()
        {
            Number = Interlocked.Increment(ref s_made);
            All[Number] = this;
        }

        /// <summary>Every tracker made, by number.</summary>
        public static ConcurrentDictionary<int, Tracker> All { get; } = new();

        public int Number { get; }

        public bool Disposed { get; private set; }

        public void Dispose() => Disposed = true;
    }

    /// <summary>A service pages call, which tells which instance and tracker ran the call.</summary>
    public sealed class Counter(Tracker tracker)
    {
        private int _calls;

        public Tracker Tracker { get; } = tracker;

        public string Count() => $"{Tracker.Number}:{Interlocked.Increment(ref _calls)}:{Tracker.Disposed}";

        public void Fail() => throw new InvalidOperationException("no luck");
    }

    private sealed class CounterDispatcher(Counter counter) : ServiceDispatcher("Counter")
    {
        public override ValueTask<ValueWriter?> InvokeAsync(string method, JsonElement args, CallContext context)
        {
            switch (method)
            {
                case "Count":
                    var result = counter.Count();
                    return ValueTask.FromResult<ValueWriter?>(w => Json.Write(w, result));
                case "Fail":
                    counter.Fail();
                    return ValueTask.FromResult<ValueWriter?>(null);
                default:
                    throw new MethodNotFoundException(Name, method);
            }
        }
    }

    static HostingTests()
    {
        ServiceRegistry.Register<Counter>(c => new CounterDispatcher(c));
    }

    private sealed class Logs : ILoggerProvider, ILogger
    {
        public ConcurrentQueue<(LogLevel Level, string Message, Exception? Error)> Entries { get; } = new();

        public ILogger CreateLogger(string categoryName) => this;

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Entries.Enqueue((logLevel, formatter(state, exception), exception));

        public void Dispose()
        {
        }
    }

    private readonly FakePlatform _platform = new();
    private readonly Logs _logs = new();

    private OrielHost Build(OrielServiceScope scope, Action<OrielAppBuilder>? configure = null)
    {
        var builder = OrielApp.CreateBuilder([]);
        builder.Platform = _platform;
        builder.Options = new AppOptions { Name = "HostingTests", AssetsDirectory = @"C:\app\wwwroot", QuitWhenLastWindowCloses = false };
        builder.Logging.ClearProviders().AddProvider(_logs);
        builder.Services.AddScoped<Tracker>();
        builder.Services.AddOrielService<Counter>(scope);
        configure?.Invoke(builder);
        return builder.Build();
    }

    /// <summary>Runs <paramref name="host"/> with two windows, calls Counter.Count from each in turn, and returns the results.</summary>
    private async Task<List<string>> CallFromWindows(OrielHost host, params int[] windows)
    {
        var opened = new[] { host.OpenWindow(), host.OpenWindow() };
        var run = host.RunAsync();
        await WaitUntil(() => _platform.Windows.Count == 2);
        var natives = _platform.Windows.ToArray();
        var results = new List<string>();
        var id = 0;
        foreach (var w in windows)
        {
            id++;
            host.App.Dispatch(() => natives[w].Events.MessageReceived("https://app.oriel/",
                $$"""{"t":"call","id":{{id}},"k":"page{{w}}","m":"Counter.Count","a":[]}"""));
            results.Add(await Reply(natives[w]));
        }
        host.App.Quit();
        Assert.Equal(0, await run.WaitAsync(TimeSpan.FromSeconds(10)));
        GC.KeepAlive(opened);
        return results;
    }

    private static async Task<string> Reply(FakeWindow window)
    {
        for (var i = 0; i < 400; i++)
        {
            if (window.Posted.TryDequeue(out var message))
            {
                using var doc = JsonDocument.Parse(message);
                return doc.RootElement.TryGetProperty("v", out var v) ? v.GetString()! : "error: " + doc.RootElement.GetProperty("e").GetString();
            }
            await Task.Delay(5);
        }
        throw new TimeoutException("no reply");
    }

    private static async Task WaitUntil(Func<bool> condition)
    {
        for (var i = 0; i < 400 && !condition(); i++)
            await Task.Delay(5);
        Assert.True(condition());
    }

    private static (int Tracker, int Calls, bool Disposed) Parse(string result)
    {
        var parts = result.Split(':');
        return (int.Parse(parts[0]), int.Parse(parts[1]), bool.Parse(parts[2]));
    }

    [Fact]
    public async Task ASingletonServesEveryCall()
    {
        await using var host = Build(OrielServiceScope.Singleton);
        var results = (await CallFromWindows(host, 0, 1, 0)).Select(Parse).ToList();

        Assert.Single(results.Select(r => r.Tracker).Distinct());
        Assert.Equal([1, 2, 3], results.Select(r => r.Calls));
    }

    [Fact]
    public async Task AServicePerCallGetsAScopeDisposedOnceTheCallIsDone()
    {
        await using var host = Build(OrielServiceScope.Call);
        var results = (await CallFromWindows(host, 0, 0, 1)).Select(Parse).ToList();

        Assert.Equal(3, results.Select(r => r.Tracker).Distinct().Count());
        Assert.All(results, r => Assert.Equal((1, false), (r.Calls, r.Disposed))); // the result was written before the scope went
        Assert.All(results, r => Assert.True(Tracker.All[r.Tracker].Disposed));
    }

    [Fact]
    public async Task AServicePerWindowLivesAsLongAsItsWindow()
    {
        await using var host = Build(OrielServiceScope.Window);
        var results = (await CallFromWindows(host, 0, 1, 0, 1)).Select(Parse).ToList();

        Assert.Equal(results[0].Tracker, results[2].Tracker);
        Assert.Equal(results[1].Tracker, results[3].Tracker);
        Assert.NotEqual(results[0].Tracker, results[1].Tracker);
        Assert.Equal([1, 1, 2, 2], results.Select(r => r.Calls));
    }

    [Fact]
    public async Task DisposesAWindowsScopeWhenItCloses()
    {
        await using var host = Build(OrielServiceScope.Window);
        var window = host.OpenWindow();
        var run = host.RunAsync();
        await WaitUntil(() => _platform.Windows.Count == 1);
        var native = _platform.Windows.Single();
        host.App.Dispatch(() => native.Events.MessageReceived("https://app.oriel/", """{"t":"call","id":1,"k":"p","m":"Counter.Count","a":[]}"""));
        var tracker = Tracker.All[Parse(await Reply(native)).Tracker];
        Assert.False(tracker.Disposed);

        native.Close();
        await WaitUntil(() => tracker.Disposed); // with the window, while the app runs on

        host.App.Quit();
        await run.WaitAsync(TimeSpan.FromSeconds(10));
    }

    [Fact]
    public async Task LogsFailedCallsAndErrors()
    {
        await using var host = Build(OrielServiceScope.Singleton);
        host.OpenWindow();
        var run = host.RunAsync();
        await WaitUntil(() => _platform.Windows.Count == 1);
        var native = _platform.Windows.Single();
        host.App.Dispatch(() => native.Events.MessageReceived("https://app.oriel/", """{"t":"call","id":1,"k":"p","m":"Counter.Fail","a":[]}"""));
        Assert.StartsWith("error:", await Reply(native));
        host.App.Dispatch(() => native.Events.Failed(new InvalidDataException("webview trouble")));

        // Waits for these two: the missing page of the test app is logged too.
        await WaitUntil(() => _logs.Entries.Any(e => e.Error is InvalidOperationException) && _logs.Entries.Any(e => e.Error is InvalidDataException));
        Assert.Contains(_logs.Entries, e => e.Level == LogLevel.Error && e.Message == "Counter.Fail failed: no luck" && e.Error is InvalidOperationException);
        Assert.Contains(_logs.Entries, e => e.Level == LogLevel.Error && e.Error is InvalidDataException);
        host.App.Quit();
        await run.WaitAsync(TimeSpan.FromSeconds(10));
    }

    private sealed class Lifecycle(IHostApplicationLifetime lifetime, OrielApp app) : IHostedService
    {
        public static ConcurrentQueue<string> Steps { get; } = new();

        public Task StartAsync(CancellationToken cancellationToken)
        {
            Steps.Enqueue($"started, app {app.Options.Name}");
            // Stopping the host quits the app.
            _ = Task.Delay(100, CancellationToken.None).ContinueWith(_ => lifetime.StopApplication(), TaskScheduler.Default);
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            Steps.Enqueue("stopped");
            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task StartsHostedServicesFirstAndStopsThemWhenTheAppQuits()
    {
        await using var host = Build(OrielServiceScope.Singleton, b => b.Services.AddHostedService<Lifecycle>());
        host.OpenWindow();

        Assert.Equal(0, await host.RunAsync().WaitAsync(TimeSpan.FromSeconds(10)));
        Assert.Equal(["started, app HostingTests", "stopped"], Lifecycle.Steps);
        Assert.Same(host.App, host.Services.GetRequiredService<OrielApp>());
    }

    [Fact]
    public void NamesTheAppAfterTheHostByDefault()
    {
        var builder = OrielApp.CreateBuilder([]);
        Assert.Equal(builder.Environment.ApplicationName, builder.Options.Name);
    }
}
