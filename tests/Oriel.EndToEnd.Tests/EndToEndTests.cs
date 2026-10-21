using System.Text.Json;

[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace Oriel.EndToEnd.Tests;

/// <summary>A test that opens windows: it runs when ORIEL_E2E=1.</summary>
public sealed class EndToEndFactAttribute : FactAttribute
{
    public EndToEndFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("ORIEL_E2E") != "1")
            Skip = "opens windows; set ORIEL_E2E=1 to run";
        else if (!OperatingSystem.IsWindows())
            Skip = "Oriel runs on Windows for now";
    }
}

/// <summary>The services the test pages call.</summary>
[Service(Name = "E2E")]
public sealed class Probe
{
    public TaskCompletionSource<string> Reported { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public TaskCompletionSource<bool> HangCanceled { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public Event<string> Pinged { get; } = new();

    public string Greet(string name) => "Hello, " + name;

    public void Fail() => throw new InvalidOperationException("failed on purpose");

    public void Ping(string payload) => Pinged.Emit(payload);

    public string WindowUrl(OrielWindow window) => window.Url.AbsolutePath;

    public async Task Hang(CancellationToken cancellation)
    {
        try
        {
            await Task.Delay(Timeout.Infinite, cancellation);
        }
        catch (OperationCanceledException)
        {
            HangCanceled.TrySetResult(true);
            throw;
        }
    }

    public void Report(string result) => Reported.TrySetResult(result);
}

public sealed class EndToEndTests : IDisposable
{
    private readonly string _assets = Path.Combine(Path.GetTempPath(), "oriel-e2e-" + Guid.NewGuid().ToString("N"));
    private readonly string _name = "Oriel E2E " + Guid.NewGuid().ToString("N")[..8];

    public EndToEndTests()
    {
        Directory.CreateDirectory(_assets);
    }

    public void Dispose()
    {
        Directory.Delete(_assets, recursive: true);
        var data = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), _name);
        for (var i = 0; i < 10 && Directory.Exists(data); i++)
        {
            try
            {
                Directory.Delete(data, recursive: true); // the webview's processes may hold files for a moment
            }
            catch (IOException)
            {
                Thread.Sleep(300);
            }
        }
    }

    /// <summary>Runs an app showing a page of <paramref name="script"/>, until the page reports; then closes it.</summary>
    private async Task<(string Report, OrielApp App, OrielWindow Window, Probe Probe)> RunPage(string script)
    {
        File.WriteAllText(Path.Combine(_assets, "index.html"), $$"""
            <!doctype html>
            <html><head><title>Test page</title></head><body>
            <script type="module">
            try {
            {{script}}
            } catch (err) {
              await oriel.call("E2E.Report", ["page error: " + err]);
            }
            </script>
            </body></html>
            """);

        var app = new OrielApp(new AppOptions { Name = _name, AssetsDirectory = _assets });
        var probe = new Probe();
        app.Bind(probe);
        var errors = new List<Exception>();
        app.Error += errors.Add;
        OrielWindow? window = null;
        var run = Task.Run(() => app.Run(() => window = app.OpenWindow(new WindowOptions { Width = 400, Height = 300 })));

        var report = await probe.Reported.Task.WaitAsync(TimeSpan.FromSeconds(30));
        Assert.Empty(errors);
        window!.Close();
        Assert.Equal(0, await run.WaitAsync(TimeSpan.FromSeconds(10)));
        return (report, app, window, probe);
    }

    [EndToEndFact]
    public async Task PagesCallServicesAndListenToTheirEvents()
    {
        var (report, _, window, _) = await RunPage("""
            const results = {};
            results.greet = await oriel.call("E2E.Greet", ["Ada"]);
            try {
              await oriel.call("E2E.Fail", []);
            } catch (err) {
              results.error = err.name + ": " + err.message;
            }
            results.event = await new Promise((resolve) => {
              const stop = oriel.on("E2E.Pinged", (payload) => { stop(); resolve(payload); });
              oriel.call("E2E.Ping", ["pong"]);
            });
            results.url = await oriel.call("E2E.WindowUrl", []);
            results.origin = location.origin;
            document.title = "Done";
            await new Promise((resolve) => setTimeout(resolve, 100));
            await oriel.call("E2E.Report", [JSON.stringify(results)]);
            """);

        using var results = JsonDocument.Parse(report);
        var root = results.RootElement;
        Assert.Equal("Hello, Ada", root.GetProperty("greet").GetString());
        Assert.Equal("CallError: failed on purpose", root.GetProperty("error").GetString());
        Assert.Equal("pong", root.GetProperty("event").GetString());
        Assert.Equal("/index.html", root.GetProperty("url").GetString());
        Assert.Equal("https://app.oriel", root.GetProperty("origin").GetString());
        Assert.Equal("Done", window.Title);
        Assert.True(window.IsClosed);
    }

    [EndToEndFact]
    public async Task ReloadingCancelsTheCallsOfThePreviousPage()
    {
        var (report, _, _, probe) = await RunPage("""
            if (!sessionStorage.getItem("reloaded")) {
              sessionStorage.setItem("reloaded", "1");
              oriel.call("E2E.Hang", []);
              setTimeout(() => location.reload(), 200);
            } else {
              await oriel.call("E2E.Report", ["reloaded"]);
            }
            """);

        Assert.Equal("reloaded", report);
        Assert.True(await probe.HangCanceled.Task.WaitAsync(TimeSpan.FromSeconds(5)));
    }

    [EndToEndFact]
    public async Task ServesTheAppsFilesBesideThePage()
    {
        File.WriteAllText(Path.Combine(_assets, "data.json"), """{"served":true}""");
        var (report, _, _, _) = await RunPage("""
            const response = await fetch("/data.json");
            await oriel.call("E2E.Report", [JSON.stringify(await response.json())]);
            """);

        Assert.Equal("""{"served":true}""", report);
    }
}
