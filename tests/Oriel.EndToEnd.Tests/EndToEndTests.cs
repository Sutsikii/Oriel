using System.Text.Json;
using Oriel.Platform;

[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace Oriel.EndToEnd.Tests;

/// <summary>
/// A test that opens windows: it runs when ORIEL_E2E=1, on Windows, on
/// Linux when <see cref="Linux"/> says so, and on macOS when <see cref="MacOS"/> does.
/// </summary>
public sealed class EndToEndFactAttribute : FactAttribute
{
    private bool _linux;
    private bool _macOS;

    public EndToEndFactAttribute() => Decide();

    /// <summary>Whether the Linux backend has what the test uses.</summary>
    public bool Linux
    {
        get => _linux;
        set
        {
            _linux = value;
            Decide();
        }
    }

    /// <summary>Whether the macOS backend has what the test uses.</summary>
    public bool MacOS
    {
        get => _macOS;
        set
        {
            _macOS = value;
            Decide();
        }
    }

    private void Decide()
    {
        if (Environment.GetEnvironmentVariable("ORIEL_E2E") != "1")
            Skip = "opens windows; set ORIEL_E2E=1 to run";
        else if (OperatingSystem.IsLinux())
            Skip = _linux ? null : "uses what the Linux backend does not have yet";
        else if (OperatingSystem.IsMacOS())
            Skip = _macOS ? null : "uses what the macOS backend does not have yet";
        else if (!OperatingSystem.IsWindows())
            Skip = "Oriel runs on Windows, Linux and macOS";
        else
            Skip = null;
    }
}

/// <summary>What differs between the systems for the test pages.</summary>
public static class Pages
{
    /// <summary>The origin of the app's pages, as they see it: WebKit serves them at oriel://.</summary>
    public static string Origin => OperatingSystem.IsLinux() || OperatingSystem.IsMacOS() ? "oriel://app.oriel" : "https://app.oriel";
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

    public Event<int> Moved { get; } = new() { Coalesce = true };

    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, SharedStream> _shared = new();

    /// <summary>Shares <paramref name="length"/> bytes, byte i being i % 251; seekable or not.</summary>
    public string ShareBytes(OrielWindow window, int length, bool seekable)
    {
        var bytes = new byte[length];
        for (var i = 0; i < length; i++)
            bytes[i] = (byte)(i % 251);
        var shared = window.App.Share(() => seekable ? new MemoryStream(bytes, writable: false) : new ForwardOnly(new MemoryStream(bytes, writable: false)), "application/octet-stream");
        _shared[shared.Url] = shared;
        return shared.Url;
    }

    /// <summary>0, 0.5, 1… as binary data.</summary>
    public ReadOnlyMemory<float> Ramp(int count)
    {
        var values = new float[count];
        for (var i = 0; i < count; i++)
            values[i] = i * 0.5f;
        return values;
    }

    /// <summary>Shares Media/count.mp4: six seconds, a key frame every second.</summary>
    public string ShareVideo(OrielWindow window) =>
        window.App.ShareFile(Path.Combine(AppContext.BaseDirectory, "Media", "count.mp4")).Url;

    public void Unshare(string url)
    {
        if (_shared.TryRemove(url, out var shared))
            shared.Dispose();
    }

    public TaskCompletionSource<int> Endless { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>1 to <paramref name="to"/>.</summary>
    public async IAsyncEnumerable<int> Count(int to)
    {
        for (var i = 1; i <= to; i++)
        {
            await Task.Yield();
            yield return i;
        }
    }

    /// <summary>1, 2, 3… until the page stops; <see cref="Endless"/> gets how many it made.</summary>
    public async IAsyncEnumerable<int> Forever([System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellation = default)
    {
        var made = 0;
        try
        {
            while (true)
            {
                await Task.Delay(1, cancellation);
                yield return ++made;
            }
        }
        finally
        {
            Endless.TrySetResult(made);
        }
    }

    /// <summary>Emits <see cref="Moved"/> 1 to <paramref name="count"/>, faster than any page takes them.</summary>
    public void Burst(int count)
    {
        for (var i = 1; i <= count; i++)
            Moved.Emit(i);
    }

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

    /// <summary>Clicks an item of <paramref name="role"/> in the window's menu, as the person would.</summary>
    public void Click(OrielWindow window, string role) =>
        window.App.Dispatch(() => ((IPlatformWindowEvents)window).MenuItemClicked(new MenuItem(Enum.Parse<MenuRole>(role))));

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

/// <summary>A stream that reads forward only, as from the network.</summary>
internal sealed class ForwardOnly(Stream inner) : Stream
{
    public override bool CanRead => true;

    public override bool CanSeek => false;

    public override bool CanWrite => false;

    public override long Length => throw new NotSupportedException();

    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

    public override int Read(byte[] buffer, int offset, int count) => inner.Read(buffer, offset, count);

    public override void Flush()
    {
    }

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            inner.Dispose();
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
        var roaming = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), _name);
        if (Directory.Exists(roaming))
            Directory.Delete(roaming, recursive: true);
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
    /// <remarks><paramref name="drive"/> acts on the page from outside while it runs; its failure ends the test at once.</remarks>
    private async Task<(string Report, OrielApp App, OrielWindow Window, Probe Probe)> RunPage(string script, Action<OrielWindow>? setUp = null, Menu? menu = null, Func<OrielWindow, Task>? drive = null)
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
            window = app.OpenWindow(new WindowOptions { Width = 400, Height = 300, Menu = menu });
            setUp?.Invoke(window);
        }));

        var driving = drive is null ? null : Task.Run(async () =>
        {
            while (window is null)
                await Task.Delay(10);
            await drive(window);
        });
        if (driving is not null && await Task.WhenAny(probe.Reported.Task, driving).WaitAsync(TimeSpan.FromSeconds(30)) == driving)
            await driving; // throws what failed
        var report = await probe.Reported.Task.WaitAsync(TimeSpan.FromSeconds(30));
        if (driving is not null)
            await driving;
        Assert.False(report.StartsWith("page error: ", StringComparison.Ordinal), report);
        Assert.Empty(errors);
        foreach (var open in app.Windows)
            open.Close();
        Assert.Equal(0, await run.WaitAsync(TimeSpan.FromSeconds(10)));
        return (report, app, window!, probe);
    }

    [EndToEndFact(Linux = true, MacOS = true)]
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
        Assert.Equal(Pages.Origin, root.GetProperty("origin").GetString());
        Assert.Equal("Done", window.Title);
        Assert.True(window.IsClosed);
    }

    [EndToEndFact(Linux = true, MacOS = true)]
    public async Task PagesFetchSharedStreamsWithRanges()
    {
        var (report, _, _, _) = await RunPage("""
            const r = {};
            const url = await oriel.call("E2E.ShareBytes", [3000000, true]);
            const whole = new Uint8Array(await (await fetch(url)).arrayBuffer());
            r.length = whole.length;
            r.intact = whole.every((b, i) => b === i % 251);
            const part = await fetch(url, { headers: { Range: "bytes=1000-1004" } });
            r.part = [part.status, part.headers.get("Content-Range"), [...new Uint8Array(await part.arrayBuffer())]];
            const tail = await fetch(url, { headers: { Range: "bytes=-3" } });
            r.tail = [tail.status, [...new Uint8Array(await tail.arrayBuffer())]];
            r.past = (await fetch(url, { headers: { Range: "bytes=9000000-" } })).status;
            const flowing = await oriel.call("E2E.ShareBytes", [100000, false]);
            const unseekable = await fetch(flowing, { headers: { Range: "bytes=0-9" } });
            r.unseekable = [unseekable.status, (await unseekable.arrayBuffer()).byteLength];
            await oriel.call("E2E.Unshare", [url]);
            r.gone = (await fetch(url)).status;
            await oriel.call("E2E.Report", [JSON.stringify(r)]);
            """);

        using var results = JsonDocument.Parse(report);
        var root = results.RootElement;
        Assert.Equal(3000000, root.GetProperty("length").GetInt32());
        Assert.True(root.GetProperty("intact").GetBoolean());
        Assert.Equal("""[206,"bytes 1000-1004/3000000",[247,248,249,250,0]]""", root.GetProperty("part").GetRawText());
        Assert.Equal($"[206,[{(2999997 % 251)},{(2999998 % 251)},{(2999999 % 251)}]]", root.GetProperty("tail").GetRawText());
        Assert.Equal(416, root.GetProperty("past").GetInt32());
        Assert.Equal("[200,100000]", root.GetProperty("unseekable").GetRawText()); // no ranges: the whole stream
        Assert.Equal(404, root.GetProperty("gone").GetInt32());
    }

    [EndToEndFact] // the clipboard is in the Windows backend only
    public async Task CopiesAndPastesThroughTheClipboard()
    {
        var app = new OrielApp(new AppOptions { Name = _name, AssetsDirectory = _assets });
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var run = Task.Run(() => app.Run(started.SetResult));
        await started.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var clipboard = app.Clipboard;
        var saved = await clipboard.GetTextAsync(); // the person's, put back after
        try
        {
            await clipboard.SetTextAsync("Oriel ✓");
            Assert.Equal("Oriel ✓", await clipboard.GetTextAsync());

            await clipboard.SetHtmlAsync("<b>bold</b>", "bold");
            Assert.Equal("<b>bold</b>", await clipboard.GetHtmlAsync());
            Assert.Equal("bold", await clipboard.GetTextAsync());
            Assert.Null(await clipboard.GetImageAsync());

            var rgba = new byte[] { 255, 0, 0, 255, 0, 0, 255, 128 };
            var png = Oriel.Platform.Png.Encode(2, 1, rgba);
            await clipboard.SetImageAsync(png);
            Assert.Equal(png, await clipboard.GetImageAsync());

            var file = Path.Combine(_assets, "copied.txt");
            File.WriteAllText(file, "x");
            await clipboard.SetFilesAsync([file]);
            Assert.Equal([file], await clipboard.GetFilesAsync());

            await clipboard.ClearAsync();
            Assert.Null(await clipboard.GetTextAsync());
            Assert.Null(await clipboard.GetFilesAsync());
        }
        finally
        {
            if (saved is not null)
                await clipboard.SetTextAsync(saved);
            app.Quit();
            await run.WaitAsync(TimeSpan.FromSeconds(10));
        }
    }

    [EndToEndFact(Linux = true, MacOS = true)]
    public async Task PagesGetBinaryResultsAsTypedArrays()
    {
        var (report, _, _, _) = await RunPage("""
            const ramp = await oriel.call("E2E.Ramp", [2500000]);
            const empty = await oriel.call("E2E.Ramp", [0]);
            let ok = true;
            for (let i = 0; i < ramp.length; i++) if (ramp[i] !== i * 0.5) { ok = false; break; }
            ramp[0] = 42; // the page may write it
            await oriel.call("E2E.Report", [JSON.stringify({ type: ramp.constructor.name, length: ramp.length, ok, first: ramp[0], empty: empty.length })]);
            """);

        Assert.Equal("""{"type":"Float32Array","length":2500000,"ok":true,"first":42,"empty":0}""", report);
    }

    [EndToEndFact(MacOS = true)] // Linux: the container has no GStreamer codecs
    public async Task VideosOfSharedStreamsSeek()
    {
        var (report, _, _, _) = await RunPage("""
            const video = document.createElement("video");
            video.muted = true;
            video.src = await oriel.call("E2E.ShareVideo", []);
            document.body.append(video);
            await new Promise((resolve, reject) => {
              video.onloadedmetadata = resolve;
              video.onerror = () => reject(new Error("video error " + (video.error && video.error.code)));
            });
            const duration = video.duration;
            video.currentTime = 4.5;
            await new Promise((resolve) => (video.onseeked = resolve));
            await oriel.call("E2E.Report", [JSON.stringify({ duration, time: video.currentTime, ready: video.readyState })]);
            """);

        using var results = JsonDocument.Parse(report);
        Assert.InRange(results.RootElement.GetProperty("duration").GetDouble(), 5.9, 6.1);
        Assert.InRange(results.RootElement.GetProperty("time").GetDouble(), 4.4, 4.6);
    }

    [EndToEndFact(Linux = true, MacOS = true)]
    public async Task PagesIterateStreamsAndStopThem()
    {
        var (report, _, _, probe) = await RunPage("""
            let sum = 0;
            for await (const n of oriel.stream("E2E.Count", [1000])) sum += n;
            let last = 0;
            for await (const n of oriel.stream("E2E.Forever", [])) {
              last = n;
              if (n === 50) break;
            }
            await oriel.call("E2E.Report", [JSON.stringify({ sum, last })]);
            """);

        using var results = JsonDocument.Parse(report);
        Assert.Equal(500500, results.RootElement.GetProperty("sum").GetInt32());
        Assert.Equal(50, results.RootElement.GetProperty("last").GetInt32());
        // The producer stopped, no further ahead than the page's window.
        Assert.InRange(await probe.Endless.Task.WaitAsync(TimeSpan.FromSeconds(10)), 50, 50 + 16);
    }

    [EndToEndFact(Linux = true, MacOS = true)]
    public async Task PagesGetTheLatestPayloadOfCoalescedEvents()
    {
        var (report, _, _, _) = await RunPage("""
            const got = [];
            await new Promise((resolve) => {
              oriel.on("E2E.Moved", (n) => {
                got.push(n);
                if (n === 100000) resolve();
              });
              oriel.call("E2E.Burst", [100000]);
            });
            await oriel.call("E2E.Report", [JSON.stringify(got)]);
            """);

        var got = JsonSerializer.Deserialize<int[]>(report)!;
        Assert.Equal(100000, got[^1]);
        Assert.InRange(got.Length, 2, 100); // the first, maybe a few, then the latest
        Assert.Equal(got.Order(), got);
    }

    [EndToEndFact]
    public async Task AcceleratorsRunTheMenuUnlessThePageHandlesThem()
    {
        var clicks = new List<string>();
        void Clicked(string what)
        {
            lock (clicks)
                clicks.Add(what);
        }
        var menu = new Menu(new MenuItem("&File")
        {
            Items =
            [
                new MenuItem("&Save", "Ctrl+S") { Clicked = _ => Clicked("save") },
                new MenuItem("&Find", "Ctrl+K") { Clicked = _ => Clicked("find") },
                new MenuItem("&Refresh", "F5") { Clicked = _ => Clicked("refresh") },
            ],
        });

        var (report, _, _, _) = await RunPage("""
            // The page keeps Ctrl+K, as an editor keeps Ctrl+Z.
            addEventListener("keydown", (e) => { if (e.ctrlKey && e.keyCode === 75) e.preventDefault(); });
            const press = (keyCode, ctrlKey) => {
              const e = new KeyboardEvent("keydown", { ctrlKey, bubbles: true, cancelable: true });
              Object.defineProperty(e, "keyCode", { get: () => keyCode });
              document.body.dispatchEvent(e);
            };
            press(75, true);
            press(83, true);
            press(116, false);
            press(83, false); // a plain S is the page's
            await new Promise((resolve) => setTimeout(resolve, 200));
            await oriel.call("E2E.Report", ["pressed"]);
            """, menu: menu);

        Assert.Equal("pressed", report);
        Assert.Equal(["save", "refresh"], clicks);
    }

    /// <summary>
    /// Runs an app with one window of <paramref name="options"/>, calls
    /// <paramref name="act"/> once its page loaded, then closes it.
    /// </summary>
    private async Task RunWindow(WindowOptions options, Func<OrielApp, OrielWindow, Task> act)
    {
        File.WriteAllText(Path.Combine(_assets, "index.html"), """
            <!doctype html><html><body><script type="module">await oriel.call("E2E.Loaded", []);</script></body></html>
            """);
        var app = new OrielApp(new AppOptions { Name = _name, AssetsDirectory = _assets });
        var probe = new Probe();
        app.Bind(probe);
        var errors = new List<Exception>();
        app.Error += errors.Add;
        var opened = new TaskCompletionSource<OrielWindow>(TaskCreationOptions.RunContinuationsAsynchronously);
        var run = Task.Run(() => app.Run(() => opened.SetResult(app.OpenWindow(options))));
        var window = await opened.Task.WaitAsync(TimeSpan.FromSeconds(30));
        await probe.FirstLoad.Task.WaitAsync(TimeSpan.FromSeconds(30));
        await act(app, window);
        window.Close();
        Assert.Equal(0, await run.WaitAsync(TimeSpan.FromSeconds(10)));
        Assert.Empty(errors);
    }

    /// <summary>Waits until <paramref name="done"/> holds, as windows move in steps.</summary>
    private static async Task Until(Func<bool> done, string what)
    {
        for (var i = 0; i < 100 && !done(); i++)
            await Task.Delay(50);
        Assert.True(done(), what);
    }

    [EndToEndFact(MacOS = true)] // Linux: maximizing needs a window manager, which Xvfb has not
    public async Task APlacementComesBackAcrossRuns()
    {
        var options = new WindowOptions { Width = 500, Height = 400, StateKey = "main" };
        WindowBounds moved = default;
        await RunWindow(options, async (_, window) =>
        {
            window.Move(123, 77);
            window.Resize(640, 480);
            await Until(() => window.Bounds is { X: 123, Y: 77, Width: 640, Height: 480 }, $"moved, at {window.Bounds}");
            moved = window.Bounds;
        });

        await RunWindow(options, async (_, window) =>
        {
            await Until(() => window.Bounds == moved, $"back at {moved}, at {window.Bounds}");
            Assert.Equal(WindowState.Normal, window.State);
            var maximized = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            window.StateChanged += (_, _) =>
            {
                if (window.State == WindowState.Maximized)
                    maximized.TrySetResult();
            };
            window.Maximize();
            await maximized.Task.WaitAsync(TimeSpan.FromSeconds(5));
        });

        await RunWindow(options, async (_, window) =>
        {
            await Until(() => window.State == WindowState.Maximized, "maximized again");
            window.Restore();
            await Until(() => window.State == WindowState.Normal && window.Bounds == moved, $"restored to {moved}, at {window.Bounds}");
        });
    }

    [EndToEndFact(MacOS = true)]
    public async Task FullScreenComesBackWhereItWas()
    {
        await RunWindow(new WindowOptions { Width = 500, Height = 400, X = 200, Y = 150 }, async (_, window) =>
        {
            await Until(() => window.Bounds is { X: 200, Y: 150, Width: 500, Height: 400 }, $"opened at {window.Bounds}");
            var resized = 0;
            window.Resized += (_, _) => resized++;
            window.IsFullScreen = true;
            await Task.Delay(300);
            Assert.Equal(new WindowBounds(200, 150, 500, 400), window.Bounds); // full screen is not a placement
            window.IsFullScreen = false;
            await Task.Delay(300);
            Assert.Equal(new WindowBounds(200, 150, 500, 400), window.Bounds);
            Assert.Equal(0, resized);
        });
    }

    [EndToEndFact(MacOS = true)]
    public async Task AFramelessWindowFillsItselfWithThePage()
    {
        await RunWindow(new WindowOptions { Width = 500, Height = 400, Frameless = true, Center = true, Menu = new Menu(new MenuItem("&File")) }, async (_, window) =>
        {
            await Until(() => window.Bounds is { Width: 500, Height: 400 }, $"sized, at {window.Bounds}");
            window.Maximize();
            await Until(() => window.State == WindowState.Maximized, "maximized");
            window.Restore();
            await Until(() => window.State == WindowState.Normal && window.Bounds is { Width: 500, Height: 400 }, $"restored, at {window.Bounds}");
        });
    }

    [EndToEndFact]
    public async Task DroppedFilesReachTheAppAndThePageWithTheirPaths()
    {
        var files = new[] { Path.Combine(_assets, "dropped one.txt"), Path.Combine(_assets, "dropped-two.csv") };
        foreach (var file in files)
            File.WriteAllText(file, "dropped");
        var dropped = new List<FilesDroppedEventArgs>();
        var (report, _, _, _) = await RunPage("""
            const results = {};
            document.addEventListener("drop", (e) => { results.pageFiles = Array.from(e.dataTransfer.files, (f) => f.name); });
            const drop = await new Promise((resolve) => {
              oriel.on("oriel:filesDropped", resolve);
              document.title = "Ready";
            });
            Object.assign(results, drop);
            await oriel.call("E2E.Report", [JSON.stringify(results)]);
            """,
            window => window.FilesDropped += (_, e) => dropped.Add(e),
            drive: window => DragFiles(window, files, x: 120, y: 80));

        using var results = JsonDocument.Parse(report);
        var root = results.RootElement;
        Assert.Equal(files, root.GetProperty("paths").EnumerateArray().Select(p => p.GetString()));
        Assert.Equal(120, root.GetProperty("x").GetDouble());
        Assert.Equal(80, root.GetProperty("y").GetDouble());
        Assert.Equal(["dropped one.txt", "dropped-two.csv"], root.GetProperty("pageFiles").EnumerateArray().Select(p => p.GetString()));
        var drop = Assert.Single(dropped);
        Assert.Equal(files, drop.Paths);
        Assert.Equal((120.0, 80.0), (drop.X, drop.Y));
    }

    /// <summary>
    /// Drags <paramref name="files"/> onto the page, once its title says it
    /// is ready, as the system would: through the DevTools protocol of the
    /// webview.
    /// </summary>
    private static async Task DragFiles(OrielWindow window, string[] files, double x, double y)
    {
        for (var i = 0; i < 200 && window.Title != "Ready"; i++)
            await Task.Delay(50);
        Assert.Equal("Ready", window.Title);
        var data = new Dictionary<string, object> { ["items"] = Array.Empty<object>(), ["files"] = files, ["dragOperationsMask"] = 1 };
        foreach (var type in new[] { "dragEnter", "dragOver", "drop" })
        {
            var parameters = JsonSerializer.Serialize(new Dictionary<string, object> { ["type"] = type, ["x"] = x, ["y"] = y, ["data"] = data });
            await window.CallDevToolsAsync("Input.dispatchDragEvent", parameters).WaitAsync(TimeSpan.FromSeconds(10));
        }
    }

    [EndToEndFact(MacOS = true)]
    public async Task EditingRolesEditThePage()
    {
        var (report, _, _, _) = await RunPage("""
            const field = document.createElement("input");
            field.value = "some text";
            document.body.append(field);
            field.focus();
            const after = async (role) => {
              await oriel.call("E2E.Click", [role]);
              await new Promise((resolve) => setTimeout(resolve, 300));
              return field.value;
            };
            // Chromium drops input until the page has painted.
            await new Promise((resolve) => requestAnimationFrame(() => requestAnimationFrame(resolve)));
            field.setSelectionRange(0, 0);
            await after("SelectAll");
            const selected = field.selectionEnd - field.selectionStart;
            const deleted = await after("Delete");
            const undone = await after("Undo");
            await oriel.call("E2E.Report", [JSON.stringify({ selected, deleted, undone })]);
            """, menu: new Menu(new MenuItem("&Edit") { Items = [new MenuItem(MenuRole.Undo)] }));

        Assert.Equal("""{"selected":9,"deleted":"","undone":"some text"}""", report);
    }

    [EndToEndFact(Linux = true, MacOS = true)]
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

    [EndToEndFact(MacOS = true)]
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
        if (OperatingSystem.IsMacOS())
            return KillWebContentProcesses();
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

    /// <summary>
    /// Kills WebKit's web content processes started after this process, on
    /// macOS, where launchd starts them, not the app: those of this test's
    /// webviews, on a machine that runs nothing else in WebKit meanwhile.
    /// </summary>
    private static int KillWebContentProcesses()
    {
        using var pgrep = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("pgrep", ["-f", "com.apple.WebKit.WebContent"])
        {
            RedirectStandardOutput = true,
        })!;
        var output = pgrep.StandardOutput.ReadToEnd();
        pgrep.WaitForExit();
        var started = System.Diagnostics.Process.GetCurrentProcess().StartTime;
        var killed = 0;
        foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            using var process = System.Diagnostics.Process.GetProcessById(int.Parse(line.Trim(), System.Globalization.CultureInfo.InvariantCulture));
            if (process.StartTime < started)
                continue;
            process.Kill();
            killed++;
        }
        return killed;
    }

    [EndToEndFact(Linux = true, MacOS = true)]
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
        Assert.Equal(Pages.Origin + "/index.html", results.RootElement.GetProperty("stayed").GetString());
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

    [EndToEndFact(Linux = true, MacOS = true)]
    public async Task ServesTheAppsFilesBesideThePage()
    {
        File.WriteAllText(Path.Combine(_assets, "data.json"), """{"served":true}""");
        var (report, _, _, _) = await RunPage("""
            const response = await fetch("/data.json");
            await oriel.call("E2E.Report", [JSON.stringify(await response.json())]);
            """);

        Assert.Equal("""{"served":true}""", report);
    }

    [EndToEndFact(Linux = true, MacOS = true)]
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
        Assert.Equal(Pages.Origin, results.RootElement.GetProperty("origin").GetString());
    }
}
