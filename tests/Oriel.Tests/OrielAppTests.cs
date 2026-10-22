using System.Text.Json;
using Oriel.Generated;
using Oriel.Platform;
using Oriel.Tests.Fakes;

namespace Oriel.Tests;

public class OrielAppTests
{
    /// <summary>A service, with the dispatcher the generator would write for it.</summary>
    public sealed class Greeter
    {
        public Event<string> Greeted { get; } = new();

        public string Greet(string name)
        {
            Greeted.Emit(name);
            return "Hello, " + name;
        }
    }

    private sealed class GreeterDispatcher(Greeter greeter) : ServiceDispatcher("Greeter")
    {
        public override ValueTask<ValueWriter?> InvokeAsync(string method, JsonElement args, CallContext context)
        {
            if (method != "Greet")
                throw new MethodNotFoundException(Name, method);
            var result = greeter.Greet(Arguments.Read(args, 0, "name", Json.ReadString));
            return ValueTask.FromResult<ValueWriter?>(w => Json.Write(w, result));
        }

        public override void Attach(IEventSink sink) => greeter.Greeted.Attach("Greeter.Greeted", Json.Write, sink);
    }

    static OrielAppTests()
    {
        ServiceRegistry.Register<Greeter>(g => new GreeterDispatcher(g));
    }

    private readonly FakePlatform _platform = new();

    private OrielApp App(string? devUrl = null, string assets = @"C:\app\wwwroot") =>
        new(new AppOptions { Name = "OrielTests", AssetsDirectory = assets }, _platform, devUrl);

    /// <summary>Runs <paramref name="app"/> on its UI thread, calling <paramref name="ready"/> there.</summary>
    private static Task<int> RunAsync(OrielApp app, Action ready) =>
        Task.Run(() => app.Run(ready)).WaitAsync(TimeSpan.FromSeconds(10));

    private FakeWindow OnlyWindow() => Assert.Single(_platform.Windows);

    private static async Task<string> NextPosted(FakeWindow window)
    {
        for (var i = 0; i < 400; i++)
        {
            if (window.Posted.TryDequeue(out var message))
                return message;
            await Task.Delay(5);
        }
        throw new TimeoutException("nothing was posted to the page");
    }

    /// <summary>The next reply to a call posted to the page, past the events.</summary>
    private static async Task<string> NextReply(FakeWindow window)
    {
        while (true)
        {
            var message = await NextPosted(window);
            if (!message.StartsWith("""{"t":"event""", StringComparison.Ordinal))
                return message;
        }
    }

    [Fact]
    public async Task RunsReadyOnTheUiThreadAndQuitsWithTheLastWindow()
    {
        var app = App();
        var readyThread = 0;
        var run = RunAsync(app, () =>
        {
            readyThread = Environment.CurrentManagedThreadId;
            app.OpenWindow();
        });

        await WaitUntil(() => _platform.Windows.Count == 1);
        Assert.Equal(_platform.UiThread, readyThread);
        app.Windows[0].Close();
        Assert.Equal(0, await run);
        Assert.Empty(app.Windows);
    }

    [Fact]
    public async Task OpensTheAppsOwnPagesWithTheBridge()
    {
        var app = App();
        var run = RunAsync(app, () => app.OpenWindow(new WindowOptions { Url = "/settings/", Width = 640, Height = 480 }));
        await WaitUntil(() => _platform.Windows.Count == 1);

        var options = OnlyWindow().Options;
        Assert.Equal(new Uri("https://app.oriel/settings/index.html"), options.Url);
        Assert.Equal(@"C:\app\wwwroot", options.AssetsFolder);
        Assert.Equal((640, 480), (options.Width, options.Height));
        Assert.Contains("window.oriel", options.BridgeScript.Replace("`window.oriel`", "window.oriel"));
        Assert.False(options.DevTools);

        app.Quit();
        await run;
    }

    [Fact]
    public async Task OpensPagesOfTheDevServerInDevelopment()
    {
        var app = App(devUrl: "http://localhost:5173");
        Assert.True(app.IsDev);
        var run = RunAsync(app, () => app.OpenWindow(new WindowOptions { Url = "/" }));
        await WaitUntil(() => _platform.Windows.Count == 1);

        var options = OnlyWindow().Options;
        Assert.Equal(new Uri("http://localhost:5173/"), options.Url);
        Assert.Null(options.AssetsFolder);
        Assert.True(options.DevTools);

        app.Quit();
        await run;
    }

    [Fact]
    public async Task AnswersTheCallsOfItsPages()
    {
        var app = App();
        app.Bind(new Greeter());
        var run = RunAsync(app, () => app.OpenWindow());
        await WaitUntil(() => _platform.Windows.Count == 1);
        var window = OnlyWindow();

        Post(app, window, "https://app.oriel/index.html", """{"t":"call","id":1,"k":"p","m":"Greeter.Greet","a":["Ada"]}""");
        Assert.Equal("""{"t":"result","id":1,"k":"p","v":"Hello, Ada"}""", await NextReply(window));

        app.Quit();
        await run;
    }

    /// <summary>A service whose method asks for the window that called it.</summary>
    public sealed class Caller
    {
        public OrielWindow? Last { get; set; }
    }

    private sealed class CallerDispatcher(Caller caller) : ServiceDispatcher("Caller")
    {
        public override ValueTask<ValueWriter?> InvokeAsync(string method, JsonElement args, CallContext context)
        {
            caller.Last = context.Window;
            return ValueTask.FromResult<ValueWriter?>(null);
        }
    }

    [Fact]
    public async Task TellsServicesWhichWindowCalled()
    {
        ServiceRegistry.Register<Caller>(c => new CallerDispatcher(c));
        var app = App();
        var caller = new Caller();
        app.Bind(caller);
        OrielWindow? first = null, second = null;
        var run = RunAsync(app, () =>
        {
            first = app.OpenWindow();
            second = app.OpenWindow();
        });
        await WaitUntil(() => _platform.Windows.Count == 2);
        var windows = _platform.Windows.ToArray();

        Post(app, windows[1], "https://app.oriel/", """{"t":"call","id":1,"k":"p","m":"Caller.Who","a":[]}""");
        await NextReply(windows[1]);
        Assert.Same(second, caller.Last);
        Post(app, windows[0], "https://app.oriel/", """{"t":"call","id":1,"k":"q","m":"Caller.Who","a":[]}""");
        await NextReply(windows[0]);
        Assert.Same(first, caller.Last);

        app.Quit();
        await run;
    }

    [Fact]
    public async Task ReportsMessagesThatBreakTheLimits()
    {
        var app = new OrielApp(new AppOptions { Name = "OrielTests", AssetsDirectory = @"C:\app\wwwroot", MaxMessageLength = 100 }, _platform, null);
        var errors = new List<Exception>();
        app.Error += e =>
        {
            if (e is InvalidDataException)
                errors.Add(e); // not the missing page, the fake has no files
        };
        var run = RunAsync(app, () => app.OpenWindow(new WindowOptions { Title = "Main" }));
        await WaitUntil(() => _platform.Windows.Count == 1);

        Post(app, OnlyWindow(), "https://app.oriel/", new string(' ', 101));
        await WaitUntil(() => errors.Count == 1);
        Assert.Equal("The page of the window \"Main\" sent a message of 101 characters, more than the 100 allowed.", errors[0].Message);

        app.Quit();
        await run;
    }

    [Theory]
    [InlineData(0, 64, 1000)]
    [InlineData(1000, 0, 1000)]
    [InlineData(1000, 64, -1)]
    public void RefusesLimitsThatAreNotPositive(int length, int depth, int calls)
    {
        Assert.Throws<ArgumentException>(() => new OrielApp(
            new AppOptions { Name = "OrielTests", MaxMessageLength = length, MaxMessageDepth = depth, MaxCallsInFlight = calls }, _platform, null));
    }

    [Fact]
    public async Task IgnoresMessagesOfOtherOrigins()
    {
        var app = App();
        app.Bind(new Greeter());
        var run = RunAsync(app, () => app.OpenWindow());
        await WaitUntil(() => _platform.Windows.Count == 1);
        var window = OnlyWindow();

        Post(app, window, "https://evil.example/", """{"t":"call","id":1,"k":"p","m":"Greeter.Greet","a":["Ada"]}""");
        Post(app, window, "https://app.oriel/", """{"t":"call","id":2,"k":"p","m":"Greeter.Greet","a":["Bob"]}""");
        Assert.Equal("""{"t":"result","id":2,"k":"p","v":"Hello, Bob"}""", await NextReply(window));

        app.Quit();
        await run;
    }

    [Fact]
    public async Task SendsEventsToEveryPage()
    {
        var app = App();
        var greeter = new Greeter();
        app.Bind(greeter);
        var run = RunAsync(app, () =>
        {
            app.OpenWindow();
            app.OpenWindow();
        });
        await WaitUntil(() => _platform.Windows.Count == 2);
        var windows = _platform.Windows.ToArray();
        foreach (var w in windows)
            Post(app, w, "https://app.oriel/", """{"t":"hello","k":"p"}""");
        await Task.Delay(50);

        greeter.Greeted.Emit("Ada");
        foreach (var w in windows)
            Assert.Equal("""{"t":"event","n":"Greeter.Greeted","v":"Ada"}""", await NextPosted(w));

        app.Quit();
        await run;
    }

    private sealed class Release(List<string> log) : IAsyncDisposable
    {
        public ValueTask DisposeAsync()
        {
            log.Add("released");
            return ValueTask.CompletedTask;
        }
    }

    [Fact]
    public async Task LeasesAnInstanceForEachCallAndReleasesItAfter()
    {
        var app = App();
        var log = new List<string>();
        var windows = new List<OrielWindow?>();
        app.Bind<Greeter>(window =>
        {
            windows.Add(window);
            log.Add("leased");
            return new ServiceLease<Greeter>(new Greeter(), new Release(log));
        });
        OrielWindow? opened = null;
        var run = RunAsync(app, () => opened = app.OpenWindow());
        await WaitUntil(() => _platform.Windows.Count == 1);
        var native = OnlyWindow();

        Post(app, native, "https://app.oriel/", """{"t":"call","id":1,"k":"p","m":"Greeter.Greet","a":["Ada"]}""");
        Assert.Equal("""{"t":"result","id":1,"k":"p","v":"Hello, Ada"}""", await NextReply(native));
        Post(app, native, "https://app.oriel/", """{"t":"call","id":2,"k":"p","m":"Greeter.Greet","a":["Grace"]}""");
        Assert.Equal("""{"t":"result","id":2,"k":"p","v":"Hello, Grace"}""", await NextReply(native));

        Assert.Equal(["leased", "released", "leased", "released"], log);
        Assert.All(windows, w => Assert.Same(opened, w));
        app.Quit();
        await run;
    }

    [Fact]
    public async Task FailsTheCallsOfALeaseWithoutAnInstance()
    {
        var app = App();
        app.Bind<Greeter>(_ => default);
        var run = RunAsync(app, () => app.OpenWindow());
        await WaitUntil(() => _platform.Windows.Count == 1);

        Post(app, OnlyWindow(), "https://app.oriel/", """{"t":"call","id":1,"k":"p","m":"Greeter.Greet","a":["Ada"]}""");
        Assert.StartsWith("""{"t":"error","id":1""", await NextReply(OnlyWindow()));
        app.Quit();
        await run;
    }

    [Fact]
    public async Task SendsAnEventToOneWindowOnly()
    {
        var app = App();
        var greeter = new Greeter();
        app.Bind(greeter);
        OrielWindow[] opened = [];
        var run = RunAsync(app, () => opened = [app.OpenWindow(), app.OpenWindow()]);
        await WaitUntil(() => _platform.Windows.Count == 2);
        var natives = _platform.Windows.ToArray();
        foreach (var w in natives)
            Post(app, w, "https://app.oriel/", """{"t":"hello","k":"p"}""");
        await Task.Delay(50);

        greeter.Greeted.EmitTo(opened[1], "Grace");
        Assert.Equal("""{"t":"event","n":"Greeter.Greeted","v":"Grace"}""", await NextPosted(natives[1]));
        await Task.Delay(50);
        Assert.Empty(natives[0].Posted);

        // Once the window closed, nothing is sent, and nothing throws.
        natives[1].Close();
        await WaitUntil(() => opened[1].IsClosed);
        greeter.Greeted.EmitTo(opened[1], "late");
        await Task.Delay(50);
        Assert.Empty(natives[1].Posted);

        app.Quit();
        await run;
    }

    [Fact]
    public void EmitsToWindowsOfItsOwnAppOnly()
    {
        var app = App();
        var greeter = new Greeter();
        app.Bind(greeter);
        var other = new OrielApp(new AppOptions { Name = "Other", AssetsDirectory = @"C:\app\wwwroot" }, new FakePlatform(), null);
        var window = other.OpenWindow();

        Assert.Throws<ArgumentException>(() => greeter.Greeted.EmitTo(window, "Ada"));
        new Greeter().Greeted.EmitTo(window, "unbound"); // does nothing, as Emit does
    }

    [Fact]
    public void NumbersWindowsAndFindsThemByName()
    {
        var app = App();
        var first = app.OpenWindow();
        var settings = app.OpenWindow(new WindowOptions { Name = "settings" });

        Assert.Equal((1, 2), (first.Id, settings.Id));
        Assert.Null(first.Name);
        Assert.Equal("settings", settings.Name);
        Assert.Same(settings, app.FindWindow("settings"));
        Assert.Null(app.FindWindow("SETTINGS")); // names compare exactly
        Assert.Null(app.FindWindow("missing"));
    }

    [Fact]
    public async Task KeepsOneWindowOfAName()
    {
        var app = App();
        OrielWindow? settings = null;
        var run = RunAsync(app, () =>
        {
            app.OpenWindow();
            settings = app.OpenWindow(new WindowOptions { Name = "settings" });
        });
        await WaitUntil(() => _platform.Windows.Count == 2);

        var e = Assert.Throws<InvalidOperationException>(() => app.OpenWindow(new WindowOptions { Name = "settings" }));
        Assert.Contains("\"settings\" is open already", e.Message);
        Assert.Equal(2, app.Windows.Count);

        // Closed, the name is free again, under a new id.
        _platform.Windows.ToArray()[1].Close();
        await WaitUntil(() => settings!.IsClosed);
        Assert.Null(app.FindWindow("settings"));
        Assert.Equal(3, app.OpenWindow(new WindowOptions { Name = "settings" }).Id);

        app.Quit();
        await run;
    }

    [Fact]
    public async Task ShowsThePagesTitleUnlessGivenOne()
    {
        var app = App();
        OrielWindow? following = null, fixedTitle = null;
        var run = RunAsync(app, () =>
        {
            following = app.OpenWindow();
            fixedTitle = app.OpenWindow(new WindowOptions { Title = "Fixed" });
        });
        await WaitUntil(() => _platform.Windows.Count == 2);
        var windows = _platform.Windows.ToArray();

        Assert.Equal("OrielTests", following!.Title);
        app.Dispatch(() =>
        {
            windows[0].Events.TitleChanged("Inbox (3)");
            windows[1].Events.TitleChanged("Ignored");
        });
        await WaitUntil(() => following.Title == "Inbox (3)");
        Assert.Equal("Inbox (3)", windows[0].Title);
        Assert.Equal("Fixed", fixedTitle!.Title);

        app.Quit();
        await run;
    }

    [Fact]
    public async Task ExplainsAPageMissingFromTheAppsFilesInsteadOfShowingNothing()
    {
        var assets = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(assets);
        try
        {
            var app = App(assets: assets);
            var errors = new List<Exception>();
            app.Error += errors.Add;
            var run = RunAsync(app, () => app.OpenWindow());
            await WaitUntil(() => _platform.Windows.Count == 1);

            var page = OnlyWindow().Options.ErrorPage;
            Assert.NotNull(page);
            Assert.Contains(System.Net.WebUtility.HtmlEncode(Path.Combine(assets, "index.html")), page);
            var error = Assert.IsType<FileNotFoundException>(Assert.Single(errors));
            Assert.Equal(Path.Combine(assets, "index.html"), error.FileName);

            app.Quit();
            await run;
        }
        finally
        {
            Directory.Delete(assets, recursive: true);
        }
    }

    [Fact]
    public async Task ShowsThePageWhenItIsThere()
    {
        var assets = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(assets);
        File.WriteAllText(Path.Combine(assets, "index.html"), "<!doctype html>");
        try
        {
            var app = App(assets: assets);
            var run = RunAsync(app, () => app.OpenWindow());
            await WaitUntil(() => _platform.Windows.Count == 1);
            Assert.Null(OnlyWindow().Options.ErrorPage);
            app.Quit();
            await run;
        }
        finally
        {
            Directory.Delete(assets, recursive: true);
        }
    }

    /// <summary>Opens a window of an app on a manual clock, and reports its webview failed once per <paramref name="failures"/>.</summary>
    private async Task<(FakeWindow Native, List<WebViewFailedEventArgs> Raised, List<Exception> Errors)> Fail(
        WebViewFailure[] failures, Action<WebViewFailedEventArgs>? handle = null, ManualTime? time = null)
    {
        var app = new OrielApp(new AppOptions { Name = "OrielTests", AssetsDirectory = @"C:\app\wwwroot" }, _platform, null, time ?? new ManualTime());
        var raised = new List<WebViewFailedEventArgs>();
        var errors = new List<Exception>();
        app.Error += errors.Add;
        var run = RunAsync(app, () =>
        {
            var window = app.OpenWindow();
            window.WebViewFailed += (_, e) =>
            {
                handle?.Invoke(e);
                raised.Add(e);
            };
        });
        await WaitUntil(() => _platform.Windows.Count == 1);
        var native = OnlyWindow();
        errors.Clear(); // the missing page of C:\app\wwwroot
        var done = new TaskCompletionSource();
        app.Dispatch(() =>
        {
            foreach (var failure in failures)
                native.Events.WebViewFailed(failure);
            done.SetResult();
        });
        await done.Task;
        app.Quit();
        await run;
        return (native, raised, errors);
    }

    [Fact]
    public async Task ReloadsThePageWhenItsRendererFails()
    {
        var (native, raised, errors) = await Fail([WebViewFailure.RendererExited]);
        Assert.Equal(1, native.Reloads);
        var args = Assert.Single(raised);
        Assert.Equal((WebViewFailure.RendererExited, WebViewRecovery.Reload), (args.Failure, args.Recovery));
        Assert.Contains("failed (RendererExited); recovery: Reload", Assert.Single(errors).Message);
    }

    [Fact]
    public async Task RecreatesTheWebViewWhenTheBrowserFails()
    {
        var (native, _, _) = await Fail([WebViewFailure.BrowserExited]);
        Assert.Equal((0, 1), (native.Reloads, native.Recreations));
    }

    [Fact]
    public async Task ShowsAnErrorPageWhenThePageKeepsFailing()
    {
        var (native, raised, _) = await Fail(Enumerable.Repeat(WebViewFailure.RendererExited, 4).ToArray());
        Assert.Equal(3, native.Reloads);
        Assert.Equal(WebViewRecovery.ErrorPage, raised[^1].Recovery);
        Assert.Contains("This window stopped working", native.ErrorPage);
        Assert.Contains("href=\"https://app.oriel/index.html\"", native.ErrorPage);
    }

    [Fact]
    public async Task LetsTheAppDecideHowToRecover()
    {
        var (native, _, _) = await Fail([WebViewFailure.RendererExited], e => e.Recovery = WebViewRecovery.None);
        Assert.Equal(0, native.Reloads);
    }

    [Fact]
    public async Task RecoversEvenWhenAHandlerThrows()
    {
        var (native, _, errors) = await Fail([WebViewFailure.RendererExited], _ => throw new InvalidOperationException("handler bug"));
        Assert.Equal(1, native.Reloads);
        Assert.Contains(errors, e => e.Message == "handler bug");
    }

    [Fact]
    public async Task DoesNotReportFailuresTheWebViewRecoversFrom()
    {
        var (native, raised, errors) = await Fail([WebViewFailure.GpuExited]);
        Assert.Equal(WebViewRecovery.None, Assert.Single(raised).Recovery);
        Assert.Equal((0, 0), (native.Reloads, native.Recreations));
        Assert.Empty(errors);
    }

    /// <summary>Runs <paramref name="act"/> on the UI thread of an app with one window, and returns what it returned.</summary>
    private async Task<T> OnWindow<T>(Func<FakeWindow, OrielWindow, T> act, WindowOptions? options = null, Action<OrielWindow>? setUp = null)
    {
        var app = App();
        OrielWindow? window = null;
        var run = RunAsync(app, () =>
        {
            window = app.OpenWindow(options);
            setUp?.Invoke(window);
        });
        await WaitUntil(() => _platform.Windows.Count == 1);
        var result = new TaskCompletionSource<T>();
        app.Dispatch(() => result.SetResult(act(OnlyWindow(), window!)));
        var value = await result.Task;
        await Task.Delay(20); // windows opened by act
        app.Quit();
        await run;
        return value;
    }

    [Fact]
    public async Task KeepsTheAppsPagesInTheWindowAndOpensOthersOutside()
    {
        var (own, other) = await OnWindow((native, _) => (
            native.Events.NavigationStarting("https://app.oriel/settings.html", true),
            native.Events.NavigationStarting("https://example.com/docs", true)));

        Assert.Equal(NavigationAction.Allow, own);
        Assert.Equal(NavigationAction.Deny, other); // canceled in the window...
        Assert.Equal([new Uri("https://example.com/docs")], _platform.OpenedExternally); // ...opened outside
    }

    [Fact]
    public async Task LetsTheAppDecideWhereNavigationsGo()
    {
        var raised = new List<NavigationRequestedEventArgs>();
        var action = await OnWindow(
            (native, _) => native.Events.NavigationStarting("https://example.com/", false),
            setUp: window => window.NavigationRequested += (_, e) =>
            {
                raised.Add(e);
                e.Action = NavigationAction.Allow;
            });

        Assert.Equal(NavigationAction.Allow, action);
        var args = Assert.Single(raised);
        Assert.Equal((new Uri("https://example.com/"), false, false), (args.Uri, args.IsNewWindow, args.IsUserInitiated));
        Assert.Empty(_platform.OpenedExternally);
    }

    [Fact]
    public async Task NeverOpensDangerousSchemesOutsideWhateverHandlersSay()
    {
        var action = await OnWindow(
            (native, _) => native.Events.NavigationStarting("file:///C:/Windows/System32/cmd.exe", true),
            setUp: window => window.NavigationRequested += (_, e) => e.Action = NavigationAction.OpenExternally);

        Assert.Equal(NavigationAction.Deny, action);
        Assert.Empty(_platform.OpenedExternally);
    }

    [Fact]
    public async Task RefusesNavigationsWhenAHandlerThrows()
    {
        var action = await OnWindow(
            (native, _) => native.Events.NavigationStarting("https://app.oriel/x.html", true),
            setUp: window => window.NavigationRequested += (_, _) => throw new InvalidOperationException("handler bug"));

        Assert.Equal(NavigationAction.Deny, action);
    }

    [Fact]
    public async Task OpensTheAppsPagesInWindowsOfTheirOwn()
    {
        await OnWindow((native, _) =>
        {
            native.Events.NewWindowRequested("https://app.oriel/detail.html", true);
            native.Events.NewWindowRequested("https://example.com/", true);
            native.Events.NewWindowRequested("about:blank", false);
            return 0;
        }, new WindowOptions { Width = 500, Height = 400 });

        Assert.Equal(2, _platform.Windows.Count);
        var opened = _platform.Windows.ToArray()[1].Options;
        Assert.Equal((new Uri("https://app.oriel/detail.html"), 500, 400), (opened.Url, opened.Width, opened.Height));
        Assert.Equal([new Uri("https://example.com/")], _platform.OpenedExternally);
    }

    [Fact]
    public async Task GrantsTheAppsPagesOnlyThePermissionsGiven()
    {
        var answers = await OnWindow((native, _) => new[]
        {
            native.Events.PermissionRequested("https://app.oriel/index.html", WebPermissions.Camera),
            native.Events.PermissionRequested("https://app.oriel/index.html", WebPermissions.Microphone),
            native.Events.PermissionRequested("https://example.com/", WebPermissions.Camera),
            native.Events.PermissionRequested("https://app.oriel/index.html", WebPermissions.None),
        }, new WindowOptions { Permissions = WebPermissions.Camera | WebPermissions.Notifications });

        Assert.Equal([true, false, false, false], answers);
    }

    [Fact]
    public async Task DeniesEveryPermissionByDefault()
    {
        Assert.False(await OnWindow((native, _) => native.Events.PermissionRequested("https://app.oriel/", WebPermissions.Notifications)));
    }

    [Fact]
    public void RefusesToBindWhatIsNotAService()
    {
        var e = Assert.Throws<InvalidOperationException>(() => App().Bind(new object()));
        Assert.Equal("System.Object cannot be bound: it is not a [Service] class, or its project does not run the Oriel source generator.", e.Message);
    }

    [Theory]
    [InlineData("")]
    [InlineData("a/b")]
    public void RefusesNamesThatCannotNameAFolder(string name)
    {
        Assert.Throws<ArgumentException>(() => new OrielApp(new AppOptions { Name = name }, _platform, null));
    }

    [Fact]
    public void KeepsTheDataOfDevelopmentApart()
    {
        var name = "OrielTests" + Guid.NewGuid().ToString("N");
        var prod = new OrielApp(new AppOptions { Name = name }, _platform, null).DataDirectory;
        var dev = new OrielApp(new AppOptions { Name = name }, _platform, "http://localhost:5173").DataDirectory;
        try
        {
            Assert.EndsWith(name, prod);
            Assert.EndsWith(name + " Dev", dev);
            Assert.True(Directory.Exists(prod));
        }
        finally
        {
            Directory.Delete(prod);
            Directory.Delete(dev);
        }
    }

    private static void Post(OrielApp app, FakeWindow window, string source, string message) =>
        app.Dispatch(() => window.Events.MessageReceived(source, message));

    private static async Task WaitUntil(Func<bool> condition)
    {
        for (var i = 0; i < 400 && !condition(); i++)
            await Task.Delay(5);
        Assert.True(condition());
    }
}
