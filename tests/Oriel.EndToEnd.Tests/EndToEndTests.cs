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

/// <summary>An error the test pages tell apart.</summary>
[ServiceError("refused")]
public sealed class RefusedException(int attempts) : Exception("refused on purpose")
{
    public int Attempts { get; } = attempts;
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

    public void Refuse() => throw new RefusedException(3);

    public void Ping(string payload) => Pinged.Emit(payload);

    public string WindowUrl(OrielWindow window) => window.Url.AbsolutePath;

    public Task<bool> SavePdf(OrielWindow window, string path, bool landscape) =>
        window.PrintToPdfAsync(path, landscape ? new PdfOptions { Landscape = true } : null);

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

    public TaskCompletionSource FirstLoad { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public TaskCompletionSource SecondLoad { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    private int _loads;

    public void Loaded()
    {
        if (Interlocked.Increment(ref _loads) == 1)
            FirstLoad.TrySetResult();
        else
            SecondLoad.TrySetResult();
    }
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
    private async Task<(string Report, OrielApp App, OrielWindow Window, Probe Probe)> RunPage(string script, Action<OrielWindow>? setUp = null)
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
        var run = Task.Run(() => app.Run(() =>
        {
            window = app.OpenWindow(new WindowOptions { Width = 400, Height = 300 });
            setUp?.Invoke(window);
        }));

        var report = await probe.Reported.Task.WaitAsync(TimeSpan.FromSeconds(30));
        Assert.Empty(errors);
        foreach (var open in app.Windows)
            open.Close();
        Assert.Equal(0, await run.WaitAsync(TimeSpan.FromSeconds(10)));
        return (report, app, window!, probe);
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
            try {
              await oriel.call("E2E.Refuse", []);
            } catch (err) {
              results.refused = err.code + " " + err.message + " " + JSON.stringify(err.data);
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
        // Outside development, pages do not get the messages of unexpected exceptions.
        Assert.Equal("CallError: E2E.Fail failed", root.GetProperty("error").GetString());
        Assert.Equal("refused refused on purpose {\"attempts\":3}", root.GetProperty("refused").GetString());
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
    public async Task ReloadsThePageWhenItsRendererIsKilled()
    {
        File.WriteAllText(Path.Combine(_assets, "index.html"), """<script type="module">await oriel.call("E2E.Loaded", []);</script>""");
        var app = new OrielApp(new AppOptions { Name = _name, AssetsDirectory = _assets });
        var probe = new Probe();
        app.Bind(probe);
        var failures = new List<WebViewFailedEventArgs>();
        OrielWindow? window = null;
        var run = Task.Run(() => app.Run(() =>
        {
            window = app.OpenWindow(new WindowOptions { Width = 400, Height = 300 });
            window.WebViewFailed += (_, e) => failures.Add(e);
        }));

        await probe.FirstLoad.Task.WaitAsync(TimeSpan.FromSeconds(30));
        Assert.True(KillRenderers() > 0, "no renderer process found");
        await probe.SecondLoad.Task.WaitAsync(TimeSpan.FromSeconds(30));

        var failure = Assert.Single(failures);
        Assert.Equal((WebViewFailure.RendererExited, WebViewRecovery.Reload), (failure.Failure, failure.Recovery));
        window!.Close();
        await run.WaitAsync(TimeSpan.FromSeconds(10));
    }

    /// <summary>
    /// Kills the renderer processes of this test's webviews: the children of
    /// the browser processes this process started. Returns how many.
    /// </summary>
    private static int KillRenderers()
    {
        var script = $$"""
            $all = Get-CimInstance Win32_Process -Filter "Name='msedgewebview2.exe'"
            $browsers = $all | Where-Object ParentProcessId -eq {{Environment.ProcessId}} | ForEach-Object ProcessId
            $renderers = $all | Where-Object { $browsers -contains $_.ParentProcessId -and $_.CommandLine -like '*--type=renderer*' }
            $renderers | ForEach-Object { Stop-Process -Id $_.ProcessId -Force }
            @($renderers).Count
            """;
        using var ps = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("powershell", ["-NoProfile", "-Command", script])
        {
            RedirectStandardOutput = true,
        })!;
        var output = ps.StandardOutput.ReadToEnd();
        ps.WaitForExit();
        return int.TryParse(output.Trim(), out var n) ? n : 0;
    }

    [EndToEndFact]
    public async Task KeepsTheWindowOnTheAppAndPagesWithoutPermissions()
    {
        File.WriteAllText(Path.Combine(_assets, "second.html"), "<!doctype html><title>Second</title>");
        var navigations = new List<(Uri Uri, bool NewWindow, NavigationAction Action)>();
        var (report, app, _, _) = await RunPage("""
            const results = {};
            const link = document.createElement("a");
            link.href = "https://example.com/";
            document.body.append(link);
            link.click();
            await new Promise((resolve) => setTimeout(resolve, 500));
            results.stayed = location.href;
            window.open("/second.html");
            await new Promise((resolve) => setTimeout(resolve, 500));
            results.notifications = await Notification.requestPermission();
            await oriel.call("E2E.Report", [JSON.stringify(results)]);
            """,
            window => window.NavigationRequested += (_, e) =>
            {
                navigations.Add((e.Uri, e.IsNewWindow, e.Action));
                if (e.Action == NavigationAction.OpenExternally)
                    e.Action = NavigationAction.Deny; // no browser opened by the tests
            });

        using var results = System.Text.Json.JsonDocument.Parse(report);
        Assert.Equal("https://app.oriel/index.html", results.RootElement.GetProperty("stayed").GetString());
        Assert.Equal("denied", results.RootElement.GetProperty("notifications").GetString());
        Assert.Contains((new Uri("https://example.com/"), false, NavigationAction.OpenExternally), navigations);
        Assert.Contains((new Uri("https://app.oriel/second.html"), true, NavigationAction.Allow), navigations);
    }

    [EndToEndFact]
    public async Task ShowsDialogsModalToTheirWindow()
    {
        File.WriteAllText(Path.Combine(_assets, "index.html"), """<script type="module">await oriel.call("E2E.Loaded", []);</script>""");
        var app = new OrielApp(new AppOptions { Name = _name, AssetsDirectory = _assets });
        var probe = new Probe();
        app.Bind(probe);
        var errors = new List<Exception>();
        app.Error += errors.Add;
        OrielWindow? window = null;
        var run = Task.Run(() => app.Run(() => window = app.OpenWindow(new WindowOptions { Title = "Dialogs", Width = 600, Height = 400 })));
        await probe.FirstLoad.Task.WaitAsync(TimeSpan.FromSeconds(30));

        // Each dialog is found on screen, then closed as a person would.
        var open = window!.Dialogs.OpenFileAsync(new() { Title = "Oriel open test", Filters = [new("Text", "*.txt")], Directory = _assets });
        var dialog = await Dialog.Find("Oriel open test");
        Assert.Equal("OrielWindow", Dialog.OwnerClass(dialog));
        Dialog.Post(dialog, Dialog.WM_CLOSE, 0);
        Assert.Null(await open.WaitAsync(TimeSpan.FromSeconds(10)));

        // OK takes the name proposed, given whole; the extension comes from the filter.
        var save = window.Dialogs.SaveFileAsync(new() { Title = "Oriel save test", FileName = Path.Combine(_assets, "notes"), Filters = [new("Text", "*.txt")] });
        dialog = await Dialog.Find("Oriel save test");
        for (var i = 0; i < 20 && !save.IsCompleted; i++)
        {
            // The dialog takes commands once it has finished starting.
            Dialog.Post(dialog, Dialog.WM_COMMAND, Dialog.IDOK);
            await Task.WhenAny(save, Task.Delay(500));
        }
        Assert.Equal(Path.Combine(_assets, "notes.txt"), await save.WaitAsync(TimeSpan.FromSeconds(10)), ignoreCase: true);

        var folder = window.Dialogs.PickFolderAsync(new() { Title = "Oriel folder test" });
        Dialog.Post(await Dialog.Find("Oriel folder test"), Dialog.WM_CLOSE, 0);
        Assert.Null(await folder.WaitAsync(TimeSpan.FromSeconds(10)));

        var message = window.Dialogs.MessageAsync(new() { Text = "Keep going?", Kind = MessageKind.Question, Buttons = MessageButtons.YesNo });
        dialog = await Dialog.Find("Dialogs"); // titled after its window
        Assert.Equal("OrielWindow", Dialog.OwnerClass(dialog));
        Dialog.Post(dialog, Dialog.WM_COMMAND, Dialog.IDNO);
        Assert.Equal(MessageResult.No, await message.WaitAsync(TimeSpan.FromSeconds(10)));

        window.Close();
        await run.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Empty(errors);
    }

    /// <summary>Finds and drives the system's dialogs of this process, as a person would.</summary>
    private static class Dialog
    {
        public const uint WM_CLOSE = 0x0010;
        public const uint WM_COMMAND = 0x0111;
        public const nint IDOK = 1;
        public const nint IDNO = 7;

        /// <summary>The dialog of this process titled <paramref name="title"/>, once it shows.</summary>
        public static async Task<nint> Find(string title)
        {
            for (var i = 0; i < 100; i++)
            {
                for (nint h = 0; (h = FindWindowExW(0, h, "#32770", null)) != 0;)
                {
                    GetWindowThreadProcessId(h, out var pid);
                    var text = new System.Text.StringBuilder(256);
                    GetWindowTextW(h, text, text.Capacity);
                    if (pid == Environment.ProcessId && text.ToString() == title)
                        return h;
                }
                await Task.Delay(100);
            }
            throw new TimeoutException($"no dialog \"{title}\" showed");
        }

        /// <summary>The window class of the dialog's owner.</summary>
        public static string OwnerClass(nint dialog)
        {
            var name = new System.Text.StringBuilder(256);
            GetClassNameW(GetWindow(dialog, 4 /* GW_OWNER */), name, name.Capacity);
            return name.ToString();
        }

        public static void Post(nint dialog, uint msg, nint wParam) => PostMessageW(dialog, msg, wParam, 0);

        [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        private static extern nint FindWindowExW(nint parent, nint after, string className, string? title);

        [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        private static extern int GetWindowTextW(nint hwnd, System.Text.StringBuilder text, int max);

        [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        private static extern int GetClassNameW(nint hwnd, System.Text.StringBuilder name, int max);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(nint hwnd, out int pid);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern nint GetWindow(nint hwnd, uint command);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool PostMessageW(nint hwnd, uint msg, nint wParam, nint lParam);
    }

    [EndToEndFact]
    public async Task PrintsThePageToPdfThroughAService()
    {
        var portrait = Path.Combine(_assets, "portrait.pdf");
        var landscape = Path.Combine(_assets, "landscape.pdf");
        var missing = Path.Combine(_assets, "no such folder", "a.pdf");
        var (report, _, _, _) = await RunPage($$"""
            document.body.innerHTML = "<h1>Invoice 42</h1>";
            const results = [
              await oriel.call("E2E.SavePdf", [{{JsonSerializer.Serialize(portrait)}}, false]),
              await oriel.call("E2E.SavePdf", [{{JsonSerializer.Serialize(landscape)}}, true]),
              await oriel.call("E2E.SavePdf", [{{JsonSerializer.Serialize(missing)}}, false]),
            ];
            await oriel.call("E2E.Report", [JSON.stringify(results)]);
            """);

        Assert.Equal("[true,true,false]", report);
        var pages = new[] { portrait, landscape }.Select(path =>
        {
            var text = System.Text.Encoding.Latin1.GetString(File.ReadAllBytes(path));
            Assert.StartsWith("%PDF-", text);
            return System.Text.RegularExpressions.Regex.Match(text, @"/MediaBox\s*\[\s*0\s+0\s+([\d.]+)\s+([\d.]+)").Groups;
        }).ToArray();
        // US Letter, 612 by 792 points, then turned.
        Assert.Equal(("612", "792"), (pages[0][1].Value, pages[0][2].Value));
        Assert.Equal(("792", "612"), (pages[1][1].Value, pages[1][2].Value));
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

    [EndToEndFact]
    public async Task ServesAFrontendEmbeddedInTheExecutable()
    {
        // The test assembly embeds Embedded/ as OrielEmbedFrontend would: no folder at all.
        var app = new OrielApp(new AppOptions { Name = _name, AssetsAssembly = typeof(Probe).Assembly });
        var probe = new Probe();
        app.Bind(probe);
        var errors = new List<Exception>();
        app.Error += errors.Add;
        var run = Task.Run(() => app.Run(() => app.OpenWindow(new WindowOptions { Width = 400, Height = 300 })));

        var report = await probe.Reported.Task.WaitAsync(TimeSpan.FromSeconds(30));
        foreach (var open in app.Windows)
            open.Close();
        Assert.Equal(0, await run.WaitAsync(TimeSpan.FromSeconds(10)));
        Assert.Empty(errors);

        Assert.DoesNotContain("page error", report);
        using var results = System.Text.Json.JsonDocument.Parse(report);
        var types = results.RootElement.GetProperty("types");
        Assert.Equal("200 text/html; charset=utf-8", types.GetProperty("index.html").GetString());
        Assert.Equal("200 text/javascript; charset=utf-8", types.GetProperty("app.js").GetString());
        Assert.Equal("200 text/css; charset=utf-8", types.GetProperty("assets/style.css").GetString());
        Assert.Equal("200 image/svg+xml", types.GetProperty("assets/pic.svg").GetString());
        Assert.Equal("200 application/wasm", types.GetProperty("assets/empty.wasm").GetString());
        Assert.Equal("200 font/woff2", types.GetProperty("assets/font.woff2").GetString());
        Assert.Equal(404, results.RootElement.GetProperty("missing").GetInt32());
        Assert.True(results.RootElement.GetProperty("wasm").GetBoolean());
        Assert.Equal("rgb(1, 2, 3)", results.RootElement.GetProperty("color").GetString());
        Assert.Equal("https://app.oriel", results.RootElement.GetProperty("origin").GetString());
    }
}
